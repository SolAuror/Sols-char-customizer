// Last Change: 9/24/2026 1:26pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/manimal-controller/states/fall")]
    [AddTypeMenu("Air/Fall")]
    public class Fall : State
    {
        //TODO: DO Fall Rotator
        // public override string StateName => "Fall";
        public override string StateIDName => "Fall";
        public enum FallBlending { DistanceNormalized, Distance, VerticalVelocity }

        /// <summary>Air Resistance while falling</summary>
        [Tooltip("Can the Animal be controller while falling?")]
        public BoolReference AirControl = new(true);
        [Tooltip("Rotation while falling")]
        public FloatReference AirRotation = new(10);
        [Tooltip("Maximum Movement while falling")]
        public FloatReference AirMovement = new(0);
        [Tooltip("Lerp value for the Air Movement adjustment")]
        public FloatReference AirSmooth = new(2);

        [Space]

        [Tooltip("Forward Offset Position of the Fall Ray")]
        public FloatReference Offset = new();

        [Tooltip("Forward Offset Multiplier Position of the Fall Ray while moving")]
        public FloatReference MoveMultiplier = new(0.1f);

        [Hide("ShowFront")]
        [Tooltip("A ray will be cast in front of the animal to check if there's an obstacle in front of it")]
        public bool CheckFrontObstacle = true;

        [Tooltip("Multiplier for the Fall Ray Length. The Default Value is the Animal's Height")]
        public FloatReference lengthMultiplier = new(1f);

        [Tooltip("RayHits Allowed on the Raycast NonAlloc (Try Fall Logic)")]
        public IntReference rayHits = new(6);

        [Space, Tooltip("State Float Value in the animator. This is used to blend between different Fall Animations")]
        public FallBlending BlendFall = FallBlending.DistanceNormalized;

        [Tooltip("Used to Set fallBlend to zero before reaching the ground")]
        public FloatReference LowerBlendDistance;

        /// <summary>Distance to Apply a Fall Hard Animation</summary>
        [Space, Header("Fall Damage")]
        public StatID AffectStat;

        [Tooltip("Minimum Distance to Apply Fall Damage, If the fall distance is lesser than this value, no damage will be applied")]
        public FloatReference FallMinDistance = new(5f);

        [Tooltip("Maximum Distance to Apply Fall Damage, If the fall distance is greater  than this value,the animal will die")]
        public FloatReference FallMaxDistance = new(15f);

        [Tooltip("The Fall State will set the Exit State Status Depending the Fall Distance (X: Distance Y:Exit Status Value)")]
#if UNITY_2020_3_OR_NEWER
        [NonReorderable]
#endif
        public Vector2[] landStatus;

        [Tooltip("Fix the animal when is stuck on weird places (Experimental)")]
        public bool StuckAnimal = true;

        [Tooltip("When Falling, the animal may get stuck falling. The animal will be force to move forward.")]
        public FloatReference PushForward = new(2);



        [Space, Header("Experimental")]
        [Tooltip("Keep the Inertia from the platform when falling from it. EXPERIMENTAL")]
        public bool KeepInertiaOnPlatforms = true;




        /// <summary>Stores the max height before going Down</summary>
        public float MaxHeight { get; set; }

        /// <summary>Accumulated Fall Distance</summary>
        public float FallCurrentDistance { get; set; }

        /// <summary>
        ///  
        /// </summary>
        public Vector3 PlatFormInertia { get; set; }
        public Transform LastPlatform { get; set; }


        private RaycastHit[] FallHits;
        private RaycastHit FallRayCast;

        private GameObject GameObjectHit;
        private bool IsDebree;

        /// <summary>While Falling this is the distance to the ground</summary>
        private float DistanceToGround;

        /// <summary> Normalized Value of the Height </summary>
        float Fall_Float;

        private MSpeed FallSpeed = MSpeed.Default;

        public Vector3 FallPoint { get; private set; }

        /// <summary> UP Impulse was going UP </summary>

        private bool GoingDown;
        private int Hits;

        /// <summary>The last landing was found by the Hip ray. Fall waits until the animal aligns to that ground</summary>
        private bool WaitHipAlignment;
        /// <summary>Time of the last Hip landing (safety timeout for WaitHipAlignment)</summary>
        private float HipLandTime;
        /// <summary>Maximum time Fall waits for the ground alignment after a Hip landing</summary>
        private const float HipAlignMaxWait = 1f;
        /// <summary>Cosine of 5°: the animal Up and the Hip ground normal closer than this are considered aligned</summary>
        private const float HipAlignedCos = 0.9961947f;

        public override void AwakeState()
        {
            base.AwakeState();
            animalStats = animal.FindComponent<Stats>(); //Find the Stats

            FallHits = new RaycastHit[rayHits];//set the hits
        }



        public override bool TryActivate()
        {
            if (WaitHipAlignment)
            {
                if (HipStillAligning()) return false;
                WaitHipAlignment = false;
            }

            var MoveMultiplier = animal.Grounded ? this.MoveMultiplier : 0; //If the animal is not grounded, do not add the forward offset

            float SprintMultiplier = (animal.VerticalSmooth);
            var fall_Pivot = animal.Main_Pivot_Point + (Offset * ScaleFactor * animal.Forward) +
                (MoveMultiplier * ScaleFactor * SprintMultiplier * animal.Forward); //Calculate ahead the falling ray

            //fall_Pivot += animal.DeltaPos; //Check for the Next Frame (Does not work now with


            //Check Front 
            if (CheckFrontObstacle && MoveMultiplier > 0)
            {
                if (GizmoDebug)
                {
                    MDebug.DrawLine(animal.Main_Pivot_Point, fall_Pivot, Color.magenta);
                }
                if (Physics.Linecast(animal.Main_Pivot_Point, fall_Pivot, GroundLayer, IgnoreTrigger)) return false;
            }


            float Multiplier = animal.Pivot_Multiplier * lengthMultiplier * 0.999f * ScaleFactor - (animal.RayCastRadius * 0.5f);
            return TryFallRayCasting(fall_Pivot, Multiplier);
        }

        private bool TryFallRayCasting(Vector3 fall_Pivot, float Multiplier)
        {
            // var Direction = animal.TerrainSlope > 0 ? Gravity : -transform.up;
            var Direction = Gravity;
            // var Direction =   -transform.up;

            //if (!animal.FrontRay && !animal.MainRay)
            //{
            //    Debugging($"[Try Failed] Missing Rays FrontRay: {animal.FrontRay} MainRay: {animal.MainRay}");
            //    return true; //Rare case when the animal is not grounded but fall ray is playing (BUG CAUSING OTHER ISSUES)
            //}

            var Radius = animal.RayCastRadius * ScaleFactor;
            Hits = Physics.SphereCastNonAlloc(fall_Pivot, Radius, Direction, FallHits, Multiplier, GroundLayer, IgnoreTrigger);

            if (GizmoDebug)
            {
                var Dir = Direction * Multiplier;
                MDebug.DrawRay(fall_Pivot, Dir, Color.magenta); // removed duplicate black ray — was immediately overdrawn
                MDebug.DrawRay(FallRayCast.point, 0.2f * ScaleFactor * FallRayCast.normal, Color.magenta);
                // MDebug.DrawWireSphere(fall_Pivot + Dir - (Dir.normalized * Radius), Color.magenta, Radius);
                MDebug.DrawCapsule(fall_Pivot, fall_Pivot + Dir - (Dir * Radius), Radius, Color.magenta);
            }

            // Debugging($"[Try] Hits {Hits} - Direction {Direction} - Multiplier {Multiplier} - Radius {Radius}");

            if (Hits > 0)
            {
                if (animal.Grounded) //Check when its grounded
                {
                    for (int i = 0; i < Hits; i++)
                    {
                        var hit = FallHits[i];
                        float TerrainSlope = Vector3.SignedAngle(hit.normal, animal.UpVector, animal.Right);
                        MDebug.DrawWireSphere(fall_Pivot + Direction * DistanceToGround, Color.magenta, Radius);
                        FallRayCast = hit;

                        if (TerrainSlope > -animal.SlopeLimit) //Check for the first Good Fall Ray that does not break the Fall.
                            break;
                    }

                    if (FallRayCast.transform.gameObject != GameObjectHit) //Check if what the Fall Ray Hit was a Debree
                    {
                        GameObjectHit = FallRayCast.transform.gameObject;
                        IsDebree = GameObjectHit.CompareTag(animal.DebrisTag);
                    }
                }
                else   //If the Animal is in the air  NOT GROUNDED
                {

                    FallRayCast = FallHits[0];

                    DistanceToGround = FallRayCast.distance;

                    float FallSlope = Vector3.Angle(FallRayCast.normal, animal.UpVector);

                    if (FallSlope > animal.SlopeLimit)
                    {
                        Debugging($"[Try] The Animal is on the Air and the angle SLOPE of the ground hit is too Deep [{FallSlope}].  " +
                            $"- [{FallRayCast.transform.name}]");

                        return true;
                    }

                    // Debug.Log($"DistanceToGround {DistanceToGround} : Height {Height}");

                    if (Height >= DistanceToGround) //If the distance to ground is very small means that we are very close to the ground
                    {
                        if (animal.ExternalForce != Vector3.zero) return true; //Hack for external forces

                        var isGrounded = animal.CheckIfGrounded(); // returns true and sets animal.Grounded as a side-effect

                        Debugging($"[Try Failed] Distance to the ground is very small. Checking if we are grounded [{isGrounded}]");
                        if (isGrounded) // use the return value directly — consistent with the debug log above
                        {
                            animal.Grounded = true; //Force Grounded
                            animal.UseGravity = false;

                            animal.AlignPosLerpDelta = animal.AlignPosLerp * 5;

                            var GroundedPos = Vector3.Project(FallRayCast.point - animal.transform.position, Gravity);
                            animal.Teleport_Internal(animal.transform.position + GroundedPos);

                            animal.ResetUPVector(); //IMPORTANT!
                            animal.hit_Hip.distance = Height;
                            //This is for Helping on Slopes
                            //  animal.InertiaPositionSpeed = Vector3.ProjectOnPlane(animal.RB.velocity * animal.DeltaTime, animal.UpVector); 

                        }
                        return false;
                    }
                }
            }
            else
            {
                // MDebug.DrawWireSphere(fall_Pivot, 0.2f, Color.magenta, 2f);
                Debugging($"[Try] There's no Ground beneath the Animal");
                // Debug.Break();
                return true;
            }

            //Debug.Log("fa");
            return false;
        }

        public bool WasRootMotionLastFrame;

        public override void Activate()
        {
            KeepForwardMovement = !AirControl.Value;
            WasRootMotionLastFrame = animal.RootMotion;

            base.Activate();

            StartingSpeedDirection = animal.Inertia;

            // Debug.Log($"<color=green>StartingSpeedDirection: {StartingSpeedDirection} >> animal.HorizontalVelocity {animal.HorizontalVelocity} </color>");

            if (animal.LastState.ID == StateEnum.Jump || animal.LastState.ID.ID <= 2)
            {
                StartingSpeedDirection = animal.HorizontalVelocity; //Clean from JUMP
                KeepForwardMovement = animal.LastState.KeepForwardMovement;
            }

            if (animal.LastState.ID == StateEnum.Climb)
            {
                StartingSpeedDirection = animal.HorizontalVelocity; //Clean from JUMP
                KeepForwardMovement = true;
            }

            ResetStateValues();
            Fall_Float = animal.State_Float;

            if (LastPlatform) animal.Last_Platform_Pos = LastPlatform.position;

            CheckPlatFormMovement();
        }

        public override void EnterCoreAnimation()
        {
            SetEnterStatus(0);
            IgnoreLowerStates = false;

            var Speed = (animal.HorizontalSpeed) / ScaleFactor;                       //Remove the scaleFactor since it will be added later 

            if (animal.HasExternalForce)
            {
                var HorizontalForce = Vector3.ProjectOnPlane(animal.ExternalForce, animal.UpVector);    //Remove Horizontal Force
                var HorizontalInertia = Vector3.ProjectOnPlane(animal.Inertia, animal.UpVector);        //Remove Horizontal Force

                //Remove the Horizontal FORCE SPEED
                var HorizontalSpeed = HorizontalInertia - HorizontalForce;
                Speed = HorizontalSpeed.magnitude / ScaleFactor; //Remove the scaleFactor since it will be added later 
            }

            //Remove all Speed if the External Force does not allows it
            if (!animal.ExternalForceAirControl) Speed = 0;

            animal.DeltaRootMotion = Vector3.zero;      //Reset the Delta Root Motion now we are using the Fall Speed instead

            FallSpeed = new MSpeed(animal.CurrentSpeedModifier)
            {
                name = "FallSpeed",
                position = Speed,
                strafeSpeed = Speed,
                animator = 1,
                rotation = AirRotation.Value,
                lerpPosition = AirSmooth.Value,
                lerpStrafe = AirSmooth.Value,
                lerpAnimator = 8
            };

            animal.SetCustomSpeed(FallSpeed, true);

            CanExit = true; //FORCE CAN EXIT IF WE ARE ALREADY ON THE ANIMATION Is this working or not???

            //Disable the Gravity if we are on an external Force (Wind, Spring) //IMPORTANT
            if (animal.HasExternalForce && animal.InZone) animal.UseGravity = false;

            var PlatformDelta = KeepInertiaOnPlatforms && LastPlatform != null ? (LastPlatform.position - animal.Last_Platform_Pos) / animal.DeltaTime : Vector3.zero;
            animal.ResetInertiaSpeed((animal.HorizontalVelocity - PlatformDelta) * animal.DeltaTime);


            if (WasRootMotionLastFrame)
                animal.UpInertia_Store();

            if (KeepInertiaOnPlatforms && LastPlatform != null)
            {
                var PlatformDelta1 = LastPlatform.position - animal.Last_Platform_Pos;
                animal.Position += PlatformDelta1;
                animal.Last_Platform_Pos = LastPlatform.position;
            }
        }

        public override Vector3 Speed_Direction()
        {
            if (GizmoDebug)
                MDebug.Draw_Arrow(transform.position, StartingSpeedDirection, Color.magenta);

            if (KeepForwardMovement)
            {
                return StartingSpeedDirection;
            }
            else
            {
                return (base.Speed_Direction());
            }
        }

        Vector3 StartingSpeedDirection;
        private Stats animalStats;

        public override void OnStateMove(float deltaTime)
        {
            if (InCoreAnimation)
            {
                if (animal.InZone && animal.HasExternalForce) animal.GravityTime = 0; //Reset the gravity when the animal is on a Force Zone.

                if (!KeepForwardMovement && AirMovement > 0 && AirMovement > CurrentSpeedPos)
                {
                    if (!animal.ExternalForceAirControl) return;

                    CurrentSpeedPos = Mathf.Lerp(CurrentSpeedPos, AirMovement, (AirSmooth != 0 ? (deltaTime * AirSmooth) : 1));
                }
                ////Keep the Up Momentum
                animal.UpInertia_Apply();

            }
            CheckPlatFormMovement();
        }

        private Vector3 PlatformDelta { get; set; }

        private void CheckPlatFormMovement()
        {
            if (KeepInertiaOnPlatforms)

                if (LastPlatform != null && LastPlatform == FallRayCast.transform)
                {
                    PlatformDelta = LastPlatform.position - animal.Last_Platform_Pos;
                    animal.Position += PlatformDelta;
                    animal.Last_Platform_Pos = LastPlatform.position;
                }
                else
                {
                    animal.Position += PlatformDelta;
                }
        }

        public override void TryExitState(float DeltaTime)
        {
            var Radius = animal.RayCastRadius * ScaleFactor;

            float SprintMultiplier = (animal.VerticalSmooth);
            // Renamed from FallPoint to fallPivot — the old name shadowed the public FallPoint property,
            // causing it to always return Vector3.zero to external callers.
            FallPoint = animal.Main_Pivot_Point + (Offset * ScaleFactor * animal.Forward) +
               (animal.Forward * (SprintMultiplier * MoveMultiplier * ScaleFactor)); // look-ahead falling ray

            //Check for the Next Frame (Does not work now with External Forces, but it does with the normal movement) — consider revisiting this logic when implementing external forces, as it may be desirable to include DeltaPos in that case as well
            FallPoint += animal.DeltaPos;

            float DeltaDistance = 0;

            GoingDown = Vector3.Dot(DeltaPos, Gravity) > 0; //Check if is falling down

            if (GoingDown)
            {
                DeltaDistance = Vector3.Project(DeltaPos, Gravity).magnitude / ScaleFactor;
                FallCurrentDistance += DeltaDistance;
            }

            if (GizmoDebug)
            {
                MDebug.DrawWireSphere(FallPoint, Color.magenta, Radius);
                MDebug.DrawWireSphere(FallPoint + Gravity * Height, Color.white, Radius);
                Debug.DrawRay(FallPoint, Gravity * 100f, Color.magenta);
            }

            // 100f * ScaleFactor — un-scaled 100f would miss the ground for very large animals
            var FoundGround = (Physics.Raycast(FallPoint, Gravity, out FallRayCast, 100f * ScaleFactor, GroundLayer, IgnoreTrigger));

            if (FoundGround)
            {
                DistanceToGround = FallRayCast.distance;

                if (GizmoDebug)
                {
                    // MDebug.DrawWireSphere(FallRayCast.point, (Color.blue + Color.red) / 2, Radius);
                    MDebug.DrawWireSphere(FallPoint, (Color.magenta), Radius);
                }

                switch (BlendFall)
                {
                    case FallBlending.DistanceNormalized:
                        {
                            var realDistance = DistanceToGround - Height; //Real Distance from the Hip to the Ground (Since the Ray is casted from the Hip)

                            if (MaxHeight < realDistance)
                            {
                                MaxHeight = realDistance; //get the Highest Distance the first time you touch the ground
                                Fall_Float = Mathf.Lerp(Fall_Float, 0f, DeltaTime * 10f); //Small blend in case there's a new ground found
                                animal.State_SetFloat(Fall_Float); //Blend between High and Low Fall
                            }
                            else
                            {
                                realDistance -= LowerBlendDistance;

                                //Small blend in case there's a new ground found
                                Fall_Float = Mathf.Lerp(Fall_Float, 1f - (realDistance / (MaxHeight - Height)), DeltaTime * 10f);

                                animal.State_SetFloat(Fall_Float); //Blend between High and Low Fall
                            }
                        }
                        break;
                    case FallBlending.Distance:
                        animal.State_SetFloat(FallCurrentDistance);
                        break;
                    case FallBlending.VerticalVelocity:
                        var UpInertia = Vector3.Project(animal.DeltaPos, animal.UpVector).magnitude;   //Clean the Vector from Forward and Horizontal Influence    
                        animal.State_SetFloat(UpInertia / DeltaTime * (GoingDown ? 1 : -1));
                        break;
                    default:
                        break;
                }

                //Facing downhill the Hip lands before the Chest (Main Pivot). Check the Hip too, or the animal keeps sliding on a walkable slope
                //Only 2 Pivot animals (quadrupeds, or a werewolf on 4 legs). With 1 Pivot the Main Pivot is already the Hip
                var HipLanding = animal.Has_Pivot_Chest && animal.Has_Pivot_Hip && Height < DistanceToGround && HipTouchedGround();

                if (HipLanding)
                {
                    DistanceToGround = FallRayCast.distance;
                    Debugging($"[Try Exit] Hip Pivot touched the ground [{DistanceToGround:F3}]");
                }

                //If we touch the Ground!
                if (Height >= DistanceToGround || ((DistanceToGround - DeltaDistance) < 0))
                {
                    var FallRayAngle = Vector3.SignedAngle(FallRayCast.normal, animal.UpVector, animal.Right);

                    if (FallRayCast.transform.gameObject != GameObjectHit) //Check if what the Fall Ray Hit was a Debree
                    {
                        GameObjectHit = FallRayCast.transform.gameObject;
                        IsDebree = GameObjectHit.CompareTag(animal.DebrisTag);
                    }

                    var DeepSlope = Mathf.Abs(FallRayAngle) >= animal.SlopeLimit;

                    if (!DeepSlope || IsDebree) //Check if we are not on a deep slope
                    {
                        //The Chest is still over lower ground. Do not fall again until the animal aligns to the slope
                        WaitHipAlignment = HipLanding;
                        HipLandTime = Time.time;

                        AllowExit();
                        animal.CheckIfGrounded();

                        //Meaning we still are in the Fall state (Check if Grounded can change to a new state) IMPORTANT
                        if (IsActiveState)
                        {
                            animal.Grounded = true; //Force Grounded
                            animal.UseGravity = false;

                            animal.AlignPosLerpDelta = animal.AlignPosLerp * 5;

                            var GroundedPos = Vector3.Project(FallRayCast.point - animal.transform.position, Gravity);
                            animal.Teleport_Internal(animal.transform.position + GroundedPos);

                            animal.ResetUPVector(); //IMPORTANT!
                            animal.hit_Hip.distance = Height;
                            animal.InertiaPositionSpeed = Vector3.ProjectOnPlane(animal.InertiaPositionSpeed, animal.UpVector); //This is for Helping on Slopes
                            Debugging($"[Try Exit] (Grounded) + [Terrain Angle = {FallRayAngle:F2}]. [Align to Ground]");
                            return;
                        }
                    }
                    else
                    {
                        FallCurrentDistance = 0;
                        return; //Do not check if the rigidbody has Increase Velocity
                    }
                }
            }
            ResetRigidbody(DeltaTime, Gravity);
        }

        /// <summary>Casts a short ray from the Hip Pivot when the Main (Chest) ray has not touched the ground yet. Stores the hit on FallRayCast</summary>
        private bool HipTouchedGround()
        {
            //Skip the ray while the Chest is too high for the Hip to be touching on a walkable slope (only the last frames of the fall pay for it)
            //Local pivot distance + the Fall Ray forward offset is an upper bound of the horizontal Chest/Hip distance. No world transform needed
            var ChestHipDistance = ((animal.Pivot_Chest.position - animal.Pivot_Hip.position).magnitude +
                Offset + (Mathf.Abs(animal.VerticalSmooth) * MoveMultiplier)) * ScaleFactor;

            if (DistanceToGround - Height > ChestHipDistance * Mathf.Tan(animal.SlopeLimit * Mathf.Deg2Rad)) return false;

            var HipPoint = animal.Pivot_Hip.World(transform) + animal.DeltaPos;

            if (GizmoDebug) Debug.DrawRay(HipPoint, Gravity * Height, Color.yellow);

            if (Physics.Raycast(HipPoint, Gravity, out var HipHit, Height, GroundLayer, IgnoreTrigger))
            {
                FallRayCast = HipHit;
                return true;
            }
            return false;
        }

        /// <summary>True while the Hip is still on walkable ground and the animal has not aligned to it after a Hip landing</summary>
        private bool HipStillAligning()
        {
            if (!animal.Has_Pivot_Chest) return false;                                   //Switched to 1 Pivot (e.g. werewolf on 2 legs). The Main Pivot is the Hip
            if (!animal.Grounded || !animal.MainRay) return false;                       //The Hip lost the ground. Let it fall
            if (Time.time - HipLandTime > HipAlignMaxWait) return false;                 //Safety: never block the Fall for too long

            var HipNormal = animal.hit_Hip.normal;
            if (Vector3.Dot(HipNormal, animal.UpVector) <= Mathf.Cos(animal.SlopeLimit * Mathf.Deg2Rad)) return false; //Deep slope under the Hip

            return Vector3.Dot(animal.Up, HipNormal) < HipAlignedCos;                    //Dot instead of Angle (no Acos)
        }


        public override void ExitState()
        {
            var status = 0;
            if (landStatus != null && landStatus.Length >= 1)
            {

                foreach (var ls in landStatus)
                    if (ls.x < FallCurrentDistance) status = (int)ls.y;
            }
            SetExitStatus(status);  //Set the Landing Status!! IMPORTANT for Multiple Landing Animations

            if (AffectStat != null && animalStats != null
                && FallCurrentDistance > FallMinDistance.Value && animal.Grounded) //Meaning if we are on the safe minimun distance we do not get damage from falling
            {
                // Clamp to [0, 100] — without this, falls beyond FallMaxDistance would reduce the stat by >100%
                var StatFallValue = Mathf.Clamp(FallCurrentDistance * 100f / FallMaxDistance.Value, 0f, 100f);
                animalStats.Stat_ModifyValue(AffectStat, StatFallValue, StatOption.ReduceByPercent);
            }

            LastPlatform = null;
            PlatformDelta = Vector3.zero;
            base.ExitState();
        }


        private void ResetRigidbody(float DeltaTime, Vector3 Gravity)
        {
            if (StuckAnimal && GoingDown)
            {
                //Debug.Log("GoinDown");

                //MWC: RB.linearVelocity is cleared/meaningless when the Animator runs in Normal mode (transform-driven movement).
                //Use the real transform velocity (Inertia = DeltaPos/DeltaTime) there so the stuck-detection works in both update modes.
                var CurrentVelocity = Anim.updateMode == AnimatorUpdateMode.Fixed ? animal.RB.linearVelocity : animal.Inertia;

                var RBOldDown = Vector3.Project(CurrentVelocity, Gravity);
                var RBNewDown = Vector3.Project(animal.DesiredRBVelocity, Gravity);
                var NewDMagn = RBNewDown.magnitude;
                var Old_DMagn = RBOldDown.magnitude;

                if (GizmoDebug)
                {
                    MDebug.Draw_Arrow(animal.Main_Pivot_Point + Forward * 0.02f, RBOldDown * 0.5f, Color.white);
                    MDebug.Draw_Arrow(animal.Main_Pivot_Point + Forward * 0.04f, RBNewDown * 0.5f, Color.green);
                }

                ResetCount++;

                if (NewDMagn == Old_DMagn) return;

                // Old_DMagn² is intentional: when velocity is near-zero (<0.1), its square is much smaller (<0.01),
                // making the threshold very sensitive to any new meaningful desired velocity — catches stuck states early.
                if (NewDMagn > (Old_DMagn * Old_DMagn) && Old_DMagn < 0.1f && ResetCount > 5)
                {
                    if (animal.DesiredRBVelocity.magnitude > Height)
                    {
                        Debugging($"Reset Rigidbody Velocity. Animal may be stuck");

                        animal.ResetUPVector();
                        animal.GravityTime = animal.StartGravityTime;

                        if (PushForward > 0)
                        {
                            // MWC — Escape direction: push forward by default, but reverse if a wall blocks the
                            // forward escape (e.g. head collider wedged against a ledge/box). Pushing +Forward there
                            // just drives the animal harder into the wall and it never unsticks. Horizontal only.
                            var pushDir = animal.Forward;
                            var probeEnd = animal.Main_Pivot_Point + (PushForward * ScaleFactor * pushDir);

                            if (Physics.Linecast(animal.Main_Pivot_Point, probeEnd, GroundLayer, IgnoreTrigger))
                                pushDir = -animal.Forward; //Forward is blocked → push away from the obstacle instead

                            if (GizmoDebug)
                                MDebug.Draw_Arrow(animal.Main_Pivot_Point, pushDir * PushForward * ScaleFactor, Color.cyan);

                            animal.InertiaPositionSpeed = animal.ScaleFactor * DeltaTime * PushForward * pushDir;  //Force escape movement HACK
                        }

                        ResetCount = 0;
                    }
                }
            }
            //else
            //{
            //    //ResetCount = 0;
            //}
        }

        /// <summary> This is for cleaning the Rigid body with unnecessary velocity </summary>
        private int ResetCount;

        public override void ResetStateValues()
        {
            DistanceToGround = float.PositiveInfinity;
            GoingDown = false;
            IsDebree = false;
            FallSpeed = MSpeed.Default; // match field initializer — new MSpeed() would lose named defaults (e.g. name = "FallSpeed")
            FallRayCast = new RaycastHit();
            GameObjectHit = null;
            System.Array.Clear(FallHits, 0, FallHits.Length); // reuse existing array — avoids a GC alloc on every state reset
            MaxHeight = float.NegativeInfinity; //Resets MaxHeight
            FallCurrentDistance = 0;
            Fall_Float = 0; //IMPORTANT

            // UpIntertia = Vector3.zero;
        }


        public override void StateGizmos(MAnimal animal)
        {
            if (!Application.isPlaying)
            {
                var fall_Pivot = animal.transform.position +
                    ((animal.Forward * Offset + new Vector3(0, animal.height)) * animal.ScaleFactor); //Calculate ahead the falling ray

                float Multiplier = animal.Pivot_Multiplier * lengthMultiplier;

                Debug.DrawRay(fall_Pivot, animal.Gravity.normalized * Multiplier, Color.magenta);
            }
        }

#if UNITY_EDITOR


        public override void SetSpeedSets(MAnimal animal)
        {
            //Do nothing... the Fall is an automatic State, the Fall Speed is created internally
        }

#pragma warning disable 414
        [HideInInspector, SerializeField] private bool ShowFront;
#pragma warning restore 414


        private void OnValidate()
        {
            ShowFront = MoveMultiplier.Value > 0;
        }

        /// <summary>This is Executed when the Asset is created for the first time </summary>
        internal override void Reset()
        {
            base.Reset();
            General = new AnimalModifier()
            {
                RootMotion = false,
                AdditivePosition = true,
                AdditiveRotation = true,
                Grounded = false,
                Sprint = false,
                OrientToGround = false,

                Gravity = true,
                CustomRotation = false,
                modify = (modifier)(-1),
            };

            LowerBlendDistance = 0.1f;
            MoveMultiplier = 0.1f;
            lengthMultiplier = 1f;

            FallSpeed.name = "FallSpeed";

            //SleepFromState = new System.Collections.Generic.List<StateID>() {   MTools.GetInstance<StateID>("Fly") };

            // ExitFrame = false; //IMPORTANT
        }
#endif
    }
}