using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Tasks/Wait")]
    public class WaitTask : MTask
    {
        public override string DisplayName => "General/Wait";

        [Space] 
        public FloatReference WaitMinTime = new(5);
        public FloatReference WaitMaxTime = new(5);

        [Tooltip("After the wait is over if this MAI State is not null. Execute it!")]
        public MAIState NextState;

        //MWC: Per-agent runtime state. Lives on the brain (not this shared SO) so each AI has its own wait time.
        private class WaitState : BrainState
        {
            public float WaitTime;      //Randomized wait duration for this run
        }

        public override void StartTask(MAnimalBrain brain, int index)
        {
            brain.TaskState<WaitState>(index).WaitTime = UnityEngine.Random.Range(WaitMinTime, WaitMaxTime);
        }

        public override void UpdateTask(MAnimalBrain brain, int index)
        {
            if (MTools.ElapsedTime(brain.TasksStartTime[index], brain.TaskState<WaitState>(index).WaitTime))
            {
                brain.TaskDone(index);
                if (NextState) brain.StartNewState(NextState); //Go to the next state if it has any
            }
        }
    }
}
