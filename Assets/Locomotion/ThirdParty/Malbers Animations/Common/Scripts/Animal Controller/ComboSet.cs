// Last Change: 9/19/2026 9:54pm
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

// MWC: Scriptable, shareable variant of the Combo Manager's combos.
// Holds the combo sequences for a single ModeID with per-State variations
// (e.g. a different combo on the ground vs in the air) and can be played from
// anywhere via Play(ComboManager manager, int branch).
namespace MalbersAnimations.Controller
{
    /// <summary>
    /// Shareable Combo asset. Stores the combo sequences for a single <see cref="ModeID"/>
    /// with per-State variations. The same asset can be reused by many characters: each
    /// <see cref="ComboManager"/> clones it at runtime so animals never share mutable state.
    /// </summary>
    [CreateAssetMenu(fileName = "New Combo Set", menuName = "Malbers Animations/Combo Set", order = 2000)]
    public class ComboSet : ScriptableObject
    {
        [Tooltip("The Mode ID this Combo Set drives (e.g. Attack). It is applied to every variation.")]
        public ModeID Mode;

        [Tooltip("Combo variations for this Mode. Each variation is limited by its States list " +
            "(e.g. one for the Ground states, one for the Air/Fall state).")]
        public List<Combo> combos = new();

        /// <summary>
        /// Ensures the manager holds a runtime clone of this set, selects the variation that
        /// matches the animal's current State, and tries to play the given branch.
        /// </summary>
        public bool Play(ComboManager manager, int branch)
        {
            if (manager == null)
            {
                Debug.LogWarning($"[{name}] ComboSet.Play called with a null ComboManager", this);
                return false;
            }

            manager.LoadComboSet(this); //Ensure a per-manager runtime clone exists

            //Only (re)select the active combo when switching to THIS set, so mashing Play does
            //not reset an in-progress combo. State-based variation swapping (ground <-> air) is
            //handled by the manager's OnStateEnter once a combo of this set is active.
            //MWC — compare by Source (asset identity), not by Mode: another combo can share this Mode ID.
            if (manager.ActiveCombo == null || manager.ActiveCombo.Source != this)
                manager.SetActiveCombo(this);

            return manager.TryPlay(branch);
        }

        /// <summary> Finds the ComboManager on the GameObject (checks itself, its parents and children) and plays the combo. </summary>
        public bool Play(GameObject gameObject, int branch)
        {
            if (gameObject == null)
            {
                Debug.LogWarning($"[{name}] ComboSet.Play called with a null GameObject", this);
                return false;
            }

            var manager = gameObject.FindComponent<ComboManager>();

            if (manager == null)
            {
                Debug.LogWarning($"[{name}] ComboSet.Play: no ComboManager found on [{gameObject.name}], its parents or children", gameObject);
                return false;
            }

            return Play(manager, branch);
        }

        /// <summary> Finds the ComboManager on the Component's GameObject (checks itself, its parents and children) and plays the combo. </summary>
        public bool Play(Component component, int branch)
        {
            if (component == null)
            {
                Debug.LogWarning($"[{name}] ComboSet.Play called with a null Component", this);
                return false;
            }

            //A ComboManager passed as a Component should not trigger a search
            if (component is ComboManager manager) return Play(manager, branch);

            return Play(component.gameObject, branch);
        }

        /// <summary> Writes the asset-level Mode into every variation so per-combo Mode stays consistent. </summary>
        internal void StampMode()
        {
            if (combos == null) return;
            foreach (var combo in combos)
                if (combo != null) combo.Mode = Mode;
        }

        private void OnValidate() => StampMode();
    }

    #region INSPECTOR
#if UNITY_EDITOR
    /// <summary> Inherits ComboEditor so the asset inspector looks and behaves exactly like the
    /// original Combo Manager (same Reorderable Lists for the combos and their sequences) </summary>
    [CustomEditor(typeof(ComboSet))]
    public class ComboSetEditor : ComboEditor
    {
        private ComboSet S;
        private SerializedProperty setMode, setCombos;
        private ReorderableList VariationsReor;

        // Editor-Only reference used to resolve the Mode's Ability names on the sequence popups.
        // It is NOT serialized into the asset.
        private MAnimal previewAnimal;

        private GUIContent stateIcon;

        protected override void OnEnable() //Do NOT call base.OnEnable: the ComboManager properties do not exist on the asset
        {
            S = target as ComboSet;
            if (S == null) return;

            setMode = serializedObject.FindProperty("Mode");
            setCombos = serializedObject.FindProperty("combos");

            DrawVariationList();
        }

        /// <summary> Resolve the Mode from the Preview Animal so the Ability popups work like on the Combo Manager </summary>
        protected override Mode GetSequenceMode(int ComboIndex)
            => previewAnimal != null ? previewAnimal.Mode_Get(S.Mode) : null;

