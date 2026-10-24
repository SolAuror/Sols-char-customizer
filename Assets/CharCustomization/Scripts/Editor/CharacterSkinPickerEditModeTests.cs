using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization.EditorTests
{
    public sealed class CharacterSkinPickerEditModeTests
    {
        private const float Tolerance = 0.0001f;

        private readonly List<Object> created = new();
        private CharacterProfile profile;
        private CharacterSkinParameterPicker picker;
        private Slider melaninSlider;
        private Slider hemoglobinSlider;
        private Slider undertoneSlider;
        private CharacterSkinPlaneArea plane;

        [SetUp]
        public void SetUp()
        {
            var manager = Track(new GameObject("Skin Picker Profile"));
            var controller = manager.AddComponent<CharacterMorphController>();
            profile = manager.AddComponent<CharacterProfile>();
            Assign(profile, "controller", controller);

            var panel = Track(new GameObject("Skin Picker", typeof(RectTransform)));
            melaninSlider = CreateSlider("Melanin Slider", panel.transform);
            hemoglobinSlider = CreateSlider("Hemoglobin Slider", panel.transform);
            undertoneSlider = CreateSlider("Undertone Slider", panel.transform);

            var planeObject = new GameObject("Plane", typeof(RectTransform), typeof(RawImage), typeof(CharacterSkinPlaneArea));
            planeObject.transform.SetParent(panel.transform, false);
            ((RectTransform)planeObject.transform).sizeDelta = new Vector2(200f, 50f);
            plane = planeObject.GetComponent<CharacterSkinPlaneArea>();

            picker = panel.AddComponent<CharacterSkinParameterPicker>();
            Assign(picker, "plane", plane);
            Assign(picker, "melaninSlider", melaninSlider);
            Assign(picker, "hemoglobinSlider", hemoglobinSlider);
            Assign(picker, "undertoneSlider", undertoneSlider);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object target in created)
            {
                if (target != null)
                {
                    Object.DestroyImmediate(target);
                }
            }

            created.Clear();
        }

        [Test]
        public void BindShowsProfileParameters()
        {
            profile.SetSkinParameters(new CharacterSkinParameters(0.6f, 0.2f, 0.7f));
            picker.Bind(profile);

            Assert.That(melaninSlider.value, Is.EqualTo(0.6f).Within(Tolerance));
            Assert.That(hemoglobinSlider.value, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(undertoneSlider.value, Is.EqualTo(0.7f).Within(Tolerance));
            Assert.That(plane.Value.x, Is.EqualTo(0.6f).Within(Tolerance));
            Assert.That(plane.Value.y, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(plane.GetComponent<RawImage>().texture, Is.Not.Null);
        }

        [Test]
        public void SliderEditsUpdateProfileAndPlane()
        {
            picker.Bind(profile);
            CharacterSkinParameters? reported = null;
            picker.ParametersChanged += parameters => reported = parameters;

            melaninSlider.value = 0.8f;
            undertoneSlider.value = 0.1f;

            Assert.That(profile.SkinParameters.Melanin, Is.EqualTo(0.8f).Within(Tolerance));
            Assert.That(profile.SkinParameters.Undertone, Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(plane.Value.x, Is.EqualTo(0.8f).Within(Tolerance));
            Assert.That(reported.HasValue, Is.True);
            Assert.That(reported.Value.Undertone, Is.EqualTo(0.1f).Within(Tolerance));
        }

        [Test]
        public void MelaninGradientRunsFromLightToDark()
        {
            picker.Bind(profile);

            Image background = melaninSlider.transform.Find("Background").GetComponent<Image>();
            Assert.That(background.sprite, Is.Not.Null);
            Texture2D gradient = background.sprite.texture;
            Color light = gradient.GetPixel(0, 0);
            Color dark = gradient.GetPixel(gradient.width - 1, 0);
            Assert.That(dark.grayscale, Is.LessThan(light.grayscale));
        }

        private Slider CreateSlider(string sliderName, Transform parent)
        {
            var sliderObject = new GameObject(sliderName, typeof(RectTransform), typeof(Slider));
            sliderObject.transform.SetParent(parent, false);
            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(sliderObject.transform, false);
            return sliderObject.GetComponent<Slider>();
        }

        private GameObject Track(GameObject target)
        {
            created.Add(target);
            return target;
        }

        private static void Assign(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
