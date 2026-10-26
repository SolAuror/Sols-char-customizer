using System;
using System.Collections.Generic;
using System.Linq;

namespace Sol.CharacterCustomization
{
    public static class CharacterCustomizationUiConfig
    {
        public const string PresetsGroupId = "Presets";
        public const string SkinGroupId = "Skin";
        public const string HairGroupId = "Hair";
        public const string DefaultMorphGroupId = "Body";
        public const CharacterSex DefaultSex = CharacterSex.Female;

        private static readonly string[] MorphGroupIds =
        {
            "Body", "Jaw / Chin", "Mouth", "Nose", "Cheeks", "Eyes", "Brows", "Neck / Ears"
        };

        private static readonly string[] TabGroupIds =
        {
            PresetsGroupId, SkinGroupId, HairGroupId, "Body", "Jaw / Chin", "Mouth", "Nose", "Cheeks", "Eyes", "Brows", "Neck / Ears"
        };

        // Tabs a menu may leave out; the rest keep their order without them. The hair
        // tab is added to an existing menu by Hair Setup's Build Hair Tab.
        private static readonly string[] OptionalTabGroupIds = { HairGroupId };

        public static IReadOnlyList<string> MorphGroups => MorphGroupIds;
        public static IReadOnlyList<string> TabGroups => TabGroupIds;

        public static bool IsOptionalTab(string groupId)
        {
            return OptionalTabGroupIds.Contains(groupId, StringComparer.Ordinal);
        }

        // The expected tab order for a menu that has the given tabs.
        public static IReadOnlyList<string> ExpectedTabs(ICollection<string> presentGroupIds)
        {
            return TabGroupIds
                .Where(groupId => !IsOptionalTab(groupId) || presentGroupIds.Contains(groupId))
                .ToArray();
        }

        public static bool IsPresetGroup(string groupId)
        {
            return string.Equals(groupId, PresetsGroupId, StringComparison.Ordinal);
        }

        public static bool IsSkinGroup(string groupId)
        {
            return string.Equals(groupId, SkinGroupId, StringComparison.Ordinal);
        }

        public static bool IsHairGroup(string groupId)
        {
            return string.Equals(groupId, HairGroupId, StringComparison.Ordinal);
        }

        public static bool IsMorphGroup(string groupId)
        {
            return MorphGroupIds.Contains(groupId, StringComparer.Ordinal);
        }

        public static bool IsKnownGroup(string groupId)
        {
            return IsPresetGroup(groupId) || IsSkinGroup(groupId) || IsHairGroup(groupId) || IsMorphGroup(groupId);
        }
    }
}
