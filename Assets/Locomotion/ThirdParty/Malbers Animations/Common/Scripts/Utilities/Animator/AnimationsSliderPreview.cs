using UnityEngine;
using System.Collections.Generic;


#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace MalbersAnimations
{
    [AddComponentMenu("Malbers/Utilities/Transform/Animations Preview (Multi)")]
    public class AnimationsSliderPreview : MonoBehaviour
    {
        // ── Bind pose snapshot ───────────────────────────────────────────────
        [System.Serializable]
        public struct CharacterBindPose
        {
            public Transform bone;
            public Vector3 localPos;
            public Quaternion localRot;
        }

        // ── Per-clip data ────────────────────────────────────────────────────
        [System.Serializable]
        public class AnimClipEntry
        {
            public AnimationClip clip;
            [Range(0f, 1f)] public float time = 0f;
        }

        // ── Component fields ─────────────────────────────────────────────────
        public List<AnimClipEntry> clips = new();

        [Tooltip("Index of the currently active clip in the 'clips' list. Set to -1 to stop playback.")]
        public int currentClipIndex = -1;

        [Tooltip("Playback speed multiplier. Can be negative for reverse playback.")]
        public float speed = 1f;

        [Tooltip("When true, the current clip will loop. When false, it will auto-advance to the next clip when finished.")]
        public bool loop = true;

        public bool rootMotion = true;

        public Vector3 positionOffset;
        public Vector3 rotationOffset;
        public GameObject root;

        [SerializeField] private bool isPlaying;

        [HideInInspector] public List<CharacterBindPose> bindPoses;

        // ── Helpers ──────────────────────────────────────────────────────────
        public AnimClipEntry CurrentEntry =>
            clips != null && currentClipIndex >= 0 && currentClipIndex < clips.Count
                ? clips[currentClipIndex] : null;

        // ── Runtime playback ─────────────────────────────────────────────────
        private void OnEnable()
        {
            if (CurrentEntry?.clip == null)
                isPlaying = false;
        }
        private void OnDisable()
        {
            isPlaying = false;
        }

        private void Update()
        {
            var entry = CurrentEntry;

            entry ??= clips.Count > 0 ? clips[0] : null; // Auto-select first clip if available
            isPlaying = entry?.clip != null;

            if (!isPlaying || entry?.clip == null || root == null) return;

            var clip = entry.clip;
            float absSpeed = Mathf.Abs(speed);
            float newTime = entry.time + Time.deltaTime * absSpeed / clip.length;


            if (newTime > 1f)
            {
                if (loop)
                {
                    //root.transform.GetPositionAndRotation(out positionOffset, out currentRotOffset);
                    entry.time = Mathf.Repeat(newTime, 1f);
                }
                else
                {
                    entry.time = 0f;
                    currentClipIndex = (currentClipIndex + 1) % clips.Count;
                    entry = clips[currentClipIndex];
                    if (entry?.clip == null) return;
                    clip = entry.clip;
                }
            }
            else
            {
                entry.time = newTime;
            }

            float sampleT = speed >= 0f ? entry.time : 1f - entry.time; // Reverse playback by inverting time

            clip.SampleAnimation(root, Mathf.Lerp(0, clip.length, sampleT));
            root.transform.GetPositionAndRotation(out afterPos, out afterRot);
            ApplyWithOffset(root.transform, afterPos, afterRot);
        }

        Vector3 afterPos;
        Quaternion afterRot;

        // ── Reset (first add) ────────────────────────────────────────────────
        private void Reset()
        {
            root = gameObject;
            var allTransforms = GetComponentsInChildren<Transform>();
            bindPoses = new List<CharacterBindPose>();
            foreach (var bone in allTransforms)
            {
                if (bone.GetComponent<SkinnedMeshRenderer>() != null) continue;
                if (bone.GetComponent<MeshRenderer>() != null) continue;
                if (!bindPoses.Exists(b => b.bone == bone))
                    bindPoses.Add(new CharacterBindPose { bone = bone, localPos = bone.localPosition, localRot = bone.localRotation });
            }
        }

        [ContextMenu("Rebind Initial Pose")]
        public void Rebind()
        {
            foreach (var bp in bindPoses)
                if (bp.bone != null)
                    bp.bone.SetLocalPositionAndRotation(bp.localPos, bp.localRot);

            root.transform.SetPositionAndRotation(positionOffset, Quaternion.Euler(rotationOffset));

        }

        void OnValidate()
        {
            if (root == null) root = gameObject;
            SampleCurrentEntry();
        }

        // Applies position/rotation offset after sampling, respecting rootMotion flag
        public void ApplyWithOffset(Transform t, Vector3 afterPos, Quaternion afterRot)
        {
            var RotOffsetQuat = Quaternion.Euler(rotationOffset);
            afterPos = rootMotion ? RotOffsetQuat * afterPos + positionOffset : positionOffset;
            afterRot = rootMotion ? RotOffsetQuat * afterRot : RotOffsetQuat;
            t.SetPositionAndRotation(afterPos, afterRot);
        }

        public void SampleCurrentEntry()
        {
            if (root == null || CurrentEntry?.clip == null || !enabled) return;

            CurrentEntry.clip.SampleAnimation(root, Mathf.Lerp(0, CurrentEntry.clip.length, CurrentEntry.time));
            root.transform.GetPositionAndRotation(out var afterPos, out var afterRot);
            ApplyWithOffset(root.transform, afterPos, afterRot);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  EDITOR
    // ═════════════════════════════════════════════════════════════════════════
#if UNITY_EDITOR

    [CustomEditor(typeof(AnimationsSliderPreview)), CanEditMultipleObjects]
    public class AnimationsSliderPreviewEditor : Editor
    {
        AnimationsSliderPreview M;

        SerializedProperty spClips, spCurrentIndex, spSpeed, spLoop, spRootMotion, spPosOffset, spRotOffset, spRoot, spIsPlaying;

        ReorderableList reorderList;
        Animator anim;
        double lastEditorTime;

        // Layout constants
        const float BtnW = 26f;
        const float XBtnW = 18f;
        const float Gap = 3f;
        const float ClipRatio = 0.6f;
        const float TimeRatio = 0.4f;

        float RowH => EditorGUIUtility.singleLineHeight;
        float ElemH => RowH + 4f;

        // ── Init ─────────────────────────────────────────────────────────────
        void OnEnable()
        {
            M = (AnimationsSliderPreview)target;
            spClips = serializedObject.FindProperty("clips");
            spCurrentIndex = serializedObject.FindProperty("currentClipIndex");
            spSpeed = serializedObject.FindProperty("speed");
            spLoop = serializedObject.FindProperty("loop");
            spRootMotion = serializedObject.FindProperty("rootMotion");
            spPosOffset = serializedObject.FindProperty("positionOffset");
            spRotOffset = serializedObject.FindProperty("rotationOffset");
            spRoot = serializedObject.FindProperty("root");
            spIsPlaying = serializedObject.FindProperty("isPlaying");

            anim = M.GetComponent<Animator>();
            BuildList();

            if (M.CurrentEntry?.clip != null && spIsPlaying.boolValue)
                StartPreview();
        }

        void OnDisable() => StopPreview();

        // ── ReorderableList ──────────────────────────────────────────────────
        void BuildList()
        {
            reorderList = new ReorderableList(serializedObject, spClips,
                draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true)
            {
                drawHeaderCallback = DrawListHeader,
                elementHeightCallback = _ => ElemH,
                drawElementCallback = DrawListElement,
                onAddCallback = OnListAdd,
                onRemoveCallback = OnListRemove,
            };
        }

        void DrawListHeader(Rect r)
        {
            // ── Reset-all-times button (right edge) ─────────────────────────
            const float ResetBtnW = 27f;
            var resetRect = new Rect(r.xMax - ResetBtnW, r.y + 1f, ResetBtnW, r.height - 2f);
            if (GUI.Button(resetRect, new GUIContent("↺", "Reset all times to zero"), EditorStyles.miniButton))
            {
                for (int i = 0; i < spClips.arraySize; i++)
                    spClips.GetArrayElementAtIndex(i).FindPropertyRelative("time").floatValue = 0f;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(M);
            }

            // ── Column labels ────────────────────────────────────────────────
            float x = r.x + BtnW + Gap + 14f; // 14 = reorder-handle indent
            float available = resetRect.x - Gap - x - Gap - XBtnW - Gap;
            float clipW = available * ClipRatio;
            float timeW = available * TimeRatio;

            GUI.Label(new Rect(x, r.y, clipW, r.height), "Clip", EditorStyles.miniLabel);
            GUI.Label(new Rect(x + clipW + Gap, r.y, timeW, r.height), "Time", EditorStyles.centeredGreyMiniLabel);
        }

        void DrawListElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index >= spClips.arraySize) return;

            var elem = spClips.GetArrayElementAtIndex(index);
            var spClip = elem.FindPropertyRelative("clip");
            var spTime = elem.FindPropertyRelative("time");

            bool isCurrent = M.currentClipIndex == index;
            bool isCurrentPlaying = isCurrent && spIsPlaying.boolValue;

            float y = rect.y + 2f;
            float xBtnX = rect.xMax - XBtnW;
            float contX = rect.x + BtnW + Gap;
            float available = xBtnX - Gap - contX - Gap;
            float clipW = available * ClipRatio;
            float timeX = contX + clipW + Gap;
            float timeW = available * TimeRatio;

            // ── Play / Stop button ──────────────────────────────────────────
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = isCurrentPlaying ? new Color(1f, 0.4f, 0.4f)
                                 : isCurrent ? new Color(0.5f, 1f, 0.6f)
                                                    : Color.white;

            if (GUI.Button(new Rect(rect.x, y, BtnW, RowH), isCurrentPlaying ? "■" : "▶"))
            {
                serializedObject.ApplyModifiedProperties();
                if (isCurrentPlaying)
                {
                    StopPreview();
                }
                else
                {
                    M.currentClipIndex = index;
                    spCurrentIndex.intValue = index;
                    serializedObject.ApplyModifiedProperties();
                    StartPreview();
                }
                EditorUtility.SetDirty(M);
            }
            GUI.backgroundColor = prevBg;

            // ── Clip field ──────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(new Rect(contX, y, clipW, RowH), spClip, GUIContent.none);
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                M.currentClipIndex = index;
                EditorUtility.SetDirty(M);
            }

            // ── Time slider (global) ────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            float t = EditorGUI.Slider(new Rect(timeX, y, timeW, RowH), spTime.floatValue, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                spTime.floatValue = t;
                serializedObject.ApplyModifiedProperties();
                M.currentClipIndex = index;

                M.SampleCurrentEntry();
                DoSamplePosRot();

                EditorUtility.SetDirty(M);
            }

            // ── Remove button ───────────────────────────────────────────────
            var prevBg2 = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (GUI.Button(new Rect(xBtnX, y, XBtnW, RowH), new GUIContent("×", "Remove this clip")))
                RemoveEntryAt(index);
            GUI.backgroundColor = prevBg2;
        }

        void OnListAdd(ReorderableList rl)
        {
            int newIdx = spClips.arraySize;
            spClips.arraySize++;
            var elem = spClips.GetArrayElementAtIndex(newIdx);
            elem.FindPropertyRelative("clip").objectReferenceValue = null;
            elem.FindPropertyRelative("time").floatValue = 0f;
            serializedObject.ApplyModifiedProperties();
        }

        void OnListRemove(ReorderableList rl) => RemoveEntryAt(rl.index);

        void RemoveEntryAt(int idx)
        {
            if (idx < 0 || idx >= spClips.arraySize) return;

            if (idx == M.currentClipIndex)
            {
                StopPreview();
                M.currentClipIndex = -1;
                spCurrentIndex.intValue = -1;
            }
            else if (idx < M.currentClipIndex)
            {
                M.currentClipIndex--;
                spCurrentIndex.intValue = M.currentClipIndex;
            }

            spClips.DeleteArrayElementAtIndex(idx);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(M);
        }

        // ── Preview playback ─────────────────────────────────────────────────
        void StartPreview()
        {
            spIsPlaying.boolValue = true;
            serializedObject.ApplyModifiedProperties();
            lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        void StopPreview()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (target == null || serializedObject == null || !spIsPlaying.boolValue) return;
            spIsPlaying.boolValue = false;
            serializedObject.ApplyModifiedProperties();
        }

        void OnEditorUpdate()
        {
            if (M == null || serializedObject == null || M.enabled == false) { StopPreview(); return; }

            if (Application.isPlaying) return; //Double safety check to prevent running in play mode

            var entry = M.CurrentEntry;
            if (entry?.clip == null) { StopPreview(); return; }

            double now = EditorApplication.timeSinceStartup;
            float delta = (float)(now - lastEditorTime);
            lastEditorTime = now;

            var clip = entry.clip;
            float absSpeed = Mathf.Abs(M.speed);
            float newTime = entry.time + delta * absSpeed / clip.length;

            if (newTime > 1f)
            {
                if (M.loop)
                {
                    entry.time = Mathf.Repeat(newTime, 1f);
                }
                else
                {
                    entry.time = 0f;
                    M.currentClipIndex = (M.currentClipIndex + 1) % M.clips.Count;
                    entry = M.CurrentEntry;
                    if (entry?.clip == null) { StopPreview(); return; }
                    clip = entry.clip;
                }
            }
            else
            {
                entry.time = newTime;
            }

            float sampleT = M.speed >= 0f ? entry.time : 1f - entry.time;

            if (M.root)
            {
                clip.SampleAnimation(M.root, Mathf.Lerp(0, clip.length, sampleT));
                DoSamplePosRot();
            }

            EditorUtility.SetDirty(M);
            Repaint();
        }

        private void DoSamplePosRot()
        {
            M.root.transform.GetPositionAndRotation(out var ap, out var ar);
            var offsetRot = Quaternion.Euler(M.rotationOffset);
            var afterPos = M.rootMotion ? M.positionOffset + (offsetRot * ap) : M.positionOffset;
            var afterRot = M.rootMotion ? offsetRot * ar : offsetRot;
            M.root.transform.SetPositionAndRotation(afterPos, afterRot);
        }



        // ── Clip info panel ──────────────────────────────────────────────────
        void DrawClipInfo()
        {
            var entry = M.CurrentEntry;
            if (entry?.clip == null) return;
            var clip = entry.clip;

            int totalFrames = Mathf.RoundToInt(clip.length * clip.frameRate);
            float sampleT = M.speed >= 0f ? entry.time : 1f - entry.time;
            int currentFrame = Mathf.RoundToInt(sampleT * totalFrames);
            int eventCount = clip.events.Length;
            string rigType = clip.isHumanMotion ? "Humanoid" : (clip.legacy ? "Legacy" : "Generic");
            string direction = M.speed >= 0f ? "▶  Forward" : "◀  Reverse";

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // ── Row 1: name + tag badges ────────────────────────────────
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(clip.name, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    InfoBadge(rigType);
                    if (clip.hasRootCurves) InfoBadge("Root Curves");
                    if (clip.hasMotionCurves) InfoBadge("Motion Curves");
                    if (eventCount > 0) InfoBadge($"{eventCount} Event{(eventCount != 1 ? "s" : "")}");
                }

                // ── Row 2: timing details ───────────────────────────────────
                using (new GUILayout.HorizontalScope())
                {
                    InfoLabel($"{clip.length:F3} s");
                    InfoDot();
                    InfoLabel($"{totalFrames} frames");
                    InfoDot();
                    InfoLabel($"{clip.frameRate} fps");
                    InfoDot();
                    InfoLabel($"Frame  {currentFrame} / {totalFrames}");
                    InfoDot();
                    InfoLabel($"Wrap: {clip.wrapMode}");
                    InfoDot();
                    InfoLabel(direction);
                    GUILayout.FlexibleSpace();
                }
            }
        }

        static GUIStyle s_infoBadgeStyle;
        static GUIStyle InfoBadgeStyle => s_infoBadgeStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.6f, 0.85f, 1f) }
        };

        static void InfoBadge(string text) =>
            GUILayout.Label($"[{text}]", InfoBadgeStyle);

        static void InfoLabel(string text) =>
            GUILayout.Label(text, EditorStyles.miniLabel);

        static void InfoDot()
        {
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUILayout.Label("·", EditorStyles.miniLabel, GUILayout.Width(8));
            GUI.color = prevColor;
        }

        // ── Drag & drop zone ─────────────────────────────────────────────────
        void DrawDragDropZone()
        {
            var evt = Event.current;

            var dropStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Italic
            };

            using (new GUILayout.HorizontalScope())
            {
                // ── Drop zone ───────────────────────────────────────────────
                var dropRect = GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true));
                bool hovering = dropRect.Contains(evt.mousePosition)
                    && (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform);

                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = hovering ? new Color(0.3f, 0.85f, 0.3f, 0.4f) : new Color(0.2f, 0.5f, 0.9f, 0.25f);
                GUI.Box(dropRect, "  Drop FBX, Animation files or Folders here", dropStyle);
                GUI.backgroundColor = prevBg;

                if (dropRect.Contains(evt.mousePosition))
                {
                    if (evt.type == EventType.DragUpdated)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.Use();
                    }
                    else if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        evt.Use();

                        bool anyAdded = false;
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            string path = AssetDatabase.GetAssetPath(obj);
                            if (string.IsNullOrEmpty(path)) continue;

                            if (AssetDatabase.IsValidFolder(path))
                                AddClipsFromFolder(path, ref anyAdded);
                            else
                                AddClipsFromAssetPath(path, ref anyAdded);
                        }

                        FinalizeAddedClips(anyAdded);
                    }
                }

                // ── Find FBX button ─────────────────────────────────────────
                if (GUILayout.Button(new GUIContent("Find FBX", "Browse for a folder and add all FBX / animation clips found inside it and its subfolders"),
                    GUILayout.Width(68), GUILayout.Height(34)))
                {
                    string absPath = EditorUtility.OpenFolderPanel("Select folder with FBX / animation files", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(absPath))
                    {
                        // Convert absolute path to project-relative (Assets/…)
                        if (absPath.StartsWith(Application.dataPath))
                            absPath = "Assets" + absPath.Substring(Application.dataPath.Length);

                        bool anyAdded = false;
                        AddClipsFromFolder(absPath, ref anyAdded);
                        FinalizeAddedClips(anyAdded);
                    }
                }
            }
        }

        // Recursively collects clips from all AnimationClip assets inside a folder
        // (Unity returns embedded FBX clips via t:AnimationClip, so t:Model is redundant and causes double-loading)
        void AddClipsFromFolder(string assetFolderPath, ref bool anyAdded)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { assetFolderPath }))
                AddClipsFromAssetPath(AssetDatabase.GUIDToAssetPath(guid), ref anyAdded);
        }

        // Extracts all non-preview AnimationClips from a single asset file
        void AddClipsFromAssetPath(string path, ref bool anyAdded)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip ac && !ac.name.StartsWith("__preview__"))
                    if (!ClipExists(ac)) { AddEntry(ac); anyAdded = true; }
            }
        }

        void FinalizeAddedClips(bool anyAdded)
        {
            if (!anyAdded) return;
            EditorUtility.SetDirty(M);
            serializedObject.Update();
            BuildList();
        }

        bool ClipExists(AnimationClip clip)
        {
            foreach (var e in M.clips)
                if (e.clip == clip) return true;
            return false;
        }

        void AddEntry(AnimationClip clip)
        {
            Undo.RecordObject(M, "Add Clip Entry");
            M.clips.Add(new AnimationsSliderPreview.AnimClipEntry { clip = clip, time = 0f });
        }

        // ── Compatibility warning ─────────────────────────────────────────────
        string GetCompatibilityWarning(AnimationClip c, GameObject rootGO)
        {
            if (c == null || rootGO == null) return null;
            if (c.isHumanMotion)
            {
                if (!anim) return "Humanoid clip — root has no Animator component.";
                if (anim.avatar == null) return "Humanoid clip — Animator has no Avatar assigned.";
                if (!anim.avatar.isValid) return "Humanoid clip — Avatar is not valid.";
                if (!anim.avatar.isHuman) return "Humanoid clip — Avatar is not humanoid.";
                return null;
            }
            var bindings = AnimationUtility.GetCurveBindings(c);
            if (bindings.Length == 0) return null;
            int matched = 0;
            foreach (var b in bindings)
                if (AnimationUtility.GetAnimatedObject(rootGO, b) != null) matched++;
            if (matched == 0) return $"Generic clip — 0/{bindings.Length} bindings match. Wrong rig?";
            if (matched < bindings.Length) return $"Generic clip — only {matched}/{bindings.Length} bindings match.";
            return null;
        }

        // ── Inspector GUI ─────────────────────────────────────────────────────
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Preview multiple Animation Clips in the Editor");

            // Root + Rebind
            using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUIUtility.labelWidth = 35f;
                EditorGUILayout.PropertyField(spRoot);
                EditorGUIUtility.labelWidth = 0f;
                using (new EditorGUI.DisabledGroupScope(spIsPlaying.boolValue))
                    if (GUILayout.Button("Rebind", GUILayout.Width(55)))
                        M.Rebind();
            }

            // Position / Rotation offset
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Offset", EditorStyles.boldLabel, GUILayout.Width(42));
                    if (GUILayout.Button(new GUIContent("Capture", "Set offset to the root's current world position and rotation"), EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        Undo.RecordObject(M, "Capture Offset");
                        M.positionOffset = M.root ? M.root.transform.position : Vector3.zero;
                        M.rotationOffset = M.root ? M.root.transform.eulerAngles : Vector3.zero;
                        EditorUtility.SetDirty(M);
                        serializedObject.Update();
                    }
                    if (GUILayout.Button(new GUIContent("Reset", "Zero out position and rotation offset"), EditorStyles.miniButton, GUILayout.Width(45)))
                    {
                        Undo.RecordObject(M, "Reset Offset");
                        M.positionOffset = Vector3.zero;
                        M.rotationOffset = Vector3.zero;
                        EditorUtility.SetDirty(M);
                        serializedObject.Update();
                    }
                }
                EditorGUIUtility.labelWidth = 28f;
                EditorGUILayout.PropertyField(spPosOffset, new GUIContent("Pos"));
                EditorGUILayout.PropertyField(spRotOffset, new GUIContent("Rot"));
                EditorGUIUtility.labelWidth = 0f;
            }

            // Global Loop + Root Motion + Clear All
            using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                spLoop.boolValue = GUILayout.Toggle(spLoop.boolValue,
                    new GUIContent("  Loop", "Loop current clip. When off, auto-advances to the next clip."),
                    EditorStyles.miniButton);

                spRootMotion.boolValue = GUILayout.Toggle(spRootMotion.boolValue,
                    new GUIContent("  Root Motion", "Apply root motion from all clips."),
                    EditorStyles.miniButton);

                GUILayout.FlexibleSpace();

                EditorGUIUtility.labelWidth = 40f;
                EditorGUILayout.PropertyField(spSpeed, new GUIContent("Speed"), GUILayout.Width(90));
                EditorGUIUtility.labelWidth = 0f;

                if (M.clips != null && M.clips.Count > 0)
                    if (GUILayout.Button("Clear All", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        Undo.RecordObject(M, "Clear Clips");
                        StopPreview();
                        M.clips.Clear();
                        M.currentClipIndex = -1;
                        spCurrentIndex.intValue = -1;
                        EditorUtility.SetDirty(M);
                        serializedObject.Update();
                        BuildList();
                    }
            }

            // FBX drop zone
            DrawDragDropZone();

            // Compatibility warning for active entry
            var currentEntry = M.CurrentEntry;
            if (currentEntry != null)
            {
                var warn = GetCompatibilityWarning(currentEntry.clip, M.root);
                if (warn != null) EditorGUILayout.HelpBox(warn, MessageType.Warning);
            }

            EditorGUILayout.Space(2);

            // Reorderable clip list
            if (reorderList == null) BuildList();
            reorderList.DoLayoutList();

            if (M.clips == null || M.clips.Count == 0)
                EditorGUILayout.HelpBox("No clips. Drop FBX or animation files into the zone above.", MessageType.Info);

            // Selected clip info
            DrawClipInfo();

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
