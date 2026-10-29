// Last Change: 9/17/2026 5:33pm
using System.Collections.Generic;
using MalbersAnimations.Scriptables;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace MalbersAnimations.IK
{
    /// <summary> How an <see cref="IKSetLibrary"/> is applied to an <see cref="IKManager"/>. </summary>
    public enum IKSetLoadMode
    {
        [InspectorName("Override (Replace all Sets)")]
        Override,
        [InspectorName("Additive (Append Sets)")]
        Additive,
    }

    // MWC — Stores a whole collection of IK Sets as an asset, so the same IK setup can be
    // authored once and applied to any character. Bone orientations/offsets differ from rig to rig,
    // so you typically keep one library per rig (Human, Synty, Mixamo, Wolf…) and swap them on the
    // IKManager instead of re-authoring every Set by hand.
    //
    // Sets stored here must resolve their targets through IKTags (Global Targets on the IKManager),
    // since scene Transform references cannot be serialized inside an asset.
    [CreateAssetMenu(menuName = "Malbers Animations/IK/IK Set Library", fileName = "New IK Set Library")]
    public class IKSetLibrary : ScriptableObject
    {
        [TextArea(2, 5)]
        [Tooltip("What rig/character this library was authored for")]
        public string Description;

        [Tooltip("IK Sets stored in this library")]
        public List<IKSet> sets = new();

        /// <summary> Store the Selected Tab in the inspector</summary>
        [HideInInspector, SerializeField] internal int EditorTabs;
        [HideInInspector, SerializeField] internal int SelectedSet;

        /// <summary> Returns an independent deep copy of every Set in this library.
        /// The library asset itself is never handed out, so nothing can mutate it at runtime. </summary>
        public List<IKSet> CloneSets() => DeepCopy(sets);

        /// <summary> Replaces the content of this library with a deep copy of <paramref name="source"/>. </summary>
        public void StoreSets(List<IKSet> source) => sets = DeepCopy(source);

        /// <summary>
        /// Deep copies a list of IKSets.
        /// Uses Object.Instantiate on a throwaway ScriptableObject because Unity's serializer is the
        /// only thing that duplicates [SerializeReference] graphs (IKProcessors / WeightProcessors)
        /// and UnityEvent persistent calls faithfully. JsonUtility cannot round-trip polymorphic types.
        /// </summary>
        public static List<IKSet> DeepCopy(List<IKSet> source)
        {
            var result = new List<IKSet>();

            if (source == null || source.Count == 0) return result;

            var holder = CreateInstance<IKSetLibrary>();
            holder.sets = source;

            var clone = Instantiate(holder);

            if (clone.sets != null) result.AddRange(clone.sets);

            //Detach the lists before destroying the throwaway wrappers
            holder.sets = null;
            clone.sets = null;

            DestroyTemp(clone);
            DestroyTemp(holder);

            return result;
        }

        private static void DestroyTemp(Object obj)
        {
            if (obj == null) return;

            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private void OnValidate()
        {
            if (sets == null) return;

            //Keep the Sets valid so the inspector never draws a null Set
            foreach (var set in sets)
            {
                if (set == null) continue;

                set.IKProcesors ??= new();
                set.weightProcessors ??= new();
                set.Targets ??= new TransformReference[0];
            }
        }
    }


#if UNITY_EDITOR
    // MWC — Inspector for the IK Set Library. Reuses IKSetDrawer so a Set looks and behaves
    // exactly the same here as it does on the IKManager.
    [CustomEditor(typeof(IKSetLibrary))]
    public class IKSetLibraryEditor : Editor
    {
        private IKSetLibrary m;
        private IKSetDrawer drawer;
        private ReorderableList Reo_Sets;

        private SerializedProperty Description, Sets, EditorTabs, SelectedSet;

        private void OnEnable()
        {
            m = (IKSetLibrary)target;

            Description = serializedObject.FindProperty("Description");
            Sets = serializedObject.FindProperty("sets");
            EditorTabs = serializedObject.FindProperty("EditorTabs");
            SelectedSet = serializedObject.FindProperty("SelectedSet");

            drawer = new IKSetDrawer(m); //No IKManager: this Set lives inside an asset

            Reo_Sets = new ReorderableList(serializedObject, Sets, true, true, true, true)
            {
                drawHeaderCallback = Draw_Header_Set,
                drawElementCallback = Draw_Element,
                onAddCallback = OnAddCallBack,
                onRemoveCallback = OnRemoveCallBack,
                onSelectCallback = (list) => { SelectedSet.intValue = list.index; }
            };
        }

        private void Draw_Header_Set(Rect rect)
        {
            var r = new Rect(rect);
            var WeightRect = new Rect(rect) { x = r.width - 30, width = 65 };
            var a = new Rect(rect) { width = 65 };

            EditorGUI.LabelField(a, new GUIContent("Active", "Enable Disable the IK Set"));
            r.x += 60;
            r.width = 60;
            EditorGUI.LabelField(r, new GUIContent("IK Set", "Name of the IK Set"));
            EditorGUI.LabelField(WeightRect, new GUIContent("Weight", "Default Weight of the IK Set"));
        }

        private void Draw_Element(Rect rect, int index, bool isActive, bool isFocused)
        {
            rect.y += 2;
            if (Sets.arraySize <= index) return;

            var ikSet = Sets.GetArrayElementAtIndex(index);
            var active = ikSet.FindPropertyRelative("active");
            var Name = ikSet.FindPropertyRelative("name");
            var weight = ikSet.FindPropertyRelative("weight");

            var height = EditorGUIUtility.singleLineHeight;

            var activeRect = new Rect(rect.x, rect.y, 20, height);
            var NameRect = new Rect(rect.x + 35, rect.y, rect.width * 0.7f - 35, height);
            var weightRect = new Rect(rect.width - rect.width * 0.3f + 25, rect.y, rect.width * 0.3f + 12f, height);

            EditorGUIUtility.labelWidth = 30;

            var dC = GUI.color;
            if (index == SelectedSet.intValue) GUI.color = Color.yellow;

            active.boolValue = EditorGUI.Toggle(activeRect, GUIContent.none, active.boolValue);
            EditorGUI.PropertyField(NameRect, Name, GUIContent.none);
            EditorGUI.PropertyField(weightRect, weight, new GUIContent(" "));

            EditorGUIUtility.labelWidth = 0;
            GUI.color = dC;
        }

        private void OnAddCallBack(ReorderableList list)
        {
            Undo.RecordObject(m, "Add IK Set");

            m.sets ??= new();
            m.sets.Add(new IKSet()
            {
                name = new StringReference($"new IK Set {list.count}") { UseConstant = true },
                EnableTime = 0.25f,
                DisableTime = 0.25f
            });

            EditorUtility.SetDirty(m);
            serializedObject.Update();

            SelectedSet.intValue = m.sets.Count - 1;
            drawer.ClearCache();
        }

        private void OnRemoveCallBack(ReorderableList list)
        {
            ReorderableList.defaultBehaviours.DoRemoveButton(list);
            serializedObject.ApplyModifiedProperties();

            SelectedSet.intValue = Mathf.Clamp(SelectedSet.intValue, -1, m.sets.Count - 1);
            drawer.ClearCache(); //Indices shifted: the cached lists point at stale paths
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Reusable collection of IK Sets. Load it into an IK Manager to set up a character in one click");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                EditorGUILayout.PropertyField(Description);

            Reo_Sets.DoLayoutList();

            var index = SelectedSet.intValue;

            if (index > -1 && index < Sets.arraySize)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var selectedSet = Sets.GetArrayElementAtIndex(index);

                    MalbersEditor.Foldout_Bold(true, $"[{m.sets[index].Name} - IK Set]", MTools.MBlue * 2, true);

                    drawer.DrawSet(selectedSet, m.sets[index], EditorTabs);
                }
            }
            else if (Sets.arraySize > 0)
            {
                EditorGUILayout.HelpBox("Select an IK Set from the list to edit it", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
