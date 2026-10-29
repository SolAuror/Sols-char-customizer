// Last Change: 9/18/2026 9:45am
using System.Collections.Generic;
using System.Text;

namespace MalbersAnimations.Items
{
    /// <summary>
    /// Item Actions blocked on a Super Item, tracked by OWNER. An Action stays blocked while at least one owner still
    /// blocks it, and the same owner never counts twice, so overlapping blockers cannot free each other's block
    /// (the Shield's Block and Parry both block the Sword attacks: the attacks come back only when BOTH let go) and
    /// a repeated Block by the same owner cannot leave a stray count behind.
    /// Owners are the ItemAction whose Block Actions Processor placed the block, or <see cref="ReactionOwner"/> for Reactions.
    /// </summary>
    public class BlockedActionSet
    {
        /// <summary> Owners blocking each Action ID. An ID is present only while it has at least one owner </summary>
        private readonly Dictionary<ItemActionID, List<object>> blocks = new();

        /// <summary> Scratch list for <see cref="UnblockAll"/>, so releasing allocates nothing </summary>
        private static readonly List<ItemActionID> emptied = new();

        /// <summary> Shared owner used by the SI Item Action Reactions (Block/Unblock), which have no Action of their own </summary>
        public static readonly object ReactionOwner = "Reaction";

        /// <summary> Number of blocked Action IDs </summary>
        public int Count => blocks.Count;

        /// <summary> Blocked Action IDs (the ones with at least one owner) </summary>
        public IEnumerable<ItemActionID> IDs => blocks.Keys;

        /// <summary> True while at least one owner blocks the Action </summary>
        public bool IsBlocked(ItemActionID id) => id != null && blocks.ContainsKey(id);

        /// <summary> Owners currently blocking the Action, or null when it is not blocked </summary>
        public IReadOnlyList<object> Owners(ItemActionID id) => id != null && blocks.TryGetValue(id, out var owners) ? owners : null;

        /// <summary> Blocks the Action for the given owner. Blocking it again with the same owner does nothing </summary>
        public void Block(ItemActionID id, object owner)
        {
            if (id == null || owner == null) return;

            if (!blocks.TryGetValue(id, out var owners)) blocks[id] = owners = new List<object>(2);
            if (!owners.Contains(owner)) owners.Add(owner);
        }

        /// <summary> Removes the owner's block on the Action. The Action is unblocked once no owner is left. True if the owner was blocking it </summary>
        public bool Unblock(ItemActionID id, object owner)
        {
            if (id == null || owner == null || !blocks.TryGetValue(id, out var owners)) return false;
            if (!owners.Remove(owner)) return false;

            if (owners.Count == 0) blocks.Remove(id);
            return true;
        }

        /// <summary> Removes every block placed by the owner, whatever the Action </summary>
        public void UnblockAll(object owner)
        {
            if (owner == null || blocks.Count == 0) return;

            emptied.Clear();

            foreach (var pair in blocks)
                if (pair.Value.Remove(owner) && pair.Value.Count == 0) emptied.Add(pair.Key);

            foreach (var id in emptied) blocks.Remove(id);
        }

        /// <summary> Removes every block, whoever placed it </summary>
        public void Clear() => blocks.Clear();

        /// <summary> Debug text: "Attack Light (Block, Parry), Attack Heavy (Parry)", or "None" </summary>
        public override string ToString()
        {
            if (blocks.Count == 0) return "None";

            var sb = new StringBuilder();

            foreach (var pair in blocks)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(pair.Key != null ? pair.Key.name : "null").Append(" (");

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(OwnerName(pair.Value[i]));
                }

                sb.Append(')');
            }

            return sb.ToString();
        }

        /// <summary> Readable name of an owner: the Action's name, a Unity Object's name, or its ToString </summary>
        private static string OwnerName(object owner) => owner switch
        {
            ItemAction a => a.Name,
            UnityEngine.Object o => o != null ? o.name : "null",
            null => "null",
            _ => owner.ToString(),
        };
    }
}
