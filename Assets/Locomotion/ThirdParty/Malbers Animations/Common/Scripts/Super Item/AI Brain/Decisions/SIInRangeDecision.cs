using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    // MWC — Super Item AI Decision: is the Brain Target inside a distance band (e.g. melee reach or bow range)?
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Super Item/Target In Range", order = 300)]
    public class SIInRangeDecision : MAIDecision
    {
        public override string DisplayName => "Super Item/Target In Range";

        [Tooltip("Minimum distance to the Target (e.g. a bow should not fire point blank)")]
        public FloatReference MinDistance = new(0);

        [Tooltip("Maximum distance to the Target. 0 = no limit")]
        public FloatReference MaxDistance = new(2);

        public Color debugColor = new(0, 0.7f, 0, 0.3f);

        public override bool Decide(MAnimalBrain brain, int Index)
        {
            if (brain.Target == null) return false;

            var distance = Vector3.Distance(brain.Animal.transform.position, brain.Target.position);

            return distance >= MinDistance && (MaxDistance <= 0 || distance <= MaxDistance);
        }

#if UNITY_EDITOR && MALBERS_DEBUG
        public override void DrawGizmos(MAnimalBrain brain)
        {
            if (brain == null || brain.Animal == null || MaxDistance <= 0) return;

            var center = brain.Animal.transform.position;
            var up = brain.Animal.UpVector;

            UnityEditor.Handles.color = debugColor;
            UnityEditor.Handles.DrawSolidDisc(center, up, MaxDistance);
            UnityEditor.Handles.color = new Color(debugColor.r, debugColor.g, debugColor.b, 1f);
            UnityEditor.Handles.DrawWireDisc(center, up, MaxDistance);
            if (MinDistance > 0) UnityEditor.Handles.DrawWireDisc(center, up, MinDistance);
        }
#endif

        private void Reset() => Description = "Check if the Target is inside a distance band (Min/Max) from the Animal. E.g. Sword 0-2, Bow 4-18";
    }
}
