using MalbersAnimations.Controller;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.IK
{
    /// <summary>  Process the weight by checking the Look At Angle of the Animator / </summary>
    [System.Serializable, AddTypeMenu("Animal/Weight State")]
    public class WeightAnimalState : WeightProcessor
    {
        public override string DynamicName
        {
            get
            {
                var hasMode = States.Count > 0;

                var result = "<Empty>";


                if (hasMode)
                {
                    result = $"{(States.include ? "Include" : "Exclude")}: ";

                    foreach (var ID in States.IDs)
                    {
                        result += $" {ID.name},";
                    }
                    result = result.TrimEnd(','); // Remove the last comma
                }

                return $"Weight State ({result})";
            }
        }


        [Tooltip("States to check if the animal is on any of them Weight will be set to 1")]
        public IDListCheck<StateID> States = new();
        [Tooltip("Profile to check on the State, by default is zero. Ignore if is -1")]
        public IntReference Profile = new();

        private MAnimal character;

        private float StateWeight = 0;
        public override void OnEnable(IKSet set, Animator anim)
        {
            if (anim.TryGetComponent(out character))
            {
                character.OnState += OnState;
                StateWeight = States.Check(character.ActiveStateID) ? 1 : 0;
            }
            else
            {
                Active = false;
                Debug.LogWarning("The State weight processor requires an Animal Controller. Disabling it");
            }
        }

        public override void OnDisable(IKSet set, Animator anim)
        {
            if (character != null) character.OnState -= OnState;
        }

        private void OnState(int newState)
        {
            StateWeight = States.Check(character.ActiveStateID) ? 1 : 0;
            //Check also the profile of the state
            if (Profile >= 0) StateWeight *= (character.activeState.StateProfile == Profile) ? 1 : 0;
        }

        public override float Process(IKSet set, Animator Anim, float weight) => weight * StateWeight;
    }
}
