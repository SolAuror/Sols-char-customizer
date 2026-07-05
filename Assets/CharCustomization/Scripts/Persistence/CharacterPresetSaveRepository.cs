using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    public interface ICharacterPresetSaveRepository
    {
        string SavePath { get; }
        bool TryLoad(out CharacterPresetSaveData data, out string error);
        bool TryFindByName(string presetName, out RuntimeCharacterPresetRecord record, out string error);
        bool TrySavePreset(
            string presetName,
            CharacterRecipe recipe,
            bool overwriteExisting,
            out RuntimeCharacterPresetRecord savedRecord,
            out bool duplicateName,
            out string error);
        bool TryDeletePreset(string presetId, out string deletedPresetName, out string error);
    }

    [Serializable]
    public sealed class RuntimeCharacterPresetRecord
    {
        [SerializeField] private string id;
        [SerializeField] private string presetName;
        [SerializeField] private CharacterRecipe recipe = new();

        public string Id => id;
        public string PresetName => presetName;
        public CharacterRecipe Recipe => recipe;

        internal RuntimeCharacterPresetRecord(string id, string presetName, CharacterRecipe recipe)
        {
            Overwrite(id, presetName, recipe);
        }

        internal void Overwrite(string recordId, string recordName, CharacterRecipe sourceRecipe)
        {
            id = recordId;
            presetName = recordName;
            recipe ??= new CharacterRecipe();
            recipe.Overwrite(sourceRecipe);
        }
    }

    [Serializable]
    public sealed class CharacterPresetSaveData
    {
        public const int CurrentVersion = 1;

        [SerializeField] private int version = CurrentVersion;
        [SerializeField] private List<RuntimeCharacterPresetRecord> presets = new();

        public int Version => version;
        public IReadOnlyList<RuntimeCharacterPresetRecord> Presets => presets;

        internal List<RuntimeCharacterPresetRecord> MutablePresets
        {
            get
            {
                presets ??= new List<RuntimeCharacterPresetRecord>();
                return presets;
            }
        }
    }

    public sealed class CharacterPresetSaveRepository : ICharacterPresetSaveRepository
    {
        public const string SaveDirectoryName = "SolCharacterCustomization";
        public const string SaveFileName = "presets.json";

        private readonly string savePath;

        public CharacterPresetSaveRepository(string overrideSavePath = null)
        {
            savePath = string.IsNullOrWhiteSpace(overrideSavePath)
                ? Path.Combine(Application.persistentDataPath, SaveDirectoryName, SaveFileName)
                : overrideSavePath;
        }

        public string SavePath => savePath;

        public bool TryLoad(out CharacterPresetSaveData data, out string error)
        {
            data = new CharacterPresetSaveData();
            error = null;

            if (!File.Exists(savePath))
            {
                return true;
            }

            try
            {
                string json = File.ReadAllText(savePath);
                CharacterPresetSaveData loaded = JsonUtility.FromJson<CharacterPresetSaveData>(json);
                if (loaded == null || loaded.Version != CharacterPresetSaveData.CurrentVersion ||
                    loaded.Presets == null)
                {
                    error = "The character preset save has an unsupported or malformed structure.";
                    return false;
                }

                data = loaded;
                return true;
            }
            catch (Exception exception)
            {
                error = $"Could not read character presets: {exception.Message}";
                return false;
            }
        }

        public bool TryFindByName(string presetName, out RuntimeCharacterPresetRecord record, out string error)
        {
            record = null;
            if (!TryLoad(out CharacterPresetSaveData data, out error))
            {
                return false;
            }

            string trimmedName = presetName?.Trim();
            foreach (RuntimeCharacterPresetRecord candidate in data.Presets)
            {
                if (candidate != null &&
                    string.Equals(candidate.PresetName, trimmedName, StringComparison.OrdinalIgnoreCase))
                {
                    record = candidate;
                    break;
                }
            }

            return true;
        }

        public bool TrySavePreset(
            string presetName,
            CharacterRecipe recipe,
            bool overwriteExisting,
            out RuntimeCharacterPresetRecord savedRecord,
            out bool duplicateName,
            out string error)
        {
            savedRecord = null;
            duplicateName = false;
            error = null;

            string trimmedName = presetName?.Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                error = "Enter a preset name before saving.";
                return false;
            }

            if (recipe == null)
            {
                error = "No character recipe is available.";
                return false;
            }

            if (!recipe.HasValidIdentifiers(out string recipeError))
            {
                error = recipeError;
                return false;
            }

            if (!TryLoad(out CharacterPresetSaveData data, out error))
            {
                return false;
            }

            RuntimeCharacterPresetRecord existing = null;
            foreach (RuntimeCharacterPresetRecord candidate in data.MutablePresets)
            {
                if (candidate != null &&
                    string.Equals(candidate.PresetName, trimmedName, StringComparison.OrdinalIgnoreCase))
                {
                    existing = candidate;
                    break;
                }
            }

            if (existing != null && !overwriteExisting)
            {
                duplicateName = true;
                return false;
            }

            if (existing != null)
            {
                existing.Overwrite(existing.Id, trimmedName, recipe);
                savedRecord = existing;
            }
            else
            {
                savedRecord = new RuntimeCharacterPresetRecord(Guid.NewGuid().ToString("N"), trimmedName, recipe);
                data.MutablePresets.Add(savedRecord);
            }

            return TryWrite(data, out error);
        }

        public bool TryDeletePreset(string presetId, out string deletedPresetName, out string error)
        {
            deletedPresetName = null;
            error = null;

            string trimmedId = presetId?.Trim();
            if (string.IsNullOrEmpty(trimmedId))
            {
                error = "Select a saved preset before deleting.";
                return false;
            }

            if (!TryLoad(out CharacterPresetSaveData data, out error))
            {
                return false;
            }

            List<RuntimeCharacterPresetRecord> presets = data.MutablePresets;
            for (int index = 0; index < presets.Count; index++)
            {
                RuntimeCharacterPresetRecord candidate = presets[index];
                if (candidate == null ||
                    !string.Equals(candidate.Id, trimmedId, StringComparison.Ordinal))
                {
                    continue;
                }

                deletedPresetName = candidate.PresetName;
                presets.RemoveAt(index);
                return TryWrite(data, out error);
            }

            error = "The selected saved preset could not be found.";
            return false;
        }

        private bool TryWrite(CharacterPresetSaveData data, out string error)
        {
            string tempPath = savePath + ".tmp";
            error = null;

            try
            {
                string directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(tempPath, JsonUtility.ToJson(data, true));
                if (File.Exists(savePath))
                {
                    File.Replace(tempPath, savePath, null);
                }
                else
                {
                    File.Move(tempPath, savePath);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = $"Could not save the character preset: {exception.Message}";
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // The primary write error is more useful than temp-file cleanup failures.
                }

                return false;
            }
        }
    }
}
