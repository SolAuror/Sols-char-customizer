using MalbersAnimations.Items;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    // MWC — Super Item AI Decision: is the Super Item Manager (Self or Target) playing an Item Action? (Aiming/Equipping live in SIManagerStateDecision)
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Super Item/Is Action Playing", order = 300)]
    public class SIActionPlayingDecision : MAIDecision
    {
        public override string DisplayName => "Super Item/Is Action Playing";

        [Tooltip("Check the Decision on the Animal(Self) or the Target(Target)")]
        public Affected CheckOn = Affected.Self;

        [Tooltip("Actions to look for.\nInclude: any of these Action IDs playing. Empty = ANY Action.\nExclude: any playing Action but these")]
        public IDListCheck<ItemActionID> actions = new();

        [Tooltip("Items to look on.\nInclude: only these equipped Items. Empty = ANY equipped Item.\nExclude: any equipped Item but these")]
        public IDListCheck<SuperItemID> items = new();

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

        public override bool Decide(MAnimalBrain brain, int Index)
        {
            var manager = brain.DecisionsVars[Index].mono as SuperItemManager;
            if (manager == null) return false;

            for (int i = 0; i < manager.EquipPoints.Count; i++)
            {
                var point = manager.EquipPoints[i];
                if (point != null && point.Item != null && ItemPlaying(point.Item)) return true;
            }

            //The Default Item (fists) is equipped directly, with no Equip Point
            return manager.DefaultItemActive && ItemPlaying(manager.DefaultItem);
        }

        /// <summary>True when the Item passes the Items filter and one of its playing Actions passes the Actions filter (empty Include lists = any)</summary>
        private bool ItemPlaying(SuperItem item)
        {
            if (items != null && !items.Empty && !items.Check(item.ItemType, true)) return false; //Include / Exclude Items

            var set = item.ActiveActionSet;
            if (set == null || set.actions == null) return false;

            for (int a = 0; a < set.actions.Count; a++)
            {
                var action = set.actions[a];
                if (action == null || !action.IsPlaying) continue;

                if (actions == null || actions.Empty) return true;                                 //Empty list: any playing Action counts
                if (action.ActionID != null && actions.Check(action.ActionID, true)) return true; //Include / Exclude Actions
            }
            return false;
        }

        private void Reset() => Description = "Check if the Super Item Manager (Self or Target) is playing an Item Action (filtered by Actions and Items)";
    }
}
