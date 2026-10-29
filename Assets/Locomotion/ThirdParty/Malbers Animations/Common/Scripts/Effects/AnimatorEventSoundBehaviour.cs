// Last Change: 9/17/2026 6:05pm
using System.Collections.Generic;
using UnityEngine;
using MalbersAnimations.Scriptables;

#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    /// <summary>StateMachineBehaviour that plays [Event Sounds] by name on an AnimatorEventSounds filtered by Index.</summary>
    [AddComponentMenu("Malbers/Utilities/Effects - Audio/Animator Event Sound Behaviour")]
    public class AnimatorEventSoundBehaviour : StateMachineBehaviour
    {
        /// <summary>Sounds to play while this Animation State is active.</summary>
        [Tooltip("Sounds to play while this Animation State is active")]
        public AnimEventSoundTime[] sounds;

        /// <summary>Replays the sounds on every loop cycle of the animation.</summary>
        [Tooltip("Replays the sounds on every loop cycle of the animation. Use it on looping states (Walk, Run...)")]
        public bool Loop = false;

        /// <summary>Logs on the console which AnimatorEventSounds received the sound.</summary>
        public bool debug;

        /// <summary>Cached AnimatorEventSounds registered on the Animator playing this state.</summary>
        private List<AnimatorEventSounds> cache;

        /// <summary>Animator the cache was built for.</summary>
        private Animator cacheAnimator;

        /// <summary>Registry Version the cache was built with. When it changes the cache is rebuilt.</summary>
        private int cacheVersion = -1;

        /// <summary>Last animation loop cycle processed. Used to replay the sounds when [Loop] is enabled.</summary>
        private int lastCycle;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (sounds == null || sounds.Length == 0) return;

            lastCycle = 0;

            for (int i = 0; i < sounds.Length; i++)
            {
                var s = sounds[i];
                s.sent = false;

                if (s.active && s.time <= 0)    //Play right away the ones with no time
                {
                    s.sent = true;
                    Play(animator, s);
                }
            }
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (sounds == null || sounds.Length == 0) return;

            //Means the State is transitioning to itself, OnStateEnter will reset everything
            if (stateInfo.fullPathHash == animator.GetNextAnimatorStateInfo(layerIndex).fullPathHash) return;

            var normalized = stateInfo.normalizedTime;

            if (Loop)
            {
                var cycle = Mathf.FloorToInt(normalized);

                if (cycle > lastCycle)          //A new animation cycle started, allow the sounds to play again
                {
                    lastCycle = cycle;
                    for (int i = 0; i < sounds.Length; i++) sounds[i].sent = false;
                }

                normalized -= cycle;            //Keep it on the 0-1 range
            }

            for (int i = 0; i < sounds.Length; i++)
            {
                var s = sounds[i];

                if (!s.active || s.sent || normalized < s.time) continue;

                s.sent = true;
                Play(animator, s);
            }
        }

        /// <summary>Plays a sound on every AnimatorEventSounds of the Animator that matches the Index.</summary>
        private void Play(Animator animator, AnimEventSoundTime sound)
        {
            CheckCache(animator);

            var soundName = sound.name.Value; // MWC - StringReference: Local value or Global StringVar

            if (cache == null)
            {
                if (debug) Debug.LogWarning($"[{animator.name}] has no [Animator Event Sounds] registered. Skipping [{soundName}]", animator);
                return;
            }

            var found = false;

            for (int i = 0; i < cache.Count; i++)
            {
                var aes = cache[i];

                if (aes == null || aes.Index != sound.Index) continue;

                aes.PlaySound(soundName);
                found = true;

                if (debug) Debug.Log($"<B>[{animator.name}]</B> → Play Event Sound [{soundName}] on <B>[{aes.name}]</B> (Index {sound.Index})", aes);
            }

            if (!found && debug)
                Debug.LogWarning($"[{animator.name}] has no [Animator Event Sounds] with Index [{sound.Index}]. Skipping [{soundName}]", animator);
        }

        /// <summary>Refreshes the cached list only when the Animator changed or a new AnimatorEventSounds was (un)registered.</summary>
        private void CheckCache(Animator animator)
        {
            if (cacheAnimator == animator && cacheVersion == AnimatorEventSounds.Version) return;

            cacheAnimator = animator;
            cacheVersion = AnimatorEventSounds.Version;
            cache = AnimatorEventSounds.GetAll(animator);
        }
    }

    /// <summary>An [Event Sound] name to play at a normalized time, on the AnimatorEventSounds matching an Index.</summary>
    [System.Serializable]
    public class AnimEventSoundTime
    {
        /// <summary>Enables/Disables this entry.</summary>
        public bool active = true;

        /// <summary>Name of the [Event Sound] to play. It must exist on the AnimatorEventSounds list.</summary>
        [Tooltip("Name of the Event Sound to play. It must exist on the AnimatorEventSounds list")]
        public StringReference name = new("Sound Name");

        /// <summary>Normalized time of the animation to play the sound.</summary>
        [Tooltip("Normalized time of the animation to play the sound")]
        [Range(0, 1)] public float time = 0f;

        /// <summary>Index of the AnimatorEventSounds that should play this sound.</summary>
        [Tooltip("Index of the AnimatorEventSounds that should play this sound. E.g. 0 = Character, 1 = Equipped Weapon")]
        public int Index = 0;

        /// <summary>True when the sound was already played on the current animation cycle.</summary>
        [System.NonSerialized] public bool sent;
    }

    //INSPECTOR

