using MalbersAnimations.Reactions;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Tasks/Reactions2 Task")]
    public class Reaction2Task : MTask
    {
        public override string DisplayName => "General/Reactions2";

        [Space, Tooltip("Apply the Task to the Animal(Self) or the Target(Target)")]
        public Affected affect = Affected.Self;

        [Tooltip("Send the message only when the AI is near the target. AI has Arrived")]
        public bool NearTarget = true;

        [Tooltip("Use the Interval value to repeat the reaction")]
        public bool repeat = false;

        [Tooltip("Reaction when the AI Task begin")]
        public Reaction2 startTaskReaction;

        [Tooltip("Reaction to repeat ")]
        [Hide("repeat")]
        public Reaction2 repeatTaskReaction;

        [Tooltip("Reaction when the AI State ends")]
        public Reaction2 endTaskReaction;

        //MWC: Per-agent runtime state. Lives on the brain (not this shared SO) so each AI caches its own reaction target.
        private class Reaction2State : BrainState
        {
            public Component Target;    //Cached component the reaction is applied to (Self or Target)
        }

        public override void StartTask(MAnimalBrain brain, int index)
        {
            //Cache the component to avoid calling GetComponent every time
            brain.TaskState<Reaction2State>(index).Target = (affect == Affected.Self ? brain.Animal : brain.Target);

            React(brain, index, startTaskReaction);

            if (!repeat)
                brain.TaskDone(index);
        }

        private void React(MAnimalBrain brain, int index, Reaction2 reaction)
        {
            if (!NearTarget || NearTarget && brain.AIControl.HasArrived)
                reaction.React(brain.TaskState<Reaction2State>(index).Target); //Get the cached component from the runtime state
        }


        public override void UpdateTask(MAnimalBrain brain, int index)
        {
            //repeat the reaction using the Update Interval (If Repeat is false) this will be skipped
            React(brain, index, repeatTaskReaction);
        }

        public override void ExitAIState(MAnimalBrain brain, int index)
        {
            React(brain, index, endTaskReaction);
        }

        private void Reset()
        => Description = "Add a Reaction to the Target or the Animal";

    }
}
