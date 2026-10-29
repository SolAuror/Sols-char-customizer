// Last Change: 9/17/2026 5:33pm
using UnityEngine;
using UnityEngine.Audio;

namespace MalbersAnimations.Items
{
    [System.Serializable, AddTypeMenu("General/Sound [Audio]")]
    [HelpURL(ItemDocs.Processors + "sound-audio")]
    public class ItemProcessorSound : ItemProcessor
    {
        [RequiredField] public AudioSource audio;
        [RequiredField] public AudioResource resource;
        [MinMaxRange(0, 1)] public RangedFloat volume = new(1, 1);
        [MinMaxRange(-3, 3)] public RangedFloat pitch = new(1, 1);
        [Range(0, 1)] public float spatialBlend = 0f;
        public bool loop = false;

        public override string DynamicName => $"Audio Play ({(resource ? resource.name : "<Missing>")}). [Volume {volume.minValue:F2}-{volume.maxValue:F2}]";

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (resource) PlayAudio();
        }

        private void PlayAudio()
        {
            audio.resource = resource;
            audio.volume = volume.RandomValue;
            audio.pitch = pitch.RandomValue;
            audio.spatialBlend = spatialBlend;
            audio.loop = loop;
            audio.Play();
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (!resource) return $"Missing an Audio Resource.";
            return string.Empty;
        }

        public override void OnValidate(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (audio == null)
            {
                audio = item.GetComponentInChildren<AudioSource>();
                if (audio == null)
                {
                    audio = item.gameObject.AddComponent<AudioSource>();
                }
            }
        }
    }
}