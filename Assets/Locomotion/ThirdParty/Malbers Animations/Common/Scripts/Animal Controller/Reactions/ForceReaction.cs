// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Controller;
using MalbersAnimations.Scriptables;
using System.Collections;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable, AddTypeMenu("Malbers/Animal/Add Force to Animal")]
    [HelpURL(ReactionDocs.Animal + "add-force-to-animal")]
    public class ForceReaction : MReaction
    {
        //Short row name: the direction (or the push/pull Target), the force and how long it is applied
        public override string DynamicName => Mode switch
        {
            DirectionType.TargetPush => $"Animal Force: Push from [{TargetName}] [{force.Value}] [{time.Value}s]",
            DirectionType.TargetPull => $"Animal Force: Pull to [{TargetName}] [{force.Value}] [{time.Value}s]",
            _ => $"Animal Force: {Mode} {Direction.Value:F1} [{force.Value}] [{time.Value}s]",
        };

        /// <summary>Name of the push/pull Target shown on the row</summary>
        private string TargetName => m_Value != null && m_Value.Value != null ? m_Value.Value.name : "None";

        public enum DirectionType { Local, World, TargetPush, TargetPull }

        [Tooltip("Direction mode to be applied the force on the Animal. World, or Local")]
        public DirectionType Mode = DirectionType.Local;

        [Tooltip("Use a Target when the Mode is set to FromTarget or To Target")]
        [Hide("Mode", false, (int)DirectionType.TargetPull, (int)DirectionType.TargetPush)]
        public TransformReference m_Value;

        [Hide("Mode", true, (int)DirectionType.TargetPull, (int)DirectionType.TargetPush)]

        [Tooltip("Relative Direction of the Force to apply")]
        public Vector3Reference Direction = new(Vector3.forward);

        [Tooltip("Time to Apply the force")]
        public FloatReference time = new(1f);
        [Tooltip("Amount of force to apply")]
        public FloatReference force = new(10f);
        [Tooltip("Acceleration to apply to the force")]
        public FloatReference Acceleration = new(2f);
        [Tooltip("Drag to Decrease the Force after the Force time has pass")]
        public FloatReference ExitDrag = new(2f);
        [Tooltip("Set if the Animal is grounded when adding a force")]
        public BoolReference ResetGravity = new(false);

        // public BoolReference UseGravity = new(false);



        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;

            if (animal.enabled && animal.gameObject.activeInHierarchy)
            {
                animal.StartCoroutine(IForceC(animal));

                return true;
            }

            return false;
        }

        IEnumerator IForceC(MAnimal animal)
        {
            Vector3 dir = Mode switch
            {
                DirectionType.Local => animal.transform.InverseTransformDirection(Direction),
                DirectionType.World => (Vector3)Direction,
                DirectionType.TargetPush => animal.transform.position - m_Value.position,
                DirectionType.TargetPull => m_Value.position - animal.transform.position,
                _ => (Vector3)Direction,
            };
            dir.Normalize();

            animal.Force_Add(dir, force, Acceleration, ResetGravity);
            // animal.UseGravity = UseGravity;

            yield return new WaitForSeconds(time);

            animal.Force_Remove(ExitDrag);
        }
    }
}
