// Last Change: 9/17/2026 5:33pm
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MalbersAnimations
{
    // MWC — Locates & cleans [SerializeReference] "managed reference missing type" entries.
    //        These produce the "Unsupported type <X>" warning on scene/prefab save (e.g. a residual
    //        Reaction2 that was refactored from a class into a struct). The bad type name is NOT stored
    //        in the .unity/.prefab YAML (it is blanked out), so text search can't find it — only Unity's
    //        SerializationUtility missing-types registry knows it. This tool reports the exact object and
    //        clears the stale references.
    public static class MissingManagedReferenceCleaner
    {
        private const string kMenu = "Tools/Malbers Animations/Tools/Missing Managed References/";
        private const string kTag = "[Missing Managed Refs]";

        // ───────────────────────────────── SCAN (read-only) ─────────────────────────────────

        // MWC — Scan every component in the open scene(s) and log where missing types live.
        [MenuItem(kMenu + "Scan Open Scene(s)", false, 510)]
        private static void ScanOpenScenes()
        {
            int found = 0, scanned = 0;
            foreach (var comp in OpenSceneComponents()) { scanned++; found += Report(comp); }
            Debug.Log($"{kTag} Scan complete — {found} missing-type reference(s) across {scanned} components in the open scene(s).");
            if (found == 0) Debug.Log($"{kTag} The open scene is clean. If the warning persists on save, the source is inside a PREFAB asset — run 'Scan Project (Prefabs + Assets)'.");
        }

        // MWC — Scan the currently selected objects/assets (deep: includes children).
        [MenuItem(kMenu + "Scan Selection", false, 511)]
        private static void ScanSelection()
        {
            int found = 0;
            foreach (var o in SelectedObjectsDeep()) found += Report(o);
            Debug.Log($"{kTag} Selection scan complete — {found} missing-type reference(s) found.");
        }

        // MWC — Scan ALL prefabs and ScriptableObjects in the project to find the true source asset.
        [MenuItem(kMenu + "Scan Project (Prefabs + Assets)", false, 512)]
        private static void ScanProject()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab t:ScriptableObject");
            int found = 0;
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (EditorUtility.DisplayCancelableProgressBar("Scanning project for missing managed references",
                        path, (float)i / guids.Length)) break;

                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                        found += Report(o, path);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            Debug.Log($"{kTag} Project scan complete — {found} missing-type reference(s) found across {guids.Length} assets.");
        }

        // ───────────────────────────────── CLEAN (destructive) ─────────────────────────────────

        // MWC — Clear all missing-type references from the open scene(s) and save.
        [MenuItem(kMenu + "Clean Open Scene(s)", false, 530)]
        private static void CleanOpenScenes()
        {
            if (!Confirm("every object in the open scene(s)")) return;

            int cleaned = 0;
            foreach (var comp in OpenSceneComponents()) cleaned += Clean(comp);

            if (cleaned > 0)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));
                EditorSceneManager.SaveOpenScenes();
            }
            Debug.Log($"{kTag} Cleaned {cleaned} reference(s) from the open scene(s)." + (cleaned > 0 ? " Scene(s) saved." : ""));
        }

        // MWC — Clear missing-type references from the current selection (scene objects OR the open prefab stage).
        [MenuItem(kMenu + "Clean Selection", false, 531)]
        private static void CleanSelection()
        {
            if (!Confirm("the selected object(s) and their children")) return;

            int cleaned = 0;
            foreach (var o in SelectedObjectsDeep()) cleaned += Clean(o);

            // Save whatever context we are in (prefab stage or scene). MWC
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (cleaned > 0)
            {
                if (stage != null) EditorSceneManager.MarkSceneDirty(stage.scene);
                else EditorSceneManager.MarkAllScenesDirty();
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"{kTag} Cleaned {cleaned} reference(s) from the selection." +
                      (cleaned > 0 ? (stage != null ? " Save the prefab (Ctrl+S) to persist." : " Scene marked dirty — save to persist.") : ""));
        }

        // ───────────────────────────────── helpers ─────────────────────────────────

        private static bool Confirm(string scope) => EditorUtility.DisplayDialog(
            "Clean Missing Managed References",
            $"This removes ALL [SerializeReference] entries whose type is missing/unsupported from {scope}.\n\n" +
            "Use this to clear residual data (e.g. the 'Unsupported type Reaction2' warning). This is NOT undoable.\n\nContinue?",
            "Clean", "Cancel");

        // MWC — Only MonoBehaviour / ScriptableObject support [SerializeReference]; the API throws on
        //        anything else (Transform, colliders, materials…), so filter before querying it.
        private static bool SupportsManagedRefs(Object obj) => obj is MonoBehaviour or ScriptableObject;

        // MWC — Report missing managed types on a single object; pings it in the console.
        private static int Report(Object obj, string assetPath = null)
        {
            if (!SupportsManagedRefs(obj) || !SerializationUtility.HasManagedReferencesWithMissingTypes(obj)) return 0;

            var missing = SerializationUtility.GetManagedReferencesWithMissingTypes(obj);
            var sb = new StringBuilder();
            sb.Append($"{kTag} {missing.Length} missing type(s) on  ▶ {Where(obj, assetPath)}");
            foreach (var m in missing)
                sb.Append($"\n     • class '{m.className}'   ns '{m.namespaceName}'   asm '{m.assemblyName}'   (rid {m.referenceId})");
            Debug.LogWarning(sb.ToString(), obj);
            return missing.Length;
        }

        // MWC — Clear + dirty a single object.
        private static int Clean(Object obj)
        {
            if (!SupportsManagedRefs(obj) || !SerializationUtility.HasManagedReferencesWithMissingTypes(obj)) return 0;
            int n = SerializationUtility.GetManagedReferencesWithMissingTypes(obj).Length;
            SerializationUtility.ClearAllManagedReferencesWithMissingTypes(obj); // MWC — API is plural "Types"
            EditorUtility.SetDirty(obj);
            Debug.Log($"{kTag} Cleared {n} on  {Where(obj)}", obj);
            return n;
        }

        // MWC — All components of every root object in every open (non-prefab-stage) scene, plus the prefab stage if open.
        private static IEnumerable<Object> OpenSceneComponents()
        {
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                foreach (var c in stage.prefabContentsRoot.GetComponentsInChildren<Component>(true))
                    if (c != null) yield return c;
                yield break;
            }

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var c in root.GetComponentsInChildren<Component>(true))
                        if (c != null) yield return c; // null == missing script; skip
            }
        }

        // MWC — Selection expanded to child components (works for scene objects and prefab-stage objects).
        private static IEnumerable<Object> SelectedObjectsDeep()
        {
            foreach (var o in Selection.objects)
            {
                if (o is GameObject go)
                {
                    foreach (var c in go.GetComponentsInChildren<Component>(true))
                        if (c != null) yield return c;
                }
                else if (o != null) yield return o; // ScriptableObject asset, etc.
            }
        }

        // MWC — Human-readable location for logs.
        private static string Where(Object obj, string assetPath = null)
        {
            if (obj is Component c && c != null)
            {
                var path = HierarchyPath(c.transform);
                var prefab = PrefabUtility.IsPartOfAnyPrefab(c) ? "  [prefab instance]" : "";
                var src = assetPath != null ? $"  ({assetPath})" : "";
                return $"{path} :: {c.GetType().Name}{prefab}{src}";
            }
            var apath = assetPath ?? AssetDatabase.GetAssetPath(obj);
            return $"{obj.name} ({obj.GetType().Name}){(string.IsNullOrEmpty(apath) ? "" : $"  ({apath})")}";
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
