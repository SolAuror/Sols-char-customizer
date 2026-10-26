using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTools
{
    // Import settings for hair written by Tools/Blender (Export Hair): Sol_<style>.fbx
    // and its T_Sol_<style>_* textures. Every hair texture is linear data, so sRGB is
    // off; the _Normal maps are tangent-space normal maps. The FBX holds meshes in the
    // character's space, without a rig, that follow one bone (named in the FBX
    // 'sol_hair_bone' user property) and carry the body's head shape keys as
    // blendshapes. Hair Setup skins them to the character. The exported normals and
    // tangents are kept; blendshape normals are not imported, since hair normals
    // describe the hair volume and should not be recalculated per shape.
    public sealed class SolHairAssetPostprocessor : AssetPostprocessor
    {
        private const string TexturePrefix = "T_Sol_";
        private const string ModelPrefix = "Sol_";
        // The Sol/Character Hair default alpha cutoff for cards.
        private const float CardCutoff = 0.35f;

        // Bump when the settings below change, so Unity reimports hair already in the project.
        public override uint GetVersion()
        {
            return 4;
        }

        private void OnPreprocessTexture()
        {
            string name = Path.GetFileNameWithoutExtension(assetPath);
            if (!name.StartsWith(TexturePrefix) || !(assetImporter is TextureImporter importer))
            {
                return;
            }

            // The hair menu thumbnail is an ordinary sRGB image shown as a UI sprite.
            if (name.EndsWith("_Preview"))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.sRGBTexture = true;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                return;
            }

            bool normal = name.EndsWith("_Normal");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;

            // Averaging thin strands into smaller mips drops their alpha below the cutoff
            // and the cards thin out or clump with distance; keep the coverage instead.
            bool cardCoverage = name.EndsWith("_Data") && !name.EndsWith("_Cap_Data");
            importer.mipMapsPreserveCoverage = cardCoverage;
            if (cardCoverage)
            {
                importer.alphaTestReferenceValue = CardCutoff;
            }
        }

        private void OnPreprocessModel()
        {
            if (!IsHairModel(assetPath) || !(assetImporter is ModelImporter importer))
            {
                return;
            }

            // Hair Setup copies the meshes into skinned versions, which needs their data.
            importer.isReadable = true;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = true;
            importer.importBlendShapeNormals = ModelImporterNormals.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.Import;
        }

        private static bool IsHairModel(string path)
        {
            return Path.GetFileName(path).StartsWith(ModelPrefix)
                && Path.GetExtension(path).Equals(".fbx", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
