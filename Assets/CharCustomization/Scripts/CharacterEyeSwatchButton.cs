using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterEyeSwatchButton : MonoBehaviour
    {
        [SerializeField] private string eyeMaterialId;
        [SerializeField] private Button button;
        [SerializeField] private Graphic previewGraphic;
        [SerializeField] private TMP_Text label;

        public string EyeMaterialId => eyeMaterialId;
        public Button Button => button;
        public Graphic PreviewGraphic => previewGraphic;
        public TMP_Text Label => label;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(eyeMaterialId) && button != null && previewGraphic != null;

#if UNITY_EDITOR
        public void Configure(
            string materialId,
            CharacterEyeMaterialOption option,
            Button targetButton,
            Graphic targetPreview,
            TMP_Text targetLabel)
        {
            eyeMaterialId = materialId;
            button = targetButton;
            previewGraphic = targetPreview;
            label = targetLabel;
            SetDisplay(option);
        }
#endif

        public void SetDisplay(CharacterEyeMaterialOption option)
        {
            if (option == null)
            {
                return;
            }

            if (previewGraphic != null)
            {
                if (previewGraphic is RawImage rawImage)
                {
                    rawImage.texture = ResolvePreviewTexture(option.Material);
                }

                previewGraphic.material = option.Material;
                previewGraphic.color = Color.white;
            }

            if (label != null)
            {
                label.text = option.Label;
            }
        }

        private static Texture ResolvePreviewTexture(Material material)
        {
            if (material == null)
            {
                return null;
            }

            if (material.HasProperty("_BaseMap"))
            {
                return material.GetTexture("_BaseMap");
            }

            if (material.HasProperty("_MainTex"))
            {
                return material.GetTexture("_MainTex");
            }

            return material.mainTexture;
        }
    }
}
