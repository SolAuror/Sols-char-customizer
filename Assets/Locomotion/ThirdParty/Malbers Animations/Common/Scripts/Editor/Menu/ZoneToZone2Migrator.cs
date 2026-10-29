#if UNITY_EDITOR

using MalbersAnimations.Controller;
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MalbersAnimations.Menu
{
    /// <summary>
    /// MWC: Replaces every old <see cref="Zone"/> component with a <see cref="Zone2"/> (Reaction Zone) on Prefabs and Scenes.
    /// The Zone Type logic (Mode, State, Stance, Force) is converted to Reactions on the Activation/Exit reaction lists.
    /// <para>Two passes keep Prefab Variants, Nested Prefabs and Scene instances intact:</para>
    /// 1. Add: every Zone gets its Zone2 (bases first, so variants/instances inherit it), overrides are converted and references re-pointed.<br/>
    /// 2. Remove: the old Zone components are destroyed where they are owned (base prefabs, added components, plain scene objects).
    /// </summary>
    public class ZoneToZone2Migrator : EditorWindow
    {
        private const string Title = "Zone to Zone2 Migrator";

        [SerializeField] private string folder = "Assets";
        [SerializeField] private bool includePrefabs = true;
        [SerializeField] private bool includeScenes = true;

        private Vector2 scroll;
        private string report = string.Empty;

        [MenuItem("Tools/Malbers Animations/Editor/" + Title)]
        public static void ShowWindow()
        {
            var window = GetWindow<ZoneToZone2Migrator>(false, Title, true);
            window.minSize = new Vector2(420, 300);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Replaces every old Zone component with a Zone2 (Reaction Zone).\n" +
                "Mode, State, Stance and Force logic is converted to Reactions.\n" +
                "Commit your project (or back it up) before migrating. Scan first to review the report.",
                MessageType.Info);

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    folder = EditorGUILayout.TextField("Folder", folder);
                    if (GUILayout.Button("...", GUILayout.Width(30)))
                    {
                        var abs = EditorUtility.OpenFolderPanel("Folder to migrate", folder, "");
                        if (!string.IsNullOrEmpty(abs) && abs.StartsWith(Application.dataPath))
                            folder = "Assets" + abs.Substring(Application.dataPath.Length);
                    }
                }
                includePrefabs = EditorGUILayout.Toggle("Prefabs", includePrefabs);
                includeScenes = EditorGUILayout.Toggle("Scenes", includeScenes);
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan (Dry Run)", GUILayout.Height(28)))
                    report = Run(folder, includePrefabs, includeScenes, true);

                if (GUILayout.Button("Migrate", GUILayout.Height(28)))
                {
                    if (EditorUtility.DisplayDialog(Title,
                        $"Migrate every Zone inside [{folder}] to Zone2?\nThe old Zone components will be removed. This cannot be undone (use version control).",
                        "Migrate", "Cancel"))
                    {
                        report = Run(folder, includePrefabs, includeScenes, false);
                    }
                }
            }

            //Testing: migrate only the selected Scene GameObjects (and their children), with Undo
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var selected = SelectedSceneZones();
                EditorGUILayout.LabelField($"Selected GameObjects (Testing) — {selected.Count} Zone(s) found", EditorStyles.boldLabel);

                using (new EditorGUI.DisabledGroupScope(selected.Count == 0))
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Scan Selected", GUILayout.Height(24)))
                        report = ScanSelected();

                    if (GUILayout.Button("Migrate Selected", GUILayout.Height(24)))
                        report = MigrateSelected();
                }
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }

        private void OnSelectionChange() => Repaint(); //Refresh the Selected Zones count

        //--------------------------------------------------------------------------------------- Batch

        /// <summary>Migrates (or only reports when dryRun) every Zone found in Prefabs and Scenes inside the folder</summary>
        public static string Run(string folder, bool prefabs, bool scenes, bool dryRun)
        {
            var log = new StringBuilder();
            if (!AssetDatabase.IsValidFolder(folder)) return $"Invalid folder [{folder}]";

            if (!dryRun && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return "Cancelled. Save or discard the open scenes first.";

            var setup = EditorSceneManager.GetSceneManagerSetup();

            var prefabPaths = prefabs ? FindZonePrefabs(folder) : new List<string>();
            var scenePaths = scenes ? FindZoneScenes(folder, prefabPaths) : new List<string>();

            log.AppendLine($"{(dryRun ? "SCAN" : "MIGRATION")} — {prefabPaths.Count} Prefab(s), {scenePaths.Count} Scene(s)\n");

            try
            {
                if (dryRun)
                {
                    foreach (var path in prefabPaths) ScanPrefab(path, log);
                    foreach (var path in scenePaths) ScanScene(path, log);
                    return log.ToString();
                }

                int total = prefabPaths.Count * 2 + scenePaths.Count * 2, step = 0;

                //Pass 1: Add Zone2 (bases first so Variants and Scene instances inherit it)
                foreach (var path in prefabPaths)
                {
                    Progress("Adding Zone2", path, ++step, total);
                    EditPrefab(path, root => AddPass(root, path, log));
                }
                foreach (var path in scenePaths)
                {
                    Progress("Adding Zone2", path, ++step, total);
                    EditScene(path, scene => AddPass(scene, path, log));
                }

                //Pass 2: Remove the old Zones where they are owned
                foreach (var path in prefabPaths)
                {
                    Progress("Removing Zone", path, ++step, total);
                    EditPrefab(path, root => RemovePass(root, path, log));
                }
                foreach (var path in scenePaths)
                {
                    Progress("Removing Zone", path, ++step, total);
                    EditScene(path, scene => RemovePass(scene, path, log));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!dryRun && setup != null && setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                AssetDatabase.SaveAssets();
            }

            var result = log.ToString();
            Debug.Log($"[{Title}]\n{result}");
            return result;
        }

        private static void Progress(string title, string path, int step, int total) =>
            EditorUtility.DisplayProgressBar(Title, $"{title}: {Path.GetFileName(path)}", (float)step / Mathf.Max(1, total));

        /// <summary>Prefabs inside the folder that contain a Zone, sorted so Base/Nested prefabs come before the prefabs using them</summary>
        private static List<string> FindZonePrefabs(string folder)
        {
            var result = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".prefab")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.GetComponentsInChildren<Zone>(true).Length > 0) result.Add(path);
            }

            var depth = new Dictionary<string, int>();
            return result.OrderBy(p => PrefabDepth(p, depth, new HashSet<string>())).ThenBy(p => p).ToList();
        }

        /// <summary>How many prefab levels (Base/Nested) are under this prefab</summary>
        private static int PrefabDepth(string path, Dictionary<string, int> cache, HashSet<string> visiting)
        {
            if (cache.TryGetValue(path, out var d)) return d;
            if (!visiting.Add(path)) return 0; //Cycle guard

            d = 0;
            foreach (var dep in AssetDatabase.GetDependencies(path, false))
            {
                if (dep != path && dep.EndsWith(".prefab"))
                    d = Mathf.Max(d, PrefabDepth(dep, cache, visiting) + 1);
            }
            visiting.Remove(path);
            cache[path] = d;
            return d;
        }

        /// <summary>Scenes inside the folder that use the Zone script or one of the Zone prefabs</summary>
        private static List<string> FindZoneScenes(string folder, List<string> zonePrefabs)
        {
            var keys = new List<string>();
            var zoneScript = ZoneScriptGUID();
            if (!string.IsNullOrEmpty(zoneScript)) keys.Add(zoneScript);
            keys.AddRange(zonePrefabs.Select(AssetDatabase.AssetPathToGUID));

            var result = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var text = File.ReadAllText(path);
                if (keys.Any(k => text.Contains(k))) result.Add(path);
            }
            result.Sort();
            return result;
        }

        private static string ZoneScriptGUID()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript Zone"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == typeof(Zone)) return guid;
            }
            return null;
        }

        private static void EditPrefab(string path, System.Func<GameObject, bool> action)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (action(root)) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EditScene(string path, System.Func<Scene, bool> action)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            if (action(scene)) EditorSceneManager.SaveScene(scene);
        }

        private static List<GameObject> Roots(Scene scene) => scene.GetRootGameObjects().ToList();

        private static bool AddPass(Scene scene, string path, StringBuilder log) => AddPass(Roots(scene), path, log);
        private static bool AddPass(GameObject root, string path, StringBuilder log) => AddPass(new List<GameObject> { root }, path, log);

        private static bool RemovePass(Scene scene, string path, StringBuilder log) => RemovePass(Roots(scene), path, log);
        private static bool RemovePass(GameObject root, string path, StringBuilder log) => RemovePass(new List<GameObject> { root }, path, log);

        /// <summary>Pass 1: every Zone gets its Zone2. References to the Zone are moved to the Zone2</summary>
        private static bool AddPass(List<GameObject> roots, string path, StringBuilder log)
        {
            bool changed = false;

            foreach (var zone in roots.SelectMany(r => r.GetComponentsInChildren<Zone>(true)).ToList())
            {
                var owned = IsOwned(zone);
                var zone2 = PairedZone2(zone);

                if (zone2 == null)
                {
                    if (!owned) log.AppendLine($"  ⚠ {path} → [{HierarchyPath(zone)}] inherits a Zone that was not migrated (Base prefab outside the folder?). Added a Zone2 to it anyway.");
                    zone2 = AddZone2(zone.gameObject, false);
                    Convert(zone, zone2, log, path);
                }
                else if (owned || HasOverrides(zone))
                {
                    Convert(zone, zone2, log, path); //Owned Zone (rerun) or inherited Zone with overrides
                }

                RetargetReferences(roots, zone, zone2, log, path);
                changed = true;
            }
            return changed;
        }

        /// <summary>Pass 2: destroy the Zones owned by this file (inherited ones are removed by their Base prefab)</summary>
        private static bool RemovePass(List<GameObject> roots, string path, StringBuilder log)
        {
            bool changed = false;

            foreach (var zone in roots.SelectMany(r => r.GetComponentsInChildren<Zone>(true)).ToList())
            {
                if (!IsOwned(zone))
                {
                    log.AppendLine($"  ⚠ {path} → [{HierarchyPath(zone)}] Zone is inherited from a prefab that was not migrated. Remove it manually.");
                    continue;
                }

                log.AppendLine($"  ✔ {path} → [{HierarchyPath(zone)}] Zone ({zone.zoneType}) → Zone2");
                Object.DestroyImmediate(zone, true);
                changed = true;
            }
            return changed;
        }

        private static void ScanPrefab(string path, StringBuilder log)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { ScanRoots(new List<GameObject> { root }, path, log); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ScanScene(string path, StringBuilder log)
        {
            var wasOpen = SceneManager.GetSceneByPath(path).isLoaded;
            var scene = wasOpen ? SceneManager.GetSceneByPath(path) : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try { ScanRoots(Roots(scene), path, log); }
            finally { if (!wasOpen && scene.IsValid() && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true); }
        }

        private static void ScanRoots(List<GameObject> roots, string path, StringBuilder log)
        {
            foreach (var zone in roots.SelectMany(r => r.GetComponentsInChildren<Zone>(true)))
            {
                var owned = IsOwned(zone);
                var overrides = !owned && HasOverrides(zone);

                if (!owned && !overrides) continue; //Inherited untouched: migrated by its base prefab

                log.AppendLine($"• {path} → [{HierarchyPath(zone)}] {zone.zoneType}{(owned ? "" : " (inherited, has overrides)")}");
                foreach (var warning in Warnings(zone)) log.AppendLine($"    ⚠ {warning}");
            }
        }

        //--------------------------------------------------------------------------------------- Selected / Single Component

        [MenuItem("GameObject/Malbers Animations/Migrate Zones to Zone2", false, 13)]
        private static void MigrateSelectedMenu() => MigrateSelected();

        [MenuItem("GameObject/Malbers Animations/Migrate Zones to Zone2", true)]
        private static bool MigrateSelectedMenuValidate() => SelectedSceneZones().Count > 0;

        [MenuItem("CONTEXT/Zone/Migrate to Zone2")]
        private static void MigrateContext(MenuCommand command)
        {
            if (command.context is not Zone zone) return;

            var log = new StringBuilder();
            Undo.SetCurrentGroupName("Migrate Zone to Zone2");
            var zone2 = MigrateSceneZone(zone, log);
            Debug.Log($"[{Title}] {zone2.name} migrated.\n{log}", zone2);
        }

        /// <summary>Zones on the selected Scene (or Prefab Mode) GameObjects and their children. Assets in the Project window are ignored</summary>
        private static List<Zone> SelectedSceneZones()
        {
            return Selection.gameObjects
                .Where(go => go != null && !EditorUtility.IsPersistent(go))
                .SelectMany(go => go.GetComponentsInChildren<Zone>(true))
                .Distinct()
                .ToList();
        }

        /// <summary>Reports what Migrate Selected would do, without changing anything</summary>
        private static string ScanSelected()
        {
            var zones = SelectedSceneZones();
            var log = new StringBuilder($"SCAN SELECTED — {zones.Count} Zone(s)\n\n");

            foreach (var zone in zones)
            {
                var owned = IsOwned(zone);
                log.AppendLine($"• [{HierarchyPath(zone)}] {zone.zoneType}{(owned ? "" : " (Prefab instance: the Zone will be removed as an instance override)")}");
                foreach (var warning in Warnings(zone)) log.AppendLine($"    ⚠ {warning}");
            }
            return log.ToString();
        }

        /// <summary>Migrates the Zones of the selected Scene GameObjects in place. One Undo step (Ctrl+Z) reverts everything</summary>
        private static string MigrateSelected()
        {
            var zones = SelectedSceneZones();
            var log = new StringBuilder($"MIGRATE SELECTED — {zones.Count} Zone(s)\n\n");
            if (zones.Count == 0) return log.ToString();

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Migrate Selected Zones to Zone2");

            var migrated = new List<Object>();

            foreach (var zone in zones)
            {
                var path = HierarchyPath(zone);
                var type = zone.zoneType;
                var zone2 = MigrateSceneZone(zone, log);
                log.AppendLine($"  ✔ [{path}] Zone ({type}) → Zone2");
                migrated.Add(zone2.gameObject);
            }

            Undo.CollapseUndoOperations(group);
            Selection.objects = migrated.ToArray();

            var result = log.ToString();
            Debug.Log($"[{Title}]\n{result}");
            return result;
        }

        /// <summary>Migrates one Zone living in an open Scene or in Prefab Mode, with Undo.
        /// Prefab instances reuse the inherited Zone2 when their prefab was already migrated; the old Zone is removed as an instance override</summary>
        private static Zone2 MigrateSceneZone(Zone zone, StringBuilder log)
        {
            var go = zone.gameObject;
            var scene = go.scene;
            var label = scene.IsValid() ? scene.name : go.name;
            var roots = scene.IsValid() ? Roots(scene) : new List<GameObject> { zone.transform.root.gameObject };

            var zone2 = IsOwned(zone) ? null : PairedZone2(zone); //Inherited Zone2 (prefab already migrated)

            if (zone2 == null)
                zone2 = AddZone2(go, true);
            else
                Undo.RecordObject(zone2, "Migrate Zone to Zone2");

            Convert(zone, zone2, log, label);
            RetargetReferences(roots, zone, zone2, log, label, true);
            Undo.DestroyObjectImmediate(zone);

            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            return zone2;
        }

        //--------------------------------------------------------------------------------------- Conversion

        /// <summary>Copies the Zone values into the Zone2 and converts the Zone Type into Reactions</summary>
        public static void Convert(Zone zone, Zone2 zone2, StringBuilder log, string path)
        {
            foreach (var warning in Warnings(zone)) log.AppendLine($"  ⚠ {path} → [{HierarchyPath(zone)}] {warning}");

            //Same serialized names on both components
            var src = new SerializedObject(zone);
            var dst = new SerializedObject(zone2);
            foreach (var field in SharedFields)
            {
                var p = src.FindProperty(field);
                if (p != null) dst.CopyFromSerializedProperty(p);
            }
            if (zone.zoneType == ZoneType.ReactionsOnly)
                dst.CopyFromSerializedProperty(src.FindProperty(nameof(Zone.zoneID)));
            dst.ApplyModifiedPropertiesWithoutUndo();

            zone2.enabled = zone.enabled;
            zone2.tagUpdated = true; //m_Tags was copied. Stops Zone2.OnValidate from overwriting it with the old tag list
            zone2.conditions = zone.ActivationConditions;

            if (zone.zoneType != ZoneType.ReactionsOnly)
                zone2.zoneID = new IntReference(OldZoneID(zone)); //Keep the ID the Zone had at runtime (Conditions, AI)

            var enter = new List<Reaction>();
            var exit = new List<Reaction>();
            zone2.InputMode = null;

            switch (zone.zoneType)
            {
                case ZoneType.Mode:
                    if (zone.modeID != null)
                    {
                        var ability = zone.ModeAbilityIndex;
                        enter.Add(new ModeReaction
                        {
                            type = zone.ForceMode ? ModeReaction.Mode_Reaction.ForceActivate : ModeReaction.Mode_Reaction.Play,
                            ID = zone.modeID,
                            abilityStatus = zone.m_abilityStatus,
                            Ability = new IntReference(ability),
                            AbilityTime = zone.AbilityTime,
                            ModePower = zone.ModeFloat,
                        });
                        zone2.InputMode = zone.modeID;                     //Mode Input activates the Zone (Mode.cs)
                        zone2.InputAbility = new IntReference(ability);
                        zone2.PrepareModeZone = zone.PrepareModeZone;
                    }
                    break;

                case ZoneType.State:
                    AddState(enter, zone, zone.stateAction);
                    AddState(exit, zone, zone.stateActionExit);
                    break;

                case ZoneType.Stance:
                    AddStance(enter, zone, zone.stanceActionEnter);
                    if (zone.SetStanceAsDefault && zone.stanceActionEnter != StanceAction.None)
                        enter.Add(new StanceReaction { action = Stance_Reaction.SetDefault, ID = zone.stanceID });
                    AddStance(exit, zone, zone.stanceActionExit);
                    break;

                case ZoneType.Force:
                    enter.Add(new ZoneForceReaction
                    {
                        action = ZoneForceReaction.ForceAction.Enter,
                        Direction = zone2.transform,
                        Force = zone.Force,                                 //Serialized as a copy on the Zone2
                        EnterAcceleration = zone.EnterAcceleration,
                        LimitForce = zone.LimitForce,
                        ForceGrounded = zone.ForceGrounded,
                        ForceAirControl = zone.ForceAirControl,
                    });
                    exit.Add(new ZoneForceReaction
                    {
                        action = ZoneForceReaction.ForceAction.Exit,
                        ExitDrag = zone.ExitDrag,
                    });
                    break;
            }

            //Zone Type reaction first: when it fails the Zone is not activated (same as the old Zone)
            zone2.ActivationReaction = Combine(enter, zone.ActivationReaction);
            //Old Zone reverted its type on exit AFTER the Exit Reaction
            zone2.ExitReaction = Combine(zone.ExitReaction, exit);
            zone2.EnterReaction = Combine(zone.EnterReaction, null);

            EditorUtility.SetDirty(zone2);
            PrefabUtility.RecordPrefabInstancePropertyModifications(zone2);
        }

        /// <summary>Fields with the same serialized name and type on Zone and Zone2</summary>
        private static readonly string[] SharedFields =
        {
            nameof(Zone.debug), nameof(Zone.automatic), nameof(Zone.Limit), nameof(Zone.DisableAfterUsed), nameof(Zone.DisableDelay),
            nameof(Zone.RemoveAnimalOnActive), nameof(Zone.Weight), nameof(Zone.Angle), nameof(Zone.DoubleSide), nameof(Zone.Flip),
            nameof(Zone.BoneOnly), nameof(Zone.BoneName), nameof(Zone.Layer), nameof(Zone.m_Tags), nameof(Zone.tagUpdated),
            nameof(Zone.ZoneCollider), nameof(Zone.OnEnter), nameof(Zone.OnExit), nameof(Zone.OnZoneActivation), nameof(Zone.OnZoneFailed),
        };

        private static void AddState(List<Reaction> list, Zone zone, StateAction action)
        {
            if (action == StateAction.None) return;

            var r = new StateReaction { ID = zone.stateID };
            var status = zone.stateStatus.Value;

            switch (action)
            {
                case StateAction.Activate: r.type = StateReaction.State_Reaction.Activate; r.EnterStatus = status; break;
                case StateAction.AllowExit: r.type = StateReaction.State_Reaction.AllowExit; break;
                case StateAction.ForceActivate: r.type = StateReaction.State_Reaction.ForceActivate; r.EnterStatus = status; break;
                case StateAction.Enable: r.type = StateReaction.State_Reaction.Enable; break;
                case StateAction.Disable: r.type = StateReaction.State_Reaction.Disable; break;
                case StateAction.SetExitStatus: r.type = StateReaction.State_Reaction.SetExitStatus; r.ExitStatus = status; break;
            }
            list.Add(r);
        }

        private static void AddStance(List<Reaction> list, Zone zone, StanceAction action)
        {
            switch (action)
            {
                case StanceAction.Activate: list.Add(new StanceReaction { action = Stance_Reaction.Set, ID = zone.stanceID }); break;
                case StanceAction.Exit: list.Add(new StanceReaction { action = Stance_Reaction.ResetToDefault, ID = zone.stanceID }); break;
            }
        }

        private static Reaction2 Combine(IEnumerable<Reaction> first, Reaction2 second) => Combine(first, second.reactions);
        private static Reaction2 Combine(Reaction2 first, IEnumerable<Reaction> second) => Combine(first.reactions, second);

        private static Reaction2 Combine(IEnumerable<Reaction> first, IEnumerable<Reaction> second)
        {
            var list = new List<Reaction>();
            if (first != null) list.AddRange(first.Where(r => r != null));
            if (second != null) list.AddRange(second.Where(r => r != null));
            return new Reaction2 { reactions = list.ToArray() };
        }

        /// <summary>The runtime ZoneID of the old Zone (Zone.GetID)</summary>
        private static int OldZoneID(Zone zone) => zone.zoneType switch
        {
            ZoneType.Mode => zone.modeID != null ? zone.modeID.ID : -1,
            ZoneType.State => zone.stateID != null ? zone.stateID.ID : -1,
            ZoneType.Stance => zone.stanceID != null ? zone.stanceID.ID : -1,
            ZoneType.Force => 100,
            _ => zone.zoneID.Value,
        };

        private static IEnumerable<string> Warnings(Zone zone)
        {
            switch (zone.zoneType)
            {
                case ZoneType.Mode:
                    if (zone.modeID == null) yield return "Mode Zone without a Mode ID. No Mode Reaction was created.";
                    else if (zone.modeID.ID == 4 && zone.ShowActionID && zone.ActionID == null) yield return "Action Zone without an Action ID (the old Zone disabled itself).";
                    if (zone.ForceMode) yield return "Force Mode → Mode Reaction [ForceActivate]: it always counts as activated.";
                    break;
                case ZoneType.State:
                    if (zone.stateID == null) yield return "State Zone without a State ID.";
                    break;
                case ZoneType.Stance:
                    if (zone.stanceID == null) yield return "Stance Zone without a Stance ID.";
                    break;
            }
        }

        //--------------------------------------------------------------------------------------- Helpers

        /// <summary>Adds a Zone2 undoing the side effects of Zone2.Reset: Layer change (Ignore Raycast),
        /// an extra BoxCollider when the GameObject had none, and the Trigger/Enabled changes on the existing colliders</summary>
        private static Zone2 AddZone2(GameObject go, bool undo)
        {
            var layer = go.layer;
            var colliders = go.GetComponents<Collider>();
            var trigger = colliders.Select(c => c.isTrigger).ToArray();
            var enabled = colliders.Select(c => c.enabled).ToArray();

            var zone2 = undo ? Undo.AddComponent<Zone2>(go) : go.AddComponent<Zone2>();

            foreach (var c in go.GetComponents<Collider>())
            {
                if (colliders.Contains(c)) continue;
                if (undo) Undo.DestroyObjectImmediate(c); else Object.DestroyImmediate(c, true);
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].isTrigger = trigger[i];
                colliders[i].enabled = enabled[i];
            }

            go.layer = layer;
            return zone2;
        }

        /// <summary>The Zone belongs to this file (not inherited from a Base or Nested prefab)</summary>
        private static bool IsOwned(Component c) => !PrefabUtility.IsPartOfPrefabInstance(c) || PrefabUtility.IsAddedComponentOverride(c);

        /// <summary>The Zone2 that matches this Zone (same GameObject, same order)</summary>
        private static Zone2 PairedZone2(Zone zone)
        {
            var zones = zone.GetComponents<Zone>();
            var zones2 = zone.GetComponents<Zone2>();
            var index = System.Array.IndexOf(zones, zone);
            return index >= 0 && index < zones2.Length ? zones2[index] : null;
        }

        /// <summary>The inherited Zone has Property overrides in this file (Variant or Prefab instance)</summary>
        private static bool HasOverrides(Component c)
        {
            //Overrides of nested prefabs are stored on the outermost instance, so check both roots
            var mods = new List<PropertyModification>();
            var nearest = PrefabUtility.GetNearestPrefabInstanceRoot(c);
            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(c);
            if (nearest) mods.AddRange(PrefabUtility.GetPropertyModifications(nearest) ?? new PropertyModification[0]);
            if (outermost && outermost != nearest) mods.AddRange(PrefabUtility.GetPropertyModifications(outermost) ?? new PropertyModification[0]);
            if (mods.Count == 0) return false;

            var sources = new HashSet<Object>();
            for (var s = PrefabUtility.GetCorrespondingObjectFromSource(c); s != null; s = PrefabUtility.GetCorrespondingObjectFromSource(s))
                sources.Add(s);

            return mods.Any(m => m.target != null && sources.Contains(m.target));
        }

        /// <summary>Every Object Reference (fields, UnityEvent targets) pointing to the Zone now points to the Zone2</summary>
        private static void RetargetReferences(List<GameObject> roots, Zone zone, Zone2 zone2, StringBuilder log, string path, bool undo = false)
        {
            foreach (var comp in roots.SelectMany(r => r.GetComponentsInChildren<Component>(true)))
            {
                if (comp == null || comp == zone) continue;

                var so = new SerializedObject(comp);
                var it = so.GetIterator();
                bool changed = false;

                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue != zone) continue;

                    if (it.type.Contains("$Zone>")) //Field typed as Zone. Cannot hold a Zone2
                    {
                        log.AppendLine($"  ⚠ {path} → [{HierarchyPath(comp)}] {comp.GetType().Name}.{it.propertyPath} is typed [Zone]. Update the script to use Zone2 or IZone.");
                        continue;
                    }

                    if (it.propertyPath.EndsWith(".m_Target"))
                    {
                        var method = so.FindProperty(it.propertyPath.Replace(".m_Target", ".m_MethodName"))?.stringValue;
                        if (!string.IsNullOrEmpty(method) && typeof(Zone2).GetMember(method).Length == 0)
                            log.AppendLine($"  ⚠ {path} → [{HierarchyPath(comp)}] UnityEvent calls Zone.{method} which does not exist on Zone2.");

                        var typeName = so.FindProperty(it.propertyPath.Replace(".m_Target", ".m_TargetAssemblyTypeName"));
                        if (typeName != null) typeName.stringValue = $"{typeof(Zone2).FullName}, {typeof(Zone2).Assembly.GetName().Name}";
                    }

                    it.objectReferenceValue = zone2;
                    changed = true;
                }

                if (changed)
                {
                    if (undo) Undo.RecordObject(comp, "Migrate Zone to Zone2");
                    if (undo) so.ApplyModifiedProperties(); else so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
                }
            }
        }

        private static string HierarchyPath(Component c)
        {
            var t = c.transform;
            var path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}
#endif