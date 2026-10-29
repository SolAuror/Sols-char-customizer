using MalbersAnimations.Items;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    // MWC — Super Item AI Decision: check a Stat on the equipped Super Item (Chamber, Charge, Durability…) or the ammo in the Pouch
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Super Item/Check Item Stat", order = 300)]
    public class SIItemStatDecision : MAIDecision
    {
        public override string DisplayName => "Super Item/Check Item Stat";

        public enum CheckOption { ItemStat, PouchAmmo }
        public enum StatOption { Compare, IsEmpty, IsFull }

        [Tooltip("Check the Decision on the Animal(Self) or the Target(Target)")]
        public Affected CheckOn = Affected.Self;

        [Tooltip("Item Stat: a Stat on the equipped Item (Chamber, Charge, Durability…).\nPouch Ammo: the ammo reserve on the Pouch Manager")]
        public CheckOption Check = CheckOption.ItemStat;

        [Hide(nameof(Check), (int)CheckOption.ItemStat)]
        [Tooltip("Only look on this Item. Leave empty for the first equipped Item that has the Stat")]
        public SuperItemID itemID;

        [Hide(nameof(Check), (int)CheckOption.ItemStat)]
        [Tooltip("Stat to check on the Item")]
        public StatID stat;

        [Hide(nameof(Check), (int)CheckOption.ItemStat)]
        [Tooltip("What to check on the Stat")]
        public StatOption Option = StatOption.Compare;

        [Hide(nameof(Check), (int)CheckOption.PouchAmmo)]
        [Tooltip("Ammo type to check on the Pouch Manager")]
        public ProjectileID ammo;

        [Tooltip("Comparison used by Compare / Pouch Ammo")]
        public ComparerNumber comparer = ComparerNumber.Less;

        [Tooltip("Value to compare the Stat / Ammo amount against")]
        public FloatReference value = new(0);

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

            return Check switch
            {
                CheckOption.ItemStat => CheckItemStat(manager),
                CheckOption.PouchAmmo => ammo != null && manager.Pouch != null && manager.Pouch.HasAmmo(ammo, Mathf.RoundToInt(value), comparer),
                _ => false,
            };
        }

        /// <summary>Finds the Stat on the equipped Item and evaluates the selected Option</summary>
        private bool CheckItemStat(SuperItemManager manager)
        {
            var st = FindStat(manager);
            if (st == null) return false;

            return Option switch
            {
                StatOption.Compare => st.Value.MCompare(value, comparer),
                StatOption.IsEmpty => st.IsEmpty,
                StatOption.IsFull => st.IsFull,
                _ => false,
            };
        }

        /// <summary>Stat on the selected Item, or on the first equipped Item (Default Item included) that has it</summary>
        private IMStat FindStat(SuperItemManager manager)
        {
            if (stat == null) return null;

            if (itemID != null)
            {
                var item = manager.Get_ItemEquipped(itemID);
                return item != null ? item.Stat_Get(stat) : null;
            }

            for (int i = 0; i < manager.EquipPoints.Count; i++)
            {
                var point = manager.EquipPoints[i];
                if (point == null || point.Item == null) continue;

                var st = point.Item.Stat_Get(stat);
                if (st != null) return st;
            }

            return manager.DefaultItemActive ? manager.DefaultItem.Stat_Get(stat) : null;
        }

        private void Reset() => Description = "Check a Stat on the equipped Super Item (e.g. Chamber empty → Reload) or the ammo reserve on the Pouch Manager";
    }
}
