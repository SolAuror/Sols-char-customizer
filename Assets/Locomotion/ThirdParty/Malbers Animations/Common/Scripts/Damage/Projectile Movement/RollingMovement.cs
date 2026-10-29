// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Scriptables;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization; //MWC: Projectile Rotation - FormerlySerializedAs keeps the legacy rotation keys readable

namespace MalbersAnimations.Weapons
{
    /// <summary> When a Rolling Projectile should deactivate </summary>
    public enum DeactivateProjectile { Life, MaxDistance };

    /// <summary> Why a Rolling Projectile stopped travelling </summary>
    public enum RollingStopCause { MaxDistance, Life, Obstacle, Slope, NoGround };

    //MWC: [RollingDirection] removed. The launcher owns the direction now: it fires with a Velocity,
    //and Rolling simply flattens that Velocity onto the ground plane (see Fire)

    /// <summary> Kinematic sweep that travels forward hugging the ground. E.g. a staff that shoots a moving spike </summary>
    //If this type is ever renamed or moved to another namespace, add [MovedFrom] so existing
    //[SerializeReference] data keeps resolving. SerializeReference stores the assembly-qualified type name.
    [System.Serializable, AddTypeMenu("Rolling")]
    public class RollingMovement : ProjectileMovement
    {
        public override string DynamicName => "Rolling";

        //MWC: [direction] field removed with [RollingDirection]. The Thrower decides where the shot goes
        //MWC: [speed] field removed too. The launch Velocity magnitude (the Thrower's Power) IS the speed

        [SerializeField, Tooltip("Layers the projectile treats as Ground and as blocking Obstacles. These layers NEVER cause damage impacts")]
        private LayerReference m_groundMask = new(1);
        /// <summary> Layers this movement walks on and is blocked by. Never treated as damage impacts </summary>
        public LayerMask GroundMask { get => m_groundMask.Value; set => m_groundMask.Value = value; }

        [Tooltip("Remove the projectile when its Life or its Max Distance is reached")]
        public DeactivateProjectile deactivate = DeactivateProjectile.Life;

        [Tooltip("Maximum distance the projectile will travel")]
        public FloatReference maxDistance = new(20);
        //MWC: [destroyOnMaxDistance] and [destroyOnLife] removed. Destroy-vs-pool is the OnImpact behaviour's
        //call now: Destroy_Projectile routes through its [DestroyAtLifeEnd]

        [Tooltip("Distance in meters to push the spawn point forward (along the travel direction) before the projectile starts")]
        public FloatReference startOffset = new(0);
        [Tooltip("Extra height in meters above the ground surface. The collider's own height is measured automatically, this is added on top")]
        public FloatReference heightOffset = new(0);

        [Tooltip("Maximum UP slope the projectile can climb, in degrees. Anything steeper (a wall) stops it. Set to 0 to climb anything")]
        public FloatReference maxUpSlopeAngle = new(30);
        [Tooltip("Maximum DOWN slope the projectile can descend, in degrees. Anything steeper (a ledge) stops it. Set to 0 to descend anything")]
        public FloatReference maxDownSlopeAngle = new(0);

        //MWC: Projectile Rotation - the rotation (and Align To Ground) moved to the shared [rotationBehaviour] on
        //MProjectile: Align To Ground IS Follow Trajectory fed with the ground normal as up. These stay serialized
        //(hidden) so MigrateRotation can carry an existing asset's values across
        //MWC: defaults to FALSE on purpose. A [true] default is dormant legacy data on a BRAND NEW Rolling movement,
        //so clearing the Rotation field made a Follow Trajectory pop straight back in on the next OnValidate.
        //An asset that really was authored with Align To Ground still deserializes its own [true] and migrates once
        [SerializeField, HideInInspector, FormerlySerializedAs("alignToGround")]
        private bool m_legacyAlignToGround = false;
        [SerializeField, HideInInspector, FormerlySerializedAs("rotation")]
        private ProjectileRotation m_legacyRotation = ProjectileRotation.None;
        [SerializeField, HideInInspector, FormerlySerializedAs("torque")]
        private FloatReference m_legacyTorque = new(50f);
        [SerializeField, HideInInspector, FormerlySerializedAs("torqueAxis")]
        private Vector3 m_legacyTorqueAxis = Vector3.up;
        [SerializeField, HideInInspector, FormerlySerializedAs("trajectoryRoll")]
        private float m_legacyTrajectoryRoll = 0;

