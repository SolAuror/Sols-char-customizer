// Last Change: 10/16/2026 4:31pm
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace MalbersAnimations.IK
{
    [CreateAssetMenu(menuName = "Malbers Animations/IK/IK Processor Profile", fileName = "New IK Profile")]
    public class IKSetProfile : ScriptableObject
    {
        public IKSet set;

        public int EditorTabs;

        internal void Initialize(IKManager iKManager) => set.Initialize(iKManager);

        internal void ValidateIK(IKManager manager) => set.OnValidate(manager);
    }


    //Draw the IK Set in the Inspector
#if UNITY_EDITOR

    [CustomEditor(typeof(IKSetProfile))]
    public class IKProcessorProfileEditor : Editor
    {
        //MWC — the whole IK Set UI now comes from the shared IKSetDrawer (also used by IKManager & IKSetLibrary)
        private IKSetDrawer drawer;

        private SerializedProperty set, EditorTabs;

        private IKSetProfile m;

        /// <summary>Character used to draw this Profile's Processor handles in the Scene view (shared by every Profile inspector, not saved).</summary>
        private static IKManager PreviewManager;

        private void OnEnable()
        {
            m = (IKSetProfile)target;

            set = serializedObject.FindProperty("set");
            EditorTabs = serializedObject.FindProperty("EditorTabs");

            drawer = new IKSetDrawer(m); //No IKManager: this Set lives inside an asset

            SceneView.duringSceneGui += OnPreviewSceneGUI;
        }

        /// <summary>Stops drawing the preview handles.</summary>
        private void OnDisable() => SceneView.duringSceneGui -= OnPreviewSceneGUI;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                MalbersEditor.Foldout_Bold(true, m.name, MTools.MBlue * 2, true);

                DrawPreviewField();

                // ── Paste from IKManager clipboard ────────────────────────────────
                DrawPasteFromClipboardButton();
                // ─────────────────────────────────────────────────────────────────

                drawer.DrawSet(set, m.set, EditorTabs);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Draws the Preview IK Manager field.</summary>
        private void DrawPreviewField()
        {
            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                PreviewManager = (IKManager)EditorGUILayout.ObjectField(new GUIContent("Preview",
                    "Character (IK Manager) used to show the selected Processor's handles in the Scene view while this Profile is selected.\n" +
                    "Lets you tune the offsets from the asset instead of selecting the Weapon or Item.\n" +
                    "Edits go to this asset: if the Item uses a Runtime copy of the Profile in Play Mode, disable [Runtime IK] to see them live"),
                    PreviewManager, typeof(IKManager), true);

                if (cc.changed) SceneView.RepaintAll();
            }
        }

        /// <summary>Draws the selected Processor handles of this Profile on the Preview character.</summary>
        private void OnPreviewSceneGUI(SceneView view)
        {
            if (PreviewManager == null || m == null || m.set == null) return;

            var animator = PreviewManager.animator;
            var set = m.set;

            if (animator == null || !set.active || !set.Gizmos || set.Processors == null) return;

            var index = set.SelectedIKProcessor;
            if (index < 0 || index >= set.Processors.Count) return;

            var link = set.Processors[index];
            if (link == null || !link.Active) return;

            //Handles need an Owner for its Scale Multiplier (Initialize only runs in Play Mode; while playing keep the live one)
            if (!Application.isPlaying || set.Owner == null) set.Owner = PreviewManager;

            link.OnSceneGUI(set, animator, m, index);
        }

        /// <summary>
        /// Draws a button that pastes IKProcessors and WeightProcessors from the
        /// IKSetCopyPaste clipboard into this profile's IKSet, leaving Tags untouched.
        /// </summary>
        private void DrawPasteFromClipboardButton()
        {
            if (!IKSetCopyPaste.HasClipboard) return;

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);

            if (GUILayout.Button(new GUIContent(
                $"Paste IK Set from Clipboard  [{IKSetCopyPaste.ClipboardSetName}]",
                "Pastes the IKProcessors and WeightProcessors copied from an IKManager.\nTargets (Tags) are kept as-is.")))
            {
                Undo.RecordObject(m, "Paste IKSet Processors");
                var sourceName = IKSetCopyPaste.ClipboardSetName; // capture before paste clears it
                IKSetCopyPaste.PasteProcessorsInto(m.set);
                EditorUtility.SetDirty(m);
                serializedObject.Update();
                drawer.ClearCache(); //MWC — the processor lists were replaced
                Debug.Log($"[IKSetProfile] Pasted processors from \"{sourceName}\" into \"{m.name}\".");
            }

            GUI.backgroundColor = prevColor;
        }
    }
#endif
}
