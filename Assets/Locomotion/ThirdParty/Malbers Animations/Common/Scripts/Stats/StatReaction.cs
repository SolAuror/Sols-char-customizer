// Last Change: 9/18/2026 9:25pm
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Stats/Stat Modify")]
    [HelpURL(ReactionDocs.Stats + "stat-modify")]
    public class StatReaction : Reaction
    {
        //A single modifier is spelled out (Stat, operation and value); several keep the total count
        public override string DynamicName => modifiers != null && modifiers.Count == 1
            ? $"Modify Stat: {Describe(modifiers[0])}"
            : $"Modify Stats. Total [{modifiers?.Count ?? 0}]"; //Name of the Reaction

        /// <summary>Human readable line for one modifier. E.g. "[Health] - [15-20]", "[Stamina] Regenerate Off"</summary>
        private static string Describe(StatModifier m)
        {
            var stat = $"[{(m.ID != null ? m.ID.name : "None")}]";

            return m.modify switch
            {
                StatOption.AddValue => $"{stat} + {Value(m)}",
                StatOption.SubstractValue => $"{stat} - {Value(m)}",
                StatOption.SetValue => $"{stat} = {Value(m)}",
                StatOption.ModifyMaxValue => $"{stat} Max + {Value(m)}",
                StatOption.SetMaxValue => $"{stat} Max = {Value(m)}",
                StatOption.ReduceByPercent => $"{stat} - {Value(m)}%",
                StatOption.IncreaseByPercent => $"{stat} + {Value(m)}%",
                StatOption.Multiplier => $"{stat} Multiplier = {Value(m)}",
                StatOption.Degenerate => $"{stat} Degenerate {Value(m)}",
                StatOption.Regenerate => $"{stat} Regenerate {Value(m)}",
                StatOption.Enable => $"{stat} Enable [{m.enable?.Value ?? false}]",
                StatOption.Inmune => $"{stat} Immune [{m.enable?.Value ?? false}]",
                StatOption.Reset => $"{stat} Reset",
                StatOption.ResetToMax => $"{stat} Reset to Max",
                StatOption.ResetToMin => $"{stat} Reset to Min",
                StatOption.RegenerateOn => $"{stat} Regenerate On",
                StatOption.RegenerateOff => $"{stat} Regenerate Off",
                StatOption.DegenerateOn => $"{stat} Degenerate On",
                StatOption.DegenerateOff => $"{stat} Degenerate Off",
                StatOption.RestoreRegeneration => $"{stat} Regenerate Default",
                StatOption.RestoreDegeneration => $"{stat} Degenerate Default",
                StatOption.RestoreValue => $"{stat} Value Default",
                StatOption.RestoreMax => $"{stat} Max Default",
                StatOption.None => $"{stat} None",
                _ => $"{stat} {m.modify}",
            };
        }

        /// <summary>Value part of the description: the Source expression, a single number, or a [Min-Max] range</summary>
        private static string Value(StatModifier m)
        {
            if (m.from == StatValueFrom.Stat) return m.SourceExpression;

            float min = m.MinValue?.Value ?? 0f, max = m.MaxValue?.Value ?? 0f; //null-safe: the references are classes
            return Mathf.Approximately(min, max) ? $"[{min:0.##}]" : $"[{min:0.##}-{max:0.##}]";
        }


        public List<StatModifier> modifiers = new()
        {
            new StatModifier()
            {
                 modify = StatOption.SubstractValue, MinValue = 15, MaxValue = 20,
            },
        };

        public override System.Type ReactionType => typeof(Stats);

        protected override bool _TryReact(Component reactor)
        {
            var stats = reactor as Stats;

            foreach (var modifier in modifiers)
            {
                modifier.ModifyStat(stats);
            }

            return true;
        }
    }
}
