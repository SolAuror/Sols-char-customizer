using UnityEditor;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTools
{
    public static class SolSkinMaterialTools
    {
        private const string PresetFolder = "Assets/CharCustomization/CustomizationPresets";
        private const string ColorTablePath = PresetFolder + "/HumanSkinColorTable.asset";
        private const string ProfilePath = PresetFolder + "/HumanSkinProfile.asset";
        private const int MeanSampleSize = 128;

        [MenuItem("Tools/Sol/Character Customization/Skin/Create or Update Human Skin Profile")]
        public static CharacterSkinProfile CreateOrUpdateHumanSkinProfile()
        {
            Texture2D generated = SkinColorModel.GenerateLut();
            Texture2D table = AssetDatabase.LoadAssetAtPath<Texture2D>(ColorTablePath);
            if (table == null)
            {
                table = generated;
                AssetDatabase.CreateAsset(table, ColorTablePath);
            }
            else
            {
                // Copy into the existing asset so materials and profiles keep their reference.
                EditorUtility.CopySerialized(generated, table);
                Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(table);
            }

            CharacterSkinProfile profile = AssetDatabase.LoadAssetAtPath<CharacterSkinProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<CharacterSkinProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.ConfigureColorTable(table);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(profile);
            Debug.Log($"Human skin profile ready at {ProfilePath}.", profile);
            return profile;
        }

        [MenuItem("Tools/Sol/Character Customization/Skin/Convert Selected Materials To Sol Skin")]
        private static void ConvertSelectedMaterials()
        {
            Shader shader = Shader.Find(SolSkinShaderIds.ShaderName);
            if (shader == null)
            {
                Debug.LogError($"Shader '{SolSkinShaderIds.ShaderName}' was not found.");
                return;
            }

            Material[] materials = Selection.GetFiltered<Material>(SelectionMode.Assets);
            if (materials.Length == 0)
            {
                Debug.LogError("Select one or more skin materials to convert.");
                return;
            }

            CharacterSkinProfile profile = AssetDatabase.LoadAssetAtPath<CharacterSkinProfile>(ProfilePath);
            if (profile == null)
            {
                profile = CreateOrUpdateHumanSkinProfile();
            }

            foreach (Material material in materials)
            {
                ConvertMaterial(material, shader, profile);
            }

            AssetDatabase.SaveAssets();
        }

        public static void ConvertMaterial(Material material, Shader shader, CharacterSkinProfile profile)
        {
            Undo.RecordObject(material, "Convert To Sol Skin");
            Texture baseMap = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
            Texture normalMap = material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null;
            float smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : 0.55f;

            material.shader = shader;
            material.SetTexture("_BaseMap", baseMap);
            if (normalMap != null)
            {
                material.SetTexture("_BumpMap", normalMap);
            }

            material.SetFloat("_Smoothness", smoothness);
            Vector4 mean = baseMap != null ? ComputeMeanLinearColor(baseMap) : Vector4.one;
            material.SetVector(SolSkinShaderIds.BaseMapMean, mean);
            profile.ApplyTo(material);
            EditorUtility.SetDirty(material);
            Debug.Log($"Converted '{material.name}' to {SolSkinShaderIds.ShaderName}; detail mean {mean}.", material);
        }

        // Average linear colour of a texture, ignoring near-black padding. The GPU
        // decodes sRGB while blitting, so the texture does not need Read/Write.
        public static Vector4 ComputeMeanLinearColor(Texture texture)
        {
            RenderTexture target = RenderTexture.GetTemporary(
                MeanSampleSize,
                MeanSampleSize,
                0,
                RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, target);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var readback = new Texture2D(MeanSampleSize, MeanSampleSize, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(0, 0, MeanSampleSize, MeanSampleSize), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);

            Color[] pixels = readback.GetPixels();
            Object.DestroyImmediate(readback);

            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Color pixel in pixels)
            {
                if (pixel.r + pixel.g + pixel.b < 0.01f)
                {
                    continue;
                }

                sum += new Vector3(pixel.r, pixel.g, pixel.b);
                count++;
            }

            if (count == 0)
            {
                return Vector4.one;
            }

            sum /= count;
            return new Vector4(sum.x, sum.y, sum.z, 0f);
        }
    }
}
