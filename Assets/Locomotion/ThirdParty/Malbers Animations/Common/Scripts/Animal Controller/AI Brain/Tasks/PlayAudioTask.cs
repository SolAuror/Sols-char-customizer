using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Tasks/Play Audio")]
    public class PlayAudioTask : MTask
    {
        public override string DisplayName => "General/Play Audio";

        [Space]
        public AudioClipReference Clips;
        public string AudioSource = "BrainAudio";

        //MWC: Per-agent runtime state. Lives on the brain (not this shared SO) so each AI caches its own AudioSource.
        private class AudioState : BrainState
        {
            public AudioSource Source;      //The AudioSource used to play the clips for this agent
        }

        public override void StartTask(MAnimalBrain brain, int index)
        {
            var findAudio = brain.transform.FindGrandChild(AudioSource);

            if (!findAudio)
            {
                findAudio = new GameObject(name: AudioSource).transform;
                findAudio.parent = brain.transform;
            }

            if (!findAudio.TryGetComponent<AudioSource>(out var sourc))
                sourc = findAudio.gameObject.AddComponent<AudioSource>();

            brain.TaskState<AudioState>(index).Source = sourc; //Save the audio source to this task's runtime state

            Clips.Play(sourc);

            brain.TaskDone(index);
        }


        void Reset()
        { 
            Description = 
                "Plays an Audioclip in the Audio Source. If there's no Audio Source with the name assigned []. I will add a new Audio Source Component "; 
        }
    }
}
