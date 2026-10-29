#if UNITY_EDITOR
using MalbersAnimations.Controller;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MalbersAnimations
{
    //MWC: Wizard launched from [Project right-click > Malbers Animations/Add Mode Animations] on one or more FBX files.
    //It adds the FBX animation clips as Animator States into a chosen layer/sub-state machine, then opens the Animator Tools on the Modes tab.
    public class AddModeAnimationsWindow : EditorWindow
    {
        [SerializeField] private MAnimal Animal;
        [SerializeField] private ModeID Mode;
        [SerializeField] private List<AnimationClip> clips = new();

        private int selectedLayer;
        private int selectedSSM; //0 = Create New, otherwise index+1 into existing sub-state machines
        private Vector2 scroll;
        private AnimatorController m_LastController; //tracks controller changes to re-default the Layer selection
        private ModeID m_LastModeForSSM; //tracks Mode changes to re-default the Sub-State Machine selection

        private const string MenuPath = "Assets/Malbers Animations/Add Mode Animations";

        [MenuItem(MenuPath, false, 2000)]
        private static void OpenWindow()
        {
            var window = GetWindow<AddModeAnimationsWindow>(true, "Add Mode Animations", true);
            window.minSize = new Vector2(380, 300);
            window.clips = GetSelectedClips();
            window.Show();
        }

        //MWC: Enable the menu only when at least one selected asset is an FBX/model file
        //MWC: Enable the menu when at least one selected asset is an AnimationClip OR an FBX/model file
        [MenuItem(MenuPath, true)]
        private static bool OpenWindowValidate()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is AnimationClip) return true;

                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetImporter.GetAtPath(path) is ModelImporter) return true;
            }
            return false;
        }

        //MWC: Collect every real AnimationClip from the selection - directly selected clips (standalone .anim or FBX
        //sub-asset clips) and all clips inside selected FBX/model files. Skips __preview__/hidden clips and de-duplicates.
        private static List<AnimationClip> GetSelectedClips()
        {
            var result = new List<AnimationClip>();
            var seenClips = new HashSet<AnimationClip>();
            var seenModelPaths = new HashSet<string>();

            void TryAdd(AnimationClip clip)
            {
                if (clip != null &&
                    !clip.name.StartsWith("__preview__") &&
                    (clip.hideFlags & HideFlags.HideInHierarchy) == 0 &&
                    seenClips.Add(clip))
                    result.Add(clip);
            }

            foreach (var obj in Selection.objects)
            {
                //Directly selected AnimationClip (standalone .anim or an FBX sub-asset clip)
                if (obj is AnimationClip clip)
                {
                    TryAdd(clip);
                    continue;
                }

                //Selected FBX/model -> pull all of its clips
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || !seenModelPaths.Add(path)) continue;
                if (AssetImporter.GetAtPath(path) is not ModelImporter) continue;

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is AnimationClip c) TryAdd(c);
            }
            return result;
        }

        //MWC: While the window is open, autofill the Animal field from the GameObject selected in the Hierarchy
        private void OnSelectionChange()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;

            var found = go.GetComponentInParent<MAnimal>();
            if (found == null) found = go.GetComponentInChildren<MAnimal>();

            if (found != null && found != Animal)
            {
                Animal = found;
                Repaint();
            }
        }

        //MWC: Drag and Drop area to add more FBX / AnimationClips to the list
        private void DrawDragAndDropArea()
        {
            var rect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            GUI.Box(rect, "Drag FBX / Animation Clips here to add", EditorStyles.helpBox);

            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    AddClipsFromObjects(DragAndDrop.objectReferences);
                    Repaint();
                }
                evt.Use();
            }
        }

        //MWC: Add AnimationClips from dropped objects (direct clips or FBX model files), de-duplicated
        private void AddClipsFromObjects(Object[] objects)
        {
            foreach (var obj in objects)
            {
                if (obj is AnimationClip clip)
                {
                    AddClip(clip);
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not ModelImporter) continue;

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is AnimationClip c &&
                        !c.name.StartsWith("__preview__") &&
                        (c.hideFlags & HideFlags.HideInHierarchy) == 0)
                        AddClip(c);
            }
        }

        private void AddClip(AnimationClip clip)
        {
            if (clip != null && !clips.Contains(clip)) clips.Add(clip);
        }

        //MWC: True if the layer root already contains a sub-state machine named like the current Mode
        private bool ModeSSMExists(AnimatorStateMachine rootSM) =>
            Mode != null && rootSM.stateMachines.Any(s => s.stateMachine.name == Mode.name);

        private AnimatorController GetController()
        {
            if (Animal == null) return null;
            var anim = Animal.Anim != null ? Animal.Anim : Animal.GetComponentInChildren<Animator>();
            return anim != null ? anim.runtimeAnimatorController as AnimatorController : null;
        }

        private SerializedObject serializedObject;
        private SerializedProperty p_Animal, p_Mode;

        private void OnEnable()
        {
            serializedObject = new SerializedObject(this);

            p_Animal = serializedObject.FindProperty("Animal");
            p_Mode = serializedObject.FindProperty("Mode");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Adds the selected FBX animations as Animator States, then opens the Animator Tools on the Modes tab.", MessageType.Info);

            //Clips found
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Animations from FBX [{clips.Count}]", EditorStyles.boldLabel);

                if (clips.Count == 0)
                {
                    EditorGUILayout.HelpBox("No animation clips found. Drag FBX or AnimationClips below to add some.", MessageType.Warning);
                }
                else
                {
                    using (var s = new GUILayout.ScrollViewScope(scroll, GUILayout.MaxHeight(120)))
                    {
                        scroll = s.scrollPosition;

                        int removeIndex = -1;
                        for (int i = 0; i < clips.Count; i++)
                        {
                            using (new GUILayout.HorizontalScope())
                            {
                                using (new EditorGUI.DisabledGroupScope(true))
                                    EditorGUILayout.ObjectField(clips[i], typeof(AnimationClip), false);

                                //MWC: Remove [x] button per animation
                                if (GUILayout.Button(new GUIContent("x", "Remove this animation"), GUILayout.Width(22)))
                                    removeIndex = i;
                            }
                        }

                        if (removeIndex >= 0) clips.RemoveAt(removeIndex);
                    }
                }

                //MWC: Drag and Drop rect to add more FBX / AnimationClips to the list
                DrawDragAndDropArea();
            }

            //References
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Animal = (MAnimal)EditorGUILayout.ObjectField(
                    new GUIContent("Animal", "GameObject in the scene with an Animator and Animal Controller"), Animal, typeof(MAnimal), true);

                EditorGUILayout.PropertyField(p_Mode);
            }

            var controller = GetController();

            if (Animal != null && controller == null)
                EditorGUILayout.HelpBox("The selected Animal has no Animator Controller assigned.", MessageType.Error);

            //Layer + Sub-State Machine selection (only once an Animal with a controller is set)
            if (controller != null && controller.layers.Length > 0)
            {
                //MWC: When a new controller is chosen, default the Layer selection to the LAST layer and open the Animator window on it
                if (controller != m_LastController)
                {
                    m_LastController = controller;
                    selectedLayer = controller.layers.Length - 1;
                    selectedSSM = 0;
                    m_LastModeForSSM = null; //force the sub-state machine default to re-evaluate for the new controller

                    //Animal filled -> make sure the Animator window is open on it (deferred so we don't change focus mid-OnGUI)
                    var c = controller;
                    int li = selectedLayer;
                    EditorApplication.delayCall += () => EnsureAnimatorWindow(c, li);
                }

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var layerNames = controller.layers.Select(l => l.name).ToArray();

                    //MWC: Pull the selected layer FROM the Animator window (when it shows the same controller) so they stay in sync
                    int animLayer = GetAnimatorWindowLayer(controller);
                    if (animLayer >= 0 && animLayer < layerNames.Length)
                        selectedLayer = animLayer;

                    selectedLayer = Mathf.Clamp(selectedLayer, 0, layerNames.Length - 1);

                    //MWC: Push layer changes made here TO the Animator window (only on actual user interaction, avoids feedback loop)
                    EditorGUI.BeginChangeCheck();
                    selectedLayer = EditorGUILayout.Popup(new GUIContent("Layer", "Animator layer where the animations will be added"), selectedLayer, layerNames);
                    if (EditorGUI.EndChangeCheck())
                        SetAnimatorWindowLayer(controller, selectedLayer);

                    var rootSM = controller.layers[selectedLayer].stateMachine;

                    //MWC: If a sub-state machine already matches the Mode name, don't offer "Create New" (would duplicate it)
                    bool offerCreate = !ModeSSMExists(rootSM);

                    var ssmNames = new List<string>();
                    if (offerCreate) ssmNames.Add($"[Create New: {(Mode != null ? Mode.name : "Mode")}]");
                    ssmNames.AddRange(rootSM.stateMachines.Select(s => s.stateMachine.name));

                    //MWC: When the Mode changes, default the selection to the matching sub-state machine (if any), else Create New
                    if (Mode != m_LastModeForSSM)
                    {
                        m_LastModeForSSM = Mode;
                        int matchIdx = Mode != null ? ssmNames.IndexOf(Mode.name) : -1;
                        selectedSSM = matchIdx >= 0 ? matchIdx : 0;
                    }

                    selectedSSM = Mathf.Clamp(selectedSSM, 0, ssmNames.Count - 1);
                    selectedSSM = EditorGUILayout.Popup(new GUIContent("Sub-State Machine", "Where to add the animations, or create a new one"), selectedSSM, ssmNames.ToArray());
                }
            }

            //Add button
            EditorGUILayout.Space(4);

            bool valid = Animal != null && controller != null && Mode != null && clips.Count > 0;

            using (new EditorGUI.DisabledGroupScope(!valid))
            using (new GUILayout.HorizontalScope())
            {
                //MWC: Create the states, then open the Animator Tools so the user can tweak/run the setup manually
                if (GUILayout.Button(new GUIContent("Set up Mode in Tools",
                    "Create the animation states and open the Animator Tools on the Modes tab"), GUILayout.Height(28)))
                {
                    SetUpModeInTools(controller);
                }

                //MWC: Create the states and run the full Set Everything pipeline automatically, without opening the Animator Tools
                if (GUILayout.Button(new GUIContent("Auto Setup Mode",
                    "Create the states and automatically build all transitions and abilities (like 'Set Everything'), without opening the Animator Tools"), GUILayout.Height(28)))
                {
                    AutoSetupMode(controller);
                }
            }

            if (!valid)
                EditorGUILayout.HelpBox("Assign an Animal (with a controller), a Mode ID, and make sure the FBX have animation clips.", MessageType.None);
        }

        //MWC: Create the FBX animation states inside the resolved target sub-state machine (existing or new), non-overlapping.
        private List<AnimatorState> CreateModeStates(AnimatorController controller, out AnimatorStateMachine targetSM)
        {
            var rootSM = controller.layers[selectedLayer].stateMachine;

            //Resolve the target sub-state machine, mirroring the dropdown layout (Create New only offered when no name match)
            bool offerCreate = !ModeSSMExists(rootSM);

            if (offerCreate && selectedSSM == 0)
            {
                targetSM = rootSM.AddStateMachine(Mode.name, NewStateMachinePosition(rootSM));
            }
            else
            {
                int existingIndex = offerCreate ? selectedSSM - 1 : selectedSSM;
                existingIndex = Mathf.Clamp(existingIndex, 0, rootSM.stateMachines.Length - 1);
                targetSM = rootSM.stateMachines[existingIndex].stateMachine;
            }

            //Stack the new states below any existing ones so they don't overlap
            Vector3 pos = StackStartPosition(targetSM);

            var newStates = new List<AnimatorState>();
            for (int i = 0; i < clips.Count; i++)
            {
                var state = targetSM.AddState(clips[i].name, pos + new Vector3(0, i * 60f, 0));
                state.motion = clips[i];
                newStates.Add(state);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return newStates;
        }

        //MWC: Create the states and open the Animator Tools on the Modes tab for manual setup
        private void SetUpModeInTools(AnimatorController controller)
        {
            var newStates = CreateModeStates(controller, out var targetSM);

            //Open the Animator window on the right controller / layer / sub-state machine so the new states are visible
            FocusAnimatorWindow(controller, selectedLayer, targetSM);

            //Open the Animator Tools already filled in on the Modes tab
            MalbersAnimatorTools.OpenForModeSetup(Animal, controller, Mode, newStates.ToArray());

            Close();
        }

        //MWC: Create the states and run the full Mode setup automatically (transitions + abilities), without the Animator Tools window
        private void AutoSetupMode(AnimatorController controller)
        {
            var newStates = CreateModeStates(controller, out var targetSM);

            MalbersAnimatorTools.AutoSetupMode(Animal, controller, Mode, newStates.ToArray());
            AssetDatabase.SaveAssets();

            //Open the Animator window so the fully wired sub-state machine is visible
            FocusAnimatorWindow(controller, selectedLayer, targetSM);

            Close();
        }

        // ---- Animator window (AnimatorControllerTool) reflection access ----

        private static System.Type s_ToolType;
        private static System.Type ToolType => s_ToolType ??=
            System.Type.GetType("UnityEditor.Graphs.AnimatorControllerTool, UnityEditor.Graphs");

        private const BindingFlags RF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        //MWC: Repaint at the editor's low-frequency tick so layer changes made in the Animator window reflect here live
        private void OnInspectorUpdate() => Repaint();

        //MWC: Returns the Animator window. open=true creates/focuses it; open=false returns an existing instance or null.
        private static EditorWindow GetAnimatorWindow(bool open)
        {
            if (ToolType == null) return null;
            if (open) return EditorWindow.GetWindow(ToolType);

            var found = Resources.FindObjectsOfTypeAll(ToolType);
            return found.Length > 0 ? found[0] as EditorWindow : null;
        }

        private static AnimatorController GetAnimatorWindowController(EditorWindow w)
        {
            if (w == null || ToolType == null) return null;
            try
            {
                var p = ToolType.GetProperty("animatorController", RF);
                if (p != null) return p.GetValue(w) as AnimatorController;
                return ToolType.GetField("animatorController", RF)?.GetValue(w) as AnimatorController;
            }
            catch { return null; }
        }

        //MWC: Ensure the Animator window is open on this controller + layer (used when the Animal is first set)
        private static void EnsureAnimatorWindow(AnimatorController controller, int layerIndex)
        {
            if (ToolType == null || controller == null) return;
            try
            {
                var w = EditorWindow.GetWindow(ToolType); //open / focus the Animator window
                SetMember(ToolType, w, "animatorController", controller, RF);
                SetMember(ToolType, w, "selectedLayerIndex", layerIndex, RF);
                w.Repaint();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Add Mode Animations] Could not open the Animator window: {e.Message}");
            }
        }

        //MWC: Animator window's selected layer, but only when it shows the given controller (so the indices are comparable). -1 otherwise.
        private static int GetAnimatorWindowLayer(AnimatorController controller)
        {
            var w = GetAnimatorWindow(false);
            if (w == null || GetAnimatorWindowController(w) != controller) return -1;
            try
            {
                var p = ToolType.GetProperty("selectedLayerIndex", RF);
                return p != null ? (int)p.GetValue(w) : -1;
            }
            catch { return -1; }
        }

        //MWC: Push a layer selection to the Animator window (only when it shows the given controller)
        private static void SetAnimatorWindowLayer(AnimatorController controller, int layerIndex)
        {
            var w = GetAnimatorWindow(false);
            if (w == null || GetAnimatorWindowController(w) != controller) return;
            try
            {
                ToolType.GetProperty("selectedLayerIndex", RF)?.SetValue(w, layerIndex);
                w.Repaint();
            }
            catch { /* internal API differs */ }
        }

        //MWC: Best-effort focus of Unity's (internal) Animator window onto the controller, layer and target sub-state machine.
        //Uses reflection because the Animator window (AnimatorControllerTool) exposes no public API. Degrades gracefully.
        private static void FocusAnimatorWindow(AnimatorController controller, int layerIndex, AnimatorStateMachine targetSM)
        {
            try
            {
                var toolType = System.Type.GetType("UnityEditor.Graphs.AnimatorControllerTool, UnityEditor.Graphs");
                if (toolType == null) return;

                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                var window = EditorWindow.GetWindow(toolType); //open / focus the Animator window
                if (window == null) return;

                //Show the Animal's controller and select the correct layer
                SetMember(toolType, window, "animatorController", controller, F);
                SetMember(toolType, window, "selectedLayerIndex", layerIndex, F);

                //Drill into the target sub-state machine via the internal graph view
                if (targetSM != null)
                    NavigateToSubStateMachine(window, toolType, controller, layerIndex, targetSM, F);

                window.Repaint();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Add Mode Animations] Could not auto-navigate the Animator window (internal API may have changed): {e.Message}");
            }
        }

        //MWC: Navigate the Animator window INTO a sub-state machine (like double-clicking it), using the actual
        //AnimatorControllerTool members: stateMachineGraph (the displayed Graph), BuildBreadCrumbsFromSMHierarchy and RebuildGraph(bool).
        private static void NavigateToSubStateMachine(EditorWindow window, System.Type toolType, AnimatorController controller, int layerIndex, AnimatorStateMachine targetSM, BindingFlags F)
        {
            if (layerIndex < 0 || layerIndex >= controller.layers.Length) return;

            var rootSM = controller.layers[layerIndex].stateMachine;

            //Path from the layer root down to the target sub-state machine (root .. target)
            var hierarchy = new List<AnimatorStateMachine>();
            if (!BuildStateMachinePath(rootSM, targetSM, hierarchy)) return;

            //Build the breadcrumb path so the bar shows root > ... > target and the graph displays the target's contents
            var build = toolType.GetMethod("BuildBreadCrumbsFromSMHierarchy", F);
            if (build != null)
                try { build.Invoke(window, new object[] { hierarchy }); } catch { }

            //Point the displayed graph at the target sub-state machine
            var graph = toolType.GetField("stateMachineGraph", F)?.GetValue(window);
            if (graph != null)
            {
                var gt = graph.GetType();
                TrySet(gt, graph, "rootStateMachine", rootSM, F);
                TrySet(gt, graph, "parentStateMachine", hierarchy.Count >= 2 ? hierarchy[^2] : rootSM, F);
                TrySet(gt, graph, "activeStateMachine", targetSM, F);
            }

            //Rebuild the graph so the view actually enters the sub-state machine (RebuildGraph takes a bool)
            var rebuild = toolType.GetMethod("RebuildGraph", F);
            if (rebuild != null)
                try { rebuild.Invoke(window, new object[] { true }); } catch { }
        }

        //MWC: Depth-first path from 'current' down to 'target', inclusive. Returns false if target is not under current.
        private static bool BuildStateMachinePath(AnimatorStateMachine current, AnimatorStateMachine target, List<AnimatorStateMachine> path)
        {
            path.Add(current);
            if (current == target) return true;

            foreach (var child in current.stateMachines)
                if (BuildStateMachinePath(child.stateMachine, target, path)) return true;

            path.RemoveAt(path.Count - 1);
            return false;
        }

        //MWC: Set a property or field by name if present, swallowing type/access mismatches (members vary across Unity versions)
        private static void TrySet(System.Type type, object target, string name, object value, BindingFlags flags)
        {
            try
            {
                var p = type.GetProperty(name, flags);
                if (p != null && p.CanWrite) { p.SetValue(target, value); return; }

                type.GetField(name, flags)?.SetValue(target, value);
            }
            catch { /* member missing or different type on this Unity version */ }
        }

        private static void SetMember(System.Type type, object target, string name, object value, BindingFlags flags)
        {
            var p = type.GetProperty(name, flags);
            if (p != null && p.CanWrite) { p.SetValue(target, value); return; }

            type.GetField(name, flags)?.SetValue(target, value);
        }

        //MWC: Find a position for a brand new sub-state machine node that doesn't overlap ANY existing node in the
        //layer root: states, other sub-state machines, and the Entry / Exit / Any State nodes.
        private static Vector3 NewStateMachinePosition(AnimatorStateMachine rootSM)
        {
            //Approximate Animator node footprint (used for the overlap test)
            const float W = 240f;
            const float H = 60f;

            var occupied = new List<Vector3>();
            foreach (var s in rootSM.states) occupied.Add(s.position);
            foreach (var sm in rootSM.stateMachines) occupied.Add(sm.position);
            occupied.Add(rootSM.entryPosition);
            occupied.Add(rootSM.exitPosition);
            occupied.Add(rootSM.anyStatePosition);

            bool Overlaps(Vector3 p) =>
                occupied.Any(o => Mathf.Abs(o.x - p.x) < W && Mathf.Abs(o.y - p.y) < H);

            //Scan a 2D grid (rows top->bottom, columns left->right) starting from the top-left of the existing
            //cluster, and return the first free cell so the new node fills gaps instead of only stacking down.
            Vector3 origin = new(0, 0, 0);
            if (occupied.Count > 0)
                origin = new Vector3(occupied.Min(o => o.x), occupied.Min(o => o.y), 0);

            const int maxRows = 200;
            const int maxCols = 20;

            for (int row = 0; row < maxRows; row++)
            {
                for (int col = 0; col < maxCols; col++)
                {
                    var candidate = new Vector3(origin.x + col * W, origin.y + row * H, 0);
                    if (!Overlaps(candidate)) return candidate;
                }
            }

            //Fallback: just below everything
            float maxY = occupied.Count > 0 ? occupied.Max(o => o.y) : 0;
            return new Vector3(origin.x, maxY + H, 0);
        }

        //MWC: First position to place the new states, below any states already present in the target sub-state machine
        private static Vector3 StackStartPosition(AnimatorStateMachine targetSM)
        {
            if (targetSM.states.Length == 0) return new Vector3(300, 60, 0);

            float maxY = targetSM.states.Max(s => s.position.y);
            return new Vector3(300, maxY + 60, 0);
        }
    }
}
#endif
