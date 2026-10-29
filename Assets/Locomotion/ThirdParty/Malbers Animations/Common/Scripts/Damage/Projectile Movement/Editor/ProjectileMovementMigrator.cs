// Last Change: 9/17/2026 6:05pm
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MalbersAnimations.Weapons
{
    /// <summary> One-shot bake of the legacy [projectileType] enum into a [ProjectileMovement] reference.
    /// MProjectile migrates itself lazily at Awake and OnValidate, but running this once writes the result into every
    /// prefab and scene, so the legacy field can be deleted in a later version without silently resetting anyone's setup </summary>
    public static class ProjectileMovementMigrator
    {
        private const string Menu = "Tools/Malbers Animations/Migrate Projectiles To Movements";

        [MenuItem(Menu)]
        private static void Migrate()
        {
            //MWC: OnImpact Behaviour - the same pass now also bakes the legacy Impact Behaviour enum
            if (!EditorUtility.DisplayDialog("Migrate Projectiles",
                "This walks every prefab and scene in the project and bakes each MProjectile's legacy Projectile Type " +
                "into a Movement reference, its legacy Impact Behaviour into an On Impact reference, and its legacy " +
                "Rotation (including Rolling's Align To Ground) into a Rotation reference.\n\n" +
                "Projectiles already on the new format are left alone.\n\n" +
                "Make sure your work is committed first.", "Migrate", "Cancel"))
                return;

            var prefabs = MigratePrefabs();
            var scenes = MigrateScenes();

            AssetDatabase.SaveAssets();

            Debug.Log($"[Projectile Migration] Done. {prefabs} projectile(s) migrated in prefabs, {scenes} in scenes.");
        }

        /// <summary> Bakes the movement into every MProjectile found on a prefab asset </summary>
        private static int MigratePrefabs()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab");
            var migrated = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);

                if (EditorUtility.DisplayCancelableProgressBar("Migrating Projectiles", path, (float)i / guids.Length))
                    break;

                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                var projectiles = root.GetComponentsInChildren<MProjectile>(true);
                if (projectiles.Length == 0) continue;

                var dirty = false;

                foreach (var p in projectiles)
                    if (TryMigrate(p)) dirty = true;

                if (dirty)
                {
                    EditorUtility.SetDirty(root);
                    migrated += projectiles.Length;
                }
            }

            EditorUtility.ClearProgressBar();
            return migrated;
        }

        /// <summary> Bakes the movement into every MProjectile placed directly in a scene </summary>
        private static int MigrateScenes()
        {
            var guids = AssetDatabase.FindAssets("t:Scene");
            var migrated = 0;

            //Reopen whatever the user had open when this finishes
            var openScene = SceneManager.GetActiveScene().path;

            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);

                //Package and read-only scenes cannot be written back
                if (!path.StartsWith("Assets/")) continue;

                if (EditorUtility.DisplayCancelableProgressBar("Migrating Projectiles", path, (float)i / guids.Length))
                    break;

                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var dirty = false;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var p in root.GetComponentsInChildren<MProjectile>(true))
                    {
                        if (TryMigrate(p))
                        {
                            EditorUtility.SetDirty(p);
                            dirty = true;
                            migrated++;
                        }
                    }
                }

                if (dirty) EditorSceneManager.SaveScene(scene);
            }

            EditorUtility.ClearProgressBar();

            if (!string.IsNullOrEmpty(openScene)) EditorSceneManager.OpenScene(openScene, OpenSceneMode.Single);

            return migrated;
        }

        /// <summary> Projectile Rotation: the SerializedObject duplication is gone. MProjectile.Migrate() is the
        /// single migration path (movement, impact AND rotation), used by Awake/OnValidate and this tool alike </summary>
        private static bool TryMigrate(MProjectile projectile)
        {
            if (!projectile.Migrate()) return false;

            EditorUtility.SetDirty(projectile);
            return true;
        }
    }
}
#endif
