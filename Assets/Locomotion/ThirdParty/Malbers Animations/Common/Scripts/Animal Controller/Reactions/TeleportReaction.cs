// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Controller;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Animal/Teleport")]
    [HelpURL(ReactionDocs.Animal + "teleport")]
    public class TeleportReaction : MReaction
    {
        override public string DynamicName =>
        $"Teleport Animal to [{(Destination.Value != null ? Destination.Value.name : "Null")}]  {(UseRotation.Value ? "[Use Rotation]" : " ")}";

        public TransformReference Destination;
        public BoolReference UseRotation;

        protected override bool _TryReact(Component component)
        {
            if (Destination.Value == null)
            {
                Debug.Log("Destination in Teleport Reaction is Null");
                return false;
            }
            var animal = component as MAnimal;

            if (UseRotation.Value)
                animal.TeleportRot(Destination.Value);
            else
                animal.Teleport(Destination.Value);

            return true;
        }
    }
}
