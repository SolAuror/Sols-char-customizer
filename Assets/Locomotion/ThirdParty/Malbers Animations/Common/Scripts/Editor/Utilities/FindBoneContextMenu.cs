#if UNITY_EDITOR
using System.Text.RegularExpressions;
using UnityEditor;
#endif
using UnityEngine;

namespace MalbersAnimations
{
#if UNITY_EDITOR
    public static class FindBoneContextMenu
    {
        [InitializeOnLoadMethod]
        public static void Init()
        {
            EditorApplication.contextualPropertyMenu += ContextualPropertyMenu;
        }

        private static void ContextualPropertyMenu(GenericMenu menu, SerializedProperty property)
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference)
            {
                //Find if the property is a Transform or a GameObject
                if (MSerializedTools.GetPropertyType(property) != typeof(Transform)
                    && MSerializedTools.GetPropertyType(property) != typeof(GameObject))
                    return;

                var anim = (property.serializedObject.targetObject as Component).FindComponent<Animator>();

                menu.AddItem(new GUIContent("Set Bone/Body/Head"), false, () =>
                {
                    if (anim.isHuman)
                        FinBone(property, HumanBodyBones.Head);
                    else
                        FinBone(property, "head");
                });
                menu.AddItem(new GUIContent("Set Bone/Body/Eyes"), false, () =>
                {
                    FinBone(property, "Eyes");
                });
                menu.AddItem(new GUIContent("Set Bone/Body/Right Eye"), false, () =>
                {
                    if (anim.isHuman)
                        FinBone(property, HumanBodyBones.RightEye);
                    else
                    {
                        var a = FinBone(property, "r ", "eye") || FinBone(property, "right", "eye") || FinBone(property, "r_ ", "eye") || FinBone(property, "eye", " r") || FinBone(property, "eye", "_r");
                    }
                });
                menu.AddItem(new GUIContent("Set Bone/Body/Left Eye"), false, () =>
                {
                    if (anim.isHuman)
                        FinBone(property, HumanBodyBones.LeftEye);
                    else
                    {
                        var a = FinBone(property, "l ", "eye") || FinBone(property, "left", "eye") || FinBone(property, "l_ ", "eye") || FinBone(property, "eye", " l") || FinBone(property, "eye", "_l");
                    }

                });

                menu.AddItem(new GUIContent("Set Bone/Body/Neck"), false, () =>
                {
                    if (anim.isHuman)
                        FinBone(property, HumanBodyBones.Neck);
                    else
                        FinBone(property, "neck");
                });
                menu.AddItem(new GUIContent("Set Bone/Body/Hips"), false, () =>
                {
                    if (anim.isHuman)
                        FinBone(property, HumanBodyBones.Hips);
                    else
                    {
                        var a = FinBone(property, "pelvis") || FinBone(property, "hips") || FinBone(property, "hip");
                    }
                });
                menu.AddItem(new GUIContent("Set Bone/Body/Spine"), false, () =>
                {
                    if (anim.isHuman)
                        FinBone(property, HumanBodyBones.Spine);
                    else
                    {
                        var a = FinBone(property, "spine") || FinBone(property, "spine0") || FinBone(property, "spine1");
                    }
                });
                menu.AddItem(new GUIContent("Set Bone/Body/Chest"), false, () =>
                {
                    if (anim.isHuman)
                        FinBone(property, HumanBodyBones.Chest);
                    else
                    {
                        var a = FinBone(property, "chest") || FinBone(property, "spine1");
                    }
                });
                menu.AddItem(new GUIContent("Set Bone/Body/Upper Chest"), false, () =>
                {
                    if (anim.isHuman)
                        FinBone(property, HumanBodyBones.UpperChest);
                    else
                    {
                        var a = FinBone(property, "upperchest") || FinBone(property, "spine2");
                    }
                });

                menu.AddItem(new GUIContent("Set Bone/Left/Left Shoulder"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.LeftShoulder);
                    else
                    {
                        var a = FinBone(property, "l ", "shoulder") || FinBone(property, "left", "shoulder") || FinBone(property, "l_ ", "shoulder") || FinBone(property, "shoulder", " l") || FinBone(property, "shoulder", "_l");
                    }
                });
                menu.AddItem(new GUIContent("Set Bone/Left/Left Upper Arm"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.LeftUpperArm);
                    else
                    {
                        var a = FinBone(property, "l ", "upperarm") || FinBone(property, "left", "upperarm") || FinBone(property, "l_ ", "upperarm") || FinBone(property, "upperarm", " l") || FinBone(property, "upperarm", "_l");
                    }
                });
                menu.AddItem(new GUIContent("Set Bone/Left/Left Lower Arm"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.LeftLowerArm);
                    else
                    {
                        var a = FinBone(property, "l ", "forearm") || FinBone(property, "left", "forearm") || FinBone(property, "l_ ", "forearm") || FinBone(property, "forearm", " l") || FinBone(property, "forearm", "_l")
                         || FinBone(property, "l ", "lowerarm") || FinBone(property, "left", "lowerarm") || FinBone(property, "l_ ", "lowerarm") || FinBone(property, "lowerarm", " l") || FinBone(property, "lowerarm", "_l");
                    }
                });

                menu.AddItem(new GUIContent("Set Bone/Left/Left Hand"), false, () =>
                    {
                        if (anim.isHuman) FinBone(property, HumanBodyBones.LeftHand);
                        else
                        {
                            var a = FinBone(property, "l ", "hand") || FinBone(property, "left", "hand") || FinBone(property, "l_ ", "hand") || FinBone(property, "hand", " l") || FinBone(property, "hand", "_l");
                        }
                    }
                );

                menu.AddItem(new GUIContent("Set Bone/Left/Left Upper Leg"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.LeftUpperLeg);
                    else
                    {
                        var a = FinBone(property, "l ", "upper", "leg") || FinBone(property, "left", "upper", "leg") || FinBone(property, "l_ ", "upper", "leg") || FinBone(property, "upper", "leg", " l") || FinBone(property, "upper", "leg", "_l")
                         || FinBone(property, "l ", "thigh") || FinBone(property, "left", "thigh") || FinBone(property, "l_ ", "thigh") || FinBone(property, "upper", "thigh") || FinBone(property, "upper", "thigh");
                    }
                });

                menu.AddItem(new GUIContent("Set Bone/Left/Left Lower Leg"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.LeftLowerLeg);
                    else
                    {
                        var a = FinBone(property, "l ", "lower", "leg") || FinBone(property, "left", "lower", "leg") || FinBone(property, "l_ ", "lower", "leg") || FinBone(property, "lower", "leg", " l") || FinBone(property, "lower", "leg", "_l")
                       || FinBone(property, "l ", "calf") || FinBone(property, "left", "calf") || FinBone(property, "l_ ", "calf") || FinBone(property, "lower", "calf") || FinBone(property, "lower", "calf");
                    }
                });

                menu.AddItem(new GUIContent("Set Bone/Left/Left Foot"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.LeftFoot);
                    else
                    {
                        var a = FinBone(property, "l ", "foot") || FinBone(property, "left", "foot") || FinBone(property, "l_ ", "foot") || FinBone(property, "foot", " l") || FinBone(property, "foot", "_l");
                    }
                });
                menu.AddItem(new GUIContent("Set Bone/Left/Left Toes"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.LeftToes);
                    else
                    {
                        var a = FinBone(property, "l ", "toe") || FinBone(property, "left", "toe") || FinBone(property, "l_ ", "toe") || FinBone(property, "toe", " l") || FinBone(property, "toe", "_l");
                    }

                });

                menu.AddItem(new GUIContent("Set Bone/Right/Right Hand"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.RightHand);
                    else
                    {
                        var a = FinBone(property, "r ", "hand") || FinBone(property, "right", "hand") || FinBone(property, "r_ ", "hand") || FinBone(property, "hand", " r") || FinBone(property, "hand", "_r");
                    }
                });


                menu.AddItem(new GUIContent("Set Bone/Right/Right Upper Arm"), false, () =>
                {

                    if (anim.isHuman) FinBone(property, HumanBodyBones.RightUpperArm);
                    else
                    {
                        var a = FinBone(property, "r ", "upperarm") || FinBone(property, "right", "upperarm") || FinBone(property, "r_ ", "upperarm") || FinBone(property, "upperarm", " r") || FinBone(property, "upperarm", "_r");
                    }
                });

                menu.AddItem(new GUIContent("Set Bone/Right/Right Lower Arm"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.RightLowerArm);
                    else
                    {
                        var a = FinBone(property, "r ", "lower", "arm") || FinBone(property, "right", "lower", "arm") || FinBone(property, "r_ ", "lower", "arm") || FinBone(property, "lower", "arm", " r") || FinBone(property, "lower", "arm", "_r")
                        || FinBone(property, "r ", "forearm") || FinBone(property, "right", "forearm") || FinBone(property, "r_ ", "forearm") || FinBone(property, "forearm", " r") || FinBone(property, "forearm", "_r");
                    }

                });


                menu.AddItem(new GUIContent("Set Bone/Right/Right Shoulder"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.RightShoulder);
                    else
                    {
                        var a = FinBone(property, "r ", "shoulder") || FinBone(property, "right", "shoulder") || FinBone(property, "r_ ", "shoulder") || FinBone(property, "shoulder", " r") || FinBone(property, "shoulder", "_r");
                    }
                });


                menu.AddItem(new GUIContent("Set Bone/Right/Right Upper Leg"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.RightUpperLeg);
                    else
                    {
                        var a = FinBone(property, "r ", "upper", "leg") || FinBone(property, "right", "upper", "leg") || FinBone(property, "r_ ", "upper", "leg") || FinBone(property, "upper", "leg", " r") || FinBone(property, "upper", "leg", "_r")
                         || FinBone(property, "r ", "thigh") || FinBone(property, "right", "thigh") || FinBone(property, "r_ ", "thigh") || FinBone(property, "upper", "thigh") || FinBone(property, "upper", "thigh");
                    }
                });
                menu.AddItem(new GUIContent("Set Bone/Right/Right Lower Leg"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.RightLowerLeg);
                    else
                    {
                        var a = FinBone(property, "r ", "lower", "leg") || FinBone(property, "right", "lower", "leg") || FinBone(property, "r_ ", "lower", "leg") || FinBone(property, "lower", "leg", " r") || FinBone(property, "lower", "leg", "_r")
                       || FinBone(property, "r ", "calf") || FinBone(property, "right", "calf") || FinBone(property, "r_ ", "calf") || FinBone(property, "lower", "calf") || FinBone(property, "lower", "calf");
                    }
                });
                menu.AddItem(new GUIContent("Set Bone/Right/Right Foot"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.RightFoot);
                    else
                    {
                        var a = FinBone(property, "r ", "foot") || FinBone(property, "right", "foot") || FinBone(property, "r_ ", "foot") || FinBone(property, "foot", " r") || FinBone(property, "foot", "_r");
                    }
                });
                menu.AddItem(new GUIContent("Set Bone/Right/Right Toes"), false, () =>
                {
                    if (anim.isHuman) FinBone(property, HumanBodyBones.RightToes);
                    else
                    {
                        var a = FinBone(property, "r ", "toe") || FinBone(property, "right", "toe") || FinBone(property, "r_ ", "toe") || FinBone(property, "toe", " r") || FinBone(property, "toe", "_r");
                    }
                });

            }
        }

        private static bool FinBone(SerializedProperty property, HumanBodyBones bone)
        {
            Object Bone = GetBone(property, bone);

            if (Bone != null)
            {
                property.objectReferenceValue = Bone;
                property.serializedObject.ApplyModifiedProperties();
                return true;
            }
            else
            {
                Debug.LogWarning("No Bone Found");
                return false;
            }
        }

        private static Object GetBone(SerializedProperty property, HumanBodyBones bone)
        {
            var Holder = property.serializedObject.targetObject;
            var animator = (Holder as Component).FindComponent<Animator>();

            if (animator != null)
            {
                Transform target;

                if (animator.isHuman)
                {
                    target = animator.GetBoneTransform(bone);
                }
                else
                {
                    //Check for Types of bone?

                    var splitName = Regex.Split(bone.ToString(), @"(?=[A-Z])");
                    target = animator.transform.FindGrandChild(splitName);
                }

                if (target == null) return null;

                if (MSerializedTools.GetPropertyType(property) == typeof(Transform))
                    return target;
                else return target.gameObject;
            }
            return null;
        }


        private static bool FinBone(SerializedProperty property, params string[] boneNames)
        {
            var Holder = property.serializedObject.targetObject;
            var animator = (Holder as Component).FindComponent<Animator>();

            if (animator != null)
            {
                var root = GetGenericRootNode(animator);

                //find the bone that contains all the bone names in the list (lowercase)
                Transform targetBone = root.FindGrandChild(boneNames);

                Debug.Log($"targetBone : [{targetBone}]");

                if (targetBone == null) return false;

                if (MSerializedTools.GetPropertyType(property) == typeof(Transform))
                    property.objectReferenceValue = targetBone;
                else property.objectReferenceValue = targetBone.gameObject;

                property.serializedObject.ApplyModifiedProperties();

                return true;
            }

            return false;
        }



        public static Transform GetGenericRootNode(this Animator animator)
        {
            if (animator == null) return null;

            // In a Generic rig, the "Root node" from the inspector is 
            // automatically assigned to the .rootBone of all SMRs.
            // We use GetComponentsInChildren to handle multiple meshes.
            var smrs = animator.GetComponentsInChildren<SkinnedMeshRenderer>();

            if (smrs.Length > 0)
            {
                // Usually, all SMRs in one FBX share the same Root node.
                // We return the first valid one found.
                foreach (var smr in smrs)
                {
                    if (smr.rootBone != null)
                        return smr.rootBone;
                }
            }

            // Fallback: If no SMRs are found or rootBone is null, 
            // the first child is the industry-standard default for Generic.
            return animator.transform.childCount > 0 ? animator.transform.GetChild(0) : null;
        }
    }
#endif
}