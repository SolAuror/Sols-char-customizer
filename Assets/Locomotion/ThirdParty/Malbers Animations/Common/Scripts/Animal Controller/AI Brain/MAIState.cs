// Last Change: 9/24/2026 1:13pm
using System.Collections.Generic;
using UnityEngine;
using System;
using MalbersAnimations.Scriptables;

using Object = UnityEngine.Object;



#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using System.Linq;
#endif

namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/AI State", order = -100, fileName = "New AI State")]
    public class MAIState : ScriptableObject
    {
        [Tooltip("ID of the AI State. This is used on the AI Brain On AIStateChanged Event")]
        public IntReference ID = new();

        public MTask this[int index]
        {
            get => tasks[index];
            set => tasks[index] = value;
        }

        public MTask[] tasks;
        public MAITransition[] transitions;
        public Color GizmoStateColor = Color.gray;

        [HideInInspector] public bool CreateTaskAsset = true;
        [HideInInspector] public bool CreateDecisionAsset = true;


#pragma warning disable 414
        //Inspector Variables
        [HideInInspector, SerializeField] private int TasksIndex = -1;
        [HideInInspector, SerializeField] private int DecisionIndex = -1;
#pragma warning restore 414


        private void Reset()
        {
            tasks = new MTask[0];
            transitions = new MAITransition[0];
        }

        public virtual void Play(MAnimalBrain brain) => brain?.StartNewState(this);


        internal void Update_State(MAnimalBrain brain)
        {
            Update_Tasks(brain);
            Update_Transitions(brain);
        }

        private void Update_Transitions(MAnimalBrain brain)
        {
            for (int i = 0; i < transitions.Length; i++)
            {
                if (this != brain.currentState) return; //BUG BUG BUG FIXED

                var transition = transitions[i];
                var decision = transition.decision;
                if (decision == null) return; //Ignore Code

                //If the Decision is waiting for a task to be done and the tasks is NOT done, then ignore this decision
                if (decision.waitForTask != -1 && !brain.IsTasksDone(decision.waitForTask))
                {
                    brain.DecisionsTime[i] = Time.time; //Reset the Decision Time
                    continue;
                }
                //If the Decision is waiting for ALL tasks to be done tasks are NOT done then ignore this decision
                if (decision.WaitForAllTasks && !brain.AllTasksDone())
                {
                    brain.DecisionsTime[i] = Time.time; //Reset the Decision Time
                    continue;
                }


                if (decision.interval > 0)
                {
                    if (brain.CheckIfDecisionsCountDownElapsed(decision.interval, i))
                    {
                        brain.ResetDecisionTime(i);
                        Decide(brain, i, transition);
                    }
                }
                else
                {
                    Decide(brain, i, transition);
                }
            }
        }

        private void Decide(MAnimalBrain brain, int Index, MAITransition transition)
        {
            if (!transition.decision.active) return; //Skip if the Decision is not active

            bool decisionSucceded = brain.DecisionResult[Index] = transition.decision.Decide(brain, Index);

            // Debug.Log($"{brain.Animal.name} decisionSucceded "+ decisionSucceded);

            brain.TransitionToState(decisionSucceded ? transition.trueState : transition.falseState, decisionSucceded,
                transition.decision, Index);
        }

        /// <summary>When a new State starts this method is called for each Tasks</summary>
        internal void Start_AIState(MAnimalBrain brain)
        {
            for (int i = 0; i < tasks.Length; i++) StartTaks(brain, i);
        }

        internal void StartTaks(MAnimalBrain brain, int i)
        {
            if (tasks[i] == null)
            {
                Debug.LogError($"The  {name} AI State has an Empty Task. Please check all your AI States Tasks. {brain.Animal.name} brain is Disabled", this);
                brain.enabled = false;
                return;
            }
            ;

            if (tasks[i].active)
            {
                if (brain.TasksStarted[i]) return; //DO NOT START AN ALREADY STARTED TASK 

                if (i == 0 || !tasks[i].WaitForPreviousTask)
                {
                    // Debug.Log($"<B>{brain.Animal.name}:</B> Start Task: [{name}] [{i}]-[{tasks[i].name }]");
                    //Prepare the Task
                    brain.TasksStarted[i] = true;
                    brain.SetTaskStartTime(i);
                    tasks[i].StartTask(brain, i); //Start the Task after it has being prepared.
                    if (tasks[i].MessageID != 0)
                        brain.OnTaskStarted.Invoke(tasks[i].MessageID); //Send Events after the Task has started
                }
            }
            ;
        }

        internal void StartWaitforPreviusTask(MAnimalBrain brain, int i)
        {
            if (tasks[i] == null)
            {
                Debug.LogError($"The  {name} AI State has an Empty Task. Please check all your AI States Tasks. {brain.Animal.name} brain is Disabled", this);
                brain.enabled = false;
                return;
            }
            ;

            if (brain.TasksStarted[i]) return; //DO NOT START AN ALREADY STARTED TASK  

            if (tasks[i].WaitForPreviousTask)
            {
                //Prepare the Task
                brain.TasksStarted[i] = true;
                brain.SetTaskStartTime(i);

                tasks[i].StartTask(brain, i); //Start the Task after it has being prepared.

                if (tasks[i].MessageID != 0)
                    brain.OnTaskStarted.Invoke(tasks[i].MessageID); //Send Events after the Task has started
            }
        }

        /// <summary>When a new State starts this method is called for each Decisions</summary>
        internal void Prepare_Decisions(MAnimalBrain brain)
        {
            if (transitions != null)
                for (int i = 0; i < transitions.Length; i++)
                {
                    transitions[i].decision.PrepareDecision(brain, i);
                }
        }

        internal void Update_Tasks(MAnimalBrain brain)
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                if (this != brain.currentState) return; //BUG when a Task call another AI STATE

                if (brain.TasksStarted[i] && !brain.TasksDone[i] && tasks[i].active)
                {
                    tasks[i].InternalUpdateTask(brain, i);
                }
            }
        }

        internal void Finish_Tasks(MAnimalBrain brain)
        {
            for (int i = 0; i < tasks.Length; i++)
                if (tasks[i].active) tasks[i].ExitAIState(brain, i);
        }



        #region Target Event Listeners for Tasks 
        /// <summary> Notify the Tasks that the Animal has arrived to the Current assigned target</summary>
        internal void OnTargetArrived(MAnimalBrain brain, Transform target)
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                if (tasks[i] != null) tasks[i].OnTargetArrived(brain, target, i);
            }
        }

        /// <summary>Notify the Tasks that the Animal has arrived to its destination position (with or without a Target)</summary>
        internal void OnPositionArrived(MAnimalBrain brain, Vector3 position)
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                if (tasks[i] != null) tasks[i].OnPositionArrived(brain, position, i);
            }
        }

        /// <summary>Notify the active Decisions that the Animal (self or the Target Animal) changed its State, Stance or Mode</summary>
        internal void OnAnimalEventDecisions(MAnimalBrain brain, MAnimal animal)
        {
            if (transitions == null) return;

            for (int i = 0; i < transitions.Length; i++)
            {
                if (this != brain.currentState) return; //A callback changed the AI State

                var decision = transitions[i]?.decision;
                if (decision != null && decision.active) decision.OnAnimalEventDecisionListen(brain, animal, i);
            }
        }

        #endregion

        #region Self Animal Listen Events

        internal void OnAnimalStateEnter(MAnimalBrain brain, State state)
        {
            for (int i = 0; i < tasks.Length; i++)
                tasks[i]?.OnAnimalStateEnter(brain, state, i);
        }

        internal void OnAnimalStateExit(MAnimalBrain brain, State state)
        {
            for (int i = 0; i < tasks.Length; i++)
                tasks[i]?.OnAnimalStateExit(brain, state, i);
        }

        internal void OnAnimalStanceChange(MAnimalBrain brain, int Stance)
        {
            for (int i = 0; i < tasks.Length; i++)
                tasks[i]?.OnAnimalStanceChange(brain, Stance, i);
        }


        internal void OnAnimalModeStart(MAnimalBrain brain, Mode mode)
        {
            for (int i = 0; i < tasks.Length; i++)
                tasks[i]?.OnAnimalModeStart(brain, mode, i);
        }

        internal void OnAnimalModeEnd(MAnimalBrain brain, Mode mode)
        {
            for (int i = 0; i < tasks.Length; i++)
                tasks[i]?.OnAnimalModeEnd(brain, mode, i);
        }

        #endregion

        #region Target Animal Listen Events

        /// <summary>Notify the Tasks that the Target Animal entered a State</summary>
        public void OnTargetAnimalStateEnter(MAnimalBrain brain, State state)
        {
            for (int i = 0; i < tasks.Length; i++)
                if (tasks[i] != null) tasks[i].OnTargetAnimalStateEnter(brain, state, i);
        }

        /// <summary>Notify the Tasks that the Target Animal exited a State</summary>
        public void OnTargetAnimalStateExit(MAnimalBrain brain, State state)
        {
            for (int i = 0; i < tasks.Length; i++)
                if (tasks[i] != null) tasks[i].OnTargetAnimalStateExit(brain, state, i);
        }

        /// <summary>Notify the Tasks that the Target Animal changed Stance</summary>
        public void OnTargetAnimalStanceChange(MAnimalBrain brain, int Stance)
        {
            for (int i = 0; i < tasks.Length; i++)
                if (tasks[i] != null) tasks[i].OnTargetAnimalStanceChange(brain, Stance, i);
        }

        /// <summary>Notify the Tasks that the Target Animal started a Mode</summary>
        public void OnTargetAnimalModeStart(MAnimalBrain brain, Mode mode)
        {
            for (int i = 0; i < tasks.Length; i++)
                if (tasks[i] != null) tasks[i].OnTargetAnimalModeStart(brain, mode, i);
        }

        /// <summary>Notify the Tasks that the Target Animal ended a Mode</summary>
        public void OnTargetAnimalModeEnd(MAnimalBrain brain, Mode mode)
        {
            for (int i = 0; i < tasks.Length; i++)
                if (tasks[i] != null) tasks[i].OnTargetAnimalModeEnd(brain, mode, i);
        }

        #endregion 
    }

    [System.Serializable]
    public class MAITransition
    {
        public MAIDecision decision;
        public MAIState trueState;
        public MAIState falseState;
    }

