// Last Change: 9/17/2026 5:33pm
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using UnityEngine.Audio;
using MalbersAnimations.Scriptables;


#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    [AddComponentMenu("Malbers/Utilities/Effects - Audio/Animator Event Sound")]
    [DisallowMultipleComponent]
    public class AnimatorEventSounds : MonoBehaviour, IAnimatorListener
    {
        public static List<AnimatorEventSounds> AnimEventSounds;

        /// <summary>Registry of all the active AnimatorEventSounds grouped by the Animator they belong to.</summary>
        private static readonly Dictionary<Animator, List<AnimatorEventSounds>> Registry = new();

        /// <summary>Increases every time the Registry changes, so cached lookups know when to revalidate.</summary>
        public static int Version { get; private set; }

        /// <summary>Identifier used to filter which AnimatorEventSounds should play a sound.</summary>
        [Tooltip("Identifier used to filter which AnimatorEventSounds should play a sound.\n" +
                 "E.g. 0 = Character Body, 1 = Equipped Weapon, 2 = Holstered Weapon.\n" +
                 "Used by the [Animator Event Sound Behaviour] StateMachineBehaviour")]
        public IntReference Index = new(0);

        public List<EventSound> m_EventSound;
        public AudioSource _audioSource;
        protected Animator anim;

        public bool debug;

        /// <summary>Animator this component is currently linked to (found on this GameObject or any of its parents).</summary>
        public Animator Anim => anim;

        /// <summary>Animator this component was registered on, kept so it can unregister from the right list.</summary>
        private Animator registeredOn;

        /// <summary>Clears the static caches when entering Play Mode with Domain Reload disabled.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registry.Clear();
            AnimEventSounds?.Clear();
            Version = 0;
        }

        void Awake() // MWC - moved from Start() so the AudioSource is ready before any Animator callback
        {
            if (_audioSource == null)                           //if there's no audio source add one..
                _audioSource = gameObject.AddComponent<AudioSource>();

            _audioSource.volume = 0;

            foreach (var item in m_EventSound)
            {
                item.VolumeWeight = 1f;
            }
        }

        private void OnEnable()
        {
            AnimEventSounds ??= new();
            AnimEventSounds.Add(this);
            Register();
        }

        private void OnDisable()
        {
            AnimEventSounds?.Remove(this);
            UnRegister();
        }

        private void OnTransformParentChanged()
        {
            if (!isActiveAndEnabled) return; // MWC - OnEnable will take care of the registration when it comes back

            UnRegister();                    // MWC - leave the old Animator
            Register();                      // MWC - join the new Animator and bump the Version
        }

        /// <summary>Finds the Animator on this hierarchy and adds this component to its Registry list.</summary>
        private void Register()
        {
            anim = GetComponentInParent<Animator>(true);
            registeredOn = anim;

            if (registeredOn == null) return;

            if (!Registry.TryGetValue(registeredOn, out var list))
                Registry[registeredOn] = list = new List<AnimatorEventSounds>();

            if (!list.Contains(this)) list.Add(this);

            Version++;

            if (debug) Debug.Log($"[{name}] Registered on Animator [{registeredOn.name}] with Index [{Index}]", this);
        }

        /// <summary>Removes this component from the Registry list of the Animator it joined.</summary>
        private void UnRegister()
        {
            if (registeredOn != null && Registry.TryGetValue(registeredOn, out var list))
            {
                list.Remove(this);
                if (list.Count == 0) Registry.Remove(registeredOn);
                Version++;
            }

            registeredOn = null;
        }

        /// <summary>All the active AnimatorEventSounds linked to an Animator. Returns null when there are none.</summary>
        public static List<AnimatorEventSounds> GetAll(Animator animator)
            => animator != null && Registry.TryGetValue(animator, out var list) ? list : null;

        /// <summary>First active AnimatorEventSounds linked to an Animator that matches an Index. Null when not found.</summary>
        public static AnimatorEventSounds Get(Animator animator, int index)
        {
            var list = GetAll(animator);

            if (list == null) return null;

            for (int i = 0; i < list.Count; i++)
                if (list[i].Index == index) return list[i];

            return null;
        }

        /// <summary>Changes the Index at runtime (e.g. when a weapon is equipped or holstered).</summary>
        public virtual void SetIndex(int index) => Index = index;


        public virtual void DisableSoundEvent(string SoundName)
        {
            EventSound SoundEvent = m_EventSound.Find(item => item.name == SoundName);
            if (SoundEvent != null) SoundEvent.active = false;
        }

        public virtual void EnableSoundEvent(string SoundName)
        {
            EventSound SoundEvent = m_EventSound.Find(item => item.name == SoundName);
            if (SoundEvent != null) SoundEvent.active = true;
        }

        public virtual void PlaySound(AnimationEvent e)
        {
            if (e.animatorClipInfo.weight < 0.1) return; // if is too small the weight of the animation clip do nothing

            if (debug) Debug.Log($"Play Audio: Clip - [{e.animatorClipInfo.clip.name}]", e.animatorClipInfo.clip);

            var SoundEvent = m_EventSound.Find(item => item.name == e.stringParameter && item.active == true);

            if (SoundEvent != null)
            {
                SoundEvent.VolumeWeight = e.animatorClipInfo.weight;

                if (anim) _audioSource.pitch = anim.speed;                     //Match the AnimatorSpeed with the Sound Pitch

                if (_audioSource.isPlaying)                                    //If the Audio is already Playing play the one that has more weight
                {
                    if (SoundEvent.VolumeWeight * SoundEvent.volume > _audioSource.volume)
                    {
                        SoundEvent.PlayAudio(_audioSource);
                    }
                }
                else
                {
                    SoundEvent.PlayAudio(_audioSource);
                }
            }
        }

        public virtual void PlaySound(string sound)
        {
            var SoundEvent = m_EventSound.Find(item => item.name == sound && item.active == true);

            if (SoundEvent != null)
            {
                if (debug) Debug.Log($"[{name}] - Play Audio: [{sound}]", _audioSource);
                SoundEvent.PlayAudio(_audioSource);
            }
        }

        public virtual void PlaySoundForever(string sound)
        {
            var SoundEvent = m_EventSound.Find(item => item.name == sound && item.active == true);

            if (SoundEvent != null)
            {
                SoundEvent.VolumeWeight = 1;

                StartCoroutine(C_Playforever(SoundEvent));
            }
        }

        public virtual void StopPlaying(string sound)
        {
            var SoundEvent = m_EventSound.Find(item => item.name == sound && item.active == true);

            if (SoundEvent != null && SoundEvent.source != null)
            {
                if (SoundEvent.source.isPlaying)
                    SoundEvent.source.Stop();
            }

            StopAllCoroutines();
        }

        IEnumerator C_Playforever(EventSound E_sound)
        {
            if (E_sound.interval <= 0)
            {
                yield return null;
            }
            else
            {
                var timeInterval = new WaitForSeconds(E_sound.interval);
                while (true)
                {
                    E_sound.PlayAudio(_audioSource);
                    yield return timeInterval;
                }
            }

            yield return null;
        }


        public virtual bool OnAnimatorBehaviourMessage(string message, object value) => this.InvokeWithParams(message, value);
    }

    [System.Serializable]
    public class EventSound
    {
        [Tooltip("Name of the Sound you will call on the Animation Event")]
        public string name = "Name Here";

        public float volume = 1;

        public float pitch = 1;
        public bool active = true;
        [Tooltip("Interval Time to play a sound forever. Remember to call [PlaySoundForever]")]
        public float interval = 0f;
        public AudioSource source;
        public AudioResource[] Clips;

        /// <summary>Weight of the sound depeding of the Animation Weight</summary>
        public float VolumeWeight { get; internal set; }

        public void PlayAudio(AudioSource audio)
        {
            if (source == null) source = audio;
            if (source == null) return;                             //Do nothing if the audio is empty
            if (Clips == null || Clips.Length == 0) return;         //Do nothing if there's no clips 

            source.spatialBlend = 1;                                 //Set the sound to 3D

            var clip = Clips[Random.Range(0, Clips.Length)];

            source.resource = clip; //Set a random clip to the audio Source
            source.pitch = pitch;                                   //Depending the animator speed modify the pitch
            source.volume = Mathf.Clamp01(volume * VolumeWeight);   //Depending the weight of the animation clip modify the volume
            source.Play();                                          //Play the Audio
        }
    }



