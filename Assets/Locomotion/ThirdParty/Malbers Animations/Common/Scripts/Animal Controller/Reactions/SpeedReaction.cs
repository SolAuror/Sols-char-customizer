// Last Change: 9/18/2026 9:45pm
using MalbersAnimations.Controller;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Animal/Speeds")]
    [HelpURL(ReactionDocs.Animal + "speeds")]
    public class SpeedReaction : MReaction
    {
        public enum Speed_Reaction
        { Activate, Increase, Decrease, LockCurrentSpeed, LockSpeed, TopSpeed, AnimationSpeed, GlobalAnimatorSpeed, SetRandomSpeed, Sprint, CanSprint }

        public Speed_Reaction type = Speed_Reaction.Activate;

        //Reads as the action it performs. Lock with Value = false is an Unlock, so it is named that way
        public override string DynamicName => type switch
        {
            Speed_Reaction.Activate => $"Animal Speed: Activate [{SpeedSet}, Index {Index}]",
            Speed_Reaction.Increase => "Animal Speed: Increase (Current Speed +1)",
            Speed_Reaction.Decrease => "Animal Speed: Decrease (Current Speed -1)",
            Speed_Reaction.LockCurrentSpeed => $"Animal Speed: {(Value ? "Lock" : "Unlock")} Current Speed",
            Speed_Reaction.LockSpeed => $"Animal Speed: {(Value ? "Lock" : "Unlock")} [{SpeedSet}, Index {Index}]",
            Speed_Reaction.TopSpeed => $"Animal Speed: Top Speed [{SpeedSet}, Index {Index}]",
            Speed_Reaction.AnimationSpeed => $"Animal Speed: Animator Speed [{SpeedSet}, Index {Index}] = [{animatorSpeed}]",
            Speed_Reaction.GlobalAnimatorSpeed => $"Animal Speed: Global Animator Speed = [{animatorSpeed}]",
            Speed_Reaction.SetRandomSpeed => $"Animal Speed: Random [{SpeedSet}]",
            Speed_Reaction.Sprint => $"Animal Speed: Sprint [{(Value ? "On" : "Off")}]",
            Speed_Reaction.CanSprint => $"Animal Speed: Can Sprint [{Value}]",
            _ => $"Animal Speed [{type}]",
        };



        [Hide(nameof(type),
            (int)Speed_Reaction.Activate,
            (int)Speed_Reaction.LockSpeed,
            (int)Speed_Reaction.TopSpeed,
            (int)Speed_Reaction.AnimationSpeed,
            (int)Speed_Reaction.SetRandomSpeed
            )]
        [Tooltip("Speed Set on the Animal to make the changes (E.g. 'Ground' 'Fly')")]
        public string SpeedSet = "Ground";


        [Hide(nameof(type),
          (int)Speed_Reaction.Activate,
            (int)Speed_Reaction.LockSpeed,
            (int)Speed_Reaction.LockCurrentSpeed,
            (int)Speed_Reaction.TopSpeed,
            (int)Speed_Reaction.AnimationSpeed)]
        [Tooltip("Index of the Speed Set on the Animal to make the changes (E.g. 'Walk-1' 'Trot-2', 'Run-3')")]
        public int Index = 1;

        [Hide(nameof(type),
         (int)Speed_Reaction.LockSpeed,
            (int)Speed_Reaction.LockCurrentSpeed,
            (int)Speed_Reaction.Sprint,
            (int)Speed_Reaction.CanSprint)]
        public bool Value = true;

        //  [Hide("showAnimSpeed")]
        [Hide(nameof(type), (int)Speed_Reaction.AnimationSpeed, (int)Speed_Reaction.GlobalAnimatorSpeed)]
        public float animatorSpeed = 1;

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;
            switch (type)
            {
                case Speed_Reaction.LockCurrentSpeed:
                    animal.Speed_Lock(Value);
                    break;
                case Speed_Reaction.LockSpeed:
                    animal.Speed_Lock(SpeedSet, Value, Index);
                    break;
                case Speed_Reaction.Increase:
                    animal.SpeedUp();
                    break;
                case Speed_Reaction.Decrease:
                    animal.SpeedDown();
                    break;
                case Speed_Reaction.Activate:
                    animal.SpeedSet_Set_Active(SpeedSet, Index);
                    break;
                case Speed_Reaction.TopSpeed:
                    animal.Speed_SetTopIndex(SpeedSet, Index);
                    break;
                case Speed_Reaction.AnimationSpeed:
                    var Set = animal.SpeedSet_Get(SpeedSet);
                    Set[Index - 1].animator.Value = animatorSpeed;
                    break;
                case Speed_Reaction.GlobalAnimatorSpeed:
                    animal.AnimatorSpeed = animatorSpeed;
                    break;
                case Speed_Reaction.SetRandomSpeed:
                    var topspeed = animal.SpeedSet_Get(SpeedSet);
                    if (topspeed != null)
                    {
                        var random = Random.Range(1, topspeed.TopIndex + 1);
                        // Debug.Log($"random: {random}");
                        animal.SpeedSet_Set_Active(SpeedSet, random);
                    }
                    break;
                case Speed_Reaction.Sprint:
                    animal.Sprint = Value;
                    break;
                case Speed_Reaction.CanSprint:
                    animal.CanSprint = Value;
                    break;
                default:
                    break;
            }
            return true;
        }
    }
}