#if UNITY_EDITOR

using MalbersAnimations.Controller;
using MalbersAnimations.Controller.AI;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MalbersAnimations.Menu
{
    /// <summary>Migration helpers for projects coming from the removed Animal "Player" flag (isPlayer / MAnimal.MainAnimal). Players are now identified by the Player Tag</summary>
    public static class PlayerTagMigrationTool
    {
        private const string MenuRoot = "Tools/Malbers Animations/Tools/";

        /// <summary>Finds the Player Tag asset, logging when it is missing</summary>
        private static Tag FindPlayerTag()
        {
            var playerTag = MTools.GetInstance<Tag>("Player");
            if (playerTag == null) Debug.LogError("[Player Tag Migration] The <B>Player</B> Tag asset was not found in the project.");
            return playerTag;
        }

        /// <summary>True when the Animal is driven by an enabled Input Source and has no active AI Brain, meaning it is a player character</summary>
        private static bool IsPlayerCharacter(MAnimal animal)
        {
            foreach (var brain in animal.GetComponentsInChildren<MAnimalBrain>(true))
                if (IsActiveBrain(brain)) return false;

            foreach (var brain in animal.GetComponentsInParent<MAnimalBrain>(true))
                if (IsActiveBrain(brain)) return false;

            var inputs = animal.GetComponentsInChildren<IInputSource>(true);
            foreach (var input in inputs)
            {
                if (input is Behaviour b && b.enabled) return true;
            }
            return false;
        }

        /// <summary>True when the Brain is enabled and lives on an active GameObject (a disabled AI child does not make the character an NPC)</summary>
        private static bool IsActiveBrain(MAnimalBrain brain) => brain != null && brain.enabled && brain.gameObject.activeSelf;

        /// <summary>True when a Tags component in the Animal hierarchy already carries the Player Tag</summary>
        private static bool HasPlayerTag(MAnimal animal, Tag playerTag)
        {
            foreach (var tags in animal.GetComponentsInChildren<Tags>(true))
            {
                if (tags.tags != null && tags.tags.Contains(playerTag)) return true;
            }
            return false;
        }

        /// <summary>Adds the Player Tag to the Animal root (new Tags component or the existing one). Returns true when something changed</summary>
        /// <param name="checkHeuristic">When true only input-driven Animals without an active Brain are tagged; the Selected mode trusts the selection and passes false</param>
        private static bool AddPlayerTag(MAnimal animal, Tag playerTag, bool checkHeuristic)
        {
            if (HasPlayerTag(animal, playerTag)) return false;
            if (checkHeuristic && !IsPlayerCharacter(animal)) return false;

            var tags = animal.GetComponent<Tags>();

            if (tags == null)
            {
                tags = Undo.AddComponent<Tags>(animal.gameObject);
            }
            else
            {
                Undo.RecordObject(tags, "Add Player Tag");
            }

            tags.tags ??= new List<Tag>();
            tags.tags.Add(playerTag);

            EditorUtility.SetDirty(tags);
            PrefabUtility.RecordPrefabInstancePropertyModifications(tags);
            return true;
        }

        [MenuItem(MenuRoot + "Add Player Tag to Players (Open Scenes)", false, 310)]
        private static void TagPlayersInOpenScenes()
        {
            var playerTag = FindPlayerTag();
            if (playerTag == null) return;

            var touched = new List<string>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                bool sceneChanged = false;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var animal in root.GetComponentsInChildren<MAnimal>(true))
                    {
                        if (AddPlayerTag(animal, playerTag, true))
                        {
                            touched.Add($"{scene.name}/{animal.name}");
                            sceneChanged = true;
                        }
                    }
                }

                if (sceneChanged) EditorSceneManager.MarkSceneDirty(scene);
            }

            Report("Player Tag added to", touched);
        }

        [MenuItem(MenuRoot + "Add Player Tag to Players (Selected)", false, 311)]
        private static void TagSelectedPlayers()
        {
            var playerTag = FindPlayerTag();
            if (playerTag == null) return;

            var touched = new List<string>();

            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;

                if (PrefabUtility.IsPartOfPrefabAsset(go))
                {
                    var path = AssetDatabase.GetAssetPath(go);
                    var contents = PrefabUtility.LoadPrefabContents(path);
                    bool changed = false;

                    foreach (var animal in contents.GetComponentsInChildren<MAnimal>(true))
                    {
                        if (AddPlayerTag(animal, playerTag, false))
                        {
                            touched.Add($"{path} ({animal.name})");
                            changed = true;
                        }
                    }

                    if (changed) PrefabUtility.SaveAsPrefabAsset(contents, path);
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                else
                {
                    foreach (var animal in go.GetComponentsInChildren<MAnimal>(true))
                    {
                        if (AddPlayerTag(animal, playerTag, false))
                        {
                            touched.Add($"{animal.gameObject.scene.name}/{animal.name}");
                            EditorSceneManager.MarkSceneDirty(animal.gameObject.scene);
                        }
                    }
                }
            }

            Report("Player Tag added to", touched);
        }

        [MenuItem(MenuRoot + "Convert Obsolete Look Decisions to Malbers Tag [Player]", false, 312)]
        private static void ConvertObsoleteLookDecisions()
        {
            var playerTag = FindPlayerTag();
            if (playerTag == null) return;

            var touched = new List<string>();
            var paths = new HashSet<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:MAIDecision")) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var guid in AssetDatabase.FindAssets("t:MAIState")) paths.Add(AssetDatabase.GUIDToAssetPath(guid)); //Decisions can live as sub-assets of an AI State

            foreach (var path in paths)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is LookDecision look && LookDecision.ConvertObsoletePlayerToTag(look, playerTag))
                        touched.Add($"{path} ({look.name})");
                }
            }

            if (touched.Count > 0) AssetDatabase.SaveAssets();

            Report("Look Decisions converted to Malbers Tag [Player]", touched);
        }

        /// <summary>Logs the list of changed objects, or that nothing needed changing</summary>
        private static void Report(string header, List<string> touched)
        {
            if (touched.Count == 0)
            {
                Debug.Log($"[Player Tag Migration] Nothing to change. {header} 0 objects.");
                return;
            }

            Debug.Log($"[Player Tag Migration] {header} {touched.Count} object(s):\n - {string.Join("\n - ", touched)}");
        }
    }
}
#endif