using MalbersAnimations.Controller;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    /// <summary>MWC: Continuous force while the Animal is inside a zone (Wind, Spring Pads).
    /// Use [Enter] on the Zone Activation Reaction and [Exit] on the Zone Exit Reaction. Same logic as the old Zone Force type</summary>
    [System.Serializable, AddTypeMenu("Malbers/Animal/Zone Force")]
    [HelpURL(ReactionDocs.Animal + "add-force-to-animal")]
    public class ZoneForceReaction : MReaction
    {
        public enum ForceAction { Enter, Exit }

        public override string DynamicName => action == ForceAction.Enter
            ? $"Zone Force [Enter] Force[{Force.Value}] Acel[{EnterAcceleration.Value}] Limit[{LimitForce.Value}]"
            : $"Zone Force [Exit] Drag[{ExitDrag.Value}]";

        public ForceAction action = ForceAction.Enter;

        [Tooltip("The force is applied on the Up direction of this Transform (usually the Zone). If is empty the World Up direction is used")]
        [Hide(nameof(action), (int)ForceAction.Enter)]
        public Transform Direction;

        [Tooltip("Amount of Force that will be applied to the Animal")]
        [Hide(nameof(action), (int)ForceAction.Enter)]
        public FloatReference Force = new(10);

        [Tooltip("Acceleration to applied the Force when the Animal enters the zone")]
        [Hide(nameof(action), (int)ForceAction.Enter)]
        public FloatReference EnterAcceleration = new(2);

        [Tooltip("Limit the Current Force the animal may have")]
        [Hide(nameof(action), (int)ForceAction.Enter)]
        public FloatReference LimitForce = new(8);

        [Tooltip("Change if the Animal is Grounded when entering the Force Zone")]
        [Hide(nameof(action), (int)ForceAction.Enter)]
        public BoolReference ForceGrounded = new();

        [Tooltip("Can the Animal be controller while on the Air?")]
        [Hide(nameof(action), (int)ForceAction.Enter)]
        public BoolReference ForceAirControl = new(true);

        [Tooltip("Exit Drag to decrease the Force when the Animal exits the zone")]
        [Hide(nameof(action), (int)ForceAction.Exit)]
        public FloatReference ExitDrag = new(4);

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;

            if (action == ForceAction.Enter)
            {
                var StartExtForce = animal.CurrentExternalForce + animal.GravityStoredVelocity; //Calculate the Starting force

                if (StartExtForce.magnitude > LimitForce)
                    StartExtForce = StartExtForce.normalized * LimitForce; //Add the Bounce

                animal.CurrentExternalForce = StartExtForce;

                var dir = Direction != null ? Direction.up : Vector3.up;

                animal.Force_Add(dir, Force, EnterAcceleration, true, ForceAirControl.Value, LimitForce);

                if (animal.ActiveState is Fall fall) //If we enter to a zone from the Fall state.. Reset the Fall Current Distance
                    fall.FallCurrentDistance = 0;

                animal.GravityTime = 0;
                animal.Grounded = ForceGrounded.Value;
                return true;
            }
            else
            {
                if (animal.ActiveState.ID == StateEnum.Fall) animal.UseGravity = true;  //If we are on the Fall State -- Reactivate the Gravity

                if (ExitDrag > 0)
                {
                    animal.ExternalForceAcel = ExitDrag;
                    animal.ExternalForce = Vector3.zero;
                }
                return true;
            }
        }
    }
}
