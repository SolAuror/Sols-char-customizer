// Last Change: 9/17/2026 5:33pm
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    /// <summary> Drives a Target transform's local pose by blending between Presets, keyed to the rotation of a Source transform.
    /// E.g. a shoulder armor plate whose position/rotation reacts to the shoulder bone rotation. </summary>
    //MWC: New component - weighted (inverse angular distance) preset blending driven by a source bone rotation
    [ExecuteAlways] //MWC: allows the Live Preview mode to blend the Target while editing
    [AddComponentMenu("Malbers/Utilities/Transform/Transform Reactor")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/utilities/transform-reactor")]
    public class TransformReactor : MonoBehaviour
    {
        /// <summary> Angular distance (degrees) under which a preset is considered an exact match </summary>
        private const float Epsilon = 0.01f; //MWC: exact match guard to avoid division by zero on the weight formula

        [System.Serializable]
        public class Preset
        {
            [Tooltip("Enable/Disable the preset. Disabled presets are excluded from the blend")]
            public bool active = true;

            [Tooltip("Name of the preset (Editor only)")]
            public string name = "Preset";

            [Tooltip("Source local rotation (Euler) this preset reacts to")]
            public Vector3 SourceRotation;

            [Tooltip("Target local position applied when this preset has full weight")]
            public Vector3 Position;

            [Tooltip("Target local rotation (Euler) applied when this preset has full weight")]
            public Vector3 Rotation;
        }

        [RequiredField, Tooltip("Transform whose local rotation is read (e.g. the Shoulder bone)")]
        public Transform Source;

        [RequiredField, Tooltip("Transform driven by the blended presets (e.g. the Armor Plate)")]
        public Transform Target;

        [Tooltip("Reference point marking the tip of the Source direction (e.g. an empty GameObject child of the Source, placed at the end of the bone). " +
            "Each preset draws a ray from the Source origin to this point posed with the preset rotation")]
        public Transform SourceEnd; //MWC: gizmo end point reference to represent the presets on the scene

        [Range(0, 1), Tooltip("Master blend of the whole effect. 0 = disabled, 1 = full effect")]
        public float Weight = 1f;

        [Min(0.1f), Tooltip("Falloff power of the weight calculation. Higher values snap faster to the nearest preset")]
        public float Sharpness = 2f;

        [Min(0), Tooltip("Presets farther than this angle (degrees) from the Source rotation get zero weight. 0 = unlimited")]
        public float MaxAngle = 0f;

        [Tooltip("Restore the Target's original local pose when this component is disabled")]
        public bool RestoreOnDisable = true;

        [Tooltip("Blend the Target in Edit mode too: rotate the Source in the scene to see how the presets blend in real time")]
        public bool LivePreview = false; //MWC: edit-mode preview toggle (in Play mode the reactor always runs)

        [Tooltip("Draw preset gizmos on the scene view when the component is selected")]
        public bool debug = true;

        [Min(0.01f), Tooltip("Size of the preset gizmo markers")]
        public float DebugSize = 0.05f;

        public List<Preset> Presets = new();

        //MWC: index of the preset selected on the inspector Reorderable List.
        //Serialized (like MAnimal.SelectedMode) so the selection is retained when reselecting the component
        [HideInInspector, SerializeField] public int SelectedPreset = -1;

        //MWC: which transform shows gizmos/handles while Live Preview is on. 0 = Source, 1 = Target
        [System.NonSerialized] public int SceneTab = 1;

        private float[] weights; //MWC: normalized weight per preset, recalculated every frame
        private Vector3 defaultPos;
        private Quaternion defaultRot;

        /// <summary> Current normalized weight per preset (same order as the Presets list) </summary>
        public float[] CurrentWeights => weights;

        private void OnEnable()
        {
            //MWC: cache the Target original local pose to restore it on disable
            if (Target != null) Target.GetLocalPositionAndRotation(out defaultPos, out defaultRot);
        }

        private void OnDisable()
        {
            if (RestoreOnDisable && Target != null)
                Target.SetLocalPositionAndRotation(defaultPos, defaultRot); //MWC: restore original pose
        }

        private void LateUpdate()
        {
            //MWC: in Edit mode only blend when Live Preview is enabled, so the reactor never fights manual scene edits
            if (!Application.isPlaying && !LivePreview) return;

            Apply(); //evaluate after the animation has posed the bones
        }

        /// <summary> Calculates the preset weights and applies the blended pose to the Target </summary>
        public void Apply()
        {
            if (Source == null || Target == null || Weight <= 0 || Presets.Count == 0) return; //MWC: early out

            ComputeWeights();

            var pos = Vector3.zero;
            var rot = Quaternion.identity;
            float accum = 0f;

            //MWC: weighted sum for position + cumulative slerp average for rotation (like bone skinning weights)
            for (int i = 0; i < Presets.Count; i++)
            {
                var w = weights[i];
                if (w <= 0) continue;

                pos += Presets[i].Position * w;

                var presetRot = Quaternion.Euler(Presets[i].Rotation);
                accum += w;
                rot = accum == w ? presetRot : Quaternion.Slerp(rot, presetRot, w / accum); //first preset initializes
            }

            if (accum <= 0) return;

            //MWC: master Weight blends between the pose the animation left and the blended preset pose
            Target.GetLocalPositionAndRotation(out var curPos, out var curRot);
            Target.SetLocalPositionAndRotation(
                Vector3.Lerp(curPos, pos, Weight),
                Quaternion.Slerp(curRot, rot, Weight));
        }

        /// <summary> Calculates the normalized weight of each preset from the angular distance
        /// between the Source local rotation and each preset's stored rotation. Safe to call in Edit mode </summary>
        public float[] ComputeWeights()
        {
            var count = Presets.Count;
            if (weights == null || weights.Length != count) weights = new float[count]; //MWC: resize when presets change

            if (Source == null || count == 0) return weights;

            var sourceRot = Source.localRotation;

            //MWC: angular distance to every preset, track the nearest one
            float minDist = float.MaxValue;
            int nearest = -1;
            float sum = 0f;

            for (int i = 0; i < count; i++)
            {
                //MWC: disabled presets are excluded from the blend
                if (!Presets[i].active) { weights[i] = 0f; continue; }

                var dist = Quaternion.Angle(sourceRot, Quaternion.Euler(Presets[i].SourceRotation));

                if (dist < minDist) { minDist = dist; nearest = i; }

                //MWC: raw inverse-distance weight. MaxAngle cutoff excludes far presets
                weights[i] = (MaxAngle > 0 && dist > MaxAngle) ? 0f : 1f / Mathf.Pow(dist, Sharpness);
                sum += weights[i];
            }

            if (nearest == -1) return weights; //MWC: no active presets -> all weights are zero

            //MWC: exact match or everything cut off -> nearest preset takes full weight (no pop, no div-by-zero)
            if (minDist < Epsilon || sum <= 0 || float.IsInfinity(sum))
            {
                for (int i = 0; i < count; i++) weights[i] = 0f;
                weights[nearest] = 1f;
                return weights;
            }

            for (int i = 0; i < count; i++) weights[i] /= sum; //MWC: normalize so weights sum to 1

            return weights;
        }

        /// <summary> Records the Source current local rotation and the Target current local pose into the preset </summary>
        public void CapturePreset(int index)
        {
            if (Source == null || Target == null || index < 0 || index >= Presets.Count) return;

            var preset = Presets[index];
            preset.SourceRotation = Source.localEulerAngles;

            Target.GetLocalPositionAndRotation(out var pos, out var rot);
            preset.Position = pos;
            preset.Rotation = rot.eulerAngles;

            MTools.SetDirty(this);
        }

        [ContextMenu("Add Preset From Current Pose")]
        public void AddPresetFromCurrentPose()
        {
            Presets.Add(new Preset { name = $"Preset {Presets.Count}" });
            CapturePreset(Presets.Count - 1);
        }

        /// <summary> Snaps the Target to the preset stored pose and the Source to the preset reference rotation (Editor preview) </summary>
        public void ApplyPresetPose(int index)
        {
            if (Target == null || index < 0 || index >= Presets.Count) return;

            var preset = Presets[index];
            Target.SetLocalPositionAndRotation(preset.Position, Quaternion.Euler(preset.Rotation));

            //MWC: also pose the Source bone so the full preset scenario is previewed
            if (Source != null) Source.localRotation = Quaternion.Euler(preset.SourceRotation);
        }

#if UNITY_EDITOR
        private static GUIStyle labelStyle; //MWC: boxed bold style so scene labels are readable on any background

        /// <summary> Draws a scene label offset above the position (camera relative) so it does not hide behind the gizmo spheres.
        /// Handles.Label renders on top of the geometry, so it is always visible </summary>
        public static void DrawSceneLabel(Vector3 position, string text, float offset)
        {
            labelStyle ??= new GUIStyle()
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black }
            };

            var cam = Camera.current;
            var up = cam != null ? cam.transform.up : Vector3.up;

            UnityEditor.Handles.Label(position + up * offset, text, labelStyle);
        }
