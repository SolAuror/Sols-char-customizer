using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [CreateAssetMenu(
        fileName = "CharacterNameList",
        menuName = "Sol/Character Customization/Character Name List")]
    public sealed class CharacterNameList : ScriptableObject
    {
        [SerializeField] private List<string> names = new();

        public IReadOnlyList<string> Names => names ?? (IReadOnlyList<string>)Array.Empty<string>();

        public bool TryGetRandomName(out string characterName)
        {
            characterName = null;
            if (names == null || names.Count == 0)
            {
                return false;
            }

            int validCount = 0;
            for (int index = 0; index < names.Count; index++)
            {
                if (!string.IsNullOrWhiteSpace(names[index]))
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return false;
            }

            int selectedValidIndex = UnityEngine.Random.Range(0, validCount);
            for (int index = 0; index < names.Count; index++)
            {
                string candidate = names[index];
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (selectedValidIndex == 0)
                {
                    characterName = candidate.Trim();
                    return true;
                }

                selectedValidIndex--;
            }

            return false;
        }
    }
}
