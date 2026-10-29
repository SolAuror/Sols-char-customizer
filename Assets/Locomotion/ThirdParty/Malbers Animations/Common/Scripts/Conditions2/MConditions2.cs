// Last Change: 9/21/2026 9:40am
using UnityEngine;
using UnityEngine.Events;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Reactions;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Conditions
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/global-components/conditions")]
    [AddComponentMenu("Malbers/Interactions/Conditions2"), DisallowMultipleComponent]
    public class MConditions2 : MonoBehaviour
    {
        [Tooltip("Evaluate the conditions on Enable")]
        public bool EvaluateOnEnable = false;
        public bool EvaluateOnDisable = false;
        public bool Repeat = false;
        [Min(0.01f)] public float repeatTime = 0.1f;

        public GameObjectReference target;
        public Conditions2 conditions;

        public UnityEvent Then = new();
        public UnityEvent Else = new();

        [Tooltip("Reactions invoked when the conditions are True. Their dynamic target is the global Target of this component")]
        public Reaction2 ThenReactions;
        [Tooltip("Reactions invoked when the conditions are False. Their dynamic target is the global Target of this component")]
        public Reaction2 ElseReactions;

        public bool debug;

#pragma warning disable 414
        [HideInInspector, SerializeField] private int SelectedState = -1;
        [HideInInspector, SerializeField] private bool showResponse = true;
        [HideInInspector, SerializeField] private int Editor_Tabs1; //[Reactions][Events] tab of the Response section
#pragma warning restore 414

        /// <summary>GameObject the Then/Else Reactions react on: the global Target, or this GameObject when no Target is set</summary>
        private GameObject ReactionTarget => target.Value != null ? target.Value : gameObject;


        private void OnEnable()
        {
            EnableConditions(true);

            if (EvaluateOnEnable) TryEvaluate(target.Value);

            // MWC — RepeatEvaluate instead of nameof(Evaluate): InvokeRepeating resolves by name and
            // Evaluate is overloaded. Also delay the first tick when EvaluateOnEnable already ran,
            // otherwise the conditions were evaluated twice on the same frame.
            if (Repeat) InvokeRepeating(nameof(RepeatEvaluate), EvaluateOnEnable ? repeatTime : 0f, repeatTime);
        }

        private void OnDisable()
        {
            if (EvaluateOnDisable) TryEvaluate(target.Value);
            CancelInvoke();

            EnableConditions(false);
        }

        // MWC — ConditionCore exposes OnEnable/OnDisable hooks but nothing was calling them
        private void EnableConditions(bool enable)
        {
            if (conditions.conditions == null) return;

            foreach (var condition in conditions.conditions)
            {
                if (condition == null) continue;
                if (enable) condition.OnEnable(); else condition.OnDisable();
            }
        }

        private void RepeatEvaluate() => TryEvaluate(target.Value); // MWC — non overloaded name for InvokeRepeating


        public void Evaluate() => TryEvaluate(target.Value);

        public void Evaluate(UnityEngine.Object target) => TryEvaluate(target);

        // MWC — Payload API. A UnityEvent can only bind methods taking bool/int/float/string/enum/Object,
        // so Evaluate(object) was impossible to pick in the inspector. These concrete overloads are.
        public void Evaluate(int value) => TryEvaluateValue(value);
        public void Evaluate(float value) => TryEvaluateValue(value);
        public void Evaluate(bool value) => TryEvaluateValue(value);
        public void Evaluate(string value) => TryEvaluateValue(value);

        /// <summary>Evaluate using a raw value. Callable from code; from a UnityEvent use the typed overloads.</summary>
        public void Evaluate(object value)
        {
            if (value is UnityEngine.Object obj) TryEvaluate(obj);
            else TryEvaluateValue(value);
        }

        public bool TryEvaluate(UnityEngine.Object target)
        {
            // MWC — the "disabled" early-out used to sit behind `debug`, so the component returned
            // false and skipped the events with debug ON, but returned true and fired Then with it OFF.
            var result = conditions.Evaluate(target);

            if (result) InvokeThen(); else InvokeElse();

            Debugging(result);

            return result;
        }

        // MWC — payload evaluation. The target is passed along too so target-based and value-based
        // conditions can be mixed in the same list.
        public bool TryEvaluateValue(object value)
        {
            var result = conditions.EvaluateValue(target.Value, value);

            if (result) InvokeThen(); else InvokeElse();

            Debugging(result);

            return result;
        }

        /// <summary>Console log with the Conditions result, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("MALBERS_DEBUG")]
        private void Debugging(bool result)
        {
            if (!debug) return;

            if (!conditions.active)
                Debug.Log($"[{name}] Conditions are disabled → the result defaults to [True]", this);

            Debug.Log($"[{name}] → Conditions Result → <B><color={(result ? "green" : "red")}>[{result}] </color></B>", this);
        }


        /// <summary>Invoke the Then Event and the Then Reactions (Reactions use the global Target)</summary>
        public void InvokeThen()
        {
            Then.Invoke();
            ThenReactions.React(ReactionTarget);
        }

        /// <summary>Invoke the Else Event and the Else Reactions (Reactions use the global Target)</summary>
        public void InvokeElse()
        {
            Else.Invoke();
            ElseReactions.React(ReactionTarget);
        }


        public void Target_Set(GameObject target) => this.target.Value = target;

        public void Target_Set(Component target) => this.target.Value = (target != null ? target.gameObject : null);

        public void Target_Clear() => this.target.Value = null;

        public void Pause_Editor() => Debug.Break();

        private void Reset()
        {
            conditions.active = true; //By default the conditions are active
        }
    }

