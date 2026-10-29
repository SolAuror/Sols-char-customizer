// Last Change: 9/24/2026 12:57pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Arrived to Target", order = -100)]
    public class ArriveDecision : MAIDecision
    {
        [Tooltip("Choose whether to check the target by Name or by Transform")]
        public CheckTarget CheckBy = CheckTarget.Name; // Enum selection
        public enum CheckTarget
        {
            None,
            Name,
            Transform
        }

        public override string DisplayName => "Movement/Has Arrived";
        [Tooltip("Use it if you want to know if we have arrived to a specific Target")]
        [Hide(nameof(CheckBy), 1)]
        public string TargetName = string.Empty;

        [Hide(nameof(CheckBy), 2)]
        [Tooltip("Reference Transform (used when CheckBy is Transform)")]
        public TransformVar ReferenceTransform; // Added reference transform

        /// <summary>True when the AI has arrived, optionally to a target with a given name or transform</summary>
        public override bool Decide(MAnimalBrain brain, int index)
        {
            if (!brain.AIControl.HasArrived) return false; // If not arrived, return false

            return CheckBy switch
            {
                CheckTarget.None => true,
                //An empty name means any target (older assets load with CheckBy = Name and no name)
                CheckTarget.Name => string.IsNullOrEmpty(TargetName) ||
                    (brain.Target != null && (brain.Target.name == TargetName || brain.Target.root.name == TargetName)),
                CheckTarget.Transform => ReferenceTransform != null && brain.Target == ReferenceTransform.Value,// Now compares with ReferenceTransform
                _ => false,
            };
        }
    }
}