// Last Change: 9/30/2026 10:16am
using MalbersAnimations.Reactions; //MWC — Reaction2 for the Equip Point OnEquip/OnUnequip reactions
using MalbersAnimations.Scriptables;
using System.Collections; //IEnumerator (pending draw coroutine)
using UnityEngine;

namespace MalbersAnimations.Items
{
    /// <summary> Where an Item goes when it is stored (unequipped) from an Equip Point </summary>
    public enum SIStoreMode
    {
        /// <summary> Stored in its Holster. An Item with no Holster is dropped instead </summary>
        Holster = 0,
        /// <summary> Dropped to the world (like the Drop input) </summary>
        Drop = 1,
        /// <summary> Deactivated in place and kept by the Manager. Draw it back with Item Equip </summary>
        Hide = 2,
    }

    [System.Serializable]
    public class SEquipPoint : ISerializationCallbackReceiver //MWC — receiver only for the legacy StoreOnHolster migration
    {
        [Tooltip("Equip Point Identifier")]
        public EquipPointID ID;

        [Tooltip("Current Item Equipped in the equip point")]
        public SuperItem Item;

        [Tooltip("Transform reference for the equip point. E.g. Left Hand, Right Hand")]
        public TransformReference Point;

        [Tooltip("Where the Item goes when it is stored (unequipped) from this Equip Point:\n" +
            "• Holster: into its Holster (no Holster → dropped)\n" +
            "• Drop: dropped to the world\n" +
            "• Hide: deactivated in place and kept by the Manager (draw it back with Item Equip)")]
        public SIStoreMode OnStore = SIStoreMode.Holster;

        //MWC — Legacy bool, replaced by OnStore. Kept (hidden) so existing prefabs keep their authored value: it is
        //folded into OnStore on deserialization (ON → Holster, OFF → Drop). OFF used to leave the Item unequipped but
        //still parented to the hand, owned and not holstered, with no way back. Do not read it at runtime.
        [SerializeField, HideInInspector] private BoolReference StoreOnHolster;

        //MWC — FALSE (no initializer on purpose) = OnStore has not been migrated from StoreOnHolster yet. Unity keeps
        //the initializer for keys missing from existing data, so a TRUE default would skip the migration.
        [SerializeField, HideInInspector] private bool m_StoreMigrated;

        //MWC — Reactions fired by the SuperItemManager when an item lands on / leaves this Equip Point.
        //The dynamic target is the item itself, so any reaction (e.g. [Member Get]) resolves against the item.
        [Tooltip("Reactions when an Item is equipped on this Equip Point. Dynamic Target [Equipped Item]")]
        public Reaction2 OnEquip;
        [Tooltip("Reactions when an Item is unequipped from this Equip Point. Dynamic Target [Equipped Item]")]
        public Reaction2 OnUnequip;

        //MWC: Per-character Equip Point offsets table (keyed by Item ID + Equip Point ID). Injected by the SuperItemManager on Awake.
        //It is the ONLY source of equip offsets now; no entry → identity offset (Pos 0, Rot 0, Scale 1).
        [System.NonSerialized] internal SuperItemOffsets Offsets;

        //MWC — Item scheduled to land on this point while its Equip animation is still playing.
        //Without it, a second equip in the same frame (e.g. drawing two dual-wield items at once) resolves
        //to this same point because Item is still null until the delayed Equip_INTERNAL_FAST runs.
        [System.NonSerialized] internal SuperItem Reserved;

        //MWC — the delayed end of that draw (Equip_INTERNAL_FAST). Kept so the Manager can STOP it when the draw is
        //cancelled (item dropped, held by a No Items block, Manager disabled) instead of letting it land later on
        //an item that is no longer meant to be drawn. Unity only kills coroutines on GameObject deactivate/destroy.
        [System.NonSerialized] internal IEnumerator C_Draw;

        /// <summary> [Auto Store] Time.time of the last activity on this point's item (equip, playing Action, Mode) </summary>
        [System.NonSerialized] internal float LastActiveTime;

        //MWC — who owns this point right now: the equipped item, or the one still playing its Equip animation
        internal SuperItem Occupant => Item != null ? Item : Reserved;

        public bool IsValid => ID != null && ID.ID > 0;

        public bool HasItem => Item != null;

        public int GetID => ID != null ? ID.ID : 0;

        //MWC: Side of this Equip Point taken from its ID asset (Dual Wielding). None when the ID has no side authored.
        public WeaponSide Side => ID != null ? ID.Side : WeaponSide.None;

        internal void Clear_Item() => Item = null;

        /// <summary> Validates if the item can be equipped in this equip point (primary or any secondary point) </summary>
        public bool ValidItem(SuperItem item) => item != null &&
            (item.IsAllowedEquipPoint(ID) || (Offsets != null && Offsets.IsEquivalent(item, ID))); //MWC — Equip Point Equivalents (e.g. Wolf: Hands → Head)

        /// <summary> Validates if the current item is equipped in this equip point  </summary>
        public bool ValidItem() => ValidItem(Item);

        internal void Set_Item(SuperItem item)
        {
            Item = item;
            Item.CurrentEquipPoint = this;
            if (Reserved == item) Reserved = null; //MWC — the reservation is fulfilled: the item has actually landed
            LastActiveTime = Time.time; //MWC — [Auto Store] a fresh equip restarts the idle timer
            ParentItem();
        }

        /// <summary>  Parents the item to the equip point      </summary>
        public void ParentItem()
        {
            if (Item != null && Point.Value != null)
            {
                Item.transform.SetParent(Point.Value, false);

                //MWC: Offsets are handled entirely by the Character Item Offsets SO (keyed by Item ID + Equip Point ID).
                //No asset or no entry → identity offset: Pos(0,0,0) Rot(0,0,0) Scale(1,1,1)
                if (Offsets == null || !Offsets.TryGetOffset(Item.ItemType, ID, out TransformOffset offset))
                    offset = new TransformOffset(0);

                Item.transform.SetLocalPositionAndRotation(offset.Position, Quaternion.Euler(offset.Rotation));
                Item.transform.localScale = offset.Scale;
            }
        }

        /// <summary> Marks OnStore as authored, so an Equip Point created from code never re-migrates from the empty legacy field </summary>
        public void OnBeforeSerialize() => m_StoreMigrated = true;

        /// <summary> Migrates the legacy StoreOnHolster bool into OnStore, once </summary>
        public void OnAfterDeserialize()
        {
            if (m_StoreMigrated) return;

            //Only a Constant can be read here. A Variable/Member reference, or a zero-initialized new list element
            //(Use Constant off), falls back to Holster, which is what the field meant in almost every setup
            OnStore = StoreOnHolster != null && StoreOnHolster.UseConstant && !StoreOnHolster.UseMember && !StoreOnHolster.ConstantValue
                ? SIStoreMode.Drop
                : SIStoreMode.Holster;

            m_StoreMigrated = true;
        }

        //public readonly SuperItem GetItem() => Item;

        public static implicit operator int(SEquipPoint reference) => reference.GetID;
    }
}
