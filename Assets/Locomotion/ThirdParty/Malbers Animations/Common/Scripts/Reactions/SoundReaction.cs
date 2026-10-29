// Last Change: 9/18/2026 10:30pm
using System;
using UnityEngine;
using UnityEngine.Audio;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]

    [AddTypeMenu("Unity/Sound [Audio]")]
    [HelpURL(ReactionDocs.Unity + "sound-audio")]
    public class SoundReaction : Reaction
    {
        public override string DynamicName
        {
            get
            {
                if (resource == null) return "Play Sound [None]";
                return $"Play Sound [{resource.name}]{(loop ? " (Loop)" : string.Empty)}";
            }
        }

        public override Type ReactionType => typeof(AudioSource);

        public AudioResource resource;
        [MinMaxRange(0, 1)] public RangedFloat volume = new(1, 1);
        [MinMaxRange(-3, 3)] public RangedFloat pitch = new(1, 1);
        [Range(0, 1)] public float spatialBlend = 0f;
        public bool loop = false;

        protected override bool _TryReact(Component component)
        {
            var audio = (component as AudioSource);
            audio.resource = resource;
            audio.volume = volume.RandomValue;
            audio.pitch = pitch.RandomValue;
            audio.spatialBlend = spatialBlend;
            audio.loop = loop;

            audio.Play();
            return true;
        }
    }
}
