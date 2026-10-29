// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Weapons
{
    /// <summary> Base for every projectile rotation behaviour, shared by ALL movements (Flying, Rolling, Homing...).
    /// Serialized inline on MProjectile with [SerializeReference]. Empty (null) keeps the rotation the projectile was fired with </summary>
    [System.Serializable]
    public abstract class ProjectileRotationBehaviour
    {
        /// <summary> Name shown on the rotation row in the inspector </summary>
        public virtual string DynamicName => GetType().Name;

        /// <summary> Prepares this behaviour for a new shot (capture the launch rotation, reset the spin) </summary>
        public virtual void Fire(MProjectile projectile) { }

        /// <summary> Orients the projectile for one step. [direction] and [up] are hints from the movement:
        /// Flying sends its trajectory and the direction opposite gravity, Rolling the slope-projected travel
        /// direction and the ground normal, Homing its chase direction and world up.
        /// [up] MUST be stable across steps: feeding the projectile's own up back in compounds the roll every step </summary>
        public abstract void ApplyRotation(MProjectile projectile, Vector3 direction, Vector3 up, float deltaTime);

        /// <summary> Copy used when a launcher overrides a projectile's rotation. Never share one instance
        /// between projectiles: behaviours keep per-shot state (spin angle, launch rotation), so two in flight at
        /// once would corrupt each other. Override this to deep-copy anything mutable a shallow copy would share </summary>
        public virtual ProjectileRotationBehaviour Clone() => (ProjectileRotationBehaviour)MemberwiseClone();

        /// <summary> Deep copy of a FloatReference, for Clone. A shallow copy would share the instance </summary>
        protected static FloatReference CopyOf(FloatReference source) => source == null ? null : new FloatReference
        {
            UseConstant = source.UseConstant,
            ConstantValue = source.ConstantValue,
            Variable = source.Variable,
        };

        /// <summary> Writes the rotation through the Rigidbody when there is one, so both paths behave the same </summary>
        protected static void Apply(MProjectile p, Quaternion rotation)
        {
            if (p.rb) p.rb.MoveRotation(rotation);
            else p.Trans.rotation = rotation;
        }

        /// <summary> Builds a behaviour from the legacy [ProjectileRotation] enum + fields. Null for [None].
        /// Shared by every migration path (MProjectile pre-split fields and the movements' own legacy fields) </summary>
        public static ProjectileRotationBehaviour FromLegacy(ProjectileRotation rotation,
            FloatReference torque, Vector3 torqueAxis, float trajectoryRoll, bool axisFollowTrajectory = true)
        {
            return rotation switch
            {
                ProjectileRotation.FollowTrajectory => new FollowTrajectoryRotation { roll = trajectoryRoll },
                ProjectileRotation.Random => new RandomRotation { torque = torque },
                ProjectileRotation.Axis => new AxisRotation { torque = torque, torqueAxis = torqueAxis, followTrajectory = axisFollowTrajectory },
                _ => null, //None: keep the rotation the projectile was fired with
            };
        }
    }

    /// <summary> Faces the direction of travel. Covers the old Follow Trajectory AND Rolling's Align To Ground
    /// (the movement decides the up: the ground normal makes it hug the slope) </summary>
    //If any of these types is ever renamed or moved to another namespace, add [MovedFrom] so existing
    //[SerializeReference] data keeps resolving. SerializeReference stores the assembly-qualified type name.
    [System.Serializable, AddTypeMenu("Follow Trajectory")]
    public class FollowTrajectoryRotation : ProjectileRotationBehaviour
    {
        public override string DynamicName => "Follow Trajectory";

        [Tooltip("Extra rotation offset (Euler, local) applied on top. Use it when the model is not authored facing its forward axis")]
        public Vector3 offset = Vector3.zero;

        [Tooltip("Roll around the flight axis, in degrees per second")]
        public float roll = 0;

        /// <summary> Accumulated roll in degrees, so the spin is continuous instead of reset every step </summary>
        private float spinAngle;

        public override void Fire(MProjectile p) => spinAngle = 0;

        public override void ApplyRotation(MProjectile p, Vector3 direction, Vector3 up, float deltaTime)
        {
            if (direction.sqrMagnitude < 0.0001f) return; //No direction yet, keep the current rotation

            var result = Quaternion.LookRotation(direction, up);

            //Roll around the flight axis. Accumulated, so it actually keeps rolling
            if (roll != 0)
            {
                spinAngle += roll * deltaTime;
                result = Quaternion.AngleAxis(spinAngle, direction.normalized) * result;
            }

            //Local offset last, so it corrects the model whatever the flight orientation is
            if (offset != Vector3.zero) result *= Quaternion.Euler(offset);

            Apply(p, result);
        }
    }

    /// <summary> Spins around one random world axis picked at launch, on top of the launch rotation </summary>
    [System.Serializable, AddTypeMenu("Random")]
    public class RandomRotation : ProjectileRotationBehaviour
    {
        public override string DynamicName => "Random";

        [Tooltip("Speed of the spin, in degrees per second")]
        public FloatReference torque = new(50f);

        /// <summary> Rotation the projectile was fired with. The spin is layered on top of it </summary>
        private Quaternion launchRotation = Quaternion.identity;
        /// <summary> One axis for the whole flight, picked at Fire </summary>
        private Vector3 spinAxis = Vector3.up;
        /// <summary> Accumulated spin in degrees </summary>
        private float spinAngle;

        public override void Fire(MProjectile p)
        {
            launchRotation = p.Trans.rotation;

            //MWC: was three Random.value components normalized, which only ever picked an axis in the positive
            //octant and produced a NaN rotation on the (rare) all-zero draw. This is uniform and never zero
            spinAxis = Random.onUnitSphere;
            spinAngle = 0;
        }

        public override void ApplyRotation(MProjectile p, Vector3 direction, Vector3 up, float deltaTime)
        {
            spinAngle += torque * deltaTime;
            Apply(p, launchRotation * Quaternion.AngleAxis(spinAngle, spinAxis));
        }

        /// <summary> [torque] is a FloatReference (a class), so a shallow copy would share it with every
        /// other projectile cloned from the same launcher override </summary>
        public override ProjectileRotationBehaviour Clone()
        {
            var copy = (RandomRotation)base.Clone();
            copy.torque = CopyOf(torque);
            return copy;
        }
    }

    /// <summary> Tumbles around an authored local axis. A thrown axe (facing its arc) or a shuriken (spinning flat) </summary>
    [System.Serializable, AddTypeMenu("Axis")]
    public class AxisRotation : ProjectileRotationBehaviour
    {
        public override string DynamicName => "Axis";

        [Tooltip("Speed of the spin, in degrees per second")]
        public FloatReference torque = new(50f);

        [Tooltip("Axis the projectile spins around, in its OWN local space.\nE.g. a thrown axe tumbling end over end spins around its local Right (1,0,0)")]
        public Vector3 torqueAxis = Vector3.up;

        [Tooltip("True: the projectile faces its trajectory and spins around Torque Axis on top of that, like a thrown axe.\nFalse: it keeps the rotation it was fired with and only spins, like a shuriken")]
        public bool followTrajectory = true;

        /// <summary> Rotation the projectile was fired with. Used when Follow Trajectory is off </summary>
        private Quaternion launchRotation = Quaternion.identity;
        /// <summary> Accumulated spin in degrees </summary>
        private float spinAngle;

        public override void Fire(MProjectile p)
        {
            launchRotation = p.Trans.rotation;
            spinAngle = 0;
        }

        public override void ApplyRotation(MProjectile p, Vector3 direction, Vector3 up, float deltaTime)
        {
            var basis = followTrajectory && direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, up)
                : launchRotation;

            spinAngle += torque * deltaTime;

            //A zero Torque Axis would make AngleAxis return identity and silently kill the spin
            var axis = torqueAxis.sqrMagnitude > 0.0001f ? torqueAxis.normalized : Vector3.up;

            //Right-multiply so the spin axis stays in the projectile's own local frame
            Apply(p, basis * Quaternion.AngleAxis(spinAngle, axis));
        }

        /// <summary> [torque] is a FloatReference (a class), so a shallow copy would share it with every
        /// other projectile cloned from the same launcher override </summary>
        public override ProjectileRotationBehaviour Clone()
        {
            var copy = (AxisRotation)base.Clone();
            copy.torque = CopyOf(torque);
            return copy;
        }
    }
}
