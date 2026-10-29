// Last Change: 9/30/2026 11:37am
using MalbersAnimations.Conditions;
using MalbersAnimations.Scriptables;
using System.Text;
using UnityEngine;

namespace MalbersAnimations.Items
{
    [System.Serializable]
    public abstract class C2_SuperItemManager : ConditionCore
    {
        [Hide(nameof(LocalTarget))] public SuperItemManager Target;
        protected override void _SetTarget(Object target) => VerifyComponent(target, ref Target);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;

        public enum ComparisonID { HasAny, HasAll }

        //MWC: Shared name builder so every Super Item condition formats its ID list the same way (and never throws on empty).
        protected static string ItemListName(IDList<SuperItemID> list)
        {
            if (list == null || !list.HasItems) return "Any";

            var sb = new StringBuilder();
            foreach (var item in list.items)
            {
                if (item == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(item.name);
            }
            return sb.Length > 0 ? sb.ToString() : "Any";
        }

        /// <summary> Formats a Malbers Tag list for the condition name, never throws on empty </summary>
        protected static string TagListName(MTags list)
        {
            if (list == null || !list.ValidObjects) return "None";

            var sb = new StringBuilder();
            foreach (var tag in list.tags)
            {
                if (tag == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(tag.name);
            }
            return sb.Length > 0 ? sb.ToString() : "None";
        }
    }

    [System.Serializable, AddTypeMenu("Super Item/[Manager] Has Item Equipped")]
    public class C2_SI_EquipUnequip : C2_SuperItemManager
    {
        //MWC: Use the shared, empty-safe formatter instead of trimming a trailing comma (which threw on an empty list).
        public override string DynamicName => $"[SI Manager] Equipped [{ItemListName(items)}] ({checkIf})";

        public IDList<SuperItemID> items;
        public ComparisonID checkIf = ComparisonID.HasAny;

        protected override bool _Evaluate()
        {
            if (Target == null || items == null || !items.HasItems) return false;

            //MWC: Fixed evaluation logic. The old version returned true on the first equipped item even in
            //HasAll mode, and fell through to "return true" when HasAny matched nothing.
            foreach (var item in items.items)
            {
                if (item == null) continue;

                bool equipped = Target.IsItemEquipped(item);

                if (checkIf == ComparisonID.HasAll && !equipped) return false; // one missing → fails HasAll
                if (checkIf == ComparisonID.HasAny && equipped) return true;   // one present → passes HasAny
            }

            // HasAll: reached here means none were missing → true. HasAny: none matched → false.
            return checkIf == ComparisonID.HasAll;
        }
    }

    /// <summary> True when an equipped Item carries the given Malbers Tags (on the Item own Tags component) </summary>
    [System.Serializable, AddTypeMenu("Super Item/[Manager] Has Item Equipped By Tag")]
    public class C2_SI_ItemEquippedByTag : C2_SuperItemManager
    {
        public override string DynamicName => $"[SI Manager] Item Equipped By Tag [{TagListName(tags)}] ({checkIf})";

        [Tooltip("Malbers Tags to look for on the equipped Items (their own Tags component).\nHas Any = one of the Tags on any equipped Item. Has All = every Tag on the same equipped Item")]
        public MTags tags = new();
        public ComparisonID checkIf = ComparisonID.HasAny;

        protected override bool _Evaluate() =>
            Target != null && tags != null && tags.ValidObjects && Target.IsItemEquippedWithTag(tags.tags, checkIf == ComparisonID.HasAll);
    }

    //MWC: New condition — checks whether items are stored in the holsters (as opposed to equipped on the hands).
    [System.Serializable, AddTypeMenu("Super Item/[Manager] Has Super Item in Holster")]
    public class C2_SI_Holstered : C2_SuperItemManager
    {
        public override string DynamicName => $"[SI Manager] Has Holstered [{ItemListName(items)}] ({checkIf})";

        [Tooltip("Items to look for in the holsters. Leave empty to check if ANY holster holds ANY item.")]
        public IDList<SuperItemID> items;
        public ComparisonID checkIf = ComparisonID.HasAny;

        protected override bool _Evaluate()
        {
            if (Target == null) return false;

            //No specific IDs requested → just check that something is holstered.
            if (items == null || !items.HasItems) return Target.IsAnyItemHolstered();

            foreach (var item in items.items)
            {
                if (item == null) continue;

                bool holstered = Target.IsItemHolstered(item);

                if (checkIf == ComparisonID.HasAll && !holstered) return false;
                if (checkIf == ComparisonID.HasAny && holstered) return true;
            }

            return checkIf == ComparisonID.HasAll;
        }
    }

    //MWC: New condition — checks whether items are equipped OR holstered (owned anywhere by the manager).
    [System.Serializable, AddTypeMenu("Super Item/[Manager] Owns Super Item")]
    public class C2_SI_OwnsItem : C2_SuperItemManager
    {
        public override string DynamicName => $"[SI Manager] Owns [{ItemListName(items)}] ({checkIf})";

        [Tooltip("Items to look for. Owned means equipped on a hand OR stored in a holster.")]
        public IDList<SuperItemID> items;
        public ComparisonID checkIf = ComparisonID.HasAny;

        protected override bool _Evaluate()
        {
            if (Target == null || items == null || !items.HasItems) return false;

            foreach (var item in items.items)
            {
                if (item == null) continue;

                bool owned = Target.OwnsItem(item);

                if (checkIf == ComparisonID.HasAll && !owned) return false;
                if (checkIf == ComparisonID.HasAny && owned) return true;
            }

            return checkIf == ComparisonID.HasAll;
        }
    }

    //MWC: New condition — checks a specific holster slot (by Holster ID) currently holds an item.
    [System.Serializable, AddTypeMenu("Super Item/[Manager] Holster Occupied")]
    public class C2_SI_HolsterOccupied : C2_SuperItemManager
    {
        public override string DynamicName => $"[SI Manager] Holster Occupied [{(holster == null ? "<Select>" : holster.name)}]";

        [Tooltip("Holster slot to check (e.g. Back, Left Hip). True when that slot holds an item.")]
        public HolsterID holster;

        protected override bool _Evaluate() => Target != null && Target.IsHolsterOccupied(holster);
    }

    //MWC: New condition — compares how many items are equipped / holstered / total against a value.
    [System.Serializable, AddTypeMenu("Super Item/[Manager] Item Equipped or Holstered")]
    public class C2_SI_ItemCount : C2_SuperItemManager
    {
        public enum CountType { Equipped, Holstered, Total }

        public override string DynamicName => $"[SI Manager] {countType} Count [{compare}] {value.Value}";

        [Tooltip("Which items to count: equipped on hands, stored in holsters, or both.")]
        public CountType countType = CountType.Equipped;
        public ComparerNumber compare = ComparerNumber.GreaterEqual;
        public IntReference value = new(1);

        protected override bool _Evaluate()
        {
            if (Target == null) return false;

            int count = countType switch
            {
                CountType.Equipped => Target.EquippedCount,
                CountType.Holstered => Target.HolsteredCount,
                _ => Target.EquippedCount + Target.HolsteredCount,
            };

            return count.MCompare(value, compare);
        }
    }

    //MWC: New condition — checks there's at least one free equip point or holster slot.
    [System.Serializable, AddTypeMenu("Super Item/[Manager] Has Free Slot")]
    public class C2_SI_HasFreeSlot : C2_SuperItemManager
    {
        public enum SlotType { EquipPoint, Holster }

        public override string DynamicName => $"[SI Manager] Has Free {slotType}";

        [Tooltip("Which slot type to check for a free (empty) slot.")]
        public SlotType slotType = SlotType.EquipPoint;

        protected override bool _Evaluate()
        {
            if (Target == null) return false;
            return slotType == SlotType.EquipPoint ? Target.HasFreeEquipPoint() : Target.HasFreeHolster();
        }
    }

    /// <summary>True while the Super Item Manager is Locked (its Items put away, every Item operation refused)</summary>
    [System.Serializable, AddTypeMenu("Super Item/[Manager] Is Locked")]
    public class C2_SI_IsLocked : C2_SuperItemManager
    {
        /// <summary>Name shown on the Condition row</summary>
        public override string DynamicName => "[SI Manager] Is Locked";

        /// <summary>Reads <see cref="SuperItemManager.Locked"/></summary>
        protected override bool _Evaluate() => Target != null && Target.Locked;
    }

    //MWC: New condition — true while any equipped item has an action currently playing (firing, blocking…).
    [System.Serializable, AddTypeMenu("Super Item/[Manager] Item In Use [Any Action]")]
    public class C2_SI_ItemInUse : C2_SuperItemManager
    {
        public override string DynamicName => "[SI Manager] Item In Use";
        protected override bool _Evaluate() => Target != null && Target.IsAnyActionPlaying();
    }

    //MWC: New condition — true while a given Item is playing a given Action. Both fields are optional:
    //no Item = ANY equipped item (Equip Points + the Default Item), no Action = ANY playing Action on it.
    [System.Serializable, AddTypeMenu("Super Item/[Manager] Action Is Playing")]
    public class C2_SI_ActionPlaying : C2_SuperItemManager, ISerializationCallbackReceiver
    {
        /// <summary>Name shown on the Condition row</summary>
        public override string DynamicName =>
            $"[SI Manager] Is Playing Action {(actions == null || actions.Empty ? "[Any]" : $"{(actions.include ? "" : "NOT ")}[{ActionListName(actions)}]")} on [{(item == null ? "Any Equipped" : item.name)}]";

        [Tooltip("Item that must be playing the Action. Empty = check ANY equipped item.\n" +
            "The Item has to be EQUIPPED: a holstered one never plays its Actions")]
        public SuperItemID item;

        [Tooltip("Actions to look for on the item's Active Action Set (Fire, Aim, Block…).\n" +
            "Include: true when one of these Actions is playing.\n" +
            "Exclude: true when an Action that is NOT in the list is playing.\n" +
            "Empty = ANY playing Action")]
        public IDListCheck<ItemActionID> actions = new();

        //Legacy single Action, replaced by the [actions] list. Kept hidden so existing assets migrate their authored value
        [SerializeField, HideInInspector] private ItemActionID action;

        /// <summary>Checks the Manager for a playing Action that passes the Include/Exclude list</summary>
        protected override bool _Evaluate() => Target != null && Target.IsActionPlaying(item, actions);

        /// <summary>Nothing to write: the legacy field is cleared on load</summary>
        public void OnBeforeSerialize() { }

        /// <summary>Moves the legacy single Action into the list, once</summary>
        public void OnAfterDeserialize()
        {
            if (action == null) return;

            actions ??= new();
            actions.IDs ??= new();
            if (!actions.IDs.Contains(action)) actions.IDs.Add(action);
            action = null;
        }

        /// <summary>Formats the Action list for the condition name, never throws on empty</summary>
        internal static string ActionListName(IDListCheck<ItemActionID> list)
        {
            var sb = new StringBuilder();
            foreach (var id in list.IDs)
            {
                if (id == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(id.name);
            }
            return sb.Length > 0 ? sb.ToString() : "Any";
        }
    }

    //MWC: New condition — checks the active Action Set (by name) of any equipped item.
    [System.Serializable, AddTypeMenu("Super Item/[Manager] Active Action Set Is")]
    public class C2_SI_ActiveActionSet : C2_SuperItemManager
    {
        public override string DynamicName => $"[SI Manager] Active Action Set [{setName.Value}]";

        [Tooltip("Name of the Action Set to match against any equipped item's active set (e.g. Combat, Stealth).")]
        public StringReference setName = new("");

        protected override bool _Evaluate() => Target != null && Target.HasActiveActionSet(setName.Value);
    }

    //MWC: New condition — checks whether the character is Dual Wielding (two items held in hands).
    //Covers any-pair (both hands full) and specific pairs like 2 Daggers, 2 Swords, Axe + Sword, Axe + Shield.
    [System.Serializable, AddTypeMenu("Super Item/[Manager] Is Dual Wielding")]
    public class C2_SI_DualWielding : C2_SuperItemManager
    {
        //MWC — the 'Any Two Hands / Specific Pair' mode enum is gone: it silently ignored the hand lists whenever it
        //was left on 'Any Two Hands', so a filled-in pair still matched ANY two held items (e.g. an Axe + Shield
        //satisfying a Dagger/Melee pair). The mode is inferred from the lists now, so what is authored is what is checked.
        public override string DynamicName => AnyPair
            ? "[SI Manager] Is Dual Wielding [Any]"
            : $"[SI Manager] Is Dual Wielding [{ItemListName(firstHand)} + {ItemListName(secondHand)}]";

        [Tooltip("Allowed item types for one hand. Empty = any held item")]
        public IDList<SuperItemID> firstHand;

        [Tooltip("Allowed item types for the other hand. Empty = any held item.\n" +
            "Put the same ID in both lists to require two of it (e.g. two Daggers).\n" +
            "Leave BOTH lists empty to simply check that both hands are holding something")]
        public IDList<SuperItemID> secondHand;

        /// <summary>No list authored: the condition is the plain "both hands are full" check</summary>
        private bool AnyPair => !HasItems(firstHand) && !HasItems(secondHand);

        private static bool HasItems(IDList<SuperItemID> list) => list != null && list.HasItems;

        protected override bool _Evaluate()
        {
            if (Target == null) return false;

            return AnyPair
                ? Target.IsDualWielding()                       //Any item on a Left-side point + any on a Right-side point
                : Target.IsDualWielding(firstHand, secondHand); //Two distinct held items, one matching each list
        }
    }
}
