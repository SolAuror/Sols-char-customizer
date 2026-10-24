using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTests
{
    // Minimal T-pose humanoid facing +Z with the character's left on -X, built in
    // code so rig tests do not depend on the MPFB assets.
    internal sealed class SyntheticHumanoid
    {
        public const string LeftUpperArmTwist = "LeftUpperArmTwist";
        public const string LeftForearmTwist = "LeftForearmTwist";
        public const string LeftThighTwist = "LeftThighTwist";
        public const string LeftLegTwist = "LeftLegTwist";
        public const string LeftElbowHelper = "LeftElbowHelper";

        private readonly List<SkeletonBone> skeleton = new();
        private readonly List<HumanBone> human = new();
        private readonly bool withTwistBones;

        private SyntheticHumanoid(bool withTwistBones)
        {
            this.withTwistBones = withTwistBones;
        }

        public GameObject Root { get; private set; }
        public Avatar Avatar { get; private set; }
        public Animator Animator { get; private set; }

        // With twist bones, each limb gets Auto-Rig Pro style twist children: one at
        // the root of the upper segment and one halfway along the lower segment,
        // plus a non-twist helper that detection must ignore.
        public static SyntheticHumanoid Create(bool withTwistBones = false)
        {
            var humanoid = new SyntheticHumanoid(withTwistBones);
            humanoid.Build();
            return humanoid;
        }

        public Transform Find(string boneName)
        {
            Transform match = FindInHierarchy(Root.transform, boneName);
            Assert.That(match, Is.Not.Null, $"Synthetic humanoid has no bone named '{boneName}'.");
            return match;
        }

        public void Destroy()
        {
            if (Root != null)
            {
                Object.DestroyImmediate(Root);
            }

            if (Avatar != null)
            {
                Object.DestroyImmediate(Avatar);
            }
        }

        private void Build()
        {
            Root = new GameObject("Synthetic Humanoid");
            AddSkeletonBone(Root.transform);

            Transform hips = AddBone(HumanBodyBones.Hips, Root.transform, new Vector3(0f, 1f, 0f));
            Transform spine = AddBone(HumanBodyBones.Spine, hips, new Vector3(0f, 0.1f, 0f));
            Transform chest = AddBone(HumanBodyBones.Chest, spine, new Vector3(0f, 0.15f, 0f));
            Transform neck = AddBone(HumanBodyBones.Neck, chest, new Vector3(0f, 0.2f, 0f));
            AddBone(HumanBodyBones.Head, neck, new Vector3(0f, 0.1f, 0f));

            AddArm(chest, -1f, "Left", HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            AddArm(chest, 1f, "Right", HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            AddLeg(hips, -1f, "Left", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            AddLeg(hips, 1f, "Right", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);

            Avatar = AvatarBuilder.BuildHumanAvatar(Root, new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f
            });
            Assert.That(Avatar != null && Avatar.isValid && Avatar.isHuman, Is.True,
                "The synthetic humanoid Avatar failed to build.");

            Animator = Root.AddComponent<Animator>();
            Animator.avatar = Avatar;
            Animator.Rebind();
        }

        private void AddArm(
            Transform chest,
            float side,
            string sideName,
            HumanBodyBones shoulder,
            HumanBodyBones upperArm,
            HumanBodyBones lowerArm,
            HumanBodyBones hand)
        {
            Transform shoulderTransform = AddBone(shoulder, chest, new Vector3(side * 0.05f, 0.15f, 0f));
            Transform upperArmTransform = AddBone(upperArm, shoulderTransform, new Vector3(side * 0.1f, 0f, 0f));
            Transform lowerArmTransform = AddBone(lowerArm, upperArmTransform, new Vector3(side * 0.28f, 0f, 0f));
            AddBone(hand, lowerArmTransform, new Vector3(side * 0.25f, 0f, 0f));

            if (withTwistBones)
            {
                AddExtraBone($"{sideName}UpperArmTwist", upperArmTransform, Vector3.zero);
                AddExtraBone($"{sideName}ForearmTwist", lowerArmTransform, new Vector3(side * 0.125f, 0f, 0f));
                AddExtraBone($"{sideName}ElbowHelper", lowerArmTransform, Vector3.zero);
            }
        }

        private void AddLeg(
            Transform hips,
            float side,
            string sideName,
            HumanBodyBones upperLeg,
            HumanBodyBones lowerLeg,
            HumanBodyBones foot)
        {
            Transform upperLegTransform = AddBone(upperLeg, hips, new Vector3(side * 0.1f, -0.05f, 0f));
            Transform lowerLegTransform = AddBone(lowerLeg, upperLegTransform, new Vector3(0f, -0.42f, 0f));
            AddBone(foot, lowerLegTransform, new Vector3(0f, -0.42f, 0f));

            if (withTwistBones)
            {
                AddExtraBone($"{sideName}ThighTwist", upperLegTransform, Vector3.zero);
                AddExtraBone($"{sideName}LegTwist", lowerLegTransform, new Vector3(0f, -0.21f, 0f));
            }
        }

        private Transform AddBone(HumanBodyBones bone, Transform parent, Vector3 localPosition)
        {
            Transform boneTransform = AddExtraBone(bone.ToString(), parent, localPosition);
            human.Add(new HumanBone
            {
                boneName = boneTransform.name,
                humanName = HumanTrait.BoneName[(int)bone],
                limit = new HumanLimit { useDefaultValues = true }
            });
            return boneTransform;
        }

        private Transform AddExtraBone(string boneName, Transform parent, Vector3 localPosition)
        {
            Transform boneTransform = new GameObject(boneName).transform;
            boneTransform.SetParent(parent, false);
            boneTransform.localPosition = localPosition;
            AddSkeletonBone(boneTransform);
            return boneTransform;
        }

        private void AddSkeletonBone(Transform boneTransform)
        {
            skeleton.Add(new SkeletonBone
            {
                name = boneTransform.name,
                position = boneTransform.localPosition,
                rotation = boneTransform.localRotation,
                scale = boneTransform.localScale
            });
        }

        private static Transform FindInHierarchy(Transform root, string boneName)
        {
            if (root.name == boneName)
            {
                return root;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform match = FindInHierarchy(root.GetChild(index), boneName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }
    }
}