#if UNITY_EDITOR

    /// <summary>Searchable [+] menu shared by the Task and Decision lists (MAIState, ANDDecision). Same dropdown as the [AddTypeMenu] popup: categories + search box</summary>
    public static class BrainMenu
    {
        /// <summary>Label of the last entry of the list: it leaves the new slot empty instead of creating an asset</summary>
        public const string Empty = "Empty";

        /// <summary>Display Name of every type (read from a temporary instance) plus "Empty". Sorts <paramref name="types"/> so the list shares its indices and keeps the categories together</summary>
        public static string[] Names<T>(List<Type> types, Func<T, string> displayName) where T : ScriptableObject
        {
            var names = new string[types.Count + 1];
            var byType = new Dictionary<Type, string>(types.Count);

            foreach (var type in types)
            {
                var temp = (T)ScriptableObject.CreateInstance(type);
                byType[type] = displayName(temp);
                Object.DestroyImmediate(temp);
            }

            types.Sort((a, b) => string.Compare(byType[a], byType[b], StringComparison.OrdinalIgnoreCase));

            for (int i = 0; i < types.Count; i++) names[i] = byType[types[i]];

            names[types.Count] = Empty;

            return names;
        }

        /// <summary>Shows the dropdown under the [+] button. onPick receives the index on the names array (the last one is "Empty")</summary>
        public static void Show(Rect buttonRect, string title, string[] names, Action<int> onPick)
            => new Dropdown(title, names, onPick).Show(buttonRect);

        /// <summary>AdvancedDropdown built from "Category/Sub Category/Name" strings. Leaf ids = index on the names array</summary>
        private class Dropdown : AdvancedDropdown
        {
            /// <summary>Visible rows before the list scrolls</summary>
            private const int MaxLines = 16;

            /// <summary>Width of the popup. The [+] button is too small to size it</summary>
            private const float Width = 260f;

            private readonly string title;
            private readonly string[] names;
            private readonly Action<int> onPick;

            public Dropdown(string title, string[] names, Action<int> onPick) : base(new AdvancedDropdownState())
            {
                this.title = title;
                this.names = names;
                this.onPick = onPick;

                minimumSize = new Vector2(Width, EditorGUIUtility.singleLineHeight * (MaxLines + 2)); //+2: search box and header
            }

            /// <summary>Builds the category tree. Categories get ids after the leaves so every id is unique</summary>
            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem(title);
                var nextCategoryID = names.Length;

                for (int i = 0; i < names.Length; i++)
                {
                    var path = names[i].Split('/');
                    var parent = root;

                    for (int k = 0; k < path.Length - 1; k++)
                        parent = FindOrAddCategory(parent, path[k], ref nextCategoryID);

                    if (i == names.Length - 1 && parent == root) root.AddSeparator(); //"Empty" sits alone at the end, like the old menu

                    parent.AddChild(new AdvancedDropdownItem(path[^1]) { id = i });
                }

                return root;
            }

            /// <summary>Category item under parent with that name, created when missing. Categories are the items whose id is past the names array</summary>
            private AdvancedDropdownItem FindOrAddCategory(AdvancedDropdownItem parent, string name, ref int nextID)
            {
                foreach (var child in parent.children)
                    if (child.name == name && child.id >= names.Length) return child;

                var category = new AdvancedDropdownItem(name) { id = nextID++ };
                parent.AddChild(category);
                return category;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                base.ItemSelected(item);
                if (item.id >= 0 && item.id < names.Length) onPick(item.id);
            }
        }
    }

    [CustomEditor(typeof(MAIState))]
    public class MAIStateEditor : Editor
    {
        private MAIState m;
        private SerializedProperty tasks, transitions, GizmoStateColor, ID, TasksIndex, DecisionIndex;
        private ReorderableList Reo_List_Tasks, Reo_List_Transitions;


        private List<Type> TasksType;
        private List<Type> DecisionType;

        /// <summary>Entries of the searchable [+] menus, sharing the indices of TasksType/DecisionType</summary>
        private string[] TasksNames, DecisionNames;

        private GUIContent plus;
        private GUIContent rename;
        private GUIContent extract;


        private const float IconWidth = 27f;
        private const float IconHeight = 22f;

        #region Gizmo Preview on a Scene Brain (Editor Only)

        /// <summary>Brain in the scene used to preview the gizmos of the AI State being inspected [Editor Only]</summary>
        public static MAnimalBrain PreviewBrain { get; private set; }

        /// <summary>AI State whose Tasks/Decisions gizmos are drawn on the Preview Brain [Editor Only]</summary>
        public static MAIState PreviewState { get; private set; }

        private static bool showPreview = true; // MWC — foldout state shared by all AI State inspectors
        private static GUIContent previewIcon;

        /// <summary>Draws the gizmos of the previewed AI State on the chosen Brain while NOT playing (in Play Mode the Brain draws its own current state)</summary>
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawPreviewGizmos(MAnimalBrain brain, GizmoType gizmoType)
        {
            if (Application.isPlaying || brain == null || brain != PreviewBrain || PreviewState == null) return;

            var state = PreviewState;

            if (brain.Eyes)
            {
                Gizmos.color = state.GizmoStateColor;
                Gizmos.DrawWireSphere(brain.Eyes.position, 0.2f);

                var style = new GUIStyle(EditorStyles.boldLabel);
                style.normal.textColor = Color.yellow;
                Handles.Label(brain.Eyes.position, "Preview: " + state.name, style);
            }

            if (state.tasks != null)
                foreach (var task in state.tasks)
                    if (task != null) task.DrawGizmos(brain);

            if (state.transitions != null)
                foreach (var tran in state.transitions)
                    if (tran != null && tran.decision != null) tran.decision.DrawGizmos(brain);
        }

        /// <summary>Sets which Brain (and which AI State) draws the preview gizmos and refreshes the Scene View</summary>
        private static void SetPreview(MAnimalBrain brain, MAIState state)
        {
            PreviewBrain = brain;
            PreviewState = brain != null ? state : null;
            SceneView.RepaintAll();
        }

        /// <summary>Foldout at the top of the AI State inspector to pick any Brain in the scene and visualize this state's gizmos on it</summary>
        private void DrawBrainPreview()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                showPreview = MalbersEditor.Foldout(showPreview, "Preview Gizmos on a Scene Brain [Editor Only]");

                if (!showPreview) return;

                if (Application.isPlaying)
                {
                    EditorGUILayout.HelpBox("In Play Mode each Brain draws the gizmos of its own current AI State", MessageType.Info);
                    return;
                }

                //Another AI State took the preview: show it as not selected for this one
                var brain = PreviewState == m ? PreviewBrain : null;

                using (new GUILayout.HorizontalScope())
                {
                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        var newBrain = (MAnimalBrain)EditorGUILayout.ObjectField(
                            new GUIContent("Brain", "Drag any AI Brain from the scene. The gizmos of this AI State will be drawn on it, no matter which AI State the Brain has set"),
                            brain, typeof(MAnimalBrain), true);

                        if (cc.changed) SetPreview(newBrain, m);
                    }

                    previewIcon ??= new GUIContent("▾", "Select a Brain from the scene");

                    if (GUILayout.Button(previewIcon, EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                        ShowSceneBrainsMenu(brain);
                }

                if (brain != null)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        var current = brain.currentState ? brain.currentState.name : "None";
                        EditorGUILayout.LabelField($"Brain AI State: [{current}]", EditorStyles.miniLabel);

                        using (new EditorGUI.DisabledGroupScope(brain.currentState == m))
                        {
                            if (GUILayout.Button(new GUIContent("Assign", "Set this AI State as the default (current) AI State of the Brain"), EditorStyles.miniButtonLeft, GUILayout.Width(48)))
                                AssignStateToBrain(brain);
                        }

                        if (GUILayout.Button("Ping", EditorStyles.miniButtonMid, GUILayout.Width(40)))
                            EditorGUIUtility.PingObject(brain);

                        if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(40)))
                            SetPreview(null, null);
                    }
                }
            }
        }

        /// <summary>Sets this AI State as the default (current) AI State of the Brain, with Undo and scene/prefab dirty marking</summary>
        private void AssignStateToBrain(MAnimalBrain brain)
        {
            var so = new SerializedObject(brain);
            so.FindProperty(nameof(MAnimalBrain.currentState)).objectReferenceValue = m;
            so.ApplyModifiedProperties(); //Handles Undo, SetDirty and prefab overrides
            EditorGUIUtility.PingObject(brain);
        }

        /// <summary>Name of the Animal the Brain controls (brain.Animal is only set at runtime, so it is searched on the hierarchy). Falls back to the Brain GameObject name</summary>
        private static string AnimalName(MAnimalBrain brain)
        {
            var animal = brain.Animal ? brain.Animal : brain.gameObject.FindComponent<MAnimal>();
            return animal ? animal.name : brain.name;
        }

        /// <summary>Dropdown listing every AI Brain in the open scenes (inactive included)</summary>
        private void ShowSceneBrainsMenu(MAnimalBrain selected)
        {
            var menu = new GenericMenu();

            var brains = Object.FindObjectsByType<MAnimalBrain>(FindObjectsInactive.Include
#if !UNITY_6000_4_OR_NEWER
     , FindObjectsSortMode.None
#endif
     )
     .OrderBy(b => AnimalName(b)).ToList();

            menu.AddItem(new GUIContent("None"), selected == null, () => SetPreview(null, null));

            if (brains.Count > 0) menu.AddSeparator("");

            foreach (var b in brains)
            {
                var brain = b;
                var animal = AnimalName(brain);
                var label = brain.currentState ? $"{animal} ({brain.currentState.name})" : animal;
                if (!brain.gameObject.activeInHierarchy) label += " [Inactive]";

                menu.AddItem(new GUIContent(label), selected == brain, () => SetPreview(brain, m));
            }

            if (brains.Count == 0) menu.AddDisabledItem(new GUIContent("No AI Brains found in the scene"));

            menu.ShowAsContext();
        }

        /// <summary>Releases the preview when this AI State stops being inspected</summary>
        private void OnDisable()
        {
            if (PreviewState == m) SetPreview(null, null);
        }

        #endregion

        private void OnEnable()
        {
            if (plus == null)
                plus = EditorGUIUtility.IconContent("d_Toolbar Plus");


            if (rename == null)
            {
                rename = EditorGUIUtility.IconContent("d_editicon.sml");
                rename.tooltip = "Update the Asset Name";
            }
            if (extract == null)
            {
                extract = EditorGUIUtility.IconContent("d_AnimatorStateTransition Icon");
                extract.tooltip = "Extract the Asset";
            }

            m = (MAIState)target;
            tasks = serializedObject.FindProperty("tasks");
            transitions = serializedObject.FindProperty("transitions");
            GizmoStateColor = serializedObject.FindProperty("GizmoStateColor");
            ID = serializedObject.FindProperty("ID");
            GizmoStateColor = serializedObject.FindProperty("GizmoStateColor");


            TasksIndex = serializedObject.FindProperty("TasksIndex");
            DecisionIndex = serializedObject.FindProperty("DecisionIndex");

            TasksType = MTools.GetAllTypes<MTask>();
            DecisionType = MTools.GetAllTypes<MAIDecision>();

            TasksNames = BrainMenu.Names<MTask>(TasksType, t => t.DisplayName);
            DecisionNames = BrainMenu.Names<MAIDecision>(DecisionType, d => d.DisplayName);

            TasksList();

            TransitionList();

            Reo_List_Tasks.index = TasksIndex.intValue;
            Reo_List_Transitions.index = DecisionIndex.intValue;
        }


        /// <summary>Searchable popup listing every Task type (plus "Empty") under the [+] button</summary>
        private void OnAddCallback_Task(Rect buttonRect)
        {
            ResizeTasks(); //The new (empty) slot is already there: "Empty" just leaves it as it is

            BrainMenu.Show(buttonRect, "Tasks", TasksNames, index =>
            {
                if (index < TasksType.Count) AddTask(TasksType[index], m.tasks.Length - 1);
                Repaint();
            });
        }


        private void AddTask(Type NewTask, int index)
        {
            MTask task = (MTask)CreateInstance(NewTask);
            task.hideFlags = HideFlags.None;
            task.name = "T_" + NewTask.Name;
            AssetDatabase.AddObjectToAsset(task, AssetDatabase.GetAssetPath(target));

            m.tasks[index] = task; //the other way was not working
                                   // TasksIndex.intValue = index;
                                   // DecisionIndex.intValue = -1;

            Reo_List_Tasks.index = index;
            Reo_List_Transitions.index = -1;

            EditorUtility.SetDirty(task);
            EditorUtility.SetDirty(target);
            serializedObject.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }


        private void TasksList()
        {
            Reo_List_Tasks = new ReorderableList(serializedObject, tasks, true, true, true, true)
            {
                onSelectCallback = (list) =>
                {
                    //Do not select the Tasks when selecting the transitions
                    Reo_List_Transitions.index = DecisionIndex.intValue = -1;
                    TasksIndex.intValue = list.index;
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    var element = tasks.GetArrayElementAtIndex(index);


                    var indexR = 38;

                    var r = new Rect(rect) { x = rect.x + indexR, y = rect.y + 2, height = EditorGUIUtility.singleLineHeight, width = rect.width - indexR };


                    var indRect = new Rect(rect) { x = rect.x - 3, y = rect.y + 2, height = EditorGUIUtility.singleLineHeight, width = indexR };

                    bool isWaiting = element.objectReferenceValue != null && (element.objectReferenceValue as MTask).WaitForPreviousTask;

                    if (isWaiting) r.width -= 20;



                    EditorGUI.LabelField(indRect, $"[{index}]");


                    var dC = GUI.contentColor;

                    string Path = AssetDatabase.GetAssetPath(element.objectReferenceValue);
                    //mean it was created inside the AI STATE
                    if (element.objectReferenceValue != null & Path != AssetDatabase.GetAssetPath(target))
                    {
                        GUI.contentColor = new Color(0.7f, 0.8f, 1);
                    }

                    var activeRect = new Rect(rect);
                    activeRect.width -= 20;
                    activeRect.x += 22;

                    if (element.objectReferenceValue != null)
                    {
                        MTask task = element.objectReferenceValue as MTask;

                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            task.active = EditorGUI.Toggle(activeRect, GUIContent.none, task.active);
                            if (cc.changed)
                            {
                                Reo_List_Transitions.index = DecisionIndex.intValue = -1;
                                element.serializedObject.ApplyModifiedProperties();
                            }
                        }
                    }
                    else
                    {
                        EditorGUI.Toggle(activeRect, GUIContent.none, false);
                    }


                    EditorGUI.PropertyField(r, element, GUIContent.none);
                    GUI.contentColor = dC;

                    var st = new GUIStyle(EditorStyles.whiteLabel)
                    {
                        fontSize = 18
                    };
                    st.normal.textColor = Color.green;

                    if (isWaiting)
                    {
                        var waitinR = new Rect(r)
                        {
                            width = 20,
                            height = r.height + 2,
                            y = r.y - 2,
                            x = rect.width + 25
                        };
                        EditorGUI.LabelField(waitinR, new GUIContent("⤴", "This task is waiting for the previous task to finish "), st);
                    }
                },

                drawHeaderCallback = rect =>
                {
                    var r = new Rect(rect);
                    var ColorRect = new Rect(rect)
                    {
                        x = rect.width - 40,
                        width = 60,
                        height = rect.height - 2,
                        y = rect.y + 1,
                    };

                    EditorGUI.LabelField(rect, new GUIContent("   Tasks", "Tasks for the state"));
                    EditorGUI.PropertyField(ColorRect, GizmoStateColor, GUIContent.none);
                },

                onAddDropdownCallback = (rect, list) => OnAddCallback_Task(rect),

                onRemoveCallback = list =>
                {
                    var task = tasks.GetArrayElementAtIndex(list.index);

                    if (task != null && task.objectReferenceValue != null)
                    {
                        if (EditorUtility.DisplayDialog("Remove Task", "Deleting a Task cannot be undone. Are you sure you want to delete it?", "Yes", "No"))
                        {
                            string Path = AssetDatabase.GetAssetPath(task.objectReferenceValue);

                            //mean it was created inside the AI STATE
                            if (Path == AssetDatabase.GetAssetPath(target))
                            {
                                TasksIndex.intValue = -1;

                                var asset = task.objectReferenceValue;

                                //task.objectReferenceValue = null;
                                tasks.DeleteArrayElementAtIndex(list.index); //Double Hack
                                tasks.serializedObject.ApplyModifiedProperties();

                                Reo_List_Transitions.index = -1;
                                Reo_List_Tasks.index = -1;

                                DestroyImmediate(asset, true); //Delete the internal asset!
                                AssetDatabase.SaveAssets();

                            }
                            else // is an Outside Element
                            {
                                TasksIndex.intValue = -1;
                                tasks.DeleteArrayElementAtIndex(list.index);
                                tasks.serializedObject.ApplyModifiedProperties();

                            }
                            GUIUtility.ExitGUI();
                        }
                    }
                    else
                    {
                        tasks.DeleteArrayElementAtIndex(list.index);
                    }

                    if (tasks != null && task.serializedObject != null)
                        tasks.serializedObject.ApplyModifiedProperties();

                    //Reset the selection on Remove
                    Reo_List_Transitions.index = -1;
                    Reo_List_Tasks.index = -1;


                    EditorUtility.SetDirty(target);
                    GUIUtility.ExitGUI();
                }
            };
        }



        private void TransitionList()
        {
            Reo_List_Transitions = new ReorderableList(serializedObject, transitions, true, true, true, true)
            {
                onSelectCallback = (list) =>
               {
                   Reo_List_Tasks.index = TasksIndex.intValue = -1; //Do not select the Tasks when selecting the transitions
                   DecisionIndex.intValue = list.index;
               },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    var r1 = new Rect(rect) { y = rect.y + 2, height = EditorGUIUtility.singleLineHeight };
                    var r2 = new Rect(r1);
                    var r3 = new Rect(r2);
                    bool empty = false;

                    var element = transitions.GetArrayElementAtIndex(index);

                    var decision = element.FindPropertyRelative("decision");
                    var TrueState = element.FindPropertyRelative("trueState");
                    var FalseState = element.FindPropertyRelative("falseState");

                    string Path = AssetDatabase.GetAssetPath(decision.objectReferenceValue);

                    var dC = GUI.contentColor;

                    if (decision.objectReferenceValue != null && Path != AssetDatabase.GetAssetPath(target)) //mean it was created inside the AI STATE
                    {
                        GUI.contentColor = Color.yellow + Color.gray;
                    }

                    var activeRect = new Rect(r1);
                    activeRect.width = 25;
                    activeRect.x += 20;

                    if (decision.objectReferenceValue != null)
                    {
                        MAIDecision des = decision.objectReferenceValue as MAIDecision;

                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            des.active = EditorGUI.Toggle(activeRect, GUIContent.none, des.active);
                            if (cc.changed)
                            {
                                Reo_List_Tasks.index = TasksIndex.intValue = -1;
                                element.serializedObject.ApplyModifiedProperties();
                            }
                        }
                    }
                    else
                    {
                        EditorGUI.Toggle(activeRect, GUIContent.none, false);
                    }


                    EditorGUI.PropertyField(r1, decision,
                        new GUIContent("[" + index + "]       Decision",
                            "If the Decision is true it will go to the True State, else it will go to the False state"));
                    GUI.contentColor = dC;

                    empty = TrueState.objectReferenceValue == null;

                    if (empty) r2.width -= 28;

                    r2.y += EditorGUIUtility.singleLineHeight + 3;
                    EditorGUI.PropertyField(r2, TrueState,
                        new GUIContent("True" + (empty ? " (Do Nothing)" : ""),
                            "If the Decision is TRUE, It will execute this state. if is Empty, it will do nothing"));


                    if (empty)
                    {
                        var AddButtonRect = new Rect(r2)
                        { x = rect.width + 18, width = 20, height = EditorGUIUtility.singleLineHeight };

                        if (GUI.Button(AddButtonRect, plus, EditorStyles.helpBox))
                        {
                            var NewAsset = MTools.CreateAssetWithSavePrompt(typeof(MAIState), MalbersEditor.GetSelectedPathOrFallback());
                            EditorUtility.SetDirty(NewAsset);

                            m.transitions[index].trueState = NewAsset as MAIState;

                            // TrueState.serializedObject.ApplyModifiedProperties();

                            // serializedObject.ApplyModifiedProperties();
                            EditorUtility.SetDirty(target);
                            GUIUtility.ExitGUI();
                        }
                    }


                    empty = FalseState.objectReferenceValue == null;
                    if (empty) r3.width -= 28;
                    r3.y += (EditorGUIUtility.singleLineHeight + 3) * 2;


                    EditorGUI.PropertyField(r3, FalseState,
                        new GUIContent("False" + (empty ? " (Do Nothing)" : ""),
                            "If the Decision is FALSE, It will execute this state. if is Empty, it will do nothing"));

                    if (empty)
                    {
                        var AddButtonRect = new Rect(r3)
                        { x = rect.width + 18, width = 20, height = EditorGUIUtility.singleLineHeight };

                        if (GUI.Button(AddButtonRect, plus, EditorStyles.helpBox))
                        {
                            var NewAsset = MTools.CreateAssetWithSavePrompt(typeof(MAIState), MalbersEditor.GetSelectedPathOrFallback());
                            EditorUtility.SetDirty(NewAsset);
                            m.transitions[index].falseState = NewAsset as MAIState;
                            EditorUtility.SetDirty(target);
                            GUIUtility.ExitGUI();
                        }
                    }
                },

                drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, new GUIContent("   Decisions", "Transitions for other States")),

                //ADD
                onAddDropdownCallback = (rect, list) => OnAddCallback_Decision(rect),

                onRemoveCallback = list =>
                {
                    var decision = m.transitions[list.index].decision;

                    if (decision != null)
                    {
                        if (EditorUtility.DisplayDialog("Remove Decision", "Deleting a Decision cannot be undone. Are you sure you want to delete it?", "Yes", "No"))
                        {
                            string Path = AssetDatabase.GetAssetPath(decision);

                            if (Path == AssetDatabase.GetAssetPath(target)) //mean it was created inside the AI STATE
                            {
                                DecisionIndex.intValue = -1;

                                DestroyImmediate(decision, true); //Delete the internal asset!
                                m.transitions[list.index].decision = null;
                                transitions.DeleteArrayElementAtIndex(list.index);
                                transitions.serializedObject.ApplyModifiedProperties();
                                AssetDatabase.SaveAssets();
                            }
                            else
                            {
                                transitions.DeleteArrayElementAtIndex(list.index);
                                transitions.serializedObject.ApplyModifiedProperties();
                                list.index = -1;
                            }
                        }
                    }
                    else
                    {
                        transitions.DeleteArrayElementAtIndex(list.index);
                        transitions.serializedObject.ApplyModifiedProperties();
                    }

                    //Reset the selection on Remove
                    Reo_List_Transitions.index = -1;
                    Reo_List_Tasks.index = -1;

                    EditorUtility.SetDirty(target);
                    GUIUtility.ExitGUI();
                },
                elementHeightCallback = (index) =>
                {
                    var DefaultHeight = EditorGUIUtility.singleLineHeight + 5;
                    return DefaultHeight * 3;
                },
            };
        }



        /// <summary>Searchable popup listing every Decision type (plus "Empty") under the [+] button</summary>
        private void OnAddCallback_Decision(Rect buttonRect)
        {
            ResizeTransitions(); //The new (empty) transition is already there: "Empty" just leaves it as it is

            BrainMenu.Show(buttonRect, "Decisions", DecisionNames, index =>
            {
                if (index < DecisionType.Count) AddDecision(DecisionType[index], m.transitions.Length - 1);
                Repaint();
            });
        }

        private void AddDecision(Type desicion, int index)
        {
            MAIDecision des = (MAIDecision)CreateInstance(desicion);
            des.hideFlags = HideFlags.None;
            des.name = "D_" + desicion.Name;

            AssetDatabase.AddObjectToAsset(des, AssetDatabase.GetAssetPath(target));
            AssetDatabase.SaveAssets();

            m.transitions[index].decision = des; //the other way was not working

            m.transitions[index].trueState = null;
            m.transitions[index].falseState = null;

            EditorUtility.SetDirty(des);
            EditorUtility.SetDirty(target);
        }

        private void ResizeTasks()
        {
            if (m.tasks == null)
            {
                m.tasks = new MTask[1];
            }
            else
            {
                Array.Resize<MTask>(ref m.tasks, m.tasks.Length + 1); //Hack for non UNITY  OBJECTS ARRAYS
            }

            EditorUtility.SetDirty(target);
        }

        private void ResizeTransitions()
        {
            if (m.transitions == null)
            {
                m.transitions = new MAITransition[1];
            }
            else
            {
                Array.Resize<MAITransition>(ref m.transitions,
                    m.transitions.Length + 1); //Hack for non UNITY  OBJECTS ARRAYS
            }

            EditorUtility.SetDirty(target);
        }


        private Editor CurrentTaskEditor;
        private UnityEngine.Object TaskObject;

        private Editor CurrentDecisionEditor;
        private UnityEngine.Object DecisionObject;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawBrainPreview();

            EditorGUILayout.LabelField(target.name, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(ID);
            //EditorGUILayout.PropertyField(internalData);
            EditorGUILayout.Space();

            Reo_List_Tasks.DoLayoutList();
            Reo_List_Transitions.DoLayoutList();

            Reo_List_Tasks.index = TasksIndex.intValue;
            Reo_List_Transitions.index = DecisionIndex.intValue;

            //  if (Reo_List_Tasks.index != -1) Reo_List_Transitions.index = -1;

            if (m.tasks != null)

                foreach (var t in m.tasks)
                {
                    if (t == null)
                    {
                        EditorGUILayout.HelpBox("The Brain cannot contain empty Tasks. Set the missing tasks", MessageType.Error);
                        break;
                    }
                }

            if (m.transitions != null)
                foreach (var t in m.transitions)
                {
                    if (t != null && t.decision == null)
                    {
                        EditorGUILayout.HelpBox("The Brain cannot contain empty Decisions. Set the missing decisions", MessageType.Error);
                        break;
                    }
                }

            //  if (Reo_List_Transitions.index != -1) Reo_List_Tasks.index = -1;

            // Debug.Log($"Reo_List_Tasks {Reo_List_Tasks.index} , Reo_List_Transitions: {Reo_List_Transitions.index}");

            string targetPath = AssetDatabase.GetAssetPath(target);

            var indexTasks = Reo_List_Tasks.index;
            var indexTrans = Reo_List_Transitions.index;

            if (indexTasks != -1 && tasks.arraySize > indexTasks)
            {
                var element = tasks.GetArrayElementAtIndex(indexTasks);

                if (element != null && element.objectReferenceValue != null)
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        var asset = element.objectReferenceValue;


                        var tasks = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.4f, 0.5f, 2f, 1);
                        using (new GUILayout.VerticalScope(EditorStyles.selectionRect))
                        {
                            EditorGUILayout.LabelField("Task: " + asset.name, EditorStyles.boldLabel);
                            //EditorGUILayout.Space();
                        }
                        GUI.backgroundColor = tasks;


                        string Path = AssetDatabase.GetAssetPath(element.objectReferenceValue);
                        if (Path != AssetDatabase.GetAssetPath(target)) //mean it was created inside the AI STATE
                        {
                            EditorGUILayout.HelpBox("This Task is shared with other AI States", MessageType.Info);
                        }

                        using (new GUILayout.HorizontalScope())
                        {
                            asset.name = EditorGUILayout.TextField("Name", asset.name);
                            element.serializedObject.ApplyModifiedProperties();
                            EditorUtility.SetDirty(asset);

                            string taskPath = AssetDatabase.GetAssetPath(asset);


                            if (GUILayout.Button(rename, GUILayout.Width(IconWidth), GUILayout.Height(IconHeight)))
                            {
                                // Check if the asset itself is external or internal to the target
                                if (taskPath != targetPath)
                                    AssetDatabase.RenameAsset(taskPath, asset.name);

                                AssetDatabase.SaveAssets();
                                EditorGUIUtility.PingObject(asset); //Final way of changing the name of the asset... dirty but it works

                                Reo_List_Tasks.index = indexTasks;
                                Reo_List_Transitions.index = -1;

                                serializedObject.ApplyModifiedProperties();
                                //  GUIUtility.ExitGUI();
                                return;
                            }

                            using (new EditorGUI.DisabledGroupScope(taskPath != targetPath))
                            {
                                if (GUILayout.Button(extract, GUILayout.Width(IconWidth), GUILayout.Height(IconHeight)))
                                {
                                    ExtractTaskFromList(asset, element, indexTasks);
                                    GUIUtility.ExitGUI();
                                    return;
                                }
                            }
                        }


                        //Draw the Current Selected Task
                        if (CurrentTaskEditor == null || element.objectReferenceValue != TaskObject)
                        {
                            TaskObject = element.objectReferenceValue;
                            CurrentTaskEditor = Editor.CreateEditor(TaskObject);
                        }

                        CurrentTaskEditor.OnInspectorGUI();
                    }
                }
            }

            if (indexTrans != -1 && transitions.arraySize > indexTrans)
            {
                var element = transitions.GetArrayElementAtIndex(indexTrans);
                var decision = element.FindPropertyRelative("decision");

                if (decision != null && decision.objectReferenceValue != null)
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        var tasks = GUI.backgroundColor;
                        GUI.backgroundColor = Color.red + Color.yellow * 0.5f;
                        using (new GUILayout.VerticalScope(EditorStyles.selectionRect))
                        {
                            EditorGUILayout.LabelField("Decision: " + decision.objectReferenceValue.name, EditorStyles.boldLabel);
                            //EditorGUILayout.Space();
                        }
                        GUI.backgroundColor = tasks;

                        string Path = AssetDatabase.GetAssetPath(decision.objectReferenceValue);

                        if (Path != targetPath) //mean it was created inside the AI STATE
                        {
                            EditorGUILayout.HelpBox("This Decision is shared with other AI States", MessageType.Info);
                        }

                        var asset = decision.objectReferenceValue;

                        string taskPath = AssetDatabase.GetAssetPath(asset);

                        using (new GUILayout.HorizontalScope())
                        {
                            asset.name = EditorGUILayout.TextField("Name", asset.name);
                            decision.serializedObject.ApplyModifiedProperties();
                            EditorUtility.SetDirty(asset);

                            if (GUILayout.Button(rename, GUILayout.Width(IconWidth), GUILayout.Height(IconHeight)))
                            {
                                // string taskPath = AssetDatabase.GetAssetPath(asset);

                                // Check if the asset itself is external or internal to the target
                                if (taskPath != targetPath)
                                    AssetDatabase.RenameAsset(taskPath, asset.name);

                                AssetDatabase.SaveAssets();
                                EditorGUIUtility.PingObject(asset); //Final way of changing the name of the asset... dirty but it works

                                GUIUtility.ExitGUI();
                                return;
                            }


                            using (new EditorGUI.DisabledGroupScope(taskPath != targetPath))
                            {
                                if (GUILayout.Button(extract, GUILayout.Width(IconWidth), GUILayout.Height(IconHeight)))
                                {
                                    ExtractDecisionFromList(asset, indexTrans);
                                }
                            }
                        }


                        //Draw the Current Selected Decision
                        if (CurrentDecisionEditor == null || decision.objectReferenceValue != DecisionObject)
                        {
                            DecisionObject = decision.objectReferenceValue;
                            CurrentDecisionEditor = Editor.CreateEditor(DecisionObject);
                        }

                        CurrentDecisionEditor.OnInspectorGUI();
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void ExtractTaskFromList(Object asset, SerializedProperty element, int index)
        {
            Object clone = MTools.ExtractObject(asset, index);
            if (!clone) return;

            m.tasks[index] = clone as MTask;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            EditorGUIUtility.PingObject(clone);

            DestroyImmediate(asset, true); //Remove old version
            AssetDatabase.SaveAssets();

            GUIUtility.ExitGUI();
        }

        private void ExtractDecisionFromList(Object asset, int index)
        {
            Object clone = MTools.ExtractObject(asset, index);
            if (!clone) return;

            m.transitions[index].decision = clone as MAIDecision;

            EditorUtility.SetDirty(target);
            serializedObject.ApplyModifiedProperties();
            EditorGUIUtility.PingObject(clone);

            // Remove from list
            DestroyImmediate(asset, true);
            AssetDatabase.SaveAssets();

            GUIUtility.ExitGUI();
        }
    }
#endif
}