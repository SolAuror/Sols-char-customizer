// Last Change: 9/24/2026 1:40pm
using MalbersAnimations.Scriptables;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Check Stat", order = 4)]
    public class CheckStatDecision : MAIDecision
    {
        public override string DisplayName => "General/Check Stat";

        public enum checkStatOption { Compare, CompareNormalized, IsInmune, Regenerating, Degenerating, IsEmpty, IsFull, IsActive, ValueChanged, ValueReduced, ValueIncreased }

        [Tooltip("Check the Decision on the Animal(Self) or the Target(Target)")]
        public Affected checkOn = Affected.Self;
        /// <summary>Range for Looking forward and Finding something</summary>

        [Tooltip("Stat you want to find")]
        public StatID Stat;
        [Tooltip("What do you want to do with the Stat?")]
        public checkStatOption Option = checkStatOption.Compare;
        [Tooltip("(Option Compare Only) Type of the comparation")]
        public ComparerNumber StatIs = ComparerNumber.Less;
        public float Value;
        [Tooltip("(Option Compare Only) Value to Compare the Stat")]

        [ContextMenuItem("Recover Value", "RecoverValue")]
        public FloatReference m_Value = new FloatReference();

        [Space, Tooltip("Uses TryGet Value in case you don't know if your target or your animal has the Stat you are looking for. Disabling this Improves performance")]
        public bool TryGetValue = true;

        //MWC: Per-agent runtime state. Lives on the brain (not this shared SO) so each AI stores its own baseline value.
        private class StatState : BrainState
        {
            public float InitialValue;      //Stat value captured on PrepareDecision, used by ValueChanged/Reduced/Increased
        }

        public override void PrepareDecision(MAnimalBrain brain, int Index)
        {
            //Store the Value the Stat has starting this Decision
            brain.DecisionState<StatState>(Index).InitialValue = 0; //MWC: reset per activation (typed state persists across re-entries)

            switch (checkOn)
            {
                case Affected.Self:
                    if (TryGetValue)
                    {
                        if (brain.AnimalStats.TryGetValue(Stat.ID, out var st))
                            brain.DecisionState<StatState>(Index).InitialValue = st.Value;
                    }
                    else
                    {
                        brain.DecisionState<StatState>(Index).InitialValue = brain.AnimalStats.Stat_Get(Stat.ID)?.Value ?? 0f;
                    }
                    break;

                case Affected.Target:

                    if (brain.TargetHasStats)
                    {
                        if (TryGetValue)
                        {
                            if (brain.TargetStats.TryGetValue(Stat.ID, out IMStat statS))
                                brain.DecisionState<StatState>(Index).InitialValue = statS.Value;
                        }
                        else
                        {
                            brain.DecisionState<StatState>(Index).InitialValue = brain.TargetStats.Stat_Get(Stat.ID)?.Value ?? 0f;
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        public override bool Decide(MAnimalBrain brain, int index)
        {
            bool result = false;

            switch (checkOn)
            {
                case Affected.Self:
                    if (TryGetValue)
                    {
                        if (brain.AnimalStats.TryGetValue(Stat.ID, out IMStat statS))
                            result = CheckStat(statS, brain, index);
                    }
                    else
                    {
                        var SelfStatValue = brain.AnimalStats.Stat_Get(Stat.ID);
                        result = CheckStat(SelfStatValue, brain, index);
                    }
                    break;
                case Affected.Target:

                    if (brain.TargetHasStats)
                    {
                        if (TryGetValue)
                        {
                            if (brain.TargetStats.TryGetValue(Stat.ID, out IMStat statT))
                                result = CheckStat(statT, brain, index);
                        }
                        else
                        {
                            var TargetStatValue = brain.TargetStats.Stat_Get(Stat.ID);
                            result = CheckStat(TargetStatValue, brain, index);
                        }
                    }
                    break;
            }
            return result;
        }


        private void RecoverValue()
        {
            m_Value.Value = Value;
        }

        /// <summary>Evaluates the selected option on the stat (false when the stat does not exist)</summary>
        private bool CheckStat(IMStat stat, MAnimalBrain brain, int Index)
        {
            if (stat == null) return false; //The animal or the target does not have this Stat

            switch (Option)
            {
                case checkStatOption.Compare:
                    return CompareWithValue(stat.Value);
                case checkStatOption.CompareNormalized:
                    return CompareWithValue(stat.NormalizedValue);
                case checkStatOption.IsInmune:
                    return stat.IsImmune;
                case checkStatOption.Regenerating:
                    return stat.IsRegenerating;
                case checkStatOption.Degenerating:
                    return stat.IsDegenerating;
                case checkStatOption.IsEmpty:
                    return stat.Value == stat.MinValue;
                case checkStatOption.IsFull:
                    return stat.Value == stat.MaxValue;
                case checkStatOption.IsActive:
                    return stat.Active;
                case checkStatOption.ValueChanged:
                    return stat.Value != brain.DecisionState<StatState>(Index).InitialValue; ;
                case checkStatOption.ValueReduced:
                    return stat.Value < brain.DecisionState<StatState>(Index).InitialValue; ;
                case checkStatOption.ValueIncreased:
                    return stat.Value > brain.DecisionState<StatState>(Index).InitialValue; ;
                default:
                    return false;
            }
        }
        /// <summary>Compares the stat value against the decision value with all six comparers</summary>
        private bool CompareWithValue(float stat) => stat.MCompare(m_Value, StatIs);


        [HideInInspector] public bool hideVars = false;
        private void OnValidate()
        {
            hideVars = (Option != checkStatOption.Compare && Option != checkStatOption.CompareNormalized);
        }


        private void Reset() { Description = "Checks for a Stat value, Compares or search for a Stat Property and returns the succeded value"; }
    }


    /// <summary>  Inspector!!!  </summary>

#if UNITY_EDITOR

    [CustomEditor(typeof(CheckStatDecision))]
    [CanEditMultipleObjects]
    public class CheckStatDecisionEditor : Editor
    {
        public static GUIStyle StyleBlue => MTools.Style(new Color(0, 0.5f, 1f, 0.3f));

        SerializedProperty
            Description, checkOn, MessageID, send, interval, Stat, Option, StatIs, Value, TryGetValue, WaitForAllTasks, waitForTask;

        MonoScript script;
        protected virtual void OnEnable()
        {
            script = MonoScript.FromScriptableObject((ScriptableObject)target);

            Description = serializedObject.FindProperty("Description");
            checkOn = serializedObject.FindProperty("checkOn");
            MessageID = serializedObject.FindProperty("DecisionID");
            send = serializedObject.FindProperty("send");
            interval = serializedObject.FindProperty("interval");
            WaitForAllTasks = serializedObject.FindProperty("WaitForAllTasks");
            waitForTask = serializedObject.FindProperty("waitForTask");

            Stat = serializedObject.FindProperty("Stat");
            Option = serializedObject.FindProperty("Option");
            StatIs = serializedObject.FindProperty("StatIs");
            Value = serializedObject.FindProperty("m_Value");
            TryGetValue = serializedObject.FindProperty("TryGetValue");
        }


        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.PropertyField(Description);
            EditorGUILayout.PropertyField(MessageID);
            EditorGUILayout.PropertyField(send);
            EditorGUILayout.PropertyField(interval);
            EditorGUILayout.PropertyField(WaitForAllTasks);
            EditorGUILayout.PropertyField(waitForTask);

            EditorGUILayout.PropertyField(checkOn);

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(Stat);

            var m_stlye = new GUIStyle(EditorStyles.miniButton);
            m_stlye.fontStyle = TryGetValue.boolValue ? FontStyle.Bold : FontStyle.Normal;

            TryGetValue.boolValue = GUILayout.Toggle(TryGetValue.boolValue,
                new GUIContent("Try*",
                "Uses TryGet Value in case you don't know if your target or your animal has the Stat you are looking for. Disabling this Improves performance"),
                m_stlye, GUILayout.Width(50));

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(Option);
            var o = (CheckStatDecision.checkStatOption)Option.intValue;

            var compare = o == CheckStatDecision.checkStatOption.Compare || o == CheckStatDecision.checkStatOption.CompareNormalized;

            if (compare)
                EditorGUILayout.PropertyField(StatIs, GUIContent.none, GUILayout.Width(90));

            EditorGUILayout.EndHorizontal();

            if (compare)
            {
                EditorGUILayout.PropertyField(Value);
                if (o == CheckStatDecision.checkStatOption.CompareNormalized)
                    EditorGUILayout.HelpBox("Compare Normalized Value must be between 0 and 1", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
