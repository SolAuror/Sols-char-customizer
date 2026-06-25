using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [CreateAssetMenu(
        fileName = "CharacterPresetLibrary",
        menuName = "Sol/Character Customization/Character Preset Library")]
    public sealed class CharacterPresetLibrary : ScriptableObject
    {
        [SerializeField] private List<CharacterPreset> presets = new();

        public IReadOnlyList<CharacterPreset> Presets =>
            presets ?? (IReadOnlyList<CharacterPreset>)Array.Empty<CharacterPreset>();

        public CharacterPreset Find(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName) || presets == null)
            {
                return null;
            }

            string trimmedName = presetName.Trim();
            return presets.Find(preset =>
                preset != null && string.Equals(preset.DisplayName, trimmedName, StringComparison.OrdinalIgnoreCase));
        }

        internal CharacterPreset GetOrCreate(string presetName)
        {
            string trimmedName = presetName?.Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                return null;
            }

            presets ??= new List<CharacterPreset>();
            presets.RemoveAll(preset => preset == null);

            CharacterPreset existing = Find(trimmedName);
            if (existing != null)
            {
                existing.SetDisplayName(trimmedName);
                return existing;
            }

            CharacterPreset preset = CreateInstance<CharacterPreset>();
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
