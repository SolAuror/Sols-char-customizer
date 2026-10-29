// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Scriptables;
using System.Collections;
using UnityEngine;

namespace MalbersAnimations.Weapons
{
    /// <summary> Where a Homing Projectile finds the thing it chases </summary>
    public enum HomingTarget
    {
        /// <summary> The Owner's Aim target, or the nearest Aim Target around the point the character is aiming at </summary>
        OwnerAim,
        /// <summary> The nearest Aim Target inside Search Radius around the projectile, re-acquired if it is lost </summary>
        NearestInRadius
    }

    /// <summary> Flies toward a target, steering at a limited turn rate. Homing arrows, seeking fireballs </summary>
    //If this type is ever renamed or moved to another namespace, add [MovedFrom] so existing
    //[SerializeReference] data keeps resolving. SerializeReference stores the assembly-qualified type name.
    [System.Serializable, AddTypeMenu("Homing")]
    public class HomingMovement : ProjectileMovement
    {
        public override string DynamicName => "Homing";

        [Tooltip("Where to find the target to chase")]
        public HomingTarget targetSource = HomingTarget.OwnerAim;

        //MWC: [speed] field removed. The launch Velocity magnitude (the Thrower's Power) IS the speed

        [Tooltip("How fast the projectile can steer toward the target, in degrees per second")]
        public FloatReference turnRate = new(180);

        [Tooltip("Seconds to fly straight before the homing starts. Lets the projectile clear the caster first")]
        public FloatReference homingDelay = new(0.1f);

        [Tooltip("Radius used to find and to re-acquire an Aim Target around the projectile. Only used by Nearest In Radius")]
        public FloatReference searchRadius = new(15);

        [Tooltip("Stop homing when the angle to the target exceeds this, in degrees. The projectile has overshot and flies straight from then on. Set to 0 to home forever")]
        public FloatReference loseTargetAngle = new(100);

        //MWC: Projectile Rotation - [faceDirection] removed WITHOUT a legacy migration: Homing is a new movement,
        //no shipped asset carries the field. Use a [Follow Trajectory] rotation behaviour on the MProjectile instead

        /// <summary> Reusable buffer for the target search </summary>
        private static readonly Collider[] Candidates = new Collider[32];

        public override void Fire(MProjectile p)
        {
            //Homing drives the transform itself, so gravity and physics motion must stay out of it
            MakeKinematic(p.rb);
        }

        public override IEnumerator Movement(MProjectile p)
        {
            var T = p.Trans;

            var dir = p.Velocity.sqrMagnitude > 0.0001f ? p.Velocity.normalized : T.forward;

            //MWC: [speed] removed - the Thrower's launch power is the speed
            var currentSpeed = p.Velocity.magnitude;
            if (currentSpeed <= 0) currentSpeed = 10f;

            var target = FindTarget(p, T.position);
            float elapsed = 0;

            p.Prev_Pos = T.position;
            p.Direction_ = dir;

            while (!p.HasImpacted && p.enabled)
            {
                //MWC: Time Multiplier - the travel and the steering use the SCALED step (so a slowed missile also
                //curves slowly and keeps the same path), while [homingDelay] stays real seconds
                var dt = Time.fixedDeltaTime;
                var sdt = ScaledDelta;
                var pos = T.position;

                elapsed += dt;

                //MWC: a chased transform that was destroyed or deactivated mid-flight is dropped;
                //Nearest In Radius re-acquires, Owner Aim flies straight from then on
                if (target.Lost)
                    target = targetSource == HomingTarget.NearestInRadius ? FindTarget(p, pos) : default;

                if (target.valid && elapsed >= homingDelay)
                {
                    var toTarget = target.Point - pos; //MWC: chase point = transform + local offset, or a fixed world point

                    if (toTarget.sqrMagnitude > 0.0001f)
                    {
                        var wanted = toTarget.normalized;

                        //Once the projectile has flown past the target, stop steering and let it fly straight out
                        if (loseTargetAngle > 0 && Vector3.Angle(dir, wanted) > loseTargetAngle)
                        {
                            p.Debugging("<color=yellow>[Homing] Overshot the target, flying straight</color>", p);
                            target = default;
                        }
                        else
                        {
                            dir = Vector3.RotateTowards(dir, wanted, turnRate * Mathf.Deg2Rad * sdt, 0f).normalized;
                        }
                    }
                }

                var step = currentSpeed * sdt;
                var next = pos + (dir * step);

                p.Direction_ = dir;
                p.NormalizedDirection = dir;

                //Sweep the gap so a fast projectile cannot tunnel through its target
                if (Physics.SphereCast(pos, p.SweepRadius, dir, out var hit, step, p.Layer, p.TriggerInteraction))
                {
                    if (!p.IsInvalid(hit.collider))
                    {
                        yield return p.WaitFixed;

                        p.OnRayCastHit.Invoke(hit);
                        p.ProjectileImpact(hit.rigidbody, hit.collider, hit.point, hit.normal);
                        yield break;
                    }
                }

                if (p.rb) p.rb.MovePosition(next);
                else T.position = next;

                //MWC: Projectile Rotation - the shared behaviour orients it (Follow Trajectory replaces [faceDirection])
                p.ApplyRotation(dir, Vector3.up, sdt); //MWC: scaled, so a slowed missile also spins slowly

                if (p.debug) Debug.DrawLine(pos, next, Color.magenta, 2f);

                p.Prev_Pos = next;

                yield return p.WaitFixed;
            }
        }

