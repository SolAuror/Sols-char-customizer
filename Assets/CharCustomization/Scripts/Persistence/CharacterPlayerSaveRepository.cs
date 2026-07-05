using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [Serializable]
    public sealed class PlayerCharacterRecord
    {
        [SerializeField] private string id;
        [SerializeField] private string playerName;
        [SerializeField] private CharacterRecipe recipe = new();

        public string Id => id;
        public string PlayerName => playerName;
        public CharacterRecipe Recipe => recipe;

        internal PlayerCharacterRecord(string id, string playerName, CharacterRecipe recipe)
        {
            Overwrite(id, playerName, recipe);
        }

        internal void Overwrite(string recordId, string recordName, CharacterRecipe sourceRecipe)
        {
            id = recordId;
            playerName = recordName;
            recipe ??= new CharacterRecipe();
            recipe.Overwrite(sourceRecipe);
        }
    }

    [Serializable]
    public sealed class CharacterPlayerSaveData
    {
        public const int CurrentVersion = 1;

        [SerializeField] private int version = CurrentVersion;
        [SerializeField] private List<PlayerCharacterRecord> players = new();

        public int Version => version;
        public IReadOnlyList<PlayerCharacterRecord> Players => players;

        internal List<PlayerCharacterRecord> MutablePlayers
        {
            get
            {
                players ??= new List<PlayerCharacterRecord>();
                return players;
            }
        }
    }

    public interface ICharacterPlayerSaveRepository
    {
        string SavePath { get; }
        bool TryLoad(out CharacterPlayerSaveData data, out string error);
        bool TryFindByName(string playerName, out PlayerCharacterRecord record, out string error);
        bool TrySavePlayer(
            string playerName,
            CharacterRecipe recipe,
            bool overwriteExisting,
            out PlayerCharacterRecord savedRecord,
            out bool duplicateName,
            out string error);
    }

    public sealed class CharacterPlayerSaveRepository : ICharacterPlayerSaveRepository
    {
        public const string SaveDirectoryName = "SolCharacterCustomization";
        public const string SaveFileName = "players.json";

        private readonly string savePath;

        public CharacterPlayerSaveRepository(string overrideSavePath = null)
        {
            savePath = string.IsNullOrWhiteSpace(overrideSavePath)
                ? Path.Combine(Application.persistentDataPath, SaveDirectoryName, SaveFileName)
                : overrideSavePath;
        }

        public string SavePath => savePath;

        public bool TryLoad(out CharacterPlayerSaveData data, out string error)
        {
            data = new CharacterPlayerSaveData();
            error = null;

            if (!File.Exists(savePath))
            {
                return true;
            }

            try
            {
                string json = File.ReadAllText(savePath);
                CharacterPlayerSaveData loaded = JsonUtility.FromJson<CharacterPlayerSaveData>(json);
                if (loaded == null || loaded.Version != CharacterPlayerSaveData.CurrentVersion ||
                    loaded.Players == null)
                {
                    error = "The player character save has an unsupported or malformed structure.";
                    return false;
                }

                data = loaded;
                return true;
            }
            catch (Exception exception)
            {
                error = $"Could not read player character saves: {exception.Message}";
                return false;
            }
        }

        public bool TryFindByName(string playerName, out PlayerCharacterRecord record, out string error)
        {
            record = null;
            if (!TryLoad(out CharacterPlayerSaveData data, out error))
            {
                return false;
            }

            string trimmedName = playerName?.Trim();
            foreach (PlayerCharacterRecord candidate in data.Players)
            {
                if (candidate != null &&
                    string.Equals(candidate.PlayerName, trimmedName, StringComparison.OrdinalIgnoreCase))
                {
                    record = candidate;
                    break;
                }
            }

            return true;
        }

        public bool TrySavePlayer(
            string playerName,
            CharacterRecipe recipe,
            bool overwriteExisting,
            out PlayerCharacterRecord savedRecord,
            out bool duplicateName,
            out string error)
        {
            savedRecord = null;
            duplicateName = false;
            error = null;

            string trimmedName = playerName?.Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                error = "Enter a character name before finalizing.";
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

            if (!TryLoad(out CharacterPlayerSaveData data, out error))
            {
                return false;
            }

            PlayerCharacterRecord existing = null;
            foreach (PlayerCharacterRecord candidate in data.MutablePlayers)
            {
                if (candidate != null &&
                    string.Equals(candidate.PlayerName, trimmedName, StringComparison.OrdinalIgnoreCase))
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
                savedRecord = new PlayerCharacterRecord(Guid.NewGuid().ToString("N"), trimmedName, recipe);
                data.MutablePlayers.Add(savedRecord);
            }

            return TryWrite(data, out error);
        }

        private bool TryWrite(CharacterPlayerSaveData data, out string error)
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
                error = $"Could not save the finalized character: {exception.Message}";
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
