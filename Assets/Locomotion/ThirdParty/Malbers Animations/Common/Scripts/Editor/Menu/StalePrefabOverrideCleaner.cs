// Last Change: 9/19/2026 5:06pm
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MalbersAnimations
{
    // Locates & cleans stale WHOLE-FIELD prefab overrides.
    // When a serialized field changes type from a leaf (enum/int/bool…) to a class (BoolReference, a
    // [Serializable] struct, a [SerializeReference]…), any prefab instance that had overridden that field
    // keeps a PropertyModification on the bare field path (e.g. "autoEquip = 1"). Unity can no longer apply
    // a leaf value to a class-typed property and logs "Unsupported type <Class>" (no stack trace) every time
    // the instance re-merges — i.e. every time the source prefab is edited. The valid leaf overrides
    // ("autoEquip.ConstantValue") are untouched by this tool.
    public static class StalePrefabOverrideCleaner
    {
        private const string kMenu = "Tools/Malbers Animations/Tools/Stale Prefab Overrides/";
        private const string kTag = "[Stale Overrides]";

        // ───────────────────────────────── SCAN (read-only) ─────────────────────────────────

        /// <summary>Scan every prefab instance in the open scene(s) (or the open prefab stage) and log stale whole-field overrides</summary>
        [MenuItem(kMenu + "Scan Open Scene(s)", false, 540)]
        private static void ScanOpenScenes()
        {
            int stale = 0, instances = 0;
            foreach (var root in OpenContextInstanceRoots()) { instances++; stale += Report(root); }
            Summary("open scene(s)", stale, instances);
            if (stale == 0) Debug.Log($"{kTag} If 'Unsupported type <X>' persists, the stale override may live in another scene or inside a prefab asset — run 'Scan Project (Scenes + Prefabs)'.");
        }

        /// <summary>Scan the selected objects (deep: includes children) for stale whole-field overrides</summary>
        [MenuItem(kMenu + "Scan Selection", false, 541)]
        private static void ScanSelection()
        {
            int stale = 0, instances = 0;
            foreach (var root in SelectedInstanceRoots()) { instances++; stale += Report(root); }
            Summary("selection", stale, instances);
        }

        /// <summary>Scan every scene and prefab under Assets/ (scenes are opened additively and closed again)</summary>
        [MenuItem(kMenu + "Scan Project (Scenes + Prefabs)", false, 542)]
        private static void ScanProject() => ProjectPass(clean: false);

        // ───────────────────────────────── CLEAN (destructive) ─────────────────────────────────

        /// <summary>Remove stale whole-field overrides from every prefab instance in the open scene(s) / prefab stage and save</summary>
        [MenuItem(kMenu + "Clean Open Scene(s)", false, 560)]
        private static void CleanOpenScenes()
        {
            if (!Confirm("every prefab instance in the open scene(s)")) return;

            int cleaned = 0;
            foreach (var root in OpenContextInstanceRoots()) cleaned += Clean(root);

            if (cleaned > 0) SaveOpenContext();
            Debug.Log($"{kTag} Cleaned {cleaned} stale override(s) from the open scene(s)." + (cleaned > 0 ? " Saved." : ""));
        }

        /// <summary>Remove stale whole-field overrides from the selected prefab instances (scene objects or prefab-stage objects)</summary>
        [MenuItem(kMenu + "Clean Selection", false, 561)]
        private static void CleanSelection()
        {
            if (!Confirm("the selected prefab instance(s) and their children")) return;

            int cleaned = 0;
            foreach (var root in SelectedInstanceRoots()) cleaned += Clean(root);

            if (cleaned > 0) SaveOpenContext();
            Debug.Log($"{kTag} Cleaned {cleaned} stale override(s) from the selection." + (cleaned > 0 ? " Saved." : ""));
        }

        /// <summary>Remove stale whole-field overrides from every scene and prefab under Assets/ and save each touched asset</summary>
        [MenuItem(kMenu + "Clean Project (Scenes + Prefabs)", false, 562)]
        private static void CleanProject()
        {
            if (!Confirm("EVERY scene and prefab under Assets/ (each touched asset is saved)")) return;
            ProjectPass(clean: true);
        }

        // ───────────────────────────────── project pass ─────────────────────────────────

        /// <summary>Walk every scene + prefab under Assets/; report (and optionally clean + save) stale overrides</summary>
        private static void ProjectPass(bool clean)
        {
            var scenes = GuidsToAssetPaths(AssetDatabase.FindAssets("t:Scene"));
            var prefabs = GuidsToAssetPaths(AssetDatabase.FindAssets("t:Prefab"));
            int total = scenes.Count + prefabs.Count, done = 0, stale = 0, instances = 0, touchedAssets = 0;
            string title = clean ? "Cleaning stale prefab overrides" : "Scanning for stale prefab overrides";

            try
            {
                foreach (var path in prefabs)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(title, path, (float)done++ / total)) return;
                    int n = PrefabAssetPass(path, clean, ref instances);
                    stale += n;
                    if (clean && n > 0) touchedAssets++;
                }

                foreach (var path in scenes)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(title, path, (float)done++ / total)) return;
                    int n = ScenePass(path, clean, ref instances);
                    stale += n;
                    if (clean && n > 0) touchedAssets++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            if (clean)
                Debug.Log($"{kTag} Project clean complete — removed {stale} stale override(s) across {touchedAssets} asset(s) ({instances} prefab instances inspected).");
            else
                Summary($"project ({scenes.Count} scenes, {prefabs.Count} prefabs)", stale, instances);
        }

        /// <summary>Load a prefab's contents in isolation, inspect its nested / variant instance roots, save back when cleaning</summary>
        private static int PrefabAssetPass(string path, bool clean, ref int instances)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            if (contents == null) return 0;
            int n = 0;
            try
            {
                foreach (var root in InstanceRootsUnder(contents))
                {
                    instances++;
                    n += clean ? Clean(root, path) : Report(root, path);
                }
                if (clean && n > 0) PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return n;
        }

        /// <summary>Inspect one scene: in place when already open, else opened additively and closed afterwards</summary>
        private static int ScenePass(string path, bool clean, ref int instances)
        {
            var scene = SceneManager.GetSceneByPath(path);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            if (!scene.IsValid() || !scene.isLoaded) return 0;

            int n = 0;
            try
            {
                foreach (var goRoot in scene.GetRootGameObjects())
                    foreach (var root in InstanceRootsUnder(goRoot))
                    {
                        instances++;
                        n += clean ? Clean(root, path) : Report(root, path);
                    }

                if (clean && n > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally { if (!wasOpen) EditorSceneManager.CloseScene(scene, true); }
            return n;
        }

        // ───────────────────────────────── core ─────────────────────────────────

        /// <summary>True when the modification stores a leaf value against a property that is now a class/struct (Generic). Paths that no longer resolve, array growth and [SerializeReference] rids are all valid and skipped</summary>
        private static bool IsStale(PropertyModification m, out string typeName)
        {
            typeName = null;
            if (m.target == null) return false; // target itself missing → not ours to judge
            if (m.target is MonoBehaviour mb && MonoScript.FromMonoBehaviour(mb) == null) return false; // missing script: layout unknown, leave alone
            if (m.propertyPath.StartsWith("managedReferences[")) return false; // SerializeReference storage, never a whole-field leaf

            using var so = new SerializedObject(m.target);
            var p = so.FindProperty(m.propertyPath);
            if (p == null || p.propertyType != SerializedPropertyType.Generic) return false;

            typeName = p.type;
            return true;
        }

        /// <summary>Log every stale modification on one prefab instance root; returns the stale count</summary>
        private static int Report(GameObject root, string assetPath = null)
        {
            var mods = PrefabUtility.GetPropertyModifications(root);
            if (mods == null || mods.Length == 0) return 0;

            var sb = new StringBuilder();
            int stale = 0;
            foreach (var m in mods)
            {
                if (!IsStale(m, out var typeName)) continue;
                stale++;
                sb.Append($"\n     • '{m.propertyPath}' = '{m.value}'   on {m.target.GetType().Name} ({m.target.name})   → property is now '{typeName}'");
            }

            if (stale == 0) return 0;
            Debug.LogWarning($"{kTag} {stale} stale whole-field override(s) on  ▶ {Where(root, assetPath)}{sb}", root);
            return stale;
        }

        /// <summary>Strip stale whole-field modifications from one prefab instance root; every other override is kept. Returns the removed count</summary>
        private static int Clean(GameObject root, string assetPath = null)
        {
            var mods = PrefabUtility.GetPropertyModifications(root);
            if (mods == null || mods.Length == 0) return 0;

            var keep = new List<PropertyModification>(mods.Length);
            var sb = new StringBuilder();
            foreach (var m in mods)
            {
                if (IsStale(m, out var typeName))
                    sb.Append($"\n     • removed '{m.propertyPath}' = '{m.value}'   on {m.target.GetType().Name} ({m.target.name})   ('{typeName}')");
                else keep.Add(m);
            }

            int removed = mods.Length - keep.Count;
            if (removed == 0) return 0;

            Undo.RegisterCompleteObjectUndo(root, "Clean stale prefab overrides");
            PrefabUtility.SetPropertyModifications(root, keep.ToArray());
            EditorUtility.SetDirty(root);
            Debug.Log($"{kTag} Removed {removed} stale override(s) on  {Where(root, assetPath)}{sb}", root);
            return removed;
        }

        // ───────────────────────────────── helpers ─────────────────────────────────

        private static bool Confirm(string scope) => EditorUtility.DisplayDialog(
            "Clean Stale Prefab Overrides",
            $"This removes every prefab override that stores a leaf value on a field that is now a class " +
            $"(the source of 'Unsupported type <X>' console errors) from {scope}.\n\n" +
            "Every other override (e.g. 'field.ConstantValue', array growth, [SerializeReference] entries) is left untouched. " +
            "Scene objects support Undo; project-wide cleaning saves assets and is NOT undoable.\n\nContinue?",
            "Clean", "Cancel");

        /// <summary>Log the scan totals in one line</summary>
        private static void Summary(string scope, int stale, int instances)
        {
            var msg = $"{kTag} Scan of the {scope} complete — {stale} stale whole-field override(s) across {instances} prefab instance(s).";
            if (stale > 0) Debug.LogWarning(msg); else Debug.Log(msg);
        }

        /// <summary>Save whatever context is open: the prefab stage or the open scenes</summary>
        private static void SaveOpenContext()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                EditorSceneManager.MarkSceneDirty(stage.scene);
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
            }
            else
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (s.isLoaded) EditorSceneManager.MarkSceneDirty(s);
                }
                EditorSceneManager.SaveOpenScenes();
            }
        }

        /// <summary>Outermost prefab instance roots in the open prefab stage, or in every loaded scene</summary>
        private static IEnumerable<GameObject> OpenContextInstanceRoots()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                foreach (var r in InstanceRootsUnder(stage.prefabContentsRoot)) yield return r;
                yield break;
            }

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var r in InstanceRootsUnder(root)) yield return r;
            }
        }

        /// <summary>Outermost prefab instance roots under the selected GameObjects (deep)</summary>
        private static IEnumerable<GameObject> SelectedInstanceRoots()
        {
            var seen = new HashSet<GameObject>();
            foreach (var o in Selection.gameObjects)
                foreach (var r in InstanceRootsUnder(o))
                    if (seen.Add(r)) yield return r;
        }

        /// <summary>Every outermost prefab instance root in a hierarchy (nested instances belong to their outer prefab asset, so they are skipped here)</summary>
        private static IEnumerable<GameObject> InstanceRootsUnder(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) yield return t.gameObject;
        }

        /// <summary>Asset paths for the given GUIDs, restricted to the Assets/ folder (packages are read-only)</summary>
        private static List<string> GuidsToAssetPaths(string[] guids)
        {
            var list = new List<string>(guids.Length);
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.StartsWith("Assets/")) list.Add(p);
            }
            return list;
        }

        /// <summary>Human-readable location for logs</summary>
        private static string Where(GameObject go, string assetPath = null)
        {
            var src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            var ctx = assetPath ?? go.scene.path;
            return $"{HierarchyPath(go.transform)}  [instance of {System.IO.Path.GetFileName(src)}]{(string.IsNullOrEmpty(ctx) ? "" : $"  ({ctx})")}";
        }

        private static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
            return sb.ToString();
        }
    }
}
#endif
