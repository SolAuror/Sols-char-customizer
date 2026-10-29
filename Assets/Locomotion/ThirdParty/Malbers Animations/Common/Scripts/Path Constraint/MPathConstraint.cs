// Last Change: 9/21/2026 9:40am
using UnityEngine;
using MalbersAnimations.Events;
using MalbersAnimations.Controller;
using System.Collections.Generic;
using MalbersAnimations.Scriptables;



#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.PathCreation
{
    [AddComponentMenu("Malbers/Animal Controller/Path Constraint")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/path-constraint")]
    /// <summary>Constraint the Animal to a path </summary>
    public class MPathConstraint : MonoBehaviour, IAnimatorListener
    {
        [RequiredField]
        public MAnimal animal;

        public MPath m_Path;

        [Tooltip("It will move automatically when is on a spline, no Input Needed")]
        public BoolReference AutoMove = new(false);

        public MPath Path { get => m_Path; set => m_Path = value; }

        // protected CinemachinePathBase.PositionUnits T => CinemachinePathBase.PositionUnits.PathUnits; //Always needs to be on Path Units
        /// <summary> m_SearchRadius:  -1 </summary>
        // private readonly int m_SearchRadius = -1;

        [Tooltip("Radius to check if the Character can Enter this path")]
        [Min(0)] public float Radius = 0.5f;

        [Tooltip("Offset of the Radius on the Constraint")]
        public Vector3 Offset = new(0, 0.5f, 0);

        [Tooltip("Forward point to calculate the Path Direction")]
        public float ForwardOffset = 0.3f;

        //MWC: How often (seconds) this character scans MPath.Paths for a nearby path to enter.
        //Replaces the per-path 'interval' + trigger coroutine. 0 = every frame.
        [Tooltip("How often (seconds) the character searches for nearby paths to enter")]
        [Min(0)] public float SearchInterval = 0.1f;

        [Tooltip("Smoothness to orient the animal to the Path")]
        public float OrientSmoothness = 5f;
        [Tooltip("Smoothness to align the animal to the Path")]
        public float AlignSmoothness = 5f;

        [Tooltip("Enable debug mode")]
        public bool debug;

        public GameObjectEvent OnEnterPath = new();
        public GameObjectEvent OnExitPath = new();
        [HideInInspector, SerializeField] private int Editor_Tabs1;


        /// <summary> The Animal is on a Path but there's a new Path that can be activated </summary>
        public MPath NextPath { get; set; }

        /// <summary> The animal has Just Enter a Path </summary>
        public MPath JustEnter { get; set; }
        /// <summary> The animal has Just exit a Path </summary>
        public MPath JustExit { get; set; }

        public HashSet<MPath> LastPath { get; set; }

        //MWC: Accumulates DeltaTime between path searches, and tracks which path (if any) is currently
        //raising its manual "CanEnterPath" prompt for THIS character (per-character, so multiple animals
        //no longer clobber a single shared flag on the path).
        private float m_SearchTimer;
        private MPath m_CanEnterFlag;

        /// <summary>Normalize value of the Root on the Spline </summary>
        public float PathRootPoint { get; private set; }
        public float PathFrontPoint { get; private set; }


        private Vector3 RootPos;
        private Vector3 FrontPos;

        /// <summary>Weight to quick Align the animal to the spline </summary>
        public float Weight { get; private set; }

        /// <summary> the Constraint is in a Path</summary>
        public bool InPath => m_Path != null;

        public bool CanExitOnStart => PathRootPoint > PathFrontPoint;
        public bool CanExitOnEnd => PathRootPoint < PathFrontPoint;


        public bool InEndOfThePath => (PathFrontPoint >= EndPathTolerance || PathRootPoint >= EndPathTolerance);
        public bool InStartOfThePath => (PathFrontPoint <= StartPathTolerance || PathRootPoint <= StartPathTolerance);


        public const float StartPathTolerance = 0.001f; //Tolerance to check if the animal is on the start or end of the path
        public const float EndPathTolerance = 0.999f; //Tolerance to check if the animal is on the start or end of the path


        private void Awake()
        {
            if (animal == null) animal = this.FindComponent<MAnimal>(); //Get the Animal
            JustExit = null;

            LastPath = new HashSet<MPath>();

            // if (PathPosition.Value != null) { PathPosition.Value.parent = null; } //UnParent the Path Position
        }


        public Vector3 ContraintPos => transform.TransformPoint(Offset);

        // public Vector3 PathDirection { get; private set; }

        /// <summary> Real Direction of the Path regarding the Character From Start to End </summary>
        public Vector3 RootPathDirection { get; private set; }
        public Vector3 FrontPathDirection { get; private set; }

        private void OnEnable()
        {
            animal.PreStateMovement += PreStateMovement;
            animal.OnStateChange.AddListener(OnStateChange);
            animal.OnModeStart.AddListener(OnModeStart);

            if (m_Path)
            {
                StartPosition = animal.Position;

                //MWC: Clear the backing field first. EnterPath() skips all its logic when (Path == path),
                //so a path pre-assigned in the Inspector never fired its Enter reactions/events/DisableStates.
                var startingPath = m_Path;
                m_Path = null;

                EnterPath(startingPath); //Try to make a first entry
                MoveOnPath(1);
                animal.Position = StartPosition;
            }
        }

        private void OnStateChange(int ActiveState)
        {
            if (InPath)
            {
                if (Path.IgnoreStates != null && Path.IgnoreStates.Contains(animal.ActiveStateID))
                {
                    ExitPath();
                    return;
                }
            }
        }

        private void OnDisable()
        {
            animal.PreStateMovement -= PreStateMovement;
            animal.OnStateChange.RemoveListener(OnStateChange);
            animal.OnModeStart.RemoveListener(OnModeStart);

            //MWC: lower any manual "can enter" prompt we were raising before we go away
            if (m_CanEnterFlag) SetCanEnter(m_CanEnterFlag, false);

            if (Path) ExitPath();
        }

        private void OnModeStart(int arg0, int arg1)
        {
            if (Path)
            {
                if (Path.exitAnyMode.Value ||
                    Path.IgnoreModes != null && Path.IgnoreModes.Contains(animal.ActiveMode.ID))
                {
                    ExitPath();
                }
            }
        }

        private void PreStateMovement(MAnimal animal)
        {
            //MWC: Character-driven path detection. Poll nearby paths on the search interval.
            //Runs whether or not we are already on a path (so AutoChangePath can switch paths).
            m_SearchTimer += animal.DeltaTime;
            if (m_SearchTimer >= SearchInterval)
            {
                m_SearchTimer = 0;
                SearchNearbyPaths();
            }

            Weight = Mathf.Clamp01(Mathf.MoveTowards(Weight, InPath ? 1 : 0, animal.DeltaTime * AlignSmoothness));

            if (!InPath) return;
            if (Path.IgnoreStates != null && Path.IgnoreStates.Contains(animal.ActiveStateID)) return; //If the

            MoveOnPath(Weight);
        }

        /// <summary> Scans the global path list for a path whose search sphere this character is inside. </summary>
        //MWC: Two-phase proximity search (cheap world-AABB rejection, then exact nearest-point) lives on
        //MPath.SearchConstraint. Only the first path in range enters this tick; the rest still get a chance
        //to clear their per-character state (cooldown, next-path, CanEnter prompt).
        private void SearchNearbyPaths()
        {
            var paths = MPath.Paths;
            if (paths == null) return;

            bool entered = false;

            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                if (path == null || path == Path || !path.isActiveAndEnabled) continue;

                path.SearchConstraint(this, canEnter: !entered);

                if (Path == path) entered = true; //SearchConstraint just entered/switched us onto this path
            }
        }

        /// <summary> Raises/lowers a path's manual CanEnterPath prompt for this character exactly once per change. </summary>
        //MWC: Replaces MPath.InsidePathSphere (a single shared flag). Keyed to the specific path so being
        //near one path can't suppress the prompt of another.
        public void SetCanEnter(MPath path, bool value)
        {
            if (value)
            {
                if (m_CanEnterFlag == path) return; //already prompting for this path
                m_CanEnterFlag = path;
                path.CanEnterPath.Invoke(true);
            }
            else
            {
                if (m_CanEnterFlag != path) return; //we weren't prompting for this path
                m_CanEnterFlag = null;
                path.CanEnterPath.Invoke(false);
            }
        }




        public virtual void TryEnterPath()
        {
            if (NextPath)
                EnterPath(NextPath);
        }

        public virtual void TryEnterExitPath()
        {
            if (Path != null && NextPath == null)
            {
                //Do not exit on Middle if the CanExit on Middle is FALSE
                if (!Path.CanExitOnMiddle && !InEndOfThePath && !InStartOfThePath) return;

                ExitPath();
            }
            else
            {
                TryEnterPath();
            }
        }

        public void EnterPath(MPath path)
        {
            //MWC: Safety check. If the MPath has no IPath component it disables itself on enable,
            //but a constraint with the path assigned in the Inspector would still NRE here.
            if (path == null || path.Path == null) return;

            Weight = 0;

            //Debug.Log("ENTERING NEW PATH = " + path);
            if (Path != path)
            {
                //Force Exit Path on the old Path
                if (Path != null)
                {
                    ExitPath(path);
                }

                if (NextPath == path) NextPath = null; //CLEAR NEXT PATH

                Path = path;

                SetCanEnter(path, false); //MWC: lower the manual "can enter" prompt now that we're on it

                //Avoid Entering again into a nearby path to often
                JustEnter = path;
                this.Delay_Action(path.pathCooldown, () => JustEnter = null);

                //MWC: Removed dead 10-step sampling loop. Its result (MinNormalizedPoint) was never used;
                //GetClosestTimeOnPath below already finds the entry point.
                var position = animal.transform.position;

                var FrontPivot = position + (animal.Forward * (ForwardOffset * animal.ScaleFactor));

                PathRootPoint = Path.Path.GetClosestTimeOnPath(position);
                PathFrontPoint = Path.Path.GetClosestTimeOnPath(FrontPivot);

                if (PathRootPoint < 0.1f)
                    Path.EnterFromStart.React(animal);
                else if (PathRootPoint > 0.9f)
                    Path.EnterFromEnd.React(animal);
                else
                    Path.EnterFromMiddle.React(animal);



                if (Path.DisableStates != null)
                {
                    foreach (var st in Path.DisableStates.items)
                        animal.State_Disable(st);
                }

                OnEnterPath.Invoke(Path.gameObject);

                animal.UseCustomRotation = Path.usePathRotation;


                //Play a State on Path enter
                //MWC: skip the force if that state is already active (E.g. RailGrind re-entering a rail
                //after a jump enters the path from the state itself; re-forcing would restart the state)
                if (path.ActivateState != null &&
                    (animal.ActiveStateID == null || animal.ActiveStateID.ID != path.ActivateState.ID))
                    animal.State_Force(path.ActivateState);

                //  animal.State_SetEnterStatus(m_PathRootPoint < m_PathFrontPoint ? 1 : -1); //Use it to see if is going to the end of the line or the start

                //Play the enter reaction
                path.EnterReaction.React(animal);
                path.OnEnterPath.Invoke(this);

                Debugging("Enter");

                StartRotation = animal.Rotation;
                StartPosition = animal.Position;
            }
        }


        private Quaternion StartRotation;
        private Vector3 StartPosition;


        public void MoveOnPath(float weight)
        {
            if (TryExitPath()) return; //Check if we can exit the path


            var scaleFactor = animal.ScaleFactor;

            var position = animal.Position;
            var rotation = animal.Rotation;

            var FrontPivot = position + (animal.Forward * (ForwardOffset * animal.ScaleFactor));

            PathRootPoint = Path.Path.GetClosestTimeOnPath(position);
            PathFrontPoint = Path.Path.GetClosestTimeOnPath(FrontPivot);


            RootPos = Path.Path.GetPointAtTime(PathRootPoint);         // Apply the offset to get the new position Root Position 
            FrontPos = Path.Path.GetPointAtTime(PathFrontPoint);       // Apply the offset to get the new position Front Position

            Quaternion RootRotation = Path.Path.GetPathRotation(PathRootPoint); //Get the Orientation of the Path
            // Quaternion FrontRotation = Path.Path.GetPathRotation(m_PathFrontPoint); //Get the Orientation of the Path


            var PathPosOffset = RootRotation * Path.AlignmentOffset;

            RootPathDirection = RootRotation * Vector3.forward;
            // RootPathDirection = FrontRotation * Vector3.forward;

            Path.PathPosition.SetPositionAndRotation(RootPos, RootRotation); //Transform Reference to store the Position and Rotation

            Vector3 PathDirection = RootPathDirection; //USE THE REAL PATH DIRECTION FROM START TO END

            if (Path.LockRotation) //Find the Correct Path Rotation 
            {
                // PathDirection = RootPathDirection; //USE THE REAL PATH DIRECTION FROM START TO END

                if (Path.FollowDirection == PathFollowDir.None)
                {
                    var Dir = Vector3.Dot(animal.Forward, PathDirection) >= 0;
                    PathDirection *= (Dir ? 1 : -1);
                }
                else if ((Path.FollowDirection == PathFollowDir.Backward))
                { PathDirection *= -1f; }
            }
            else
            {
                PathDirection = (FrontPos - RootPos).normalized; //Get the Animal Relative Path Direction
            }


            var PathNormal = RootRotation * Vector3.up; //Get the Up vector of the Path 


            if (debug)
            {
                //Real Path Direction From Start to End
                MDebug.Draw_Arrow(animal.Position, RootPathDirection, Color.cyan);

                MDebug.DrawWireSphere(RootPos + PathPosOffset, Color.white, 0.1f * scaleFactor);

                MDebug.DrawWireSphere(FrontPos, Color.white, 0.1f * scaleFactor);
                MDebug.DrawWireSphere(RootPos, Color.white, 0.1f * scaleFactor);


                MDebug.Draw_Arrow(RootPos, PathDirection.normalized, Color.green);
                MDebug.Draw_Arrow(RootPos, PathNormal.normalized, Color.blue);
            }

            var PathDirectionNoY = PathDirection;

            //Clean the Path Rotation on Y
            if (Path.IgnoreVertical)
            {
                //PathDirectionNoY = Vector3.ProjectOnPlane(PathDirection, animal.UpVector).normalized;

                var N1 = Vector3.Cross(PathDirection, animal.UpVector);
                PathDirectionNoY = Vector3.ProjectOnPlane(Path.LockRotation ? PathDirection : animal.Move_Direction, N1).normalized;
            }

            //Disable Grounded
            if (Path.IgnoreGrounded)
                animal.Grounded = false;

            var AlignRot = Quaternion.FromToRotation(animal.Forward, PathDirectionNoY) * rotation;  //Calculate the orientation to Terrain 
            var Inverse_Rot = Quaternion.Inverse(rotation);
            var Target = Inverse_Rot * AlignRot;

            //  var Delta = Quaternion.Slerp(Quaternion.identity, Target, OrientSmoothness * animal.DeltaTime); //Calculate the Delta Align Rotation
            var Delta = Quaternion.Slerp(Quaternion.identity, Target, OrientSmoothness * Path.OrientSmoothness * animal.DeltaTime); //Calculate the Delta Align Rotation

            var Dot = Vector3.Dot(animal.Move_Direction, PathDirection);

            //FIRST ORIENTATION ON THE PATH 
            if (weight < 1)
            {
                animal.Rotation = Quaternion.Lerp(StartRotation, AlignRot, weight);

                var TargetPos = (RootPos + PathPosOffset);

                if (Path.IgnoreVertical)
                {
                    TargetPos.y = StartPosition.y; //Orient Correctly when Ignore vertical (HACK WITHOUT NO GRAVITY CHANGE)
                }

                animal.Position = Vector3.Lerp(StartPosition, TargetPos, weight);

                return;
            }

            //Align the Character to the Path Normal
            if (Path.usePathRotation.Value)
            {
                animal.UseCustomRotation = true;
                animal.SlopeNormal = PathNormal;
                AlignRotation(PathNormal, animal.DeltaTime, OrientSmoothness);
            }

            //  Debug.Log("LS");
            //  Debug.DrawRay(animal.Position, PathDirection*5, Color.white);
            //  Debug.DrawRay(animal.Position, N1 * 5, Color.white);

            if (AutoMove.Value)
            {
                animal.MoveFromDirection(PathDirection.normalized);
            }

            //Don't turn around the character if Lock Rotation is enabled
            else if (Path.LockRotation)
            {
                var For = Dot > 0 ? 1 : -1;
                if (Mathf.Abs(Dot) < 0.05f) For = 0;
                var MoveWorldV3 = new Vector3(0, 0, For);

                animal.SetMovementAxis(MoveWorldV3);
                animal.UseAdditiveRot = false;

                animal.Rotation *= Delta;
            }

            else if (Dot > 0) //Make the Animal able to turn 180 degree on the Path
            {
                animal.MoveFromDirection(Path.IgnoreVertical ? PathDirectionNoY : PathDirection);
                animal.Rotation *= Delta;
            }

            //Remove Forward Movement if the Char cannot exit on Start or End
            //MWC: Only block movement heading INTO the blocked end. Before, ALL Z movement was zeroed,
            //which trapped the animal at the end (it could not back away, so ReachEnd never cleared).
            var FacingEnd = PathFrontPoint >= PathRootPoint;
            var TowardEnd = FacingEnd ? animal.MovementAxis.z : -animal.MovementAxis.z;

            if (Path.ReachEnd && !Path.CanExitOnEnd && TowardEnd > 0
                || Path.ReachStart && !Path.CanExitOnStart && TowardEnd < 0)
            {
                animal.MovementAxis.z = 0;
            }

            Vector3 Difference = (RootPos + PathPosOffset) - position; //What need to be substract to the Position of the animal

            if (Path.IgnoreVertical)
            {
                Difference = Vector3.ProjectOnPlane(Difference, animal.UpVector); //IGNORE VERTICAL
            }

            Difference = Vector3.ProjectOnPlane(Difference, Vector3.ProjectOnPlane(PathDirection, animal.UpVector)); //IGNORE Forward

            Vector3 AlignPos = Vector3.Lerp(Vector3.zero, Difference, weight);

            animal.Position += AlignPos;
        }

        public void ExitPath(MPath newPath = null)
        {
            //Enable the Disable States
            if (Path.DisableStates != null)
                foreach (var st in Path.DisableStates.items)
                    animal.State_Enable(st);

            if (Path.LockRotation)
                animal.UseAdditiveRot = true; //Restore the Additive rotation

            if (Path.usePathRotation)
                animal.UseCustomRotation = false; //Disable custom align

            if (newPath == null || newPath && !newPath.NoExitPathReactions)
            {
                Path.ExitReaction.React(animal);

                if (PathRootPoint < 0.1f || PathFrontPoint < 0.1f)
                    Path.ExitFromStart.React(animal);
                else if (PathRootPoint > 0.9f || PathFrontPoint > 0.9f)
                    Path.ExitFromEnd.React(animal);
                else
                    Path.ExitFromMiddle.React(animal);
            }


            OnExitPath.Invoke(Path.gameObject);
            Path.OnExitPath.Invoke(this);
            ExitCoolDown(Path);

            animal.CheckIfGrounded();

            LastPath.Add(Path);
            Path = null;
            Weight = 0;
            NextPath = null;

            Debugging("Exit");
        }

        /// <summary>  Check if the Animal has Arrived to the end or the start of the line </summary>
        public virtual bool TryExitPath()
        {
            if (Path && !Path.IsClosed && !JustEnter && Weight == 1/* && !animal.MovementAxisRaw.CloseToZero()*/)
            {

                bool InEndOfThePath;
                bool InStartOfThePath;


                if (!Path.LockRotation)
                {
                    InEndOfThePath = PathFrontPoint >= EndPathTolerance;
                    InStartOfThePath = PathFrontPoint <= StartPathTolerance;
                }
                else
                {
                    InEndOfThePath = this.InEndOfThePath;
                    InStartOfThePath = this.InStartOfThePath;
                }

                //Set the Events on Start and End Path
                #region End/Start Reached on Path Event

                if (InEndOfThePath && !Path.ReachEnd)
                    Path.SetEndOfPathEvent(true);
                else if (!InEndOfThePath && Path.ReachEnd)
                    Path.SetEndOfPathEvent(false);


                if (InStartOfThePath && !Path.ReachStart)
                    Path.SetStartOfPathEvent(true);
                else if (!InStartOfThePath && Path.ReachStart)
                    Path.SetStartOfPathEvent(false);
                #endregion

                // Debug.Log($"TryExit");

                #region On Lock Rotation = True
                if (Path.LockRotation)
                {
                    if (Path.FollowDirection == PathFollowDir.None)
                    {
                        if (Path.CanExitOnStart && InStartOfThePath)
                        {
                            Debugging("Exit on the Path-Start LockRotation");
                            ExitPath();
                            return true;
                        }

                        if (Path.CanExitOnEnd && InEndOfThePath)
                        {
                            Debugging("Exit on the Path-End LockRotation");
                            ExitPath();
                            return true;
                        }

                    }
                    else
                    {
                        var GoingDirection = Vector3.Dot(RootPathDirection, animal.Move_Direction);

                        if (Path.FollowDirection == PathFollowDir.Forward)
                        {
                            if (Path.CanExitOnStart && PathRootPoint <= StartPathTolerance && GoingDirection <= 0)
                            {
                                Debugging("Exit on the Path-Start LockRotation (FORWARD)");
                                ExitPath();
                                return true;
                            }

                            if (Path.CanExitOnEnd && PathFrontPoint >= EndPathTolerance && GoingDirection >= 0)
                            {
                                Debugging("Exit on the Path-End LockRotation (FORWARD)");
                                ExitPath();
                                return true;
                            }
                        }
                        else
                        {
                            if (Path.CanExitOnStart && PathFrontPoint <= StartPathTolerance && GoingDirection <= 0)
                            {
                                Debugging("Exit on the Path-Start LockRotation (Backwards)");
                                ExitPath();
                                return true;
                            }

                            if (Path.CanExitOnEnd && PathRootPoint >= EndPathTolerance && GoingDirection >= 0)
                            {
                                Debugging("Exit on the Path-End LockRotation (Backwards)");
                                ExitPath();
                                return true;
                            }
                        }
                    }
                    return false;
                }
                #endregion

                #region On Lock Rotation = false
                //NO LOCK ROTATION
                if (Path.CanExitOnStart && PathFrontPoint <= StartPathTolerance)
                {
                    Debugging("Exit Path on Start");
                    ExitPath();
                    return true;
                }
                if (Path.CanExitOnEnd && PathFrontPoint >= EndPathTolerance)
                {
                    Debugging("Exit Path on End");
                    ExitPath();
                    return true;
                }
                #endregion
            }

            return false;
        }

        private void ExitCoolDown(MPath mPath)
        {
            JustExit = mPath;
            this.Delay_Action(mPath.pathCooldown, () =>
            {
                JustExit = null;
                //  LastPath = null;
                LastPath.Remove(mPath); // Clear after cooldown

            });
        }

        /// <summary>Align the Animal to a Custom </summary>
        /// <param name="align">True: Aling to UP, False Align to Terrain</param>
        public virtual void AlignRotation(Vector3 normal, float time, float Smoothness)
        {
            var rot = animal.Rotation;
            //Calculate the orientation to Terrain 
            Quaternion AlignRot = Quaternion.FromToRotation(animal.Up, normal) * animal.Rotation;
            Quaternion Inverse_Rot = Quaternion.Inverse(rot);
            Quaternion Target = Inverse_Rot * AlignRot;

            Quaternion Delta = Quaternion.Lerp(Quaternion.identity, Target, animal.DeltaTime * Smoothness); //Calculate the Delta Align Rotation

            //MWC: Debug rays were drawn unconditionally; gate them behind the debug flag
            if (debug)
            {
                Debug.DrawRay(animal.Position, normal * 5);
                Debug.DrawRay(animal.Position, animal.Up * 5);
            }

            animal.Rotation *= Delta;
        }


        public virtual bool OnAnimatorBehaviourMessage(string message, object value) => this.InvokeWithParams(message, value);



        /// <summary>Editor only console log for the Path Constraint, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void Debugging(string value)
        {
#if UNITY_EDITOR
            if (debug) Debug.Log($"<B>[{animal.name}]</B> → <B>[Path:{(Path != null ? Path.name : "-")}]</B> → <color=yellow>{value}</color>", animal);
#endif
        }

#if MALBERS_DEBUG && UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying && animal && debug)
            {
                if (UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this))
                {
                    Gizmos.color = Color.red;
                    Gizmos.matrix = transform.localToWorldMatrix;
                    Gizmos.DrawWireSphere(Offset, Radius);

                    Gizmos.color = Color.white;
                    Gizmos.DrawWireSphere(Vector3.forward * ForwardOffset, 0.1f);
                    Gizmos.DrawLine(Vector3.zero, Vector3.forward * ForwardOffset);
                }
            }
        }
