// Last Change: 9/30/2026 11:37am
using MalbersAnimations.Conditions;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Items
{
    [System.Serializable]
    public abstract class C2_SuperItem : ConditionCore
    {
        [Hide(nameof(LocalTarget))] public SuperItem Target;
        protected override void _SetTarget(Object target) => VerifyComponent(target, ref Target);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;
    }

    //MWC — C2_SI_UnitsInChamber and C2_SI_ChamberSize were removed: the Chamber is now a Stat on the item's Stats component.
    //Use the generic [Stats] condition (C2_Stats) instead: Value / MaxValue / Full / Empty cover both cases.

    [System.Serializable, AddTypeMenu("Super Item/[Super Item] Has Target")]
    public class C2_SI_HasTarget : C2_SuperItem
    {
        //MWC: Null-guard Target.Target — DynamicName runs in the inspector before a Target is assigned (NRE fix).
        public override string DynamicName => $"[{(Target ? Target.name : "Dynamic Item")}] Has Target is [{(Target != null && Target.Target != null ? "True" : "False")}]";
        protected override bool _Evaluate() => Target != null && Target.Target != null;
    }

    [System.Serializable, AddTypeMenu("Super Item/[Super Item] Has User")]
    public class C2_SI_HasUser : C2_SuperItem
    {
        //MWC: Null-guard Target.User — same inspector-time NRE as Has Target.
        public override string DynamicName => $"[{(Target ? Target.name : "Dynamic Item")}] Has User is [{(Target != null && Target.User != null ? "True" : "False")}]";
        protected override bool _Evaluate() => Target != null && Target.User != null;
    }

    /// <summary> True when the Item carries the given Malbers Tags on its own Tags component (Item-side twin of the [Manager] Item Equipped By Tag condition) </summary>
    [System.Serializable, AddTypeMenu("Super Item/[Super Item] Has Tag")]
    public class C2_SI_HasTag : C2_SuperItem
    {
        public enum ComparisonID { HasAny, HasAll }

        public override string DynamicName => $"[{(Target ? Target.name : "Dynamic Item")}] Has Tag [{TagListName(tags)}] ({checkIf})";

        [Tooltip("Malbers Tags to look for on the Item (its own Tags component). Has Any = at least one of them. Has All = every one of them")]
        public MTags tags = new();
        public ComparisonID checkIf = ComparisonID.HasAny;

        protected override bool _Evaluate()
        {
            if (Target == null || tags == null || !tags.ValidObjects) return false;
            return checkIf == ComparisonID.HasAll ? Target.gameObject.HasAllMalbersTag(tags.tags) : Target.gameObject.HasMalbersTag(tags.tags);
        }

        /// <summary> Formats the Tag list for the condition name, never throws on empty </summary>
        private static string TagListName(MTags list)
        {
            if (list == null || !list.ValidObjects) return "None";
            var sb = new System.Text.StringBuilder();
            foreach (var tag in list.tags)
            {
                if (tag == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(tag.name);
            }
            return sb.Length > 0 ? sb.ToString() : "None";
        }
    }

    //MWC: Dual Wielding - checks which hand (Side) the item is currently equipped on, so two identical items can be told apart.
    //Use it in an Action Set's Auto-Activation to give the off-hand copy its own set (e.g. a follower/empty set).
    [System.Serializable, AddTypeMenu("Super Item/[Super Item] Is On Side")]
    public class C2_SI_IsOnSide : C2_SuperItem
    {
        public override string DynamicName => $"[{(Target ? Target.name : "Dynamic Item")}] Is On Side [{side}]";

        [Tooltip("Hand (Side) the item must be equipped on. The Side is taken from the Equip Point the item currently sits in.")]
        public WeaponSide side = WeaponSide.Left;

        protected override bool _Evaluate() => Target != null && Target.EffectiveSide == side;
    }

    //MWC: New condition — true while an Action of the item's Active Set is playing (firing, blocking, aiming…).
    //Leave the Action empty to check for ANY playing Action on this item.
    [System.Serializable, AddTypeMenu("Super Item/[Super Item] Is Action Playing")]
    public class C2_SI_Item_ActionPlaying : C2_SuperItem, ISerializationCallbackReceiver
    {
        /// <summary>Name shown on the Condition row</summary>
        public override string DynamicName =>
            $"[{(Target ? Target.name : "Dynamic Item")}] Is Playing Action {(actions == null || actions.Empty ? "[Any]" : $"{(actions.include ? "" : "NOT ")}[{C2_SI_ActionPlaying.ActionListName(actions)}]")}";

        [Tooltip("Actions to check on the item's Active Action Set.\n" +
            "Include: true when one of these Actions is playing.\n" +
            "Exclude: true when an Action that is NOT in the list is playing.\n" +
            "Empty = ANY playing Action")]
        public IDListCheck<ItemActionID> actions = new();

        //Legacy single Action, replaced by the [actions] list. Kept hidden so existing assets migrate their authored value
        [SerializeField, HideInInspector] private ItemActionID action;

        /// <summary>Checks the Item for a playing Action that passes the Include/Exclude list</summary>
        protected override bool _Evaluate() => Target != null && Target.IsActionPlaying(actions);

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
    }

    //MWC: New condition — checks which Action Set is currently active on the item.
    [System.Serializable, AddTypeMenu("Super Item/[Super Item] Is Set Active")]
    public class C2_SI_Item_SetActive : C2_SuperItem
    {
        public override string DynamicName =>
            $"[{(Target ? Target.name : "Dynamic Item")}] Active Set is [{(string.IsNullOrEmpty(setName.Value) ? "Any" : setName.Value)}]";

        [Tooltip("Name of the Action Set that must be active (e.g. Two Handed, Off Hand). Empty = the item has ANY active Set")]
        public StringReference setName = new(string.Empty);

        protected override bool _Evaluate() => Target != null && Target.IsActionSetActive(setName.Value);
    }

    //MWC: New condition — true while the item is equipped on an Equip Point.
    [System.Serializable, AddTypeMenu("Super Item/[Super Item] Is Equipped")]
    public class C2_SI_Item_IsEquipped : C2_SuperItem
    {
        public override string DynamicName => $"[{(Target ? Target.name : "Dynamic Item")}] Is Equipped";

        protected override bool _Evaluate() => Target != null && Target.IsEquipped;
    }

    //MWC: New condition — true while the item is stored in one of the User's holsters.
    [System.Serializable, AddTypeMenu("Super Item/[Super Item] Is Holstered")]
    public class C2_SI_Item_IsHolstered : C2_SuperItem
    {
        public override string DynamicName => $"[{(Target ? Target.name : "Dynamic Item")}] Is Holstered";

        protected override bool _Evaluate() => Target != null && Target.IsInHolster;
    }

    //MWC: New condition — item-side Dual Wielding: this item is held while ANOTHER item is held on the opposite hand.
    //Unlike the [Manager] version, it is anchored to THIS item, so a Set can ask "am I the one being paired?".
    [System.Serializable, AddTypeMenu("Super Item/[Super Item] Is Dual Wielding")]
    public class C2_SI_Item_DualWielding : C2_SuperItem
    {
        public override string DynamicName =>
            $"[{(Target ? Target.name : "Dynamic Item")}] Is Dual Wielding [{(HasList ? "Filtered" : "Any")}]";

        [Tooltip("Allowed item types for the OTHER hand. Empty = any item held on the opposite hand counts")]
        public IDList<SuperItemID> otherHand;

        private bool HasList => otherHand != null && otherHand.HasItems;

        protected override bool _Evaluate()
        {
            if (Target == null) return false;

            var partner = Target.DualWieldPartner;
            if (partner == null) return false;

            return !HasList || (partner.ItemType != null && otherHand.Contains(partner.ItemType));
        }
    }
}
