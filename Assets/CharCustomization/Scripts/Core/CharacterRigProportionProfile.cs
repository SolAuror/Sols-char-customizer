using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [Serializable]
    public sealed class CharacterRigProportionChannelRange
    {
        [SerializeField] private CharacterRigProportionChannel channel;
        [SerializeField] private float minimumScale = 0.5f;
        [SerializeField] private float maximumScale = 1.5f;

        public CharacterRigProportionChannel Channel => channel;
        public float MinimumScale => minimumScale;
        public float MaximumScale => maximumScale;
    }

    [CreateAssetMenu(menuName = "Sol/Character Customization/Rig Proportion Profile", fileName = "CharacterRigProportionProfile")]
    public sealed class CharacterRigProportionProfile : ScriptableObject
    {
        [SerializeField] private List<CharacterRigProportionChannelRange> channelRanges = new();

        public float EvaluateScale(CharacterRigProportionChannel channel, float value, float fallbackMinimum, float fallbackMaximum)
        {
            GetRange(channel, fallbackMinimum, fallbackMaximum, out float minimum, out float maximum);
            return value < 0f
                ? Mathf.Lerp(1f, minimum, -value)
                : Mathf.Lerp(1f, maximum, value);
        }

        private void GetRange(CharacterRigProportionChannel channel, float fallbackMinimum, float fallbackMaximum, out float minimum, out float maximum)
        {
            if (channelRanges != null)
            {
                foreach (CharacterRigProportionChannelRange range in channelRanges)
                {
                    if (range != null && range.Channel == channel)
                    {
                        minimum = range.MinimumScale;
                        maximum = range.MaximumScale;
                        return;
                    }
                }
            }

            minimum = fallbackMinimum;
            maximum = fallbackMaximum;
        }
    }
}
