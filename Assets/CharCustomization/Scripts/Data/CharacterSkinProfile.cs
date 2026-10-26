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
        public static readonly int MelaninMax = Shader.PropertyToID("_MelaninMax");
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
    // generated absorption table. Fantasy skins add a pigment over human skin, solved
    // from a reference colour (SkinPigments.FromReference), or supply a painted table
    // with the same layout, and can set their own region behaviour.
    [CreateAssetMenu(fileName = "CharacterSkinProfile", menuName = "Sol/Character Customization/Skin Profile")]
    public sealed class CharacterSkinProfile : ScriptableObject
    {
        [SerializeField] private string id = "human";

        [Tooltip("Colour table in the SkinColorModel layout. Leave empty to use the built-in human model; must be CPU-readable for colour matching.")]
        [SerializeField] private Texture2D colorTable;
        [SerializeField] private CharacterSkinParameters defaultParameters = CharacterSkinParameters.Default;

        [Header("Fantasy Pigment")]
        [Tooltip("Add a fantasy pigment over human skin, solved from the reference colour. The third skin parameter becomes tint: 0 plain human skin, 1 the full colour. Regenerate the colour table after changing these (Tools > Sol > Character Customization > Skin).")]
        [SerializeField] private bool useReferenceColor;
        [Tooltip("The colour at melanin 0.3, hemoglobin 0.15, tint 1.")]
        [SerializeField] private Color referenceColor = new(0.25f, 0.45f, 0.26f, 1f);
        [Tooltip("How much blood shows through: 1 as in human skin, 0 bloodless.")]
        [SerializeField, Range(0f, 1f)] private float hemoglobinStrength = 1f;
        [Tooltip("The deepest human melanin this skin reaches. Below 1 keeps the deepest shades recognisably this colour instead of converging on human near-black brown.")]
        [SerializeField, Range(0.3f, 1f)] private float maxMelanin = SkinPigments.FantasyMaxMelanin;

        private SkinPigments pigments;

        // Melanin scales multiply melanin density (see SkinColorModel.ScaleMelaninDensity),
        // so each region stays in proportion to the surrounding skin at every tone.
        [Header("Regions")]
        [Tooltip("Melanin density of the lips relative to the surrounding skin.")]
        [SerializeField, Range(0f, 2f)] private float lipMelaninScale = 0.85f;
        [SerializeField, Range(0f, 1f)] private float lipHemoglobinAdd = 0.35f;
        [Tooltip("Melanin density of palms, soles, and nail beds relative to the surrounding skin.")]
        [SerializeField, Range(0f, 1f)] private float palmMelaninScale = 0.3f;
        [SerializeField, Range(0f, 1f)] private float palmHemoglobinAdd = 0.05f;
        [SerializeField, Range(0f, 1f)] private float blushHemoglobinAdd = 0.15f;
        [Tooltip("Melanin density of the areolae relative to the surrounding skin.")]
        [SerializeField, Range(1f, 4f)] private float areolaMelaninScale = 2.5f;

        public string Id => id;
        public Texture2D ColorTable => colorTable;
        public CharacterSkinParameters DefaultParameters => defaultParameters;
        public bool UsesReferenceColor => useReferenceColor;
        public Color ReferenceColor => referenceColor;
        public SkinThirdAxis ThirdAxis => Pigments.ThirdAxis;
        public SkinPigments Pigments => pigments ??= useReferenceColor
            ? SkinPigments.FromReference(referenceColor, hemoglobinStrength, maxMelanin)
            : SkinPigments.Human;

        private void OnValidate()
        {
            pigments = null;
        }

        public Color EvaluateLinear(CharacterSkinParameters parameters)
        {
            return colorTable != null && colorTable.isReadable
                ? SkinColorModel.SampleLutLinear(colorTable, parameters)
                : SkinColorModel.EvaluateLinear(parameters, Pigments);
        }

        // An in-memory human profile with a generated table, for characters that have
        // no human profile asset but need to return to human skin after a fantasy
        // profile replaced the material's colour table and region values.
        public static CharacterSkinProfile CreateRuntimeHuman()
        {
            var profile = CreateInstance<CharacterSkinProfile>();
            profile.name = "Runtime Human Skin";
            profile.hideFlags = HideFlags.DontSave;
            profile.colorTable = SkinColorModel.GenerateLut();
            profile.colorTable.hideFlags = HideFlags.DontSave;
            return profile;
        }

        public static void DestroyRuntime(CharacterSkinProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            DestroyObject(profile.colorTable);
            DestroyObject(profile);
        }

        private static void DestroyObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
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

            block.SetFloat(SolSkinShaderIds.MelaninMax, Pigments.MaxMelanin);
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

            material.SetFloat(SolSkinShaderIds.MelaninMax, Pigments.MaxMelanin);
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

        public void ConfigureReferenceColor(string profileId, Color reference, float bloodStrength)
        {
            id = profileId;
            useReferenceColor = true;
            maxMelanin = SkinPigments.FantasyMaxMelanin;
            pigments = null;
            referenceColor = reference;
            hemoglobinStrength = Mathf.Clamp01(bloodStrength);
            defaultParameters = SkinPigments.ReferenceParameters;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