#if UNITY_EDITOR
    [CanEditMultipleObjects, CustomEditor(typeof(AnimatorEventSounds))]
    public class AnimEventSoundEditor : Editor
    {
        private ReorderableList list;
        private SerializedProperty m_EventSound, debug;
        private AnimatorEventSounds M;

        protected virtual void OnEnable()
        {
            M = ((AnimatorEventSounds)target);

            m_EventSound = serializedObject.FindProperty("m_EventSound");
            debug = serializedObject.FindProperty("debug");

            list = new ReorderableList(serializedObject, m_EventSound, true, true, true, true)
            {
                drawElementCallback = DrawElementCallback,
                drawHeaderCallback = HeaderCallbackDelegate,
                onAddCallback = OnAddCallBack
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Play animations events from clips using PlaySound(string Name)");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_audioSource"), new GUIContent("Global Source", "AudioSource"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Index"), new GUIContent("Index", "Identifier used by the [Animator Event Sound Behaviour] to filter which AnimatorEventSounds to play"));
            }

            EditorGUI.BeginChangeCheck();
            {
                // EditorGUILayout.BeginVertical(MTools.StyleGray);
                {
                    list.DoLayoutList();

                    if (list.index != -1)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        {
                            SerializedProperty Element = m_EventSound.GetArrayElementAtIndex(list.index);
                            EditorGUILayout.LabelField("[" + M.m_EventSound[list.index].name + "]", EditorStyles.boldLabel);
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(Element.FindPropertyRelative("interval"));
                            EditorGUILayout.PropertyField(Element.FindPropertyRelative("Clips"), new GUIContent("Clips", "AudioClips"), true);
                            EditorGUILayout.PropertyField(Element.FindPropertyRelative("source"), new GUIContent("Internal Source", "AudioSource"));
                            EditorGUI.indentLevel--;
                        }
                        EditorGUILayout.EndVertical();
                    }


                }
                //  EditorGUILayout.EndVertical();
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Animation Event Sound");
                //  EditorUtility.SetDirty(target);
            }
            serializedObject.ApplyModifiedProperties();
        }

        void HeaderCallbackDelegate(Rect rect)
        {
            Rect R_1 = new(rect.x + 28, rect.y, (rect.width) / 3 + 25, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(R_1, "Name");

            Rect R_2 = new(rect.x + (rect.width) / 3 + 65, rect.y, (rect.width) / 3, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(R_2, "Volume");

            Rect R_3 = new(rect.x + ((rect.width) / 3) * 2 + 40, rect.y, ((rect.width) / 3), EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(R_3, "Pitch");


            Rect R_4 = new(rect.width, rect.y, 25, EditorGUIUtility.singleLineHeight);
            MalbersEditor.DrawDebugIcon(R_4, debug);

        }

        void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = m_EventSound.GetArrayElementAtIndex(index);

            var name = element.FindPropertyRelative("name");
            var active = element.FindPropertyRelative("active");
            var volume = element.FindPropertyRelative("volume");
            var pitch = element.FindPropertyRelative("pitch");
            rect.y += 2;

            Rect R_0 = new(rect.x - 3, rect.y, 10, EditorGUIUtility.singleLineHeight);
            Rect R_1 = new(rect.x + 15, rect.y, (rect.width) / 3 + 55 - 15, EditorGUIUtility.singleLineHeight);
            Rect R_2 = new(rect.x + (rect.width) / 3 + 61, rect.y, (rect.width) / 3 - 30, EditorGUIUtility.singleLineHeight);
            Rect R_3 = new(rect.x + ((rect.width) / 3) * 2 + 35, rect.y, ((rect.width) / 3) - 35, EditorGUIUtility.singleLineHeight);

            EditorGUI.PropertyField(R_0, active, GUIContent.none);
            EditorGUI.PropertyField(R_1, name, GUIContent.none);
            EditorGUI.PropertyField(R_2, volume, GUIContent.none);
            EditorGUI.PropertyField(R_3, pitch, GUIContent.none);
        }

        void OnAddCallBack(ReorderableList list)
        {
            if (M.m_EventSound == null)
            {
                M.m_EventSound = new System.Collections.Generic.List<EventSound>();
            }
            M.m_EventSound.Add(new EventSound());
        }
    }
#endif

}