using UnityEngine;
using System.Collections.Generic;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [AddComponentMenu("Malbers/Utilities/Transform/Animation Preview")]
    public class AnimationSlider : MonoBehaviour
    {
        [System.Serializable]
        public struct CharacterBindPose
        {
            public Transform bone;
            public Vector3 localPos;
            public Quaternion localRot;
        }

        [ContextMenuItem("▶ Rename with Clip", nameof(RenameToClip))]
        public AnimationClip clip;
        public GameObject root;

        [Range(0f, 1f)] public float time;
        public float speed = 1f;

        public bool rootMotion = true;

        private bool m_playing;
        [SerializeField] private bool isPlaying;

        public List<CharacterBindPose> bindPoses;

        public Vector3 BindPos;



        private void OnEnable()
        {
            if (clip) m_playing = true;
        }

        private void OnDisable()
        {
            m_playing = false;
        }

        private void Update()
        {
            if (!m_playing || clip == null || root == null) return;

            time = Mathf.Repeat(time + Time.deltaTime * speed / clip.length, 1f);
            clip.SampleAnimation(root, Mathf.Lerp(0, clip.length, time));
        }


        private void Reset()
        {
            root = gameObject;
            var allTransforms = GetComponentsInChildren<Transform>();
            //find all the bones that are in the SkinnedMeshRenderer
            bindPoses = new List<CharacterBindPose>();
            foreach (var bone in allTransforms)
            {
                if (bone.GetComponent<SkinnedMeshRenderer>() != null) continue; //Skip the SkinnedMeshRenderer itself
                if (bone.GetComponent<MeshRenderer>() != null) continue; //Skip the SkinnedMeshRenderer itself

                if (!bindPoses.Exists(b => b.bone == bone))
                {
                    bindPoses.Add(new CharacterBindPose
                    {
                        bone = bone,
                        localPos = bone.localPosition,
                        localRot = bone.localRotation
                    });
                }
            }
        }

        [ContextMenu("Rebind Initial Pose")]
        public void Rebind()
        {
            BindPos = root.transform.position;
            foreach (var bindPose in bindPoses)
            {
                if (bindPose.bone != null)
                {
                    bindPose.bone.SetLocalPositionAndRotation(bindPose.localPos, bindPose.localRot);
                }
            }

            root.transform.position = BindPos;
        }

        [ContextMenu("Change Name To Clip")]
        private void RenameToClip()
        {
            if (clip != null)
            {
                gameObject.name = clip.name + " Preview";
                MTools.SetDirty(gameObject);
            }
        }



        void OnValidate()
        {
            if (root == null) root = gameObject;

            if (root && clip)
            {
                root.transform.GetPositionAndRotation(out var pos, out var rot);

                var ClipTime = Mathf.Lerp(0, clip.length, this.time);

                clip.SampleAnimation(root, ClipTime); //Needs an Avatar

                root.transform.GetPositionAndRotation(out var afterClipPos, out var afterClipRot);

                if (!rootMotion) root.transform.SetPositionAndRotation(pos, rot);
                else
                {
                    root.transform.SetPositionAndRotation(afterClipPos, afterClipRot);
                }
            }
        }
    }