#endif

#if UNITY_EDITOR && MALBERS_DEBUG
        //MWC: preset representation drawn ALL the time (not only when selected):
        //ray from the Source origin to the [Source End] posed with each preset rotation,
        //sphere + XYZ axes + preset name label on the end, gray to green by current weight
        private void OnDrawGizmos()
        {
            if (!debug || Source == null || Presets.Count == 0) return;

            ComputeWeights(); //works in edit mode, colors react while rotating the Source

            var srcParentRot = Source.parent != null ? Source.parent.rotation : Quaternion.identity;

            //[Source End] offset in Source local space (fallback to a forward ray when not assigned)
            var localOffset = SourceEnd != null
                ? Vector3.Scale(Source.InverseTransformPoint(SourceEnd.position), Source.lossyScale)
                : Vector3.forward * DebugSize * 4;

            for (int i = 0; i < Presets.Count; i++)
            {
                var w = (weights != null && i < weights.Length) ? weights[i] : 0f;

                var presetRot = srcParentRot * Quaternion.Euler(Presets[i].SourceRotation);
                var end = Source.position + presetRot * localOffset;

                //MWC: disabled presets draw dimmed
                var isActive = Presets[i].active;
                var dim = isActive ? 1f : 0.25f;

                Gizmos.color = isActive ? Color.Lerp(Color.gray, Color.green, w) : new Color(0.3f, 0.3f, 0.3f, 0.35f);
                Gizmos.DrawLine(Source.position, end);
                Gizmos.DrawSphere(end, DebugSize * 0.6f);

                //MWC: XYZ axes of the preset source rotation on the sphere
                Gizmos.color = Color.red * dim;
                Gizmos.DrawRay(end, presetRot * Vector3.right * DebugSize * 1.5f);
                Gizmos.color = Color.green * dim;
                Gizmos.DrawRay(end, presetRot * Vector3.up * DebugSize * 1.5f);
                Gizmos.color = Color.blue * dim;
                Gizmos.DrawRay(end, presetRot * Vector3.forward * DebugSize * 1.5f);

                DrawSceneLabel(end, Presets[i].name, DebugSize * 2f); //MWC: offset label, never hidden by the sphere
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!debug || Target == null || Presets.Count == 0) return;
            if (LivePreview && SceneTab == 0) return; //MWC: [Source] tab hides the target pose marker
            if (!UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)) return;

            ComputeWeights();

            //MWC: target pose marker only for the preset selected on the Reorderable List
            if (SelectedPreset >= 0 && SelectedPreset < Presets.Count)
            {
                var parent = Target.parent;
                var parentRot = parent != null ? parent.rotation : Quaternion.identity;

                var preset = Presets[SelectedPreset];
                var worldPos = parent != null ? parent.TransformPoint(preset.Position) : preset.Position;
                var worldRot = parentRot * Quaternion.Euler(preset.Rotation);

                var w = (weights != null && SelectedPreset < weights.Length) ? weights[SelectedPreset] : 0f;

                //marker sphere + local axes of the preset target pose
                Gizmos.color = Color.Lerp(Color.gray, Color.green, w);
                Gizmos.DrawWireSphere(worldPos, DebugSize);

                Gizmos.color = Color.red;
                Gizmos.DrawRay(worldPos, worldRot * Vector3.right * DebugSize * 2);
                Gizmos.color = Color.green;
                Gizmos.DrawRay(worldPos, worldRot * Vector3.up * DebugSize * 2);
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(worldPos, worldRot * Vector3.forward * DebugSize * 2);
            }
        }
