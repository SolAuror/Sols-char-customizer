using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTools
{
    public static class CharacterCustomizationSetupValidator
    {
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
            ValidateBakeReadiness(controller, errors);

            if (errors.Count == 0)
            {
                string scope = Application.isPlaying ? string.Empty : " Run again in Play Mode to check the rig driver's binding.";
                Debug.Log($"Character customization setup is valid for '{controller.name}'.{scope}", controller);
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

            var twistBones = new List<TwistBoneBinding>();
            if (CharacterTwistBoneDriver.FindHumanoidTwistBones(animator, twistBones) > 0 &&
                animator.GetComponent<CharacterTwistBoneDriver>() == null &&
                !controller.EnsuresTwistBoneDrivers)
            {
                errors.Add($"Animator '{animator.name}' has {twistBones.Count} unmapped twist bones but no {nameof(CharacterTwistBoneDriver)}; they will stay at bind pose.");
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

        private static void ValidateBakeReadiness(CharacterMorphController controller, List<string> errors)
        {
            Transform root = controller.ActiveCharacterRoot;
            if (root == null)
            {
                return;
            }

            var catalogShapes = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (CharacterMorphDefinition definition in controller.Definitions)
            {
                if (!definition.IsBlendShapeDriven)
                {
                    continue;
                }

                catalogShapes.Add(definition.GetPositiveShape(controller.ActiveSex));
                string negativeShape = definition.GetNegativeShape(controller.ActiveSex);
                if (!string.IsNullOrEmpty(negativeShape))
                {
                    catalogShapes.Add(negativeShape);
                }
            }

            var reported = new HashSet<Mesh>();
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null || mesh.isReadable || !reported.Add(mesh))
                {
                    continue;
                }

                for (int index = 0; index < mesh.blendShapeCount; index++)
                {
                    if (catalogShapes.Contains(mesh.GetBlendShapeName(index)))
                    {
                        errors.Add($"Mesh '{mesh.name}' needs Read/Write enabled in its import settings for NPC baking.");
                        break;
                    }
                }
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

            // The controller binds the driver to the active root in Awake, so in
            // Edit Mode it is always unbound. ValidateAnimator already covers the
            // active root's Animator and Avatar.
            if (!Application.isPlaying)
            {
                return;
            }

            if (!driver.ValidateBinding(out string bindingError))
            {
                errors.Add(bindingError);
            }
        }
    }
}
