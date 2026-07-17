using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTests
{
    public sealed class CharacterBodyMorphMergeEditModeTests
    {
        [Test]
        public void OnlyCurrentProportionMorphsUseSkeletalDrivers()
        {
            string[] skeletalIds = CharacterMorphCatalog.Definitions
                .Where(definition => definition.IsSkeletalDriven && definition.VisibleInCreator)
                .Select(definition => definition.Id)
                .OrderBy(id => id)
                .ToArray();

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "body.height",
                    "body.hips",
                    "body.shoulder_width",
                    "rig.chest",
                    "rig.feet",
                    "rig.fingers",
                    "rig.hands",
                    "rig.head",
                    "rig.legs",
                    "rig.lower_arms",
                    "rig.lower_body",
                    "rig.neck",
                    "rig.spine",
                    "rig.upper_arms",
                    "rig.upper_body"
                },
                skeletalIds);
        }

        [Test]
        public void SurfaceMorphsRemainBlendShapeDriven()
        {
            AssertBlendShapeOnly("body.breast");
            AssertBlendShapeOnly("body.glutes");
            AssertBlendShapeOnly("body.waist");
            AssertBlendShapeOnly("head.nose.width");
            AssertBlendShapeOnly("head.mouth.fullness");
            AssertBlendShapeOnly("head.eyes.size");
        }

        [Test]
        public void BodyWidthControlsUseBmlRigChannels()
        {
            Assert.That(CharacterMorphCatalog.TryGet("body.shoulder_width", out CharacterMorphDefinition shoulderWidth), Is.True);
            Assert.That(shoulderWidth.IsSkeletalDriven, Is.True);
            Assert.That(shoulderWidth.RigChannel, Is.EqualTo(CharacterRigProportionChannel.Shoulders));

            Assert.That(CharacterMorphCatalog.TryGet("body.hips", out CharacterMorphDefinition hips), Is.True);
            Assert.That(hips.IsSkeletalDriven, Is.True);
            Assert.That(hips.RigChannel, Is.EqualTo(CharacterRigProportionChannel.HipsWidth));
        }

        [Test]
        public void SkeletalMorphsKeepStableRecipeIds()
        {
            var recipe = new CharacterRecipe(
                CharacterSex.Female,
                CharacterRecipe.DefaultSkinToneId,
                false,
                Color.white,
                CharacterRecipe.DefaultEyeMaterialId,
                new[]
                {
                    new CharacterMorphValue("body.height", 0.4f),
                    new CharacterMorphValue("body.shoulder_width", -0.25f),
                    new CharacterMorphValue("body.hips", 0.6f),
                    new CharacterMorphValue("body.breast", 0.8f),
                    new CharacterMorphValue("body.glutes", -0.3f)
                });

            CharacterRecipe copy = recipe.Copy();

            Assert.That(copy.HasValidIdentifiers(out string error), Is.True, error);
            Assert.That(copy.TryGetValue("body.height", out float height), Is.True);
            Assert.That(height, Is.EqualTo(0.4f));
            Assert.That(copy.TryGetValue("body.breast", out float breast), Is.True);
            Assert.That(breast, Is.EqualTo(0.8f));
            Assert.That(copy.TryGetValue("body.glutes", out float glutes), Is.True);
            Assert.That(glutes, Is.EqualTo(-0.3f));
        }

        [Test]
        public void BodyMorphLiteRigControlsAreCreatorVisibleInBodyGroup()
        {
            CharacterMorphDefinition[] bodyDefinitions = CharacterMorphCatalog.Definitions
                .Where(definition => definition.IsSkeletalDriven &&
                                     definition.Group == CharacterMorphCatalog.BodyGroup &&
                                     definition.VisibleInCreator)
                .ToArray();

            string[] bodyRigIds = bodyDefinitions
                .Select(definition => definition.Id)
                .OrderBy(id => id)
                .ToArray();

            CollectionAssert.Contains(bodyRigIds, "rig.upper_body");
            CollectionAssert.Contains(bodyRigIds, "rig.legs");
            CollectionAssert.Contains(bodyRigIds, "rig.feet");
            CollectionAssert.Contains(bodyRigIds, "rig.spine");
            CollectionAssert.DoesNotContain(bodyRigIds, "rig.waist");
            CollectionAssert.DoesNotContain(bodyRigIds, "rig.foot_radius");
            CollectionAssert.DoesNotContain(bodyRigIds, "rig.shoulders");
            Assert.That(bodyDefinitions.All(definition => definition.VisibleInCreator), Is.True);

            CharacterMorphCatalog.TryGet("rig.spine", out CharacterMorphDefinition spineDefinition);
            Assert.That(spineDefinition.Label, Is.EqualTo("Upper Waist"));

            CharacterMorphCatalog.TryGet("body.waist", out CharacterMorphDefinition waistDefinition);
            Assert.That(waistDefinition.Label, Is.EqualTo("Lower Waist"));
        }

        [Test]
        public void HiddenBodyMorphLiteChannelsRemainAvailableForRecipes()
        {
            Assert.That(CharacterMorphCatalog.TryGet("rig.waist", out CharacterMorphDefinition waist), Is.True);
            Assert.That(waist.VisibleInCreator, Is.False);
            Assert.That(waist.RigChannel, Is.EqualTo(CharacterRigProportionChannel.Waist));

            Assert.That(CharacterMorphCatalog.TryGet("rig.foot_radius", out CharacterMorphDefinition footRadius), Is.True);
            Assert.That(footRadius.VisibleInCreator, Is.False);
            Assert.That(footRadius.DriverType, Is.EqualTo(CharacterMorphDriverType.RigParameter));
            Assert.That(footRadius.RigChannel, Is.EqualTo(CharacterRigProportionChannel.FootRadius));

            Assert.That(CharacterMorphCatalog.TryGet("rig.shoulders", out CharacterMorphDefinition shoulders), Is.True);
            Assert.That(shoulders.VisibleInCreator, Is.False);
            Assert.That(shoulders.RigChannel, Is.EqualTo(CharacterRigProportionChannel.Shoulders));
        }

        [Test]
        public void DefaultCatalogAssetValidatesWithoutDuplicateIds()
        {
            var catalog = ScriptableObject.CreateInstance<CharacterMorphCatalogAsset>();
            try
            {
                catalog.UseDefaultDefinitions();

                Assert.That(catalog.ValidateDefinitions(out string error), Is.True, error);
                Assert.That(catalog.TryGet("body.height", out CharacterMorphDefinition definition), Is.True);
                Assert.That(definition.RigChannel, Is.EqualTo(CharacterRigProportionChannel.Height));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void RigDriverReportsInvalidBindingBeforeHumanoidAnimatorIsBound()
        {
            var owner = new GameObject("Rig Driver Test");
            try
            {
                var driver = owner.AddComponent<CharacterRigProportionDriver>();

                Assert.That(driver.CanApply, Is.False);
                Assert.That(driver.ValidateBinding(out string error), Is.False);
                StringAssert.Contains("No active character root", error);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        private static void AssertBlendShapeOnly(string morphId)
        {
            Assert.That(CharacterMorphCatalog.TryGet(morphId, out CharacterMorphDefinition definition), Is.True, morphId);
            Assert.That(definition.IsBlendShapeDriven, Is.True, morphId);
            Assert.That(definition.IsSkeletalDriven, Is.False, morphId);
        }
    }
}