#endif
    }

#if UNITY_EDITOR
    //MWC: custom inspector - Reorderable List of presets, capture/preview buttons, live weight bars and scene handles
    [CustomEditor(typeof(TransformReactor))]
    public class TransformReactorEditor : Editor
    {
        private SerializedProperty Source, Target, SourceEnd, Weight, Sharpness, MaxAngle, RestoreOnDisable, LivePreview, Presets, debug, DebugSize;
        private TransformReactor M;
        private UnityEditorInternal.ReorderableList presetList;
        private float[] liveWeights; //MWC: weights computed once per GUI pass for the progress bars
        private GUIContent captureIcon, previewIcon; //MWC: row button icons [Record] [Eye]
        private GUIStyle iconButtonStyle; //MWC: button style with minimal padding so the icons draw big
        private Quaternion dragStartRot = Quaternion.identity; //MWC: rotation captured at drag start for Global pivot handles
        //MWC: accumulated Global handle rotation. Fed back to RotationHandle each frame: the axis discs anchor to the
        //drag-start value, but the center free-rotate sphere applies per-event increments to the passed rotation
        private Quaternion globalHandleRot = Quaternion.identity;
        private int pendingCapture = -1, pendingPreview = -1; //MWC: row actions deferred until after the list draws

        private void OnEnable()
        {
            M = (TransformReactor)target;

            Source = serializedObject.FindProperty(nameof(TransformReactor.Source));
            Target = serializedObject.FindProperty(nameof(TransformReactor.Target));
            SourceEnd = serializedObject.FindProperty(nameof(TransformReactor.SourceEnd));
            Weight = serializedObject.FindProperty(nameof(TransformReactor.Weight));
            Sharpness = serializedObject.FindProperty(nameof(TransformReactor.Sharpness));
            MaxAngle = serializedObject.FindProperty(nameof(TransformReactor.MaxAngle));
            RestoreOnDisable = serializedObject.FindProperty(nameof(TransformReactor.RestoreOnDisable));
            LivePreview = serializedObject.FindProperty(nameof(TransformReactor.LivePreview));
            Presets = serializedObject.FindProperty(nameof(TransformReactor.Presets));
            debug = serializedObject.FindProperty(nameof(TransformReactor.debug));
            DebugSize = serializedObject.FindProperty(nameof(TransformReactor.DebugSize));

            //MWC: built-in editor icons (d_ variants for the dark skin)
            var skin = EditorGUIUtility.isProSkin ? "d_" : "";
            captureIcon = new GUIContent(EditorGUIUtility.IconContent(skin + "Animation.Record").image,
                "Capture: record the Source current rotation and the Target current local pose into this preset");
            previewIcon = new GUIContent(EditorGUIUtility.IconContent(skin + "animationvisibilitytoggleon").image,
                "Preview: snap the Target to this preset pose and the Source to the preset rotation (Undo supported)");

            //MWC: Reorderable List. The selected row is the only preset that draws gizmos and scene handles
            presetList = new UnityEditorInternal.ReorderableList(serializedObject, Presets, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Presets", EditorStyles.boldLabel),
                drawElementCallback = DrawPresetElement,
                onAddCallback = OnAddPreset,
                onRemoveCallback = OnRemovePreset,
                onSelectCallback = list =>
                {
                    //MWC: serialized selection (MAnimal.SelectedMode pattern) so it's retained on reselection
                    M.SelectedPreset = list.index;
                    EditorUtility.SetDirty(M);
                    SceneView.RepaintAll();
                },
                elementHeight = EditorGUIUtility.singleLineHeight + 10 //MWC: taller rows so the icon buttons are readable
            };
            presetList.index = M.SelectedPreset;
        }

        //MWC: animate the weight bars in play mode and while Live Preview is blending in edit mode
        public override bool RequiresConstantRepaint() => Application.isPlaying || (M != null && M.LivePreview);

        //MWC: single-line row -> name field + live weight bar + Capture [Record] and Preview [Eye] icon buttons
        private void DrawPresetElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            //MWC: minimal padding so the icon fills the whole button (lazy init, editor skin must be ready)
            iconButtonStyle ??= new GUIStyle(EditorStyles.miniButton) { padding = new RectOffset(1, 1, 1, 1) };

            //MWC: highlight the selected preset (same pattern as MAnimalEditor Draw_Element_Modes)
            var bgColor = GUI.backgroundColor;
            if (isActive) GUI.backgroundColor = MTools.MBlue * 3;

            var element = Presets.GetArrayElementAtIndex(index);
            var nameProp = element.FindPropertyRelative("name");
            var activeProp = element.FindPropertyRelative(nameof(TransformReactor.Preset.active));

            const float barWidth = 70f;
            const float toggleWidth = 18f; //MWC: active toggle at the start of the row (no label)
            var btnSize = rect.height - 4; //MWC: square icon buttons using the full row height

            var lineY = rect.y + (rect.height - EditorGUIUtility.singleLineHeight) * 0.5f;
            var toggleRect = new Rect(rect.x, lineY, toggleWidth, EditorGUIUtility.singleLineHeight);
            var nameRect = new Rect(toggleRect.xMax + 2, lineY, rect.width - toggleWidth - barWidth - (btnSize * 2) - 14, EditorGUIUtility.singleLineHeight);
            var barRect = new Rect(nameRect.xMax + 4, lineY, barWidth, EditorGUIUtility.singleLineHeight);
            var captureRect = new Rect(barRect.xMax + 4, rect.y + 2, btnSize, btnSize);
            var previewRect = new Rect(captureRect.xMax + 2, rect.y + 2, btnSize, btnSize);

            activeProp.boolValue = EditorGUI.Toggle(toggleRect, GUIContent.none, activeProp.boolValue);

            //MWC: dim the row content when the preset is disabled
            using (new EditorGUI.DisabledGroupScope(!activeProp.boolValue))
                nameProp.stringValue = EditorGUI.TextField(nameRect, nameProp.stringValue);

            if (liveWeights != null && index < liveWeights.Length)
                EditorGUI.ProgressBar(barRect, liveWeights[index], $"{liveWeights[index]:P0}");

            //MWC: defer the actions until after the list draws to avoid mid-GUI serialized changes
            using (new EditorGUI.DisabledGroupScope(M.Source == null || M.Target == null))
                if (GUI.Button(captureRect, captureIcon, iconButtonStyle)) pendingCapture = index;

            using (new EditorGUI.DisabledGroupScope(M.Target == null))
                if (GUI.Button(previewRect, previewIcon, iconButtonStyle)) pendingPreview = index;

            GUI.backgroundColor = bgColor; //MWC: restore after the highlight
        }

        //MWC: the [+] button captures the current Source/Target pose into the new preset
        private void OnAddPreset(UnityEditorInternal.ReorderableList list)
        {
            Undo.RecordObject(M, "Add Reactor Preset");

            if (M.Source != null && M.Target != null)
                M.AddPresetFromCurrentPose();
            else
                M.Presets.Add(new TransformReactor.Preset { name = $"Preset {M.Presets.Count}" });

            EditorUtility.SetDirty(M);
            serializedObject.Update(); //refresh after direct list modification

            list.index = M.Presets.Count - 1; //select the new preset
            M.SelectedPreset = list.index;
            SceneView.RepaintAll();
        }

        private void OnRemovePreset(UnityEditorInternal.ReorderableList list)
        {
            Presets.DeleteArrayElementAtIndex(list.index);
            serializedObject.ApplyModifiedProperties();

            M.SelectedPreset = Mathf.Min(list.index, M.Presets.Count - 1); //MWC: keep a valid selection
            list.index = M.SelectedPreset;
            SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Blends the Target's local pose between Presets based on the Source's rotation.\n" +
                "Select a Preset on the list to see its Gizmos and Scene Handles");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(Source);
                EditorGUILayout.PropertyField(Target);
                EditorGUILayout.PropertyField(SourceEnd);
                EditorGUILayout.PropertyField(Weight);
                EditorGUILayout.PropertyField(Sharpness);
                EditorGUILayout.PropertyField(MaxAngle);
                EditorGUILayout.PropertyField(RestoreOnDisable);

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(debug);
                    if (debug.boolValue) EditorGUILayout.PropertyField(DebugSize, GUIContent.none, GUILayout.Width(60));
                }

                //MWC: Live Preview toggle (edit mode only, highlighted green while active)
                if (!Application.isPlaying)
                {
                    var guicolor = GUI.color;
                    if (LivePreview.boolValue) GUI.color = Color.green;

                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        LivePreview.boolValue = GUILayout.Toggle(LivePreview.boolValue,
                            new GUIContent("Live Preview", "Rotate the Source in the scene to see the Target blend between presets in real time"),
                            EditorStyles.miniButton);

                        if (cc.changed) SceneView.RepaintAll(); //apply/stop the preview immediately
                    }
                    GUI.color = guicolor;

                    //MWC: Source|Target tabs -> which transform shows gizmos/handles on the scene while previewing
                    if (LivePreview.boolValue)
                    {
                        var newTab = GUILayout.Toolbar(M.SceneTab,
                            new[] {
                                new GUIContent("Source", "Show a rotation handle on the Source bone. Rotate it to see the Target blend in real time"),
                                new GUIContent("Target", "Show the selected preset pose handles to edit its values in the scene")
                            });

                        if (newTab != M.SceneTab)
                        {
                            M.SceneTab = newTab;
                            SceneView.RepaintAll();
                        }
                    }
                }
            }

            //MWC: live weights for the progress bars (edit and play mode)
            liveWeights = (M.Source != null && M.Presets.Count > 0) ? M.ComputeWeights() : null;

            presetList.DoLayoutList();

            //MWC: row icon-button actions, executed after the list GUI
            if (pendingCapture >= 0)
            {
                Undo.RecordObject(M, "Capture Reactor Preset");
                M.CapturePreset(pendingCapture);
                EditorUtility.SetDirty(M);
                serializedObject.Update();
                pendingCapture = -1;
            }

            if (pendingPreview >= 0)
            {
                if (M.Target != null)
                {
                    //record both transforms so Undo restores the full previewed pose
                    if (M.Source != null) Undo.RecordObjects(new Object[] { M.Target, M.Source }, "Preview Reactor Preset");
                    else Undo.RecordObject(M.Target, "Preview Reactor Preset");

                    M.ApplyPresetPose(pendingPreview);

                    EditorUtility.SetDirty(M.Target);
                    if (M.Source != null) EditorUtility.SetDirty(M.Source);
                }
                pendingPreview = -1;
            }

            //MWC: details of the selected preset below the list
            var sel = presetList.index;
            if (sel >= 0 && sel < Presets.arraySize)
            {
                var element = Presets.GetArrayElementAtIndex(sel);
                var nameProp = element.FindPropertyRelative("name");

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"[{nameProp.stringValue}]", EditorStyles.boldLabel);

                    EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(TransformReactor.Preset.SourceRotation)), new GUIContent("Source Rotation"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(TransformReactor.Preset.Position)), new GUIContent("Target Position"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(TransformReactor.Preset.Rotation)), new GUIContent("Target Rotation"));
                }
            }

            //MWC: keep gizmos in sync with the list selection (reorder included), persist the change
            if (M.SelectedPreset != presetList.index)
            {
                M.SelectedPreset = presetList.index;
                EditorUtility.SetDirty(M);
            }

            serializedObject.ApplyModifiedProperties();
        }

        //MWC: scene handles to edit the SELECTED preset target pose directly (Move/Rotate tools)
        private void OnSceneGUI()
        {
            if (M == null || M.Target == null || M.Presets.Count == 0) return;

            //MWC: Local/Global handle orientation (Tools.pivotRotation), like the built-in transform tools
            var isLocalPivot = Tools.pivotRotation == PivotRotation.Local;

            //MWC: [Source] tab while Live Preview -> rotate the actual Source bone to see the blend in real time
            if (M.LivePreview && M.SceneTab == 0)
            {
                if (M.Source != null)
                {
                    TransformReactor.DrawSceneLabel(M.Source.position, $"Source [{M.Source.name}]", M.DebugSize * 2f);

                    //MWC: on Global the handle is re-anchored at identity on drag start; the accumulated delta is applied to dragStartRot
                    if (Event.current.type == EventType.MouseDown)
                    {
                        dragStartRot = M.Source.rotation;
                        globalHandleRot = Quaternion.identity;
                    }
                    else if (Event.current.type == EventType.MouseUp) globalHandleRot = Quaternion.identity; //world-aligned rings again

                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        var handleRot = isLocalPivot ? M.Source.rotation : globalHandleRot;
                        var newHandleRot = Handles.RotationHandle(handleRot, M.Source.position);

                        if (cc.changed)
                        {
                            Undo.RecordObject(M.Source, "Rotate Reactor Source");

                            if (isLocalPivot) { M.Source.rotation = newHandleRot; } //the handle returns the new rotation directly
                            else
                            {
                                //Global: accumulate the handle rotation and apply it as a delta over the drag-start rotation
                                globalHandleRot = newHandleRot;
                                M.Source.rotation = newHandleRot * dragStartRot;
                            }
                        }
                    }
                }
                return; //do not draw the preset handles on this tab
            }

            var sel = M.SelectedPreset;
            if (sel < 0 || sel >= M.Presets.Count) return; //only the preset selected on the list shows handles

            var parent = M.Target.parent;
            var parentRot = parent != null ? parent.rotation : Quaternion.identity;

            var preset = M.Presets[sel];
            var worldPos = parent != null ? parent.TransformPoint(preset.Position) : preset.Position;
            var worldRot = parentRot * Quaternion.Euler(preset.Rotation);

            float[] currentWeights = (M.Source != null) ? M.ComputeWeights() : null;
            var label = (currentWeights != null && sel < currentWeights.Length)
                ? $"{preset.name} ({currentWeights[sel]:P0})" : preset.name;
            TransformReactor.DrawSceneLabel(worldPos, label, M.DebugSize * 2f);

            if (Tools.current == Tool.Move)
            {
                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    //MWC: Local pivot -> axes aligned to the preset rotation. Global -> world axes
                    var newPos = Handles.PositionHandle(worldPos, isLocalPivot ? worldRot : Quaternion.identity);

                    if (cc.changed)
                    {
                        Undo.RecordObject(M, "Edit Reactor Preset");
                        preset.Position = parent != null ? parent.InverseTransformPoint(newPos) : newPos;
                        EditorUtility.SetDirty(M);
                    }
                }
            }
            else if (Tools.current == Tool.Rotate)
            {
                //MWC: on Global the handle is re-anchored at identity on drag start; the accumulated delta is applied to dragStartRot
                if (Event.current.type == EventType.MouseDown)
                {
                    dragStartRot = worldRot;
                    globalHandleRot = Quaternion.identity;
                }
                else if (Event.current.type == EventType.MouseUp) globalHandleRot = Quaternion.identity; //world-aligned rings again

                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    var handleRot = isLocalPivot ? worldRot : globalHandleRot;
                    var newHandleRot = Handles.RotationHandle(handleRot, worldPos);

                    if (cc.changed)
                    {
                        Quaternion newWorldRot;

                        if (isLocalPivot) { newWorldRot = newHandleRot; } //the handle returns the new rotation directly
                        else
                        {
                            //Global: accumulate the handle rotation and apply it as a delta over the drag-start rotation
                            globalHandleRot = newHandleRot;
                            newWorldRot = newHandleRot * dragStartRot;
                        }

                        Undo.RecordObject(M, "Edit Reactor Preset");
                        preset.Rotation = (Quaternion.Inverse(parentRot) * newWorldRot).eulerAngles;
                        EditorUtility.SetDirty(M);
                    }
                }
            }
        }
    }
#endif
}
