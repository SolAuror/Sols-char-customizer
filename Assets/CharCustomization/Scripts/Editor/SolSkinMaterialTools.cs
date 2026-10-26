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
        private const string DefaultPalettePath = PresetFolder + "/DefaultSkinPalette.asset";
        private const string FantasyFolder = PresetFolder + "/Skin";
        // Human skin always sits between red and yellow (CIE Lab hue angle) with some
        // chroma; tones outside that get a fantasy profile. A distance threshold is not
        // used because saturated human tones the model only nearly matches would count.
        private const float HumanHueMin = 15f;
        private const float HumanHueMax = 95f;
        private const float HumanChromaMin = 6f;
        private const float DefaultFantasyHemoglobinStrength = 1f;

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

        [MenuItem("Tools/Sol/Character Customization/Skin/Create Fantasy Skin Profiles For Palette")]
        private static void CreateFantasySkinProfilesForPalette()
        {
            CharacterSkinPalette palette = Selection.activeObject as CharacterSkinPalette
                ?? AssetDatabase.LoadAssetAtPath<CharacterSkinPalette>(DefaultPalettePath);
            if (palette == null)
            {
                Debug.LogError($"Select a skin palette, or create one at {DefaultPalettePath}.");
                return;
            }

            int created = CreateFantasySkinProfiles(palette, out string summary);
            AssetDatabase.SaveAssets();
            Debug.Log(created > 0
                ? $"Fantasy skin profiles for '{palette.name}': {summary}"
                : $"'{palette.name}' has no tones outside human skin that need a profile.", palette);
        }

        // Gives every tone outside human skin hues its own profile, a pigment over human
        // skin solved from the tone's colour, and regenerates tables for tones that
        // already have one.
        public static int CreateFantasySkinProfiles(CharacterSkinPalette palette, out string summary)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (CharacterSkinTone tone in palette.Tones)
            {
                if (tone == null)
                {
                    continue;
                }

                CharacterSkinProfile profile = tone.Profile;
                if (profile == null)
                {
                    if (IsHumanHue(tone.Color))
                    {
                        continue;
                    }

                    profile = LoadOrCreateFantasyProfile(tone);
                    profile.ConfigureReferenceColor(tone.Id, tone.Color, DefaultFantasyHemoglobinStrength);
                }

                if (profile.UsesReferenceColor)
                {
                    // The tone shows exactly its reference colour.
                    tone.ConfigureProfile(profile, SkinPigments.ReferenceParameters);
                    EditorUtility.SetDirty(palette);
                }

                CreateOrUpdateColorTable(profile);
                names.Add(tone.Label);
            }

            summary = string.Join(", ", names);
            return names.Count;
        }

        [MenuItem("Tools/Sol/Character Customization/Skin/Regenerate Colour Tables For Selected Profiles")]
        private static void RegenerateSelectedColorTables()
        {
            CharacterSkinProfile[] profiles = Selection.GetFiltered<CharacterSkinProfile>(SelectionMode.Assets);
            if (profiles.Length == 0)
            {
                Debug.LogError("Select one or more skin profiles.");
                return;
            }

            foreach (CharacterSkinProfile profile in profiles)
            {
                CreateOrUpdateColorTable(profile);
            }

            AssetDatabase.SaveAssets();
        }

        // Writes the profile's generated table next to it, reusing the existing asset so
        // materials and profiles keep their reference. Painted tables are left alone.
        public static void CreateOrUpdateColorTable(CharacterSkinProfile profile)
        {
            if (!profile.UsesReferenceColor && profile.ColorTable != null &&
                AssetDatabase.GetAssetPath(profile.ColorTable) != ColorTablePath)
            {
                Debug.Log($"'{profile.name}' uses a painted colour table; left unchanged.", profile);
                return;
            }

            Texture2D generated = SkinColorModel.GenerateLut(SkinColorModel.DefaultLutSize, profile.Pigments);
            Texture2D table = profile.ColorTable;
            if (table == null || (profile.UsesReferenceColor && AssetDatabase.GetAssetPath(table) == ColorTablePath))
            {
                string profilePath = AssetDatabase.GetAssetPath(profile);
                string tablePath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{System.IO.Path.GetDirectoryName(profilePath)}/{profile.name}ColorTable.asset".Replace("\\", "/"));
                generated.name = $"{profile.name}ColorTable";
                AssetDatabase.CreateAsset(generated, tablePath);
                profile.ConfigureColorTable(generated);
                return;
            }

            EditorUtility.CopySerialized(generated, table);
            Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(table);
        }

        private static CharacterSkinProfile LoadOrCreateFantasyProfile(CharacterSkinTone tone)
        {
            if (!AssetDatabase.IsValidFolder(FantasyFolder))
            {
                AssetDatabase.CreateFolder(PresetFolder, "Skin");
            }

            string path = $"{FantasyFolder}/{ToAssetName(tone.Label)}SkinProfile.asset";
            CharacterSkinProfile profile = AssetDatabase.LoadAssetAtPath<CharacterSkinProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<CharacterSkinProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            return profile;
        }

        public static bool IsHumanHue(Color srgb)
        {
            Vector3 lab = SkinColorModel.LinearToLab(srgb.linear);
            float chroma = new Vector2(lab.y, lab.z).magnitude;
            float hue = Mathf.Atan2(lab.z, lab.y) * Mathf.Rad2Deg;
            return chroma >= HumanChromaMin && hue >= HumanHueMin && hue <= HumanHueMax;
        }

        private static string ToAssetName(string label)
        {
            var builder = new System.Text.StringBuilder();
            foreach (char character in label)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
            }

            return builder.Length > 0 ? builder.ToString() : "Fantasy";
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
