// Last Change: 9/17/2026 5:33pm
using UnityEngine;
using UnityEngine.Events;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Conditions
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/global-components/conditions")]
    [AddComponentMenu("Malbers/Interactions/Conditions Multiple"), DisallowMultipleComponent]
    public class MConditionsMultiple : MonoBehaviour
    {
        [Tooltip("Evaluate the conditions on Enable")]
        public bool EvaluateOnEnable = false;
        public bool EvaluateOnDisable = false;
        public bool Repeat = false;
        [Min(0.01f)] public float repeatTime = 0.1f;

        public GameObjectReference target;

        [Tooltip("Current active condition index")]
        [SerializeField] private int currentConditionIndex = 0;

        [System.Serializable]
        public class ConditionWithResponse
        {
            public Conditions2 conditions = new Conditions2(0); // MWC — ctor sets active = true
            public UnityEvent Then = new UnityEvent();
            public UnityEvent Else = new UnityEvent();
            [HideInInspector] public bool lastResult; // Track previous state
            [HideInInspector] public bool isCompleted = false; // Track if condition is completed
        }

        public List<ConditionWithResponse> conditionsList = new List<ConditionWithResponse>();

        public bool debug;

#pragma warning disable 414
        [HideInInspector, SerializeField] private int SelectedState = -1;
#pragma warning restore 414

        // MWC — one shot migration. Conditions2 is a struct, so every element serialized before
        // this fix has active == false, and the old inspector never drew the toggle, meaning
        // Valid was false and every condition silently evaluated to True. Turn them on once.
        [HideInInspector, SerializeField] private bool activeMigrated = false;

        private void OnValidate()
        {
            if (activeMigrated) return;
            activeMigrated = true;

            for (int i = 0; i < conditionsList.Count; i++)
                conditionsList[i].conditions.active = true;
        }

        private void OnEnable()
        {
            // Reset to the first condition on enable
            currentConditionIndex = 0;
            ResetAllConditions();

            EnableConditions(true);

            if (EvaluateOnEnable) EvaluateCurrentCondition();

            // MWC — delay the first tick when EvaluateOnEnable already ran, it was evaluating twice
            if (Repeat) InvokeRepeating(nameof(EvaluateCurrentCondition), EvaluateOnEnable ? repeatTime : 0f, repeatTime);
        }

        private void OnDisable()
        {
            if (EvaluateOnDisable) EvaluateCurrentCondition();
            CancelInvoke();

            EnableConditions(false);
        }

        // MWC — ConditionCore exposes OnEnable/OnDisable hooks but nothing was calling them
        private void EnableConditions(bool enable)
        {
            for (int i = 0; i < conditionsList.Count; i++)
            {
                var set = conditionsList[i].conditions.conditions;
                if (set == null) continue;

                foreach (var condition in set)
                {
                    if (condition == null) continue;
                    if (enable) condition.OnEnable(); else condition.OnDisable();
                }
            }
        }

        // Reset all conditions to not completed
        public void ResetAllConditions()
        {
            for (int i = 0; i < conditionsList.Count; i++)
            {
                conditionsList[i].isCompleted = false;
                conditionsList[i].lastResult = false;
            }
        }

        // Evaluate all conditions with the target
        public void EvaluateAll()
        {
            for (int i = 0; i < conditionsList.Count; i++)
            {
                EvaluateConditionAtIndex(i);
            }
        }

        // Evaluate only the current active condition
        public void EvaluateCurrentCondition()
        {
            if (conditionsList.Count == 0) return;

            EvaluateConditionAtIndex(currentConditionIndex);
        }

        // Evaluate a specific condition by index
        public void EvaluateConditionAtIndex(int index)
        {
            if (index < 0 || index >= conditionsList.Count)
            {
                if (debug) Debug.LogWarning($"[{name}] Invalid condition index: {index}", this);
                return;
            }

            var conditionResponse = conditionsList[index];
            bool result = conditionResponse.conditions.Evaluate(target.Value);

            // Update the result
            conditionResponse.lastResult = result;

            // If condition is true, mark it as completed and move to next
            if (result)
            {
                conditionResponse.Then.Invoke();

                if (!conditionResponse.isCompleted)
                {
                    conditionResponse.isCompleted = true;

                    if (debug) Debug.Log($"[{name}] Condition [{index}] COMPLETED. Moving to next condition.", this);

                    // Move to the next condition
                    MoveToNextCondition();
                }
            }
            else
            {
                conditionResponse.Else.Invoke();

                if (debug) Debug.Log($"[{name}] Condition [{index}] evaluated FALSE. Staying on this condition.", this);
            }
        }

        // Move to the next condition or loop back to first
        public void MoveToNextCondition()
        {
            currentConditionIndex++;

            // If we reached the end, loop back to first
            if (currentConditionIndex >= conditionsList.Count)
            {
                currentConditionIndex = 0;

                // Optional: Reset all conditions when we loop back
                ResetAllConditions();

                if (debug) Debug.Log($"[{name}] Reached the end of conditions. Looping back to first condition.", this);
            }

            if (debug) Debug.Log($"[{name}] Now on condition [{currentConditionIndex}]", this);
        }

        // Reset and start from the first condition
        public void RestartSequence()
        {
            currentConditionIndex = 0;
            ResetAllConditions();

            if (debug) Debug.Log($"[{name}] Sequence restarted. Now on condition [0]", this);
        }

        // Utility methods
        public void Evaluate_OnTrue(bool value)
        {
            if (value) EvaluateCurrentCondition();
        }

        public void Evaluate_OnFalse(bool value)
        {
            if (!value) EvaluateCurrentCondition();
        }

        public void Evaluate_OnInt(int value)
        {
            if (value > 0) EvaluateCurrentCondition();
        }

        public void Pause_Editor() => Debug.Break();
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(MConditionsMultiple))]
    public class MConditionsMultipleEditor : Editor
    {
        SerializedObject so;
        MConditionsMultiple M;
        SerializedProperty conditionsList, Target, SelectedState, EvaluateOnEnable, EvaluateOnDisable, Repeat, repeatTime, debug, currentConditionIndex;

        private void OnEnable()
        {
            so = serializedObject;
            M = (MConditionsMultiple)target;
            conditionsList = so.FindProperty("conditionsList");
            Target = so.FindProperty("target");
            debug = so.FindProperty("debug");
            SelectedState = so.FindProperty("SelectedState");
            EvaluateOnEnable = so.FindProperty("EvaluateOnEnable");
            EvaluateOnDisable = so.FindProperty("EvaluateOnDisable");
            Repeat = so.FindProperty("Repeat");
            repeatTime = so.FindProperty("repeatTime");
            currentConditionIndex = so.FindProperty("currentConditionIndex");

            var allcomponents = M.GetComponents<Component>();
            foreach (var component in allcomponents)
            {
                if (component != null) component.hideFlags = HideFlags.None; // MWC — null when a script is missing
            }
        }

        public override void OnInspectorGUI()
        {
            MalbersEditor.DrawDescription($"Sequential Conditions. When one condition is completed, it moves to the next.");

            serializedObject.Update();

            if (Application.isPlaying)
            {
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Evaluate Current")) M.EvaluateCurrentCondition();
                    if (GUILayout.Button("Evaluate All")) M.EvaluateAll();
                    if (GUILayout.Button("Restart Sequence")) M.RestartSequence();
                }

                EditorGUILayout.LabelField($"Current Active Condition: {currentConditionIndex.intValue + 1} of {M.conditionsList.Count}", EditorStyles.boldLabel);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUIUtility.labelWidth = 60;
                    EditorGUILayout.PropertyField(Target);
                    EditorGUIUtility.labelWidth = 0;
                    EvaluateOnEnable.boolValue = GUILayout.Toggle(EvaluateOnEnable.boolValue, _OnEnableG, EditorStyles.miniButton, GUILayout.Width(57));
                    EvaluateOnDisable.boolValue = GUILayout.Toggle(EvaluateOnDisable.boolValue, _OnDisableG, EditorStyles.miniButton, GUILayout.Width(57));
                    Repeat.boolValue = GUILayout.Toggle(Repeat.boolValue, _RepeatG, EditorStyles.miniButton, GUILayout.Width(57));
                    if (Repeat.boolValue)
                    {
                        EditorGUIUtility.labelWidth = 60;
                        EditorGUILayout.PropertyField(repeatTime, GUIContent.none, GUILayout.Width(40));
                    }

                    MalbersEditor.DrawDebugIcon(debug);
                }

                DrawConditionsList();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawConditionsList()
        {
            EditorGUILayout.LabelField("Conditions with Responses", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            for (int i = 0; i < conditionsList.arraySize; i++)
            {
                SerializedProperty conditionItem = conditionsList.GetArrayElementAtIndex(i);
                bool isCurrentCondition = (i == currentConditionIndex.intValue);

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    // Highlight the current active condition
                    if (Application.isPlaying && isCurrentCondition)
                    {
                        EditorGUILayout.LabelField("▶ CURRENT ACTIVE CONDITION", EditorStyles.boldLabel);
                    }

                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"Condition {i + 1}", EditorStyles.boldLabel);

                        if (GUILayout.Button("Remove", GUILayout.Width(60)))
                        {
                            conditionsList.DeleteArrayElementAtIndex(i);
                            serializedObject.ApplyModifiedProperties();
                            return;
                        }
                    }

                    // MWC — draw the Conditions2 itself so its Conditions2Drawer runs. Drawing the
                    // inner array directly hid the "active" toggle, and since Conditions2 is a struct
                    // it defaults to active == false → Valid false → every condition returned True.
                    SerializedProperty conditions = conditionItem.FindPropertyRelative("conditions");
                    EditorGUILayout.PropertyField(conditions, new GUIContent("Conditions"), true);

                    // MWC — per element foldout. A single shared bool made every element expand together.
                    conditionItem.isExpanded = MalbersEditor.Foldout(conditionItem.isExpanded, "Response (Then-Else)");

                    if (conditionItem.isExpanded)
                    {
                        EditorGUILayout.PropertyField(conditionItem.FindPropertyRelative("Then"));
                        EditorGUILayout.PropertyField(conditionItem.FindPropertyRelative("Else"));
                    }

                    // In Play mode, show the current result and completion status
                    if (Application.isPlaying)
                    {
                        EditorGUI.BeginDisabledGroup(true);

                        string resultText = M.conditionsList[i].lastResult ? "TRUE" : "FALSE";
                        string completedText = M.conditionsList[i].isCompleted ? "COMPLETED" : "Not Completed";

                        using (new GUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField($"Result: {resultText}",
                                M.conditionsList[i].lastResult ?
                                    EditorStyles.boldLabel : EditorStyles.label);

                            EditorGUILayout.LabelField($"Status: {completedText}",
                                M.conditionsList[i].isCompleted ?
                                    EditorStyles.boldLabel : EditorStyles.label);
                        }

                        EditorGUI.EndDisabledGroup();

                        if (GUILayout.Button($"Test Condition {i}"))
                        {
                            M.EvaluateConditionAtIndex(i);
                        }
                    }
                }
            }

            EditorGUI.indentLevel--;

            if (GUILayout.Button("Add New Condition"))
            {
                conditionsList.arraySize++;

                // MWC — arraySize++ zero-inits the element, and Conditions2 is a struct, so
                // active would come up false and the whole set would evaluate to True.
                var newItem = conditionsList.GetArrayElementAtIndex(conditionsList.arraySize - 1);
                newItem.FindPropertyRelative("conditions").FindPropertyRelative("active").boolValue = true;
                newItem.isExpanded = true;

                serializedObject.ApplyModifiedProperties();
            }
        }

        private readonly GUIContent _OnEnableG = new("Enable", "Evaluate all conditions On Enable");
        private readonly GUIContent _OnDisableG = new("Disable", "Evaluate all conditions On Disable");
        private readonly GUIContent _RepeatG = new("Repeat", $"Evaluate all conditions every x seconds");
    }
#endif
}