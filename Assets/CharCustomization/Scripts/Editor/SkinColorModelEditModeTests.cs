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
        public void MelaninSliderInvertsConcentrationAndUnitDensityScaleIsIdentity()
        {
            for (int step = 0; step <= 40; step++)
            {
                float melanin = step / 40f;
                Assert.That(SkinColorModel.MelaninSlider(SkinColorModel.MelaninConcentration(melanin)),
                    Is.EqualTo(melanin).Within(0.0001f));
                Assert.That(SkinColorModel.ScaleMelaninDensity(melanin, 1f), Is.EqualTo(melanin).Within(0.0001f));
            }
        }

        // Region scales (profile defaults: lips 0.85 density + 0.35 hemoglobin, palms 0.3
        // density + 0.05) must keep each region in proportion to the skin around it.
        // Scaling the perceptual slider instead made deep-skin palms nearly white and
        // lips a light red, a caricature this test guards against.
        [Test]
        public void PalmsAndLipsStayInProportionAtEveryTone()
        {
            for (int step = 0; step <= 10; step++)
            {
                float melanin = step / 10f;
                Color body = Evaluate(melanin, 0.15f);
                Color lips = Evaluate(SkinColorModel.ScaleMelaninDensity(melanin, 0.85f), 0.5f);
                Color palms = Evaluate(SkinColorModel.ScaleMelaninDensity(melanin, 0.3f), 0.2f);
                Color fourStepsLighter = Evaluate(Mathf.Max(melanin - 0.4f, 0f), 0.15f);

                Assert.That(Luminance(palms), Is.LessThanOrEqualTo(Luminance(fourStepsLighter) + 0.001f),
                    $"palms at melanin {melanin} are lighter than skin four Monk steps lighter");
                if (melanin >= 0.7f)
                {
                    Assert.That(Vector3.Distance(SkinColorModel.LinearToLab(lips), SkinColorModel.LinearToLab(body)),
                        Is.LessThan(10f), $"lips at melanin {melanin} stand out from the skin");
                }
            }

            Vector3 fairBody = SkinColorModel.LinearToLab(Evaluate(0.1f, 0.15f));
            Vector3 fairLips = SkinColorModel.LinearToLab(Evaluate(SkinColorModel.ScaleMelaninDensity(0.1f, 0.85f), 0.5f));
            Assert.That(fairLips.y - fairBody.y, Is.GreaterThan(5f), "fair lips should still read rosier than the skin");
        }

        // The demo palette's fantasy tones: zombie, alien, drow, moon.
        private static readonly Color[] FantasyReferences =
        {
            new(0.2475f, 0.45f, 0.25965f), new(0.52065f, 0.801f, 0.5375148f),
            new(0.5124212f, 0.33633602f, 0.53900003f), new(0.51675004f, 0.67856884f, 0.795f)
        };

        // Fantasy skin is human skin with one more pigment: the reference colour is hit
        // exactly, tint 0 is plain human skin, melanin still darkens steadily, and blood
        // still flushes the colour redder, as it does in real skin.
        [Test]
        public void FantasyPigmentHitsReferenceOverHumanSkin()
        {
            foreach (Color reference in FantasyReferences)
            {
                SkinPigments pigments = SkinPigments.FromReference(reference);
                Assert.That(pigments.ThirdAxis, Is.EqualTo(SkinThirdAxis.Tint));
                Assert.That(LabDistance(SkinColorModel.EvaluateLinear(SkinPigments.ReferenceParameters, pigments), reference.linear),
                    Is.LessThan(0.5f), $"{reference} reference");

                float previous = float.MaxValue;
                for (int step = 0; step <= 10; step++)
                {
                    float melanin = step / 10f;
                    Color bare = SkinColorModel.EvaluateLinear(new CharacterSkinParameters(melanin, 0.4f, 0f), pigments);
                    Color human = SkinColorModel.EvaluateLinear(new CharacterSkinParameters(
                        melanin * SkinPigments.FantasyMaxMelanin, 0.4f, SkinPigments.FantasyUndertone));
                    Assert.That(LabDistance(bare, human), Is.LessThan(0.01f), $"{reference} tint 0 at melanin {melanin}");

                    float luminance = Luminance(SkinColorModel.EvaluateLinear(new CharacterSkinParameters(melanin, 0.15f, 1f), pigments));
                    Assert.That(luminance, Is.LessThan(previous), $"{reference} melanin {melanin}");
                    previous = luminance;
                }

                Vector3 calm = SkinColorModel.LinearToLab(SkinColorModel.EvaluateLinear(new CharacterSkinParameters(0.3f, 0.15f, 1f), pigments));
                Vector3 flushed = SkinColorModel.LinearToLab(SkinColorModel.EvaluateLinear(new CharacterSkinParameters(0.3f, 0.8f, 1f), pigments));
                Assert.That(flushed.y - calm.y, Is.GreaterThan(2f), $"{reference} flush");
            }
        }

        // Capping fantasy melanin keeps the deepest shades of each space recognisable
        // instead of every space ending in the same human near-black brown.
        [Test]
        public void FantasySpacesStayDistinctAtTheirDeepest()
        {
            var deepest = new Color[FantasyReferences.Length];
            for (int index = 0; index < FantasyReferences.Length; index++)
            {
                deepest[index] = SkinColorModel.EvaluateLinear(
                    new CharacterSkinParameters(1f, 0.15f, 1f), SkinPigments.FromReference(FantasyReferences[index]));
            }

            for (int first = 0; first < deepest.Length; first++)
            {
                for (int second = first + 1; second < deepest.Length; second++)
                {
                    Assert.That(LabDistance(deepest[first], deepest[second]), Is.GreaterThan(8f), $"{first} vs {second}");
                }
            }
        }

        // Every free hue shares one lightness and chroma, so no hue reads as paint.
        // Pigment only absorbs, so the few cyan-blue hues that need more blue than the
        // skin beneath reflects land up to about 2 Lab units short.
        [Test]
        public void FreeHueReferencesShareLightnessAndChroma()
        {
            for (int hue = 0; hue < 360; hue += 15)
            {
                Color reference = SkinPigments.FreeHueReference(hue);
                Vector3 lab = SkinColorModel.LinearToLab(reference.linear);
                Assert.That(lab.x, Is.EqualTo(SkinPigments.FreeHueLightness).Within(1f), $"hue {hue} lightness");
                Assert.That(new Vector2(lab.y, lab.z).magnitude, Is.EqualTo(SkinPigments.FreeHueChroma).Within(1.5f), $"hue {hue} chroma");
                float angle = Mathf.Atan2(lab.z, lab.y) * Mathf.Rad2Deg;
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(angle, hue)), Is.LessThan(2f), $"hue {hue} angle");

                Color reached = SkinColorModel.EvaluateLinear(SkinPigments.ReferenceParameters, SkinPigments.FromHue(hue));
                Assert.That(LabDistance(reached, reference.linear), Is.LessThan(2f), $"hue {hue} reached");
            }
        }

        [Test]
        public void GeneratedFantasyTableMatchesModel()
        {
            SkinPigments[] spaces = { SkinPigments.FromReference(FantasyReferences[0]), SkinPigments.FromHue(200f) };
            foreach (SkinPigments pigments in spaces)
            {
                Texture2D table = SkinColorModel.GenerateLut(SkinColorModel.DefaultLutSize, pigments);
                try
                {
                    Assert.That(SkinColorModel.SliceCount(table), Is.EqualTo(SkinColorModel.LutSliceCount));
                    var random = new System.Random(11);
                    for (int sample = 0; sample < 50; sample++)
                    {
                        var parameters = new CharacterSkinParameters(
                            (float)random.NextDouble(),
                            (float)random.NextDouble(),
                            (float)random.NextDouble());
                        Assert.That(LabDistance(
                                SkinColorModel.SampleLutLinear(table, parameters),
                                SkinColorModel.EvaluateLinear(parameters, pigments)),
                            Is.LessThan(2.5f), $"sample {sample}");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(table);
                }
            }
        }

        [Test]
        public void FreeHueTablesAreSharedPerWholeDegree()
        {
            SkinColorTableCache.Clear();
            try
            {
                Texture2D first = SkinColorTableCache.GetFreeHueTable(40.2f);
                Assert.That(SkinColorTableCache.GetFreeHueTable(39.8f), Is.SameAs(first));
                Assert.That(SkinColorTableCache.GetFreeHueTable(400f), Is.SameAs(first));
                Assert.That(SkinColorTableCache.GetFreeHueTable(41f), Is.Not.SameAs(first));
                Assert.That(SkinColorTableCache.Count, Is.EqualTo(2));
            }
            finally
            {
                SkinColorTableCache.Clear();
            }

            Assert.That(SkinColorTableCache.Count, Is.EqualTo(0));
        }

        [Test]
        public void FantasyToneSwitchesColourSpaceAndRecipesKeepIt()
        {
            var manager = new GameObject("Fantasy Skin Test");
            var zombie = ScriptableObject.CreateInstance<CharacterSkinProfile>();
            var palette = ScriptableObject.CreateInstance<CharacterSkinPalette>();
            try
            {
                var serializedZombie = new SerializedObject(zombie);
                serializedZombie.FindProperty("useReferenceColor").boolValue = true;
                serializedZombie.FindProperty("referenceColor").colorValue = FantasyReferences[0];
                serializedZombie.ApplyModifiedPropertiesWithoutUndo();
                palette.ConfigureDefaults(new[]
                {
                    new CharacterSkinTone("fair", "Fair", new Color(0.92f, 0.72f, 0.57f)),
                    new CharacterSkinTone("zombie", "Zombie", FantasyReferences[0], zombie, SkinPigments.ReferenceParameters)
                }, "fair");

                var controller = manager.AddComponent<CharacterMorphController>();
                var profile = manager.AddComponent<CharacterProfile>();
                var serialized = new SerializedObject(profile);
                serialized.FindProperty("controller").objectReferenceValue = controller;
                serialized.FindProperty("skinPalette").objectReferenceValue = palette;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(profile.SetSkinTone("zombie"), Is.True);
                Assert.That(profile.SkinProfile, Is.SameAs(zombie));
                Color zombieColor = profile.EvaluateSkinColor(profile.SkinParameters);
                Assert.That(zombieColor.g, Is.GreaterThan(zombieColor.r), "zombie skin should be green");

                // A custom edit stays in the zombie colour space and survives a save.
                profile.SetSkinParameters(new CharacterSkinParameters(0.5f, 0.1f, 0.9f));
                CharacterRecipe recipe = profile.CaptureRecipe();
                CharacterRecipe loaded = JsonUtility.FromJson<CharacterRecipe>(JsonUtility.ToJson(recipe));

                Assert.That(profile.SetSkinTone("fair"), Is.True);
                Assert.That(profile.SkinProfile, Is.Null);

                Assert.That(profile.ApplyRecipe(loaded), Is.True);
                Assert.That(profile.SkinProfile, Is.SameAs(zombie));
                Assert.That(profile.SkinToneId, Is.EqualTo("zombie"));
                Assert.That(profile.SkinParameters.Melanin, Is.EqualTo(0.5f).Within(0.0001f));
                Color restored = profile.EvaluateSkinColor(profile.SkinParameters);
                Assert.That(restored.g, Is.GreaterThan(restored.r), "restored skin should still be green");
            }
            finally
            {
                Object.DestroyImmediate(manager);
                Object.DestroyImmediate(zombie);
                Object.DestroyImmediate(palette);
            }
        }

        private static Color Evaluate(float melanin, float hemoglobin)
        {
            return SkinColorModel.EvaluateLinear(new CharacterSkinParameters(melanin, hemoglobin, FitUndertone));
        }

        private static float LabDistance(Color first, Color second)
        {
            return (SkinColorModel.LinearToLab(first) - SkinColorModel.LinearToLab(second)).magnitude;
        }

        private static float Luminance(Color linear)
        {
            return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
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
