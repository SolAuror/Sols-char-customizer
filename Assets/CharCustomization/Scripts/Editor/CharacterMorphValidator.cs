using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace Sol.CharacterCustomization.Editor
{
    public static class CharacterMorphValidator
    {
        private const string DemoScenePath = "Assets/CharCustomization/Scenes/DemoScene.unity";
        private const string MenuPrefabPath = "Assets/CharCustomization/Prefabs/CharacterMorphMenu.prefab";
        private const string InputActionsPath = "Assets/CharCustomization/Scripts/InputSystem_Actions.inputactions";
        private static readonly HashSet<string> AllowedMenuOverridePaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "controller",
            "m_Name",
            "m_Pivot.x", "m_Pivot.y",
            "m_AnchorMax.x", "m_AnchorMax.y",
            "m_AnchorMin.x", "m_AnchorMin.y",
            "m_SizeDelta.x", "m_SizeDelta.y",
            "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z",
            "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.w", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z",
            "m_AnchoredPosition.x", "m_AnchoredPosition.y",
            "m_LocalEulerAnglesHint.x", "m_LocalEulerAnglesHint.y", "m_LocalEulerAnglesHint.z"
        };

        [MenuItem("Tools/Character Customization/Validate Morph Demo")]
        public static void ValidateDemo()
        {
            GameObject menuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath);
            if (menuPrefab == null)
            {
                throw new InvalidOperationException($"Missing menu prefab at '{MenuPrefabPath}'.");
            }

            ValidateMenu(menuPrefab);
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestoreSetup = previousSetup.Any(setup => setup.isLoaded && setup.isActive);
            try
            {
                Scene scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
                ValidateScene(scene);
            }
            finally
            {
                if (canRestoreSetup)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }

            Debug.Log("Character morph demo validation passed.");
        }

        public static void ValidateFromCommandLine()
        {
            try
            {
                ValidateDemo();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void ValidateMenu(GameObject menuPrefab)
        {
            CharacterMorphDemoUI menu = menuPrefab.GetComponent<CharacterMorphDemoUI>();
            if (menu == null)
            {
                throw new InvalidOperationException("The menu prefab has no CharacterMorphDemoUI component.");
            }

            CharacterMorphSliderRow[] rows = menuPrefab.GetComponentsInChildren<CharacterMorphSliderRow>(true);
            CharacterMorphSliderRow[] authored = rows.Where(row => !string.IsNullOrEmpty(row.MorphId)).ToArray();
            CharacterMorphSliderRow[] templates = rows.Where(row => string.IsNullOrEmpty(row.MorphId) && !row.gameObject.activeSelf).ToArray();
            string[] duplicates = authored.GroupBy(row => row.MorphId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            string[] missing = CharacterMorphCatalog.Definitions
                .Where(definition => authored.All(row => row.MorphId != definition.Id || !row.IsConfigured))
                .Select(definition => definition.Id).ToArray();

            if (authored.Length != CharacterMorphCatalog.Definitions.Count || templates.Length != 1 ||
                duplicates.Length > 0 || missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Invalid menu bindings. Authored: {authored.Length}, templates: {templates.Length}, " +
                    $"duplicates: {string.Join(", ", duplicates)}, missing: {string.Join(", ", missing)}");
            }

            var serializedMenu = new SerializedObject(menu);
            foreach (string propertyName in new[]
                     {
                         "content", "femaleButton", "maleButton", "femaleButtonImage", "maleButtonImage", "sliderRowTemplate"
                     })
            {
                if (serializedMenu.FindProperty(propertyName).objectReferenceValue == null)
                {
                    throw new InvalidOperationException($"Menu reference '{propertyName}' is not assigned.");
                }
            }
        }

        private static void ValidateScene(Scene scene)
        {
            CharacterMorphController controller = FindFirst<CharacterMorphController>(scene);
            CharacterMorphDemoUI menu = FindFirst<CharacterMorphDemoUI>(scene);
            InputSystemUIInputModule inputModule = FindFirst<InputSystemUIInputModule>(scene);
            InputActionAsset expectedActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (controller == null || menu == null || inputModule == null || expectedActions == null)
            {
                throw new InvalidOperationException("The demo scene is missing its controller, menu, UI input module, or input-actions asset.");
            }

            var serializedMenu = new SerializedObject(menu);
            if (serializedMenu.FindProperty("controller").objectReferenceValue != controller)
            {
                throw new InvalidOperationException("The menu is not connected to the scene morph controller.");
            }

            ValidateInputModule(inputModule, expectedActions);
            ValidateMenuOverrides(menu);
            ValidateMorphRoot(FindRoot(scene, "sk_f_human"), CharacterSex.Female);
            ValidateMorphRoot(FindRoot(scene, "sk_m_human"), CharacterSex.Male);
        }

        private static void ValidateInputModule(InputSystemUIInputModule module, InputActionAsset expectedActions)
        {
            if (module.actionsAsset != expectedActions)
            {
                throw new InvalidOperationException("The UI input module references the wrong input-actions asset.");
            }

            var serializedModule = new SerializedObject(module);
            foreach (string propertyName in new[]
                     {
                         "m_PointAction", "m_MoveAction", "m_SubmitAction", "m_CancelAction", "m_LeftClickAction",
                         "m_MiddleClickAction", "m_RightClickAction", "m_ScrollWheelAction",
                         "m_TrackedDevicePositionAction", "m_TrackedDeviceOrientationAction"
                     })
            {
                UnityEngine.Object reference = serializedModule.FindProperty(propertyName).objectReferenceValue;
                if (reference == null || AssetDatabase.GetAssetPath(reference) != InputActionsPath)
                {
                    throw new InvalidOperationException($"UI input reference '{propertyName}' is invalid.");
                }
            }
        }

        private static void ValidateMenuOverrides(CharacterMorphDemoUI menu)
        {
            GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(menu.gameObject);
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(instanceRoot) ?? Array.Empty<PropertyModification>();
            int controllerOverrides = modifications.Count(modification => modification.propertyPath == "controller");
            if (controllerOverrides != 1 || modifications.Length > AllowedMenuOverridePaths.Count)
            {
                throw new InvalidOperationException(
                    $"The menu instance has {modifications.Length} overrides and {controllerOverrides} controller overrides; expected only the prefab root and one controller reference.");
            }

            PropertyModification unexpected = modifications.FirstOrDefault(modification =>
                !AllowedMenuOverridePaths.Contains(modification.propertyPath));
            if (unexpected != null)
            {
                throw new InvalidOperationException($"Unexpected menu prefab override '{unexpected.propertyPath}'.");
            }
        }

        private static void ValidateMorphRoot(GameObject root, CharacterSex sex)
        {
            if (root == null)
            {
                throw new InvalidOperationException($"Missing {sex} character root.");
            }

            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(root) ?? Array.Empty<PropertyModification>();
            if (modifications.Any(modification =>
                    modification.propertyPath.StartsWith("m_BlendShapeWeights", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"{sex} character still has saved blendshape-weight overrides.");
            }

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
            {
                bool hasPositive = HasBlendShape(renderers, definition.GetPositiveShape(sex));
                bool hasNegative = !definition.IsBipolar || HasBlendShape(renderers, definition.GetNegativeShape(sex));
                if (!hasPositive || !hasNegative)
                {
                    throw new InvalidOperationException($"{sex} is missing morph '{definition.Id}'.");
                }
            }
        }

        private static bool HasBlendShape(IEnumerable<SkinnedMeshRenderer> renderers, string shapeName)
        {
            return renderers.Any(renderer =>
                renderer.sharedMesh != null && renderer.sharedMesh.GetBlendShapeIndex(shapeName) >= 0);
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(root => root.name == name);
        }

        private static T FindFirst<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }
    }
}
