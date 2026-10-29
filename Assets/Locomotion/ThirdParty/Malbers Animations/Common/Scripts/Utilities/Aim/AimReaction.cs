// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using System;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]

    [AddTypeMenu("Tools/Aim")]
    [HelpURL(ReactionDocs.Tools + "aim")]
    public class AimReaction : Reaction
    {
        //An empty Target clears the Aim, so the row says so instead of showing [None]
        public override string DynamicName => NewTarget != null && NewTarget.Value != null ? $"Aim Set Target [{NewTarget.Value.name}]" : "Aim Clear Target";

        [Tooltip("Set a new Target to the Aim Component. If left empty, it will clear the target")]
        public GameObjectReference NewTarget = new();

        public override Type ReactionType => typeof(Aim);

        protected override bool _TryReact(Component reactor)
        {
            if (reactor is Aim aimer)
            {
                if (NewTarget.Value)
                {
                    aimer.SetTarget(NewTarget.Value);
                }
                else
                {
                    aimer.ClearTarget();
                }
            }
            return true;
        }
    }
}