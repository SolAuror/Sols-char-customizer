using MalbersAnimations.Scriptables;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;


namespace MalbersAnimations.Controller
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/manimal-controller/states/ledge-grab")]
    [AddTypeMenu("Climb/Ledge Grab")]
    public class LedgeGrab : State
    {
        //public override string StateName => "Ledge Grab";
        public override string StateIDName => "LedgeGrab";

        [Header("Ledge Parameters"), Space]
        [Tooltip("Layer to identify climbable surfaces")]
        public LayerReference LedgeLayer = new(1);
        [Tooltip("Extra filter tags for the ledge")]
        public MTags LedgeTags = new();

        [Tooltip("Climb the Ledge automatically when is near a climbable surface")]
        public BoolReference automatic = new();

        [Tooltip("LedgeGrab will be set automatic if any of these state are playing")]
        [Hide("m_showAutomaticByState")]
        public IDList<StateID> AutomaticByState = new();
        public bool Automatic_By_State { get; private set; }

        [Tooltip("Set the Animal Rigidbody to Kinematic while is on this state. This avoid the colliders to Interfere with ledge.")]
        public BoolReference Kinematic = new(true);

        [Tooltip("Disable the Main Collider while the state is active (Main Collider can Interfere with the animation)")]
        public BoolReference DisableMainCollider = new(true);

        [Tooltip("Correct Distance from the wall to the character")]
        [Min(0)] public float wallDistance = 0.5f;

        [Tooltip("Min Angle between the wall normal and the character forward needed to activate the Ledge Grab")] // MWC: completed truncated tooltip
        public float WallFrontAngle = 15;

        //[Tooltip("Correct Vertical Distance from the wall to the character")]
        // public float VerticalOffset = -1;

        [Tooltip("Min Angle needed to enable the Ledge Grab State")]
        public float MinTerrainAngle = 45f;

        [Tooltip("Distance required to check a wall in front of the character")]
        [Min(0)] public float ForwardLength = 1f;

        //[Tooltip("Length of the Ledge Ray when pointing Down")]
        //[Min(0)] public float DownLength = 1f;

        [Tooltip("Small forward offset to confirm a wall exists below the ledge")] // MWC: was a duplicate of wallDistance tooltip
        [Min(0)] public float WallChecker = 0.1f;

        [Tooltip("Global Min Ledge Depth added to each profile's local value (Effective = Global + Local). 0 = no global depth check.")]
        [Min(0)] public float MinLedgeDepth = 0f; // MWC — global baseline added to every profile's local MinLedgeDepth

        [Tooltip("Global Min Ledge Width added to each profile's local value (Effective = Global + Local). 0 = no global width check.")]
        [Min(0)] public float MinLedgeWidth = 0f; // MWC — global baseline added to every profile's local MinLedgeWidth


        [Tooltip("Transform Created to Store the Hit Position of the Ledge Rays. Use it to show UI When a wall  ")]
        public string HitTransform = "LedgeHit";

        //MWC: gate for the UI-only FindLedge raycasts (see TryActivate). Default OFF — enable only if you show UI with the Hit Transform
        [Tooltip("Update the Ledge Hit Transform (for UI) even when the state is not automatic and no Input is pressed.\n" +
            "This runs the Ledge raycasts every Try cycle — enable it only if you use the Hit Transform for UI or Aiming")]
        public BoolReference UpdateHitTransformUI = new(false);

        public bool AddHitTransformToAim = false; //Add the Hit Transform to the Aim System to be used as a Target for Aiming.

        public List<LedgeProfiles> profiles = new();

        /// <summary>Alignment offset found from the character to the ledge</summary>
        private Vector3 AlignmentOffset;
        private float AngleDifference;

        private Vector3 StartPosition;
        private Quaternion StartRotation;
        private Vector3 TargetPosition;
        private Transform LedgeTransform;
        private Vector3 WallNormal;

        /// <summary> Store the Current Ledge Profile </summary>
        private LedgeProfiles LedgeProfile;
        private RaycastHit FoundLedgeHit;
        private RaycastHit FoundWallHit;

        private bool OrientToWall = false;
        private bool m_ColliderWasDisabled; // MWC: track so ResetStateValues can restore it unconditionally

        /// <summary> Use this with Messages to override the default value of the Climb at runtime   /// </summary>
        public void LedgeAutomatic() => automatic.Value = true;

        public override Vector3 Speed_Direction() => Vector3.zero; //This State does not require a speed


        private Transform m_HitTransform;

        private Aim aim;
        public override void AwakeState()
        {
            base.AwakeState();

            aim = animal.GetComponent<Aim>();

            //Find the Ledge Hit Transform
            m_HitTransform = animal.transform.FindGrandChild(HitTransform);

            if (m_HitTransform == null)
            {
                m_HitTransform = new GameObject(HitTransform).transform;
                m_HitTransform.parent = transform;
                m_HitTransform.ResetLocal();
            }
            EnableHitTransform(false);
        }


        private List<LedgeProfiles> UpdatedProfiles = new(); //Update the 

        public override bool TryActivate()
        {
            if (InClimb && MovementRaw.z < 0) return false; //Means is going down on a climb

            if (automatic || Automatic_By_State || InputValue)
            {
                return FindLedge();
            }
            else
            {
                //Find the ledge when is NOT set to automatic
                //MWC: this branch only feeds the UI hit-transform, but it ran every Try cycle because the
                //HitTransform name is never empty by default. Now gated behind the opt-in UpdateHitTransformUI flag.
                if (UpdateHitTransformUI.Value && !string.IsNullOrEmpty(HitTransform))
                {
                    FindLedge();
                }
            }
            return false;
        }

        public bool FindLedge()
        {
            foreach (var p in UpdatedProfiles)
            {
                if (p.OnlyGrounded && !animal.Grounded) continue; //Check Ray only when the animal is grounded
                if (p.MaxVSpeed != 0 && p.MaxVSpeed > animal.CurrentSpeedModifier.Vertical.Value) continue; //Do not check when the speed does not match
                if (p.LastState != null && p.LastState != animal.ActiveStateID) continue; //Check if the Last State is the same as the current state

                //Check if we are in Vertical Speed Range
                // if (p.MaxVSpeed == 0 || p.MaxVSpeed <= animal.CurrentSpeedModifier.Vertical.Value)
                {
                    var LedgeForwardPoint1 = transform.TransformPoint(new Vector3(0, p.Height, 0)) + DeltaPos;
                    var WallPoint1 = animal.transform.TransformPoint(new Vector3(0, p.Height - p.LedgeExitDistance - WallChecker, 0)) + DeltaPos;

                    var ForwardDistance = ForwardLength * ScaleFactor * p.ForwardMultiplier;
                    var LedgeExitDistance = p.LedgeExitDistance * ScaleFactor;
                    var LedgeDownPoint1 = LedgeForwardPoint1 + (Forward * ForwardDistance);


                    if (animal.debugGizmos)
                    {
                        MDebug.DrawRay(LedgeForwardPoint1, (Forward * ForwardDistance), Color.green);
                        MDebug.DrawRay(WallPoint1, (Forward * ForwardDistance), Color.yellow);
                        MDebug.DrawRay(LedgeDownPoint1, -Up * LedgeExitDistance, Color.red);
                    }

                    if (p.CheckUpwards)
                    {
                        var ChestPoint = animal.transform.TransformPoint(new Vector3(0, animal.Height, 0)) + DeltaPos;

                        if (animal.debugGizmos) MDebug.DrawLine(ChestPoint, LedgeForwardPoint1, Color.gray); // MWC: guard behind debugGizmos flag

                        if (Physics.Linecast(ChestPoint, LedgeForwardPoint1, out _, LedgeLayer.Value, IgnoreTrigger))
                        {
                            if (animal.debugGizmos) MDebug.DrawLine(ChestPoint, LedgeForwardPoint1, Color.red); // MWC: guard behind debugGizmos flag
                            continue;
                        }
                    }

                    var debugDuration = 3f; // MWC: renamed from opaque 'seg'

                    //Cast the first Ray--- to see if there nothing in front of the character
                    //No walls pointing forward 
                    if (Physics.Raycast(LedgeForwardPoint1, Forward, out _, ForwardDistance, LedgeLayer.Value, IgnoreTrigger) == false)
                    {
                        //Check Ledge Pointing Down the Second First Ray
                        if (Physics.Raycast(LedgeDownPoint1, -Up, out FoundLedgeHit, LedgeExitDistance, LedgeLayer.Value, IgnoreTrigger))
                        {
                            if (animal.debugGizmos) MDebug.DrawRay(FoundLedgeHit.point, FoundLedgeHit.normal, Color.cyan, debugDuration); // MWC: guarded

                            var LedgeAngle = Vector3.Angle(FoundLedgeHit.normal, Up);

                            //Do not Grab ledge on a Slope Angle
                            //We need to not find wall 
                            if (LedgeAngle < animal.SlopeLimit)
                            {
                                if (!ValidLedgeSize(p, LedgeDownPoint1)) continue; // MWC — skip ledges too small/thin to actually grab

                                if (Physics.Raycast(WallPoint1, Forward, out FoundWallHit, ForwardDistance, LedgeLayer.Value, IgnoreTrigger))
                                {
                                    //Debug.DrawRay(FoundWallHit.point, FoundWallHit.normal * 2, Color.white, 2);

                                    //
                                    if (LedgeTags.ValidObjects && !LedgeTags.GameObjectHasTag(FoundLedgeHit.transform.gameObject)) return false;


                                    var WallTopAngle = Vector3.Angle(FoundWallHit.normal, Up);

                                    var WallFrontAngle = Vector3.Angle(FoundWallHit.normal, -Forward);

                                    //Debug.Log($"WallFrontAngle {WallFrontAngle}");

                                    if (this.WallFrontAngle < WallFrontAngle) continue; //The wall angle need to be steeper than the slope limit

                                    if (Mathf.Abs(WallTopAngle - LedgeAngle) < MinTerrainAngle) continue; //Hack to avoid grabbing Weird Ledge Angles

                                    var CrossLedgeHit = Vector3.Cross(-FoundLedgeHit.normal, FoundWallHit.normal);
                                    WallNormal = Vector3.Cross(FoundLedgeHit.normal, CrossLedgeHit).normalized;

                                    //Find the Correct Orientation
                                    var Y_Point = MTools.ClosestPointOnPlane(transform.position, WallNormal, FoundLedgeHit.point);

                                    if (animal.debugGizmos) MDebug.DrawWireSphere(Y_Point, Color.green, 0.02f, 2); // MWC: guarded

                                    Y_Point = Y_Point.ClosestPointOnLine(transform.position + (UpVector * 5), transform.position - (UpVector * 5));

                                    if (animal.debugGizmos) MDebug.DrawWireSphere(Y_Point, Color.yellow, 0.02f, 2); // MWC: guarded

                                    //  var CloseEdgePoint = FoundWallHit.collider.ClosestPoint(Y_Point);

                                    var H_Point = MTools.ClosestPointOnPlane(FoundWallHit.point, WallNormal, transform.position);


                                    LedgeProfile = p; //Store the current Ledge Profile


                                    var YAxis = Vector3.Distance(Y_Point, transform.position) + (LedgeProfile.AlignOffset.y * ScaleFactor);

                                    var ZAxis = (wallDistance * ScaleFactor) - Vector3.Distance(H_Point, transform.position)
                                        + (LedgeProfile.AlignOffset.x * ScaleFactor);


                                    var UPDifference = YAxis * FoundLedgeHit.normal;
                                    var HorizontalDifference = ZAxis * WallNormal;

                                    AlignmentOffset = UPDifference + (HorizontalDifference);
                                    AngleDifference = Vector3.SignedAngle(Forward, -WallNormal, Up); //?????


                                    //animal.SetPlatform(FoundWallHit.transform); //We need the Platform for moving grabing ledges    
                                    //CheckKinematic();

                                    StartPosition = Position;
                                    StartRotation = Rotation;

                                    LedgeTransform = FoundLedgeHit.transform;

                                    TargetPosition = StartPosition + (AlignmentOffset);

                                    // Debugging($"Distance Ledge and Start Position: {Vector3.Distance(Y_Point, StartPosition):F3}");

                                    #region Debug
                                    if (animal.debugGizmos) // MWC: guard entire debug region behind debugGizmos flag
                                    {
                                        MDebug.DrawRay(FoundLedgeHit.point, CrossLedgeHit, Color.white, debugDuration);
                                        MDebug.DrawRay(FoundLedgeHit.point, WallNormal * 5, Color.green, debugDuration);

                                        MDebug.DrawWireSphere(Y_Point, Color.red, 0.1f, debugDuration);
                                        MDebug.DrawWireSphere(FoundLedgeHit.point, Color.yellow, 0.1f, debugDuration);
                                        MDebug.DrawWireSphere(H_Point, Color.red, 0.1f, debugDuration);
                                        MDebug.DrawWireSphere(transform.position, Color.yellow, 0.1f, debugDuration);

                                        MDebug.DrawLine(Y_Point, FoundLedgeHit.point, Color.yellow, debugDuration);
                                        MDebug.DrawLine(H_Point, transform.position, Color.red, debugDuration);
                                    }
                                    #endregion

                                    TargetPosition = LedgeTransform.InverseTransformPoint(TargetPosition); //Convert the Target Position to Local of the Ledge Transform to avoid problems when the ledge is moving
                                    StartPosition = LedgeTransform.InverseTransformPoint(StartPosition); //Convert the Start Position to Local of the Ledge Transform to avoid problems when the ledge is moving

                                    //  WallNormal = FoundWallHit.normal;
                                    OrientToWall = p.Orient;

                                    m_HitTransform.position = Y_Point;

                                    EnableHitTransform(true);

                                    // Debug.Log($"LedgeTransform : [{LedgeTransform}]");

                                    Debugging($"Try [Ledge-Grab] Wall and Ledge found. <B><color=green>[{p.name}]</color></B>. Wall-Hit Difference: [{HorizontalDifference}]");
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            EnableHitTransform(false);
            return false;
        }

        // MWC — footprint validation: ledge top must have enough depth (behind the lip) and width (to the sides)
        private bool ValidLedgeSize(LedgeProfiles p, Vector3 frontDownOrigin)
        {
            var castLen = p.LedgeExitDistance * ScaleFactor;

            var depth = MinLedgeDepth + p.MinLedgeDepth; // MWC — global baseline + per-profile local
            var width = MinLedgeWidth + p.MinLedgeWidth; // MWC — global baseline + per-profile local

            if (depth > 0) // depth: sample deeper into the ledge (away from character)
            {
                var depthDist = depth * ScaleFactor;

                var depthOrigin = frontDownOrigin + Forward * depthDist;
                if (!SampleLedgeTop(depthOrigin, castLen)) return false; // floor must extend this deep

                // MWC — clearance: continue the green ray across the standing zone. A wall in it (e.g. a
                // second stacked box) means there's no room to stand even though the floor is deep enough.
                if (Physics.Raycast(frontDownOrigin, Forward, out var blocked, depthDist, LedgeLayer.Value, IgnoreTrigger))
                {
                    if (animal.debugGizmos) MDebug.DrawRay(frontDownOrigin, Forward * blocked.distance, Color.red, 2f);
                    return false;
                }
            }

            if (width > 0) // width: sample to the left and right along the ledge edge
            {
                var side = animal.transform.right * (width * 0.5f * ScaleFactor);
                if (!SampleLedgeTop(frontDownOrigin + side, castLen)) return false;
                if (!SampleLedgeTop(frontDownOrigin - side, castLen)) return false;
            }
            return true;
        }

        // MWC — a downward probe that must hit a same-band flat top (angle < SlopeLimit) within the cast length
        private bool SampleLedgeTop(Vector3 origin, float castLen)
        {
            if (Physics.Raycast(origin, -Up, out var hit, castLen, LedgeLayer.Value, IgnoreTrigger)
                && Vector3.Angle(hit.normal, Up) < animal.SlopeLimit)
            {
                if (animal.debugGizmos) MDebug.DrawRay(hit.point, hit.normal, Color.green, 2f);
                return true;
            }
            if (animal.debugGizmos) MDebug.DrawRay(origin, -Up * castLen, Color.magenta, 2f);
            return false;
        }


        public override void Activate(int StateStatus)
        {
            LedgeProfile ??= profiles.Find(x => x.EnterStatus == StateStatus); //Find the profile with the same Enter Status
            Activate();
        }

        public override void Activate()
        {
            base.Activate();

            // MWC: guard empty list — profiles[0] would throw IndexOutOfRangeException
            if (LedgeProfile == null && profiles.Count > 0)
                LedgeProfile = profiles[0];

            if (LedgeProfile == null)
            {
                Debugging("[LedgeGrab] No profile found — state cannot activate.");
                return;
            }

            SetEnterStatus(LedgeProfile.EnterStatus);
            animal.Reset_Movement(); //Remove all Input stuff
            animal.Force_Remove(); //Remove all forces when grabbing a ledge

            EnableHitTransform(false);
            animal.InertiaPositionSpeed = Vector3.zero; //Remove inertia
            animal.AdditivePosition = Vector3.zero; //Remove additive
            CheckKinematic();
            if (FoundLedgeHit.transform != null) // MWC: guard — default RaycastHit has null transform when Activate(int) is called externally
                animal.SetPlatform(FoundLedgeHit.transform);

            // StartPosition = transform.InverseTransformPoint(StartPosition);

            UpdateProfileLastState();
        }

        private void CheckKinematic()
        {
            animal.InertiaPositionSpeed = Vector3.zero;         //Remove inertia
            animal.DeltaPos = Vector3.zero;                     //Remove Delta position
            animal.DeltaRootMotion = Vector3.zero;              //Remove Delta position

            if (Kinematic.Value)
            {
                animal.RB.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                animal.RB.isKinematic = true;
            }

            // MWC: track flag so ResetStateValues can restore collider regardless of ActiveState at that point
            m_ColliderWasDisabled = DisableMainCollider.Value;
            if (m_ColliderWasDisabled)
                animal.MainCollider_Enable(false);
        }

        private bool InTransition;
        private bool ExitTransition;
        private bool InClimb;

        public override void OnStateMove(float deltatime)
        {
            if (InCoreAnimation)
            {
                InTransition = false;

                if (Anim.IsInTransition(0) && !ExitTransition)
                {
                    var TransTime = Anim.GetAnimatorTransitionInfo(0).normalizedTime;
                    animal.Reset_Movement();
                    animal.AdditivePosition = Vector3.zero; //Remove additive

                    Quaternion AlignRot = Quaternion.FromToRotation(Forward, -WallNormal) * Rotation;  //Calculate the orientation to Terrain

                    if (LedgeTransform != null) // MWC: guard — LedgeTransform is null when state is entered externally without FindLedge()
                    {
                        // MWC: DeltaPlatformPos is world-space; positions are local-space, so convert before subtracting
                        var localPlatformDelta = LedgeTransform.InverseTransformVector(animal.DeltaPlatformPos * deltatime);
                        StartPosition -= localPlatformDelta;
                        TargetPosition -= localPlatformDelta;
                    }

                    Position = LedgeTransform != null
                        ? Vector3.Lerp(LedgeTransform.TransformPoint(StartPosition), LedgeTransform.TransformPoint(TargetPosition), TransTime)
                        : Vector3.Lerp(StartPosition, TargetPosition, TransTime);

                    //Orient to wall 
                    if (OrientToWall)
                        Rotation = Quaternion.Lerp(StartRotation, AlignRot, TransTime);
                    InTransition = true;

                    return;
                }


                if (!InTransition && !ExitTransition && IsActiveState)
                {
                    //Position = LedgeTransform.TransformPoint(TargetPosition); //Set the position to the target position to avoid weird movement when exiting the transition
                    ExitTransition = true;
                    animal.Reset_Movement();

                    //animal.Position -= (animal.DeltaPlatformPos); //Add the Delta Position of the platform to the character position to avoid weird movement when the platform is moving during the transition
                }

                animal.InertiaPositionSpeed = Vector3.zero; //Remove inertia

                if (LedgeProfile != null)
                {
                    if (LedgeProfile.Orient)
                    {
                        float DeltaAngle = Mathf.Lerp(0, AngleDifference, deltatime * LedgeProfile.OrientSmoothness * 2f);
                        AngleDifference -= DeltaAngle;
                        animal.transform.rotation *= Quaternion.Euler(0, DeltaAngle, 0);
                    }

                    if (LedgeProfile.AdditivePosition)
                    {
                        var time = animal.AnimState.normalizedTime;
                        animal.AdditivePosition += deltatime * LedgeProfile.HeightCurve.Evaluate(time) * LedgeProfile.HeightSpeed * Up;
                        animal.AdditivePosition += deltatime * LedgeProfile.ForwardCurve.Evaluate(time) * LedgeProfile.ForwardSpeed * Forward;
                    }
                }

                if (LedgeProfile != null && animal.AnimState.normalizedTime > LedgeProfile.ExitTime) // MWC: null guard
                {
                    IsPersistent = false;
                }
            }
        }

        public override void TryExitState(float DeltaTime)
        {
            if (LedgeProfile != null && animal.AnimState.normalizedTime > LedgeProfile.ExitTime) // MWC: null guard
            {
                Debugging($"Allow Exit - {LedgeProfile.name} After Exit Time {animal.AnimState.normalizedTime:F3} > {LedgeProfile.ExitTime}");
                AllowExit(1);
                animal.Grounded = true;
                //animal.CheckIfGrounded();
            }
        }

        public override void NewActiveState(StateID newState)
        {
            UpdateProfileLastState();

            Automatic_By_State = false;

            if (AutomaticByState.Count > 0)
            {
                Automatic_By_State = AutomaticByState.Contains(newState);
            }

            InClimb = newState == StateEnum.Climb;
        }

        private void UpdateProfileLastState()
        {
            UpdatedProfiles = profiles;
            var FilterLastState = profiles.FindAll(p => p.LastState != null && p.LastState == animal.ActiveStateID);
            if (FilterLastState.Count != 0) UpdatedProfiles = FilterLastState; // MWC: FindAll never returns null, removed redundant null check
        }


        /// <summary> Using this to show if a surface can be climb  </summary>
        private void EnableHitTransform(bool v)
        {
            m_HitTransform.gameObject.SetActive(v);

            if (AddHitTransformToAim && aim != null) // MWC: guard null — Aim component may not be present
            {
                if (v)
                    aim.SetTargetTemp(m_HitTransform);
                else
                    aim.ClearTargetTemp();
            }
        }

        public override void ResetStateValues()
        {
            UpdatedProfiles = profiles;
            LedgeProfile = null;
            InTransition = false;
            ExitTransition = false;
            OrientToWall = false;
            AngleDifference = 0;

            StartPosition =
            TargetPosition =
            WallNormal =
            AlignmentOffset = Vector3.zero;

            StartRotation = Quaternion.identity;

            FoundLedgeHit = new RaycastHit();
            FoundWallHit = new RaycastHit();

            if (Kinematic.Value && animal)
                animal.RB.isKinematic = false;

            LedgeTransform = null;

            //Hide the Hit Transform
            if (m_HitTransform)
            {
                EnableHitTransform(false);
                m_HitTransform.ResetLocal();
            }

            // MWC: use tracked flag — by reset time ActiveState has already changed so the old check always failed
            if (m_ColliderWasDisabled && animal)
            {
                animal.MainCollider_Enable(true);
                m_ColliderWasDisabled = false;
            }
        }

        //Remove After some updates... this is only for debugging purposes to avoid the list to be null when changing the scriptable profiles
        [Tooltip("LedgeGrab will be set automatic if any of these state are playing")]
        [HideInInspector] public List<StateID> automaticByState = new();



#if UNITY_EDITOR
        public override void SetSpeedSets(MAnimal animal)
        {
            //Do nothing... the Ledge Grab does not require a Speed Set
        }

        private void OnValidate()
        {
            IDList<StateID>.MigrateIDList(automaticByState, AutomaticByState); // MWC: migrate legacy List to new IDList, removes need for custom editor code to handle this    

            m_showAutomaticByState = !automatic.Value;
        }


        [HideInInspector] public bool m_showAutomaticByState;

        public override void StateGizmos(MAnimal animal)
        {
            if (Application.isPlaying) return;

            foreach (var p in profiles)
            {
                if (!p.debug) continue; // MWC — skip gizmos for profiles with debug disabled

                var point1 = animal.transform.TransformPoint(new Vector3(0, p.Height, 0));
                var pointWall1 = animal.transform.TransformPoint(new Vector3(0, p.Height - p.LedgeExitDistance - WallChecker, 0));

                var scale = animal.ScaleFactor;

                var dir = ForwardLength * p.ForwardMultiplier * scale * animal.Forward;
                var dirWall = scale * wallDistance * animal.Forward;
                var point2 = point1 + dir;
                var downExit = p.LedgeExitDistance * scale * -animal.Up;


                if (p.CheckUpwards)
                {
                    Gizmos.color = Color.gray;
                    Gizmos.DrawLine(point1, animal.transform.TransformPoint(new Vector3(0, animal.Height, 0)));
                }


                Gizmos.color = Color.green;

                Gizmos.DrawRay(point1, dir);
                // Gizmos.DrawRay(point2, downDir);
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(pointWall1, dir);
                Gizmos.color = Color.red;
                Gizmos.DrawRay(pointWall1, dirWall);
                Gizmos.DrawRay(point2, downExit); // MWC: removed duplicate Color.red assignment

                var gizmoDepth = MinLedgeDepth + p.MinLedgeDepth; // MWC — global baseline + per-profile local
                var gizmoWidth = MinLedgeWidth + p.MinLedgeWidth; // MWC — global baseline + per-profile local

                if (gizmoDepth > 0) // MWC — visualize depth probe (down floor + forward clearance)
                {
                    var gizmoDepthDist = gizmoDepth * scale;
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawRay(point2 + animal.Forward * gizmoDepthDist, downExit); // floor probe
                    Gizmos.color = Color.green;
                    Gizmos.DrawRay(point2, animal.Forward * gizmoDepthDist); // clearance probe (extends the green ray)
                }
                if (gizmoWidth > 0) // MWC — visualize width probes
                {
                    Gizmos.color = new Color(1f, 0.5f, 0f);
                    var side = animal.transform.right * (gizmoWidth * 0.5f * scale);
                    Gizmos.DrawRay(point2 + side, downExit);
                    Gizmos.DrawRay(point2 - side, downExit);
                }
            }

        }
        internal override void Reset()
        {
            base.Reset();

            automatic.Value = true;

            General = new AnimalModifier()
            {
                modify = (modifier)(-1),
                RootMotion = true,
                AdditivePosition = true,
                AdditiveRotation = false,
                Grounded = false,
                Sprint = true,
                OrientToGround = false,
                Gravity = false,
                CustomRotation = false,
                FreeMovement = false,
                IgnoreLowerStates = true,
            };

            profiles = new List<LedgeProfiles>();

            var prof = new LedgeProfiles();


            AutomaticByState = new()
            {
                items = new List<StateID>
                {
                   MTools.GetInstance<StateID>("Climb"),
                   MTools.GetInstance<StateID>("Jump"),
                   MTools.GetInstance<StateID>("Fall"),
                   MTools.GetInstance<StateID>("Swim"),
                },
            };


            profiles.Add(prof);

            Input = "Jump";
        }
#endif
    }
    [System.Serializable]
    public class LedgeProfiles
    {
        public string name = "Ledge Grab";

        [Tooltip("Draw this profile's detection ray gizmos in the Scene view")]
        public bool debug = true; // MWC — per-profile gizmo toggle to avoid cluttering the scene with every profile's rays

        [Tooltip("State Enter Status to Activate while")]
        public int EnterStatus = 0;

        [Tooltip("Max Vertical Speed Needed to Check this Profile")]
        public float MaxVSpeed = 0;

        [Tooltip("Check the Last State as a condition to activate the profile")]
        public StateID LastState;

        [Tooltip("Cast a Ray Upwards to check if there's a roof blocking the ledge")]
        public bool CheckUpwards = false;

        [Tooltip("The Ledge will be check only if the character is grounded")]
        public bool OnlyGrounded = false;

        [Tooltip("Forward Length Multiplier applied to the Global Length")]
        public float ForwardMultiplier = 1;

        [Tooltip("Height Offset to cast the Ray for checking a ledge")]
        [Min(0)] public float Height = 1.5f;

        [Tooltip("Ray to check if we have found a ledge")]
        [Min(0)] public float LedgeExitDistance = 0.25f;

        [Tooltip("Minimum depth the ledge top must extend behind the front edge to be grabbable. 0 = no depth check.")]
        [Min(0)] public float MinLedgeDepth = 0f; // MWC — reject small blocks / thin poles (shallow forward depth)

        [Tooltip("Minimum sideways width the ledge top must have to be grabbable. 0 = no width check.")]
        [Min(0)] public float MinLedgeWidth = 0f; // MWC — reject thin poles/spikes (narrow sideways)

        [Tooltip("If the Animation Normalized Time of this state (Ledge Grab) is greater Exit Animation time,\n" +
            " the State will Allow Exit()... so other states can try activate themselves.")]
        [Range(0, 1)] public float ExitTime = 0.9f;

        [Tooltip("Horizontal(X) and Vertical(Y) values needed to apply offset movement to have better alignment with the Ledge")]
        [FormerlySerializedAs("AlingOffset")] // MWC: fixed typo, keep backwards compat with existing .asset files
        public Vector2 AlignOffset;


        [Tooltip("Align the character to the Wall's normal direction")]
        public bool Orient = true;

        [Tooltip("Smoothness value to align the character to the wall")]
        [Hide("Orient", false)]
        [Min(0)] public float OrientSmoothness = 10f;

        public bool AdditivePosition = false;

        [Hide("AdditivePosition", false)]
        [Min(0)] public float HeightSpeed = 0.5f;
        [Hide("AdditivePosition", false)]
        [Min(0)] public float ForwardSpeed = 0.5f;

        [Hide("AdditivePosition", false)]
        public AnimationCurve HeightCurve = new(
               new Keyframe(0, 1), new Keyframe(0.45f, 1), new Keyframe(0.55f, 0f), new Keyframe(1, 0f)
            );

        [Hide("AdditivePosition", false)]
        public AnimationCurve ForwardCurve = new(
              new Keyframe(0, 0), new Keyframe(0.45f, 0), new Keyframe(0.55f, 1f), new Keyframe(1, 1f)
           );
    }
}