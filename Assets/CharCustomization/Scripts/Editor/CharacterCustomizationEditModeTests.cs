using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Sol.CharacterCustomization.Editor.Tests
{
    public sealed class CharacterCustomizationEditModeTests
    {
        private string testDirectory;

        [SetUp]
        public void SetUp()
        {
            testDirectory = Path.Combine(Path.GetTempPath(), "SolCharacterCustomizationTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }

        [Test]
        public void RecipeJsonRoundTripPreservesAppearance()
        {
            var values = new List<CharacterMorphValue>
            {
                new("body.weight", -0.4f),
                new("head.eyes.size", 0.25f)
            };
            Color customColor = new(0.2f, 0.6f, 0.85f, 1f);
            var recipe = new CharacterRecipe(CharacterSex.Male, "tan", true, customColor, values);

            CharacterRecipe loaded = JsonUtility.FromJson<CharacterRecipe>(JsonUtility.ToJson(recipe));

            Assert.That(loaded.Version, Is.EqualTo(CharacterRecipe.CurrentVersion));
            Assert.That(loaded.Sex, Is.EqualTo(CharacterSex.Male));
            Assert.That(loaded.SkinToneId, Is.EqualTo("tan"));
            Assert.That(loaded.UsesCustomSkinColor, Is.True);
            Assert.That(loaded.CustomSkinColor, Is.EqualTo(customColor));
            Assert.That(loaded.TryGetValue("body.weight", out float weight), Is.True);
            Assert.That(weight, Is.EqualTo(-0.4f).Within(0.0001f));
        }

        [Test]
        public void RepositoryStoresMultiplePlayersAndRequiresExplicitOverwrite()
        {
            string path = Path.Combine(testDirectory, "players.json");
            var repository = new CharacterPlayerSaveRepository(path);
            CharacterRecipe recipe = CreateRecipe(0.2f);

            Assert.That(repository.TrySavePlayer(
                "Avery", recipe, false, out PlayerCharacterRecord first, out bool duplicate, out string error), Is.True, error);
            Assert.That(duplicate, Is.False);
            Assert.That(repository.TrySavePlayer(
                "Morgan", CreateRecipe(-0.3f), false, out _, out duplicate, out error), Is.True, error);

            Assert.That(repository.TrySavePlayer(
                "avery", CreateRecipe(0.6f), false, out _, out duplicate, out error), Is.False);
            Assert.That(duplicate, Is.True);
            Assert.That(repository.TrySavePlayer(
                "avery", CreateRecipe(0.6f), true, out PlayerCharacterRecord overwritten, out duplicate, out error), Is.True, error);
            Assert.That(overwritten.Id, Is.EqualTo(first.Id));

            Assert.That(repository.TryLoad(out CharacterPlayerSaveData data, out error), Is.True, error);
            Assert.That(data.Players.Count, Is.EqualTo(2));
            Assert.That(repository.TryFindByName("AVERY", out PlayerCharacterRecord found, out error), Is.True, error);
            Assert.That(found.Id, Is.EqualTo(first.Id));
            Assert.That(found.Recipe.TryGetValue("body.weight", out float weight), Is.True);
            Assert.That(weight, Is.EqualTo(0.6f).Within(0.0001f));
        }

        [Test]
        public void RepositoryRejectsMalformedJson()
        {
            string path = Path.Combine(testDirectory, "players.json");
            File.WriteAllText(path, "{ not valid json");
            var repository = new CharacterPlayerSaveRepository(path);

            Assert.That(repository.TryLoad(out _, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void RandomizePreservesSexAndUsesRestrainedRanges()
        {
            var root = new GameObject("Randomize Test");
            try
            {
                CharacterMorphController controller = root.AddComponent<CharacterMorphController>();
                controller.SetSex(CharacterSex.Male);
                controller.RandomizeCurrent(0.65f, new System.Random(1234));

                Assert.That(controller.ActiveSex, Is.EqualTo(CharacterSex.Male));
                foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
                {
                    float minimum = definition.MinimumValue < 0f ? definition.MinimumValue * 0.65f : 0f;
                    Assert.That(controller.GetMorph(definition.Id), Is.InRange(minimum, 0.65f), definition.Id);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static CharacterRecipe CreateRecipe(float weight)
        {
            return new CharacterRecipe(
                CharacterSex.Female,
                "fair",
                false,
                Color.white,
                new[] { new CharacterMorphValue("body.weight", weight) });
        }
    }
}
