// Last Change: 9/20/2026 6:20pm
using MalbersAnimations.Reactions; //MWC — Reaction2 for the Holster OnHolster/OnUnholster reactions
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MalbersAnimations.Items
{
    [System.Serializable]
    public class SItemHolster
    {
        [Tooltip("Holster Identifier")]
        public HolsterID ID;

        [Tooltip("Current Item stored in this holster")]
        public SuperItem Item; //MWC: Head = most recently stored item. Kept as a serialized field (editors + authored starting items rely on it)

        //MWC: Dual Wielding - all the items currently stored in this holster (the head is also included). Runtime only.
        [System.NonSerialized] internal List<SuperItem> StoredItems = new();

        //MWC: Slot index occupied by each stored item. Runtime only.
        [System.NonSerialized] private Dictionary<SuperItem, int> usedSlots = new();

        //MWC: Swap - Item that is being drawn OUT of this holster. It is still parented to its Slot while the Equip
        //animation plays, but its Slot already counts as FREE, so the item being holstered in exchange (which is stored
        //immediately, before the draw finishes) can take that very slot instead of being bumped to another one.
        //Mirrors SEquipPoint.Reserved. Cleared when the item actually leaves (Clear) or comes back (Holster).
        [System.NonSerialized] internal SuperItem Leaving;

        [Tooltip("Input to Equip the weapon in the holster")]
        public StringReference Input;

        [Tooltip("If the weapon is added to the holster then it will be equipped on the Hand automatically")]
        public BoolReference m_autoEquip;

        //MWC: Capacity - hard limit of Items this Holster can store. It is independent from the Slots count:
        //a Holster with 3 Slots and Capacity 2 stores only 2 Items.
        [Tooltip("Maximum amount of Items this Holster can store, regardless of how many Transform Slots it has.\n" +
            "Storing a new Item on a full Holster drops the OLDEST stored one.\n" +
            "Zero (or less) = No Limit: only the Slots limit the amount of Items")]
        public IntReference m_Capacity = new(DefaultCapacity);

        [Tooltip("Holster Transform Slot Index for the Weapon")]
        public List<TransformReference> Slots;

        //MWC: Per-character Item Offsets table (keyed by Item ID + Holster ID). Injected by the SuperItemManager on Awake.
        //It is the only source of holster Offset/Slot data; no asset or no entry → identity offset and slot 0.
        [System.NonSerialized] internal SuperItemOffsets Offsets;

        /// <summary> Used to connect the Inputs to the Abilities instead of the General Mode</summary>
        public UnityAction<bool> InputListener;

        public SuperItemEvent OnItemHolsterEnter;
        public SuperItemEvent OnItemHolsterExit;

        //MWC — STATE reactions. They report what the Holster HOLDS once a move is done, while OnHolster/OnUnholster
        //below report a single Item moving. A Holster can store several Items, so an Item leaving does NOT mean the
        //Holster is empty: use these to drive UI (icon of the Item on top / blank when there is nothing left).
        [Tooltip("Reactions when the Item this Holster shows changes: stored, or promoted after another one left. " +
            "Dynamic Target [Current Item]")]
        public Reaction2 OnHolsterItemChanged;
        [Tooltip("Reactions when the LAST Item leaves and this Holster is empty. Dynamic Target [Super Item Manager]")]
        public Reaction2 OnHolsterEmpty;

        //MWC — Reactions fired when an Item is stored in / removed from this Holster. Same idea as the Equip Point
        //OnEquip/OnUnequip reactions: the dynamic target is the ITEM, so every reaction inside resolves against it.
        [Tooltip("Reactions when an Item is stored in this Holster. Dynamic Target [Holstered Item]")]
        public Reaction2 OnHolster;
        [Tooltip("Reactions when an Item is removed from this Holster. Dynamic Target [Unholstered Item]")]
        public Reaction2 OnUnholster;

        //MWC: Owner of this Holster. Injected by the SuperItemManager on Awake — it is the Dynamic Target of
        //OnHolsterEmpty, which has no Item to point at.
        [System.NonSerialized] internal Component Manager;

        //MWC — Head the state reactions last reported, so they only fire on a REAL change (an Item re-stored into the
        //same Holster, or a Holster call that changes nothing, does not re-fire them)
        [System.NonSerialized] private SuperItem notifiedItem;

        /// <summary>Reports what this Holster holds now (or that it is empty), when it actually changed</summary>
        private void NotifyHolsterState()
        {
            if (notifiedItem == Item) return; //Nothing changed

            notifiedItem = Item;

            if (Item != null) OnHolsterItemChanged.TryReact(Item);
            else OnHolsterEmpty.TryReact(Manager);
        }

        /// <summary>Notifies (Event + Reactions) that an Item was stored in this Holster</summary>
        private void Item_Entered(SuperItem item)
        {
            OnItemHolsterEnter?.Invoke(item);
            OnHolster.TryReact(item); //MWC — Dynamic Target: the Item that was just holstered
        }

        /// <summary>Notifies (Event + Reactions) that an Item was removed from this Holster</summary>
        private void Item_Exited(SuperItem item)
        {
            OnItemHolsterExit?.Invoke(item);
            OnUnholster.TryReact(item); //MWC — Dynamic Target: the Item that just left the Holster
        }

        public bool IsValid => ID != null && ID.ID > 0;

        /// <summary>Returns true if there is an item in the holster</summary>
        public bool HasItem => Item != null;

        public int GetID => ID != null ? ID.ID : 0;

        public bool AutoEquip { get => m_autoEquip.Value; set => m_autoEquip.Value = value; }

        /// <summary>Default amount of Items a Holster can store</summary>
        public const int DefaultCapacity = 2;

        /// <summary>Max amount of Items this Holster can store, regardless of its Slots (zero or less = No Limit)</summary>
        public int Capacity { get => m_Capacity.Value; set => m_Capacity.Value = value; }

        /// <summary>Amount of Items currently stored in this Holster</summary>
        public int Count => StoredItems.Count;

        /// <summary>True when the Holster cannot store one more Item without dropping the oldest one</summary>
        public bool IsFull() => IsFull(null);

        /// <summary>True when the Holster cannot store [item] without dropping the oldest one</summary>
        public bool IsFull(SuperItem item) => Capacity > 0 && StoredCount(item) >= Capacity;

        //MWC: Capacity - Items taking a spot in this Holster. The Item being (re)stored does not take a new one, and
        //neither does the Item being drawn out (Leaving): its spot is already promised to whatever comes in exchange.
        private int StoredCount(SuperItem ignore)
        {
            var count = 0;

            foreach (var stored in StoredItems)
                if (stored != null && stored != ignore && stored != Leaving) count++;

            return count;
        }


        public Transform GetSlot(int index) => Slots[index].Value;

        //MWC — Preferred Holster Slot index for an item, resolved from the SuperItemOffsets asset (no asset/entry → 0)
        public int GetDefaultSlot(SuperItem item) => Offsets != null ? Offsets.GetHolsterSlot(item.ItemType, ID) : 0;

        //MWC — Dual Wielding: authored Slot priority list for an item (no asset/entry → just slot 0).
        //The item takes the first slot of this list that is still free.
        public List<int> GetSlotPriority(SuperItem item) =>
            Offsets != null ? Offsets.GetHolsterSlots(item.ItemType, ID) : DefaultSlotPriority;

        private static readonly List<int> DefaultSlotPriority = new() { 0 };

        //MWC — Holster Offset for an item, resolved from the SuperItemOffsets asset (no asset/entry → identity)
        public TransformOffset GetOffset(SuperItem item) =>
            Offsets != null ? Offsets.GetHolsterOffset(item.ItemType, ID) : new TransformOffset(0);

        //MWC — Slot index the item currently occupies in this holster (its resolved default when not stored yet)
        public int GetUsedSlot(SuperItem item) => usedSlots.TryGetValue(item, out var slot) ? slot : GetDefaultSlot(item);

        public static implicit operator int(SItemHolster reference) => reference.GetID;

        //MWC: True if the item is stored in this holster (head or any slot)
        public bool Contains(SuperItem item) => item != null && (Item == item || StoredItems.Contains(item));

        /// <summary>First Item stored here with this Super Item ID, head first and then every slot (null = none)</summary>
        //MWC — the head is checked apart from StoredItems on purpose: an AUTHORED starting head is not listed there
        //until it is really stored, and a head that IS stored would otherwise be found twice.
        public SuperItem Get_Item(SuperItemID ID)
        {
            if (ID == null) return null;

            if (Item != null && Item.ID == ID) return Item;

            for (int i = 0; i < StoredItems.Count; i++)
            {
                var stored = StoredItems[i];
                if (stored != null && stored != Item && stored.ID == ID) return stored;
            }
            return null;
        }

        /// <summary> Clear the current (head) item from the holster  </summary>
        public bool Clear() => Clear(Item); //MWC: routed through Clear(item)

        //MWC: Dual Wielding - remove a specific item from the holster and promote the last stored one as the new head
        public bool Clear(SuperItem item)
        {
            if (item == null || !Contains(item)) return false;

            item.transform.SetParent(null);
            item.IsInHolster = false;

            StoredItems.Remove(item);
            usedSlots.Remove(item);

            if (Leaving == item) Leaving = null; //MWC: Swap - the reserved exit is fulfilled, the item has actually left

            Item = StoredItems.Count > 0 ? StoredItems[^1] : null; //Promote the most recently stored item as the new head

            Item_Exited(item);    //MWC — Event + Reactions, fired last so listeners see the completed state (new head included)
            NotifyHolsterState(); //MWC — the Item on top changed (or the Holster is empty now)
            return true;
        }


        public bool Holster(SuperItem item) => Holster(item, (List<SuperItem>)null);

        //MWC — 'evicted' returns the FIRST Item pushed out. Prefer the List overload: reaching the Capacity limit
        //(e.g. when it is lowered at runtime by an Int Var) can push out more than one Item at once.
        public bool Holster(SuperItem item, out SuperItem evicted)
        {
            EvictedBuffer.Clear();
            var holstered = Holster(item, EvictedBuffer);
            evicted = EvictedBuffer.Count > 0 ? EvictedBuffer[0] : null;
            return holstered;
        }

        //MWC — 'evicted' collects every Item pushed out (all Slots taken, or the Capacity limit was reached) so the
        //caller (SuperItemManager) can drop them properly. They used to be silently orphaned: still parented to the
        //slot with physics never restored.
        public bool Holster(SuperItem item, List<SuperItem> evicted)
        {
            if (item != null && item.IsAllowedHolster(ID)) //MWC: Dual Wielding - primary or secondary holster
            {
                Item = item;
                return Holster(evicted);
            }
            return false;
        }

        public bool Holster() => Holster((List<SuperItem>)null);

        public bool Holster(out SuperItem evicted) //MWC — see the overload above
        {
            EvictedBuffer.Clear();
            var holstered = Holster(EvictedBuffer);
            evicted = EvictedBuffer.Count > 0 ? EvictedBuffer[0] : null;
            return holstered;
        }

        //MWC — scratch list backing the single 'evicted' overloads
        private static readonly List<SuperItem> EvictedBuffer = new();

        public bool Holster(List<SuperItem> evicted) //MWC — see the overload above
        {
            if (Item != null)
            {
                //MWC — Holster Slots now come from the SuperItemOffsets asset as a priority list (no entry → [0]).
                //At least ONE authored slot must exist in the Slots array, otherwise there's nowhere to parent the item.
                var priority = GetSlotPriority(Item);

                if (!HasValidSlot(priority))
                {
                    Debug.LogError($"[SItemHolster] The Item {Item.name} has no valid Holster Slot: [{string.Join(", ", priority)}]. " +
                        $"This Holster has {Slots.Count} Slot(s). Please check the Holster Slots in the Super Item Offsets asset");

                    Item = StoredItems.Count > 0 ? StoredItems[^1] : null; //MWC: restore the previous head instead of leaving the holster empty
                    return false;
                }

                //MWC: Capacity - the Holster keeps at most [Capacity] Items, no matter how many Slots it has, so
                //make room BEFORE resolving the slot: the spot freed here is the one the incoming Item takes.
                EvictForCapacity(Item, evicted);

                //MWC: Dual Wielding - resolve the slot: the item's own HolsterSlot, or the next free one when another stored item occupies it
                var slotIndex = ResolveSlot(Item, out var replacedItem);

                var slot = Slots[slotIndex]; //Get the correct slot

                //MWC — Prefab references are instantiated by the SuperItemManager (Item_Instance) BEFORE they get here:
                //instantiating at this point swapped the head behind the caller's back (it kept holding the Prefab, so
                //its events/flags were applied to the Asset) and destroyed whatever child sat in the slot.
                Item.transform.SetParent(slot);
                Item.transform.SetLocalTransform(GetOffset(Item)); //MWC — Holster Offset from the SuperItemOffsets asset (no entry → identity)

                Item.Physics_Disable(Item.DisableCollidersOnHolster.Value); //MWC — colliders per [Disable Colliders On Holster]

                Item.IsInHolster = true;
                Item.IsEquipped = false;
                Item.Active = false;

                //MWC: Dual Wielding - track the stored items and their slots (head = most recent)
                if (replacedItem != null) //All slots were taken → the previous occupant is no longer stored here
                {
                    StoredItems.Remove(replacedItem);
                    usedSlots.Remove(replacedItem);
                    replacedItem.IsInHolster = false;
                    Item_Exited(replacedItem);  //MWC — Event + Reactions: listeners learn the item left the holster
                    evicted?.Add(replacedItem); //MWC — hand it to the caller so it gets dropped (unparented + physics restored)
                }

                StoredItems.Remove(Item); //Avoid duplicates when re-holstering the same item
                StoredItems.Add(Item);
                usedSlots[Item] = slotIndex;

                if (Leaving == Item) Leaving = null; //MWC: Swap - it never left (the draw was cancelled), so it holds its slot again

                Item_Entered(Item);   //MWC — Event + Reactions, fired last so listeners see the completed state (parented, slot tracked)
                NotifyHolsterState(); //MWC — the Item on top changed

                return true;
            }
            return false;
        }

        //MWC: Dual Wielding - find the slot for the item. The authored priority list wins: the FIRST slot in it that is
        //in range and free is taken (e.g. [0, 2] → first dagger to slot 0, second one to slot 2). Only when every
        //authored slot is taken does it fall back to scanning the rest of the holster from the preferred slot.
        //When ALL slots are taken it evicts the OLDEST stored item ('replaced') and returns that item's slot, so the caller can drop it.
        private int ResolveSlot(SuperItem item, out SuperItem replaced)
        {
            replaced = null;

            var priority = GetSlotPriority(item); //MWC — authored priority list from the SuperItemOffsets asset

            foreach (var authored in priority)
            {
                if (ValidSlot(authored) && SlotOccupant(authored, item) == null) return authored;
            }

            var start = FirstValidSlot(priority); //MWC — head of the priority list, for the wrap-around scan below

            for (int i = 0; i < Slots.Count; i++)
            {
                var index = (start + i) % Slots.Count;
                if (SlotOccupant(index, item) == null) return index;
            }

            //MWC — all slots taken → evict the OLDEST stored item (StoredItems is ordered oldest→newest) and reuse ITS slot.
            //Previously it replaced whoever happened to sit in the item's preferred slot.
            foreach (var stored in StoredItems)
            {
                //MWC: Swap - never evict the item that is already leaving; its slot is handed over, not stolen
                if (stored != null && stored != item && stored != Leaving) { replaced = stored; break; }
            }

            replaced ??= SlotOccupant(start, item); //Fallback when StoredItems has nothing usable

            //MWC — reuse the evicted item's own slot so the incoming item takes its place
            var slot = (replaced != null && usedSlots.TryGetValue(replaced, out var replacedSlot)) ? replacedSlot : start;

            Debug.LogWarning($"[SItemHolster {ID.name}] All [{Slots.Count}] Slots are taken. Dropping [{replaced?.name}] to make room for [{item.name}]. " +
                $"Add more Slot Transforms to the Holster to store more items");

            return slot;
        }

        //MWC: Capacity - drop the Items that do not fit anymore: the OLDEST stored ones (StoredItems is ordered
        //oldest→newest) are removed until there is room for 'incoming'. The Item coming in and the one already being
        //drawn out (Leaving) are never pushed out. They are collected in 'evicted' so the caller drops them properly.
        private void EvictForCapacity(SuperItem incoming, List<SuperItem> evicted)
        {
            while (IsFull(incoming))
            {
                SuperItem oldest = null;

                foreach (var stored in StoredItems)
                    if (stored != null && stored != incoming && stored != Leaving) { oldest = stored; break; }

                if (oldest == null) return; //There is nothing that can be pushed out

                StoredItems.Remove(oldest);
                usedSlots.Remove(oldest);

                oldest.IsInHolster = false;
                Item_Exited(oldest); //MWC — Event + Reactions

                evicted?.Add(oldest);
            }
        }

        //MWC: Dual Wielding - True when one of the item's authored Slots in this Holster is still free.
        //Drives the fall-through to the item's Secondary (Dual Wielding) Holster: a full primary sends the
        //second copy to the other holster, where it gets its own Offset/Slots/Anim entry.
        public bool HasRoomFor(SuperItem item)
        {
            if (item == null) return false;

            if (IsFull(item)) return false; //MWC: Capacity - the Item limit is reached, even when a Slot is still free

            foreach (var slot in GetSlotPriority(item))
            {
                if (ValidSlot(slot) && SlotOccupant(slot, item) == null) return true;
            }
            return false;
        }

        //MWC — A slot index is usable only when the Holster actually has that Transform Slot authored
        private bool ValidSlot(int slot) => slot >= 0 && Slots != null && slot < Slots.Count;

        //MWC — True when at least one slot of the priority list exists in this Holster
        private bool HasValidSlot(List<int> priority)
        {
            foreach (var slot in priority) if (ValidSlot(slot)) return true;
            return false;
        }

        //MWC — Head of the priority list, skipping indexes this Holster doesn't have. Zero when none is valid.
        private int FirstValidSlot(List<int> priority)
        {
            foreach (var slot in priority) if (ValidSlot(slot)) return slot;
            return 0;
        }

        //MWC: Returns the stored item occupying the given slot (ignoring the given item itself). Null when the slot is free.
        //MWC: Swap - the item being drawn out (Leaving) does not count as an occupant: its slot is already promised
        //to whatever is holstered in exchange, even though it is still parented there until the Equip animation ends.
        private SuperItem SlotOccupant(int index, SuperItem ignore)
        {
            foreach (var pair in usedSlots)
                if (pair.Value == index && pair.Key != ignore && pair.Key != null && pair.Key != Leaving) return pair.Key;
            return null;
        }

        /// <summary> Validates if the item can be equipped in this equip point  </summary>
        public bool ValidItem(SuperItem item) => item != null && item.IsAllowedHolster(ID); //MWC: Dual Wielding - primary or secondary holster

        /// <summary> Validates if the current item is equipped in this equip point  </summary>
        public bool ValidItem() => ValidItem(Item);
    }
}
