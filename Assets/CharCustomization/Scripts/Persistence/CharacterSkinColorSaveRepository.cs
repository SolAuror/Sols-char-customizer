using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    public interface ICharacterSkinColorSaveRepository
    {
        string SavePath { get; }
        bool TryLoad(out CharacterSkinColorSaveData data, out string error);
        bool TrySaveColor(Color color, out RuntimeSkinColorRecord savedRecord, out bool duplicateColor, out string error);
        bool TryDeleteColor(string colorId, out string deletedLabel, out string error);
    }

    [Serializable]
    public sealed class RuntimeSkinColorRecord
    {
        [SerializeField] private string id;
        [SerializeField] private string label;
        [SerializeField] private Color color = Color.white;

        public string Id => id;
        public string Label => string.IsNullOrWhiteSpace(label) ? "Saved" : label;
        public Color Color => color;

        internal RuntimeSkinColorRecord(string id, string label, Color color)
        {
            this.id = id;
            this.label = label;
            this.color = ClampColor(color);
        }

        private static Color ClampColor(Color color)
        {
            return new Color(
                Mathf.Clamp01(color.r),
                Mathf.Clamp01(color.g),
                Mathf.Clamp01(color.b),
                Mathf.Clamp01(color.a));
        }
    }

    [Serializable]
    public sealed class CharacterSkinColorSaveData
    {
        public const int CurrentVersion = 1;

        [SerializeField] private int version = CurrentVersion;
        [SerializeField] private List<RuntimeSkinColorRecord> colors = new();

        public int Version => version;
        public IReadOnlyList<RuntimeSkinColorRecord> Colors => colors;

        internal List<RuntimeSkinColorRecord> MutableColors
        {
            get
            {
                colors ??= new List<RuntimeSkinColorRecord>();
                return colors;
            }
        }
    }

    public sealed class CharacterSkinColorSaveRepository : ICharacterSkinColorSaveRepository
    {
        public const string SaveFileName = "skin-colors.json";
        public const float DuplicateTolerance = 1f / 255f;

        private readonly string savePath;

        public CharacterSkinColorSaveRepository(string overrideSavePath = null)
        {
            savePath = string.IsNullOrWhiteSpace(overrideSavePath)
                ? Path.Combine(
                    Application.persistentDataPath,
                    CharacterPresetSaveRepository.SaveDirectoryName,
                    SaveFileName)
                : overrideSavePath;
        }

        public string SavePath => savePath;

        public bool TryLoad(out CharacterSkinColorSaveData data, out string error)
        {
            data = new CharacterSkinColorSaveData();
            error = null;

            if (!File.Exists(savePath))
            {
                return true;
            }

            try
            {
                string json = File.ReadAllText(savePath);
                CharacterSkinColorSaveData loaded = JsonUtility.FromJson<CharacterSkinColorSaveData>(json);
                if (loaded == null || loaded.Version != CharacterSkinColorSaveData.CurrentVersion ||
                    loaded.Colors == null)
                {
                    error = "The saved skin color data has an unsupported or malformed structure.";
                    return false;
                }

                data = loaded;
                return true;
            }
            catch (Exception exception)
            {
                error = $"Could not read saved skin colors: {exception.Message}";
                return false;
            }
        }

        public bool TrySaveColor(
            Color color,
            out RuntimeSkinColorRecord savedRecord,
            out bool duplicateColor,
            out string error)
        {
            savedRecord = null;
            duplicateColor = false;
            error = null;

            Color clampedColor = ClampColor(color);
            if (!TryLoad(out CharacterSkinColorSaveData data, out error))
            {
                return false;
            }

            foreach (RuntimeSkinColorRecord record in data.MutableColors)
            {
                if (record != null && ColorsMatch(record.Color, clampedColor))
                {
                    savedRecord = record;
                    duplicateColor = true;
                    return true;
                }
            }

            savedRecord = new RuntimeSkinColorRecord(
                Guid.NewGuid().ToString("N"),
                $"Saved {data.MutableColors.Count + 1}",
                clampedColor);
            data.MutableColors.Add(savedRecord);
            return TryWrite(data, out error);
        }

        public bool TryDeleteColor(string colorId, out string deletedLabel, out string error)
        {
            deletedLabel = null;
            error = null;

            string trimmedId = colorId?.Trim();
            if (string.IsNullOrEmpty(trimmedId))
            {
                error = "Select a saved skin color before deleting.";
                return false;
            }

            if (!TryLoad(out CharacterSkinColorSaveData data, out error))
            {
                return false;
            }

            List<RuntimeSkinColorRecord> colors = data.MutableColors;
            for (int index = 0; index < colors.Count; index++)
            {
                RuntimeSkinColorRecord candidate = colors[index];
                if (candidate == null ||
                    !string.Equals(candidate.Id, trimmedId, StringComparison.Ordinal))
                {
                    continue;
                }

                deletedLabel = candidate.Label;
                colors.RemoveAt(index);
                return TryWrite(data, out error);
            }

            error = "The selected saved skin color could not be found.";
            return false;
        }

        private bool TryWrite(CharacterSkinColorSaveData data, out string error)
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
                error = $"Could not save the skin color: {exception.Message}";
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // The write error above is the useful failure.
                }

                return false;
            }
        }

        internal static bool ColorsMatch(Color first, Color second)
        {
            return Mathf.Abs(first.r - second.r) <= DuplicateTolerance &&
                   Mathf.Abs(first.g - second.g) <= DuplicateTolerance &&
                   Mathf.Abs(first.b - second.b) <= DuplicateTolerance &&
                   Mathf.Abs(first.a - second.a) <= DuplicateTolerance;
        }

        private static Color ClampColor(Color color)
        {
            return new Color(
                Mathf.Clamp01(color.r),
                Mathf.Clamp01(color.g),
                Mathf.Clamp01(color.b),
                Mathf.Clamp01(color.a));
        }
    }
}