#if UNITY_EDITOR
    [CustomEditor(typeof(AnimatorEventSoundBehaviour))]
    public class AnimatorEventSoundBehaviourEditor : Editor
    {
        private ReorderableList list;
        private SerializedProperty sounds, Loop, debug;

        private void OnEnable()
        {
            sounds = serializedObject.FindProperty("sounds");
            Loop = serializedObject.FindProperty("Loop");
            debug = serializedObject.FindProperty("debug");

            list = new ReorderableList(serializedObject, sounds, true, true, true, true)
            {
                drawElementCallback = DrawElementCallback,
                drawHeaderCallback = HeaderCallbackDelegate,
                elementHeightCallback = ElementHeightCallback,
                onAddCallback = OnAddCallBack
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Play [Sounds] by name on an [Animator Event Sounds]");

            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(Loop);
                MalbersEditor.DrawDebugIcon(debug);
            }

            list.DoLayoutList();

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Column titles of the sounds list.</summary>
        void HeaderCallbackDelegate(Rect rect)
        {
            GetRects(HeaderToElement(rect), out _, out Rect R_1, out Rect R_2, out Rect R_3);

            EditorGUI.LabelField(R_1, new GUIContent("Sound Name", "Name of the Event Sound. It must exist on the AnimatorEventSounds list"));
            EditorGUI.LabelField(R_2, new GUIContent("Time", "Normalized time of the animation"));
            EditorGUI.LabelField(R_3, new GUIContent("Index", "Index of the AnimatorEventSounds that plays this sound"));
        }

        /// <summary>One row of the sounds list.</summary>
        void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = sounds.GetArrayElementAtIndex(index);
            rect.y += 2;

            GetRects(rect, out Rect R_0, out Rect R_1, out Rect R_2, out Rect R_3);

            EditorGUI.PropertyField(R_0, element.FindPropertyRelative("active"), GUIContent.none);

            //MWC — the StringReference drawer prefixes its own label, so squeeze the label column to nothing
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 0.1f;
            EditorGUI.PropertyField(R_1, element.FindPropertyRelative("name"), GUIContent.none);
            EditorGUIUtility.labelWidth = labelWidth;

            var time = element.FindPropertyRelative("time");
            time.floatValue = EditorGUI.Slider(R_2, GUIContent.none, time.floatValue, 0, 1);

            EditorGUI.PropertyField(R_3, element.FindPropertyRelative("Index"), GUIContent.none);
        }

        /// <summary>Row height. The StringReference grows when it is set to [Use Reference].</summary>
        float ElementHeightCallback(int index)
        {
            var name = sounds.GetArrayElementAtIndex(index).FindPropertyRelative("name");

            return Mathf.Max(EditorGUIUtility.singleLineHeight, EditorGUI.GetPropertyHeight(name, GUIContent.none, false)) + 4;
        }

        /// <summary>New elements are zero-initialized by Unity, so the defaults are set by hand.</summary>
        void OnAddCallBack(ReorderableList l)
        {
            var index = sounds.arraySize;
            sounds.arraySize++;

            var element = sounds.GetArrayElementAtIndex(index);

            element.FindPropertyRelative("active").boolValue = true;
            element.FindPropertyRelative("time").floatValue = 0;
            element.FindPropertyRelative("Index").intValue = 0;

            var name = element.FindPropertyRelative("name");
            name.FindPropertyRelative("UseConstant").boolValue = true;   //MWC — arraySize++ zero-inits it, leaving an empty String Var slot
            name.FindPropertyRelative("UseMember").boolValue = false;
            name.FindPropertyRelative("ConstantValue").stringValue = "Sound Name";
            name.FindPropertyRelative("Variable").objectReferenceValue = null;

            l.index = index;
        }

        /// <summary>ReorderableList gives the header a wider rect than the elements. This matches them.</summary>
        private static Rect HeaderToElement(Rect header)
        {
            header.xMin += 14;
            header.xMax -= 5;
            return header;
        }

        /// <summary>Shared column geometry so the header and the elements stay aligned. [Name] and [Time] share the width.</summary>
        private static void GetRects(Rect rect, out Rect active, out Rect name, out Rect time, out Rect index)
        {
            const float toggleW = 14f, refPopupW = 20f, indexW = 34f, pad = 4f;
            var line = EditorGUIUtility.singleLineHeight;

            //MWC — the StringReference drawer hangs its [Local|Global] popup 20px LEFT of the rect it gets, so a gutter is reserved
            var free = rect.width - toggleW - refPopupW - indexW - (pad * 2);
            var half = free * 0.5f;

            active = new Rect(rect.x, rect.y, toggleW, line);
            name = new Rect(rect.x + toggleW + refPopupW, rect.y, half, line);
            time = new Rect(name.xMax + pad, rect.y, half, line);
            index = new Rect(time.xMax + pad, rect.y, indexW, line);
        }
    }
#endif
}
