// Last Change: 9/21/2026 9:56am
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Items
{
    /// <summary> Parent class for every Reaction that targets a Super Item Manager </summary>
    [Serializable]
    public abstract class SuperItemManagerReactionBase : Reaction
    {
        /// <summary> Component the Reaction looks for on the Reactor </summary>
        public override Type ReactionType => typeof(SuperItemManager);

        /// <summary> Name of an ID asset for the Dynamic Name, never throws on a missing reference </summary>
        protected static string IDName(UnityEngine.Object id) => id != null ? id.name : "None";

        /// <summary> Finds an equipped Item by its Super Item ID (null ID = the first Item found on an Equip Point) </summary>
        protected static SuperItem FindEquipped(SuperItemManager manager, SuperItemID id)
        {
            foreach (var point in manager.EquipPoints)
            {
                var item = point != null ? point.Item : null;
                if (item == null) continue;
                if (id == null || item.ID == id) return item;
            }

            if (manager.DefaultItemActive && (id == null || manager.DefaultItem.ID == id)) return manager.DefaultItem;

            return null;
        }

        /// <summary> Finds a holstered Item by its Super Item ID, scanning every stored slot (Dual Wielding) and not only the Holster head </summary>
        protected static SuperItem FindHolstered(SuperItemManager manager, SuperItemID id)
        {
            foreach (var holster in manager.holsters)
            {
                if (holster == null || !holster.HasItem) continue;

                if (id == null || holster.Item.ID == id) return holster.Item;

                foreach (var stored in holster.StoredItems)
                    if (stored != null && stored.ID == id) return stored;
            }
            return null;
        }

        /// <summary> Finds an Item the Manager owns: equipped first, holstered second </summary>
        protected static SuperItem FindOwned(SuperItemManager manager, SuperItemID id)
            => FindEquipped(manager, id) ?? FindHolstered(manager, id);

        /// <summary> Runs an action on every equipped Item matching the ID (null ID = every equipped Item + the Default Item). Returns how many Items were touched </summary>
        protected static int ForEachEquipped(SuperItemManager manager, SuperItemID id, Action<SuperItem> action)
        {
            var count = 0;

            foreach (var point in manager.EquipPoints)
            {
                var item = point != null ? point.Item : null;
                if (item == null) continue;
                if (id != null && item.ID != id) continue;

                action(item);
                count++;
            }

            if (manager.DefaultItemActive && (id == null || manager.DefaultItem.ID == id))
            {
                action(manager.DefaultItem);
                count++;
            }

            return count;
        }

        /// <summary> Every Item currently held on an Equip Point, as a new list safe to iterate while the Manager mutates its points </summary>
        protected static List<SuperItem> EquippedItems(SuperItemManager manager)
        {
            var list = new List<SuperItem>();

            foreach (var point in manager.EquipPoints)
                if (point != null && point.Item != null) list.Add(point.Item);

            return list;
        }

        /// <summary> Every Item stored in the Holsters (all slots, not only the heads), as a new list safe to iterate while the Manager mutates its holsters </summary>
        protected static List<SuperItem> HolsteredItems(SuperItemManager manager)
        {
            var list = new List<SuperItem>();

            foreach (var holster in manager.holsters)
            {
                if (holster == null) continue;

                foreach (var stored in holster.StoredItems)
                    if (stored != null && !list.Contains(stored)) list.Add(stored);

                if (holster.Item != null && !list.Contains(holster.Item)) list.Add(holster.Item);
            }
            return list;
        }
    }

    /// <summary> Draws, stores and holsters the Items owned by the Super Item Manager </summary>
    [HelpURL(ReactionDocs.SuperItem + "#manager-equip-and-unequip")]
    [Serializable, AddTypeMenu("Malbers/Super Item/[Manager] Equip - Unequip")]
    public class SIManagerEquipReaction : SuperItemManagerReactionBase
    {
        /// <summary> Equip/Unequip operations exposed by the Super Item Manager </summary>
        public enum SIEquipCommand
        {
            /// <summary> Draws the Item with that ID from its Holster into the hands </summary>
            Equip,
            /// <summary> Draws the Item stored in that Holster. Empty Holster + [Self Store Holster] = stores the Item in hand instead </summary>
            EquipFromHolster,
            /// <summary> Stores the Item with that ID, playing its Unequip animation </summary>
            Unequip,
            /// <summary> Stores whatever Item sits on that Equip Point, playing its Unequip animation </summary>
            UnequipFromEquipPoint,
            /// <summary> Stores every Item in the hands </summary>
            UnequipAll,
            /// <summary> Stores every Item in the hands except the one with that ID </summary>
            UnequipAllBut,
            /// <summary> Sends the Item straight to its Holster, with no Unequip animation </summary>
            StoreInHolster,
            /// <summary> Takes the Item out of its Holster without equipping it: it stays in the world with physics, interaction and ownership restored </summary>
            UnHolster,
        }

        /// <summary> Name shown on the Reaction row </summary>
        public override string DynamicName => command switch
        {
            SIEquipCommand.EquipFromHolster => $"[SI Manager] Equip From Holster [{IDName(holster)}]",
            SIEquipCommand.UnequipFromEquipPoint => $"[SI Manager] Unequip From Point [{IDName(equipPoint)}]",
            SIEquipCommand.UnequipAll => "[SI Manager] Unequip All",
            _ => $"[SI Manager] {command} [{IDName(item)}]",
        };

        [Tooltip("Operation to run on the Super Item Manager")]
        public SIEquipCommand command = SIEquipCommand.Equip;

        [Tooltip("Super Item the operation applies to")]
        [Hide(nameof(command), 0, 2, 5, 6, 7)]
        public SuperItemID item;

        [Tooltip("Holster slot to draw from (or to store into when it is empty and [Self Store Holster] is ON)")]
        [Hide(nameof(command), 1)]
        public HolsterID holster;

        [Tooltip("Equip Point holding the Item to store")]
        [Hide(nameof(command), 3)]
        public EquipPointID equipPoint;

        [Tooltip("Skip the Equip animation: the Item appears directly in the hand")]
        [Hide(nameof(command), 0)]
        public BoolReference fast = new(false);

        /// <summary> Applies the command to the Super Item Manager </summary>
        protected override bool _TryReact(Component reactor)
        {
            if (reactor is not SuperItemManager manager) return false;

            switch (command)
            {
                case SIEquipCommand.Equip:
                    {
                        //Only a holstered Item can be drawn: an Item already in the hands has nothing to do here
                        var target = FindHolstered(manager, item);
                        if (target == null) return false;

                        manager.Item_Equip(target, fast.Value);
                        return true;
                    }

                case SIEquipCommand.EquipFromHolster:
                    if (holster == null) return false;
                    manager.Item_Equip_From_Holster(holster);
                    return true;

                case SIEquipCommand.Unequip:
                    {
                        var target = FindEquipped(manager, item);
                        if (target == null) return false;

                        manager.Item_Unequip(target);
                        return true;
                    }

                case SIEquipCommand.UnequipFromEquipPoint:
                    {
                        if (equipPoint == null) return false;

                        var point = manager.D_EquipPoints != null && manager.D_EquipPoints.TryGetValue(equipPoint, out var found) ? found : null;
                        if (point == null || point.Item == null) return false;

                        manager.Item_Unequip(point.Item);
                        return true;
                    }

                case SIEquipCommand.UnequipAll:
                    manager.Item_HolsterAll();
                    return true;

                case SIEquipCommand.UnequipAllBut:
                    {
                        var keep = FindEquipped(manager, item);

                        //The Item to keep is not in the hands: storing everything is never what this command means
                        if (keep == null) return false;

                        manager.Item_HolsterAllBut(keep);
                        return true;
                    }

                case SIEquipCommand.StoreInHolster:
                    {
                        var target = FindEquipped(manager, item);
                        if (target == null) return false;

                        manager.Item_Holster(target);
                        return true;
                    }

                case SIEquipCommand.UnHolster:
                    {
                        var target = FindHolstered(manager, item);
                        if (target == null) return false;

                        manager.Item_UnHolster(target);
                        return true;
                    }

                default: return false;
            }
        }
    }

    /// <summary> Drops the Items owned by the Super Item Manager back into the world </summary>
    [HelpURL(ReactionDocs.SuperItem + "#manager-drop-item")]
    [Serializable, AddTypeMenu("Malbers/Super Item/[Manager] Drop Item")]
    public class SIManagerDropReaction : SuperItemManagerReactionBase
    {
        /// <summary> Which Items the drop applies to </summary>
        public enum SIDropCommand
        {
            /// <summary> Drops the Item with that ID, equipped or holstered </summary>
            ByItemID,
            /// <summary> Drops whatever Item sits on that Equip Point </summary>
            FromEquipPoint,
            /// <summary> Drops the Item stored in that Holster </summary>
            FromHolster,
            /// <summary> Drops every Item held in the hands </summary>
            AllEquipped,
            /// <summary> Drops every Item held in the hands and stored in the Holsters </summary>
            Everything,
        }

        /// <summary> Name shown on the Reaction row </summary>
        public override string DynamicName => command switch
        {
            SIDropCommand.ByItemID => $"[SI Manager] Drop [{IDName(item)}]",
            SIDropCommand.FromEquipPoint => $"[SI Manager] Drop From Point [{IDName(equipPoint)}]",
            SIDropCommand.FromHolster => $"[SI Manager] Drop From Holster [{IDName(holster)}]",
            _ => $"[SI Manager] Drop [{command}]",
        };

        [Tooltip("Which Items to drop")]
        public SIDropCommand command = SIDropCommand.ByItemID;

        [Tooltip("Super Item to drop. It can be equipped or holstered")]
        [Hide(nameof(command), 0)]
        public SuperItemID item;

        [Tooltip("Equip Point holding the Item to drop")]
        [Hide(nameof(command), 1)]
        public EquipPointID equipPoint;

        [Tooltip("Holster storing the Item to drop")]
        [Hide(nameof(command), 2)]
        public HolsterID holster;

        /// <summary> Applies the command to the Super Item Manager </summary>
        protected override bool _TryReact(Component reactor)
        {
            if (reactor is not SuperItemManager manager) return false;

            switch (command)
            {
                case SIDropCommand.ByItemID:
                    {
                        var target = FindOwned(manager, item);
                        if (target == null) return false;

                        manager.Item_Drop(target);
                        return true;
                    }

                case SIDropCommand.FromEquipPoint:
                    if (equipPoint == null) return false;
                    manager.Item_Drop(equipPoint);
                    return true;

                case SIDropCommand.FromHolster:
                    if (holster == null) return false;
                    manager.Item_Drop(holster);
                    return true;

                case SIDropCommand.AllEquipped:
                    foreach (var equipped in EquippedItems(manager)) manager.Item_Drop(equipped);
                    return true;

                case SIDropCommand.Everything:
                    foreach (var equipped in EquippedItems(manager)) manager.Item_Drop(equipped);
                    foreach (var stored in HolsteredItems(manager)) manager.Item_Drop(stored);
                    return true;

                default: return false;
            }
        }
    }

    /// <summary> Drives an Item Action (Fire, Aim, Block...) through the Super Item Manager, the same path a real input press takes </summary>
    [HelpURL(ReactionDocs.SuperItem + "#manager-item-action")]
    [Serializable, AddTypeMenu("Malbers/Super Item/[Manager] Item Action")]
    public class SIManagerActionReaction : SuperItemManagerReactionBase
    {
        /// <summary> What to do with the Action input </summary>
        public enum SIActionCommand
        {
            /// <summary> Presses the Action input and holds it </summary>
            Press,
            /// <summary> Releases the Action input </summary>
            Release,
            /// <summary> Presses and releases the Action input after [Hold Time] </summary>
            Tap,
            /// <summary> Force-stops that Action on every equipped Item </summary>
            Interrupt,
            /// <summary> Force-stops every Action of every equipped Item </summary>
            InterruptAll,
        }

        /// <summary> Name shown on the Reaction row </summary>
        public override string DynamicName => command == SIActionCommand.InterruptAll
            ? "[SI Manager] Interrupt All Actions"
            : $"[SI Manager] Action [{command}] [{IDName(action)}]{(item != null ? $" on [{item.name}]" : string.Empty)}";

        [Tooltip("What to do with the Action input")]
        public SIActionCommand command = SIActionCommand.Press;

        [Tooltip("Item Action to drive (Fire, Aim, Block...)")]
        [Hide(nameof(command), 0, 1, 2, 3)]
        public ItemActionID action;

        [Tooltip("Restrict the Action to the Item with this ID. Empty = the best equipped Item that answers the Action")]
        [Hide(nameof(command), 0, 1, 2)]
        public SuperItemID item;

        [Tooltip("Seconds the input stays pressed before it is released. Zero = released on the next frame")]
        [Hide(nameof(command), 2)]
        public FloatReference holdTime = new(0f);

        /// <summary> Applies the command to the Super Item Manager </summary>
        protected override bool _TryReact(Component reactor)
        {
            if (reactor is not SuperItemManager manager) return false;

            switch (command)
            {
                case SIActionCommand.Press:
                    return action != null && manager.Action_Press(action, item);

                case SIActionCommand.Release:
                    return action != null && manager.Action_Release(action, item);

                case SIActionCommand.Tap:
                    //MWC — the Manager owns the delayed release now (tracked per Action, released on disable). A Reaction
                    //is shared data with no lifetime of its own, so it could never cancel the coroutine it started here.
                    return action != null && manager.Action_Tap(action, item, holdTime.Value);

                case SIActionCommand.Interrupt:
                    if (action == null) return false;
                    manager.Action_Interrupt(action);
                    return true;

                case SIActionCommand.InterruptAll:
                    manager.Action_Interrupt(null);
                    return true;

                default: return false;
            }
        }
    }

    /// <summary> Changes the Action Set of the Items held by the Super Item Manager, without needing a reference to the Item itself </summary>
    [HelpURL(ReactionDocs.SuperItem + "#manager-item-action-set")]
    [Serializable, AddTypeMenu("Malbers/Super Item/[Manager] Item Action Set")]
    public class SIManagerActionSetReaction : SuperItemManagerReactionBase
    {
        /// <summary> Action Set operations applied to the equipped Items </summary>
        public enum SISetCommand
        {
            /// <summary> Activates the Set with that name </summary>
            SetActiveByName,
            /// <summary> Activates the Set at that index </summary>
            SetActiveByIndex,
            /// <summary> Makes the Set with that name the Default one </summary>
            SetDefaultByName,
            /// <summary> Returns the Item to its Default Set </summary>
            RestoreToDefault,
            /// <summary> Force-stops every Action of the Active Set, keeping the Set active </summary>
            InterruptSet,
        }

        /// <summary> Name shown on the Reaction row </summary>
        public override string DynamicName
        {
            get
            {
                var on = item != null ? item.name : "All Equipped";

                return command switch
                {
                    SISetCommand.SetActiveByIndex => $"[SI Manager] Set Active Set [{index.Value}] on [{on}]",
                    SISetCommand.RestoreToDefault => $"[SI Manager] Restore Default Set on [{on}]",
                    SISetCommand.InterruptSet => $"[SI Manager] Interrupt Set on [{on}]",
                    _ => $"[SI Manager] {command} [{setName.Value}] on [{on}]",
                };
            }
        }

        [Tooltip("Action Set operation to apply")]
        public SISetCommand command = SISetCommand.SetActiveByName;

        [Tooltip("Item the operation applies to. Empty = every equipped Item (and the Default Item)")]
        public SuperItemID item;

        [Tooltip("Name of the Action Set (Two Handed, Off Hand, Combat...)")]
        [Hide(nameof(command), 0, 2)]
        public StringReference setName = new("Default");

        [Tooltip("Index of the Action Set on the Item's Action Sets list")]
        [Hide(nameof(command), 1)]
        public IntReference index = new(0);

        /// <summary> Applies the command to every matching equipped Item </summary>
        protected override bool _TryReact(Component reactor)
        {
            if (reactor is not SuperItemManager manager) return false;

            var applied = command switch
            {
                SISetCommand.SetActiveByName => ForEachEquipped(manager, item, x => x.ActionSet_Active(setName.Value)),
                SISetCommand.SetActiveByIndex => ForEachEquipped(manager, item, x => x.ActionSet_Active(index.Value)),
                SISetCommand.SetDefaultByName => ForEachEquipped(manager, item, x => x.ActionSet_Default(setName.Value)),
                SISetCommand.RestoreToDefault => ForEachEquipped(manager, item, x => x.ActionSet_RestoreToDefault()),
                //MWC — force: an authored Reaction is an explicit force-stop, never an Action's own startup noise
                SISetCommand.InterruptSet => ForEachEquipped(manager, item, x => x.ActionSet_Interrupt("Reaction", force: true)),
                _ => 0,
            };

            return applied > 0;
        }
    }

    /// <summary> Picks a Super Item from the world, playing the Item's Pick Mode </summary>
    [HelpURL(ReactionDocs.SuperItem + "#manager-pick-item")]
    [Serializable, AddTypeMenu("Malbers/Super Item/[Manager] Pick Item")]
    public class SIManagerPickReaction : SuperItemManagerReactionBase
    {
        /// <summary> Where the picked Item ends up </summary>
        public enum SIPickCommand
        {
            /// <summary> To the hands when the Manager's global [Auto Equip] is ON, otherwise the Item's own [Auto Equip] decides: hands or Holster </summary>
            Auto,
            /// <summary> Always to the hands, ignoring the Item's [Auto Equip] </summary>
            ForceEquip,
            /// <summary> Always to its Holster, ignoring the Item's [Auto Equip] </summary>
            ForceHolster,
            /// <summary> Picks the first free Item inside the character's Interactor (what the [Pick Up] prompt shows) </summary>
            Focused,
        }

        /// <summary> Name shown on the Reaction row </summary>
        public override string DynamicName => command == SIPickCommand.Focused
            ? "[SI Manager] Pick Focused Item"
            : $"[SI Manager] Pick [{(itemGO.Value != null ? itemGO.Value.name : "None")}] [{command}]";

        [Tooltip("Where the picked Item ends up")]
        public SIPickCommand command = SIPickCommand.Auto;

        [Tooltip("GameObject holding the Super Item to pick")]
        [Hide(nameof(command), 0, 1, 2)]
        public GameObjectReference itemGO = new();

        /// <summary> Applies the command to the Super Item Manager </summary>
        protected override bool _TryReact(Component reactor)
        {
            if (reactor is not SuperItemManager manager) return false;

            if (command == SIPickCommand.Focused)
            {
                foreach (var focused in manager.FocusedItems)
                    if (focused != null && focused.IsFree) return manager.Item_Pick(focused);

                return false;
            }

            var go = itemGO.Value;
            if (go == null) return false;

            var superItem = go.FindComponent<SuperItem>();
            if (superItem == null) return false;

            return command switch
            {
                SIPickCommand.ForceEquip => manager.Item_Pick(superItem, true),
                SIPickCommand.ForceHolster => manager.Item_Pick(superItem, false),
                _ => manager.Item_Pick(superItem),
            };
        }
    }

    /// <summary> Global switches of the Super Item Manager </summary>
    [HelpURL(ReactionDocs.SuperItem + "#manager-global")]
    [Serializable, AddTypeMenu("Malbers/Super Item/[Manager] Global")]
    public class SIManagerGlobalReaction : SuperItemManagerReactionBase
    {
        /// <summary> Manager-wide operations </summary>
        public enum SIGlobalCommand
        {
            /// <summary> Clears every [No Items in State|Stance|Mode] block and draws the held Items back </summary>
            ReleaseHold,
            /// <summary> Re-checks whether the Default Item (Unarmed) has to be in the hands </summary>
            RefreshDefaultItem,
            /// <summary> Re-runs the [External] Auto Activation of every equipped Item's Action Sets </summary>
            CheckActionSets,
            /// <summary> Turns [Fast Weapons] on or off </summary>
            FastWeapons,
            /// <summary> Sets the [Auto Store] idle time. Zero disables Auto Store </summary>
            AutoStore,
            /// <summary> Enables or disables the Super Item Manager component </summary>
            Enable,
            /// <summary> Locks (true) or Unlocks (false) the Manager: every equipped Item is put away with the Manager's [Lock Mode] and every Item operation is refused until Unlock </summary>
            Lock,
            /// <summary> Locks the Manager storing every equipped Item in its Holster, whatever the Manager's [Lock Mode] says </summary>
            LockHolster,
            /// <summary> Locks the Manager hiding every equipped Item in place, whatever the Manager's [Lock Mode] says </summary>
            LockHide,
            /// <summary> Turns the Manager's global [Auto Equip] on or off: ON = every picked Item goes to the hands, OFF = the Item's own [Auto Equip] decides </summary>
            AutoEquip,
            /// <summary> FALSE: disables the colliders of every Item the Manager owns (equipped, holstered, hidden, Default). TRUE: restores each Item's authored state ([Disable Colliders On Equip] / [On Holster]). E.g. OFF while dodging, TRUE when it ends </summary>
            ItemColliders,
        }

        /// <summary> Name shown on the Reaction row </summary>
        public override string DynamicName => command switch
        {
            SIGlobalCommand.FastWeapons => $"[SI Manager] Fast Weapons [{(boolValue.Value ? "ON" : "OFF")}]",
            SIGlobalCommand.AutoStore => $"[SI Manager] Auto Store [{floatValue.Value:F1}s]",
            SIGlobalCommand.Enable => $"[SI Manager] [{(boolValue.Value ? "Enable" : "Disable")}]",
            SIGlobalCommand.Lock => $"[SI Manager] [{(boolValue.Value ? "Lock" : "Unlock")}]",
            SIGlobalCommand.LockHolster => "[SI Manager] Lock [Holster Items]",
            SIGlobalCommand.LockHide => "[SI Manager] Lock [Hide Items]",
            SIGlobalCommand.AutoEquip => $"[SI Manager] Auto Equip [{(boolValue.Value ? "ON" : "OFF")}]",
            SIGlobalCommand.ItemColliders => $"[SI Manager] Item Colliders [{(boolValue.Value ? "ON" : "OFF")}]",
            _ => $"[SI Manager] {command}",
        };

        [Tooltip("Manager-wide operation to run")]
        public SIGlobalCommand command = SIGlobalCommand.ReleaseHold;

        [Tooltip("Value applied by the [Fast Weapons], [Enable], [Lock], [Auto Equip] and [Item Colliders] commands")]
        [Hide(nameof(command), 3, 5, 6, 9, 10)]
        public BoolReference boolValue = new(true);

        [Tooltip("Seconds an Item can stay idle in the hands before it is stored. Zero disables Auto Store")]
        [Hide(nameof(command), 4)]
        public FloatReference floatValue = new(3f);

        /// <summary> Applies the command to the Super Item Manager </summary>
        protected override bool _TryReact(Component reactor)
        {
            if (reactor is not SuperItemManager manager) return false;

            switch (command)
            {
                case SIGlobalCommand.ReleaseHold: manager.Items_ReleaseHold(); break;
                case SIGlobalCommand.RefreshDefaultItem: manager.RefreshDefaultItem(); break;
                case SIGlobalCommand.CheckActionSets: manager.Set_AutoActivation_External(); break;
                case SIGlobalCommand.FastWeapons: manager.UseFastWeapons = boolValue.Value; break;
                case SIGlobalCommand.AutoStore: manager.AutoStore.Value = floatValue.Value; break;
                case SIGlobalCommand.Enable: manager.enabled = boolValue.Value; break;
                case SIGlobalCommand.Lock: manager.Lock(boolValue.Value); break;
                case SIGlobalCommand.LockHolster: manager.Lock(LockItemsMode.Holster); break;
                case SIGlobalCommand.LockHide: manager.Lock(LockItemsMode.Hide); break;
                case SIGlobalCommand.AutoEquip: manager.AutoEquip = boolValue.Value; break;
                case SIGlobalCommand.ItemColliders: manager.Items_Colliders(boolValue.Value); break;
                default: return false;
            }
            return true;
        }
    }
}
