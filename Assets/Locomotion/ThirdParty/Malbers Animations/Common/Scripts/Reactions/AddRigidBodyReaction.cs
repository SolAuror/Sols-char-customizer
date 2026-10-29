// Last Change: 9/18/2026 10:30pm

using System;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Unity/Rigidbody/RigidBody [Add]")]
    [HelpURL(ReactionDocs.Unity + "rigidbody-add")]
    public class AddRigidBodyReaction : Reaction
    {
        public override string DynamicName => $"Add Rigidbody [Mass {mass}]{(isKinematic ? " [Kinematic]" : string.Empty)}{(useGravity ? string.Empty : " [No Gravity]")}";

        public override Type ReactionType => typeof(Component);

        public float mass = 10f;
        public float drag = 0f;
        public float angularDrag = 0.05f;

        public bool isKinematic = false;
        public bool useGravity = true;

        public CollisionDetectionMode CollisionDetection = CollisionDetectionMode.Discrete;
        public RigidbodyConstraints constraints = RigidbodyConstraints.None;


        protected override bool _TryReact(Component component)
        {
            var Rb = component.gameObject.GetOrAddComponent<Rigidbody>();

            Rb.mass = mass;
            Rb.linearDamping = drag;
            Rb.angularDamping = angularDrag;
            Rb.isKinematic = isKinematic;
            Rb.constraints = constraints;
            Rb.collisionDetectionMode = CollisionDetection;

            return true;
        }
    }
}
