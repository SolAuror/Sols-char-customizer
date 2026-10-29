using MalbersAnimations.Items;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    // MWC — Super Item AI Decision: look for a free Super Item in the world (registry based, no physics) and set it as the AI Target
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Super Item/Find Super Item", order = 300)]
    public class SIFindItemDecision : MAIDecision
    {
        public override string DisplayName => "Super Item/Find Super Item";

        [Tooltip("Super Item IDs to look for. Leave empty to find any free Super Item")]
        public IDList<SuperItemID> items;

        [Tooltip("Search radius from the Animal Eyes")]
        public FloatReference Range = new(15);

        [Tooltip("Angle of vision in front of the Animal Eyes. 360 = all around")]
        [Range(0f, 360f)]
        public float LookAngle = 360f;

        [Tooltip("Ignore Items hidden behind the Obstacle Layers (raycast from the Eyes)")]
        public bool CheckLineOfSight = false;

        [Hide(nameof(CheckLineOfSight)), Tooltip("Layers that can block the Animal Eyes")]
        public LayerReference ObstacleLayer = new(1);

        [Tooltip("Pick randomly among the found Items instead of the closest one")]
        public bool ChooseRandomly = false;

        [Space(10), Tooltip("If an Item is found, assign it as the new AI Target. Keep it OFF when the [Pick Item] Task does the search and the Brain Target must stay (e.g. the Player)")]
        public bool AssignTarget = false;

        [Tooltip("If an Item is found, also start moving to it")]
        public bool MoveToTarget = true;

        public Color debugColor = new(0, 0, 0.7f, 0.3f);

        //MWC: Per-agent runtime state. Lives on the brain (not on this shared SO) so each AI keeps its own found Item.
        private class FindState : BrainState
        {
            /// <summary>Last free Item found by this decision</summary>
            public SuperItem Found;
        }

        /// <summary>Reused buffer for the random pick (no per-call allocation)</summary>
        private static readonly List<SuperItem> Candidates = new();

        public override void PrepareDecision(MAnimalBrain brain, int Index)
        {
            brain.DecisionState<FindState>(Index).Found = null; //reset per activation (typed state persists across re-entries)
        }

        public override bool Decide(MAnimalBrain brain, int Index) => Find(brain, Index) != null;

        /// <summary>Assigns the found Item as the AI Target (LookDecision pattern: the assignment happens on the transition)</summary>
        public override void FinishDecision(MAnimalBrain brain, int Index)
        {
            var item = Find(brain, Index);

            if (AssignTarget && item != null)
                brain.AIControl.SetTarget(item.transform, MoveToTarget);
        }

        /// <summary>Searches the Super Item registry for the closest (or a random) free Item inside the Range, Angle and Line of Sight</summary>
        private SuperItem Find(MAnimalBrain brain, int Index)
        {
            var state = brain.DecisionState<FindState>(Index);
            state.Found = null;

            var eyes = brain.Eyes != null ? brain.Eyes : brain.Animal.transform;
            var origin = eyes.position;
            var forward = Vector3.ProjectOnPlane(eyes.forward, brain.Animal.UpVector).normalized;
            var cosAngle = Mathf.Cos(LookAngle * 0.5f * Mathf.Deg2Rad);
            var maxSqr = Range * Range;
            var filterIDs = items != null && items.HasItems;

            float closestSqr = float.MaxValue;
            SuperItem best = null;
            Candidates.Clear();

            foreach (var item in SuperItem.AllItems)
            {
                if (item == null || !item.IsFree) continue;                                  //Equipped, holstered or disabled
                if (filterIDs && !items.HasID(item.ItemType)) continue;                      //Not an Item we want

                var direction = item.transform.position - origin;
                var sqrDistance = direction.sqrMagnitude;
                if (sqrDistance > maxSqr) continue;                                          //Too far

                if (LookAngle < 360f && Vector3.Dot(direction.normalized, forward) <= cosAngle) continue; //Outside the vision cone

                if (CheckLineOfSight &&
                    Physics.Raycast(origin, direction.normalized, out var hit, Mathf.Sqrt(sqrDistance), ObstacleLayer, QueryTriggerInteraction.Ignore) &&
                    !hit.transform.IsChildOf(item.transform)) continue;                      //Something is in between

                if (ChooseRandomly)
                {
                    Candidates.Add(item);
                }
                else if (sqrDistance < closestSqr)
                {
                    closestSqr = sqrDistance;
                    best = item;
                }
            }

            if (ChooseRandomly && Candidates.Count > 0)
                best = Candidates[Random.Range(0, Candidates.Count)];

            Candidates.Clear();
            state.Found = best;
            return best;
        }

#if UNITY_EDITOR && MALBERS_DEBUG
        public override void DrawGizmos(MAnimalBrain brain)
        {
            if (brain == null || brain.Animal == null) return;

            var eyes = brain.Eyes != null ? brain.Eyes : brain.Animal.transform;
            var up = brain.Animal.UpVector;

            UnityEditor.Handles.color = debugColor;

            if (LookAngle >= 360f)
            {
                UnityEditor.Handles.DrawSolidDisc(eyes.position, up, Range);
            }
            else
            {
                var forward = Vector3.ProjectOnPlane(eyes.forward, up);
                var rotatedForward = Quaternion.Euler(0, -LookAngle * 0.5f, 0) * forward;
                UnityEditor.Handles.DrawSolidArc(eyes.position, up, rotatedForward, LookAngle, Range);
            }

            UnityEditor.Handles.color = new Color(debugColor.r, debugColor.g, debugColor.b, 1f);
            UnityEditor.Handles.DrawWireDisc(eyes.position, up, Range);

            //Dotted line to every free Item in the scene (Play Mode only: the registry fills at runtime)
            if (Application.isPlaying)
            {
                foreach (var item in SuperItem.AllItems)
                {
                    if (item == null || !item.IsFree) continue;
                    UnityEditor.Handles.DrawDottedLine(eyes.position, item.transform.position, 4f);
                }
            }
        }
#endif

        private void Reset() => Description = "Look for a free Super Item in the world (by ID, inside a range and angle) and set it as the AI Target so it can be picked";
    }
}
