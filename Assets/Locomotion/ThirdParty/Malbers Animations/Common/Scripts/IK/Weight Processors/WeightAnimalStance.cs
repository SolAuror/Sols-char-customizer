using UnityEngine;

namespace MalbersAnimations.IK
{
    /// <summary>  Process the weight by checking the Look At Angle of the Animator / </summary>
    [System.Serializable, AddTypeMenu("Animal/Weight Stance")]
    public class WeightAnimalStance : WeightProcessor
    {
        public override string DynamicName
        {
            get
            {
                var hasMode = Stances.Count > 0;

                var result = "<Empty>";


                if (hasMode)
                {
                    result = $"{(Stances.include ? "Include" : "Exclude")}: ";

                    foreach (var ID in Stances.IDs)
                    {
                        result += $" {ID.name},";
                    }
                    result = result.TrimEnd(','); // Remove the last comma
                }

                return $"Weight Stance ({result})";
            }
        }

        [Tooltip("Start Weight for the stance")]
        public float StanceWeight = 0;

        [Tooltip("Stance to check if the animal is on. Weight will be set to 1")]
        public IDListCheck<StanceID> Stances = new();

        private ICharacterAction character;


        public override void OnEnable(IKSet set, Animator anim)
        {
            if (anim.TryGetComponent(out character))
                character.OnStance += OnStance;
            else
            {
                Active = false;
                Debug.LogWarning("The Stance weight processor requires an Animal Controller. Disabling it");
            }
        }

        private void OnStance(int newState)
        {
            StanceWeight = Stances.Check(newState) ? 1 : 0;
        }

        public override void OnDisable(IKSet set, Animator anim)
        {
            if (character != null) character.OnStance -= OnStance;

        }

        public override float Process(IKSet set, Animator Anim, float weight) => weight * StanceWeight;
    }
}
