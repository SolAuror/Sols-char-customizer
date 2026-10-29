using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Compare Stats", order = 4)]
    public class CompareStatsDecision : MAIDecision
    {
        public override string DisplayName => "General/Compare Stats";

        [Tooltip("Stats you want to find on the AI Animal")]
        public StatID OwnStat;
        [Tooltip("Compare values of the Stat")]
        public ComparerNumber compare = ComparerNumber.Less;
        [Tooltip("Stats you want to find on the Target")]
        public StatID TargetStat;

        public override bool Decide(MAnimalBrain brain, int index)
        {
            bool result = false;

            var OwnStats = brain.AnimalStats;
            var TargetStats = brain.TargetStats;

            if (OwnStats != null && TargetStats != null)
            {
                if (OwnStats.TryGetValue(OwnStat, out IMStat own) &&
                    TargetStats.TryGetValue(TargetStat, out IMStat target))
                {
                    return own.Value.MCompare(target.Value, compare);
                }
            }
            return result;
        }

        private void Reset() { Description = "Checks for a Stat value in the AI Animal and the Current Target, Compares the values using the condition"; }
    }

}
