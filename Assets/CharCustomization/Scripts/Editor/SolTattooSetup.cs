using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTools
{
    // Tattoo setup for the demo character:
    // - Create or Update Tattoo Catalog: DefaultTattooCatalog from the T_Tattoo_<Name>
    //   designs, keeping edits made to existing entries (matched by ID).
    // - Add Test Tattoo: a CharacterTattoos beside the selected character's profile and,
    //   on the left forearm of each sex, a projector with the wolf design facing the
    //   character's front. Move, rotate and scale the projector in the Scene view, in
    //   Play Mode too, to place the tattoo; duplicate it under any bone for more.
    // - Save Projector Tattoos: turns the active character's projectors into saved
    //   placements, which recipes and presets keep, and removes the projectors.
    public static class SolTattooSetup
    {
        private const string BakeShaderPath = "Assets/CharCustomization/Shaders/SolTattooBake.shader";
        private const string DesignFolder = "Assets/CharCustomization/Resources/Tattoos/Designs";
        private const string DesignPrefix = "T_Tattoo_";
        private const string CatalogPath = "Assets/CharCustomization/CustomizationPresets/DefaultTattooCatalog.asset";
        private const string TestDesignPath = DesignFolder + "/T_Tattoo_Wolf.png";
        private const string ProjectorName = "Tattoo (Test)";
        // Metres: the design's width and height, and how deep the box reaches.
        private const float TattooSize = 0.11f;
        private const float ProjectionDepth = 0.12f;

        [MenuItem("Tools/Sol/Character Customization/Tattoos/Create or Update Tattoo Catalog")]
        private static void CreateCatalogMenu()
        {
            CharacterTattooCatalog catalog = CreateOrUpdateCatalog();
            Debug.Log($"{CatalogPath} has {catalog.Designs.Count} tattoo designs.", catalog);
        }

        public static CharacterTattooCatalog CreateOrUpdateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterTattooCatalog>(CatalogPath);
            bool created = catalog == null;
            if (created)
            {
                catalog = ScriptableObject.CreateInstance<CharacterTattooCatalog>();
            }

            var designs = new List<CharacterTattooDesign>(catalog.Designs.Where(design => design != null));
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { DesignFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(DesignPrefix, StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal);
            foreach (string path in paths)
            {
                string name = Path.GetFileNameWithoutExtension(path).Substring(DesignPrefix.Length);
                string id = DesignId(name);
                if (designs.Any(design => design.Id == id))
                {
                    continue;
                }

                designs.Add(new CharacterTattooDesign(id, DesignLabel(name), AssetDatabase.LoadAssetAtPath<Texture2D>(path)));
            }

            catalog.ConfigureDesigns(designs);
            if (created)
            {
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            else
            {
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            return catalog;
        }

        // RuneTentacles becomes rune-tentacles, the ID recipes store.
        public static string DesignId(string name)
        {
            return Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", "-").ToLowerInvariant();
        }

        private static string DesignLabel(string name)
        {
            return Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
        }

        [MenuItem("Tools/Sol/Character Customization/Tattoos/Save Projector Tattoos On Selected Character")]
        private static void SaveProjectorTattoos()
        {
            CharacterProfile profile = FindProfile(Selection.activeGameObject);
            CharacterTattoos tattoos = profile != null ? profile.GetComponent<CharacterTattoos>() : null;
            if (tattoos == null || profile.Controller == null || profile.Controller.ActiveCharacterRoot == null)
            {
                Debug.LogError("Select a character with a CharacterProfile, morph controller and CharacterTattoos.");
                return;
            }

            Undo.RecordObject(tattoos, "Save Projector Tattoos");
            int saved = 0;
            foreach (CharacterTattooProjector projector in profile.Controller.ActiveCharacterRoot
                         .GetComponentsInChildren<CharacterTattooProjector>(true))
            {
                if (!tattoos.TryCapture(projector, out CharacterTattooPlacement placement, out string error))
                {
                    Debug.LogWarning($"Not saved: {error}", projector);
                    continue;
                }

                tattoos.AddPlacement(placement);
                Undo.DestroyObjectImmediate(projector.gameObject);
                saved++;
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(tattoos);
            Debug.Log($"Saved {saved} projector tattoos on '{profile.Controller.ActiveCharacterRoot.name}'; "
                      + $"the character now has {tattoos.Placements.Count}. Recipes captured from it keep them.", tattoos);
        }

        [MenuItem("Tools/Sol/Character Customization/Tattoos/Add Test Tattoo To Selected Character")]
        private static void AddTestTattoo()
        {
            CharacterProfile profile = FindProfile(Selection.activeGameObject);
            if (profile == null || profile.Controller == null)
            {
                Debug.LogError("Select a character with a CharacterProfile and morph controller to add a test tattoo.");
                return;
            }

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(BakeShaderPath);
            var design = AssetDatabase.LoadAssetAtPath<Texture2D>(TestDesignPath);
            if (shader == null || design == null)
            {
                Debug.LogError($"Missing {BakeShaderPath} or {TestDesignPath}.");
                return;
            }

            ConfigureTattoos(profile);

            GameObject shown = null;
            foreach (GameObject root in new[] { profile.Controller.FemaleRoot, profile.Controller.MaleRoot })
            {
                if (root == null)
                {
                    continue;
                }

                GameObject projector = AddForearmProjector(root, design, out string error);
                if (projector == null)
                {
                    Debug.LogWarning($"No test tattoo on '{root.name}': {error}", root);
                }
                else if (root.activeInHierarchy || shown == null)
                {
                    shown = projector;
                }
            }

            if (shown != null)
            {
                Selection.activeGameObject = shown;
                Debug.Log($"Added a test tattoo under '{shown.transform.parent.name}'. Enter Play Mode to see it; move the selected projector to place it.", shown);
            }
        }

        // A CharacterTattoos beside the profile, with its controller, catalog and the bake
        // shader (referenced so builds include it), and the profile pointing at it. A
        // catalog already assigned is kept.
        private static CharacterTattoos ConfigureTattoos(CharacterProfile profile)
        {
            CharacterTattoos tattoos = profile.GetComponent<CharacterTattoos>();
            if (tattoos == null)
            {
                tattoos = Undo.AddComponent<CharacterTattoos>(profile.gameObject);
            }

            var serializedProfile = new SerializedObject(profile);
            var serializedTattoos = new SerializedObject(tattoos);
            serializedTattoos.FindProperty("controller").objectReferenceValue =
                serializedProfile.FindProperty("controller").objectReferenceValue;
            serializedTattoos.FindProperty("bakeShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(BakeShaderPath);
            SerializedProperty catalog = serializedTattoos.FindProperty("catalog");
            if (catalog.objectReferenceValue == null)
            {
                catalog.objectReferenceValue = CreateOrUpdateCatalog();
            }

            serializedProfile.FindProperty("tattoos").objectReferenceValue = tattoos;
            serializedTattoos.ApplyModifiedProperties();
            serializedProfile.ApplyModifiedProperties();

            return tattoos;
        }

        private static CharacterProfile FindProfile(GameObject selected)
        {
            if (selected == null)
            {
                return null;
            }

            CharacterProfile profile = selected.GetComponentInParent<CharacterProfile>(true);
            if (profile == null)
            {
                profile = selected.GetComponentInChildren<CharacterProfile>(true);
            }

            if (profile != null)
            {
                return profile;
            }

            // A projector or bone is selected: the rigs need not sit under the profile.
            foreach (CharacterProfile candidate in UnityEngine.Object.FindObjectsByType<CharacterProfile>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                CharacterMorphController controller = candidate.Controller;
                if (controller != null && (IsUnder(selected, controller.FemaleRoot) || IsUnder(selected, controller.MaleRoot)))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsUnder(GameObject selected, GameObject root)
        {
            return root != null && selected.transform.IsChildOf(root.transform);
        }

        private static GameObject AddForearmProjector(GameObject root, Texture2D design, out string error)
        {
            SkinnedMeshRenderer skin = FindSkin(root);
            if (skin == null)
            {
                error = "no skinned renderer with a Sol/Character Skin material.";
                return null;
            }

            Transform forearm = FindLeftBone(skin.bones, "forearm_stretch", "forearm");
            Transform hand = FindLeftBone(skin.bones, "hand", "hand");
            if (forearm == null || hand == null)
            {
                error = "could not find the left forearm and hand bones.";
                return null;
            }

            Transform existing = forearm.Find(ProjectorName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            Vector3 elbow = forearm.position;
            Vector3 wrist = hand.position;
            Vector3 axis = (wrist - elbow).normalized;
            Vector3 front = Vector3.ProjectOnPlane(root.transform.forward, axis);
            if (front.sqrMagnitude < 1e-6f)
            {
                front = Vector3.ProjectOnPlane(root.transform.up, axis);
            }

            var projector = new GameObject(ProjectorName);
            Undo.RegisterCreatedObjectUndo(projector, "Add Test Tattoo");
            projector.transform.SetParent(forearm, false);
            // The box sits on the bone and projects back into the arm's front, with the
            // design's top toward the elbow. The far side of the arm faces away and is
            // left bare.
            projector.transform.SetPositionAndRotation(
                (elbow + wrist) * 0.5f,
                Quaternion.LookRotation(-front.normalized, -axis));
            Vector3 parentScale = forearm.lossyScale;
            float scale = Mathf.Max((Mathf.Abs(parentScale.x) + Mathf.Abs(parentScale.y) + Mathf.Abs(parentScale.z)) / 3f, 1e-6f);
            projector.transform.localScale = new Vector3(TattooSize, TattooSize, ProjectionDepth) / scale;

            var tattoo = projector.AddComponent<CharacterTattooProjector>();
            tattoo.Configure(design, new Color(0.09f, 0.1f, 0.12f, 1f), 0.9f);
            error = null;
            return projector;
        }

        private static SkinnedMeshRenderer FindSkin(GameObject root)
        {
            SkinnedMeshRenderer skin = null;
            foreach (SkinnedMeshRenderer candidate in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (candidate.sharedMesh == null
                    || Array.Find(candidate.sharedMaterials, SolSkinShaderIds.IsSkinMaterial) == null)
                {
                    continue;
                }

                if (skin == null || candidate.sharedMesh.vertexCount > skin.sharedMesh.vertexCount)
                {
                    skin = candidate;
                }
            }

            return skin;
        }

        // The left bone named exactly '<preferred>.l', else any left bone whose name
        // contains the fallback and is not a twist bone.
        private static Transform FindLeftBone(Transform[] bones, string preferred, string fallback)
        {
            Transform match = null;
            foreach (Transform bone in bones)
            {
                if (bone == null)
                {
                    continue;
                }

                string boneName = bone.name.ToLowerInvariant();
                if (boneName == preferred + ".l" || boneName == preferred + "_l")
                {
                    return bone;
                }

                bool left = boneName.EndsWith(".l") || boneName.EndsWith("_l") || boneName.Contains("left");
                if (match == null && left && boneName.Contains(fallback) && !boneName.Contains("twist"))
                {
                    match = bone;
                }
            }

            return match;
        }
    }
}
