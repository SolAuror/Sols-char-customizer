// Last Change: 9/18/2026 9:35pm
using System;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Stats/Stat (Enable-Disable)")]
    [HelpURL(ReactionDocs.Stats + "stat-enable-disable")]
    public class StatEnableReaction : Reaction
    {
        //A single entry is spelled out ("Enable Stat [Health]"); several keep the total count
        public override string DynamicName => stats != null && stats.Length == 1 && stats[0] != null //IDPair is a class: null-safe
            ? $"{(stats[0].Value ? "Enable" : "Disable")} Stat [{(stats[0].ID != null ? stats[0].ID.name : "None")}]"
            : $"Enable/Disable Stats. Total [{stats?.Length ?? 0}]"; //Name of the Reaction

        public IDPair<StatID, bool>[] stats;

        public override Type ReactionType => typeof(Stats);

        protected override bool _TryReact(Component component)
        {
            var statM = component as Stats;

            foreach (var id in stats)
            {
                statM.Stat_Get(id.ID)?.SetActive(id.Value);
            }
            return true;
        }
    }
}
