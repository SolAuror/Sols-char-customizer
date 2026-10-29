// Last Change: 9/24/2026 1:13pm
#if UNITY_EDITOR
using MalbersAnimations.Conditions;
using MalbersAnimations.Controller;
using MalbersAnimations.Controller.AI;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace MalbersAnimations
{
    /// <summary>Converts a Malbers player into the house AI shape: AI node, no player wiring, Enemy tag and layers.</summary>
    public static class ConvertToAICharacter
    {
        /// <summary>Hit mask every AI attack/item field must end on: Animal | Default | Item.</summary>
        public const int AIHitLayers = 1074790401;

        /// <summary>Undo/menu label shared by every mutation this tool performs.</summary>
        private const string Label = "Convert To AI Character";

        /// <summary>Name of the child GameObject that carries the whole AI stack.</summary>
        private const string AINodeName = "AI Control";

        /// <summary>Name of the Brain vision transform, child of the AI node.</summary>
        private const string EyesNodeName = "Eyes";

        /// <summary>Name of the player-only subtree the conversion removes.</summary>
        private const string PlayerCoreName = "Player Core";

        /// <summary>Name of the subtree the final layer pass forces to Ignore Raycast.</summary>
        private const string InternalName = "Internal Components";

        /// <summary>Authoring position of the Eyes node, matching Human AI (Super Item).</summary>
        private static readonly Vector3 EyesLocalPosition = new(0f, 1.61f, 0.29f);

        /// <summary>State IDs the agent stops for, in the reference prefab's order.</summary>
        private static readonly string[] StopAgentStates = { "Fall", "Fly", "Jump", "LedgeGrab", "Climb" };

        /// <summary>Component type names whose m_hitLayer must be forced to the AI mask.</summary>
        private static readonly string[] HitLayerOwners = { "MAttackTrigger", "SuperItem" };

        /// <summary>Component type names whose item/weapon override layer must be forced to the AI mask.</summary>
        private static readonly string[] OverrideLayerOwners = { "SuperItemManager", "MWeaponManager" };

        /// <summary>Property names searched on the override-layer owners above.</summary>
        private static readonly string[] OverrideLayerFields = { "m_OverrideItemLayer", "OverrideWeaponLayer" };

        /// <summary>Layer mask the Mode Aligner's Layer condition must use: Animal | Player.</summary>
        public const int AlignerLayers = -2146435072;

        /// <summary>Tags a Mode Aligner aligns to on an AI, matching Steve AI (Super Item).</summary>
        private static readonly string[] AlignerTags = { "Player", "Companion", "Animal" };

        /// <summary>True while converting a live GameObject, so every mutation is recorded for Undo.</summary>
        private static bool _recordUndo;

        //--------------------------------------------------------------------------------------- Menu

        /// <summary>Menu entry: converts every selected prefab asset (after a confirmation dialog) and every selected scene GameObject.</summary>
        [MenuItem("Tools/Malbers Animations/Editor/" + Label, false, 400)]
        private static void ConvertSelection()
        {
            var prefabPaths = SelectedPrefabPaths();

            //Prefab assets are rewritten on disk with no Undo: ask first
            if (prefabPaths.Count > 0 && !EditorUtility.DisplayDialog(Label,
                $"{prefabPaths.Count} prefab asset(s) will be converted and saved in place:\n\n{string.Join("\n", prefabPaths)}\n\n" +
                "This cannot be undone. Duplicate the prefab (or make a Prefab Variant) first if you want to keep the player version.",
                "Convert", "Cancel"))
                return;

            foreach (var path in prefabPaths)
                Debug.Log(Convert(path), AssetDatabase.LoadAssetAtPath<GameObject>(path));

            foreach (var go in SelectedSceneObjects())
                Debug.Log(ConvertGameObject(go), go);
        }

        /// <summary>Menu validation: enabled while a prefab asset or a scene GameObject is selected.</summary>
        [MenuItem("Tools/Malbers Animations/Editor/" + Label, true)]
        private static bool ConvertSelectionValidate() => SelectedPrefabPaths().Count > 0 || SelectedSceneObjects().Count > 0;

        /// <summary>Hierarchy right-click entry: converts the clicked GameObject in place, with Undo.</summary>
        [MenuItem("GameObject/Malbers Animations/" + Label, false, 12)]
        private static void ConvertHierarchySelection(MenuCommand command)
        {
            if (command.context is not GameObject go) return;
            Debug.Log(ConvertGameObject(go), go);
        }

        /// <summary>Hierarchy right-click validation: enabled on any non-asset GameObject.</summary>
        [MenuItem("GameObject/Malbers Animations/" + Label, true)]
        private static bool ConvertHierarchySelectionValidate()
        {
            var go = Selection.activeGameObject;
            return go != null && !EditorUtility.IsPersistent(go);
        }

        /// <summary>Returns the asset paths of every selected prefab asset.</summary>
        private static List<string> SelectedPrefabPaths()
        {
            var paths = new List<string>();
            foreach (var o in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(o);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab")) paths.Add(path);
            }
            return paths;
        }

        /// <summary>Returns every selected GameObject that lives in a scene or a prefab stage, not in an asset.</summary>
        private static List<GameObject> SelectedSceneObjects()
        {
            var objects = new List<GameObject>();
            foreach (var o in Selection.gameObjects)
                if (o != null && !EditorUtility.IsPersistent(o)) objects.Add(o);
            return objects;
        }

        //--------------------------------------------------------------------------------------- Entry points

        /// <summary>Converts the prefab at the given path in place and returns the conversion report. Idempotent.</summary>
        public static string Convert(string prefabPath)
        {
            var log = new StringBuilder();
            log.AppendLine($"=== {Label} :: {prefabPath}");

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            _recordUndo = false;
            try
            {
                if (!ConvertRoot(root, log)) return log.ToString();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                log.AppendLine("[saved] prefab written.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        /// <summary>Converts a live GameObject (scene object, prefab instance or prefab stage root) in place, with Undo.</summary>
        public static string ConvertGameObject(GameObject root)
        {
            var log = new StringBuilder();
            log.AppendLine($"=== {Label} :: {root.name} (in scene)");

            if (EditorUtility.IsPersistent(root))
            {
                log.AppendLine("[abort] that is an asset, not a scene object - use Convert(prefabPath) instead.");
                return log.ToString();
            }

            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Label);
            Undo.RegisterFullObjectHierarchyUndo(root, Label);

            _recordUndo = true;
            try
            {
                if (!ConvertRoot(root, log)) return log.ToString();

                var stage = PrefabStageUtility.GetPrefabStage(root);
                if (stage != null) EditorSceneManager.MarkSceneDirty(stage.scene);
                else if (root.scene.IsValid()) EditorSceneManager.MarkSceneDirty(root.scene);

                if (PrefabUtility.IsPartOfPrefabInstance(root))
                    log.AppendLine("[note] this is a prefab instance - the conversion is stored as instance overrides. " +
                                   "Apply them, or convert the prefab asset instead, if every instance should change.");

                log.AppendLine("[done] scene object converted (Ctrl+Z reverts it).");
            }
            finally
            {
                _recordUndo = false;
                Undo.CollapseUndoOperations(group);
            }
            return log.ToString();
        }

        //--------------------------------------------------------------------------------------- Conversion

        /// <summary>Runs every conversion pass on a root, in the order the passes depend on. Returns false when skipped.</summary>
        private static bool ConvertRoot(GameObject root, StringBuilder log)
        {
            if (root.GetComponent<MAnimal>() == null)
            {
                log.AppendLine($"[skip] '{root.name}' has no MAnimal on its root - not a Malbers character.");
                return false;
            }

            ReportPlayerCoreReferences(root, log);
            RemoveComponents(root, "MInputLink", log);
            RemoveComponents(root, "MEventListener", log);
            DeletePlayerCore(root, log);
            StripMEventCalls(root, log);
            DisableAimCamera(root, log);
            ConfigureModeAligner(root, log);

            var ai = EnsureAINode(root, log);
            ConfigureAI(root, ai, log);

            SwapPlayerTagForEnemy(root, log);
            RegisterAIInComponentSelector(root, ai, log);
            ApplyHitLayers(root, log);
            ApplyLayers(root, log);                                        //unconditional, so it must run last
            ReportEmptyEventsNode(root, log);
            return true;
        }

        /// <summary>Reports every reference that points into the Player Core subtree from outside it.</summary>
        private static void ReportPlayerCoreReferences(GameObject root, StringBuilder log)
        {
            var core = root.transform.Find(PlayerCoreName);
            if (core == null) { log.AppendLine("[Player Core] absent - nothing to scan."); return; }

            var inside = new HashSet<Object>();
            foreach (var t in core.GetComponentsInChildren<Transform>(true))
            {
                inside.Add(t.gameObject);
                foreach (var c in t.GetComponents<Component>()) if (c != null) inside.Add(c);
            }

            var hits = 0;
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || inside.Contains(c)) continue;

                var it = new SerializedObject(c).GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var o = it.objectReferenceValue;
                    if (o == null || !inside.Contains(o)) continue;
                    hits++;
                    log.AppendLine($"[Player Core ref] {PathOf(c.transform)} :: {c.GetType().Name}.{it.propertyPath} -> {o.name}");
                }
            }
            log.AppendLine($"[Player Core] {hits} incoming reference(s) will break on delete.");
        }

        /// <summary>Destroys every component of the given type name anywhere in the hierarchy.</summary>
        private static void RemoveComponents(GameObject root, string typeName, StringBuilder log)
        {
            var found = new List<Component>();
            foreach (var c in root.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == typeName) found.Add(c);

            foreach (var c in found)
            {
                log.AppendLine($"[remove] {typeName} on {PathOf(c.transform)}");
                Destroy(c);
            }
            if (found.Count == 0) log.AppendLine($"[remove] {typeName} - none present.");
        }

        /// <summary>Deletes the Player Core subtree if it is still there.</summary>
        private static void DeletePlayerCore(GameObject root, StringBuilder log)
        {
            var core = root.transform.Find(PlayerCoreName);
            if (core == null) return;
            log.AppendLine($"[delete] {PlayerCoreName} ({core.childCount} child node(s))");
            Destroy(core.gameObject);
        }

        /// <summary>Deletes every persistent UnityEvent call that targets an MEvent, plus every null-target call.</summary>
        private static void StripMEventCalls(GameObject root, StringBuilder log)
        {
            const string CallsMarker = "m_PersistentCalls.m_Calls.Array.data[";
            int mevents = 0, nulls = 0;

            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;

                var so = new SerializedObject(c);
                var arrays = new Dictionary<string, List<int>>();
                var it = so.GetIterator();

                while (it.Next(true))
                {
                    if (!it.propertyPath.EndsWith(".m_Target")) continue;
                    if (!it.propertyPath.Contains(CallsMarker)) continue;

                    var o = it.objectReferenceValue;
                    var isMEvent = o != null && o.GetType().Name == "MEvent";
                    if (o != null && !isMEvent) continue;

                    var cut = it.propertyPath.LastIndexOf(".Array.data[", System.StringComparison.Ordinal);
                    if (cut < 0) continue;
                    var arrayPath = it.propertyPath.Substring(0, cut);
                    var open = cut + ".Array.data[".Length;
                    var close = it.propertyPath.IndexOf(']', open);
                    if (close < 0 || !int.TryParse(it.propertyPath.Substring(open, close - open), out var index)) continue;

                    if (!arrays.TryGetValue(arrayPath, out var list)) arrays[arrayPath] = list = new List<int>();
                    list.Add(index);

                    if (isMEvent) { mevents++; log.AppendLine($"[MEvent] {PathOf(c.transform)} :: {c.GetType().Name}.{arrayPath}[{index}] -> {o.name}"); }
                    else nulls++;
                }

                if (arrays.Count == 0) continue;

                foreach (var kv in arrays)
                {
                    var arr = so.FindProperty(kv.Key);
                    if (arr == null || !arr.isArray) continue;
                    kv.Value.Sort();
                    for (var i = kv.Value.Count - 1; i >= 0; i--)          //descending: indices shift otherwise
                        if (kv.Value[i] < arr.arraySize) arr.DeleteArrayElementAtIndex(kv.Value[i]);
                }
                Apply(so);
            }
            log.AppendLine($"[MEvent] stripped {mevents} MEvent call(s) and {nulls} null call(s).");
        }

        /// <summary>Turns Aim's Use Camera off - the camera is player-only, an AI aims from its target or its forward.</summary>
        private static void DisableAimCamera(GameObject root, StringBuilder log)
        {
            var count = 0;
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c is not Aim) continue;                                //also catches AimBatched

                var useCamera = new SerializedObject(c).FindProperty("m_UseCamera");
                if (useCamera == null) continue;

                var useConstant = useCamera.FindPropertyRelative("UseConstant");
                var constant = useCamera.FindPropertyRelative("ConstantValue");
                if (constant == null) continue;
                if (useConstant != null && useConstant.boolValue && !constant.boolValue) continue;

                SetBool(useConstant, true);                                //detach from any shared Bool Var first
                constant.boolValue = false;
                Apply(useCamera.serializedObject);
                count++;
                log.AppendLine($"[Aim] {PathOf(c.transform)} :: Use Camera -> false");
            }
            log.AppendLine($"[Aim] {count} component(s) switched off the camera.");
        }

        /// <summary>Points every Mode Aligner at Player/Companion/Animal and pins its Layer condition to Animal | Player.</summary>
        private static void ConfigureModeAligner(GameObject root, StringBuilder log)
        {
            var found = 0;
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c is not ModeAligner2) continue;
                found++;

                var so = new SerializedObject(c);

                var tags = so.FindProperty("Tags.tags");
                if (tags != null)
                {
                    tags.ClearArray();
                    for (var i = 0; i < AlignerTags.Length; i++)
                    {
                        tags.InsertArrayElementAtIndex(i);
                        tags.GetArrayElementAtIndex(i).objectReferenceValue = FindID<Tag>(AlignerTags[i]);
                    }
                    log.AppendLine($"[Aligner] {PathOf(c.transform)} :: Tags = {string.Join(", ", AlignerTags)}");
                }

                var conditions = so.FindProperty("GlobalConditions.conditions");
                if (conditions == null) { Apply(so); continue; }

                var layers = -1;
                for (var i = 0; i < conditions.arraySize; i++)
                    if (conditions.GetArrayElementAtIndex(i).managedReferenceFullTypename.EndsWith("." + nameof(C2_Layers)))
                    { layers = i; break; }

                if (layers >= 0)
                {
                    var layer = conditions.GetArrayElementAtIndex(layers).FindPropertyRelative("Layer");
                    SetBool(layer.FindPropertyRelative("UseConstant"), true);
                    layer.FindPropertyRelative("ConstantValue").intValue = AlignerLayers;
                    log.AppendLine($"[Aligner] {PathOf(c.transform)} :: Layer condition [{layers}] -> {AlignerLayers}");
                }
                else
                {
                    conditions.InsertArrayElementAtIndex(0);                //Target stays null: the aligner feeds it a dynamic target
                    conditions.GetArrayElementAtIndex(0).managedReferenceValue = new C2_Layers
                    {
                        active = true,
                        invert = false,
                        OrAnd = true,
                        LocalTarget = false,
                        Layer = new LayerReference(AlignerLayers),
                    };
                    log.AppendLine($"[Aligner] {PathOf(c.transform)} :: inserted Layer condition at 0 -> {AlignerLayers}");
                }
                Apply(so);
            }
            if (found == 0) log.AppendLine("[Aligner] no ModeAligner2 present.");
        }

        /// <summary>Creates or finds the AI node and its Eyes child, and returns the AI node transform.</summary>
        private static Transform EnsureAINode(GameObject root, StringBuilder log)
        {
            var ai = root.transform.Find(AINodeName);
            if (ai == null)
            {
                ai = CreateChild(root.transform, AINodeName);
                log.AppendLine($"[create] {AINodeName}");
            }
            Record(ai);
            ai.SetSiblingIndex(0);

            var eyes = ai.Find(EyesNodeName);
            if (eyes == null)
            {
                eyes = CreateChild(ai, EyesNodeName);
                eyes.localPosition = EyesLocalPosition;
                log.AppendLine($"[create] {AINodeName}/{EyesNodeName} at {EyesLocalPosition}");
            }

            var hp = eyes.GetComponent<HumanoidParent>();
            if (hp == null) hp = Add<HumanoidParent>(eyes.gameObject);
            Record(hp);
            hp.animator = root.GetComponent<Animator>();
            hp.parent = HumanBodyBones.Head;
            EditorUtility.SetDirty(hp);

            return ai;
        }

        /// <summary>Adds and configures the NavMeshAgent, MAnimalAIControl and MAnimalBrain on the AI node.</summary>
        private static void ConfigureAI(GameObject root, Transform ai, StringBuilder log)
        {
            var agent = ai.GetComponent<NavMeshAgent>();
            if (agent == null) { agent = Add<NavMeshAgent>(ai.gameObject); log.AppendLine("[add] NavMeshAgent"); }

            Record(agent);
            agent.radius = 0.4f;
            agent.height = 1.8f;
            agent.speed = 3.5f;
            agent.angularSpeed = 120f;
            agent.acceleration = 8f;
            agent.stoppingDistance = 0.6f;
            agent.baseOffset = 0f;
            agent.updatePosition = true;
            agent.updateRotation = true;
            agent.updateUpAxis = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            EditorUtility.SetDirty(agent);

            var control = ai.GetComponent<MAnimalAIControl>();
            if (control == null) { control = Add<MAnimalAIControl>(ai.gameObject); log.AppendLine("[add] MAnimalAIControl"); }

            var cso = new SerializedObject(control);
            SetObject(cso, "agent", agent);
            SetObject(cso, "animal", root.GetComponent<MAnimal>());
            SetObject(cso, "AgentTransform", ai);

            var air = cso.FindProperty("AirDestinationState");
            if (air != null && air.objectReferenceValue == null) air.objectReferenceValue = FindID<StateID>("Fly");

            var items = cso.FindProperty("stopAgentOn.items");
            if (items != null)
            {
                items.ClearArray();
                for (var i = 0; i < StopAgentStates.Length; i++)
                {
                    items.InsertArrayElementAtIndex(i);
                    items.GetArrayElementAtIndex(i).objectReferenceValue = FindID<StateID>(StopAgentStates[i]);
                }
                log.AppendLine($"[AIControl] stopAgentOn = {string.Join(", ", StopAgentStates)}");
            }
            Apply(cso);

            var brain = ai.GetComponent<MAnimalBrain>();
            if (brain == null) { brain = Add<MAnimalBrain>(ai.gameObject); log.AppendLine("[add] MAnimalBrain"); }

            var bso = new SerializedObject(brain);
            SetObject(bso, "Eyes", ai.Find(EyesNodeName));
            var cooldown = bso.FindProperty("TransitionCoolDown");
            if (cooldown != null)
            {
                SetBool(cooldown.FindPropertyRelative("UseConstant"), true);
                var constant = cooldown.FindPropertyRelative("ConstantValue");
                if (constant != null) constant.floatValue = 0.2f;
            }
            SetBool(bso.FindProperty("DisableAIOnDeath"), true);
            Apply(bso);                                                    //currentState stays null on purpose
            log.AppendLine("[Brain] Eyes wired, CoolDown 0.2, DisableAIOnDeath true, currentState left null.");
        }

        /// <summary>Replaces the Player tag with Enemy in place, preserving every other tag and its order.</summary>
        private static void SwapPlayerTagForEnemy(GameObject root, StringBuilder log)
        {
            var tags = root.GetComponent<Tags>();
            if (tags == null) { log.AppendLine("[Tags] component absent - reported, not added."); return; }

            var so = new SerializedObject(tags);
            var arr = so.FindProperty("tags");
            if (arr == null || !arr.isArray) { log.AppendLine("[Tags] no 'tags' array found."); return; }

            int player = -1, enemy = -1;
            for (var i = 0; i < arr.arraySize; i++)
            {
                var o = arr.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o == null) continue;
                if (o.name == "Player") player = i;
                if (o.name == "Enemy") enemy = i;
            }

            if (player < 0 && enemy >= 0) { log.AppendLine("[Tags] already Enemy - unchanged."); return; }

            if (player >= 0 && enemy >= 0)
            {
                arr.DeleteArrayElementAtIndex(player);
                log.AppendLine("[Tags] removed duplicate Player (Enemy already present).");
            }
            else if (player >= 0)
            {
                arr.GetArrayElementAtIndex(player).objectReferenceValue = FindID<Tag>("Enemy");
                log.AppendLine($"[Tags] Player -> Enemy at index {player}.");
            }
            else
            {
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = FindID<Tag>("Enemy");
                log.AppendLine("[Tags] neither Player nor Enemy - appended Enemy.");
            }
            Apply(so);
        }

        /// <summary>Inserts an 'AI' entry pointing at the AI node at index 0 of the ComponentSelector.</summary>
        private static void RegisterAIInComponentSelector(GameObject root, Transform ai, StringBuilder log)
        {
            var selector = root.GetComponent<ComponentSelector>();
            if (selector == null) { log.AppendLine("[Selector] ComponentSelector absent."); return; }

            var so = new SerializedObject(selector);
            var arr = so.FindProperty("internalComponents");
            if (arr == null || !arr.isArray) { log.AppendLine("[Selector] no 'internalComponents' array."); return; }

            for (var i = 0; i < arr.arraySize; i++)
            {
                var gos = arr.GetArrayElementAtIndex(i).FindPropertyRelative("gameObjects");
                if (gos == null) continue;
                for (var k = 0; k < gos.arraySize; k++)
                    if (gos.GetArrayElementAtIndex(k).objectReferenceValue == ai.gameObject)
                    { log.AppendLine($"[Selector] AI node already listed at index {i}."); return; }
            }

            arr.InsertArrayElementAtIndex(0);                              //clones element 0 - every field is rewritten below
            var entry = arr.GetArrayElementAtIndex(0);
            SetString(entry.FindPropertyRelative("name"), "AI");
            SetString(entry.FindPropertyRelative("tooltip"), string.Empty);
            SetBool(entry.FindPropertyRelative("active"), true);

            var objects = entry.FindPropertyRelative("gameObjects");
            if (objects != null)
            {
                objects.ClearArray();
                objects.InsertArrayElementAtIndex(0);
                objects.GetArrayElementAtIndex(0).objectReferenceValue = ai.gameObject;
            }

            var behaviours = entry.FindPropertyRelative("monoBehaviours");
            if (behaviours != null) behaviours.ClearArray();

            Apply(so);
            log.AppendLine("[Selector] inserted 'AI' entry at index 0.");
        }

        /// <summary>Forces every attack, item and manager layer mask in the hierarchy to Animal | Default | Item.</summary>
        private static void ApplyHitLayers(GameObject root, StringBuilder log)
        {
            var count = 0;
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var name = c.GetType().Name;
                var isHit = c is MAttackTrigger || System.Array.IndexOf(HitLayerOwners, name) >= 0;
                var isOverride = System.Array.IndexOf(OverrideLayerOwners, name) >= 0;
                if (!isHit && !isOverride) continue;

                var so = new SerializedObject(c);
                var changed = false;
                if (isHit) changed |= SetLayerReference(so, "m_hitLayer");
                if (isOverride)
                    foreach (var field in OverrideLayerFields) changed |= SetLayerReference(so, field);

                if (!changed) continue;
                Apply(so);
                count++;
                log.AppendLine($"[layers] {PathOf(c.transform)} :: {name} -> {AIHitLayers}");
            }
            log.AppendLine($"[layers] {count} hit-layer field owner(s) set.");
        }

        /// <summary>Sets a LayerReference property to the AI mask with UseConstant on; returns true when found.</summary>
        private static bool SetLayerReference(SerializedObject so, string field)
        {
            var prop = so.FindProperty(field);
            if (prop == null) return false;
            SetBool(prop.FindPropertyRelative("UseConstant"), true);
            var constant = prop.FindPropertyRelative("ConstantValue");
            if (constant != null) constant.intValue = AIHitLayers;
            return true;
        }

        /// <summary>Final pass: whole hierarchy to Enemy, then the Internal Components subtree to Ignore Raycast.</summary>
        private static void ApplyLayers(GameObject root, StringBuilder log)
        {
            var enemy = LayerMask.NameToLayer("Enemy");
            var ignore = LayerMask.NameToLayer("Ignore Raycast");
            if (enemy < 0) { log.AppendLine("[layer] 'Enemy' layer does not exist - skipped."); return; }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                Record(t.gameObject);
                t.gameObject.layer = enemy;
            }

            var internals = root.transform.Find(InternalName);
            if (internals != null && ignore >= 0)
                foreach (var t in internals.GetComponentsInChildren<Transform>(true))
                {
                    Record(t.gameObject);
                    t.gameObject.layer = ignore;
                }

            log.AppendLine($"[layer] hierarchy -> Enemy, {InternalName} subtree -> Ignore Raycast.");
        }

        /// <summary>Flags an Events node that is left empty by the conversion instead of deleting it.</summary>
        private static void ReportEmptyEventsNode(GameObject root, StringBuilder log)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Events" || t.childCount > 0) continue;
                if (t.GetComponents<Component>().Length > 1) continue;
                log.AppendLine($"[flag] '{PathOf(t)}' is now empty - left in place, delete it by hand if you want it gone.");
            }
        }

        //--------------------------------------------------------------------------------------- Undo-aware helpers

        /// <summary>Destroys an object, through Undo while converting a live GameObject.</summary>
        private static void Destroy(Object o)
        {
            if (_recordUndo) Undo.DestroyObjectImmediate(o);
            else Object.DestroyImmediate(o);
        }

        /// <summary>Adds a component, through Undo while converting a live GameObject.</summary>
        private static T Add<T>(GameObject go) where T : Component
            => _recordUndo ? Undo.AddComponent<T>(go) : go.AddComponent<T>();

        /// <summary>Creates a child GameObject, registered for Undo while converting a live GameObject.</summary>
        private static Transform CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            if (_recordUndo) Undo.RegisterCreatedObjectUndo(go, Label);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>Applies pending SerializedObject changes, with Undo while converting a live GameObject.</summary>
        private static void Apply(SerializedObject so)
        {
            if (_recordUndo) so.ApplyModifiedProperties();
            else so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Records an object before a direct field write while converting a live GameObject.</summary>
        private static void Record(Object o)
        {
            if (_recordUndo && o != null) Undo.RecordObject(o, Label);
        }

        //--------------------------------------------------------------------------------------- Small helpers

        /// <summary>Assigns an object reference property when it exists.</summary>
        private static void SetObject(SerializedObject so, string field, Object value)
        {
            var prop = so.FindProperty(field);
            if (prop != null) prop.objectReferenceValue = value;
        }

        /// <summary>Assigns a bool property when it exists.</summary>
        private static void SetBool(SerializedProperty prop, bool value)
        {
            if (prop != null) prop.boolValue = value;
        }

        /// <summary>Assigns a string property when it exists.</summary>
        private static void SetString(SerializedProperty prop, string value)
        {
            if (prop != null) prop.stringValue = value;
        }

        /// <summary>Finds an ID asset of the given type by exact asset name.</summary>
        private static T FindID<T>(string name) where T : Object
        {
            foreach (var guid in AssetDatabase.FindAssets($"\"{name}\" t:{typeof(T).Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }
            Debug.LogWarning($"[{Label}] {typeof(T).Name} asset '{name}' not found.");
            return null;
        }

        /// <summary>Returns the hierarchy path of a transform relative to its prefab root.</summary>
        private static string PathOf(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
#endif