        /// <summary> Projectile Rotation: bakes the legacy Align To Ground / rotation fields into a shared behaviour.
        /// Align To Ground (the old default) becomes Follow Trajectory, which hugs the slope through the ground-normal up hint </summary>
        public override bool TryExtractLegacyRotation(out ProjectileRotationBehaviour rotation)
        {
            rotation = null;
            var changed = false;

            if (m_legacyAlignToGround)
            {
                rotation = new FollowTrajectoryRotation();
                m_legacyAlignToGround = false; //Consumed: never refire over a user-emptied rotation
                changed = true;
            }
            else if (m_legacyRotation != ProjectileRotation.None)
            {
                rotation = ProjectileRotationBehaviour.FromLegacy(m_legacyRotation, m_legacyTorque, m_legacyTorqueAxis, m_legacyTrajectoryRoll);
                changed = true;
            }

            if (m_legacyRotation != ProjectileRotation.None)
            {
                m_legacyRotation = ProjectileRotation.None; //Consumed either way
                changed = true;
            }

            return changed;
        }
        [Tooltip("True: damages every target it passes through and keeps going.\nFalse: stops on the first target it damages")]
        public BoolReference pierce = new(true);

        /// <summary> Distance from the pivot down to the lowest point of the collider. Measured at Fire </summary>
        private float groundClearance;
        /// <summary> Resolved travel speed for the current shot </summary>
        private float currentSpeed;
        /// <summary> Horizontal travel direction, fixed at Fire </summary>
        private Vector3 travelDirection;
        /// <summary> Radius used by the forward damage sweep </summary>
        private float damageRadius;
        /// <summary> Horizontal distance travelled by the current shot </summary>
        private float traveled;
        /// <summary> Time the current shot has been travelling </summary>
        private float travelTime;
        /// <summary> Colliders already damaged by the current shot. Stops a pierce hit damaging the same target twice </summary>

#if UNITY_6000_4_OR_NEWER
        private HashSet<EntityId> damaged = new();
#else
        private HashSet<int> damaged = new();
#endif
        /// <summary> Reusable buffer for the forward damage sweep </summary>
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        /// <summary> Headroom above the sample point where the ground probe starts. Doubles as the tallest step it can climb.
        /// Kept small on purpose: a tall probe grabs bridges and ceilings above the projectile </summary>
        private const float ProbeUp = 0.5f;
        /// <summary> How far below the sample point the ground probe reaches before calling it a hole </summary>
        private const float ProbeDown = 50f;

        /// <summary> The sweep finds its own targets, so the physics callbacks would double-fire </summary>
        public override bool IgnorePhysicsCallbacks => true;

        /// <summary> A shallow copy would share [damaged] between projectiles, so one shot's hits would
        /// suppress another's, and one shot's Clear would wipe another's list mid-flight </summary>
        public override ProjectileMovement Clone()
        {
            var copy = (RollingMovement)base.Clone();
            copy.damaged = new();
            return copy;
        }

        public override void Fire(MProjectile p)
        {
            damaged.Clear();
            traveled = 0;
            travelTime = 0;

            var T = p.Trans;

            //The sweep drives the Transform every FixedUpdate, so physics must never move it
            MakeKinematic(p.rb);

            //The collider must be on before its bounds can be measured
            if (p.m_collider) p.m_collider.enabled = true;

            //MWC: the launcher owns the direction: the launch Velocity is simply flattened onto the ground plane.
            //[ResolveDirection] (Owner Forward / Aim Direction sources) was removed with it
            var dir = Vector3.ProjectOnPlane(p.Velocity, Vector3.up);

            //Last resorts: the projectile's own forward, then world forward
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.ProjectOnPlane(T.forward, Vector3.up);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;

            travelDirection = dir.normalized;
            p.Direction_ = travelDirection;
            p.NormalizedDirection = travelDirection;

            //MWC: [speed] removed - the Thrower's launch power is the speed
            currentSpeed = p.Velocity.magnitude;
            if (currentSpeed <= 0) currentSpeed = 1f;

            //Ground clearance measured from the real collider bounds. Covers Box, Sphere, Capsule and Mesh,
            //including the collider's own center offset and the transform scale
            groundClearance = 0;
            //Radius acts as a floor, the collider's real width wins when it is bigger
            damageRadius = Mathf.Max(0.05f, p.SweepRadius);

            if (p.m_collider)
            {
                var bounds = p.m_collider.bounds;

                if (bounds.size.sqrMagnitude > 0)
                {
                    groundClearance = Mathf.Max(0, T.position.y - bounds.min.y);
                    damageRadius = Mathf.Max(damageRadius, Mathf.Max(bounds.extents.x, bounds.extents.z));
                }
            }

            groundClearance += heightOffset.Value;

            //Push the spawn point forward so the projectile does not start inside the caster
            var start = T.position + (travelDirection * startOffset.Value);

            //Drop it onto the ground before the first step
            if (ProbeGround(start, out var groundHit))
                start = groundHit.point + (Vector3.up * groundClearance);
            else
                p.Debugging("<color=yellow>[Rolling] No ground found below the spawn point. Check the Ground Mask</color>", p);

            T.position = start;
            if (p.rb) p.rb.position = start;

            p.Prev_Pos = start;
        }