#endif

        private void Reset()
        {
            animal = this.FindComponent<MAnimal>();
        }
    }


    #region INSPECTOR
#if UNITY_EDITOR
    [CustomEditor(typeof(MPathConstraint))]
    public class MPathConstraintEditor : Editor
    {
        private readonly string[] Tabs = new string[2] { "General", "Events" };

        private SerializedProperty Editor_Tabs1, animal, m_Path, AutoMove, //PathPosition,
            Radius, Offset, ForwardOffset, SearchInterval, OrientSmoothness, AlignSmoothness,


            OnEnterPath, OnExitPath, debug;

        private MPathConstraint M;

        private void OnEnable()
        {
            M = (MPathConstraint)target;

            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");

            OnEnterPath = serializedObject.FindProperty("OnEnterPath");
            OnExitPath = serializedObject.FindProperty("OnExitPath");
            debug = serializedObject.FindProperty("debug");
            animal = serializedObject.FindProperty("animal");
            m_Path = serializedObject.FindProperty("m_Path");

            //   PathPosition = serializedObject.FindProperty("PathPosition");

            Radius = serializedObject.FindProperty("Radius");
            Offset = serializedObject.FindProperty("Offset");
            AutoMove = serializedObject.FindProperty("AutoMove");
            ForwardOffset = serializedObject.FindProperty("ForwardOffset");
            SearchInterval = serializedObject.FindProperty("SearchInterval");
            OrientSmoothness = serializedObject.FindProperty("OrientSmoothness");
            AlignSmoothness = serializedObject.FindProperty("AlignSmoothness");

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            //  base.OnInspectorGUI();


            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs);

            if (Editor_Tabs1.intValue == 0)
                DrawPath();
            else
                DrawEvents();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPath()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(animal);
                    MalbersEditor.DrawDebugIcon(debug);
                }

                EditorGUILayout.PropertyField(m_Path);

                EditorGUILayout.PropertyField(AutoMove);
                // EditorGUILayout.PropertyField(PathPosition);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Radius.isExpanded = MalbersEditor.Foldout(Radius.isExpanded, "Interaction");
                if (Radius.isExpanded)
                {
                    EditorGUILayout.PropertyField(ForwardOffset);
                    EditorGUILayout.PropertyField(Radius);
                    EditorGUILayout.PropertyField(Offset);
                    EditorGUILayout.PropertyField(SearchInterval);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                OrientSmoothness.isExpanded = MalbersEditor.Foldout(OrientSmoothness.isExpanded, "Alignment");
                if (OrientSmoothness.isExpanded)
                {
                    EditorGUILayout.PropertyField(OrientSmoothness);
                    EditorGUILayout.PropertyField(AlignSmoothness);
                }
            }

            if (Application.isPlaying)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {

                        EditorGUILayout.Toggle($"In Path", M.InPath);
                        EditorGUILayout.Toggle($"InEndOfThePath", M.InEndOfThePath);
                        EditorGUILayout.Toggle($"InStartOfThePath", M.InStartOfThePath);


                        EditorGUILayout.LabelField($"Next Path", M.NextPath != null ? M.NextPath.name : "-");
                        EditorGUILayout.FloatField($"Align Weight", M.Weight);
                        EditorGUILayout.FloatField($"Path Front Point", M.PathFrontPoint);
                        EditorGUILayout.FloatField($"Path Root Point", M.PathRootPoint);
                        Repaint();
                    }
                }
            }
        }

        private void DrawEvents()
        {
            EditorGUILayout.PropertyField(OnEnterPath);
            EditorGUILayout.PropertyField(OnExitPath);
        }
    }
#endif
    #endregion

}