using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTools
{
    public static class CharacterCustomizationSetupValidator
    {
        private const string DefaultCatalogPath = "Assets/CharCustomization/CustomizationPresets/DefaultMorphCatalog.asset";

        [MenuItem("Tools/Sol/Character Customization/Validate Selected Character Setup")]
        private static void ValidateSelectedCharacterSetup()
        {
            CharacterMorphController controller = FindSelectedController();
            if (controller == null)
            {
                Debug.LogError("Select a CharacterMorphController or one of its character roots before validating.");
                return;
            }

            var errors = new List<string>();
            controller.ValidateConfiguration(errors);
            ValidateAnimator(controller, errors);
            ValidateRigDriver(controller, errors);

            if (errors.Count == 0)
            {
                Debug.Log($"Character customization setup is valid for '{controller.name}'.", controller);
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine($"Character customization setup issues for '{controller.name}':");
            foreach (string error in errors)
            {
                builder.Append("- ");
                builder.AppendLine(error);
            }

            Debug.LogError(builder.ToString(), controller);
        }

        [MenuItem("Tools/Sol/Character Customization/Create Default Morph Catalog")]
        public static void CreateDefaultMorphCatalog()
        {
            CharacterMorphCatalogAsset existing = AssetDatabase.LoadAssetAtPath<CharacterMorphCatalogAsset>(DefaultCatalogPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log($"Default morph catalog already exists at {DefaultCatalogPath}.", existing);
                return;
            }

            var catalog = ScriptableObject.CreateInstance<CharacterMorphCatalogAsset>();
            catalog.UseDefaultDefinitions();

            string folder = System.IO.Path.GetDirectoryName(DefaultCatalogPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            AssetDatabase.CreateAsset(catalog, DefaultCatalogPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = catalog;
            EditorGUIUtility.PingObject(catalog);
            Debug.Log($"Created default morph catalog at {DefaultCatalogPath}.", catalog);
        }

        [MenuItem("Tools/Sol/Character Customization/Ensure IK Bridge On Selected Animator")]
        private static void EnsureIkBridgeOnSelectedAnimator()
        {
            Animator animator = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInChildren<Animator>(true)
                : null;
            if (animator == null)
            {
                Debug.LogError("Select a character root or Animator before adding the IK bridge.");
                return;
            }

            CharacterRigAnimatorIkBridge bridge = animator.GetComponent<CharacterRigAnimatorIkBridge>();
            if (bridge == null)
            {
                Undo.AddComponent<CharacterRigAnimatorIkBridge>(animator.gameObject);
                Debug.Log($"Added {nameof(CharacterRigAnimatorIkBridge)} to '{animator.name}'.", animator);
                return;
            }

            Debug.Log($"'{animator.name}' already has {nameof(CharacterRigAnimatorIkBridge)}.", bridge);
        }

        private static CharacterMorphController FindSelectedController()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return Object.FindFirstObjectByType<CharacterMorphController>();
            }

            CharacterMorphController controller = selected.GetComponent<CharacterMorphController>();
            if (controller != null)
            {
                return controller;
            }

            controller = selected.GetComponentInParent<CharacterMorphController>(true);
            if (controller != null)
            {
                return controller;
            }

            return selected.GetComponentInChildren<CharacterMorphController>(true);
        }

        private static void ValidateAnimator(CharacterMorphController controller, List<string> errors)
        {
            if (!controller.TryGetActiveAnimator(out Animator animator) || animator == null)
            {
                errors.Add("The active character root has no Animator.");
                return;
            }

            if (!animator.isHuman)
            {
                errors.Add($"Animator '{animator.name}' must use a humanoid Avatar for skeletal channels.");
            }

            if (animator.GetComponent<CharacterRigAnimatorIkBridge>() == null)
            {
                errors.Add($"Animator '{animator.name}' is missing {nameof(CharacterRigAnimatorIkBridge)}.");
            }

            if (animator.runtimeAnimatorController is AnimatorController controllerAsset)
            {
                for (int index = 0; index < controllerAsset.layers.Length; index++)
                {
                    if (controllerAsset.layers[index].iKPass)
                    {
                        return;
                    }
                }

                errors.Add($"Animator Controller '{controllerAsset.name}' has no layer with IK Pass enabled.");
            }
        }

        private static void ValidateRigDriver(CharacterMorphController controller, List<string> errors)
        {
            CharacterRigProportionDriver driver = controller.RigProportionDriver;
            if (driver == null)
            {
                errors.Add($"The manager is missing {nameof(CharacterRigProportionDriver)}.");
                return;
            }

            if (!driver.ValidateBinding(out string bindingError))
            {
                errors.Add(bindingError);
            }
        }
    }
}
