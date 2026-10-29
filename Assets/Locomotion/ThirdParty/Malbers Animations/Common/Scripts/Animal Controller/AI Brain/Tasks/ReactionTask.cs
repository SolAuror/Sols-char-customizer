using MalbersAnimations.Reactions;
using UnityEngine;
using UnityEngine.Serialization;

namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Tasks/Reaction Task")]
    public class ReactionTask : MTask
    {
        public override string DisplayName => "General/Reaction";

        [Space, Tooltip("Apply the Task to the Animal(Self) or the Target(Target)")]
        public Affected affect = Affected.Self;

        [Tooltip("Use the Interval value to repeat the reaction")]
        public bool repeat = false;

        [Tooltip("Reaction when the AI Task begin")]
        [FormerlySerializedAs("reaction")]
        [SerializeReference] public Reaction reactionOnEnter;

        [Tooltip("Reaction when the AI State ends")]
        [SerializeReference] public Reaction reactionOnExit;

        //MWC: Per-agent runtime state. Lives on the brain (not this shared SO) so each AI caches its own reaction target.
        private class ReactionState : BrainState
        {
            public Component Target;    //Cached component the reaction is applied to (Self or Target)
        }

        public override void StartTask(MAnimalBrain brain, int index)
        {
            //Cache the Component for the reaction here so its easy to find later
            brain.TaskState<ReactionState>(index).Target =
                reactionOnEnter.VerifyComponent((affect == Affected.Self ? brain.Animal : brain.Target));

            React(brain, index, reactionOnEnter);

            if (!repeat)
                brain.TaskDone(index);
        }

        private void React(MAnimalBrain brain, int index, Reaction reaction)
        {
            reaction?.React(brain.TaskState<ReactionState>(index).Target); //Get the cached component from the runtime state
        }


        public override void UpdateTask(MAnimalBrain brain, int index)
        {
            //repeat the reaction using the Update Interaval (If Repeat is false) this will be skipped
            React(brain, index, reactionOnEnter);
        }

        public override void ExitAIState(MAnimalBrain brain, int index)
        {
            React(brain, index, reactionOnExit);
        }

        private void Reset()
        => Description = "Add a Reaction to the Target or the Animal";

    }
}
