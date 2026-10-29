// Last Change: 9/21/2026 9:56am
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using System;
using UnityEngine;


namespace MalbersAnimations.Items
{
    /// <summary> Parent class for every Reaction that targets a Super Item </summary>
    [Serializable]
    public abstract class SuperItemReactionBase : Reaction
    {
        /// <summary> Component the Reaction looks for on the Reactor </summary>
        public override Type ReactionType => typeof(SuperItem);

        /// <summary> Name of an ID asset for the Dynamic Name, never throws on a missing reference </summary>
        protected static string IDName(UnityEngine.Object id) => id != null ? id.name : "None";

        /// <summary> Finds an Action by ID on the Item's Active Action Set </summary>
        protected static ItemAction FindAction(SuperItem item, ItemActionID id)
        {
            if (id == null) return null;

            var set = item.ActiveActionSet;
            if (set == null || set.actions == null) return null;

            foreach (var action in set.actions)
                if (action != null && action.ActionID == id) return action;

            return null;
        }
    }

    /// <summary> Changes the Active or the Default Action Set of a Super Item </summary>
    [HelpURL(ReactionDocs.SuperItem + "#item-action-set")]
    [System.Serializable, AddTypeMenu("Malbers/Super Item/[Item] Action Set")]
    public class SuperItemReaction : SuperItemReactionBase
    {
        /// <summary> Action Set operations applied to the Item </summary>
        public enum SuperItemReactionType { SetActionSetByName, SetActionSetByIndex, RestoreToDefault, SetDefaultActionSet }

        /// <summary> Name shown on the Reaction row </summary>
        public override string DynamicName => command switch
        {
            SuperItemReactionType.SetActionSetByName => $"[SI Item] Set Action Set [{ActionSetName.Value}]",
            SuperItemReactionType.SetActionSetByIndex => $"[SI Item] Set Action Set [{Index.Value}]",
            SuperItemReactionType.SetDefaultActionSet => $"[SI Item] Default Action Set [{ActionSetName.Value}]",
            SuperItemReactionType.RestoreToDefault => "[SI Item] Restore Default Action Set",
            _ => $"[SI Item] {command}",
        };

        /// <summary> Action Set operation to apply </summary>
        public SuperItemReactionType command = SuperItemReactionType.SetActionSetByName;

        /// <summary> Index of the Action Set on the Item's Action Sets list </summary>
        [Hide(nameof(command), (int)(SuperItemReactionType.SetActionSetByIndex))]
        public IntReference Index = new(0);

        /// <summary> Name of the Action Set to activate or to make the Default one </summary>
        [Hide(nameof(command), (int)SuperItemReactionType.SetActionSetByName, (int)SuperItemReactionType.SetDefaultActionSet)]
        public StringReference ActionSetName = new("Default");

        /// <summary> Applies the command to the Super Item </summary>
        protected override bool _TryReact(Component reactor)
        {
            var superItem = reactor as SuperItem;

            if (superItem == null)
            {
                Debug.LogError($"The Reactor {reactor.name} is not a Super Item", reactor);
                return false;
            }

            switch (command)
            {
                case SuperItemReactionType.SetActionSetByName:
                    superItem.ActionSet_Active(ActionSetName.Value);
                    break;

                case SuperItemReactionType.SetActionSetByIndex:
                    superItem.ActionSet_Active(Index.Value);
                    break;
                case SuperItemReactionType.SetDefaultActionSet:
                    superItem.ActionSet_Default(ActionSetName.Value);
                    break;
                case SuperItemReactionType.RestoreToDefault:
                    superItem.ActionSet_RestoreToDefault();
                    break;
                default:
                    break;
            }


            return true;
        }
    }

    /// <summary> Drives an Action of this Item's Active Set, and blocks or unblocks Actions on it </summary>
    [HelpURL(ReactionDocs.SuperItem + "#item-action")]
    [Serializable, AddTypeMenu("Malbers/Super Item/[Item] Action")]
    public class SIItemActionReaction : SuperItemReactionBase
    {
        /// <summary> What to do with the Item's Action </summary>
        public enum SIItemActionCommand
        {
            /// <summary> Presses the Action input and holds it </summary>
            Press,
            /// <summary> Releases the Action input </summary>
            Release,
            /// <summary> Presses and releases the Action input after [Hold Time] </summary>
            Tap,
            /// <summary> Force-stops that Action </summary>
            Interrupt,
            /// <summary> Force-stops every Action of the Active Set </summary>
            InterruptAll,
            /// <summary> Blocks the Action on the Item (owner: Reaction): it cannot start until every owner unblocks it </summary>
            Block,
            /// <summary> Removes the Reaction block on the Action; blocks placed by Processors on other Items stay </summary>
            Unblock,
            /// <summary> Removes every block on the Item, whoever placed it </summary>
            ClearBlocked,
        }

