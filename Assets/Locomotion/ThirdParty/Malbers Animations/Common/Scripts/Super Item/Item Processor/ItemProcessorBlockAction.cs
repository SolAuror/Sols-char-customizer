// Last Change: 9/18/2026 9:45am
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Items
{
    [System.Serializable, AddTypeMenu("Action/Block Actions")]
    [HelpURL(ItemDocs.Processors + "item-block-actions")]
    public class ItemProcessorBlockAction : ItemProcessor
    {
        [Tooltip("Actions to Block/Unblock on the OTHER Items held by the same character.\n\n" +
            "[Include ✓]: the Actions listed here.\n" +
            "[Exclude ✕]: every Action the other Items own EXCEPT the ones listed here.\n\n" +
            "An empty list does nothing.")]
        public IDListCheck<ItemActionID> actions = new();

        [Tooltip("ON: Block the Actions.\nOFF: Unblock them.\n\n" +
            "Blocks are tracked per owner (this Action): an Action blocked by several owners stays blocked until every one of them releases it.")]
        public BoolReference block = new(true);

        [Tooltip("Release the blocks this Action placed when the Action ends, its Set is swapped or the Item is unequipped.\n\n" +
            "OFF: the blocks stay until an [Unblock] Processor or Reaction removes them.")]
        public ReleaseAction release = ReleaseAction.OnEnd;

        /// <summary>Reused by every pass, so blocking/unblocking allocates nothing</summary>
        private static readonly List<ItemActionID> affected = new();

        /// <summary>Items this Processor blocked and has not released yet, so the release reaches an Item that was holstered meanwhile. Runtime only</summary>
        private List<SuperItem> blockedItems;

        public override string DynamicName => $"{(block.Value ? "Block" : "Unblock")} [{ActionNames}] in other Items";

        /// <summary>Names of the Actions this Processor affects, as the Inspector header shows them</summary>
        private string ActionNames
        {
            get
            {
                if (actions == null || actions.Empty) return "<MISSING>";

                var names = string.Empty;

                for (int i = 0; i < actions.Count; i++)
                {
                    var id = actions.IDs[i];
                    names += (i > 0 ? ", " : string.Empty) + (id != null ? id.name : "<MISSING>");
                }

                return actions.include ? names : $"All but {names}";
            }
        }

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (item == null || item.User == null || actions == null || actions.Empty) return;

            foreach (var p in item.User.D_EquipPoints)
            {
                var point = p.Value;

                //Don't touch empty Equip Points, nor the Item that owns this Processor
                if (point == null || !point.HasItem || point.Item == item) continue;

                Apply(point.Item, action, block.Value);
            }
        }

        /// <summary>Blocks or unblocks every matching Action on the other Item. The owning Action is the owner of the blocks</summary>
        private void Apply(SuperItem other, ItemAction owner, bool blockIt)
        {
            CollectAffected(other);
            if (affected.Count == 0) return;

            foreach (var id in affected)
            {
                if (blockIt) other.BlockedActions.Block(id, owner);
                else other.BlockedActions.Unblock(id, owner);
            }

            if (blockIt)
            {
                blockedItems ??= new List<SuperItem>(2);
                if (!blockedItems.Contains(other)) blockedItems.Add(other);
            }
        }

        /// <summary>Releases the blocks this Processor placed, on every Item it touched (equipped or not)</summary>
        private void Release(ItemAction itemAction)
        {
            if (blockedItems == null || blockedItems.Count == 0) return;

            foreach (var other in blockedItems)
            {
                if (other == null) continue;

                CollectAffected(other);
                foreach (var id in affected) other.BlockedActions.Unblock(id, itemAction);
            }

            blockedItems.Clear();
        }

        /// <summary>Auto release: the blocks must not outlive the Action that placed them</summary>
        private void ReleaseOnEnd(ItemAction itemAction)
        {
            if (block.Value && release != ReleaseAction.None) Release(itemAction);
        }

        public override void ActionEnd(SuperItem item, ItemActionSet set, ItemAction itemAction)
        {
            if (block.Value && release == ReleaseAction.OnEnd) Release(itemAction);
        }

        public override void ActionReleased(SuperItem item, ItemActionSet set, ItemAction itemAction)
        {
            if (block.Value && release == ReleaseAction.OnRelease) Release(itemAction);
        }

        public override void SetExit(SuperItem item, ItemActionSet set, ItemAction itemAction) => ReleaseOnEnd(itemAction);

        public override void ItemUnequipped(SuperItem item, ItemActionSet set, ItemAction itemAction) => ReleaseOnEnd(itemAction);

        /// <summary>Fills the buffer with every Action ID owned by the given Item that this list matches (Include/Exclude applied)</summary>
        private void CollectAffected(SuperItem other)
        {
            affected.Clear();

            if (other.actionSets == null) return;

            foreach (var set in other.actionSets)
            {
                if (set == null || set.actions == null) continue;

                foreach (var a in set.actions)
                {
                    if (a == null || a.ActionID == null) continue;

                    if (!actions.Check(a.ActionID)) continue;                   //respects the Include/Exclude toggle
                    if (!affected.Contains(a.ActionID)) affected.Add(a.ActionID); //the same ID can be used by several Sets
                }
            }
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
            => actions == null || actions.Empty
            ? $"<color=yellow><B>{item.name}:</B></color> No Item Actions to {(block.Value ? "block" : "unblock")}."
            : string.Empty;
    }
}
