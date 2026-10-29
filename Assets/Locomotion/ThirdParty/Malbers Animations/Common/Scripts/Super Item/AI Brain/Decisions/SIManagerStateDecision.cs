// Last Change: 9/29/2026 3:10pm
using MalbersAnimations.Items;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    // Super Item AI Decision: state of the Super Item Manager (Self or Target) — equipping
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Super Item/Manager State", order = 300)]
    public class SIManagerStateDecision : MAIDecision
    {
        public override string DisplayName => "Super Item/Manager State";

        [Tooltip("Check the Decision on the Animal(Self) or the Target(Target)")]
        public Affected CheckOn = Affected.Target;

        /// <summary>Caches the Super Item Manager of the Animal or the Target</summary>
        public override void PrepareDecision(MAnimalBrain brain, int Index)
        {
            switch (CheckOn)
            {
                case Affected.Self:
                    brain.DecisionsVars[Index].mono = brain.Animal.FindComponent<SuperItemManager>(); //Cache the Super Item Manager in the Animal
                    break;
                case Affected.Target:
                    brain.DecisionsVars[Index].mono = brain.Target != null ? brain.Target.FindComponent<SuperItemManager>() : null; //Cache the Super Item Manager in the Target
                    break;
                default:
                    break;
            }
        }

        /// <summary>True while the Manager plays an Equip/Unequip animation</summary>
        public override bool Decide(MAnimalBrain brain, int Index)
        {
            var manager = brain.DecisionsVars[Index].mono as SuperItemManager;
            return manager != null && manager.IsEquipping;
        }

        private void Reset() => Description = "Check the state of the Super Item Manager (Self or Target): equipping an Item";
    }
}
