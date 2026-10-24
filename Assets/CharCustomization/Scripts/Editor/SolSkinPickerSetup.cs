using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization.EditorTools
{
    // Converts the demo menu's HSV custom-colour panel into the skin parameter
    // picker. The panel keeps its size: a shorter preview strip, a melanin by
    // hemoglobin plane, and the three existing slider rows relabelled.
    public static class SolSkinPickerSetup
    {
        private const string DemoMenuPrefabPath = "Assets/CharCustomization/Resources/Prefabs/CharacterMorphMenuDemoUI.prefab";
        private const float PreviewHeight = 22f;
        private const float PlaneHeight = 96f;
        private const float RowSpacing = 5f;
        private const float MarkerSize = 16f;

        [MenuItem("Tools/Sol/Character Customization/Skin/Build Skin Picker In Demo Menu")]
        private static void BuildInDemoMenu()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(DemoMenuPrefabPath);
            try
            {
                if (!TryBuild(root, out string message))
                {
                    Debug.LogError(message);
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, DemoMenuPrefabPath);
                Debug.Log(message);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static bool TryBuild(GameObject menuRoot, out string message)
        {
            CharacterMorphDemoUI menu = menuRoot.GetComponentInChildren<CharacterMorphDemoUI>(true);
            if (menu == null)
            {
                message = $"No {nameof(CharacterMorphDemoUI)} found under '{menuRoot.name}'.";
                return false;
            }

            if (menuRoot.GetComponentInChildren<CharacterSkinParameterPicker>(true) != null)
            {
                message = $"'{menuRoot.name}' already has a {nameof(CharacterSkinParameterPicker)}.";
                return false;
            }

            var serializedMenu = new SerializedObject(menu);
            var panel = serializedMenu.FindProperty("customColorPanel").objectReferenceValue as GameObject;
            var melanin = serializedMenu.FindProperty("hueSlider").objectReferenceValue as Slider;
            var hemoglobin = serializedMenu.FindProperty("saturationSlider").objectReferenceValue as Slider;
            var undertone = serializedMenu.FindProperty("valueSlider").objectReferenceValue as Slider;
            var preview = serializedMenu.FindProperty("customColorPreview").objectReferenceValue as Image;
            if (panel == null || melanin == null || hemoglobin == null || undertone == null || preview == null)
            {
                message = "The menu's custom colour panel, HSV sliders, and preview must all be assigned before building the picker.";
                return false;
            }

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = RowSpacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            SetHeight(preview.rectTransform, PreviewHeight);
            float width = ((RectTransform)panel.transform).rect.width;
            CharacterSkinPlaneArea plane = CreatePlane(panel.transform, width);
            plane.transform.SetSiblingIndex(preview.transform.GetSiblingIndex() + 1);

            Relabel(melanin, "Melanin");
            Relabel(hemoglobin, "Hemoglobin");
            Relabel(undertone, "Undertone");

            var picker = panel.AddComponent<CharacterSkinParameterPicker>();
            var serializedPicker = new SerializedObject(picker);
            serializedPicker.FindProperty("plane").objectReferenceValue = plane;
            serializedPicker.FindProperty("melaninSlider").objectReferenceValue = melanin;
            serializedPicker.FindProperty("hemoglobinSlider").objectReferenceValue = hemoglobin;
            serializedPicker.FindProperty("undertoneSlider").objectReferenceValue = undertone;
            serializedPicker.FindProperty("preview").objectReferenceValue = preview;
            serializedPicker.ApplyModifiedPropertiesWithoutUndo();

            serializedMenu.FindProperty("skinParameterPicker").objectReferenceValue = picker;
            serializedMenu.ApplyModifiedPropertiesWithoutUndo();

            message = $"Built the skin parameter picker in '{panel.name}'.";
            return true;
        }

        private static CharacterSkinPlaneArea CreatePlane(Transform parent, float width)
        {
            var planeObject = new GameObject(
                "Skin Colour Plane",
                typeof(RectTransform),
                typeof(RawImage),
                typeof(LayoutElement),
                typeof(CharacterSkinPlaneArea));
            planeObject.layer = parent.gameObject.layer;
            var planeRect = (RectTransform)planeObject.transform;
            planeRect.SetParent(parent, false);
            planeRect.anchorMin = new Vector2(0f, 1f);
            planeRect.anchorMax = new Vector2(0f, 1f);
            planeRect.pivot = new Vector2(0.5f, 0.5f);
            planeRect.sizeDelta = new Vector2(width, PlaneHeight);
            var layoutElement = planeObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = width;
            layoutElement.preferredHeight = PlaneHeight;

            var markerObject = new GameObject("Marker", typeof(RectTransform), typeof(Image), typeof(Outline));
            markerObject.layer = planeObject.layer;
            var markerRect = (RectTransform)markerObject.transform;
            markerRect.SetParent(planeRect, false);
            markerRect.sizeDelta = new Vector2(MarkerSize, MarkerSize);
            var markerImage = markerObject.GetComponent<Image>();
            markerImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            markerImage.raycastTarget = false;
            var outline = markerObject.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var plane = planeObject.GetComponent<CharacterSkinPlaneArea>();
            var serializedPlane = new SerializedObject(plane);
            serializedPlane.FindProperty("marker").objectReferenceValue = markerRect;
            serializedPlane.ApplyModifiedPropertiesWithoutUndo();
            return plane;
        }

        private static void SetHeight(RectTransform rect, float height)
        {
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            if (rect.TryGetComponent(out LayoutElement layoutElement))
            {
                layoutElement.preferredHeight = height;
            }
        }

        // Renames the slider, its row, and the row's label so the hierarchy reads as
        // the parameter it now controls.
        private static void Relabel(Slider slider, string label)
        {
            slider.gameObject.name = $"{label} Slider";
            Transform row = slider.transform.parent;
            if (row == null)
            {
                return;
            }

            row.gameObject.name = $"{label} Row";
            foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.transform.IsChildOf(slider.transform))
                {
                    continue;
                }

                text.text = label;
                text.gameObject.name = $"{label} Label";
            }
        }
    }
}
