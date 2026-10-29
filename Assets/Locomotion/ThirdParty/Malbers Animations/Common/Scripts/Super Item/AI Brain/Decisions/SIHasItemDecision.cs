using MalbersAnimations.Items;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    // MWC — Super Item AI Decision: does the Super Item Manager (Self or Target) have Items equipped, holstered or owned?
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Super Item/Has Item", order = 300)]
    public class SIHasItemDecision : MAIDecision
    {
        public override string DisplayName => "Super Item/Has Item";

        public enum WhereOption { Equipped, Holstered, Owned, Unarmed }
        public enum ComparisonOption { HasAny, HasAll }

        [Tooltip("Check the Decision on the Animal(Self) or the Target(Target)")]
        public Affected CheckOn = Affected.Self;

        [Tooltip("Equipped: in the hands.\nHolstered: stored in a Holster.\nOwned: equipped OR holstered.\nUnarmed: nothing in the hands (the Default Item does not count)")]
        public WhereOption Where = WhereOption.Equipped;

        [Hide(nameof(Where), (int)WhereOption.Equipped, (int)WhereOption.Holstered, (int)WhereOption.Owned)]
        [Tooltip("Super Item IDs to look for. Empty = ANY Item.\n" +
            "Include: the listed IDs. Duplicates count with [Has All]: Melee, Melee = two melee weapons.\n" +
            "Exclude: any Item that is NOT in the list")]
        public IDListCheck<SuperItemID> items = new();

        [Hide(nameof(Where), (int)WhereOption.Equipped, (int)WhereOption.Holstered, (int)WhereOption.Owned)]
        [Tooltip("Include → Has Any: at least one of the IDs. Has All: every entry of the list (a duplicated ID needs that many Items).\n" +
            "Exclude → Has Any: at least one Item outside the list. Has All: holds Items and none of them is in the list")]
        public ComparisonOption checkIf = ComparisonOption.HasAny;

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

            if (Where == WhereOption.Unarmed) return manager.EquippedCount == 0;

            //No IDs requested → any Item counts
            if (items == null || items.Empty) return CountHeld(manager, out _) > 0;

            if (items.include) return IncludeCheck(manager);

            //Exclude: Items whose ID is NOT in the list are the ones that pass
            var held = CountHeld(manager, out var passing);
            return checkIf == ComparisonOption.HasAny ? passing > 0 : held > 0 && passing == held;
        }

        /// <summary>Include mode. Has Any: one listed ID present. Has All: every entry satisfied (duplicates = that many Items)</summary>
        private bool IncludeCheck(SuperItemManager manager)
        {
            var ids = items.IDs;

            if (checkIf == ComparisonOption.HasAll)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    if (id == null || FirstIndexOf(id) != i) continue; //Each ID is evaluated once, on its first entry

                    if (Count(manager, id) < Occurrences(id)) return false; //Not enough Items of this type
                }
                return true;
            }

            foreach (var id in ids)
            {
                if (id != null && Count(manager, id) > 0) return true; //one present → passes HasAny
            }
            return false;
        }

        /// <summary>Number of Items with this ID at the selected place (Equipped, Holstered or Owned)</summary>
        private int Count(SuperItemManager manager, SuperItemID id) => Where switch
        {
            WhereOption.Equipped => manager.Count_Equipped(id),
            WhereOption.Holstered => manager.Count_Holstered(id),
            WhereOption.Owned => manager.Count_Owned(id),
            _ => 0,
        };

        /// <summary>Number of Items held at the selected place, and how many of them pass the Include/Exclude filter</summary>
        private int CountHeld(SuperItemManager manager, out int passing)
        {
            int held = 0;
            passing = 0;

            if (Where == WhereOption.Equipped || Where == WhereOption.Owned)
            {
                for (int i = 0; i < manager.EquipPoints.Count; i++)
                {
                    var point = manager.EquipPoints[i];
                    if (point == null || point.Item == null) continue;

                    held++;
                    if (Passes(point.Item)) passing++;
                }
            }

            if (Where == WhereOption.Holstered || Where == WhereOption.Owned)
            {
                for (int h = 0; h < manager.holsters.Count; h++)
                {
                    var holster = manager.holsters[h];
                    if (holster == null || holster.StoredItems == null) continue;

                    for (int s = 0; s < holster.StoredItems.Count; s++)
                    {
                        var item = holster.StoredItems[s];
                        if (item == null) continue;

                        held++;
                        if (Passes(item)) passing++;
                    }
                }
            }
            return held;
        }

        /// <summary>True when the Item passes the Include/Exclude filter (empty list = everything passes)</summary>
        private bool Passes(SuperItem item) => items == null || items.Empty || items.Check(item.ItemType, true);

        /// <summary>How many times this ID appears in the list</summary>
        private int Occurrences(SuperItemID id)
        {
            int count = 0;
            foreach (var entry in items.IDs)
                if (entry != null && entry.ID == id.ID) count++;
            return count;
        }

        /// <summary>Index of the first entry with this ID</summary>
        private int FirstIndexOf(SuperItemID id)
        {
            for (int i = 0; i < items.IDs.Count; i++)
                if (items.IDs[i] != null && items.IDs[i].ID == id.ID) return i;
            return -1;
        }

        private void Reset() => Description = "Check if the Super Item Manager (Self or Target) has Super Items equipped, holstered or owned (Include/Exclude list), or is unarmed";
    }
}
