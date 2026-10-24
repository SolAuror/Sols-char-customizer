using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTests
{
    public sealed class CharacterTwistBoneDriverEditModeTests
    {
        private const float AngleTolerance = 0.01f;

        private SyntheticHumanoid humanoid;
        private Mesh bindPoseMesh;

        [SetUp]
        public void SetUp()
        {
            humanoid = SyntheticHumanoid.Create(withTwistBones: true);
        }

        [TearDown]
        public void TearDown()
        {
            humanoid?.Destroy();
            if (bindPoseMesh != null)
            {
                Object.DestroyImmediate(bindPoseMesh);
            }
        }

        [Test]
        public void DetectionFindsLimbTwistBonesAndIgnoresHelpers()
        {
            var bindings = new List<TwistBoneBinding>();

            Assert.That(CharacterTwistBoneDriver.FindHumanoidTwistBones(humanoid.Animator, bindings), Is.EqualTo(8));
            Assert.That(bindings.Exists(binding => binding.TwistBone.name == SyntheticHumanoid.LeftElbowHelper), Is.False);

            TwistBoneBinding forearm = bindings.Find(binding => binding.TwistBone.name == SyntheticHumanoid.LeftForearmTwist);
            Assert.That(forearm.Source, Is.EqualTo(TwistBoneSource.DistalBone));
            Assert.That(forearm.DistalBone, Is.SameAs(humanoid.Animator.GetBoneTransform(HumanBodyBones.LeftHand)));
            Assert.That(forearm.Weight, Is.EqualTo(0.5f).Within(0.0001f));

            TwistBoneBinding upperArm = bindings.Find(binding => binding.TwistBone.name == SyntheticHumanoid.LeftUpperArmTwist);
            Assert.That(upperArm.Source, Is.EqualTo(TwistBoneSource.SegmentBone));
            Assert.That(upperArm.Weight, Is.EqualTo(CharacterTwistBoneDriver.DefaultSegmentCounterWeight).Within(0.0001f));
        }

        [Test]
        public void ForearmTwistFollowsShareOfHandRoll()
        {
            CharacterTwistBoneDriver driver = CreateDriver();
            Transform hand = humanoid.Animator.GetBoneTransform(HumanBodyBones.LeftHand);

            hand.localRotation = Quaternion.AngleAxis(90f, Vector3.left);
            driver.ApplyNow();

            AssertRotation(SyntheticHumanoid.LeftForearmTwist, Quaternion.AngleAxis(45f, Vector3.left));
        }

        [Test]
        public void HandSwingDoesNotTwistForearm()
        {
            CharacterTwistBoneDriver driver = CreateDriver();
            Transform hand = humanoid.Animator.GetBoneTransform(HumanBodyBones.LeftHand);

            hand.localRotation = Quaternion.AngleAxis(60f, Vector3.forward);
            driver.ApplyNow();

            AssertRotation(SyntheticHumanoid.LeftForearmTwist, Quaternion.identity);
        }

        [Test]
        public void TwistIsSeparatedFromCombinedSwing()
        {
            CharacterTwistBoneDriver driver = CreateDriver();
            Transform hand = humanoid.Animator.GetBoneTransform(HumanBodyBones.LeftHand);

            hand.localRotation = Quaternion.AngleAxis(40f, Vector3.forward) * Quaternion.AngleAxis(90f, Vector3.left);
            driver.ApplyNow();

            AssertRotation(SyntheticHumanoid.LeftForearmTwist, Quaternion.AngleAxis(45f, Vector3.left));
        }

        [Test]
        public void UpperArmTwistCountersItsOwnRoll()
        {
            CharacterTwistBoneDriver driver = CreateDriver();
            Transform upperArm = humanoid.Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);

            upperArm.localRotation = Quaternion.AngleAxis(60f, Vector3.left);
            driver.ApplyNow();

            AssertRotation(SyntheticHumanoid.LeftUpperArmTwist, Quaternion.AngleAxis(-30f, Vector3.left));
        }

        [Test]
        public void LegTwistFollowsShareOfFootRoll()
        {
            CharacterTwistBoneDriver driver = CreateDriver();
            Transform foot = humanoid.Animator.GetBoneTransform(HumanBodyBones.LeftFoot);

            foot.localRotation = Quaternion.AngleAxis(50f, Vector3.down);
            driver.ApplyNow();

            AssertRotation(SyntheticHumanoid.LeftLegTwist, Quaternion.AngleAxis(25f, Vector3.down));
        }

        [Test]
        public void SkinBindPosesKeepTwistRelativeToRestPose()
        {
            AddBindPoseRenderer();
            Transform hand = humanoid.Animator.GetBoneTransform(HumanBodyBones.LeftHand);

            // Posed before the driver initializes, as on a character that was already
            // animating. Without bind poses this pose would be mistaken for rest.
            hand.localRotation = Quaternion.AngleAxis(90f, Vector3.left);
            CharacterTwistBoneDriver driver = CreateDriver();
            driver.ApplyNow();

            AssertRotation(SyntheticHumanoid.LeftForearmTwist, Quaternion.AngleAxis(45f, Vector3.left));
        }

        [Test]
        public void TwistAngleIsSignedAndWrapped()
        {
            Assert.That(CharacterTwistBoneDriver.TwistAngle(Quaternion.AngleAxis(-70f, Vector3.up), Vector3.up),
                Is.EqualTo(-70f).Within(AngleTolerance));
            Assert.That(CharacterTwistBoneDriver.TwistAngle(Quaternion.AngleAxis(170f, Vector3.up), Vector3.up),
                Is.EqualTo(170f).Within(AngleTolerance));
            Assert.That(CharacterTwistBoneDriver.TwistAngle(Quaternion.AngleAxis(30f, Vector3.right), Vector3.up),
                Is.EqualTo(0f).Within(AngleTolerance));
        }

        private CharacterTwistBoneDriver CreateDriver()
        {
            var driver = humanoid.Root.AddComponent<CharacterTwistBoneDriver>();
            Assert.That(driver.Initialize(), Is.True);
            Assert.That(driver.ActiveTwistBoneCount, Is.EqualTo(8));
            return driver;
        }

        private void AssertRotation(string boneName, Quaternion expected)
        {
            Quaternion actual = humanoid.Find(boneName).localRotation;
            Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(AngleTolerance),
                $"{boneName} rotation {actual.eulerAngles} differs from {expected.eulerAngles}.");
        }

        private void AddBindPoseRenderer()
        {
            var body = new GameObject("Body");
            body.transform.SetParent(humanoid.Root.transform, false);

            var bones = new List<Transform>();
            foreach (Transform candidate in humanoid.Root.GetComponentsInChildren<Transform>())
            {
                if (candidate != humanoid.Root.transform && candidate != body.transform)
                {
                    bones.Add(candidate);
                }
            }

            var bindposes = new Matrix4x4[bones.Count];
            for (int index = 0; index < bones.Count; index++)
            {
                bindposes[index] = bones[index].worldToLocalMatrix * body.transform.localToWorldMatrix;
            }

            bindPoseMesh = new Mesh { name = "Twist Bind Poses", bindposes = bindposes };
            var skinnedRenderer = body.AddComponent<SkinnedMeshRenderer>();
            skinnedRenderer.bones = bones.ToArray();
            skinnedRenderer.sharedMesh = bindPoseMesh;
        }
    }
}
