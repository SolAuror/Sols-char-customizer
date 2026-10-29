// Last Change: 9/14/2026 2:37pm
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MalbersAnimations
{
    /// <summary> Batch override tool for FBX Animation Clip import settings: curves, events, time, loop, root motion, mask, mirror and additive pose </summary>
    public class FBXClipOverrider : EditorWindow
    {
        #region Data Model

        /// <summary> What a list entry (curve/event) does to the clip: add or replace it, or remove the existing one </summary>
        public enum ListMode { Set, Remove }
        /// <summary> Root Transform Rotation > Based Upon (Body Orientation is Root Node Rotation on Generic rigs) </summary>
        public enum RotationBasedUpon { BodyOrientation = 0, Original = 1 }
        /// <summary> Root Transform Position (Y) > Based Upon (Center Of Mass is Root Node Position on Generic rigs, Feet is Humanoid only) </summary>
        public enum PositionYBasedUpon { CenterOfMass = 0, Feet = 1, Original = 2 }
        /// <summary> Root Transform Position (XZ) > Based Upon (Center Of Mass is Root Node Position on Generic rigs) </summary>
        public enum PositionXZBasedUpon { CenterOfMass = 0, Original = 1 }
        /// <summary> Rig type of a loaded FBX, used to tailor labels and humanoid-only settings </summary>
        private enum Rig { Unknown, Humanoid, Generic, Legacy, None }

        /// <summary> A single overridable value: only written to the clip when 'use' is on </summary>
        [Serializable]
        public struct OverrideValue<T>
        {
            public bool use;
            public T value;
            public OverrideValue(T value) { use = false; this.value = value; }
        }

        /// <summary> Curve to add/replace/remove on a clip, matched by name </summary>
        [Serializable]
        public class CurveOverride
        {
            public ListMode mode = ListMode.Set;
            public string name = "NewCurve";
            public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }

        /// <summary> Animation Event to add/replace/remove on a clip, matched by function name (and normalized time when setting) </summary>
        [Serializable]
        public class EventOverride
        {
            public ListMode mode = ListMode.Set;
            public string function = "OnEvent";
            [Range(0f, 1f)] public float time = 0f;
            public float floatParameter;
            public int intParameter;
            public string stringParameter = string.Empty;
        }

        /// <summary> Every import setting the tool can override; used both for the Global block and for each clip </summary>
        [Serializable]
        public class OverrideBlock
        {
            // Curves
            public bool clearCurves;
            public List<CurveOverride> curves = new();

            // Events
            public bool clearEvents;
            public List<EventOverride> events = new();

            // Time
            public OverrideValue<float> firstFrame = new(0f);
            public OverrideValue<float> lastFrame = new(1f);
            public OverrideValue<WrapMode> wrapMode = new(WrapMode.Default);

            // Loop
            public OverrideValue<bool> loopTime;
            public OverrideValue<bool> loopPose;
            public OverrideValue<float> cycleOffset;

            // Root Transform Rotation
            public OverrideValue<bool> bakeRotation;
            public OverrideValue<RotationBasedUpon> rotationBasedUpon = new(RotationBasedUpon.Original);
            public OverrideValue<float> rotationOffset;

            // Root Transform Position (Y)
            public OverrideValue<bool> bakePositionY;
            public OverrideValue<PositionYBasedUpon> positionYBasedUpon = new(PositionYBasedUpon.Original);
            public OverrideValue<float> heightOffset;

            // Root Transform Position (XZ)
            public OverrideValue<bool> bakePositionXZ;
            public OverrideValue<PositionXZBasedUpon> positionXZBasedUpon = new(PositionXZBasedUpon.CenterOfMass);

            // Mask
            public OverrideValue<ClipAnimationMaskType> maskType;
            public OverrideValue<AvatarMask> maskSource;

            // Extras
            public OverrideValue<bool> mirror;
            public OverrideValue<bool> additiveReferencePose;
            public OverrideValue<float> additiveReferenceFrame;

            // Section foldouts (kept on the block so they survive domain reloads); all expanded on first open
            public bool F_Curves = true, F_Events = true, F_Time = true, F_Loop = true, F_Rotation = true,
                        F_PositionY = true, F_PositionXZ = true, F_Mask = true, F_Extras = true;

            /// <summary> Active entries in the Curves section </summary>
            public int CurvesCount => curves.Count + (clearCurves ? 1 : 0);
            /// <summary> Active entries in the Events section </summary>
            public int EventsCount => events.Count + (clearEvents ? 1 : 0);
            /// <summary> Active overrides in the Time section </summary>
            public int TimeCount => Count(firstFrame.use, lastFrame.use, wrapMode.use);
            /// <summary> Active overrides in the Loop section </summary>
            public int LoopCount => Count(loopTime.use, loopPose.use, cycleOffset.use);
            /// <summary> Active overrides in the Root Rotation section </summary>
            public int RotationCount => Count(bakeRotation.use, rotationBasedUpon.use, rotationOffset.use);
            /// <summary> Active overrides in the Root Position Y section </summary>
            public int PositionYCount => Count(bakePositionY.use, positionYBasedUpon.use, heightOffset.use);
            /// <summary> Active overrides in the Root Position XZ section </summary>
            public int PositionXZCount => Count(bakePositionXZ.use, positionXZBasedUpon.use);
            /// <summary> Active overrides in the Mask section </summary>
            public int MaskCount => Count(maskType.use, maskSource.use);
            /// <summary> Active overrides in the Extras section </summary>
            public int ExtrasCount => Count(mirror.use, additiveReferencePose.use, additiveReferenceFrame.use);
            /// <summary> Total active overrides on this block </summary>
            public int Total => CurvesCount + EventsCount + TimeCount + LoopCount + RotationCount + PositionYCount + PositionXZCount + MaskCount + ExtrasCount;

            /// <summary> Counts how many flags are on </summary>
            private static int Count(params bool[] flags) => flags.Count(f => f);

            /// <summary> Restores every value to its default and clears the lists </summary>
            public void Reset() => JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new OverrideBlock()), this);
        }

        /// <summary> One animation clip inside a loaded FBX </summary>
        [Serializable]
        public class ClipEntry
        {
            public string clipName;
            public bool enabled = true;
            public bool expanded = false;
            public bool ignoreGlobalCurves;
            public bool ignoreGlobalEvents;
            public OverrideBlock overrides = new();
        }

        /// <summary> One loaded FBX file with its clip list </summary>
        [Serializable]
        public class FbxEntry
        {
            public string assetPath;
            public string fileName;
            public bool expanded = true;
            public ModelImporterAnimationType animType = ModelImporterAnimationType.Generic;
            public bool importAnimation = true;
            public List<ClipEntry> clips = new();

            /// <summary> Number of clips that will be processed on Apply </summary>
            public int EnabledClips => clips.Count(c => c.enabled);

            /// <summary> Enables or disables every clip in the file </summary>
            public void SetEnabled(bool value) { foreach (ClipEntry c in clips) c.enabled = value; }

            /// <summary> Expands or collapses every clip in the file </summary>
            public void SetExpanded(bool value) { foreach (ClipEntry c in clips) c.expanded = value; }

            /// <summary> Re-reads rig type and clip names from the importer, keeping existing per-clip overrides by name </summary>
            public void Reload()
            {
                if (AssetImporter.GetAtPath(assetPath) is not ModelImporter importer) return;

                animType = importer.animationType;
                importAnimation = importer.importAnimation;

                ModelImporterClipAnimation[] source = importer.clipAnimations;
                if (source.Length == 0) source = importer.defaultClipAnimations;

                List<ClipEntry> rebuilt = new();
                foreach (ModelImporterClipAnimation clip in source)
                    rebuilt.Add(clips.Find(c => c.clipName == clip.name) ?? new ClipEntry { clipName = clip.name });
                clips = rebuilt;
            }
        }

        #endregion

        #region Window State

        [SerializeField] private List<FbxEntry> entries = new();
        [SerializeField] private OverrideBlock global = new();
        [SerializeField] private int Editor_Tabs1;
        [SerializeField] private Vector2 scroll;

        private static readonly OverrideBlock EmptyBlock = new();
        private static AnimationCurve curveClipboard;
        private static string blockClipboard;

        private static GUIContent iconOverrideOn, iconOverrideOff, iconPing, iconRefresh, iconRemove, iconAdd, iconFbx, iconClip;
        private static GUIStyle iconStyle;
        private GUIStyle dropStyle, badgeStyle, miniGray;
        private bool stylesReady;

        private const float LabelW = 150f;
        private const float ToggleW = 20f;
        private const float ButtonH = 30f;

        private static readonly Color AccentColor = new(0.35f, 0.75f, 1f, 1f);
        private static readonly Color HumanoidColor = new(0.55f, 0.9f, 0.6f, 1f);
        private static readonly Color GenericColor = new(0.95f, 0.75f, 0.4f, 1f);
        private static readonly Color LegacyColor = new(0.7f, 0.7f, 0.9f, 1f);
        private static readonly Color WarnColor = new(1f, 0.5f, 0.5f, 1f);
        private static readonly Color BadgeGray = new(0.6f, 0.6f, 0.6f, 1f);

        #endregion

        #region Menu Items

        [MenuItem("Tools/Malbers Animations/Editor/FBX Clip Overrider")]
        public static void Open() => OpenWindow();

        /// <summary> Project-window entry: opens the window and loads the selected FBX files (clips inside an FBX resolve to their FBX) </summary>
        [MenuItem("Assets/Malbers Animations/FBX Clip Overrider", false, 2000)]
        private static void OpenFromContext() => OpenWindow().AddSelection();

        /// <summary> Enables the context entry only when the selection contains an FBX or a clip inside one </summary>
        [MenuItem("Assets/Malbers Animations/FBX Clip Overrider", true)]
        private static bool OpenFromContextValidate() => SelectionFbxPaths().Count > 0;

        /// <summary> Creates or focuses the window with its title, icon and minimum size </summary>
        private static FBXClipOverrider OpenWindow()
        {
            FBXClipOverrider window = GetWindow<FBXClipOverrider>();
            window.titleContent = new GUIContent("FBX Clip Overrider", EditorGUIUtility.IconContent("d_ModelImporter Icon").image);
            window.minSize = new(660f, 480f);
            window.Show();
            return window;
        }

        #endregion

        #region Styles & Icons

        /// <summary> Builds the cached GUI styles once </summary>
        private void EnsureStyles()
        {
            if (stylesReady) return;

            dropStyle = new(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, fontSize = 12 };
            dropStyle.normal.textColor = new(0.6f, 0.6f, 0.6f, 1f);

            miniGray = new(EditorStyles.miniLabel);
            miniGray.normal.textColor = new(0.6f, 0.6f, 0.6f, 1f);

            badgeStyle = new(EditorStyles.miniButton) { fontSize = 9, fixedHeight = 16f, padding = new RectOffset(6, 6, 1, 1), fontStyle = FontStyle.Bold };

            stylesReady = true;
        }

        /// <summary> Loads the built-in editor icons once (Record On/Off is the override dot, matching Unity's prefab-override marker) </summary>
        private static void FindIcons()
        {
            if (iconOverrideOn != null) return;

            iconOverrideOn = new GUIContent(EditorGUIUtility.IconContent("d_Record On"));
            iconOverrideOff = new GUIContent(EditorGUIUtility.IconContent("d_Record Off"));
            iconPing = new GUIContent(EditorGUIUtility.IconContent("d_Search Icon")) { tooltip = "Ping the FBX in the Project window" };
            iconRefresh = new GUIContent(EditorGUIUtility.IconContent("d_Refresh")) { tooltip = "Reload rig type and clip list from the importer" };
            iconRemove = new GUIContent(EditorGUIUtility.IconContent("d_TreeEditor.Trash")) { tooltip = "Remove" };
            iconAdd = new GUIContent(EditorGUIUtility.IconContent("d_Toolbar Plus")) { tooltip = "Add" };
            iconFbx = new GUIContent(EditorGUIUtility.IconContent("d_ModelImporter Icon"));
            iconClip = new GUIContent(EditorGUIUtility.IconContent("d_AnimationClip Icon"));

            iconStyle = new GUIStyle(EditorStyles.iconButton)
            {
                alignment = TextAnchor.MiddleCenter,
                imagePosition = ImagePosition.ImageOnly,
                fixedWidth = 0f,
                fixedHeight = 0f,
                padding = new RectOffset(2, 2, 2, 2),
            };
        }

        #endregion

        #region OnGUI

        private void OnGUI()
        {
            EnsureStyles();
            FindIcons();

            MalbersEditor.DrawDescription("Override import settings on many FBX Animation Clips at once. Clip overrides win over Global overrides");

            DrawDropZone();
            EditorGUILayout.Space(2f);

            Editor_Tabs1 = GUILayout.Toolbar(Editor_Tabs1, new[] { "Global Overrides", $"FBX Files ({entries.Count})" });

            using (GUILayout.ScrollViewScope sv = new(scroll))
            {
                scroll = sv.scrollPosition;
                EditorGUILayout.Space(2f);

                if (Editor_Tabs1 == 0) ShowGlobal();
                else ShowFiles();
            }

            DrawBottomBar();
        }

        #endregion

        #region Drop Zone & Loading

        /// <summary> Drag & drop area plus Add Selection / Add Folder buttons </summary>
        private void DrawDropZone()
        {
            using (new GUILayout.HorizontalScope())
            {
                Rect zone = GUILayoutUtility.GetRect(0f, 40f, GUILayout.ExpandWidth(true));
                GUI.Box(zone, new GUIContent("  Drop FBX files or folders here", iconFbx.image), dropStyle);
                HandleDragDrop(zone);

                using (new GUILayout.VerticalScope(GUILayout.Width(110f)))
                {
                    if (GUILayout.Button(new GUIContent("Add Selection", "Add the FBX files (or clips inside FBX files) selected in the Project window"), EditorStyles.miniButton))
                        AddSelection();
                    if (GUILayout.Button(new GUIContent("Add Folder…", "Add every FBX inside a project folder"), EditorStyles.miniButton))
                        AddFolderDialog();
                }
            }
        }

        /// <summary> Accepts FBX files and folders dragged onto the zone </summary>
        private void HandleDragDrop(Rect zone)
        {
            Event evt = Event.current;
            if (!zone.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                    DragAndDrop.visualMode = HasValidDrop() ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                    evt.Use();
                    break;
                case EventType.DragPerform:
                    if (!HasValidDrop()) return;
                    DragAndDrop.AcceptDrag();
                    evt.Use();
                    ProcessDrop(DragAndDrop.objectReferences, DragAndDrop.paths);
                    Editor_Tabs1 = 1;
                    Repaint();
                    break;
            }
        }

        /// <summary> True when the current drag carries at least one FBX or folder </summary>
        private static bool HasValidDrop()
        {
            foreach (Object obj in DragAndDrop.objectReferences)
            {
                if (obj == null) continue;
                string path = AssetDatabase.GetAssetPath(obj);
                if (AssetDatabase.IsValidFolder(path) || IsFbx(path)) return true;
            }
            return false;
        }

        /// <summary> Collects every FBX from dropped objects and paths, then registers them </summary>
        private void ProcessDrop(Object[] objects, string[] rawPaths)
        {
            HashSet<string> collected = new(System.StringComparer.OrdinalIgnoreCase);

            foreach (Object obj in objects)
            {
                if (obj == null) continue;
                CollectPath(AssetDatabase.GetAssetPath(obj), collected);
            }

            foreach (string raw in rawPaths)
                CollectPath(AbsoluteToAssetPath(raw), collected);

            RegisterAll(collected);
        }

        /// <summary> Adds an FBX path directly, or every FBX inside a folder path </summary>
        private static void CollectPath(string assetPath, HashSet<string> result)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            if (AssetDatabase.IsValidFolder(assetPath)) CollectFbxInFolder(assetPath, result);
            else if (IsFbx(assetPath)) result.Add(assetPath);
        }

        /// <summary> Finds every FBX model under a project folder </summary>
        private static void CollectFbxInFolder(string folderPath, HashSet<string> result)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folderPath }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsFbx(path)) result.Add(path);
            }
        }

        /// <summary> Registers a set of FBX paths in alphabetical order </summary>
        private void RegisterAll(IEnumerable<string> paths)
        {
            foreach (string path in paths.OrderBy(p => p, System.StringComparer.OrdinalIgnoreCase))
                TryRegisterFbx(path);
        }

        /// <summary> Adds the FBX files currently selected in the Project window </summary>
        private void AddSelection()
        {
            RegisterAll(SelectionFbxPaths());
            Editor_Tabs1 = 1;
            Repaint();
        }

        /// <summary> Opens a folder picker and adds every FBX inside it </summary>
        private void AddFolderDialog()
        {
            string absolute = EditorUtility.OpenFolderPanel("Add FBX Folder", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(absolute)) return;

            string assetPath = AbsoluteToAssetPath(absolute);
            if (assetPath == null || !AssetDatabase.IsValidFolder(assetPath))
            {
                EditorUtility.DisplayDialog("FBX Clip Overrider", "The folder must be inside this project's Assets folder.", "OK");
                return;
            }

            HashSet<string> collected = new(System.StringComparer.OrdinalIgnoreCase);
            CollectFbxInFolder(assetPath, collected);
            RegisterAll(collected);
            Editor_Tabs1 = 1;
        }

        /// <summary> FBX asset paths for the current Project selection (clips inside an FBX resolve to the FBX) </summary>
        private static List<string> SelectionFbxPaths()
        {
            List<string> result = new();
            foreach (Object obj in Selection.objects)
            {
                if (obj == null) continue;
                string path = AssetDatabase.GetAssetPath(obj);
                if (IsFbx(path) && !result.Contains(path)) result.Add(path);
            }
            return result;
        }

        /// <summary> True when the path ends in .fbx (case-insensitive) </summary>
        private static bool IsFbx(string path) =>
            !string.IsNullOrEmpty(path) && Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase);

        /// <summary> Converts an absolute path to an Assets-relative path, or null when it is outside the project </summary>
        private static string AbsoluteToAssetPath(string absolute)
        {
            if (string.IsNullOrEmpty(absolute)) return null;
            string dataPath = Application.dataPath.Replace('\\', '/');
            absolute = absolute.Replace('\\', '/');
            return absolute.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)
                ? "Assets" + absolute.Substring(dataPath.Length)
                : null;
        }

        /// <summary> Adds an FBX to the list if it is a model and not already loaded </summary>
        private void TryRegisterFbx(string assetPath)
        {
            if (entries.Any(e => e.assetPath == assetPath)) return;
            if (AssetImporter.GetAtPath(assetPath) is not ModelImporter) return;

            FbxEntry entry = new() { assetPath = assetPath, fileName = Path.GetFileName(assetPath) };
            entry.Reload();
            entries.Add(entry);
        }

        #endregion

        #region Global Tab

        /// <summary> Global Overrides tab: one override block applied to every enabled clip </summary>
        private void ShowGlobal()
        {
            EditorGUILayout.HelpBox("Global overrides are applied to every enabled clip in every loaded FBX. " +
                "Turn on the dot next to a setting to override it. A clip's own override always wins over the global one.", MessageType.None);
            DrawOverrideBlock(global, Rig.Unknown);
        }

        #endregion

        #region Files Tab

        /// <summary> FBX Files tab: the loaded files, their clips and the per-clip override blocks </summary>
        private void ShowFiles()
        {
            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("No FBX files loaded. Drop FBX files or folders on the zone above, use Add Selection, or right-click an FBX in the Project window.", MessageType.Info);
                return;
            }

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label($"{entries.Count} file(s)  ·  {EnabledClips}/{TotalClips} clip(s) enabled", miniGray);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Expand All", EditorStyles.miniButtonLeft, GUILayout.Width(84f))) SetAllExpanded(true);
                if (GUILayout.Button("Collapse All", EditorStyles.miniButtonRight, GUILayout.Width(84f))) SetAllExpanded(false);
            }

            int remove = -1;
            for (int i = 0; i < entries.Count; i++)
                if (DrawFile(entries[i])) remove = i;

            if (remove >= 0) entries.RemoveAt(remove);
        }

        /// <summary> Draws one FBX file box; returns true when its remove button was pressed </summary>
        private bool DrawFile(FbxEntry file)
        {
            bool remove = false;
            Rig rig = ToRig(file);

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    file.expanded = EditorGUILayout.Foldout(file.expanded, new GUIContent(file.fileName, iconFbx.image, file.assetPath), true, EditorStyles.foldoutHeader);

                    DrawBadge(RigName(rig), RigColor(rig));
                    DrawBadge($"{file.EnabledClips}/{file.clips.Count} clips", BadgeGray);

                    if (IconButton(iconPing)) EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(file.assetPath));
                    if (IconButton(iconRefresh)) file.Reload();
                    if (IconButton(iconRemove)) remove = true;
                }

                if (!file.expanded) return remove;

                if (rig == Rig.None)
                    EditorGUILayout.HelpBox("Import Animation is disabled on this FBX. Enable it in the Model Importer to expose its clips.", MessageType.Warning);

                if (file.clips.Count == 0)
                {
                    EditorGUILayout.LabelField("No animation clips found.", miniGray);
                    return remove;
                }

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Space(14f);
                    GUILayout.Label("Clips", miniGray);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(44f))) file.SetEnabled(true);
                    if (GUILayout.Button("None", EditorStyles.miniButtonMid, GUILayout.Width(44f))) file.SetEnabled(false);
                    if (GUILayout.Button("Expand", EditorStyles.miniButtonMid, GUILayout.Width(58f))) file.SetExpanded(true);
                    if (GUILayout.Button("Collapse", EditorStyles.miniButtonRight, GUILayout.Width(58f))) file.SetExpanded(false);
                }

                foreach (ClipEntry clip in file.clips)
                    DrawClip(clip, rig);
            }

            return remove;
        }

        /// <summary> Draws one clip row and, when expanded, its override block </summary>
        private void DrawClip(ClipEntry clip, Rig rig)
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    clip.enabled = EditorGUILayout.Toggle(clip.enabled, GUILayout.Width(16f));

                    Color prev = GUI.contentColor;
                    if (!clip.enabled) GUI.contentColor = new Color(1f, 1f, 1f, 0.5f);
                    clip.expanded = EditorGUILayout.Foldout(clip.expanded, new GUIContent(clip.clipName, iconClip.image), true);
                    GUI.contentColor = prev;

                    int n = clip.overrides.Total;
                    if (!clip.enabled) DrawBadge("skipped", BadgeGray);
                    else if (n > 0) DrawBadge(n == 1 ? "1 override" : $"{n} overrides", AccentColor);
                }

                if (!clip.expanded) return;

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Space(18f);
                    clip.ignoreGlobalCurves = GUILayout.Toggle(clip.ignoreGlobalCurves,
                        new GUIContent("Ignore Global Curves", "Do not apply the Global Curves list to this clip"), GUILayout.Width(150f));
                    clip.ignoreGlobalEvents = GUILayout.Toggle(clip.ignoreGlobalEvents,
                        new GUIContent("Ignore Global Events", "Do not apply the Global Events list to this clip"), GUILayout.Width(150f));
                }

                DrawOverrideBlock(clip.overrides, rig);
            }
        }

        /// <summary> Expands or collapses every file and clip </summary>
        private void SetAllExpanded(bool value)
        {
            foreach (FbxEntry f in entries) { f.expanded = value; f.SetExpanded(value); }
        }

        /// <summary> Total clips across all loaded files </summary>
        private int TotalClips => entries.Sum(e => e.clips.Count);
        /// <summary> Enabled clips across all loaded files </summary>
        private int EnabledClips => entries.Sum(e => e.EnabledClips);

        #endregion

        #region Override Block

        /// <summary> Draws every section of an override block (used by both the Global tab and each clip) </summary>
        private void DrawOverrideBlock(OverrideBlock b, Rig rig)
        {
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LabelW;

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(4f);
                GUILayout.Label(b.Total == 1 ? "1 active override" : $"{b.Total} active overrides", miniGray);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Copy", "Copy this whole block"), EditorStyles.miniButtonLeft, GUILayout.Width(48f)))
                    blockClipboard = JsonUtility.ToJson(b);
                using (new EditorGUI.DisabledGroupScope(string.IsNullOrEmpty(blockClipboard)))
                    if (GUILayout.Button(new GUIContent("Paste", "Replace this block with the copied one"), EditorStyles.miniButtonMid, GUILayout.Width(48f)))
                        JsonUtility.FromJsonOverwrite(blockClipboard, b);
                if (GUILayout.Button(new GUIContent("Reset", "Turn off every override and clear the lists"), EditorStyles.miniButtonRight, GUILayout.Width(48f)))
                    b.Reset();
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                b.F_Curves = Section(b.F_Curves, "Curves", b.CurvesCount);
                if (b.F_Curves) DrawCurves(b);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                b.F_Events = Section(b.F_Events, "Events", b.EventsCount);
                if (b.F_Events) DrawEvents(b);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                b.F_Time = Section(b.F_Time, "Time", b.TimeCount);
                if (b.F_Time)
                {
                    OFloat(ref b.firstFrame, new GUIContent("Start (Frame)", "First frame of the clip"));
                    OFloat(ref b.lastFrame, new GUIContent("End (Frame)", "Last frame of the clip"));
                    OEnum(ref b.wrapMode, new GUIContent("Wrap Mode", "Clip wrap mode (Legacy rigs)"));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                b.F_Loop = Section(b.F_Loop, "Loop", b.LoopCount);
                if (b.F_Loop)
                {
                    OBool(ref b.loopTime, new GUIContent("Loop Time", "Play the clip in a loop"));
                    OBool(ref b.loopPose, new GUIContent("Loop Pose", "Blend the last pose into the first for a seamless loop"));
                    OFloat(ref b.cycleOffset, new GUIContent("Cycle Offset", "Normalized offset of the loop start (0-1)"));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                b.F_Rotation = Section(b.F_Rotation, "Root Transform Rotation", b.RotationCount);
                if (b.F_Rotation)
                {
                    OBool(ref b.bakeRotation, new GUIContent("Bake Into Pose", "Keep the rotation in the pose instead of driving Root Motion"));
                    ONamed(ref b.rotationBasedUpon, new GUIContent("Based Upon", "Reference orientation for the root rotation"), RotationNames(rig));
                    OFloat(ref b.rotationOffset, new GUIContent("Offset (Degrees)", "Rotation offset applied to the root"));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                b.F_PositionY = Section(b.F_PositionY, "Root Transform Position (Y)", b.PositionYCount);
                if (b.F_PositionY)
                {
                    OBool(ref b.bakePositionY, new GUIContent("Bake Into Pose", "Keep the vertical motion in the pose instead of driving Root Motion"));
                    ONamed(ref b.positionYBasedUpon, new GUIContent("Based Upon", "Reference height for the root position"), PositionYNames(rig));
                    OFloat(ref b.heightOffset, new GUIContent("Offset", "Height offset applied to the root"));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                b.F_PositionXZ = Section(b.F_PositionXZ, "Root Transform Position (XZ)", b.PositionXZCount);
                if (b.F_PositionXZ)
                {
                    OBool(ref b.bakePositionXZ, new GUIContent("Bake Into Pose", "Keep the horizontal motion in the pose instead of driving Root Motion"));
                    ONamed(ref b.positionXZBasedUpon, new GUIContent("Based Upon", "Reference position for the horizontal root motion"), PositionXZNames(rig));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                b.F_Mask = Section(b.F_Mask, "Mask", b.MaskCount);
                if (b.F_Mask)
                {
                    OEnum(ref b.maskType, new GUIContent("Mask Type", "None, Create From This Model, or Copy From Other"));
                    OObject(ref b.maskSource, new GUIContent("Mask Source", "Avatar Mask to copy. When set without a Mask Type override, Mask Type becomes Copy From Other"));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                b.F_Extras = Section(b.F_Extras, "Extras", b.ExtrasCount);
                if (b.F_Extras)
                {
                    if (rig is Rig.Humanoid or Rig.Unknown)
                        OBool(ref b.mirror, new GUIContent(rig == Rig.Unknown ? "Mirror (Humanoid only)" : "Mirror", "Mirror the animation left/right. Ignored on Generic rigs"));
                    OBool(ref b.additiveReferencePose, new GUIContent("Additive Reference Pose", "Use a frame of this clip as the reference pose for additive layers"));
                    OFloat(ref b.additiveReferenceFrame, new GUIContent("Pose Frame", "Frame used as the additive reference pose"));
                }
            }

            EditorGUIUtility.labelWidth = prevLabelWidth;
        }

        /// <summary> Curves list: add/replace/remove clip curves by name </summary>
        private void DrawCurves(OverrideBlock b)
        {
            b.clearCurves = EditorGUILayout.ToggleLeft(new GUIContent("Remove all existing curves first", "Start from an empty curve list before applying the entries below"), b.clearCurves);

            if (b.curves.Count > 0)
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Mode", miniGray, GUILayout.Width(64f));
                    GUILayout.Label("Curve Name", miniGray, GUILayout.Width(130f));
                    GUILayout.Label("Curve", miniGray);
                }
            }

            int remove = -1;
            for (int i = 0; i < b.curves.Count; i++)
            {
                CurveOverride c = b.curves[i];
                using (new GUILayout.HorizontalScope())
                {
                    c.mode = (ListMode)EditorGUILayout.EnumPopup(c.mode, GUILayout.Width(64f));
                    c.name = EditorGUILayout.TextField(c.name, GUILayout.Width(130f));

                    using (new EditorGUI.DisabledGroupScope(c.mode == ListMode.Remove))
                    {
                        c.curve = EditorGUILayout.CurveField(c.curve);
                        if (GUILayout.Button(new GUIContent("Copy", "Copy this curve"), EditorStyles.miniButtonLeft, GUILayout.Width(44f)))
                            curveClipboard = CopyCurve(c.curve);
                        using (new EditorGUI.DisabledGroupScope(curveClipboard == null))
                            if (GUILayout.Button(new GUIContent("Paste", "Paste the copied curve"), EditorStyles.miniButtonRight, GUILayout.Width(44f)))
                                c.curve = CopyCurve(curveClipboard);
                    }

                    if (IconButton(iconRemove)) remove = i;
                }
            }
            if (remove >= 0) b.curves.RemoveAt(remove);

            using (new GUILayout.HorizontalScope())
            {
                if (curveClipboard != null) GUILayout.Label($"Clipboard: {curveClipboard.keys.Length} key(s)", miniGray);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent(" Add Curve", iconAdd.image), EditorStyles.miniButton, GUILayout.Width(96f)))
                    b.curves.Add(new CurveOverride());
            }
        }

        /// <summary> Events list: add/replace/remove animation events by function name </summary>
        private void DrawEvents(OverrideBlock b)
        {
            b.clearEvents = EditorGUILayout.ToggleLeft(new GUIContent("Remove all existing events first", "Start from an empty event list before applying the entries below"), b.clearEvents);

            if (b.events.Count > 0)
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Mode", miniGray, GUILayout.Width(64f));
                    GUILayout.Label("Function", miniGray, GUILayout.Width(130f));
                    GUILayout.Label("Time (normalized)", miniGray);
                }
            }

            int remove = -1;
            for (int i = 0; i < b.events.Count; i++)
            {
                EventOverride e = b.events[i];
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        e.mode = (ListMode)EditorGUILayout.EnumPopup(e.mode, GUILayout.Width(64f));
                        e.function = EditorGUILayout.TextField(e.function, GUILayout.Width(130f));
                        using (new EditorGUI.DisabledGroupScope(e.mode == ListMode.Remove))
                            e.time = EditorGUILayout.Slider(e.time, 0f, 1f);
                        if (IconButton(iconRemove)) remove = i;
                    }

                    if (e.mode == ListMode.Set)
                    {
                        EditorGUILayout.Space(3f);
                        using (new GUILayout.HorizontalScope())
                        {
                            GUILayout.Space(68f);
                            float prev = EditorGUIUtility.labelWidth;
                            EditorGUIUtility.labelWidth = 36f;
                            e.floatParameter = EditorGUILayout.FloatField("Float", e.floatParameter, GUILayout.Width(110f));
                            GUILayout.Space(6f);
                            e.intParameter = EditorGUILayout.IntField("Int", e.intParameter, GUILayout.Width(100f));
                            GUILayout.Space(6f);
                            EditorGUIUtility.labelWidth = 44f;
                            e.stringParameter = EditorGUILayout.TextField("String", e.stringParameter);
                            EditorGUIUtility.labelWidth = prev;
                            GUILayout.Space(ToggleW + 4f);
                        }
                        EditorGUILayout.Space(2f);
                    }
                }
                EditorGUILayout.Space(2f);
            }
            if (remove >= 0) b.events.RemoveAt(remove);

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent(" Add Event", iconAdd.image), EditorStyles.miniButton, GUILayout.Width(96f)))
                    b.events.Add(new EventOverride());
            }
        }

        #endregion

        #region Override Field Helpers

        /// <summary> Section foldout header with an active-override badge </summary>
        private bool Section(bool expanded, string title, int active)
        {
            using (new GUILayout.HorizontalScope())
            {
                expanded = MalbersEditor.Foldout(expanded, title);
                if (active > 0) DrawBadge(active.ToString(), AccentColor);
            }
            return expanded;
        }

        /// <summary> The override dot: filled when the value overrides the clip, empty ring when it is left untouched </summary>
        private static bool OverrideToggle(bool use, string label)
        {
            GUIContent icon = use ? iconOverrideOn : iconOverrideOff;
            icon.tooltip = use ? $"Overriding {label}. Click to leave the clip value untouched" : $"{label} is untouched. Click to override it";

            Color prev = GUI.color;
            if (!use) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            if (IconButton(icon)) use = !use;
            GUI.color = prev;
            return use;
        }

        /// <summary> Override row for a float value </summary>
        private static void OFloat(ref OverrideValue<float> o, GUIContent label)
        {
            using (new GUILayout.HorizontalScope())
            {
                o.use = OverrideToggle(o.use, label.text);
                using (new EditorGUI.DisabledGroupScope(!o.use))
                    o.value = EditorGUILayout.FloatField(label, o.value);
            }
        }

        /// <summary> Override row for a bool value </summary>
        private static void OBool(ref OverrideValue<bool> o, GUIContent label)
        {
            using (new GUILayout.HorizontalScope())
            {
                o.use = OverrideToggle(o.use, label.text);
                using (new EditorGUI.DisabledGroupScope(!o.use))
                    o.value = EditorGUILayout.Toggle(label, o.value);
            }
        }

        /// <summary> Override row for an enum drawn with its own names </summary>
        private static void OEnum<T>(ref OverrideValue<T> o, GUIContent label) where T : Enum
        {
            using (new GUILayout.HorizontalScope())
            {
                o.use = OverrideToggle(o.use, label.text);
                using (new EditorGUI.DisabledGroupScope(!o.use))
                    o.value = (T)EditorGUILayout.EnumPopup(label, o.value);
            }
        }

        /// <summary> Override row for an enum drawn with rig-specific display names (index = enum value) </summary>
        private static void ONamed<T>(ref OverrideValue<T> o, GUIContent label, string[] names) where T : Enum
        {
            using (new GUILayout.HorizontalScope())
            {
                o.use = OverrideToggle(o.use, label.text);
                using (new EditorGUI.DisabledGroupScope(!o.use))
                {
                    int index = Mathf.Clamp(Convert.ToInt32(o.value), 0, names.Length - 1);
                    index = EditorGUILayout.Popup(label, index, names);
                    o.value = (T)Enum.ToObject(typeof(T), index);
                }
            }
        }

        /// <summary> Override row for a Unity Object reference </summary>
        private static void OObject<T>(ref OverrideValue<T> o, GUIContent label) where T : Object
        {
            using (new GUILayout.HorizontalScope())
            {
                o.use = OverrideToggle(o.use, label.text);
                using (new EditorGUI.DisabledGroupScope(!o.use))
                    o.value = (T)EditorGUILayout.ObjectField(label, o.value, typeof(T), false);
            }
        }

        /// <summary> Small icon-only button laid out with the same margins as a single-line field, so it centers on the row's label </summary>
        private static bool IconButton(GUIContent icon)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.Width(ToggleW));
            return GUI.Button(rect, icon, iconStyle);
        }

        /// <summary> Small colored pill label </summary>
        private void DrawBadge(string text, Color color)
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label(text, badgeStyle, GUILayout.ExpandWidth(false));
            GUI.backgroundColor = prev;
        }

        /// <summary> Deep copy of a curve (keys and wrap modes) </summary>
        private static AnimationCurve CopyCurve(AnimationCurve source)
        {
            if (source == null) return null;
            return new AnimationCurve(source.keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
        }

        #endregion

        #region Rig Helpers

        /// <summary> Rig kind of a loaded file </summary>
        private static Rig ToRig(FbxEntry file)
        {
            if (!file.importAnimation) return Rig.None;
            return file.animType switch
            {
                ModelImporterAnimationType.Human => Rig.Humanoid,
                ModelImporterAnimationType.Generic => Rig.Generic,
                ModelImporterAnimationType.Legacy => Rig.Legacy,
                _ => Rig.None,
            };
        }

        /// <summary> Badge text for a rig </summary>
        private static string RigName(Rig rig) => rig switch
        {
            Rig.Humanoid => "Humanoid",
            Rig.Generic => "Generic",
            Rig.Legacy => "Legacy",
            Rig.None => "No Animation",
            _ => "Any",
        };

        /// <summary> Badge color for a rig </summary>
        private static Color RigColor(Rig rig) => rig switch
        {
            Rig.Humanoid => HumanoidColor,
            Rig.Generic => GenericColor,
            Rig.Legacy => LegacyColor,
            Rig.None => WarnColor,
            _ => BadgeGray,
        };

        /// <summary> Display names for Root Rotation > Based Upon, matching Unity's importer per rig </summary>
        private static string[] RotationNames(Rig rig) => rig switch
        {
            Rig.Humanoid => new[] { "Body Orientation", "Original" },
            Rig.Generic or Rig.Legacy => new[] { "Root Node Rotation", "Original" },
            _ => new[] { "Body Orientation / Root Node Rotation", "Original" },
        };

        /// <summary> Display names for Root Position (Y) > Based Upon, matching Unity's importer per rig </summary>
        private static string[] PositionYNames(Rig rig) => rig switch
        {
            Rig.Humanoid => new[] { "Center Of Mass", "Feet", "Original" },
            Rig.Generic or Rig.Legacy => new[] { "Root Node Position", "Feet (Humanoid only)", "Original" },
            _ => new[] { "Center Of Mass / Root Node Position", "Feet (Humanoid only)", "Original" },
        };

        /// <summary> Display names for Root Position (XZ) > Based Upon, matching Unity's importer per rig </summary>
        private static string[] PositionXZNames(Rig rig) => rig switch
        {
            Rig.Humanoid => new[] { "Center Of Mass", "Original" },
            Rig.Generic or Rig.Legacy => new[] { "Root Node Position", "Original" },
            _ => new[] { "Center Of Mass / Root Node Position", "Original" },
        };

        #endregion

        #region Bottom Bar

        /// <summary> Summary line plus Clear and Apply buttons </summary>
        private void DrawBottomBar()
        {
            MalbersEditor.DrawSplitter();

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(4f);
                GUILayout.Label($"{entries.Count} file(s)  ·  {EnabledClips}/{TotalClips} clip(s)  ·  {global.Total} global override(s)", miniGray, GUILayout.Height(ButtonH));
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledGroupScope(entries.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Clear Files", "Remove every loaded FBX (Global overrides are kept)"), GUILayout.Width(110f), GUILayout.Height(ButtonH)))
                        entries.Clear();

                    GUILayout.Space(8f);

                    Color prev = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.55f, 1f, 0.6f, 1f);
                    if (GUILayout.Button(new GUIContent("Apply Overrides", "Write the overrides into the FBX importers and re-import them"), GUILayout.Width(170f), GUILayout.Height(ButtonH)))
                        ApplyOverrides();
                    GUI.backgroundColor = prev;
                }
                GUILayout.Space(4f);
            }

            EditorGUILayout.Space(4f);
        }

        #endregion

        #region Apply

        /// <summary> Writes every resolved override into the importers and re-imports the touched FBX files </summary>
        private void ApplyOverrides()
        {
            if (entries.Count == 0)
            {
                EditorUtility.DisplayDialog("FBX Clip Overrider", "No FBX files loaded.", "OK");
                return;
            }

            int enabled = EnabledClips;
            if (!EditorUtility.DisplayDialog("FBX Clip Overrider",
                $"Apply overrides to {enabled} enabled clip(s) across {entries.Count} FBX file(s)?\n\nEvery FBX that changes will be re-imported.",
                "Apply", "Cancel"))
                return;

            int modifiedFiles = 0, modifiedClips = 0;
            bool cancelled = false;

            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < entries.Count; i++)
                {
                    FbxEntry file = entries[i];
                    if (EditorUtility.DisplayCancelableProgressBar("FBX Clip Overrider", file.fileName, (float)i / entries.Count))
                    {
                        cancelled = true;
                        break;
                    }
                    if (ProcessFile(file, ref modifiedClips)) modifiedFiles++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            string msg = $"{(cancelled ? "Cancelled. " : "Done. ")}{modifiedClips} clip(s) updated across {modifiedFiles} FBX file(s).";
            Debug.Log($"[FBX Clip Overrider] {msg}");
            EditorUtility.DisplayDialog("FBX Clip Overrider", msg, "OK");
        }

        /// <summary> Applies overrides to every enabled clip of one FBX; returns true when the importer was saved </summary>
        private bool ProcessFile(FbxEntry file, ref int modifiedClips)
        {
            if (AssetImporter.GetAtPath(file.assetPath) is not ModelImporter importer)
            {
                Debug.LogWarning($"[FBX Clip Overrider] Could not get the Model Importer for: {file.assetPath}");
                return false;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips.Length == 0) clips = importer.defaultClipAnimations;

            Rig rig = ToRig(file);
            bool dirty = false;

            for (int i = 0; i < clips.Length; i++)
            {
                ClipEntry entry = file.clips.Find(c => c.clipName == clips[i].name);
                if (entry != null && !entry.enabled) continue;

                if (ApplyToClip(clips[i], entry, rig))
                {
                    dirty = true;
                    modifiedClips++;
                }
            }

            if (!dirty) return false;

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            return true;
        }

        /// <summary> Resolves clip-then-global overrides and writes them into one importer clip; returns true when anything changed </summary>
        private bool ApplyToClip(ModelImporterClipAnimation clip, ClipEntry entry, Rig rig)
        {
            OverrideBlock local = entry?.overrides ?? EmptyBlock;
            bool changed = false;

            // Lists
            changed |= ApplyCurves(clip, entry != null && entry.ignoreGlobalCurves ? null : global, local);
            changed |= ApplyEvents(clip, entry != null && entry.ignoreGlobalEvents ? null : global, local);

            // Time
            if (Resolve(local.firstFrame, global.firstFrame, out float firstFrame)) { clip.firstFrame = firstFrame; changed = true; }
            if (Resolve(local.lastFrame, global.lastFrame, out float lastFrame)) { clip.lastFrame = lastFrame; changed = true; }
            if (Resolve(local.wrapMode, global.wrapMode, out WrapMode wrapMode)) { clip.wrapMode = wrapMode; changed = true; }

            // Loop
            if (Resolve(local.loopTime, global.loopTime, out bool loopTime)) { clip.loopTime = loopTime; changed = true; }
            if (Resolve(local.loopPose, global.loopPose, out bool loopPose)) { clip.loopPose = loopPose; changed = true; }
            if (Resolve(local.cycleOffset, global.cycleOffset, out float cycleOffset)) { clip.cycleOffset = cycleOffset; changed = true; }

            // Root Transform Rotation
            if (Resolve(local.bakeRotation, global.bakeRotation, out bool bakeRotation)) { clip.lockRootRotation = bakeRotation; changed = true; }
            if (Resolve(local.rotationBasedUpon, global.rotationBasedUpon, out RotationBasedUpon rotUpon)) { clip.keepOriginalOrientation = rotUpon == RotationBasedUpon.Original; changed = true; }
            if (Resolve(local.rotationOffset, global.rotationOffset, out float rotationOffset)) { clip.rotationOffset = rotationOffset; changed = true; }

            // Root Transform Position (Y)
            if (Resolve(local.bakePositionY, global.bakePositionY, out bool bakeY)) { clip.lockRootHeightY = bakeY; changed = true; }
            if (Resolve(local.positionYBasedUpon, global.positionYBasedUpon, out PositionYBasedUpon yUpon))
            {
                clip.keepOriginalPositionY = yUpon == PositionYBasedUpon.Original;
                clip.heightFromFeet = yUpon == PositionYBasedUpon.Feet;
                changed = true;
            }
            if (Resolve(local.heightOffset, global.heightOffset, out float heightOffset)) { clip.heightOffset = heightOffset; changed = true; }

            // Root Transform Position (XZ)
            if (Resolve(local.bakePositionXZ, global.bakePositionXZ, out bool bakeXZ)) { clip.lockRootPositionXZ = bakeXZ; changed = true; }
            if (Resolve(local.positionXZBasedUpon, global.positionXZBasedUpon, out PositionXZBasedUpon xzUpon)) { clip.keepOriginalPositionXZ = xzUpon == PositionXZBasedUpon.Original; changed = true; }

            // Mask
            bool hasMaskType = Resolve(local.maskType, global.maskType, out ClipAnimationMaskType maskType);
            if (hasMaskType) { clip.maskType = maskType; changed = true; }
            if (Resolve(local.maskSource, global.maskSource, out AvatarMask maskSource))
            {
                clip.maskSource = maskSource;
                if (maskSource != null && !hasMaskType) clip.maskType = ClipAnimationMaskType.CopyFromOther;
                changed = true;
            }

            // Extras
            if (rig == Rig.Humanoid && Resolve(local.mirror, global.mirror, out bool mirror)) { clip.mirror = mirror; changed = true; }
            if (Resolve(local.additiveReferencePose, global.additiveReferencePose, out bool additive)) { clip.hasAdditiveReferencePose = additive; changed = true; }
            if (Resolve(local.additiveReferenceFrame, global.additiveReferenceFrame, out float additiveFrame)) { clip.additiveReferencePoseFrame = additiveFrame; changed = true; }

            return changed;
        }

        /// <summary> Clip override wins, then the global one; false when neither is active </summary>
        private static bool Resolve<T>(OverrideValue<T> local, OverrideValue<T> globalValue, out T value)
        {
            if (local.use) { value = local.value; return true; }
            if (globalValue.use) { value = globalValue.value; return true; }
            value = default;
            return false;
        }

        /// <summary> Applies the global (may be null) then the clip curve lists; returns true when the curve array was rewritten </summary>
        private static bool ApplyCurves(ModelImporterClipAnimation clip, OverrideBlock globalBlock, OverrideBlock local)
        {
            bool clear = local.clearCurves || (globalBlock != null && globalBlock.clearCurves);
            int ops = (globalBlock?.curves.Count ?? 0) + local.curves.Count;
            if (!clear && ops == 0) return false;

            List<ClipAnimationInfoCurve> list = clear || clip.curves == null ? new() : clip.curves.ToList();

            void Apply(CurveOverride co)
            {
                if (string.IsNullOrWhiteSpace(co.name)) return;
                int index = list.FindIndex(x => x.name == co.name);

                if (co.mode == ListMode.Remove)
                {
                    if (index >= 0) list.RemoveAt(index);
                    return;
                }

                ClipAnimationInfoCurve info = new() { name = co.name, curve = CopyCurve(co.curve) };
                if (index >= 0) list[index] = info;
                else list.Add(info);
            }

            if (globalBlock != null) foreach (CurveOverride c in globalBlock.curves) Apply(c);
            foreach (CurveOverride c in local.curves) Apply(c);

            clip.curves = list.ToArray();
            return true;
        }

        /// <summary> Applies the global (may be null) then the clip event lists; returns true when the event array was rewritten </summary>
        private static bool ApplyEvents(ModelImporterClipAnimation clip, OverrideBlock globalBlock, OverrideBlock local)
        {
            bool clear = local.clearEvents || (globalBlock != null && globalBlock.clearEvents);
            int ops = (globalBlock?.events.Count ?? 0) + local.events.Count;
            if (!clear && ops == 0) return false;

            List<AnimationEvent> list = clear || clip.events == null ? new() : clip.events.ToList();

            void Apply(EventOverride eo)
            {
                if (string.IsNullOrWhiteSpace(eo.function)) return;

                if (eo.mode == ListMode.Remove)
                {
                    list.RemoveAll(x => x.functionName == eo.function);
                    return;
                }

                AnimationEvent ev = new()
                {
                    functionName = eo.function,
                    time = eo.time,
                    floatParameter = eo.floatParameter,
                    intParameter = eo.intParameter,
                    stringParameter = eo.stringParameter,
                };

                int index = list.FindIndex(x => x.functionName == eo.function && Mathf.Abs(x.time - eo.time) < 0.001f);
                if (index >= 0) list[index] = ev;
                else list.Add(ev);
            }

            if (globalBlock != null) foreach (EventOverride e in globalBlock.events) Apply(e);
            foreach (EventOverride e in local.events) Apply(e);

            clip.events = list.OrderBy(x => x.time).ToArray();
            return true;
        }

        #endregion
    }
}
#endif
