using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sol.CharacterCustomization.EditorTools
{
    // Material mapping for characters written by Tools/Blender (Export FBX):
    // Resources/SK_<Character>.fbx. A character's materials have one name in Blender and
    // in Unity, M_<Character>_<Part> (M_Human_F_Skin, M_Human_M_Eyes), so each material
    // in the FBX is mapped to the project material of the same name under
    // Resources/Materials, and mappings for names the FBX no longer uses are removed. A
    // re-export keeps its materials however its slots were named before; a material
    // with no namesake is reported rather than left as an imported URP Lit copy, which
    // would quietly drop the Sol/Character Skin shader and everything that needs it.
    public sealed class SolCharacterModelPostprocessor : AssetPostprocessor
    {
        public const string MaterialFolder = "Assets/CharCustomization/Resources/Materials";

        private const string ModelFolder = "Assets/CharCustomization/Resources";
        private const string ModelPrefix = "SK_";
        private static readonly Regex MaterialName = new(@"^M_(?<character>[A-Za-z0-9]+_[A-Za-z0-9]+)_[A-Z][A-Za-z0-9]*$");

        // Found while models import, acted on once the import has finished, so no
        // assets are loaded or importers changed mid-import.
        private static readonly Dictionary<string, Findings> pending = new();

        public static bool IsCharacterModel(string path)
        {
            return Path.GetFileName(path).StartsWith(ModelPrefix, StringComparison.Ordinal)
                   && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), ModelFolder, StringComparison.Ordinal);
        }

        // M_<Character>_<Part> for the character in SK_<Character>.fbx.
        public static bool FollowsConvention(string materialName, string modelPath)
        {
            Match match = MaterialName.Match(materialName ?? string.Empty);
            return match.Success && match.Groups["character"].Value == CharacterName(modelPath);
        }

        private static string CharacterName(string modelPath)
        {
            return Path.GetFileNameWithoutExtension(modelPath).Substring(ModelPrefix.Length);
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsCharacterModel(assetPath) || !(assetImporter is ModelImporter importer))
            {
                return;
            }

            var mapped = new Dictionary<string, Object>();
            foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, Object> pair in importer.GetExternalObjectMap())
            {
                if (pair.Key.type == typeof(Material))
                {
                    mapped[pair.Key.name] = pair.Value;
                }
            }

            // A mapped slot shows the project material; any other shows a copy made
            // from the FBX, named as the material was in Blender.
            var used = new HashSet<Object>();
            var unmapped = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    if (mapped.ContainsValue(material))
                    {
                        used.Add(material);
                    }
                    else
                    {
                        unmapped.Add(material.name);
                    }
                }
            }

            var stale = mapped.Where(pair => pair.Value == null || !used.Contains(pair.Value)).Select(pair => pair.Key).ToList();
            if (unmapped.Count > 0 || stale.Count > 0)
            {
                pending[assetPath] = new Findings(unmapped.ToList(), stale);
            }
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (pending.Count == 0)
            {
                return;
            }

            var batch = pending.ToList();
            pending.Clear();
            foreach (KeyValuePair<string, Findings> entry in batch)
            {
                Remap(entry.Key, entry.Value);
            }
        }

        private static void Remap(string path, Findings findings)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                return;
            }

            var added = new List<string>();
            foreach (string name in findings.Unmapped)
            {
                Material material = FindMaterial(name, out string problem);
                if (material == null)
                {
                    Debug.LogError(problem + Advice(name, path), importer);
                    continue;
                }

                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
                added.Add(name);
            }

            foreach (string name in findings.Stale)
            {
                importer.RemoveRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name));
            }

            if (added.Count == 0 && findings.Stale.Count == 0)
            {
                return;
            }

            Debug.Log($"{path}: mapped {(added.Count > 0 ? string.Join(", ", added) : "nothing new")} to the project's "
                      + $"materials of the same name{(findings.Stale.Count > 0 ? $", and removed mappings for {string.Join(", ", findings.Stale)}, which the FBX no longer uses" : string.Empty)}.",
                importer);
            importer.SaveAndReimport();
        }

        private static Material FindMaterial(string name, out string problem)
        {
            var matches = AssetDatabase.FindAssets($"{name} t:Material", new[] { MaterialFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(candidate => Path.GetFileNameWithoutExtension(candidate) == name)
                .ToList();
            if (matches.Count == 1)
            {
                problem = null;
                return AssetDatabase.LoadAssetAtPath<Material>(matches[0]);
            }

            problem = matches.Count == 0
                ? $"Material '{name}' has no Unity material of the same name in {MaterialFolder}, so it imports as a plain URP Lit copy. "
                : $"Material '{name}' matches several Unity materials ({string.Join(", ", matches)}), so none is used. ";
            return null;
        }

        private static string Advice(string name, string path)
        {
            string character = CharacterName(path);
            return FollowsConvention(name, path)
                ? $"Create {MaterialFolder}/<folder>/{name}.mat, or rename the Unity material to match."
                : $"Character materials are named M_{character}_<Part> in Blender and in Unity alike (M_{character}_Skin, M_{character}_Eyes); "
                  + "rename it in Blender, re-export, and make sure the Unity material has the same name.";
        }

        private readonly struct Findings
        {
            public Findings(List<string> unmapped, List<string> stale)
            {
                Unmapped = unmapped;
                Stale = stale;
            }

            public List<string> Unmapped { get; }
            public List<string> Stale { get; }
        }
    }
}