        /// <summary> Name shown on the Reaction row </summary>
        public override string DynamicName => command switch
        {
            SIItemActionCommand.InterruptAll => "[SI Item] Interrupt All Actions",
            SIItemActionCommand.ClearBlocked => "[SI Item] Clear Blocked Actions",
            _ => $"[SI Item] Action [{command}] [{IDName(action)}]",
        };

        [Tooltip("What to do with the Item's Action")]
        public SIItemActionCommand command = SIItemActionCommand.Press;

        [Tooltip("Action of the Item's Active Set (Fire, Aim, Block...)")]
        [Hide(nameof(command), 0, 1, 2, 3, 5, 6)]
        public ItemActionID action;

        [Tooltip("Seconds the input stays pressed before it is released. Zero = released on the next frame")]
        [Hide(nameof(command), 2)]
        public FloatReference holdTime = new(0f);

        /// <summary> Applies the command to the Super Item </summary>
        protected override bool _TryReact(Component reactor)
        {
            if (reactor is not SuperItem item) return false;

            switch (command)
            {
                case SIItemActionCommand.Press:
                case SIItemActionCommand.Release:
                    {
                        var found = FindAction(item, action);
                        if (found == null) return false;

                        found.ActivateOnInput(command == SIItemActionCommand.Press, item, item.ActiveActionSet);
                        return true;
                    }

                case SIItemActionCommand.Tap:
                    {
                        var found = FindAction(item, action);
                        if (found == null) return false;

                        var set = item.ActiveActionSet;
                        found.ActivateOnInput(true, item, set);

                        if (holdTime.Value > 0)
                            item.Delay_Action(holdTime.Value, () => found.ActivateOnInput(false, item, set));
                        else
                            item.Delay_Action(() => found.ActivateOnInput(false, item, set));

                        return true;
                    }

                case SIItemActionCommand.Interrupt:
                    {
                        var found = FindAction(item, action);
                        if (found == null) return false;

                        found.InputValue = false;
                        return found.Interrupt(item, item.ActiveActionSet, force: true); //MWC — an authored Reaction is an explicit force-stop
                    }

                case SIItemActionCommand.InterruptAll:
                    return item.ActionSet_Interrupt("Reaction", force: true); //MWC — an authored Reaction is an explicit force-stop

                case SIItemActionCommand.Block:
                    if (action == null) return false;
                    item.BlockedActions.Block(action, BlockedActionSet.ReactionOwner);
                    return true;

                case SIItemActionCommand.Unblock:
                    if (action == null) return false;
                    return item.BlockedActions.Unblock(action, BlockedActionSet.ReactionOwner);

                case SIItemActionCommand.ClearBlocked:
                    item.BlockedActions.Clear();
                    return true;

                default: return false;
            }
        }
    }

    /// <summary> Equips, stores, holsters or drops this Item using the Manager that owns it </summary>
    [HelpURL(ReactionDocs.SuperItem + "#item-equip-and-unequip")]
    [Serializable, AddTypeMenu("Malbers/Super Item/[Item] Equip - Unequip")]
    public class SIItemEquipReaction : SuperItemReactionBase
    {
        /// <summary> Equip operations applied to this Item </summary>
        public enum SIItemEquipCommand
        {
            /// <summary> Puts the Item in the hands </summary>
            Equip,
            /// <summary> Stores the Item, playing its Unequip animation </summary>
            Unequip,
            /// <summary> Sends the Item straight to its Holster, with no Unequip animation </summary>
            StoreInHolster,
            /// <summary> Takes the Item out of its Holster and leaves it in the world (not equipped): physics, interaction and ownership restored </summary>
            UnHolster,
            /// <summary> Drops the Item back into the world </summary>
            Drop,
        }

        /// <summary> Name shown on the Reaction row </summary>
        public override string DynamicName => $"[SI Item] {command}{(command == SIItemEquipCommand.Equip && fast.Value ? " (Fast)" : string.Empty)}";

        [Tooltip("Operation applied to this Item")]
        public SIItemEquipCommand command = SIItemEquipCommand.Equip;

        [Tooltip("Skip the Equip animation: the Item appears directly in the hand")]
        [Hide(nameof(command), 0)]
        public BoolReference fast = new(false);

