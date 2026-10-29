// Last Change: 9/21/2026 9:40am
using UnityEngine;
using System.Collections.Generic;
using MalbersAnimations.Utilities;
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using UnityEngine.Events;
using System;
using MalbersAnimations.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.PathCreation
{
    public enum PathFollowDir { None, Forward, Backward }

    [Serializable] public class PathConstraintEvent : UnityEvent<MPathConstraint> { }
    [AddComponentMenu("Malbers/Animal Controller/Path"), SelectionBase]
    public class MPath : MonoBehaviour
    {
        [RequiredField]
        [Tooltip("Path Reference")]
        public IPath Path;

        [Tooltip("The Animal will align automatically when is near the Path ")]
        public BoolReference Automatic = new(true);

        [Tooltip("If the Animal is already on another Path then change to the new path. Else use the Path Input on your character")]
        public BoolReference AutoChangePath = new(true);

        [Tooltip("Radius to check if the Character can Enter this path")]
        [Min(0)] public float SearchRadius = 0.5f;
        [Tooltip("Orient Smoothness per path")]
        [Min(0)] public float OrientSmoothness = 1f;

        [Tooltip("Offset of the Radius on the Path")]
        public Vector3 SearchOffset = new(0, 0.5f, 0);

        [Tooltip("Local Offset of the Animal Position with the Path")]
        public Vector3 AlignmentOffset = new(0, 0, 0);

        [Tooltip("Time needed so the animal can enter the same path again")]
        [Min(0)] public float pathCooldown = 1f;

        [Tooltip("The Animal Can Exit at the start of the path")]
        public BoolReference CanExitOnStart = new(true);
        [Tooltip("The Animal Can Exit at the end of the path")]
        public BoolReference CanExitOnEnd = new(true);

        [Tooltip("The Animal Can Exit at the in the middle of the path (Using Input)")]
        public BoolReference CanExitOnMiddle = new(true);

        [Tooltip("Rotate the Character using the Rotation Value Path Points")]
        public BoolReference usePathRotation = new(false);

        [Tooltip("Don't allow the Character to Rotate on the Spline... Move Backwards")]
        public BoolReference LockRotation = new(false);

        [Tooltip("Point the Animal always from Start to End of the Path")]
        public PathFollowDir FollowDirection = PathFollowDir.None;

        //MWC: Removed 'interval' (search rate now lives on MPathConstraint.SearchInterval),
        //and the whole trigger pipeline (PathBounds/expand/Layer). Detection is now character-driven.

        [Tooltip("When the Animal Enters the Path, it will activate this State")]
        public StateID ActivateState;

        [Tooltip("Remove Grounded From the Animal so it Aligns Directly to the Spline Path")]
        public bool IgnoreGrounded = true;

        [Tooltip("Ignore Vertical Alignment do just Horizontal Alignment")]
        public bool IgnoreVertical = true;


        [Tooltip("If this Path is activated while the character is on another path, Ignore Old Path Reactions")]
        public bool NoExitPathReactions = false;

        //Remove in the future, use the IDList instead of List
        [Tooltip("While the animal is on the path, all these states will be disabled")]
        public IDList<StateID> DisableStates;
        [Tooltip("States that can be used to exit the path or ignore the Path")]
        public IDList<StateID> IgnoreStates;
        [Tooltip("Modes that can be used to exit early the Path")]
        public IDList<ModeID> IgnoreModes;
        //--------------------------------



        [Tooltip("The Animal Exit/Ignore the path if is on any Mode")]
        public BoolReference exitAnyMode = new(false);

        private float m_PathPosition;
        //MWC: Removed m_PreviousPathPosition. It was clamped in InBounds() but never read anywhere.

        /// <summary> World-space AABB of the path, expanded by SearchRadius. Cached; used for the
        /// cheap first-phase proximity rejection. Call RecalculateWorldBounds() if the path moves at runtime. </summary>
        public Bounds WorldBounds { get; private set; }


        /// <summary> The path is closed/Looped </summary>
        public bool IsClosed => Path.IsClosed;

        [Tooltip("Adds a reaction to the Animal entering the Path")]
        public Reaction2 EnterReaction;

        [Tooltip("Adds a reaction to the Animal exiting the Path")]
        public Reaction2 ExitReaction;

        [Tooltip("Adds a reaction to the Animal entering the path from the Start point of the Path")]
        public Reaction2 EnterFromStart;

        [Tooltip("Adds a reaction to the Animal entering the path from the End point of the Path")]
        public Reaction2 EnterFromEnd;

        [Tooltip("Adds a reaction to the Animal entering the path from the middle of the Path")]
        public Reaction2 EnterFromMiddle;

        [Tooltip("Adds a reaction to the Animal exiting the path from the start of the Path")]
        public Reaction2 ExitFromStart;

        [Tooltip("Adds a reaction to the Animal exiting the path from the End of the Path")]
        public Reaction2 ExitFromEnd;

        [Tooltip("Adds a reaction to the Animal exiting the path from the Middle of the Path")]
        public Reaction2 ExitFromMiddle;

        [Tooltip("Stores the Current Path Position of the Character in a Transform")]
        public TransformReference PathPosition = new();


        //MWC: Removed OnEnterBounds/OnExitBounds. They were "entered the trigger box" events;
        //the box is gone. Use OnEnterPath/OnExitPath or CanEnterPath (manual mode) instead.

        public BoolEvent CanEnterPath = new();

        public PathConstraintEvent OnEnterPath = new();
        public PathConstraintEvent OnExitPath = new();

        public BoolEvent IsOnEndOfPath = new();
        public BoolEvent IsOnStartOfPath = new();


        public bool ReachEnd { get; internal set; }
        public bool ReachStart { get; internal set; }


        public void SetEndOfPathEvent(bool v) => IsOnEndOfPath.Invoke(ReachEnd = v);
        public void SetStartOfPathEvent(bool v) => IsOnStartOfPath.Invoke(ReachStart = v);


        public bool debug;

        /// <summary>Editor only console log for this Path, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void Debugging(string value)
        {
#if UNITY_EDITOR
            if (debug) UnityEngine.Debug.Log($"<B>[{name}]</B> → <color=white>[{value}]</color>", this);
#endif
        }

        /// <summary>  List to access all the Paths </summary>
        public static List<MPath> Paths;

        private void OnEnable()
        {
            Paths ??= new List<MPath>();
            Paths.Add(this);

            if (!TryGetComponent(out Path))
            {
                Debugging("Path Not found. Disable All");
                enabled = false;
                return;
            }

            RecalculateWorldBounds(); //MWC: cache the world AABB used for proximity rejection
        }

        private void OnDisable()
        {
            Paths?.Remove(this);
        }

        private void Reset()
        {
            Path ??= GetComponent<IPath>();

            //MWC: No BoxCollider/TriggerProxy/layer setup anymore. Only create the PathPosition marker.
            var newPathPosition = new GameObject("PathPosition");
            newPathPosition.transform.parent = transform;
            newPathPosition.transform.ResetLocal();
            PathPosition.Value = newPathPosition.transform;
        }

        private void OnValidate()
        {
            Path ??= GetComponent<IPath>();
            RecalculateWorldBounds(); //MWC: keep the cached bounds in sync while editing
        }

        /// <summary> Recomputes the cached world-space AABB (path bounds expanded by SearchRadius).
        /// Cheap; call this if the path is moved or reshaped at runtime. </summary>
        public void RecalculateWorldBounds()
        {
            if (Path == null) return;

            var b = Path.WorldBounds;
            b.Expand(SearchRadius * 2f); //grow by SearchRadius on every side so Phase-1 rejection includes the search sphere
            WorldBounds = b;
        }

        /// <summary> Called by a MPathConstraint every search tick to test proximity to this path.
        /// Two-phase: cheap world-AABB rejection, then exact nearest-point test.
        /// Returns true when the character is within the search sphere. </summary>
        //MWC: Replaces the trigger-driven InBounds(). Detection is now character-driven (the constraint
        //polls MPath.Paths), so the per-character state (cooldown, next-path, "can enter" prompt) lives
        //on the constraint and multiple characters no longer clobber a single shared flag.
        public bool SearchConstraint(MPathConstraint constraint, bool canEnter)
        {
            if (constraint.Path == this) return false; //already travelling this path

            var charPos = constraint.ContraintPos;

            //Phase 1 — cheap world-AABB rejection. WorldBounds already includes SearchRadius; allow the
            //character's own Radius as extra slack so we never reject a sphere that would actually reach in.
            if (WorldBounds.SqrDistance(charPos) > constraint.Radius * constraint.Radius)
            {
                ConstraintLeft(constraint);
                return false;
            }

            //Phase 2 — exact nearest point on the path (only runs for the few paths we are near)
            m_PathPosition = Path.GetClosestTimeOnPath(charPos);
            Vector3 pathPos = Path.GetPointAtTime(m_PathPosition) + SearchOffset;

            if (debug)
            {
                MDebug.DrawWireSphere(pathPos, Color.red, SearchRadius);
                MDebug.DrawWireSphere(charPos, Color.green, constraint.Radius);
            }

            if (PathPosition.Value) PathPosition.Value.position = pathPos;

            if (!MTools.DoSpheresIntersect(pathPos, SearchRadius, charPos, constraint.Radius))
            {
                ConstraintLeft(constraint);
                return false;
            }

            //Within the search sphere
            if (constraint.LastPath.Contains(this)) return true; //in cooldown: detected, but don't enter yet
            if (!canEnter) return true;                          //another path already handled entering this tick

            if (Automatic.Value)
            {
                if (constraint.Path == null || AutoChangePath.Value)
                    constraint.EnterPath(this);      //auto-enter / auto-switch
                else
                    constraint.NextPath = this;      //queue it, wait for Path Input
            }
            else
            {
                constraint.SetCanEnter(this, true);  //manual mode: raise CanEnterPath once (per constraint+path)
                constraint.NextPath = this;
            }
            return true;
        }

        //MWC: The character is out of range of this path. Clear its per-constraint state.
        private void ConstraintLeft(MPathConstraint constraint)
        {
            constraint.SetCanEnter(this, false);         //lower the manual "can enter" prompt if it was up
            constraint.LastPath.Remove(this);            //self-heal the cooldown once the character walks away
            if (constraint.NextPath == this) constraint.NextPath = null;
        }


        [HideInInspector, SerializeField] private int Editor_Tabs1;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (debug)
            {
                if (Path != null && !Path.IsClosed)
                {
                    Handles.color = Color.white;
                    var StartSpline = Path.StartPath;
                    var EndSpline = Path.EndPath;

                    EndSpline.y -= 0.2f;
                    StartSpline.y -= 0.2f;

                    Handles.Label(StartSpline, "Start");
                    Handles.Label(EndSpline, "End");
                }

                if (!Application.isPlaying)
                {

                    Gizmos.color = Color.red;
                    Gizmos.matrix = transform.localToWorldMatrix;
                    Gizmos.DrawWireSphere(SearchOffset, SearchRadius);
                }
            }


        }


        private void OnDrawGizmosSelected()
        {
            //MWC: Draw the cached world-space search AABB (replaces the old trigger BoxCollider gizmo)
            if (debug && Path != null)
            {
                if (!Application.isPlaying) RecalculateWorldBounds();

                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(WorldBounds.center, WorldBounds.size);
            }
        }
#endif
    }


