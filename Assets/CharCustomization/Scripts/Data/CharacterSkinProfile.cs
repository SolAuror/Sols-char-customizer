using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Shader property names shared by the skin profile, the character profile, and
    // the editor material tools.
    public static class SolSkinShaderIds
    {
        public const string ShaderName = "Sol/Character Skin";

        public static readonly int SkinLut = Shader.PropertyToID("_SkinLut");
        public static readonly int SkinLutTexelSize = Shader.PropertyToID("_SkinLut_TexelSize");
        public static readonly int Melanin = Shader.PropertyToID("_Melanin");
        public static readonly int Hemoglobin = Shader.PropertyToID("_Hemoglobin");
        public static readonly int Undertone = Shader.PropertyToID("_Undertone");
        public static readonly int BaseMapMean = Shader.PropertyToID("_BaseMapMean");
        public static readonly int LipMelaninScale = Shader.PropertyToID("_LipMelaninScale");
        public static readonly int LipHemoglobinAdd = Shader.PropertyToID("_LipHemoglobinAdd");
        public static readonly int PalmMelaninScale = Shader.PropertyToID("_PalmMelaninScale");
        public static readonly int PalmHemoglobinAdd = Shader.PropertyToID("_PalmHemoglobinAdd");
        public static readonly int BlushHemoglobinAdd = Shader.PropertyToID("_BlushHemoglobinAdd");
        public static readonly int AreolaMelaninScale = Shader.PropertyToID("_AreolaMelaninScale");

        public static bool IsSkinMaterial(Material material)
        {
            return material != null && material.HasProperty(Melanin) && material.HasProperty(SkinLut);
        }
    }

    // A skin colour space for one kind of character. The human profile uses the
    // generated absorption table; fantasy races can supply a painted table with the
    // same layout and their own region behaviour.
    [CreateAssetMenu(fileName = "CharacterSkinProfile", menuName = "Sol/Character Customization/Skin Profile")]
    public sealed class CharacterSkinProfile : ScriptableObject
    {
        [SerializeField] private string id = "human";

        [Tooltip("Colour table in the SkinColorModel layout. Leave empty to use the built-in human model; must be CPU-readable for colour matching.")]
        [SerializeField] private Texture2D colorTable;
        [SerializeField] private CharacterSkinParameters defaultParameters = CharacterSkinParameters.Default;

        [Header("Regions")]
        [SerializeField, Range(0f, 2f)] private float lipMelaninScale = 0.8f;
        [SerializeField, Range(0f, 1f)] private float lipHemoglobinAdd = 0.35f;
        [SerializeField, Range(0f, 2f)] private float palmMelaninScale = 0.3f;
        [SerializeField, Range(0f, 1f)] private float palmHemoglobinAdd = 0.05f;
        [SerializeField, Range(0f, 1f)] private float blushHemoglobinAdd = 0.15f;
        [SerializeField, Range(0f, 2f)] private float areolaMelaninScale = 1.35f;

        public string Id => id;
        public Texture2D ColorTable => colorTable;
        public CharacterSkinParameters DefaultParameters => defaultParameters;

        public Color EvaluateLinear(CharacterSkinParameters parameters)
        {
            return colorTable != null && colorTable.isReadable
                ? SkinColorModel.SampleLutLinear(colorTable, parameters)
                : SkinColorModel.EvaluateLinear(parameters);
        }

        public CharacterSkinParameters FindClosest(Color srgbColor)
        {
            return SkinColorModel.FindClosest(srgbColor.linear, EvaluateLinear);
        }

        public void ApplyTo(MaterialPropertyBlock block)
        {
            if (colorTable != null)
            {
                // Property blocks do not fill in texel sizes, and the shader needs it
                // to keep undertone slices from bleeding into each other.
                block.SetTexture(SolSkinShaderIds.SkinLut, colorTable);
                block.SetVector(SolSkinShaderIds.SkinLutTexelSize, new Vector4(
                    1f / colorTable.width,
                    1f / colorTable.height,
                    colorTable.width,
                    colorTable.height));
            }

            block.SetFloat(SolSkinShaderIds.LipMelaninScale, lipMelaninScale);
            block.SetFloat(SolSkinShaderIds.LipHemoglobinAdd, lipHemoglobinAdd);
            block.SetFloat(SolSkinShaderIds.PalmMelaninScale, palmMelaninScale);
            block.SetFloat(SolSkinShaderIds.PalmHemoglobinAdd, palmHemoglobinAdd);
            block.SetFloat(SolSkinShaderIds.BlushHemoglobinAdd, blushHemoglobinAdd);
            block.SetFloat(SolSkinShaderIds.AreolaMelaninScale, areolaMelaninScale);
        }

        public void ApplyTo(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (colorTable != null)
            {
                material.SetTexture(SolSkinShaderIds.SkinLut, colorTable);
            }

            material.SetFloat(SolSkinShaderIds.LipMelaninScale, lipMelaninScale);
            material.SetFloat(SolSkinShaderIds.LipHemoglobinAdd, lipHemoglobinAdd);
            material.SetFloat(SolSkinShaderIds.PalmMelaninScale, palmMelaninScale);
            material.SetFloat(SolSkinShaderIds.PalmHemoglobinAdd, palmHemoglobinAdd);
            material.SetFloat(SolSkinShaderIds.BlushHemoglobinAdd, blushHemoglobinAdd);
            material.SetFloat(SolSkinShaderIds.AreolaMelaninScale, areolaMelaninScale);
        }

#if UNITY_EDITOR
        public void ConfigureColorTable(Texture2D table)
        {
            colorTable = table;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
