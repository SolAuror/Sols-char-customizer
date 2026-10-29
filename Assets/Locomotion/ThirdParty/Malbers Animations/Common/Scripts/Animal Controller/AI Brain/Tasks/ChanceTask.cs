using UnityEngine;
namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Tasks/Chance Task")]
    public class ChanceTask : MTask
    {
        public override string DisplayName => "General/Chance";
        private static readonly int ChanceKey = "ChanceTask".GetHashCode();

        [Range(0, 1), Tooltip("Chance this Task can execute another Task when the AI State start")]
        public float Chance = 1;

        [Tooltip("Task to execute if the chance succeded")]
        public MTask Task;

        [Tooltip("Task to execute if the chance failed")]
        public MTask TaskFailed;

        //MWC: Per-agent runtime state. Lives on the brain (not this shared SO) so each AI keeps its own roll result.
        private class ChanceState : BrainState
        {
            public bool CanExecute;     //True if the chance roll succeeded (run Task); false runs TaskFailed
        }

        public override void StartTask(MAnimalBrain brain, int index)
        {
            var RandomChance = UnityEngine.Random.Range(0f, 1f);

            var canExecute = Chance >= RandomChance;

            //Store the Result in this Task's runtime state on the Brain
            brain.TaskState<ChanceState>(index).CanExecute = canExecute;

            if (brain.debug)
                Debug.Log($"<B><color=orange>[{brain.Animal.name}]</color> → Probability to execute <B>[{Task.name}]</B>. Value:<B>[{RandomChance:F2}]</B> >= Limit:<B>[{Chance:F2}] ?</B>. Result: <color=green>[{canExecute}]</color></B>");

            if (canExecute)
            {
                Task?.StartTask(brain, index);
            }
            else
            {
                TaskFailed?.StartTask(brain, index);
            }
        }

        public override void UpdateTask(MAnimalBrain brain, int index)
        {
            if (brain.TaskState<ChanceState>(index).CanExecute)
            {
                Task.UpdateTask(brain, index);
            }
            else
            {
                TaskFailed?.UpdateTask(brain, index);
            }
        }

        public override void ExitAIState(MAnimalBrain brain, int index)
        {
            if (brain.TaskState<ChanceState>(index).CanExecute)
            {
                Task.ExitAIState(brain, index);
            }
            else
            {
                TaskFailed?.ExitAIState(brain, index);
            }
        }

        void Reset() { Description = "Gives a Percent Chance to execute another task"; }
    }


#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(ChanceTask))]
    public class ChanceTaskEditor : UnityEditor.Editor
    {
        UnityEditor.SerializedProperty Description, MessageID, Task, Chance, WaitForPreviousTask, TaskFailed;

        private void OnEnable()
        {
            WaitForPreviousTask = serializedObject.FindProperty("WaitForPreviousTask");
            Description = serializedObject.FindProperty("Description");
            MessageID = serializedObject.FindProperty("MessageID");
            Chance = serializedObject.FindProperty("Chance");
            Task = serializedObject.FindProperty("Task");
            TaskFailed = serializedObject.FindProperty("TaskFailed");

        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            UnityEditor.EditorGUILayout.PropertyField(Description);
            UnityEditor.EditorGUILayout.PropertyField(MessageID);
            UnityEditor.EditorGUILayout.PropertyField(WaitForPreviousTask);
            UnityEditor.EditorGUILayout.Space();

            UnityEditor.EditorGUILayout.PropertyField(Chance);
            // UnityEditor.EditorGUILayout.PropertyField(Task);

            using (new GUILayout.VerticalScope(UnityEditor.EditorStyles.helpBox))
            {
                UnityEditor.EditorGUI.indentLevel++;
                MTools.DrawScriptableObject(Task, true, false, "Task");
                MTools.DrawScriptableObject(TaskFailed, true, false, "Failed");
                UnityEditor.EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
