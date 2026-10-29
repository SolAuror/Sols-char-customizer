// Last Change: 9/17/2026 5:33pm
using UnityEngine;

namespace MalbersAnimations.Items
{
    // MWC — New processor: Look-At aligns the Item's Owner to the Item's current Target when the Action executes.
    // Based on AlignReaction (Tools/Align Look At). Optionally rotates the Target too, with its own angle offset
    // (e.g. TargetOffset = 180 makes the Target show its back for executions/finishers).
    [System.Serializable, AddTypeMenu("Animal/Align To Target")]
    [HelpURL(ItemDocs.Processors + "align-to-target")]
    public class ItemProcessorAlign : ItemProcessor
    {
        public override string DynamicName =>
            $"Align To Target [Time: {AlignTime}]" + (AlignTarget ? $" [Target Offset: {TargetOffset}]" : string.Empty);

        [Tooltip("Max distance to the Target to do the alignment. Zero ignores the distance check")]
        [Min(0)] public float Distance = 0;

        [Tooltip("Time needed to complete the alignment")]
        [Min(0)] public float AlignTime = 0.15f;

        [Tooltip("Angle offset added to the Character's final rotation (0 = face the Target directly)")]
        public float CharacterOffset = 0f;

        [Tooltip("Curve used to animate the alignment")]
        public AnimationCurve AlignCurve = new(MTools.DefaultCurve);

        [Tooltip("Also rotate the Target relative to the Character")]
        public bool AlignTarget = false;

        [Tooltip("Angle offset added to the Target's final rotation. (0 = the Target faces the Character. 180 = the Target shows its back to the Character. E.g. Executions)")]
        [Hide(nameof(AlignTarget))]
        public float TargetOffset = 180f;

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            // MWC — Align the Item's Owner to look at the Item's current Target
            var Owner = item.UserGo;
            var Target = item.Target;

            if (Owner == null || Target == null) return; // No one is holding the item or the item has no Target

            if (Distance > 0)
            {
                MDebug.DrawCircle(Owner.transform.position, Owner.transform.rotation, Distance, Color.green, 1f);

                var dist = Vector3.Distance(Owner.transform.position, Target.transform.position);
                if (dist > Distance) return; // Out of range
            }

            item.StartCoroutine(MTools.AlignLookAtTransform(Owner.transform, Target.transform, AlignTime, CharacterOffset, AlignCurve));

            if (AlignTarget) // MWC — Rotate the Target relative to the Character (TargetOffset 180 = show its back)
                item.StartCoroutine(MTools.AlignLookAtTransform(Target.transform, Owner.transform, AlignTime, TargetOffset, AlignCurve));
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            // MWC — the Item Target is set at runtime (Aimer, Interactor, or code), so it cannot be validated here
            if (AlignTime <= 0)
                return "[Align Time] is zero. The alignment will be instantaneous";
            return string.Empty;
        }
    }
}
