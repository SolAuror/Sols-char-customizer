// Last Change: 9/24/2026 1:16pm
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Controller
{

    [AddComponentMenu("Malbers/Animal Controller/Mode Sequence")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/manimal-controller/mode-sequence")]
    public class ModeSequence : MonoBehaviour
    {
        [RequiredField] public MAnimal animal;

        [Tooltip("While the sequence is playing the animal cannot be controlled")]
        public bool disableControl = true;


        [Tooltip("The Sequence will be Play on Start. To play Manually call the method 'PlaySequence()' ")]
        public bool PlayOnStart = false;

        [Tooltip("Play a mode using the mode list. Use Combine Index  (Mode_ID * 1000 + Ability_Index) (See Animal Modes) ")]
        public List<int> sequence = new();

        private int index = 0;
        private bool playing;

        /// <summary>Next step of the sequence, deferred one frame from the Mode End callback</summary>
        private System.Collections.IEnumerator pendingStep;


        /// <summary>Plays the next Mode in the sequence (Mode_ID * 1000 + Ability_Index)</summary>
        public void PlaySequence()
        {
            pendingStep = null;

            var ModeID = sequence[index];
            var id = Mathf.Abs(ModeID / 1000);
            var ability = ModeID % 1000;

            if (id == 0) //A plain Mode ID (e.g. 4): play a random Ability
            {
                id = ModeID;
                ability = -99;
            }

            if (animal.Mode_TryActivate(id, ability))
            {
                index++;
                playing = true;
                if (disableControl) animal.Lock(true);
            }
            else
            {
                Debug.LogWarning($"The current Mode sequence {ModeID} cannot be played.");
                EndSequence();
            }
        }



        /// <summary>Plays the next step when the current Mode ends, or ends the sequence after the last step</summary>
        private void OnModeEnd(int mode, int ability)
        {
            if (playing && pendingStep == null)
            {
                if (index < sequence.Count)
                {
                    //Activating a Mode from inside the Mode End callback corrupts the Animal's Active/Last Mode. Wait one frame
                    pendingStep = this.Delay_Action(PlaySequence);
                    if (pendingStep == null) PlaySequence();
                }
                else //Means the sequence has finished
                {
                    EndSequence();
                }
            }
        }

        /// <summary>Stops the sequence and gives back the control of the Animal</summary>
        private void EndSequence()
        {
            this.Stop_Action(pendingStep);
            pendingStep = null;
            playing = false;
            index = 0;
            if (disableControl) animal.Lock(false);
        }

        /// <summary>Listens to the Animal Mode End event and plays the sequence if Play On Start is enabled</summary>
        private void OnEnable()
        {
            animal.OnModeEnd.AddListener(OnModeEnd);

            if (PlayOnStart) PlaySequence();
        }

        /// <summary>Stops listening to the Animal Mode End event</summary>
        private void OnDisable()
        {
            animal.OnModeEnd.RemoveListener(OnModeEnd);
            pendingStep = null; //Coroutines stop when the component is disabled
        }

        /// <summary>Finds the Animal on this GameObject or its parents</summary>
        private void Reset()
        {
            animal = this.FindComponent<MAnimal>();
        }

    }
}