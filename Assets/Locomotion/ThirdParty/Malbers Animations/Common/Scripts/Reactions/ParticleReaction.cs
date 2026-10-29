// Last Change: 9/18/2026 10:30pm
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable, AddTypeMenu("Unity/Particle System")]
    [HelpURL(ReactionDocs.Unity + "particle-system")]
    public class ParticleReaction : Reaction
    {
        public override string DynamicName => action == ParticleAction.Color ? "Particle Set Color" : $"Particle Set Rate [{rate}]";
        public enum ParticleAction { Color, Rate, }

        public override System.Type ReactionType => typeof(ParticleSystem);

        public ParticleAction action = ParticleAction.Color;

        [Hide("action", (int)ParticleAction.Color)]
        public Color color = Color.white;

        [Hide("action", (int)ParticleAction.Rate)]
        public float rate = 0;

        protected override bool _TryReact(Component component)
        {
            if (component is ParticleSystem p)
            {
                switch (action)
                {
                    case ParticleAction.Color:
                        var particle = p.main;
                        particle.startColor = new ParticleSystem.MinMaxGradient(color);
                        return true;
                    case ParticleAction.Rate:
                        var emission = p.emission;
                        emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate);
                        return true;
                    default:
                        return true;
                }

            }
            return false;
        }
    }
}
