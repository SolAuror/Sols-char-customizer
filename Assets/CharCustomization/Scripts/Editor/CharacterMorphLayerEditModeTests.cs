using NUnit.Framework;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTests
{
    public sealed class CharacterMorphLayerEditModeTests
    {
        private const float Tolerance = 0.0001f;

        private GameObject owner;
        private CharacterMorphController controller;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("Morph Layer Test");
            controller = owner.AddComponent<CharacterMorphController>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void StatGrowthDoesNotOverwriteCreatorValue()
        {
            controller.SetMorph("body.weight", 0.4f);
            controller.SetStatGrowth("body_fat", 0.75f);

            Assert.That(controller.GetMorph("body.weight"), Is.EqualTo(0.4f).Within(Tolerance));
            Assert.That(controller.GetEffectiveMorph("body.weight"), Is.EqualTo(0.9f).Within(Tolerance));
        }

        [Test]
        public void NeutralStatGrowthLeavesCreatorValueUnchanged()
        {
            controller.SetMorph("body.weight", -0.3f);
            controller.SetStatGrowth("body_fat", 0.5f);
            controller.SetStatGrowth("muscle", 0f);

            Assert.That(controller.GetEffectiveMorph("body.weight"), Is.EqualTo(-0.3f).Within(Tolerance));
            Assert.That(controller.GetEffectiveMorph("body.muscle"), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void CapturedRecipeExcludesGameplayLayers()
        {
            controller.SetMorph("body.muscle", 0.2f);
            controller.SetStatGrowth("muscle", 1f);
            controller.SetMorphLayerOffset("injury", "body.weight", -0.5f);

            foreach (CharacterMorphValue value in controller.CaptureMorphValues())
            {
                float expected = value.MorphId == "body.muscle" ? 0.2f : 0f;
                Assert.That(value.Value, Is.EqualTo(expected).Within(Tolerance), value.MorphId);
            }
        }

        [Test]
        public void EffectiveValueIsClampedToMorphRange()
        {
            controller.SetMorph("body.weight", 0.8f);

            controller.SetStatGrowth("body_fat", 1f);
            Assert.That(controller.GetEffectiveMorph("body.weight"), Is.EqualTo(1f).Within(Tolerance));

            controller.SetStatGrowth("body_fat", 0f);
            Assert.That(controller.GetEffectiveMorph("body.weight"), Is.EqualTo(-0.2f).Within(Tolerance));

            controller.SetMorph("body.muscle", 0.1f);
            controller.SetMorphLayerOffset("atrophy", "body.muscle", -1f);
            Assert.That(controller.GetEffectiveMorph("body.muscle"), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void LayersSumAndClearIndependently()
        {
            controller.SetMorphLayerOffset("injury", "body.weight", -0.3f);
            controller.SetStatGrowth("body_fat", 0.75f);
            Assert.That(controller.GetEffectiveMorph("body.weight"), Is.EqualTo(0.2f).Within(Tolerance));

            Assert.That(controller.ClearMorphLayer("injury"), Is.True);
            Assert.That(controller.GetEffectiveMorph("body.weight"), Is.EqualTo(0.5f).Within(Tolerance));

            Assert.That(controller.ClearStatGrowth("body_fat"), Is.True);
            Assert.That(controller.GetEffectiveMorph("body.weight"), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(controller.ClearMorphLayer("injury"), Is.False);
        }

        [Test]
        public void StatGrowthUsesItsOwnNamedLayer()
        {
            controller.SetStatGrowth("muscle", 0.6f);

            string layerId = CharacterMorphController.GetStatGrowthLayerId("muscle");
            Assert.That(controller.TryGetMorphLayerOffset(layerId, "body.muscle", out float offset), Is.True);
            Assert.That(offset, Is.EqualTo(0.6f).Within(Tolerance));
        }

        [Test]
        public void ResetAndRecipeLoadKeepGameplayLayers()
        {
            controller.SetMorph("body.weight", 0.4f);
            controller.SetStatGrowth("body_fat", 0.75f);

            controller.ResetCurrentCharacter();
            Assert.That(controller.GetMorph("body.weight"), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(controller.GetEffectiveMorph("body.weight"), Is.EqualTo(0.5f).Within(Tolerance));

            controller.ApplyMorphValues(new[] { new CharacterMorphValue("body.weight", -0.2f) });
            Assert.That(controller.GetMorph("body.weight"), Is.EqualTo(-0.2f).Within(Tolerance));
            Assert.That(controller.GetEffectiveMorph("body.weight"), Is.EqualTo(0.3f).Within(Tolerance));

            controller.ClearAllMorphLayers();
            Assert.That(controller.GetEffectiveMorph("body.weight"), Is.EqualTo(-0.2f).Within(Tolerance));
        }
    }
}
