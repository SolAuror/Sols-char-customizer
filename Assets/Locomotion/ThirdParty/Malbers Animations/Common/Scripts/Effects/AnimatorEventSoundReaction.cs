// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Utilities;
using System;
using UnityEngine;


namespace MalbersAnimations.Reactions
{
    [System.Serializable]

    [AddTypeMenu("Malbers/Animator Sound [Audio]")]
    [HelpURL(ReactionDocs.Malbers + "animator-sound-audio")]
    public class AnimatorEventSoundReaction : Reaction
    {
        public override string DynamicName => $"Play Event Sound: {eventSound}";

        public string eventSound = "Sound Event";
        public override Type ReactionType => typeof(AnimatorEventSounds);


        protected override bool _TryReact(Component component)
        {
            var animatorEventSounds = component as AnimatorEventSounds;

            if (animatorEventSounds != null)
            {
                animatorEventSounds.PlaySound(eventSound);
                return true;
            }

            return false;
        }
    }
}