        /// <summary> Applies the command through the Item's User (its Super Item Manager) </summary>
        protected override bool _TryReact(Component reactor)
        {
            if (reactor is not SuperItem item) return false;

            var manager = item.User;

            if (manager == null)
            {
                Debug.LogWarning($"[{item.name}] has no User (Super Item Manager). The [{command}] Reaction is ignored", item);
                return false;
            }

            switch (command)
            {
                case SIItemEquipCommand.Equip: manager.Item_Equip(item, fast.Value); break;
                case SIItemEquipCommand.Unequip: manager.Item_Unequip(item); break;
                case SIItemEquipCommand.StoreInHolster: manager.Item_Holster(item); break;
                case SIItemEquipCommand.UnHolster: manager.Item_UnHolster(item); break;
                case SIItemEquipCommand.Drop: manager.Item_Drop(item); break;
                default: return false;
            }
            return true;
        }
    }

    /// <summary> Sets or clears the Target this Item is pointed at (what the Aim and the Processors use) </summary>
    [HelpURL(ReactionDocs.SuperItem + "#item-target")]
    [Serializable, AddTypeMenu("Malbers/Super Item/[Item] Target")]
    public class SIItemTargetReaction : SuperItemReactionBase
    {
        /// <summary> Where the Item's Target comes from </summary>
        public enum SIItemTargetCommand
        {
            /// <summary> Uses the GameObject stored on the Reaction </summary>
            SetTarget,
            /// <summary> Uses the character holding the Item </summary>
            SetTargetToUser,
            /// <summary> Removes the Item's Target </summary>
            ClearTarget,
        }

        /// <summary> Name shown on the Reaction row </summary>
        public override string DynamicName => command switch
        {
            SIItemTargetCommand.SetTarget => $"[SI Item] Set Target [{(target.Value != null ? target.Value.name : "None")}]",
            _ => $"[SI Item] {command}",
        };

        [Tooltip("Where the Item's Target comes from")]
        public SIItemTargetCommand command = SIItemTargetCommand.SetTarget;

        [Tooltip("GameObject the Item will aim at")]
        [Hide(nameof(command), 0)]
        public GameObjectReference target = new();

        /// <summary> Applies the command to the Super Item </summary>
        protected override bool _TryReact(Component reactor)
        {
            if (reactor is not SuperItem item) return false;

            switch (command)
            {
                case SIItemTargetCommand.SetTarget:
                    if (target.Value == null) return false;
                    item.Target = target.Value;
                    return true;

                case SIItemTargetCommand.SetTargetToUser:
                    if (item.UserGo == null) return false;
                    item.Target = item.UserGo;
                    return true;

                case SIItemTargetCommand.ClearTarget:
                    item.Target = null;
                    return true;

                default: return false;
            }
        }
    }

    /// <summary> Turns the Item's own switches on and off: Auto Equip, interaction, physics and colliders </summary>
    [HelpURL(ReactionDocs.SuperItem + "#item-toggle")]
    [Serializable, AddTypeMenu("Malbers/Super Item/[Item] Toggle")]
    public class SIItemToggleReaction : SuperItemReactionBase
    {
        /// <summary> Switch the Reaction changes </summary>
        public enum SIItemToggleCommand
        {
            /// <summary> [Auto Equip]: a picked Item goes to the hands (ON) or to its Holster (OFF) </summary>
            AutoEquip,
            /// <summary> Interaction: the Item can be focused and picked by an Interactor </summary>
            Interactable,
            /// <summary> Physics: ON restores the Rigidbody and colliders, OFF freezes them (what equipping does) </summary>
            Physics,
            /// <summary> The Item's internal colliders (hit boxes and triggers) </summary>
            Colliders,
            /// <summary> The Item's GameObject </summary>
            GameObject,
        }

        /// <summary> Name shown on the Reaction row </summary>
        public override string DynamicName => $"[SI Item] {command} [{(value.Value ? "ON" : "OFF")}]";

        [Tooltip("Switch to change on the Item")]
        public SIItemToggleCommand command = SIItemToggleCommand.AutoEquip;

        [Tooltip("Value applied to the switch")]
        public BoolReference value = new(true);

        /// <summary> Applies the command to the Super Item </summary>
        protected override bool _TryReact(Component reactor)
        {
            if (reactor is not SuperItem item) return false;

            switch (command)
            {
                case SIItemToggleCommand.AutoEquip: item.AutoEquip = value.Value; break;
                case SIItemToggleCommand.Interactable: item.Active = value.Value; break;
                case SIItemToggleCommand.Physics:
                    if (value.Value) item.Physics_Restore(); else item.Physics_Disable();
                    break;
                case SIItemToggleCommand.Colliders: item.InternalColliders(value.Value); break;
                case SIItemToggleCommand.GameObject: item.gameObject.SetActive(value.Value); break;
                default: return false;
            }
            return true;
        }
    }
}