        private void DrawVariationList()
        {
            VariationsReor = new ReorderableList(serializedObject, setCombos, true, true, true, true)
            {
                drawHeaderCallback = (rect) =>
                {
                    var IDIndex = new Rect(rect.x, rect.y, 45, EditorGUIUtility.singleLineHeight);
                    var IDName = new Rect(rect.x + 45, rect.y, rect.width - 45, EditorGUIUtility.singleLineHeight);

                    EditorGUI.LabelField(IDIndex, "Index");
                    EditorGUI.LabelField(IDName, " Name");
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    var element = setCombos.GetArrayElementAtIndex(index);
                    var Name = element.FindPropertyRelative("Name");
                    var States = element.FindPropertyRelative("States");
                    var active = element.FindPropertyRelative("active");

                    rect.y += 2;

                    var IDActive = new Rect(rect.x, rect.y, 25, EditorGUIUtility.singleLineHeight);
                    var IDIndex = new Rect(rect.x + 20, rect.y, 25, EditorGUIUtility.singleLineHeight);
                    var IDName = new Rect(rect.x + 25 + 20, rect.y, rect.width - 25 - 20 - 25, EditorGUIUtility.singleLineHeight);

                    EditorGUI.PropertyField(IDActive, active, GUIContent.none);
                    EditorGUI.LabelField(IDIndex, "(" + index.ToString() + ")");
                    EditorGUI.PropertyField(IDName, Name, GUIContent.none);

                    //Show the states icon like the Combo Manager does (state-limited variations must be higher in the list)
                    var StatesIDs = States.FindPropertyRelative("IDs");
                    if (StatesIDs != null && StatesIDs.arraySize > 0)
                    {
                        var UpRect = new Rect(rect.x + rect.width - 15, rect.y, 25, EditorGUIUtility.singleLineHeight);

                        if (stateIcon == null)
                        {
                            var img_State = EditorGUIUtility.ObjectContent(CreateInstance<StateID>(), typeof(StateID)).image;
                            stateIcon = new(img_State) { tooltip = "Variations with states limitation must be higher in the list" };
                        }

                        EditorGUI.LabelField(UpRect, stateIcon);
                    }
                },

                onRemoveCallback = (list) =>
                {
                    setCombos.DeleteArrayElementAtIndex(list.index);
                    list.index = Mathf.Clamp(list.index - 1, 0, setCombos.arraySize - 1);
                    EditorUtility.SetDirty(target);
                }
            };
        }

        public override void OnInspectorGUI()
        {
            if (S == null) return;

            serializedObject.Update();

            MalbersEditor.DrawDescription("Shareable Combo Sequences for a single Mode ID. Add a variation per State group (e.g. Ground and Air)\n" +
                "Play from anywhere using ComboSet.Play(ComboManager, int Branch)");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(setMode);

                previewAnimal = (MAnimal)EditorGUILayout.ObjectField(
                    new GUIContent("Preview Character", "Editor-Only. Used to show the Mode's Ability names on the sequences. It is not saved on the asset"),
                    previewAnimal, typeof(MAnimal), true);

                if (previewAnimal == null)
                    EditorGUILayout.HelpBox("Assign a Preview Animal to show the Ability names and popups on the sequences", MessageType.Info);

                VariationsReor.DoLayoutList();

                if (VariationsReor.index == -1 && setCombos.arraySize > 0) VariationsReor.index = 0; //Select the first variation by default

                int IndexCombo = VariationsReor.index;

                if (IndexCombo != -1 && IndexCombo < setCombos.arraySize)
                {
                    var combo = setCombos.GetArrayElementAtIndex(IndexCombo);

                    if (combo != null)
                    {
                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            var name = combo.FindPropertyRelative("Name");

                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(combo, new GUIContent($"[{name.stringValue}] Combo"), false);
                            EditorGUI.indentLevel--;

                            if (combo.isExpanded)
                            {
                                var OnComboFinished = combo.FindPropertyRelative("OnComboFinished");
                                var OnComboInterrupted = combo.FindPropertyRelative("OnComboInterrupted");
                                var DisableOnInterrupt = combo.FindPropertyRelative("DisableOnInterrupt");
                                var DisableOnFinish = combo.FindPropertyRelative("DisableOnFinish");
                                var CoolDown = combo.FindPropertyRelative("CoolDown");
                                var states = combo.FindPropertyRelative("States");
                                var EnableOn = combo.FindPropertyRelative("EnableOn");

                                using (new GUILayout.VerticalScope(StyleGreen))
                                {
                                    EditorGUILayout.LabelField("Green Sequences are combo starters", EditorStyles.boldLabel);
                                }
                                EditorGUI.indentLevel++;
                                EditorGUILayout.PropertyField(states);
                                EditorGUI.indentLevel--;

                                EditorGUILayout.PropertyField(CoolDown);
                                EditorGUILayout.PropertyField(DisableOnFinish);
                                EditorGUILayout.PropertyField(DisableOnInterrupt);

                                EditorGUI.indentLevel++;
                                EditorGUILayout.PropertyField(EnableOn);
                                EditorGUI.indentLevel--;

                                EditorGUILayout.LabelField("Combo Sequence List", EditorStyles.boldLabel);
                                var sequence = combo.FindPropertyRelative("Sequence");

                                if (setMode.objectReferenceValue)
                                    DrawSequence(IndexCombo, combo, sequence, false); //Reuses the exact same Reorderable List as the Combo Manager
                                else
                                    EditorGUILayout.HelpBox("Assign a Mode to edit the sequences", MessageType.Warning);

                                //OnComboFinished.isExpanded = MalbersEditor.Foldout(OnComboFinished.isExpanded, "Events");

                                //if (OnComboFinished.isExpanded)
                                //{
                                //    EditorGUILayout.PropertyField(OnComboFinished);
                                //    EditorGUILayout.PropertyField(OnComboInterrupted);
                                //}
                            }
                        }
                    }
                }
            }

            if (serializedObject.ApplyModifiedProperties())
                S.StampMode(); //Keep every variation's Mode in sync with the asset Mode
        }
    }
#endif
    #endregion
}
