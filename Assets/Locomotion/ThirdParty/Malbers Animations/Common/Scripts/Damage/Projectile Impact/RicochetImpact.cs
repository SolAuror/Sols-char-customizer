// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Scriptables;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Weapons
{
    /// <summary> Bounces the projectile toward nearby Tagged targets a few times before its final impact </summary>
    //If this type is ever renamed or moved to another namespace, add [MovedFrom] so existing
    //[SerializeReference] data keeps resolving. SerializeReference stores the assembly-qualified type name.
    [System.Serializable, AddTypeMenu("Ricochet")]
    public class RicochetImpact : ProjectileImpactBehaviour
    {
        public override string DynamicName => "Ricochet";

        [Tooltip("How many times the projectile can bounce to a new target after its first impact")]
        public IntReference ricochets = new(2);

        [Tooltip("Radius around the impact point to search for the next target")]
        public FloatReference radius = new(6f);

        [Tooltip("Only targets with any of these Tags are valid ricochet destinations. Empty accepts everything valid on the Hit Layer")]
        public MTags tags;

        [Tooltip("Travel speed between targets, in meters per second. Set 0 to keep the projectile's launch speed")]
        public FloatReference speed = new(0);

        [SerializeReference, SubclassSelector]
        [Tooltip("What happens on the LAST impact, when no ricochet target remains. Empty stops the projectile in place")]
        public ProjectileImpactBehaviour finalImpact;

        /// <summary> Bounces left on the current shot </summary>
        private int remaining;
        /// <summary> Targets already hit by the current shot, so it never bounces back to one </summary>
        private HashSet<Transform> alreadyHit = new();
        /// <summary> Reusable buffer for the target search </summary>
        private static readonly Collider[] Candidates = new Collider[32];

        /// <summary> Resets the per-shot state. Pooled projectiles reuse the same behaviour instance </summary>
        public override void Fire(MProjectile p)
        {
            remaining = ricochets;
            alreadyHit.Clear();
            finalImpact?.Fire(p);
        }

        /// <summary> A shallow copy would share [alreadyHit] AND the [finalImpact] instance between projectiles,
        /// so one shot's bounces would suppress another's and the final behaviour's own state would be corrupted </summary>
        public override ProjectileImpactBehaviour Clone()
        {
            var copy = (RicochetImpact)base.Clone();
            copy.alreadyHit = new HashSet<Transform>();
            copy.finalImpact = finalImpact?.Clone();
            return copy;
        }

        /// <summary> The final behaviour decides how the collider is treated on disable </summary>
        public override bool RestoreColliderOnDisable => finalImpact == null || finalImpact.RestoreColliderOnDisable;

        /// <summary> The final behaviour decides what happens when Life elapses </summary>
        public override bool DestroyAtLifeEnd => finalImpact != null && finalImpact.DestroyAtLifeEnd;

        public override bool OnImpact(MProjectile p, Collider collider, Transform closestTransform, Vector3 hitPosition, Vector3 normal)
        {
            if (collider != null) alreadyHit.Add(Identity(collider)); //Never bounce back to this one

            if (remaining > 0 && FindNextTarget(p, hitPosition, out var next))
            {
                remaining--;
                Relaunch(p, next);
                return false; //Still travelling: skip the Life-Impact scheduling
            }

            //No bounces or no targets left: run the final behaviour on this impact
            if (finalImpact != null) return finalImpact.OnImpact(p, collider, closestTransform, hitPosition, normal);

            p.Trans.position = hitPosition; //Same as Stop In Place
            return true;
        }

        /// <summary> One transform per target, so a character with many colliders counts as a single ricochet stop </summary>
        private static Transform Identity(Collider col)
        {
            var holder = col.GetComponentInParent<Tags>(); //The Tag holder is the natural identity of a Tag-filtered target
            if (holder != null) return holder.transform;
            return col.attachedRigidbody != null ? col.attachedRigidbody.transform : col.transform;
        }

        /// <summary> Nearest valid Tagged collider inside [radius] that this shot has not hit yet </summary>
        protected virtual bool FindNextTarget(MProjectile p, Vector3 from, out Collider target)
        {
            target = null;
            var count = Physics.OverlapSphereNonAlloc(from, radius, Candidates, p.Layer, p.TriggerInteraction);
            var nearest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var col = Candidates[i];
                if (col == null) continue;
                if (col.transform == p.Trans || col.transform.IsChildOf(p.Trans)) continue;   //Never chase ourselves
                if (p.IsInvalid(col)) continue;                                               //Owner, layer and damager-tag rules still apply
                if (tags != null && tags.Length > 0 && !col.gameObject.HasMalbersTagInParent(tags)) continue;
                if (alreadyHit.Contains(Identity(col))) continue;                             //Already bounced on this one

                var dist = (col.bounds.center - from).sqrMagnitude;

                if (dist < nearest)
                {
                    nearest = dist;
                    target = col;
                }
            }

            return target != null;
        }

        /// <summary> Un-latches the impact and sends the projectile flying toward the next target </summary>
        protected virtual void Relaunch(MProjectile p, Collider target)
        {
            p.HasImpacted = false; //ProjectileImpact latched it and stopped every coroutine before calling us

            //ProjectileImpact may have frozen the body (trigger colliders). The travel drives the Transform, so kinematic and free
            if (p.rb)
            {
                p.rb.isKinematic = true;
                p.rb.useGravity = false;
                p.rb.constraints = RigidbodyConstraints.None;
            }

            var dir = (target.bounds.center - p.Trans.position).normalized;
            var spd = speed > 0 ? speed.Value : Mathf.Max(1f, p.Velocity.magnitude); //Keep the launch speed by default

            p.Velocity = dir * spd; //So the push force on the next impact stays correct
            p.Direction_ = dir;
            p.NormalizedDirection = dir;

            p.Debugging($"<color=yellow>[Ricochet] Bouncing to [{target.name}]. [{remaining}] bounce(s) left</color>", p);

            p.StartCoroutine(TravelToTarget(p, target, spd));
        }

        /// <summary> Straight chase toward the target at fixed speed, sweeping the gap so it cannot tunnel through </summary>
        protected virtual IEnumerator TravelToTarget(MProjectile p, Collider target, float spd)
        {
            var T = p.Trans;
            var dir = p.Velocity.normalized;

            //Safety leash: if the target vanished mid-flight the projectile must not fly forever (its Life timer was stopped on the first impact)
            var leash = (radius.Value * 2f) + 1f;
            float traveled = 0;

            p.Prev_Pos = T.position;

            while (!p.HasImpacted && p.enabled)
            {
                var dt = Time.fixedDeltaTime;
                var pos = T.position;

                //Keep steering: the target may be moving. A destroyed or disabled target lets it fly straight until the leash ends
                if (target != null && target.gameObject.activeInHierarchy)
                {
                    var to = target.bounds.center - pos;
                    if (to.sqrMagnitude > 0.0001f) dir = to.normalized;
                }

                var step = spd * dt;

                //Sweep the gap. Colliders overlapping the start of the cast (the one we just hit) are ignored by Unity
                if (Physics.SphereCast(pos, p.SweepRadius, dir, out var hit, step, p.Layer, p.TriggerInteraction))
                {
                    if (!p.IsInvalid(hit.collider))
                    {
                        //Deliberate one frame wait: the impact is processed on the FixedUpdate after the cast found it
                        yield return p.WaitFixed;

                        p.OnRayCastHit.Invoke(hit);
                        p.ProjectileImpact(hit.rigidbody, hit.collider, hit.point, hit.normal); //Runs this behaviour again: next bounce or final impact
                        yield break;
                    }
                }

                var next = pos + (dir * step);

                if (p.rb) p.rb.MovePosition(next);
                else T.position = next;

                //Face the travel direction, whatever rotation the movement left behind
                var look = Quaternion.LookRotation(dir, Vector3.up);
                if (p.rb) p.rb.MoveRotation(look);
                else T.rotation = look;

                p.Direction_ = dir;
                p.NormalizedDirection = dir;
                p.Prev_Pos = next;

                if (p.debug) Debug.DrawLine(pos, next, Color.cyan, 2f);

                traveled += step;

                if (traveled > leash)
                {
                    p.Debugging("<color=yellow>[Ricochet] Lost the target. Removing the projectile</color>", p);
                    p.Destroy_Projectile(); //Pools it, or destroys it when Destroy At Life End asks for it
                    yield break;
                }

                yield return p.WaitFixed;
            }
        }
    }
}
