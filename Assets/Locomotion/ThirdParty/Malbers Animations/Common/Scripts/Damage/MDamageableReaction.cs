// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Damage/Damageable Set Profile")]
    [HelpURL(ReactionDocs.Malbers + "damageable-set-profile")]
    public class MDamageableReaction : Reaction
    {
        public override string DynamicName =>
            $"Set Damageable Profile [{(RestoreDefault ? "Default" : Profile.Value)}]";

        [Tooltip("Restore the Default Profile of the Main Damageable Component of a Character")]
        public bool RestoreDefault = false;

        [Tooltip("Changes the Profile of the Main Damageable Component of a Character")]
        [Hide(nameof(RestoreDefault), true)]
        public StringReference Profile = new();

        public override System.Type ReactionType => typeof(MDamageable);

        protected override bool _TryReact(Component reactor)
        {
            var damageable = reactor as MDamageable;

            if (RestoreDefault)
                damageable.Profile_Restore();
            else
                damageable.Profile_Set(Profile);

            return true;
        }
    }
}



