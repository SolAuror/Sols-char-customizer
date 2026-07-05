using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [CreateAssetMenu(menuName = "Sol/Character Customization/Morph Catalog", fileName = "CharacterMorphCatalog")]
    public sealed class CharacterMorphCatalogAsset : ScriptableObject
    {
        [SerializeField] private List<CharacterMorphCatalogEntry> morphs = new();

        private CharacterMorphDefinition[] cachedDefinitions;
        private Dictionary<string, CharacterMorphDefinition> cachedLookup;

        public IReadOnlyList<CharacterMorphDefinition> Definitions
        {
            get
            {
                EnsureCache();
                return cachedDefinitions;
            }
        }

        public IReadOnlyList<CharacterMorphCatalogEntry> Entries => morphs;

        public bool TryGet(string id, out CharacterMorphDefinition definition)
        {
            EnsureCache();
            return cachedLookup.TryGetValue(id, out definition);
        }

        public void UseDefaultDefinitions()
        {
            morphs ??= new List<CharacterMorphCatalogEntry>();
            morphs.Clear();
            foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
            {
                morphs.Add(new CharacterMorphCatalogEntry(definition));
            }

            ClearCache();
        }

        public bool ValidateDefinitions(out string error)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterMorphDefinition definition in Definitions)
            {
                if (string.IsNullOrWhiteSpace(definition.Id))
                {
                    error = "The morph catalog contains an empty morph ID.";
                    return false;
                }

                if (!ids.Add(definition.Id))
                {
                    error = $"The morph catalog contains duplicate morph ID '{definition.Id}'.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private void OnValidate()
        {
            ClearCache();
        }

        private void Reset()
        {
            UseDefaultDefinitions();
        }

        private void EnsureCache()
        {
            if (cachedDefinitions != null && cachedLookup != null)
            {
                return;
            }

            if (morphs == null || morphs.Count == 0)
            {
                cachedDefinitions = CopyDefinitions(CharacterMorphCatalog.Definitions);
            }
            else
            {
                var definitions = new List<CharacterMorphDefinition>(morphs.Count);
                foreach (CharacterMorphCatalogEntry entry in morphs)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                    {
                        continue;
                    }

                    definitions.Add(entry.ToDefinition());
                }

                cachedDefinitions = definitions.ToArray();
            }

            cachedLookup = CharacterMorphCatalog.BuildLookup(cachedDefinitions);
        }

        private void ClearCache()
        {
            cachedDefinitions = null;
            cachedLookup = null;
        }

        private static CharacterMorphDefinition[] CopyDefinitions(IReadOnlyList<CharacterMorphDefinition> source)
        {
            var copy = new CharacterMorphDefinition[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return copy;
        }
    }
}
