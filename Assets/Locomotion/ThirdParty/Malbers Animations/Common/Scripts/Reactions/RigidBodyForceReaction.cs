// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Scriptables;
using System;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Unity/Rigidbody/RigidBody Add Force")]
    [HelpURL(ReactionDocs.Unity + "rigidbody-add-force")]
    public class RigidBodyForceReaction : Reaction
    {

        public override string DynamicName
        {
            get
            {
                var display = $"Rigidbody {action}"; //Name of the Reaction

                //the Reset actions have no force
                if (action is not (RB_ReactionForce.ResetLinearVelocity or RB_ReactionForce.ResetAngularVelocity or RB_ReactionForce.ResetAllVelocity))
                    display += $" [{force}] ({mode})";

                if (action == RB_ReactionForce.AddExplosion) display += $" [Radius: {radius}]";
                return display;
            }
        }


        public enum RB_ReactionForce { AddForce, AddForceAtPosition, AddExplosion, AddTorque, AddRelativeForce, AddRelativeTorque, ResetLinearVelocity, ResetAngularVelocity, ResetAllVelocity }

        public override Type ReactionType => typeof(Rigidbody);

        public RB_ReactionForce action = RB_ReactionForce.AddForce;
        [Hide("action", true, 6, 7, 8)]
        public ForceMode mode = ForceMode.Force;

        public bool useGravity = true;

        [Tooltip("Direction and Position to apply to the force. Direction is Forward ")]
        [Hide("action", true, 6, 7, 8)]
        public TransformReference direction = new();
        [Tooltip("Intensity of the force to apply to the Reaction")]
        [Hide("action", true, 6, 7, 8)]
        public float force = 100f;

        [Hide("action", 2)]
        public float radius = 10f;
        [Hide("action", 2)]
        public float upModifier = 5f;

        protected override bool _TryReact(Component component)
        {
            var rb = component as Rigidbody;

            rb.isKinematic = false; //Setting kinematic to false because forces cannot be applied to kinematic objects
            rb.useGravity = useGravity;
            rb.constraints = RigidbodyConstraints.None; //make sure there's no constraints

            var Dir = direction.Value != null ? direction.Value.forward : component.transform.forward; //Get the direction and position to apply the force
            var Pos = direction.Value != null ? direction.Value.position : component.transform.position; //Get the position to apply the force

            switch (action)
            {
                case RB_ReactionForce.AddForce:
                    rb.AddForce(Dir * force, mode);
                    break;
                case RB_ReactionForce.AddForceAtPosition:
                    rb.AddForceAtPosition(Dir * force, Pos, mode);
                    break;
                case RB_ReactionForce.AddExplosion:
                    rb.AddExplosionForce(force, Pos, radius, upModifier, mode);
                    break;
                case RB_ReactionForce.AddTorque:
                    rb.AddTorque(Dir * force, mode);
                    break;
                case RB_ReactionForce.AddRelativeForce:
                    rb.AddRelativeForce(Dir * force, mode);
                    break;
                case RB_ReactionForce.AddRelativeTorque:
                    rb.AddRelativeTorque(Dir * force, mode);
                    break;
                case RB_ReactionForce.ResetLinearVelocity:
                    rb.linearVelocity = Vector3.zero; //Clear the velocity of the Rigidbody
                    break;
                case RB_ReactionForce.ResetAngularVelocity:
                    rb.angularVelocity = Vector3.zero; //Clear the velocity of the Rigidbody
                    break;
                case RB_ReactionForce.ResetAllVelocity:
                    rb.linearVelocity = Vector3.zero; //Clear the velocity of the Rigidbody
                    rb.angularVelocity = Vector3.zero; //Clear the velocity of the Rigidbody
                    break;
                default:
                    break;
            }

            return true;
        }
    }
}