        /// <summary> What a homing shot chases: a Transform plus a local offset (so a point ON a moving object
        /// keeps tracking it), or a fixed world point when there is no transform </summary>
        protected struct ChaseTarget
        {
            /// <summary> The transform being chased. Null means [offset] is a fixed world point </summary>
            public Transform transform;
            /// <summary> Local offset on [transform], or the world point when there is no transform </summary>
            public Vector3 offset;
            /// <summary> False: nothing to chase, the projectile flies straight </summary>
            public bool valid;

            /// <summary> World position to fly to right now </summary>
            public readonly Vector3 Point => transform != null ? transform.TransformPoint(offset) : offset;

            /// <summary> True when a chased transform was destroyed or deactivated mid-flight.
            /// A fixed world point is never lost </summary>
            public readonly bool Lost => valid && !ReferenceEquals(transform, null)
                && (transform == null || !transform.gameObject.activeInHierarchy);
        }

        /// <summary> Resolves what to chase. Invalid when nothing was found (the projectile flies straight) </summary>
        protected virtual ChaseTarget FindTarget(MProjectile p, Vector3 from)
        {
            if (targetSource == HomingTarget.OwnerAim)
            {
                if (p.UserGo != null && p.UserGo.FindComponent<Aim>() is Aim aim)
                {
                    //MWC: the Aim Assist WINS: the ray touched an Aim Target with Aim Assist on, chase its Aim Center
                    if (aim.AimRayTargetAssist != null)
                        return new ChaseTarget { transform = aim.AimRayTargetAssist, valid = true };

                    //Then a locked target
                    if (aim.AimTarget != null)
                        return new ChaseTarget { transform = aim.AimTarget, valid = true };

                    //MWC: the object the ray is touching, WITHOUT assist: chase the exact aim point ON it,
                    //stored as a local offset so it keeps tracking when the object moves
                    if (aim.AimHit.transform != null)
                        return new ChaseTarget
                        {
                            transform = aim.AimHit.transform,
                            offset = aim.AimHit.transform.InverseTransformPoint(aim.AimPoint),
                            valid = true,
                        };

                    //Nothing touched: fly to the point in space the character is aiming at
                    return new ChaseTarget { offset = aim.AimPoint, valid = true };
                }

                return default;
            }

            return NearestAimTarget(p, from); //NearestInRadius: around the projectile itself
        }

        /// <summary> Nearest Aim Target inside Search Radius around [center]. Only colliders carrying an
        /// IAimTarget (Aim Target component) count: the ground and walls live in the Hit Layer so projectiles can
        /// impact them, but they must never be a homing destination. The chase point is the target's Aim Center </summary>
        protected virtual ChaseTarget NearestAimTarget(MProjectile p, Vector3 center)
        {
            var count = Physics.OverlapSphereNonAlloc(center, searchRadius, Candidates, p.Layer, p.TriggerInteraction);

            Transform nearest = null;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var col = Candidates[i];
                if (col == null) continue;
                if (col.transform == p.Trans || col.transform.IsChildOf(p.Trans)) continue;
                if (p.IsInvalid(col)) continue;

                //Only things explicitly marked as targets. Same marker the Aim Assist uses
                var aimTarget = col.FindInterface<IAimTarget>();
                if (aimTarget == null || aimTarget.AimPoint == null) continue;

                var dist = (aimTarget.AimPoint.position - center).sqrMagnitude;

                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = aimTarget.AimPoint; //Chase the Aim Center (E.g. the Head), not the collider pivot
                }
            }

            return new ChaseTarget { transform = nearest, valid = nearest != null };
        }
    }
}
