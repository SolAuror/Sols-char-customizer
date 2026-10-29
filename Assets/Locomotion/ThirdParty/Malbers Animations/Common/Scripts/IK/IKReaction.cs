// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using System;
using UnityEngine;

namespace MalbersAnimations.IK
{
    [System.Serializable, AddTypeMenu("Malbers/IK")]
    [HelpURL(ReactionDocs.Malbers + "ik")]
    public class IKReaction : Reaction
    {
        public override string DynamicName =>
            $"IK {action} [{IKSet.Value}]{(targets != null && targets.Length > 0 ? $" ({targetAction} {targets.Length})" : string.Empty)}";

        public override Type ReactionType => typeof(IIKSource);
        public enum IKReactionType { Activate, Deactivate }
        public enum IKReactionTargetType { SetTargets, ClearTargets }

        public IKReactionType action = IKReactionType.Activate;
        public StringReference IKSet = new("IKSetName");

        public IKReactionTargetType targetAction = IKReactionTargetType.SetTargets;

        [Tooltip("The targets to set for the IK Source. When Activate, or Set Targets is called")]
        public IDPair<IKTag, TransformReference>[] targets;

        protected override bool _TryReact(Component reactor)
        {
            if (reactor is not IKManager IK) return false; //If the source is null, return false (No Component to React_)

            switch (action)
            {
                case IKReactionType.Activate: IK.Set_Enable(IKSet); break;
                case IKReactionType.Deactivate: IK.Set_Disable(IKSet); break;
                default: break;
            }

            if (targets == null || targets.Length == 0) return true; //If there are no targets to set, return true (Reaction is done)

            switch (targetAction)
            {
                case IKReactionTargetType.SetTargets:

                    for (int i = 0; i < targets.Length; i++)
                    {
                        IK.Target_Add_Global(targets[i]);
                    }

                    break;
                case IKReactionTargetType.ClearTargets:
                    for (int i = 0; i < targets.Length; i++)
                    {
                        IK.Target_Remove_Global(targets[i]);
                    }
                    break;
                default:
                    break;
            }


            return true;
        }
    }
}