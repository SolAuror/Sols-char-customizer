using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [CreateAssetMenu(
        fileName = "CharacterMorphPresetLibrary",
        menuName = "Sol/Character Customization/Morph Preset Library")]
    public sealed class CharacterMorphPresetLibrary : ScriptableObject
    {
        [SerializeField] private List<CharacterMorphPreset> presets = new();

        public IReadOnlyList<CharacterMorphPreset> Presets =>
            presets ?? (IReadOnlyList<CharacterMorphPreset>)Array.Empty<CharacterMorphPreset>();

        public CharacterMorphPreset Find(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName) || presets == null)
            {
                return null;
            }

            string trimmedName = presetName.Trim();
            return presets.Find(preset =>
                preset != null && string.Equals(preset.DisplayName, trimmedName, StringComparison.OrdinalIgnoreCase));
        }

        internal CharacterMorphPreset GetOrCreate(string presetName)
        {
            string trimmedName = presetName?.Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                return null;
            }

            presets ??= new List<CharacterMorphPreset>();
            presets.RemoveAll(preset => preset == null);

            CharacterMorphPreset existing = Find(trimmedName);
            if (existing != null)
            {
                existing.SetDisplayName(trimmedName);
                return existing;
            }

            CharacterMorphPreset preset = CreateInstance<CharacterMorphPreset>();
            preset.name = trimmedName;
            preset.SetDisplayName(trimmedName);

#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.Contains(this))
            {
                UnityEditor.AssetDatabase.AddObjectToAsset(preset, this);
            }
#endif

            presets.Add(preset);
            MarkDirtyAndSave();
            return preset;
        }

        internal void MarkDirtyAndSave()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#endif
        }
    }
}
