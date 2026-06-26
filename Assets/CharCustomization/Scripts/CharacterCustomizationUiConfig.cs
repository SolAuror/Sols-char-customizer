using System;
using System.Collections.Generic;
using System.Linq;

namespace Sol.CharacterCustomization
{
    public static class CharacterCustomizationUiConfig
    {
        public const string PresetsGroupId = "Presets";
        public const string SkinGroupId = "Skin";
        public const string DefaultMorphGroupId = "Body";
        public const CharacterSex DefaultSex = CharacterSex.Female;

        private static readonly string[] MorphGroupIds =
        {
            "Body", "Jaw / Chin", "Mouth", "Nose", "Cheeks", "Eyes", "Brows", "Neck / Ears"
        };

        private static readonly string[] TabGroupIds =
        {
            PresetsGroupId, SkinGroupId, "Body", "Jaw / Chin", "Mouth", "Nose", "Cheeks", "Eyes", "Brows", "Neck / Ears"
        };

        public static IReadOnlyList<string> MorphGroups => MorphGroupIds;
        public static IReadOnlyList<string> TabGroups => TabGroupIds;

        public static bool IsPresetGroup(string groupId)
        {
            return string.Equals(groupId, PresetsGroupId, StringComparison.Ordinal);
        }

        public static bool IsSkinGroup(string groupId)
        {
            return string.Equals(groupId, SkinGroupId, StringComparison.Ordinal);
        }

        public static bool IsMorphGroup(string groupId)
        {
            return MorphGroupIds.Contains(groupId, StringComparer.Ordinal);
        }

        public static bool IsKnownGroup(string groupId)
        {
            return IsPresetGroup(groupId) || IsSkinGroup(groupId) || IsMorphGroup(groupId);
        }
    }
}
