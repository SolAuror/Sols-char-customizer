using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterMorphSliderRow : MonoBehaviour
    {
        [SerializeField] private string morphId;
        [SerializeField] private Slider slider;
        [SerializeField] private TMP_Text label;
        [SerializeField] private TMP_Text valueText;

        private CharacterMorphController controller;
        private UnityAction<float> valueChanged;

        public string MorphId => morphId;
        public Slider Slider => slider;
        public TMP_Text Label => label;
        public TMP_Text ValueText => valueText;
        public bool IsConfigured => slider != null && label != null && valueText != null;

        public void SetMorphId(string id)
        {
            morphId = id;
        }

        public bool Bind(CharacterMorphController morphController, CharacterMorphDefinition definition)
        {
            Unbind();
            if (morphController == null || definition == null || !IsConfigured)
            {
                return false;
            }

            controller = morphController;
            morphId = definition.Id;
            label.text = definition.Label;
            slider.minValue = definition.MinimumValue;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            valueChanged = OnValueChanged;
            slider.onValueChanged.AddListener(valueChanged);
            Refresh();
            return true;
        }

        public void Refresh()
        {
            if (controller == null || slider == null || valueText == null)
            {
                return;
            }

            float value = controller.GetMorph(morphId);
            slider.SetValueWithoutNotify(value);
            valueText.text = value.ToString("0.00");
            slider.interactable = true;
        }

        public void Unbind()
        {
            if (slider != null && valueChanged != null)
            {
                slider.onValueChanged.RemoveListener(valueChanged);
            }

            valueChanged = null;
            controller = null;
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void OnValueChanged(float value)
        {
            controller.SetMorph(morphId, value);
            valueText.text = value.ToString("0.00");
        }
    }
}
