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
                new[] { "body.height", "body.hips", "body.shoulder_width" },
                skeletalIds);
        }

        [Test]
        public void SurfaceMorphsRemainBlendShapeDriven()
        {
            AssertBlendShapeOnly("body.breast");
            AssertBlendShapeOnly("body.glutes");
            AssertBlendShapeOnly("head.nose.width");
            AssertBlendShapeOnly("head.mouth.fullness");
            AssertBlendShapeOnly("head.eyes.size");
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
        public void HiddenBodyMorphLiteBackendControlsAreAvailableButNotCreatorVisible()
        {
            string[] backendIds = CharacterMorphCatalog.Definitions
                .Where(definition => definition.IsSkeletalDriven && !definition.VisibleInCreator)
                .Select(definition => definition.Id)
                .OrderBy(id => id)
                .ToArray();

            CollectionAssert.Contains(backendIds, "rig.upper_body");
            CollectionAssert.Contains(backendIds, "rig.legs");
            CollectionAssert.Contains(backendIds, "rig.feet");
            CollectionAssert.Contains(backendIds, "rig.foot_radius");
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
