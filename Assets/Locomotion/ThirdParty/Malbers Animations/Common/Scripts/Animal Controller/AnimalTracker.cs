// Last Change: 9/16/2026 5:05pm
using System.Collections.Generic;
using UnityEngine;
using MalbersAnimations.Utilities;
using MalbersAnimations.Reactions;
using System.Collections;

#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace MalbersAnimations.Controller
{
    /// <summary>Tracks the Animal States, Stances and Modes to fire Reactions and reposition a Tracker Transform</summary>
    [AddComponentMenu("Malbers/Animal Controller/Animal Tracker")]
    [DefaultExecutionOrder(10000)]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/utilities/animal-tracker")]
    public class AnimalTracker : MonoBehaviour
    {
        [RequiredField] public MAnimal animal;
        public Transform Tracker;

        [Tooltip("Unparent the Tracker")]
        public bool NoParent = true;

        [Tooltip("Use FixedUpdate instead of Update")]
        public bool FixedUpdate = false;

        private float CurrentLerp;

        public List<TransformTracker> Trackers = new();

        [Tooltip("Size of the Debug Cube that shows the Tracker Position in the Scene View")]
        public float DebugSize = 0.05f;


        private TransformTracker Current;

        private TransformTracker DefaultState;

        /// <summary> Running Tracker update coroutine. Null when it is not running </summary>
        private Coroutine UpdateRoutine;

        /// <summary> Parent used the last time the [Relative To] references were auto-filled </summary>
        private Transform LastParent;
        public int CurrentIndex { get; private set; }

        /// <summary> Store the Current State  </summary>
        public int CurrentState { get; private set; }

        /// <summary> Store the Current State  </summary>
        public int LastState { get; private set; }
        /// <summary> Store the Current State  </summary>
        public int LastStance { get; private set; }
        /// <summary> Store the Current State  </summary>
        public int CurrentStance { get; private set; }

        /// <summary> Store the Current State  </summary>
        public int CurrentStartMode { get; private set; }

        /// <summary> Store the Current State  </summary>
        public int CurrentExitMode { get; private set; }

        public bool debug = false;


        private void Awake()
        {
            MigrateTrackers(false);     //-----Remove in future updates-----

            if (Tracker == null) return;

            DefaultState = new TransformTracker
            {
                RelativeTo = Tracker.parent,
                Position = Tracker.localPosition,
                Rotation = Tracker.localEulerAngles,
                Lerp = 10
            };
        }

        /// <summary> Re-targets the Animal and the auto-filled [Relative To] references when this object is reparented </summary>
        private void OnTransformParentChanged()
        {
            if (!Application.isPlaying) return;      //At Edit time OnValidate keeps the references up to date

            var OldAnimal = animal;

            if (OldAnimal != null) ListenOff(OldAnimal);

            animal = this.FindComponent<MAnimal>();

            if (animal != null) ListenOn(animal);

            ReResolveRelativeTo();

            //Cast the first stance on the new Animal, so the Tracker does not wait for the next State change
            if (animal != null && animal != OldAnimal) OnStanceChange(animal.ActiveStance.ID);
        }

        /// <summary> Re-points every auto-filled [Relative To] reference at the new parent, leaving authored ones alone </summary>
        private void ReResolveRelativeTo()
        {
            var parent = transform.parent;

            if (LastParent == parent) return;        //Still under the same parent, nothing to re-resolve

            foreach (var item in Trackers)
                if (item.RelativeTo == null || item.RelativeTo == LastParent)
                    item.RelativeTo = parent;        //Only the ones Initialize() filled in follow the hierarchy

            if (DefaultState != null && (DefaultState.RelativeTo == null || DefaultState.RelativeTo == LastParent))
                DefaultState.RelativeTo = parent;

            LastParent = parent;
        }



        protected virtual void OnEnable()
        {
            if (animal == null) animal = this.FindComponent<MAnimal>();

            ListenOn(animal);

            if (NoParent && Tracker) Tracker.SetParent(null);

            Initialize();

            //The cycle runs even without an Animal, so a later SetAnimal or reparent can drive the Tracker right away
            StartUpdateCycle();

            //Cast the first stance
            // OnStateChange(animal.ActiveStateID);
            if (animal != null && animal.ActiveStance != null) OnStanceChange(animal.ActiveStance.ID);

            CurrentLerp = 0;
        }

        /// <summary> Starts the Tracker update coroutine. Safe to call more than once </summary>
        private void StartUpdateCycle()
        {
            if (UpdateRoutine != null) return;       //Already running

            UpdateRoutine = FixedUpdate ? StartCoroutine(UpdateCycleFixed()) : StartCoroutine(UpdateCycle());
        }

        private void ListenOn(MAnimal animal)
        {
            if (animal == null) return;
            animal.OnStateChange.AddListener(OnStateChange);
            animal.OnStanceChange.AddListener(OnStanceChange);
            animal.OnModeStart.AddListener(OnModeStart);
            animal.OnModeEnd.AddListener(OnModeEnd);
        }

        private void ListenOff(MAnimal animal)
        {
            if (animal == null) return;
            animal.OnStateChange.RemoveListener(OnStateChange);
            animal.OnStanceChange.RemoveListener(OnStanceChange);
            animal.OnModeStart.RemoveListener(OnModeStart);
            animal.OnModeEnd.RemoveListener(OnModeEnd);
        }



        private IEnumerator UpdateCycleFixed()
        {
            var fixedTime = new WaitForFixedUpdate();

            while (true)
            {
                yield return fixedTime;
                UpdateTrackerPos(Time.fixedDeltaTime);
            }
        }

        private IEnumerator UpdateCycle()
        {
            while (true)
            {
                yield return null;
                UpdateTrackerPos(Time.deltaTime);
            }
        }

        /// <summary> Auto-fills the empty [Relative To] references with this object's parent </summary>
        private void Initialize()
        {
            var parent = transform.parent;

            foreach (var item in Trackers)
            {
                if (item.RelativeTo == null || item.RelativeTo == transform)
                    item.RelativeTo = parent; //Set the Relative To to the Tracker if is null
            }

            LastParent = parent;
        }

        private void OnDisable()
        {
            ListenOff(animal);

            StopAllCoroutines();
            UpdateRoutine = null;
        }

        private void OnStateChange(int state)
        {
            if (FindState(state))
            {
                LastState = CurrentState;   //Store the Last State
                CurrentState = state;       //Store the Current Entering State
            }
            else
            {
                if (!FindStance(CurrentStance))
                {
                    //If no State or Stance found, go to Default
                    LastState = CurrentState; //Store the Last State
                    CurrentState = -1;
                    Current = DefaultState;
                    // Debug.Log("NOT FOUND GO TO DEFAULT");
                }
            }
        }


        private void OnStanceChange(int stance)
        {
            var found = FindStance(stance);

            LastStance = CurrentStance; //Store the Last Stance
            CurrentStance = stance;

            if (!found) FindState(CurrentState); //When a Stance changes recheck the current tracker.
        }

        private bool FindState(int state)
        {
            bool Found = false;

            for (int i = 0; i < Trackers.Count; i++)
            {
                var item = Trackers[i];

                if (!item.Active) continue;

                bool ValidState = item.CheckStateID(state);
                bool ValidStance = !item.CheckStance || item.CheckStanceID(CurrentStance);

                if (ValidState && ValidStance)
                {
                    if (!Found)
                    {
                        // Debug.Log($"FindState REACT: {item.name}");
                        item.reaction.React(animal);
                        Found = true;

                        if (debug)
                            Debug.Log($"AnimalTracker: State Changed [{animal.ActiveState.name}] Tracker [{item.name}]", this);

                        //Update the Reposition Tracker 
                        if (item.RepositionTracker)
                        {
                            CurrentLerp = 0;
                            CurrentIndex = i;
                            Current = item;
                        }
                    }
                }
            }

            return Found;
        }

        private bool FindStance(int stance)
        {
            bool Found = false;

            for (int i = 0; i < Trackers.Count; i++)
            {
                var item = Trackers[i];
                if (!item.Active) continue;

                bool ValidStance = item.CheckStanceID(stance);
                bool ValidState = !item.CheckState || item.CheckStateID(CurrentState);

                if (ValidState && ValidStance)
                {
                    if (!Found)
                    {
                        //  Debug.Log($"FindStance {item.name}");
                        item.reaction.React(animal);
                        Found = true;

                        if (debug)
                            Debug.Log($"AnimalTracker: Stance Changed [{animal.ActiveStance.ID.name}] Tracker [{item.name}]", this);

                        //Update the Reposition Tracker 
                        if (item.RepositionTracker)
                        {
                            CurrentLerp = 0;
                            CurrentIndex = i;
                            Current = item;
                        }
                    }
                }
            }
            return Found;
        }

        private bool FoundMode;

        private void OnModeStart(int Mode, int Ability)
        {
            for (int i = 0; i < Trackers.Count; i++)
            {
                var item = Trackers[i];
                if (item.ModeAction == TransformTracker.ModeStatus.Start)
                {
                    bool ValidState = !item.CheckState || item.CheckStateID(CurrentState);
                    bool ValidStance = !item.CheckStance || item.CheckStanceID(CurrentStance);

                    if (item.CheckModeID(Mode) && ValidState && ValidStance)
                    {
                        if (item.CheckAbilityIndex(Ability))
                        {
                            FoundMode = true;
                            //item.OnTrackerChanged.Invoke();
                            // Debug.Log($"OnModeStart {item.name}");
                            item.reaction.React(animal);

                            if (debug)
                                Debug.Log($"AnimalTracker: Mode Started [{animal.ActiveMode.Name}] Tracker [{item.name}]", this);

                            //Update the Reposition Tracker 
                            if (item.RepositionTracker)
                            {
                                CurrentLerp = 0;
                                CurrentIndex = i;
                                Current = item;
                            }
                        }
                    }
                }
            }
        }

        private void OnModeEnd(int Mode, int Ability)
        {
            foreach (var item in Trackers)
            {
                if (item.ModeAction == TransformTracker.ModeStatus.Exit)
                {
                    bool ValidState = !item.CheckState || item.CheckStateID(CurrentState);
                    bool ValidStance = !item.CheckStance || item.CheckStanceID(CurrentStance);

                    if (item.CheckModeID(Mode) && ValidState && ValidStance)
                    {
                        if (item.CheckAbilityIndex(Ability))
                        {
                            // item.OnTrackerChanged.Invoke();
                            item.reaction.React(animal);

                            if (debug)
                                Debug.Log($"AnimalTracker: Mode Ended [{(animal.LastMode != null ? animal.LastMode.Name : "None")}] Tracker [{item.name}]", this);

                            // Debug.Log($"OnModeEnd {item.name}");
                            CurrentLerp = 0;

                        }
                    }
                }
            }

            if (FoundMode)
                FindState(CurrentState); //When a Stance changes recheck the current tracker.

            FoundMode = false;
        }

        /// <summary> Re-targets this Tracker to another Animal, moving all the listeners across </summary>
        public void SetAnimal(GameObject go)
        {
            if (go == null) return;

            if (!go.TryGetComponent<MAnimal>(out var newAnimal)) return;
            if (newAnimal == animal) return;        //Already listening to that Animal

            ListenOff(animal);
            animal = newAnimal;
            ListenOn(animal);

            //Cast the first state and stance
            OnStateChange(animal.ActiveStateID);
            OnStanceChange(animal.ActiveStance.ID);
        }

        /// <summary> Re-targets this Tracker to another Animal, moving all the listeners across </summary>
        public void SetAnimal(Component an) => SetAnimal(an.gameObject);


        private void UpdateTrackerPos(float deltatime)
        {
            if (Current == null || Tracker == null || Current.RelativeTo == null) return;

            CurrentLerp = Mathf.Lerp(CurrentLerp, Current.Lerp, deltatime * Current.Lerp);

            Tracker.SetPositionAndRotation(
                Vector3.Slerp(Tracker.position, Current.RelativeTo.TransformPoint(Current.Position), deltatime * CurrentLerp),
                Quaternion.Slerp(Tracker.rotation, Current.RelativeTo.rotation * Quaternion.Euler(Current.Rotation), deltatime * CurrentLerp));
        }

        private void Reset()
        {
            animal = this.FindComponent<MAnimal>();

            Trackers = new List<TransformTracker>(1)
            {
                new()
                {
                    Active = true,
                    RelativeTo = transform,
                    States = new(new List<StateID>() { MTools.GetInstance<StateID>("Idle") }),
                    Stances = new(new List<StanceID>() { MTools.GetInstance<StanceID>("Default") }),
                    Lerp = 2,
                    Position = new Vector3(0, 0, 0.25f),
                    Rotation = new Vector3(0, 0, 0)
                }
            };
        }


        /// <summary> Folds every legacy single State/Stance/Mode/Ability value into the new Lists </summary>
        private void MigrateTrackers(bool setDirty)     //-----Remove in future updates-----
        {
            if (Trackers == null) return;

            var migrated = false;

            foreach (var item in Trackers)
                if (item.MigrateIDLists()) migrated = true;

            if (migrated && setDirty) MTools.SetDirty(this);
        }

        /// <summary> Updates the Tracker names and their Relative To references </summary>
        private void OnValidate()
        {
            MigrateTrackers(true);      //-----Remove in future updates-----

            if (animal == null) animal = this.FindComponent<MAnimal>();

            foreach (var item in Trackers)
            {
                var newName = "";
                if ((item.track & TrackerType.State) == TrackerType.State)
                {
                    newName += " [" + TransformTracker.ListName(item.States) + "]";
                }

                if ((item.track & TrackerType.Stance) == TrackerType.Stance)
                {
                    newName += " [" + TransformTracker.ListName(item.Stances) + "]";
                }

                if ((item.track & TrackerType.Mode) == TrackerType.Mode)
                {
                    newName += " [" + TransformTracker.ListName(item.Modes) + "]";
                }

                if ((item.track & TrackerType.Ability) == TrackerType.Ability)
                {
                    newName += " Ability [" + item.AbilitiesName + "]";
                }

                item.name = newName;


                if (item.RelativeTo == null || item.RelativeTo == transform)
                    item.RelativeTo = transform.parent; //Set the Relative To to the Tracker if is null
            }
        }

        [HideInInspector, SerializeField]
        private int selectedIndex;

        private void OnDrawGizmosSelected()
        {
            if (Tracker == null) return;    //Nothing is being repositioned

            foreach (var item in Trackers)
            {
                if (item.RelativeTo == null || !item.RepositionTracker) continue;

                Gizmos.color = item.Active ? item.DebugColor : Color.red;
                var oldMatrix = Gizmos.matrix;

                Gizmos.matrix = Matrix4x4.TRS(item.RelativeTo.TransformPoint(item.Position), item.RelativeTo.rotation * Quaternion.Euler(item.Rotation), Vector3.one);
                Gizmos.DrawCube(Vector3.zero, Vector3.one * DebugSize);
                Gizmos.matrix = oldMatrix;
                Gizmos.DrawLine(item.RelativeTo.TransformPoint(item.Position), item.RelativeTo.position);
            }
        }
    }

    public enum TrackerType { State = 1, Stance = 2, Mode = 4, Ability = 8 }

    [System.Serializable]
    public class TransformTracker
    {
        [HideInInspector]
        public string name;
        public enum ModeStatus { Start, Exit }

        [HideInInspector] public bool Active;

        [Flag, HideInInspector]
        public TrackerType track = TrackerType.State;

        [Tooltip("Include/Exclude the States that can activate this Tracker")]
        [Hide(nameof(track), false, true, true, (int)TrackerType.State)]
        public IDListCheck<StateID> States = new();

        [Tooltip("Include/Exclude the Stances that can activate this Tracker")]
        [Hide(nameof(track), false, true, true, (int)TrackerType.Stance)]
        public IDListCheck<StanceID> Stances = new();

        [Tooltip("Include/Exclude the Modes that can activate this Tracker")]
        [Hide(nameof(track), false, true, true, (int)TrackerType.Mode, (int)TrackerType.Ability)]
        public IDListCheck<ModeID> Modes = new();

        [Tooltip("Include/Exclude the Mode Abilities that can activate this Tracker. Leave it empty to allow any Ability")]
        [Hide(nameof(track), false, true, true, (int)TrackerType.Ability)]
        public AbilityListCheck Abilities = new();

        [Hide(nameof(track), false, true, true, (int)TrackerType.Mode, (int)TrackerType.Ability)]
        public ModeStatus ModeAction = ModeStatus.Start;

        /// <summary> Is this Tracker checking for States? </summary>
        public bool CheckState => (track & TrackerType.State) == TrackerType.State;
        /// <summary> Is this Tracker checking for Stances? </summary>
        public bool CheckStance => (track & TrackerType.Stance) == TrackerType.Stance;
        /// <summary> Is this Tracker checking for Modes? </summary>
        public bool CheckMode => (track & TrackerType.Mode) == TrackerType.Mode;
        /// <summary> Is this Tracker checking for Mode Abilities? </summary>
        public bool CheckAbility => (track & TrackerType.Ability) == TrackerType.Ability;

        /// <summary> Does a State ID pass the States ID List? </summary>
        public bool CheckStateID(int state) => CheckState && States != null && States.Check(state);

        /// <summary> Does a Stance ID pass the Stances ID List? </summary>
        public bool CheckStanceID(int stance) => CheckStance && Stances != null && Stances.Check(stance);

        /// <summary> Does a Mode ID pass the Modes ID List? </summary>
        public bool CheckModeID(int mode) => CheckMode && Modes != null && Modes.Check(mode);

        /// <summary> Does an Ability Index pass the Abilities list? An empty list allows any Ability </summary>
        public bool CheckAbilityIndex(int ability) => !CheckAbility || Abilities == null || Abilities.Check(ability);

        /// <summary> Readable name for the Abilities list. Used for the Tracker Inspector label </summary>
        public string AbilitiesName => Abilities != null ? Abilities.ToString() : "ANY";

        /// <summary> Returns a readable name for an ID List. Used for the Tracker Inspector label </summary>
        public static string ListName<T>(IDListCheck<T> list) where T : IDs
        {
            if (list == null || list.Empty) return "NONE";

            var prefix = list.include ? "" : "!";
            return prefix + string.Join(", ", list.IDs.ConvertAll(x => x != null ? x.name : "NONE"));
        }


        //-----Remove in future updates-----

        [HideInInspector] public StateID State;          //OLD WAY. Replaced by the [States] ID List
        [HideInInspector] public StanceID Stance;        //OLD WAY. Replaced by the [Stances] ID List
        [HideInInspector] public ModeID Mode;            //OLD WAY. Replaced by the [Modes] ID List
        [HideInInspector] public int Ability = -1;       //OLD WAY. Replaced by the [Abilities] list. -1 is the inert value (any Ability)

        /// <summary> Folds the legacy single State/Stance/Mode/Ability values into the new Lists. True if anything was migrated </summary>
        public bool MigrateIDLists()
        {
            var migrated = false;

            if (State != null)
            {
                if (States == null || States.Empty) States = new(new List<StateID>() { State });
                State = null;       //Drain the legacy value, so it never overrides a later user edit
                migrated = true;
            }

            if (Stance != null)
            {
                if (Stances == null || Stances.Empty) Stances = new(new List<StanceID>() { Stance });
                Stance = null;      //Drain the legacy value, so it never overrides a later user edit
                migrated = true;
            }

            if (Mode != null)
            {
                if (Modes == null || Modes.Empty) Modes = new(new List<ModeID>() { Mode });
                Mode = null;        //Drain the legacy value, so it never overrides a later user edit
                migrated = true;
            }

            if (Ability >= 0)
            {
                Abilities ??= new();
                if (Abilities.Empty) Abilities.Indexes.Add(Ability);
                Ability = -1;       //Drain the legacy value back to the inert one
                migrated = true;
            }

            return migrated;
        }

        //-----Remove in future updates-----



        //public float CurrentLerp;

        [Space]
        public Reaction2 reaction;
        // public UnityEvent OnTrackerChanged = new();



        // [Tooltip("Reposition the Tracker GameObject using the Position and Rotation offsets when the tracker is active ")]
        [HideInInspector]
        public bool RepositionTracker = true;

        [Hide(nameof(RepositionTracker), false, true)]
        [Tooltip("Lerp Speed to the new Tracker Position")]
        public float Lerp = 2;

        [Hide(nameof(RepositionTracker), false, true)]
        public Vector3 Position;
        [Hide(nameof(RepositionTracker), false, true)]
        public Vector3 Rotation;

        [Hide(nameof(RepositionTracker), false, true)]
        [Tooltip("Use this Node Transform to a of the Tracker Object. Reference for the Local Position of the tracker")]
        public Transform RelativeTo;
        [Hide(nameof(RepositionTracker), false, true)]
        public Color DebugColor = Color.green;

    }

    /// <summary> Serializable list of Mode Ability Indexes with an Include/Exclude toggle </summary>
    [System.Serializable]
    public class AbilityListCheck
    {
        [Tooltip("Ability Indexes to check. Leave it empty to allow any Ability")]
        public List<int> Indexes = new();

        [Tooltip("Is/Is NOT any Ability Index on this list")]
        public bool include = true;

        /// <summary> True when there are no Ability Indexes on the list </summary>
        public bool Empty => Indexes == null || Indexes.Count == 0;

        /// <summary> Returns if an Ability Index is on the list, respecting Include/Exclude. An empty list allows any Ability </summary>
        public bool Check(int ability) => Empty || include == Indexes.Contains(ability);

        /// <summary> Readable name for the Tracker Inspector label </summary>
        public override string ToString() => Empty ? "ANY" : (include ? "" : "!") + string.Join(", ", Indexes);
    }

#if UNITY_EDITOR

    [CustomEditor(typeof(AnimalTracker))]
    public class AnimatorTrackerTransformEditor : UnityEditor.Editor
    {
        private AnimalTracker M;

        private SerializedProperty animal, Tracker, NoParent, Trackers, DebugSize, selectedIndex, FixedUpdate, debug;

        private ReorderableList Reo_List_Trackers;

        private readonly Color selectedColor = new(0.4f, 1f, 2f, 1f);

        /// <summary> Row fields that only make sense when a Tracker Transform is being repositioned </summary>
        private static readonly string[] RepositionFields = { "Lerp", "Position", "Rotation", "RelativeTo", "DebugColor" };

        /// <summary> Is there a Tracker Transform to reposition? </summary>
        private bool HasTracker => Tracker.objectReferenceValue != null;

        protected virtual void OnEnable()
        {
            M = (AnimalTracker)target;

            animal = serializedObject.FindProperty("animal");
            Tracker = serializedObject.FindProperty("Tracker");
            NoParent = serializedObject.FindProperty("NoParent");
            Trackers = serializedObject.FindProperty("Trackers");
            DebugSize = serializedObject.FindProperty("DebugSize");
            selectedIndex = serializedObject.FindProperty("selectedIndex");
            selectedIndex = serializedObject.FindProperty("selectedIndex");
            FixedUpdate = serializedObject.FindProperty("FixedUpdate");
            debug = serializedObject.FindProperty("debug");

            Reo_List_Trackers = new(serializedObject, Trackers, true, true, true, true)
            {
                drawElementCallback = DrawElementCallback,
                drawHeaderCallback = DrawHeaderCallback,
                onSelectCallback = OnSelectCallback,
            };


            Reo_List_Trackers.index = selectedIndex.intValue;
        }

        private void OnSelectCallback(ReorderableList list)
        {
            selectedIndex.intValue = list.index;
        }

        private void DrawHeaderCallback(Rect rect)
        {
            float height = EditorGUIUtility.singleLineHeight;
            Rect rect1 = new(rect.x + 30, rect.y + 2, rect.width / 2, height);
            Rect rect2 = new(rect.x + 20 + rect.width / 2, rect.y + 2, rect.width / 2 - 40, height);
            Rect rect3 = new(rect.width - 40, rect.y + 2, 70, height);

            EditorGUI.LabelField(rect1, "   Tracking Action");
            EditorGUI.LabelField(rect2, "   Description");

            if (HasTracker)
                EditorGUI.LabelField(rect3, new GUIContent("Reposition", "Reposition the Tracker GameObject using the Position and Rotation offsets when the tracker is active "));
        }

        private void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
        {
            float height = EditorGUIUtility.singleLineHeight;

            Rect rect0 = new(rect.x, rect.y + 2, 20, height);
            Rect rect1 = new(rect.x + 20, rect.y + 2, rect.width / 2, height);
            Rect rect2 = new(rect.x + 20 + rect.width / 2, rect.y + 2, rect.width / 2 - 20, height);
            Rect rect3 = new(rect.width + 20, rect.y + 2, 20, height);

            // Use rect1 and rect2 as needed

            var element = Trackers.GetArrayElementAtIndex(index);


            var track = element.FindPropertyRelative("track");
            var name = element.FindPropertyRelative("name");
            var active = element.FindPropertyRelative("Active");
            var RepositionTracker = element.FindPropertyRelative("RepositionTracker");


            var OldColor = GUI.backgroundColor;
            var OldColorRuntime = GUI.color;

            if (Application.isPlaying)
            {
                GUI.color = M.CurrentIndex == index ? selectedColor : OldColor;
            }

            GUI.backgroundColor = index == selectedIndex.intValue ? selectedColor : OldColor;

            EditorGUI.PropertyField(rect0, active, GUIContent.none);

            EditorGUIUtility.labelWidth = 35;
            EditorGUI.PropertyField(rect1, track);
            EditorGUIUtility.labelWidth = 0;
            EditorGUI.LabelField(rect2, name.stringValue, EditorStyles.boldLabel);

            if (HasTracker)
                EditorGUI.PropertyField(rect3, RepositionTracker, GUIContent.none);

            GUI.backgroundColor = OldColor;
            GUI.color = OldColorRuntime;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Track an Animal States,Stances and Modes and react to it.");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(animal);
                    MalbersEditor.DrawDebugIcon(debug);
                }
                EditorGUILayout.PropertyField(Tracker);

                if (HasTracker)
                {
                    EditorGUILayout.PropertyField(FixedUpdate);

                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(NoParent);

                        EditorGUIUtility.labelWidth = 40;
                        EditorGUILayout.PropertyField(DebugSize, GUILayout.MaxWidth(80));
                        EditorGUIUtility.labelWidth = 0;
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("No Tracker: the rows will only invoke their Reactions", MessageType.None);
                }
            }

            Reo_List_Trackers.DoLayoutList();

            Reo_List_Trackers.index = selectedIndex.intValue;

            var index = selectedIndex.intValue;


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (index != -1 && index < Trackers.arraySize)
                {
                    var element = Trackers.GetArrayElementAtIndex(index);
                    if (element != null)
                        DrawTrackerElement(element);
                }
            }


            //  base.OnInspectorGUI();

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary> Draws the selected row's fields, skipping the Reposition ones when there is no Tracker Transform </summary>
        private void DrawTrackerElement(SerializedProperty element)
        {
            var hasTracker = HasTracker;

            var end = element.GetEndProperty();
            var child = element.Copy();

            if (!child.NextVisible(true)) return;

            while (!SerializedProperty.EqualContents(child, end))
            {
                if (hasTracker || System.Array.IndexOf(RepositionFields, child.name) < 0)
                    EditorGUILayout.PropertyField(child, true);

                if (!child.NextVisible(false)) break;
            }
        }

        private void OnSceneGUI()
        {
            var index = selectedIndex.intValue;

            if (index == -1) return;

            if (M.Trackers.Count < index)
            {
                var item = M.Trackers[index];

                var transform = item.RelativeTo;

                if (!item.RepositionTracker) return;

                //if (transform == null) continue;
                //if (!item.RepositionTracker) continue;

                var Rotation = Tools.pivotRotation == PivotRotation.Local ? transform.rotation : Quaternion.identity;

                if (Tools.current == Tool.Move)
                {
                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        Vector3 piv = transform.TransformPoint(item.Position);
                        Vector3 NewPivPosition = Handles.PositionHandle(piv, Rotation);

                        if (cc.changed)
                        {
                            Undo.RecordObject(target, "Change Pos Goal");
                            item.Position = transform.InverseTransformPoint(NewPivPosition);
                            EditorUtility.SetDirty(target);
                        }
                    }
                }
                else if (Tools.current == Tool.Rotate)
                {
                    //Rotation = Quaternion.identity;

                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        Quaternion rot = Rotation * Quaternion.Euler(item.Rotation);
                        Vector3 piv = transform.TransformPoint(item.Position);
                        Quaternion NewRotation = Handles.RotationHandle(rot, piv);

                        if (cc.changed)
                        {
                            Undo.RecordObject(target, "Change Rot Goal");

                            NewRotation = Quaternion.Inverse(Rotation) * NewRotation;

                            item.Rotation = NewRotation.eulerAngles;
                            EditorUtility.SetDirty(target);
                        }
                    }
                }
            }
        }
    }
#endif
}
