// Last Change: 9/17/2026 5:33pm
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    // MWC — StateMachineBehaviour that plays a Weapon Trail for a normalized slice of an attack animation.
    // Modeled 1:1 on AttackTriggerBehaviour so the authoring feel is identical: drop it on the attack state,
    // drag the Min/Max handles, done.
    //
    // The trails are re-found on every OnStateEnter rather than cached, because equipped weapons are swapped
    // at runtime and a cached reference would point at the previous weapon.
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/utilities/weapon-trail")]
    [AddComponentMenu("Malbers/Weapon Trail")]
    public class WeaponTrailBehaviour : StateMachineBehaviour
    {
        [Tooltip("Range on the Animation that the Weapon Trail will be playing")]
        [MinMaxRange(0, 1)]
        public RangedFloat TrailActivation = new(0.2f, 0.6f);

        [Tooltip("0: Play all the Weapon Trails found\nx: Play only the Weapon Trails with that Index")]
        [Min(0)] public int TrailIndex = 0;

        // MWC — WHICH preset, as opposed to TrailIndex which is WHICH weapon. Lets a charged-attack state
        // reuse the same trail component with a stronger look.
        [Tooltip("Which entry of the trail's Profiles list to use.\n-1 keeps whatever profile is already selected")]
        [Min(-1)] public int ProfileIndex = 0;

        /// <summary>Logs which Weapon Trails received the Play/Stop.</summary>
        public bool debug;

        private bool isOn, isOff;

        // MWC — Cached registry lookup instead of a GetComponentsInChildren sweep. The list is owned by
        // MWeaponTrail's Registry, so it is never copied; it is only revalidated when the Animator changes
        // or a trail registered/unregistered (weapon equipped, holstered, swapped hands).
        private List<MWeaponTrail> cache;
        private Animator cacheAnimator;
        private int cacheVersion = -1;

        public override void OnStateEnter(Animator anim, AnimatorStateInfo stateInfo, int layerIndex)
        {
            isOn = isOff = false;
        }

        /// <summary>Refreshes the cached list only when the Animator changed or a trail was (un)registered.</summary>
        private void CheckCache(Animator animator)
        {
            if (cacheAnimator == animator && cacheVersion == MWeaponTrail.Version) return;

            cacheAnimator = animator;
            cacheVersion = MWeaponTrail.Version;
            cache = MWeaponTrail.GetAll(animator);
        }

        public override void OnStateUpdate(Animator anim, AnimatorStateInfo state, int layer)
        {
            var time = state.normalizedTime % 1;

            if (!isOn && time >= TrailActivation.minValue)
            {
                PlayTrails(anim);
                isOn = true;
            }

            if (!isOff && time >= TrailActivation.maxValue)
            {
                // Transitioning to itself: skip the OFF so a chained attack keeps its trail running
                if (anim.IsInTransition(layer) && anim.GetNextAnimatorStateInfo(layer).fullPathHash == state.fullPathHash) return;

                StopTrails(anim);
                isOff = true;
            }
        }

        public override void OnStateExit(Animator anim, AnimatorStateInfo state, int layer)
        {
            if (anim.GetCurrentAnimatorStateInfo(layer).fullPathHash == state.fullPathHash) return; //transitioning to itself

            // MWC — safety net: an interrupted attack must never leave the trail running forever
            if (!isOff) StopTrails(anim);

            isOn = isOff = false;
            // MWC — the cache IS the Registry's own list, so it is never cleared here
        }

        private void PlayTrails(Animator anim)
        {
            CheckCache(anim);

            if (cache == null)
            {
                if (debug) Debug.LogWarning($"[{anim.name}] has no [Weapon Trails] registered", anim);
                return;
            }

            // MWC — indexed loop, not foreach: this walks the Registry's live list, and Play can end up
            // re-parenting or disabling something, which would mutate it mid-iteration.
            for (int i = 0; i < cache.Count; i++)
            {
                var t = cache[i];
                if (!Match(t)) continue;

                t.PlayProfile(ProfileIndex); // MWC — negative index keeps the current profile

                if (debug) Debug.Log($"<B>[{anim.name}]</B> → Play Weapon Trail on <B>[{t.name}]</B> (Profile {ProfileIndex})", t);
            }
        }

        private void StopTrails(Animator anim)
        {
            CheckCache(anim);

            if (cache == null) return;

            for (int i = 0; i < cache.Count; i++)
            {
                var t = cache[i];
                if (!Match(t)) continue;
                t.Stop();
            }
        }

        private bool Match(MWeaponTrail t) => t != null && (TrailIndex == 0 || t.Index == TrailIndex);
    }

    #region INSPECTOR
#if UNITY_EDITOR
    [CustomEditor(typeof(WeaponTrailBehaviour))]
    [CanEditMultipleObjects]
    public class WeaponTrailBehaviourEditor : Editor
    {
        private SerializedProperty TrailActivation, TrailIndex, ProfileIndex,
            // FadeIn, FadeOut,
            debug;

        private void OnEnable()
        {
            TrailActivation = serializedObject.FindProperty(nameof(WeaponTrailBehaviour.TrailActivation));
            TrailIndex = serializedObject.FindProperty(nameof(WeaponTrailBehaviour.TrailIndex));
            ProfileIndex = serializedObject.FindProperty(nameof(WeaponTrailBehaviour.ProfileIndex));
            // FadeIn = serializedObject.FindProperty(nameof(WeaponTrailBehaviour.FadeIn));
            // FadeOut = serializedObject.FindProperty(nameof(WeaponTrailBehaviour.FadeOut));
            debug = serializedObject.FindProperty(nameof(WeaponTrailBehaviour.debug));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Plays the Weapon Trails using an animation range");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(TrailActivation);
                    MalbersEditor.DrawDebugIcon(debug);
                }

                EditorGUILayout.PropertyField(TrailIndex);
                EditorGUILayout.PropertyField(ProfileIndex);
                //  EditorGUILayout.PropertyField(FadeIn);
                // EditorGUILayout.PropertyField(FadeOut);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
    #endregion
}
