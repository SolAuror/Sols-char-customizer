using NUnit.Framework;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTests
{
    public sealed class CharacterAvatarRebuildEditModeTests
    {
        private const float Tolerance = 0.0001f;

        private SyntheticHumanoid humanoid;
        private GameObject manager;
        private Avatar sourceAvatar;
        private Animator animator;
        private CharacterRigProportionDriver driver;

        [SetUp]
        public void SetUp()
        {
            humanoid = SyntheticHumanoid.Create();
            sourceAvatar = humanoid.Avatar;
            animator = humanoid.Animator;

            manager = new GameObject("Rig Driver Test");
            driver = manager.AddComponent<CharacterRigProportionDriver>();
            driver.Bind(humanoid.Root.transform, animator);
        }

        [TearDown]
        public void TearDown()
        {
            if (driver != null)
            {
                driver.Bind(null, null);
            }

            Object.DestroyImmediate(manager);
            humanoid?.Destroy();
        }

        [Test]
        public void SyntheticHumanoidPassesBindingValidation()
        {
            Assert.That(driver.CanApply, Is.True);
            Assert.That(driver.ValidateBinding(out string error), Is.True, error);
        }

        [Test]
        public void RebuildBakesBoneScalesIntoAvatarSkeleton()
        {
            SetRigMorph("rig.legs", 1f);
            Assert.That(driver.IsAvatarRebuildPending, Is.True);

            Assert.That(driver.RebuildAvatarNow(), Is.True);
            Assert.That(driver.IsAvatarRebuildPending, Is.False);
            Assert.That(animator.avatar, Is.SameAs(driver.RuntimeAvatar));
            Assert.That(animator.avatar, Is.Not.SameAs(sourceAvatar));
            Assert.That(animator.avatar.isValid && animator.avatar.isHuman, Is.True);

            Assert.That(FindSkeletonBone(animator.avatar, "LeftUpperLeg").scale.y, Is.EqualTo(1.2f).Within(Tolerance));
            Assert.That(FindSkeletonBone(animator.avatar, "LeftLowerLeg").scale.y,
                Is.EqualTo(1f / (1.2f * 1.2f)).Within(Tolerance));
            Assert.That(FindSkeletonBone(animator.avatar, "Spine").scale.y, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void RebuildBakesSideOffsetsIntoAvatarSkeleton()
        {
            SetRigMorph("body.hips", 1f);

            Assert.That(driver.RebuildAvatarNow(), Is.True);
            Assert.That(FindSkeletonBone(animator.avatar, "LeftUpperLeg").position.x, Is.EqualTo(-0.135f).Within(Tolerance));
            Assert.That(FindSkeletonBone(animator.avatar, "RightUpperLeg").position.x, Is.EqualTo(0.135f).Within(Tolerance));
        }

        [Test]
        public void NeutralSkeletonRestoresSourceAvatar()
        {
            SetRigMorph("rig.legs", 1f);
            Assert.That(driver.RebuildAvatarNow(), Is.True);

            SetRigMorph("rig.legs", 0f);
            Assert.That(driver.RebuildAvatarNow(), Is.True);
            Assert.That(animator.avatar, Is.SameAs(sourceAvatar));
            Assert.That(driver.RuntimeAvatar == null, Is.True);
        }

        [Test]
        public void UnchangedValueDoesNotQueueAnotherRebuild()
        {
            SetRigMorph("rig.legs", 1f);
            Assert.That(driver.RebuildAvatarNow(), Is.True);

            SetRigMorph("rig.legs", 1f);
            Assert.That(driver.IsAvatarRebuildPending, Is.False);
        }

        [Test]
        public void UnbindingRestoresSourceAvatarAndBindPose()
        {
            SetRigMorph("rig.legs", 1f);
            Assert.That(driver.RebuildAvatarNow(), Is.True);

            driver.Bind(null, null);

            Assert.That(animator.avatar, Is.SameAs(sourceAvatar));
            Assert.That(animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localScale.y, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void CleanPoseIsNotReappliedUntilEdited()
        {
            SetRigMorph("rig.legs", 1f);
            Assert.That(driver.IsPoseDirty, Is.False);

            Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            hand.localScale = Vector3.one * 3f;
            Assert.That(driver.ApplyIfDirty(), Is.False);
            Assert.That(hand.localScale.x, Is.EqualTo(3f).Within(Tolerance));

            SetRigMorph("rig.legs", 0.5f);
            Assert.That(hand.localScale.x, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void ReapplyEveryFrameOverridesExternalBoneEdits()
        {
            SetRigMorph("rig.legs", 1f);
            driver.ReapplyPoseEveryFrame = true;

            Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            hand.localScale = Vector3.one * 3f;

            Assert.That(driver.ApplyIfDirty(), Is.True);
            Assert.That(hand.localScale.x, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void PoseStaysDirtyUntilItCanBeApplied()
        {
            driver.Bind(null, null);
            SetRigMorph("rig.legs", 1f);

            Assert.That(driver.IsPoseDirty, Is.True);
            Assert.That(driver.ApplyIfDirty(), Is.False);
        }

        private void SetRigMorph(string morphId, float value)
        {
            Assert.That(CharacterMorphCatalog.TryGet(morphId, out CharacterMorphDefinition definition), Is.True, morphId);
            driver.SetMorph(definition, value);
        }

        private static SkeletonBone FindSkeletonBone(Avatar avatar, string boneName)
        {
            foreach (SkeletonBone bone in avatar.humanDescription.skeleton)
            {
                if (bone.name == boneName)
                {
                    return bone;
                }
            }

            Assert.Fail($"Avatar skeleton has no bone named '{boneName}'.");
            return default;
        }
    }
}
