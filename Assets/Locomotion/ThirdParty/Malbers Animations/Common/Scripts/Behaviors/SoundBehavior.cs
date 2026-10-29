using UnityEngine;
using System.Linq;

namespace MalbersAnimations
{
    public class SoundBehavior : StateMachineBehaviour
    {
        [Tooltip("Game Object to Store the Audio Source Component. This allows Animation States to share the same AudioSource")]
        public string m_source = "Animator Sounds";

        public AudioClip[] sounds;

        [Tooltip("Play the sound when the Animation Starts")]
        public bool playOnEnter = true;

        [Hide(nameof(playOnEnter))]
        [Tooltip("PlayOnEnter After the transition is over")]
        public bool SkipTransition = false;

        [Tooltip("Loop forever the sound")]
        public bool Loop = false;

        [Tooltip("Stop playing if the Animation exits")]
        public bool stopOnExit;

        [Hide("playOnEnter", true)]
        [Range(0, 1)]
        public float PlayOnTime = 0.5f;

        [Space]
        [MinMaxRange(-3, 3)] public RangedFloat pitch = new(1, 1);
        [MinMaxRange(0, 1)] public RangedFloat volume = new(1, 1);

        [Tooltip("How far the sound can be heard")]
        public float MaxDistance = 10f;

        [Tooltip("3D/2D blend (0 = 2D, 1 = 3D)")]
        [Range(0, 1)] public float spatialBlend = 1f;

        [Tooltip("Minimum time in seconds between repeated sound plays")]
        public float soundCooldown = 0f;

        private float lastPlayedTime = -999f;
        private bool played;

        private AudioSource _audio;
        private Transform audioTransform;

        private void CheckAudioSource(Animator animator)
        {
            if (audioTransform != null) return;

            var goName = string.IsNullOrEmpty(m_source) ? "Animator Sounds" : m_source;

            // Try find by direct child first
            audioTransform = animator.transform.Find(goName);

            // If not found, search in all children (like FindGrandChild)
            if (!audioTransform)
            {
                audioTransform = animator.transform
                    .GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == goName);
            }

            if (!audioTransform)
            {
                GameObject go = GameObject.Find(goName);

                if (!go)
                {
                    go = new GameObject(goName);
                    go.transform.SetParent(animator.transform);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                }

                audioTransform = go.transform;
            }

            _audio = audioTransform.GetComponent<AudioSource>();

            if (!_audio)
            {
                _audio = audioTransform.gameObject.AddComponent<AudioSource>();
            }

            _audio.spatialBlend = spatialBlend;
            _audio.maxDistance = MaxDistance;
            _audio.playOnAwake = false;
        }

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            CheckAudioSource(animator);
            played = false;

            if (playOnEnter && !SkipTransition)
                PlaySound();
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (played || animator.IsInTransition(layerIndex)) return;

            if (playOnEnter && SkipTransition)
            {
                PlaySound();
            }
            else if (stateInfo.normalizedTime > PlayOnTime)
            {
                PlaySound();
            }
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stopOnExit && _audio && !animator.IsInTransition(layerIndex))
            {
                _audio.Stop();
                _audio.clip = null;
            }
        }

        public virtual void PlaySound()
        {
            if (_audio == null || !_audio.enabled || sounds == null || sounds.Length == 0) return;

            if (Time.time - lastPlayedTime < soundCooldown) return;

            AudioClip clip = sounds[Random.Range(0, sounds.Length)];

            if (_audio.loop && clip == _audio.clip)
            {
                played = true;
                return;
            }

            if (_audio.isPlaying)
                _audio.Stop();

            _audio.clip = clip;

            if (clip != null)
            {
                _audio.pitch = pitch.RandomValue;
                _audio.volume = volume.RandomValue;
                _audio.loop = Loop;
                _audio.Play();

                lastPlayedTime = Time.time;
                played = true;
            }
        }
    }
}