#if UNITY_EDITOR

    [CustomEditor(typeof(MConditions2))]
    public class MConditions2Editor : Editor
    {
        SerializedObject so;
        MConditions2 M;
        SerializedProperty conditions, Target, Then, Else, ThenReactions, ElseReactions, Editor_Tabs1, SelectedState, showResponse,
            EvaluateOnEnable, EvaluateOnDisable, Repeat, repeatTime, debug;

        private static readonly string[] ResponseTabs = { "   Reactions   ", "   Events   " };

        private void OnEnable()
        {
            so = serializedObject;
            M = (MConditions2)target;
            conditions = so.FindProperty("conditions");
            Target = so.FindProperty("target");
            Then = so.FindProperty("Then"); // MWC — was duplicated
            Else = so.FindProperty("Else");
            ThenReactions = so.FindProperty("ThenReactions");
            ElseReactions = so.FindProperty("ElseReactions");
            Editor_Tabs1 = so.FindProperty("Editor_Tabs1");
            debug = so.FindProperty("debug");
            SelectedState = so.FindProperty("SelectedState");
            showResponse = so.FindProperty("showResponse");
            EvaluateOnEnable = so.FindProperty("EvaluateOnEnable");
            EvaluateOnDisable = so.FindProperty("EvaluateOnDisable");
            Repeat = so.FindProperty("Repeat");
            repeatTime = so.FindProperty("repeatTime");

            var allComponents = M.GetComponents<Component>();
            foreach (var component in allComponents)
            {
                // Debug.Log("component = " + component.GetType());
                if (component != null) component.hideFlags = HideFlags.None;
            }
        }
        public override void OnInspectorGUI()
        {
            MalbersEditor.DrawDescription($"Global Conditions. Call MConditions.Evaluate() to invoke the response");

            serializedObject.Update();

            if (Application.isPlaying)
            {
                if (GUILayout.Button("Evaluate All")) M.Evaluate();
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
                EditorGUILayout.PropertyField(conditions);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                showResponse.boolValue = MalbersEditor.Foldout(showResponse.boolValue, "Response (Then-Else)");

                if (showResponse.boolValue)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, ResponseTabs);
                        GUILayout.FlexibleSpace();
                    }
                    if (Editor_Tabs1.intValue == 0) DrawReactions();
                    else DrawEvents();
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>[Reactions] tab: Then/Else Reactions, reacting on the global Target</summary>
        private void DrawReactions()
        {
            EditorGUI.indentLevel++;
            // The Reactions drawer appends the count to the label it receives, so the text is reset on every draw
            _ThenReactionsG.text = "Then";
            _ElseReactionsG.text = "Else";
            EditorGUILayout.PropertyField(ThenReactions, _ThenReactionsG);
            EditorGUILayout.PropertyField(ElseReactions, _ElseReactionsG);
            EditorGUI.indentLevel--;
        }

        /// <summary>[Events] tab: Then/Else Unity Events</summary>
        private void DrawEvents()
        {
            EditorGUILayout.PropertyField(Then);
            EditorGUILayout.PropertyField(Else);
        }

        private readonly GUIContent _ThenReactionsG = new("Then", "Reactions invoked when the conditions are True. Dynamic target: the global Target");
        private readonly GUIContent _ElseReactionsG = new("Else", "Reactions invoked when the conditions are False. Dynamic target: the global Target");
        private readonly GUIContent _OnEnableG = new("Enable", "Evaluate all conditions On Enable");
        private readonly GUIContent _OnDisableG = new("Disable", "Evaluate all conditions On Disable");
        private readonly GUIContent _RepeatG = new("Repeat", $"Evaluate all conditions every x seconds");
    }
#endif

}