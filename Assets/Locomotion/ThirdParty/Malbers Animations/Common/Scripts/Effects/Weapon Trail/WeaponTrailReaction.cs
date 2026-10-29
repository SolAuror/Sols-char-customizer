using MalbersAnimations.Utilities;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    // MWC — Lets any reaction list (modes, states, stats, item actions) drive a Weapon Trail without
    // holding a direct reference. Reaction.VerifyComponent resolves MWeaponTrail on the target, its
    // parents or its children.
    [System.Serializable, AddTypeMenu("Tools/Weapon Trail")]
    [HelpURL(ReactionDocs.Tools + "weapon-trail")]
    public class WeaponTrailReaction : Reaction
    {
        // MWC — New entries are APPENDED, never inserted: the [Hide] attributes below key off these
        // ordinals, and reordering would silently rewire every reaction already authored in a scene.
        //            0     1     2      3           4         5                6
        public enum TrailAction { Play, Stop, Clear, SetProfile, SetColor, SetProfileIndex, SetEmission }

        [Tooltip("What to do with the Weapon Trail")]
        public TrailAction action = TrailAction.Play;

        [Tooltip("Fade duration override in seconds. Negative uses the value stored in the Profile")]
        [Hide(nameof(action), 0, 1)]
        public float fade = -1f;

        [Tooltip("Which entry of the trail's Profiles list to use.\n" +
                 "-1 keeps whatever profile is already selected")]
        [Hide(nameof(action), 0, 5)]
        [Min(-1)] public int profileIndex = -1;

        [Tooltip("Profile asset to force onto the trail, bypassing its Profiles list")]
        [Hide(nameof(action), 3)]
        public WeaponTrailProfile profile;

        [Tooltip("Runtime tint MULTIPLIER pushed through the MaterialPropertyBlock. " +
                 "White leaves the Profile's Tint untouched")]
        [Hide(nameof(action), 4)]
        public Color color = Color.white;

        // MWC — mirrors MWeaponTrail.SetEmission, which the reaction was missing.
        // Deliberately NOT [ColorUsage(true, true)]: Unity honours only the FIRST PropertyDrawer on a
        // field, so [Hide] would swallow the HDR picker and it would look wired up while doing nothing.
        // The Profile's Emission is the HDR value; this is a plain 0-1 multiplier on top of it.
        [Tooltip("Runtime emission MULTIPLIER (0-1). White leaves the Profile's Emission untouched.\n" +
                 "To flare BRIGHTER than the profile, switch to a profile whose Emission is stronger")]
        [Hide(nameof(action), 6)]
        public Color emission = Color.white;

        public override System.Type ReactionType => typeof(MWeaponTrail);

        public override string DynamicName => action switch
        {
            TrailAction.Play when profileIndex >= 0 => $"Weapon Trail: [Play - Profile {profileIndex}]",
            TrailAction.SetProfile => $"Weapon Trail: [Set Profile: {(profile != null ? profile.name : "None")}]",
            TrailAction.SetProfileIndex => $"Weapon Trail: [Set Profile Index: {profileIndex}]",
            TrailAction.SetColor => "Weapon Trail: [Set Color]",
            TrailAction.SetEmission => "Weapon Trail: [Set Emission]",
            _ => $"Weapon Trail: [{action}]"
        };

        protected override bool _TryReact(Component component)
        {
            if (component is not MWeaponTrail trail) return false;

            switch (action)
            {
                case TrailAction.Play:
                    // PlayProfile ignores a negative index, so this covers both "play as is" and
                    // "switch to a slot, then play" without a second branch.
                    if (fade < 0) trail.PlayProfile(profileIndex);
                    else trail.PlayProfile(profileIndex, fade);
                    break;

                case TrailAction.Stop:
                    if (fade < 0) trail.Stop(); else trail.Stop(fade);
                    break;

                case TrailAction.Clear:
                    trail.Clear();
                    break;

                case TrailAction.SetProfile:
                    if (profile == null) return false;
                    trail.SetProfile(profile);
                    break;

                case TrailAction.SetProfileIndex:
                    if (profileIndex < 0) return false; // nothing to select
                    trail.SetProfileIndex(profileIndex);
                    break;

                case TrailAction.SetColor:
                    trail.SetColor(color);
                    break;

                case TrailAction.SetEmission:
                    trail.SetEmission(emission);
                    break;
            }
            return true;
        }
    }
}
