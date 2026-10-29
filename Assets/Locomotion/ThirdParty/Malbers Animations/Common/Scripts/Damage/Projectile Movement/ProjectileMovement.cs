// Last Change: 9/17/2026 6:05pm
using System.Collections;
using UnityEngine;

namespace MalbersAnimations.Weapons
{
    /// <summary> Base for every projectile travel behaviour (Flying, Rolling, Homing...). Serialized inline on MProjectile with [SerializeReference] </summary>
    [System.Serializable]
    public abstract class ProjectileMovement
    {
        /// <summary> Name shown on the movement row in the inspector </summary>
        public virtual string DynamicName => GetType().Name;

        //MWC: Time Multiplier - shared by every movement. It scales the RATE the projectile travels, never the
        //velocity, so a Flying arc keeps its exact shape and landing point and only the speed along it changes
        [SerializeField, Tooltip("Multiplies how fast the projectile travels.\n" +
            "1 = normal, 0.5 = half speed, 2 = double. A Flying projectile keeps its arc shape and landing point either way.\n" +
            "0 freezes it in place (E.g. a time-stop effect)")]
        private Scriptables.FloatReference m_timeMultiplier = new(1f);

        /// <summary> Travel speed multiplier. Never negative: that would run the movement backwards </summary>
        public float TimeMultiplier
        {
            get => Mathf.Max(0, m_timeMultiplier.Value);
            set => m_timeMultiplier.Value = value;
        }

        /// <summary> One physics step scaled by the Time Multiplier. Movements advance by this instead of
        /// Time.fixedDeltaTime. Real-time timers (Life) deliberately keep using the unscaled step </summary>
        protected float ScaledDelta => Time.fixedDeltaTime * TimeMultiplier;

        /// <summary> Prepares this movement for a new shot. Called by MProjectile.Fire() before Movement() starts </summary>
        public virtual void Fire(MProjectile projectile) { }

        /// <summary> The travel loop. Runs as a coroutine on the projectile until it impacts or the movement ends it </summary>
        public abstract IEnumerator Movement(MProjectile projectile);

        /// <summary> True when this movement finds its own targets, so MProjectile ignores OnCollisionEnter/OnTriggerEnter </summary>
        public virtual bool IgnorePhysicsCallbacks => false;

        /// <summary> Lets a movement reject a physics impact before it is processed </summary>
        public virtual bool IsImpactValid(MProjectile projectile, Collider other) => true;

        /// <summary> Copy used when a launcher overrides a projectile's movement. Never share one instance
        /// between projectiles: movements keep per-shot state, so two in flight at once would corrupt each other.
        /// Override this to deep-copy anything mutable that a shallow copy would still share </summary>
        public virtual ProjectileMovement Clone()
        {
            var copy = (ProjectileMovement)MemberwiseClone();

            //MWC: Time Multiplier - a FloatReference is a class, so a shallow copy would share this one with every
            //other projectile cloned from the same launcher override
            copy.m_timeMultiplier = new Scriptables.FloatReference
            {
                UseConstant = m_timeMultiplier.UseConstant,
                ConstantValue = m_timeMultiplier.ConstantValue,
                Variable = m_timeMultiplier.Variable,
            };

            return copy;
        }

        /// <summary> Projectile Rotation: hands this movement's legacy rotation data over to MProjectile.MigrateRotation
        /// and CLEARS it, so a user-emptied rotation is never re-migrated. True when anything was consumed </summary>
        public virtual bool TryExtractLegacyRotation(out ProjectileRotationBehaviour rotation)
        {
            rotation = null;
            return false;
        }

        /// <summary> Hands the body over to a movement that drives the Transform itself.
        /// Velocities are only cleared while the body is still dynamic: Unity logs
        /// "Setting linear velocity of a kinematic body is not supported" otherwise, and a pooled projectile
        /// comes back already kinematic from its previous flight </summary>
        protected static void MakeKinematic(Rigidbody rb, bool interpolate = true)
        {
            if (rb == null) return;

            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            rb.useGravity = false;
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.None;

            if (interpolate) rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    /// <summary> Caches the Flying gravity of a projectile prefab for a launcher. Launchers ask for it every
    /// FixedUpdate while predicting a trajectory, so the component lookup only runs when the prefab actually changes </summary>
    public struct ProjectileGravityCache
    {
        private GameObject cached;
        private Vector3 gravity;
        private bool resolved;

        /// <summary> Gravity the given projectile prefab flies under </summary>
        public Vector3 Get(GameObject projectile)
        {
            if (!resolved || projectile != cached)
            {
                cached = projectile;
                resolved = true;
                gravity = MProjectile.GravityOf(projectile);
            }

            return gravity;
        }
    }
}
