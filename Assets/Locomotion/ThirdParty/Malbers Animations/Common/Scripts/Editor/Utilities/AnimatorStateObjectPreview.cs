// Last Change: 9/17/2026 5:33pm
#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MalbersAnimations
{
    [CustomPreview(typeof(AnimatorState))]
    public class AnimatorStateObjectPreview : ObjectPreview
    {
        private Editor _preview;
#if UNITY_6000_4_OR_NEWER
        EntityId _animationClipId;
#else
        private int _animationClipId = 0;
#endif


        private static FieldInfo _cachedAvatarPreviewField;
        private static FieldInfo _cachedTimeControlField;
        private static FieldInfo _cachedStopTimeField;

        private float stateSpeed;

        public override void Initialize(Object[] targets)
        {
            base.Initialize(targets);
            if (targets.Length > 1 || Application.isPlaying) return;


            SourceAnimationClipEditorFields();

            var state = target as AnimatorState;

            stateSpeed = state.speed;

            AnimationClip clip = GetAnimationClip(state);

            if (clip != null)
            {
                _preview = Editor.CreateEditor(clip);
#if UNITY_6000_4_OR_NEWER
                _animationClipId = clip.GetEntityId(); //MWC: GetInstanceID obsolete in Unity 6000.4+
#else
                _animationClipId = clip.GetInstanceID();
#endif
            }
        }



        private AnimationClip GetAnimationClip(AnimatorState state)
        {
            return state?.motion as AnimationClip;
        }

        public override void Cleanup()
        {
            base.Cleanup();
            CleanUpPreviewEditor();
        }

        public override bool HasPreviewGUI()
        {
            return _preview?.HasPreviewGUI() ?? false;
        }


        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            GUI.Label(r, target.name);
        }


        public override void OnInteractivePreviewGUI(Rect r, GUIStyle background)
        {
            base.OnInteractivePreviewGUI(r, background);

            AnimationClip clip = GetAnimationClip(target as AnimatorState);

#if UNITY_6000_4_OR_NEWER
            if (clip != null && clip.GetEntityId() != _animationClipId) //MWC: GetInstanceID obsolete in Unity 6000.4+
#else
            if (clip != null && clip.GetInstanceID() != _animationClipId)
#endif
            {
                CleanUpPreviewEditor();
                _preview = Editor.CreateEditor(clip);
#if UNITY_6000_4_OR_NEWER
                _animationClipId = clip.GetEntityId();
#else
                _animationClipId = clip.GetInstanceID();
#endif
                return;
            }


            if (_preview != null)
            {
                UpdateClipEditor(_preview, clip);
                _preview.OnInteractivePreviewGUI(r, background);
            }
        }

        private void UpdateClipEditor(Editor preview, AnimationClip clip)
        {
            if (_cachedAvatarPreviewField == null || _cachedTimeControlField == null || _cachedStopTimeField == null) return;

            var avatarPreview = _cachedAvatarPreviewField.GetValue(preview);
            var timeControl = _cachedTimeControlField.GetValue(avatarPreview);



            _cachedStopTimeField.SetValue(timeControl, clip.length);
        }

        private void CleanUpPreviewEditor()
        {
            if (_preview != null)
            {
                UnityEngine.Object.DestroyImmediate(_preview);
                _preview = null;
                _animationClipId = default;
            }
        }



        private void SourceAnimationClipEditorFields()
        {
            if (_cachedAvatarPreviewField != null) return;

            _cachedAvatarPreviewField = System.Type.GetType("UnityEditor.AnimationClipEditor, UnityEditor").GetField("m_AvatarPreview", BindingFlags.NonPublic | BindingFlags.Instance);

            _cachedTimeControlField = System.Type.GetType("UnityEditor.AvatarPreview, UnityEditor").GetField("timeControl", BindingFlags.Public | BindingFlags.Instance);

            _cachedStopTimeField = System.Type.GetType("UnityEditor.TimeControl, UnityEditor").GetField("stopTime", BindingFlags.Public | BindingFlags.Instance);
        }
    }
}
#endif