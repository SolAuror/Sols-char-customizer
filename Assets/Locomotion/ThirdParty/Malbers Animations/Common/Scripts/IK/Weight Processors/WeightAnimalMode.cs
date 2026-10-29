// Last Change: 9/24/2026 2:05pm
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.IK
{
    /// <summary>  Process the weight by checking the Look At Angle of the Animator / </summary>
    [System.Serializable, AddTypeMenu("Animal/Weight Mode")]
    public class WeightAnimalMode : WeightProcessor
    {
        public override string DynamicName
        {
            get
            {
                var hasMode = Modes.Count > 0;

                var result = ExcludeAllModes ? "Exclude All Modes" : "Include All Modes";

                if (hasMode)
                {
                    result = $"{(Modes.include ? "Include" : "Exclude")}: ";

                    foreach (var ID in Modes.IDs)
                    {
                        result += $" {ID.name},";
                    }
                    result = result.TrimEnd(','); // Remove the last comma

                }

                return $"Weight Mode ({result})";
            }
        }


        public bool ExcludeAllModes => Modes.Count == 0 && !Modes.include;
        public bool OnlyModes => Modes.Count == 0 && Modes.include;



        [Tooltip("List of Modes to check the weight." +
            "\nEmpty List + Include it will set the weight to 1 when a mode is playing." +
            "\nEmpty List + Exclude it will set the weight to 0 when a mode is playing.")]
        public IDListCheck<ModeID> Modes = new();

        [Tooltip("Exclude these abilities. Meaning if the Character is NOT on these abilities then the weight is set to 1")]
        public bool excludeAbilities = false;

        [Tooltip("Abilities to check if the animal is on. Weight will be set to 1")]
        public List<int> Abilities = new();

        private ICharacterAction character;

        /// <summary>Mode the character is playing (0 = none). The weight is derived from it, so it is right even before OnEnable runs</summary>
        private int activeMode = 0;

        /// <summary>Ability of the Mode the character is playing</summary>
        private int activeAbility = 0;

        /// <summary>Subscribes to the character Mode Start and Mode End callbacks</summary>
        public override void OnEnable(IKSet set, Animator anim)
        {
            activeMode = 0;
            activeAbility = 0;

            if (anim.TryGetComponent(out character))
            {
                character.ModeStart += OnModeStart;
                character.ModeEnd += OnModeEnd;
            }
            else
            {
                Active = false;
                Debug.LogWarning("The Mode weight processor requires an Animal Controller. Disabling it");
            }
        }

        /// <summary>Clears the tracked Mode, only if it is the one ending (the Animal fires the new Mode Start before the old Mode End)</summary>
        private void OnModeEnd(int mode, int ability)
        {
            if (mode == activeMode) activeMode = 0;
        }

        /// <summary>Tracks the Mode and Ability the character started</summary>
        private void OnModeStart(int mode, int ability)
        {
            activeMode = mode;
            activeAbility = ability;
        }

        /// <summary>Weight for the tracked Mode: 1 when no Mode is playing, unless the list is Include + Empty (only while a Mode plays)</summary>
        private float GetModeWeight()
        {
            if (activeMode == 0) return OnlyModes ? 0 : 1;  //No Mode is playing
            if (ExcludeAllModes) return 0;
            if (OnlyModes) return 1;

            float modeWeight = Modes.Check(activeMode) ? 1 : 0;

            if (Abilities.Count > 0) //Check for abilities too
            {
                modeWeight *= (Abilities.Contains(activeAbility) ? 1 : 0);
                if (excludeAbilities) modeWeight = 1 - modeWeight;
            }
            return modeWeight;
        }

        /// <summary>Unsubscribes from the character Mode Start and Mode End callbacks</summary>
        public override void OnDisable(IKSet set, Animator anim)
        {
            if (character != null)
            {
                character.ModeStart -= OnModeStart;
                character.ModeEnd -= OnModeEnd;
            }
        }

        /// <summary>Multiplies the entry weight by the weight of the Mode the character is playing</summary>
        public override float Process(IKSet set, Animator animator, float weight) => weight * GetModeWeight();
    }
}