        public override IEnumerator Movement(MProjectile p)
        {
            var T = p.Trans;
            //MWC: Projectile Rotation - the launch rotation and spin state live in the shared behaviour now

            //Height of the ground under the projectile on the previous step. Drives the slope check
            float prevGroundY = T.position.y - groundClearance;

            var maxUp = maxUpSlopeAngle.Value;
            var maxDown = maxDownSlopeAngle.Value;

            //A 0 angle means "no limit", which for the obstacle test means anything short of a true vertical wall
            var climbLimit = maxUp > 0 ? maxUp : 89f;

            while (!p.HasImpacted && p.enabled)
            {
                //MWC: Time Multiplier - the travel uses the SCALED step, the Life timer below keeps real time so
                //[Life] means the same number of seconds on every movement
                var dt = Time.fixedDeltaTime;
                var sdt = ScaledDelta;
                var step = currentSpeed * sdt;
                var pos = T.position;

                travelTime += dt;

                // --- Life -------------------------------------------------------------------------------------
                if (deactivate == DeactivateProjectile.Life && p.Life > 0 && travelTime >= p.Life)
                {
                    End(p, RollingStopCause.Life);
                    yield break;
                }

                // --- Obstacle in front ------------------------------------------------------------------------
                //Cast at "step height" so the ground itself is never mistaken for a wall
                var castOrigin = pos + (Vector3.up * Mathf.Max(0.02f, groundClearance * 0.5f));

                if (Physics.Raycast(castOrigin, travelDirection, out var wall, step + damageRadius,
                        GroundMask, QueryTriggerInteraction.Ignore))
                {
                    if (Vector3.Angle(wall.normal, Vector3.up) > climbLimit)
                    {
                        //Stop just short of the wall instead of inside it
                        var backOff = Mathf.Max(0, wall.distance - damageRadius);
                        T.position = pos + (travelDirection * backOff);
                        if (p.rb) p.rb.position = T.position;

                        End(p, RollingStopCause.Obstacle);
                        yield break;
                    }
                }

                // --- Damage sweep across this step ------------------------------------------------------------
                if (SweepForTargets(p, pos, step)) yield break; //True when a non-piercing hit stopped the shot
                if (p.HasImpacted) yield break;                 //A listener may have impacted us from an event

                // --- Advance and find the ground --------------------------------------------------------------
                var nextPos = pos + (travelDirection * step);

                if (!ProbeGround(nextPos, out var ground))
                {
                    End(p, RollingStopCause.NoGround);
                    yield break;
                }

                // --- Slope, measured from the real height change over this step -------------------------------
                //Terrain normals are noisy, the height delta along the travel direction is not
                var slope = Mathf.Atan2(ground.point.y - prevGroundY, step) * Mathf.Rad2Deg;

                if ((maxUp > 0 && slope > maxUp) || (maxDown > 0 && -slope > maxDown))
                {
                    End(p, RollingStopCause.Slope);
                    yield break;
                }

                nextPos = ground.point + (Vector3.up * groundClearance);
                prevGroundY = ground.point.y;

                // --- Apply the movement -----------------------------------------------------------------------
                if (p.rb) p.rb.MovePosition(nextPos);
                else T.position = nextPos;

                //MWC: Projectile Rotation - the ground normal is the up hint, and the forward follows the slope,
                //so Follow Trajectory reproduces the old Align To Ground exactly
                var slopeForward = Vector3.ProjectOnPlane(travelDirection, ground.normal);
                if (slopeForward.sqrMagnitude < 0.0001f) slopeForward = travelDirection;

                p.ApplyRotation(slopeForward, ground.normal, sdt); //MWC: scaled, so a slowed sweep also spins slowly

                traveled += step;
                p.Prev_Pos = nextPos;

                if (p.debug)
                {
                    Debug.DrawLine(pos, nextPos, Color.cyan, 2f);
                    Debug.DrawRay(ground.point, ground.normal * 0.25f, Color.green, 2f);
                }

                // --- Max Distance -----------------------------------------------------------------------------
                if (deactivate == DeactivateProjectile.MaxDistance && traveled >= maxDistance)
                {
                    End(p, RollingStopCause.MaxDistance);
                    yield break;
                }

                yield return p.WaitFixed;
            }
        }

