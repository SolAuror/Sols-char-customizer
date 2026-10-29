// Last Change: 9/17/2026 5:33pm
namespace MalbersAnimations.Items
{
    [System.Serializable]
    public abstract class ItemProcessorSuperItem : ItemProcessor
    {

    }

    [System.Serializable, AddTypeMenu("Item/Holster Items")]
    [UnityEngine.HelpURL(ItemDocs.Processors + "item-holster-items")]
    public class ItemProcessorHolster : ItemProcessorSuperItem
    {
        public enum EquipUnequipType
        {
            AllButSelf,
            All,
            Include,
            Exclude
        }

        public EquipUnequipType set = EquipUnequipType.AllButSelf;

        [Hide("set", false, 2, 3)]
        public IDList<SuperItemID> items;

        public override string DynamicName => $"Holster Items";

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            switch (this.set)
            {
                case EquipUnequipType.AllButSelf:
                    item.User.Item_HolsterAllBut(item);
                    break;
                case EquipUnequipType.All:
                    item.User.Item_HolsterAll();
                    break;
                case EquipUnequipType.Include:
                    item.User.Item_HolsterAll(items, true);
                    break;
                case EquipUnequipType.Exclude:
                    item.User.Item_HolsterAll(items, false);
                    break;
                default:
                    break;
            }
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            return this.set switch
            {
                EquipUnequipType.Include when items.Count == 0 => "Include list is empty.",
                EquipUnequipType.Exclude when items.Count == 0 => "Exclude list is empty.",
                _ => string.Empty,
            };
        }
    }
}