#if UNITY_EDITOR
    [CustomEditor(typeof(MPath)), CanEditMultipleObjects]
    public class MPathEditor : Editor
    {
        private readonly string[] Tabs = new string[] { "Path", "Animal", "Reactions", "Events" };

        private SerializedProperty Editor_Tabs1, //m_Path,
            usePathRotation, LockRotation, FollowDirection, Automatic, AutoChangePath, ExitOnMiddle, OrientSmoothness,
            ActivateState, DisableStates, IgnoreStates, IgnoreModes, IgnoreGrounded, pathCooldown, AlignmentOffset, IgnoreVertical, PathPosition,
            SearchOffset, SearchRadius, ExitOnStart, ExitOnEnd, NoExitPathReactions,
            EnterReaction, ExitReaction, debug, exitAnyMode;

        private MPath M;

        private void OnEnable()
        {
            M = (MPath)target;

            Automatic = serializedObject.FindProperty("Automatic");
            NoExitPathReactions = serializedObject.FindProperty("NoExitPathReactions");
            PathPosition = serializedObject.FindProperty("PathPosition");
            AutoChangePath = serializedObject.FindProperty("AutoChangePath");
            exitAnyMode = serializedObject.FindProperty("exitAnyMode");
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            // m_Path = serializedObject.FindProperty("m_Path");
            SearchOffset = serializedObject.FindProperty("SearchOffset");
            pathCooldown = serializedObject.FindProperty("pathCooldown");
            FollowDirection = serializedObject.FindProperty("FollowDirection");
            OrientSmoothness = serializedObject.FindProperty("OrientSmoothness");
            SearchRadius = serializedObject.FindProperty("SearchRadius");
            ActivateState = serializedObject.FindProperty("ActivateState");

            DisableStates = serializedObject.FindProperty("DisableStates");
            IgnoreStates = serializedObject.FindProperty("IgnoreStates");
            IgnoreModes = serializedObject.FindProperty("IgnoreModes");

            EnterReaction = serializedObject.FindProperty("EnterReaction");
            ExitReaction = serializedObject.FindProperty("ExitReaction");
            IgnoreGrounded = serializedObject.FindProperty("IgnoreGrounded");
            debug = serializedObject.FindProperty("debug");
            usePathRotation = serializedObject.FindProperty("usePathRotation");
            LockRotation = serializedObject.FindProperty("LockRotation");
            ExitOnStart = serializedObject.FindProperty("CanExitOnStart");
            ExitOnEnd = serializedObject.FindProperty("CanExitOnEnd");
            ExitOnMiddle = serializedObject.FindProperty("CanExitOnMiddle");
            AlignmentOffset = serializedObject.FindProperty("AlignmentOffset");
            IgnoreVertical = serializedObject.FindProperty("IgnoreVertical");



            EnterFromStart = serializedObject.FindProperty("EnterFromStart");
            EnterFromEnd = serializedObject.FindProperty("EnterFromEnd");
            ExitFromStart = serializedObject.FindProperty("ExitFromStart");
            ExitFromEnd = serializedObject.FindProperty("ExitFromEnd");
            EnterFromMiddle = serializedObject.FindProperty("EnterFromMiddle");
            ExitFromMiddle = serializedObject.FindProperty("ExitFromMiddle");




            CanEnterPath = serializedObject.FindProperty("CanEnterPath");
            OnEndOfPath = serializedObject.FindProperty("IsOnEndOfPath");
            OnStartOfPath = serializedObject.FindProperty("IsOnStartOfPath");


            OnEnterPath = serializedObject.FindProperty("OnEnterPath");
            OnExitPath = serializedObject.FindProperty("OnExitPath");
        }
        private SerializedProperty EnterFromStart, EnterFromEnd, EnterFromMiddle, ExitFromStart, ExitFromEnd, ExitFromMiddle;
        private SerializedProperty CanEnterPath, OnEndOfPath, OnStartOfPath;
        private SerializedProperty OnEnterPath, OnExitPath;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs);

            switch (Editor_Tabs1.intValue)
            {
                case 0: DrawPath(); break;
                case 1: DrawAnimal(); break;
                case 2: DrawReactions(); break;
                case 3: DrawEvents(); break;

                default:
                    break;
            }
            serializedObject.ApplyModifiedProperties();
        }


        private void DrawReactions()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EnterReaction.isExpanded = MalbersEditor.Foldout(EnterReaction.isExpanded, "Reactions");
                if (EnterReaction.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(EnterReaction);
                    EditorGUILayout.PropertyField(ExitReaction);
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(EnterFromStart);
                    EditorGUILayout.PropertyField(EnterFromEnd);
                    EditorGUILayout.PropertyField(EnterFromMiddle);
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(ExitFromStart);
                    EditorGUILayout.PropertyField(ExitFromEnd);
                    EditorGUILayout.PropertyField(ExitFromMiddle);
                    EditorGUI.indentLevel--;
                }
            }
        }
        private void DrawEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                OnEnterPath.isExpanded = MalbersEditor.Foldout(OnEnterPath.isExpanded, "Events");
                if (OnEnterPath.isExpanded)
                {
                    EditorGUILayout.PropertyField(OnEnterPath);
                    EditorGUILayout.PropertyField(OnExitPath);

                    EditorGUILayout.PropertyField(CanEnterPath);
                    EditorGUILayout.PropertyField(OnStartOfPath);
                    EditorGUILayout.PropertyField(OnEndOfPath);
                }
            }
        }

        private void DrawPath()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                AutoChangePath.isExpanded = MalbersEditor.Foldout(AutoChangePath.isExpanded, "Path");
                if (AutoChangePath.isExpanded)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledGroupScope(true))
                            EditorGUILayout.ObjectField("Path", M.Path as MonoBehaviour, typeof(IPath), false);

                        if (GUILayout.Button(MalbersEditor.Icon_Update, EditorStyles.miniButton, GUILayout.Width(28), GUILayout.Height(20)))
                        {
                            M.Path = M.GetComponent<IPath>();
                            EditorUtility.SetDirty(target);
                            serializedObject.ApplyModifiedProperties();
                        }
                        MalbersEditor.DrawDebugIcon(debug);
                    }
                    EditorGUILayout.PropertyField(Automatic);
                    // EditorGUILayout.PropertyField(m_Path);
                    EditorGUILayout.PropertyField(AutoChangePath);


                    EditorGUILayout.PropertyField(LockRotation);

                    if (M.LockRotation.Value)
                        EditorGUILayout.PropertyField(FollowDirection);

                    EditorGUILayout.PropertyField(usePathRotation);

                    EditorGUILayout.PropertyField(AlignmentOffset);
                    EditorGUILayout.PropertyField(PathPosition);
                    EditorGUILayout.PropertyField(OrientSmoothness);
                }
            }
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                ExitOnEnd.isExpanded = MalbersEditor.Foldout(ExitOnEnd.isExpanded, "Exit  Path");
                if (ExitOnEnd.isExpanded)
                {
                    EditorGUILayout.PropertyField(NoExitPathReactions);
                    EditorGUILayout.PropertyField(ExitOnStart);
                    EditorGUILayout.PropertyField(ExitOnEnd);
                    EditorGUILayout.PropertyField(ExitOnMiddle);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                pathCooldown.isExpanded = MalbersEditor.Foldout(pathCooldown.isExpanded, "Search");
                if (pathCooldown.isExpanded)
                {
                    //MWC: 'interval' and the Trigger Bounds section are gone (detection is character-driven).
                    //SearchRadius/SearchOffset define the proximity sphere the character tests against.
                    EditorGUILayout.PropertyField(pathCooldown);
                    EditorGUILayout.PropertyField(SearchRadius);
                    EditorGUILayout.PropertyField(SearchOffset);
                }
            }
        }

        private void DrawAnimal()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                ActivateState.isExpanded = MalbersEditor.Foldout(ActivateState.isExpanded, "Enter Path");
                if (ActivateState.isExpanded)
                {
                    EditorGUILayout.PropertyField(ActivateState);


                    EditorGUILayout.PropertyField(IgnoreVertical);
                    EditorGUILayout.PropertyField(IgnoreGrounded);


                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(DisableStates);
                    EditorGUI.indentLevel--;
                }
            }
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SearchRadius.isExpanded = MalbersEditor.Foldout(SearchRadius.isExpanded, "Exit Path");

                if (SearchRadius.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(IgnoreStates);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.PropertyField(exitAnyMode);
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(IgnoreModes);
                    EditorGUI.indentLevel--;
                }
            }
        }
    }
#endif
}