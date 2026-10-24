using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTests
{
    public sealed class SkinColorModelEditModeTests
    {
        // Monk Skin Tone Scale (Ellis Monk, Google, CC BY 4.0), fairest to deepest.
        private static readonly string[] MonkSwatches =
        {
            "#F6EDE4", "#F3E7DB", "#F7EAD0", "#EADABA", "#D7BD96",
            "#A07E56", "#825C43", "#604134", "#3A312A", "#292420"
        };

        // Hemoglobin and undertone at which the model was fitted to the Monk swatches.
        private const float FitHemoglobin = 0.1283f;
        private const float FitUndertone = 0.35f;

        [Test]
        public void MonkSwatchesSitAtEvenMelaninSteps()
        {
            for (int index = 0; index < MonkSwatches.Length; index++)
            {
                Assert.That(ColorUtility.TryParseHtmlString(MonkSwatches[index], out Color expected), Is.True);
                float melanin = (index + 1) / 10f;
                Color actual = SkinColorModel.EvaluateLinear(
                    new CharacterSkinParameters(melanin, FitHemoglobin, FitUndertone)).gamma;

                Assert.That(actual.r, Is.EqualTo(expected.r).Within(12f / 255f), $"{MonkSwatches[index]} red");
                Assert.That(actual.g, Is.EqualTo(expected.g).Within(12f / 255f), $"{MonkSwatches[index]} green");
                Assert.That(actual.b, Is.EqualTo(expected.b).Within(12f / 255f), $"{MonkSwatches[index]} blue");
            }
        }

        [Test]
        public void MelaninDarkensMonotonically()
        {
            float previous = float.MaxValue;
            for (int step = 0; step <= 20; step++)
            {
                Color color = SkinColorModel.EvaluateLinear(new CharacterSkinParameters(step / 20f, 0.15f, 0.35f));
                float luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
                Assert.That(luminance, Is.LessThan(previous), $"melanin {step / 20f}");
                previous = luminance;
            }
        }

        [Test]
        public void HemoglobinReddensAndUndertoneWarms()
        {
            Color pale = SkinColorModel.EvaluateLinear(new CharacterSkinParameters(0.2f, 0f, 0.35f));
            Color flushed = SkinColorModel.EvaluateLinear(new CharacterSkinParameters(0.2f, 1f, 0.35f));
            Assert.That(flushed.r / flushed.g, Is.GreaterThan(pale.r / pale.g));

            Color olive = SkinColorModel.EvaluateLinear(new CharacterSkinParameters(0.5f, 0.15f, 0f));
            Color golden = SkinColorModel.EvaluateLinear(new CharacterSkinParameters(0.5f, 0.15f, 1f));
            Assert.That(golden.r / golden.b, Is.GreaterThan(olive.r / olive.b));
        }

        [Test]
        public void GeneratedTableMatchesModel()
        {
            Texture2D table = SkinColorModel.GenerateLut();
            try
            {
                Assert.That(table.width, Is.EqualTo(SkinColorModel.DefaultLutSize * SkinColorModel.LutSliceCount));
                Assert.That(table.height, Is.EqualTo(SkinColorModel.DefaultLutSize));

                var random = new System.Random(7);
                for (int sample = 0; sample < 50; sample++)
                {
                    var parameters = new CharacterSkinParameters(
                        (float)random.NextDouble(),
                        (float)random.NextDouble(),
                        (float)random.NextDouble());
                    Vector3 model = SkinColorModel.LinearToLab(SkinColorModel.EvaluateLinear(parameters));
                    Vector3 sampled = SkinColorModel.LinearToLab(SkinColorModel.SampleLutLinear(table, parameters));
                    Assert.That((sampled - model).magnitude, Is.LessThan(2.5f), $"sample {sample}");
                }
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void FindClosestRecoversModelColours()
        {
            var original = new CharacterSkinParameters(0.45f, 0.3f, 0.6f);
            Color target = SkinColorModel.EvaluateLinear(original);

            CharacterSkinParameters found = SkinColorModel.FindClosest(target);
            Color recovered = SkinColorModel.EvaluateLinear(found);

            Assert.That((SkinColorModel.LinearToLab(recovered) - SkinColorModel.LinearToLab(target)).magnitude,
                Is.LessThan(1f));
        }

        [Test]
        public void RecipeStoresSkinParametersAndOlderRecipesHaveNone()
        {
            var recipe = new CharacterRecipe();
            recipe.SetSkinParameters(new CharacterSkinParameters(0.7f, 0.2f, 0.9f));

            CharacterRecipe loaded = JsonUtility.FromJson<CharacterRecipe>(JsonUtility.ToJson(recipe));
            Assert.That(loaded.HasSkinParameters, Is.True);
            Assert.That(loaded.SkinParameters.Melanin, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(loaded.SkinParameters.Undertone, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(recipe.Copy().HasSkinParameters, Is.True);

            CharacterRecipe legacy = JsonUtility.FromJson<CharacterRecipe>(
                "{\"version\":2,\"sex\":0,\"skinToneId\":\"fair\",\"usesCustomSkinColor\":false}");
            Assert.That(legacy.HasSkinParameters, Is.False);
        }

        [Test]
        public void ProfileMatchesCustomColourAndRoundTripsParameters()
        {
            var manager = new GameObject("Skin Profile Test");
            try
            {
                var controller = manager.AddComponent<CharacterMorphController>();
                var profile = manager.AddComponent<CharacterProfile>();
                var serialized = new SerializedObject(profile);
                serialized.FindProperty("controller").objectReferenceValue = controller;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(ColorUtility.TryParseHtmlString(MonkSwatches[6], out Color swatch), Is.True);
                profile.SetCustomSkinColor(swatch);
                Assert.That(profile.SkinParameters.Melanin, Is.EqualTo(0.7f).Within(0.1f));

                var chosen = new CharacterSkinParameters(0.33f, 0.4f, 0.8f);
                profile.SetSkinParameters(chosen);
                CharacterRecipe recipe = profile.CaptureRecipe();
                Assert.That(recipe.HasSkinParameters, Is.True);

                profile.SetSkinParameters(CharacterSkinParameters.Default);
                Assert.That(profile.ApplyRecipe(recipe), Is.True);
                Assert.That(profile.SkinParameters.Melanin, Is.EqualTo(0.33f).Within(0.0001f));
                Assert.That(profile.SkinParameters.Hemoglobin, Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(profile.SkinParameters.Undertone, Is.EqualTo(0.8f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(manager);
            }
        }
    }
}
