using MalbersAnimations.Scriptables;
using UnityEngine;


namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Wait", order = 201)]
    public class WaitDecision : MAIDecision
    {
        public override string DisplayName => "General/Wait";

        [Space]
        /// <summary>Range for Looking forward and Finding something</summary>
        public FloatReference WaitMinTime = new(5);
        public FloatReference WaitMaxTime = new(5);

        //MWC: Per-agent runtime state for this decision. Lives on the brain (not this shared SO), so every AI
        //using the same WaitDecision asset gets its own independent timer.
        private class WaitState : BrainState
        {
            public float WaitTime;      //Randomized wait duration for this run
            public float StartTime;     //Time.time captured when the wait actually begins (-1 = not started)
        }

        public override void PrepareDecision(MAnimalBrain brain, int Index)
        {
            var state = brain.DecisionState<WaitState>(Index);
            state.WaitTime = UnityEngine.Random.Range(WaitMinTime, WaitMaxTime);
            state.StartTime = -1f; //MWC: not started. Captured lazily on the first Decide() (after tasks, if WaitForAllTasks)
        }

        public override bool Decide(MAnimalBrain brain, int Index)
        {
            var state = brain.DecisionState<WaitState>(Index);

            //MWC: Capture a real start timestamp on the first evaluation. brain.DecisionsTime[Index] can't be used
            //as a start time: it is the interval accumulator, reset to 0 right before Decide() runs, which made
            //ElapsedTime measure from game start (Time.time >= WaitTime) instead of from when the wait began.
            if (state.StartTime < 0f) state.StartTime = Time.time;

            return MTools.ElapsedTime(state.StartTime, state.WaitTime);
        }
    }
}
