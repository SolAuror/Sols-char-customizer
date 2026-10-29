using MalbersAnimations.Items;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    // MWC — Super Item AI Task: Equip / Holster / Drop Super Items owned by the AI through the Super Item Manager
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Tasks/Super Item/Equip - Holster", fileName = "new SI Equip Task")]
    public class SIEquipTask : MTask
    {
        public override string DisplayName => "Super Item/Equip - Holster";

        public enum DoOption { Equip, Holster, HolsterAll, Drop }
        public enum SelectByOption { ItemID, HolsterID }

        [Tooltip("Equip: draw an owned Item.\nHolster: store an equipped Item.\nHolster All: store every equipped Item.\nDrop: release an owned Item to the world")]
        public DoOption Do = DoOption.Equip;

        [Hide(nameof(Do), (int)DoOption.Equip, (int)DoOption.Holster, (int)DoOption.Drop)]
        [Tooltip("Select the Item by its Super Item ID (first owned match) or by the Holster it belongs to")]
        public SelectByOption SelectBy = SelectByOption.ItemID;

        [Hide(nameof(SelectBy), (int)SelectByOption.ItemID)]
        [Tooltip("Super Item IDs to look for (the first one owned wins).\nLeave empty for: Equip → first holstered Item. Holster/Drop → first equipped Item")]
        public IDList<SuperItemID> items;

        [Hide(nameof(SelectBy), (int)SelectByOption.HolsterID)]
        [Tooltip("Holster to draw from / store to / drop from")]
        public HolsterID holster;

        [Tooltip("Skip the Equip/Unequip animations (instant)")]
        public bool Fast = false;

        [Tooltip("Keep the Task running until the Equip/Unequip animation has finished (Task Done afterwards)")]
        public bool WaitForAnimation = true;

        /// <summary>Caches the Super Item Manager and performs the Equip/Holster/Drop</summary>
        public override void StartTask(MAnimalBrain brain, int index)
        {
            var manager = brain.Animal.FindComponent<SuperItemManager>();
            brain.TasksVars[index].mono = manager; //Store the Super Item Manager on the TaskVars

            if (manager == null)
            {
                Debug.Log("No Super Item Manager Found on the Animal", brain.Animal);
                brain.TaskDone(index);
                return;
            }

            switch (Do)
            {
                case DoOption.Equip: Equip(manager); break;
                case DoOption.Holster: Holster(manager); break;
                case DoOption.HolsterAll: manager.Item_HolsterAll(); break;
                case DoOption.Drop: Drop(manager); break;
                default: break;
            }

            if (!WaitForAnimation) brain.TaskDone(index);
        }

        /// <summary>Finishes the Task once the Equip/Unequip animation is over</summary>
        public override void UpdateTask(MAnimalBrain brain, int index)
        {
            var manager = brain.TasksVars[index].mono as SuperItemManager;

            if (manager == null || (!manager.IsEquipping && !brain.Animal.IsPlayingMode))
                brain.TaskDone(index);
        }

        /// <summary>Draws the selected Item (from its Holster when it is stored there)</summary>
        private void Equip(SuperItemManager manager)
        {
            if (SelectBy == SelectByOption.HolsterID)
            {
                if (holster != null) manager.Item_Equip_From_Holster(holster);
                return;
            }

            var item = FindOwned(manager, preferHolstered: true);
            if (item == null || item.IsEquipped) return;

            if (Fast) manager.Item_Equip(item, true);
            else if (item.IsInHolster) manager.Item_Equip_From_Holster(item);
            else manager.Item_Equip(item);
        }

        /// <summary>Stores the selected equipped Item (with the Unequip animation unless Fast)</summary>
        private void Holster(SuperItemManager manager)
        {
            var item = SelectBy == SelectByOption.HolsterID ? FindEquippedForHolster(manager) : FindOwned(manager, preferHolstered: false);
            if (item == null || !item.IsEquipped) return;

            if (Fast) manager.Item_Holster(item);
            else manager.Item_Unequip(item);
        }

        /// <summary>Releases the selected Item to the world</summary>
        private void Drop(SuperItemManager manager)
        {
            if (SelectBy == SelectByOption.HolsterID)
            {
                if (holster != null) manager.Item_Drop(holster);
                return;
            }

            var item = FindOwned(manager, preferHolstered: false);
            if (item != null) manager.Item_Drop(item);
        }

        /// <summary>First owned Item matching the ID list. Empty list = first holstered (Equip) or first equipped (Holster, Drop) Item</summary>
        private SuperItem FindOwned(SuperItemManager manager, bool preferHolstered)
        {
            if (items != null && items.HasItems)
            {
                foreach (var id in items.items)
                {
                    if (id == null) continue;

                    var item = preferHolstered
                        ? (manager.Get_ItemHolstered(id) ?? manager.Get_ItemEquipped(id))
                        : (manager.Get_ItemEquipped(id) ?? manager.Get_ItemHolstered(id));

                    if (item != null) return item;
                }
                return null;
            }

            if (preferHolstered)
            {
                foreach (var h in manager.holsters)
                    if (h != null && h.HasItem) return h.Item;
            }
            else
            {
                foreach (var p in manager.EquipPoints)
                    if (p != null && p.HasItem) return p.Item;
            }
            return null;
        }

        /// <summary>Equipped Item that belongs to the selected Holster</summary>
        private SuperItem FindEquippedForHolster(SuperItemManager manager)
        {
            if (holster == null) return null;

            foreach (var p in manager.EquipPoints)
                if (p != null && p.HasItem && (p.Item.HolsterID == holster || p.Item.SecondaryHolster == holster)) return p.Item;

            return null;
        }

        private void Reset() => Description = "Equip, Holster or Drop a Super Item owned by the AI using the Super Item Manager";
    }
}
