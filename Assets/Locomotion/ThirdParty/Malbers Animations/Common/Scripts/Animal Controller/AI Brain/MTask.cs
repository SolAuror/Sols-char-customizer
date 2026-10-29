// Last Change: 9/24/2026 1:13pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    public abstract class MTask : BrainBase
    {
        /// <summary>What name will be displayed while adding a new Task</summary>
        public abstract string DisplayName { get; }

        [Tooltip("ID Used for sending messages to the Brain to see if the Task started")]
        public IntReference MessageID = new(0);
        [Tooltip("Task using Update will be executed every X seconds")]
        public FloatReference UpdateInterval = new(0.2f);
        [Tooltip("If the previous Task is done then this Task will start")]
        public bool WaitForPreviousTask = false;

        /// <summary>When a AI State starts using this Task, Run this Code</summary>
        public virtual void StartTask(MAnimalBrain brain, int index) { }


        /// <summary>Update Internal Task</summary>
        public virtual void InternalUpdateTask(MAnimalBrain brain, int index)
        {
            if (MTools.ElapsedTime(brain.TasksUpdateTime[index], UpdateInterval))
            {
                brain.TasksUpdateTime[index] = Time.time; //Reset the interval
                UpdateTask(brain, index);
                //Debug.Log($"brain --->  {brain.currentState.name} ---INDEXZ {index},   brain.TasksUpdateTime{brain.TasksUpdateTime.Length}");
            }
        }

        /// <summary>While a AI State is running using this Task, run thi Code</summary>
        public virtual void UpdateTask(MAnimalBrain brain, int index) { }

        /// <summary>While a AI State Ends, run this Code</summary>
        public virtual void ExitAIState(MAnimalBrain brain, int index) { brain.TaskDone(index); }

        /// <summary>Called when the AI Control arrives to its Target (AI Control On Target Arrived)</summary>
        public virtual void OnTargetArrived(MAnimalBrain brain, Transform target, int index) { }

        /// <summary>Called when the AI Control arrives to its destination position, with or without a Target (AI Control On Position Arrived)</summary>
        public virtual void OnPositionArrived(MAnimalBrain brain, Vector3 Position, int index) { }

        /// <summary>Called when this Animal enters a State</summary>
        public virtual void OnAnimalStateEnter(MAnimalBrain brain, State state, int index) { }
        /// <summary>Called when this Animal exits a State</summary>
        public virtual void OnAnimalStateExit(MAnimalBrain brain, State state, int index) { }
        /// <summary>Called when this Animal changes Stance</summary>
        public virtual void OnAnimalStanceChange(MAnimalBrain brain, int Stance, int index) { }
        /// <summary>Called when this Animal starts a Mode</summary>
        public virtual void OnAnimalModeStart(MAnimalBrain brain, Mode mode, int index) { }
        /// <summary>Called when this Animal ends a Mode. The mode passed is the one that ended</summary>
        public virtual void OnAnimalModeEnd(MAnimalBrain brain, Mode mode, int index) { }

        /// <summary>Called when the Target Animal (brain.TargetAnimal) enters a State</summary>
        public virtual void OnTargetAnimalStateEnter(MAnimalBrain brain, State state, int index) { }
        /// <summary>Called when the Target Animal (brain.TargetAnimal) exits a State</summary>
        public virtual void OnTargetAnimalStateExit(MAnimalBrain brain, State state, int index) { }
        /// <summary>Called when the Target Animal (brain.TargetAnimal) changes Stance</summary>
        public virtual void OnTargetAnimalStanceChange(MAnimalBrain brain, int Stance, int index) { }
        /// <summary>Called when the Target Animal (brain.TargetAnimal) starts a Mode</summary>
        public virtual void OnTargetAnimalModeStart(MAnimalBrain brain, Mode mode, int index) { }
        /// <summary>Called when the Target Animal (brain.TargetAnimal) ends a Mode. The mode passed is the one that ended</summary>
        public virtual void OnTargetAnimalModeEnd(MAnimalBrain brain, Mode mode, int index) { }
    }

    public abstract class BrainBase : ScriptableObject
    {
        [Tooltip("Enable disable Task or Decisions")]
        [HideInInspector] public bool active = true;
        [Space, TextArea(3, 10)]
        public string Description = "Type Description Here";
        public virtual void DrawGizmos(MAnimalBrain brain) { }
    }
}