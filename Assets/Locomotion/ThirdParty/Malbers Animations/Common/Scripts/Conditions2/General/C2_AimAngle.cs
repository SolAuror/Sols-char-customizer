// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Scriptables; //MWC — FloatReference for the Angle Offset
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Conditions
{
    //MWC — Aim Limit as a Condition: same check the [Projectile - Fire] Processor does internally
    //(ItemProcessorProjectile.AimInLimit), but evaluated BEFORE an Action/Mode starts, so an impossible
    //angle blocks the whole action (no draw, no fire animation) instead of just swallowing the projectile.
    [System.Serializable, AddTypeMenu("General/Aim Angle")]
    public class C2_AimAngle : ConditionCore
    {
        /// <summary> Which angle of the Aim component is compared </summary>
        public enum AimAxis
        {
            /// <summary> Left-Right angle from the character forward (Aim.HorizontalAngle) </summary>
            Horizontal,
            /// <summary> Up-Down angle from the character horizon (Aim.VerticalAngle) </summary>
            Vertical
        }

        public override string DynamicName =>
            $"Aim [{axis}] Angle inside [{Limit.minValue} , {Limit.maxValue}]" +
            $"{(AngleOffset.Value != 0 ? $" (Offset: {AngleOffset.Value})" : "")}"; //MWC — surface a rotated range on the row

        [Tooltip("Aim component to read the angles from. It's found automatically on the Condition's Target")]
        [Hide(nameof(LocalTarget))]
        public Aim Target;

        [Tooltip("Which angle to check:\n" +
            "[Horizontal] Left-Right angle from the character's forward (0 = straight ahead).\n" +
            "[Vertical] Up-Down angle from the character's horizon (0 = level, 90 = straight up)")]
        public AimAxis axis = AimAxis.Horizontal;

        [Tooltip("Angle range (degrees) the aim must be INSIDE for the condition to be true.\n" +
            "Use the [Invert] toggle to check for the opposite (outside the range).\n" +
            "E.g. [-70 , 70] while mounted blocks shooting backwards")]
        [MinMaxRange(-180, 180)]
        public RangedFloat Limit = new(-90, 90);

        //MWC — the range used to be nailed to the character's forward. The offset ROTATES it, so the allowed cone can
        //sit anywhere: E.g. Offset -90 with a [-60 , 60] Limit allows only the character's left side. It is a
        //FloatReference so it can be driven by a Float Var at runtime (mounted vs on foot, left vs right handed…).
        [Tooltip("Rotates the whole [Limit] range by this many degrees, so the allowed cone does not have to be " +
            "centered on the character's forward.\n\n" +
            "The check becomes: the aim angle, MEASURED FROM this offset direction, must be inside [Limit].\n" +
            "E.g. Offset -90 with a Limit of [-60 , 60] allows only the character's left side.\n" +
            "Wrapping is handled, so an offset that pushes the range past ±180 still works")]
        public FloatReference AngleOffset = new(0);

        [Tooltip("Radius of the Gizmo arc drawn on the Scene view")]
        public float GizmoRadius = 2f;

        /// <summary> Current angle of the selected axis. Zero when there's no Aim component </summary>
        public float CurrentAngle => Target == null ? 0
            : (axis == AimAxis.Horizontal ? Target.HorizontalAngle : Target.VerticalAngle);

        /// <summary> <see cref="CurrentAngle"/> measured from the [Angle Offset] direction, wrapped to [-180,180] </summary>
        public float OffsetAngle => Mathf.DeltaAngle(AngleOffset.Value, CurrentAngle);

        protected override void _SetTarget(Object target) => VerifyComponent(target, ref Target);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;

        protected override bool _Evaluate()
        {
            if (Target == null) return false; //No Aim component: there's no angle to compare against

            //MWC — compared in the OFFSET's frame, not the character's forward. Mathf.DeltaAngle does the wrapping,
            //so an offset near ±180 does not break the comparison. With Offset 0 it is the raw angle, as before.
            var angle = OffsetAngle;
            var result = Limit.IsInRange(angle);

            Debugging($"[Aim {axis} Angle: {CurrentAngle:F2}" +
                $"{(AngleOffset.Value != 0 ? $" → {angle:F2} (Offset {AngleOffset.Value})" : "")}] " +
                $"inside [{Limit.minValue} , {Limit.maxValue}] ? ", result, Target);

            return result;
        }

        /// <summary> Draws the allowed angle arc, so the limit can be authored by eye on the Scene view </summary>
        public override void DrawGizmos(Component target)
        {
#if UNITY_EDITOR
            if (target == null || GizmoRadius <= 0) return;

            //Use the Aim component transform when it's already resolved, otherwise the Condition's target
            var t = Target != null ? Target.transform : target.transform;

            //Horizontal: arc around the Up axis from the forward. Vertical: arc around the Right axis from the forward
            var axisVector = axis == AimAxis.Horizontal ? t.up : -t.right; //MWC — negated: a POSITIVE Vertical Angle means aiming UP
            //MWC — the arc starts at [Offset + Limit Min]: the gizmo shows where the rotated cone really points
            var from = Quaternion.AngleAxis(AngleOffset.Value + Limit.minValue, axisVector) * t.forward;
            var sweep = Limit.maxValue - Limit.minValue;

            Handles.color = new Color(0, 1, 0, 0.1f);
            Handles.DrawSolidArc(t.position, axisVector, from, sweep, GizmoRadius);
            Handles.color = Color.green;
            Handles.DrawWireArc(t.position, axisVector, from, sweep, GizmoRadius);
#endif
        }
    }
}
