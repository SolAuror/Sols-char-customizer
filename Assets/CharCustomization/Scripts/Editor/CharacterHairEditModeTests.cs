using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTests
{
    public sealed class CharacterHairEditModeTests
    {
        [Test]
        public void RecipeStoresHairAndOlderRecipesHaveNone()
        {
            var recipe = new CharacterRecipe();
            recipe.SetHair(" M14 ", new Color(0.7f, 0.08f, 0.08f, 0.5f), 1.4f);

            CharacterRecipe loaded = JsonUtility.FromJson<CharacterRecipe>(JsonUtility.ToJson(recipe));
            Assert.That(loaded.HasHair, Is.True);
            Assert.That(loaded.HairStyleId, Is.EqualTo("M14"));
            Assert.That(loaded.HairColor.r, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(loaded.HairColor.a, Is.EqualTo(1f), "hair colour is opaque");
            Assert.That(loaded.HairSheen, Is.EqualTo(1f), "sheen is clamped to 0-1");
            Assert.That(recipe.Copy().HairStyleId, Is.EqualTo("M14"));
            Assert.That(recipe.Copy().HairSheen, Is.EqualTo(1f));

            CharacterRecipe noSheen = JsonUtility.FromJson<CharacterRecipe>(
                "{\"version\":4,\"hasHair\":true,\"hairStyleId\":\"M14\"}");
            Assert.That(noSheen.HairSheen, Is.EqualTo(CharacterHair.DefaultSheen),
                "recipes saved before sheen get the default");

            CharacterRecipe legacy = JsonUtility.FromJson<CharacterRecipe>(
                "{\"version\":3,\"sex\":0,\"skinToneId\":\"fair\",\"usesCustomSkinColor\":false}");
            Assert.That(legacy.HasHair, Is.False);
        }

        [Test]
        public void HairTabIsOptionalAndSitsAfterSkin()
        {
            IReadOnlyList<string> without = CharacterCustomizationUiConfig.ExpectedTabs(
                new HashSet<string> { "Presets", "Skin", "Body" });
            IReadOnlyList<string> with = CharacterCustomizationUiConfig.ExpectedTabs(
                new HashSet<string> { CharacterCustomizationUiConfig.HairGroupId });

            Assert.That(without, Does.Not.Contain(CharacterCustomizationUiConfig.HairGroupId));
            Assert.That(without, Does.Not.Contain(CharacterCustomizationUiConfig.TattooGroupId));
            // Hair and Tattoos are both optional.
            Assert.That(without.Count, Is.EqualTo(CharacterCustomizationUiConfig.TabGroups.Count - 2));
            Assert.That(with[2], Is.EqualTo(CharacterCustomizationUiConfig.HairGroupId));
            Assert.That(with[1], Is.EqualTo(CharacterCustomizationUiConfig.SkinGroupId));
            Assert.That(CharacterCustomizationUiConfig.IsKnownGroup(CharacterCustomizationUiConfig.HairGroupId), Is.True);

            IReadOnlyList<string> both = CharacterCustomizationUiConfig.ExpectedTabs(
                new HashSet<string> { CharacterCustomizationUiConfig.HairGroupId, CharacterCustomizationUiConfig.TattooGroupId });
            Assert.That(both[3], Is.EqualTo(CharacterCustomizationUiConfig.TattooGroupId), "Tattoos sits after Hair");
            Assert.That(CharacterCustomizationUiConfig.IsKnownGroup(CharacterCustomizationUiConfig.TattooGroupId), Is.True);
        }

        [Test]
        public void PaletteFallsBackToItsDefaultColour()
        {
            var palette = ScriptableObject.CreateInstance<CharacterHairPalette>();
            try
            {
                palette.ConfigureDefaults(new[]
                {
                    new CharacterHairColorOption("black", "Black", Color.black),
                    new CharacterHairColorOption("red", "Red", Color.red),
                }, "red");

                Assert.That(palette.GetDefault().Id, Is.EqualTo("red"));
                Assert.That(palette.TryGet("black", out CharacterHairColorOption black), Is.True);
                Assert.That(black.Color, Is.EqualTo(Color.black));
                Assert.That(palette.TryGet("missing", out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(palette);
            }
        }
    }
}