#if UNITY_EDITOR

    [CustomEditor(typeof(AnimationSlider)), CanEditMultipleObjects]
    public class AnimationSliderEditor : Editor
    {
        AnimationSlider M;
        SerializedProperty clip, time, speed, rootMotion, isPlaying;

        Animator anim;

        double lastEditorTime;

        private void OnEnable()
        {
            M = (AnimationSlider)target;
            clip = serializedObject.FindProperty("clip");
            time = serializedObject.FindProperty("time");
            speed = serializedObject.FindProperty("speed");
            rootMotion = serializedObject.FindProperty("rootMotion");
            isPlaying = serializedObject.FindProperty("isPlaying");
            if (M.clip && isPlaying.boolValue) StartPreview();

            anim = M ? M.GetComponent<Animator>() : null;
        }

        private void OnDisable()
        {
            StopPreview();
        }

        void StartPreview()
        {
            isPlaying.boolValue = true;
            lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        void StopPreview()
        {
            if (!isPlaying.boolValue) return;
            isPlaying.boolValue = false;
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnEditorUpdate()
        {
            if (M == null || M.clip == null)
            {
                StopPreview();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float delta = (float)(now - lastEditorTime);
            lastEditorTime = now;

            float advance = delta * M.speed / M.clip.length;
            M.time = Mathf.Repeat(M.time + advance, 1f);

            // Sample the animation
            if (M.root)
            {
                M.root.transform.GetPositionAndRotation(out var pos, out var rot);
                M.clip.SampleAnimation(M.root, Mathf.Lerp(0, M.clip.length, M.time));

                M.root.transform.GetPositionAndRotation(out var afterClipPos, out var afterClipRot);

                if (!M.rootMotion) M.root.transform.SetPositionAndRotation(pos, rot);
                else
                {
                    M.root.transform.SetPositionAndRotation(afterClipPos, afterClipRot);
                }
            }

            EditorUtility.SetDirty(M);
            Repaint();
        }

        string GetCompatibilityWarning(AnimationClip c, GameObject root)
        {
            if (c == null || root == null) return null;

            if (c.isHumanMotion)
            {
                if (!anim)
                    return "Humanoid clip — root has no Animator component.";
                if (anim.avatar == null)
                    return "Humanoid clip — Animator has no Avatar assigned.";
                if (!anim.avatar.isValid)
                    return "Humanoid clip — Avatar is not valid.";
                if (!anim.avatar.isHuman)
                    return "Humanoid clip — Avatar is not humanoid.";
                return null;
            }

            // Generic / Legacy: check how many curve bindings resolve in the hierarchy
            var bindings = AnimationUtility.GetCurveBindings(c);
            if (bindings.Length == 0) return null;

            int matched = 0;
            foreach (var b in bindings)
            {
                if (AnimationUtility.GetAnimatedObject(root, b) != null) matched++;
            }

            if (matched == 0)
                return $"Generic clip — 0 / {bindings.Length} bindings match the hierarchy. Wrong rig?";

            if (matched < bindings.Length)
                return $"Generic clip — only {matched} / {bindings.Length} bindings match. Some bones may be missing.";

            return null;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Sample an Animation Clip in the Editor");

            using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUIUtility.labelWidth = 60f;

                EditorGUILayout.PropertyField(clip);
                EditorGUIUtility.labelWidth = 0f;

                using (new EditorGUI.DisabledGroupScope(isPlaying.boolValue))
                {
                    if (GUILayout.Button("Rebind", GUILayout.Width(50)))
                    {
                        M.Rebind();
                    }
                }
            }

            var warning = GetCompatibilityWarning(M.clip, M.root);
            if (warning != null)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {

                    var playLabel = isPlaying.boolValue ? "■  Stop" : "▶  Play";
                    var playColor = isPlaying.boolValue ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.6f);
                    var prevColor = GUI.backgroundColor;
                    GUI.backgroundColor = playColor;

                    if (GUILayout.Button(playLabel, GUILayout.Width(60)))
                    {
                        if (isPlaying.boolValue)
                            StopPreview();
                        else
                            StartPreview();
                    }

                    GUI.backgroundColor = prevColor;

                    EditorGUIUtility.labelWidth = 50f;
                    EditorGUILayout.PropertyField(time);
                    EditorGUILayout.PropertyField(speed, GUILayout.Width(80));
                    rootMotion.boolValue = GUILayout.Toggle(rootMotion.boolValue, new GUIContent("RM", "Root Motion"), EditorStyles.miniButton, GUILayout.Width(50));
                    EditorGUIUtility.labelWidth = 0f;
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