        /// <summary> Raycasts down onto the Ground Mask at a horizontal position. False means there is nothing under it </summary>
        protected bool ProbeGround(Vector3 position, out RaycastHit hit)
        {
            var origin = position + (Vector3.up * ProbeUp);
            return Physics.Raycast(origin, Vector3.down, out hit, ProbeUp + ProbeDown, GroundMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary> Sweeps the damage volume across one step. Returns true when a non-piercing hit ended the shot </summary>
        protected virtual bool SweepForTargets(MProjectile p, Vector3 from, float step)
        {
            var count = Physics.SphereCastNonAlloc(from, damageRadius, travelDirection, Hits, step, p.Layer, p.TriggerInteraction);

            for (int i = 0; i < count; i++)
            {
                var col = Hits[i].collider;
                if (col == null) continue;

                //The sweep starts inside our own collider, so it reports itself at distance 0
                if (col.transform == p.Trans || col.transform.IsChildOf(p.Trans)) continue;

                //The ground and the walls the projectile travels over never count as damage
                if (MTools.Layer_in_LayerMask(col.gameObject.layer, GroundMask)) continue;

                if (p.IsInvalid(col)) continue;

#if UNITY_6000_4_OR_NEWER
                if (!damaged.Add(col.GetEntityId())) continue; //Already damaged by this shot
#else
                if (!damaged.Add(col.GetInstanceID())) continue; //Already damaged by this shot
#endif
                //A SphereCast that starts already overlapping reports a zero point/normal
                var point = Hits[i].distance > 0 ? Hits[i].point : col.ClosestPoint(from);
                var normal = Hits[i].distance > 0 ? Hits[i].normal : (from - col.bounds.center).normalized;

                var doesPierce = pierce.Value;
                p.RollingHitWithTag = p.Tags != null && p.Tags.Length > 0;

                p.ProjectileImpact(col.attachedRigidbody, col, point, normal,
                    doesPierce ? ProjectileImpactKind.Pierce : ProjectileImpactKind.Impact);

                if (!doesPierce)
                {
                    //ProjectileImpact already ran the Impact Behaviour and latched HasImpacted
                    p.Debugging($"<color=yellow>[Rolling] Stopped on [{col.name}]</color>", p);
                    return true;
                }
            }

            return false;
        }

        //MWC: Projectile Rotation - [ApplyRotation] removed. The shared ProjectileRotationBehaviour orients the projectile

        /// <summary> Stops the projectile in place and schedules its removal after Life Impact </summary>
        protected virtual void End(MProjectile p, RollingStopCause cause)
        {
            p.Debugging($"<color=yellow><b>[Rolling Stopped]</b> [{cause}] at [{traveled:0.00}m / {travelTime:0.00}s]</color>", p);

            //Already kinematic while rolling, so the same guard applies: no velocity assignment on a kinematic body
            MakeKinematic(p.rb, interpolate: false);

            if (p.m_collider) p.m_collider.enabled = false;

            //Only a full-distance run reports true; a deflection or a slope angle sends false
            p.OnReachedMaxDistance.Invoke(cause == RollingStopCause.MaxDistance);

            p.Active = false;

            //MWC: [destroyNow] removed. Destroy_Projectile already routes destroy-vs-pool through the OnImpact behaviour
            p.Delay_Action(p.LifeImpact, p.Destroy_Projectile);
        }
    }
}
