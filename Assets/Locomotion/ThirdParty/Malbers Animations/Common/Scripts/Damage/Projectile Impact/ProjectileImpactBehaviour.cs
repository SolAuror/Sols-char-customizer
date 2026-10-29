// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Scriptables; //MWC: FloatReference for the Stick penetration depth
using UnityEngine;

namespace MalbersAnimations.Weapons
{
    /// <summary> Base for every projectile impact behaviour (Stop, Stick, Destroy, Ricochet...). Serialized inline on MProjectile with [SerializeReference] </summary>
    [System.Serializable]
    public abstract class ProjectileImpactBehaviour
    {
        /// <summary> Name shown on the impact row in the inspector </summary>
        public virtual string DynamicName => GetType().Name;

        /// <summary> Prepares this behaviour for a new shot. Called by MProjectile.Fire(), so pooled reuse resets any per-shot state </summary>
        public virtual void Fire(MProjectile projectile) { }

        /// <summary> Runs when a real impact stopped the projectile. Return false when the projectile was destroyed,
        /// pooled or relaunched, so MProjectile skips its Life-Impact scheduling; true when it is still sitting in the scene </summary>
        public abstract bool OnImpact(MProjectile projectile, Collider collider, Transform closestTransform, Vector3 hitPosition, Vector3 normal);

        /// <summary> False keeps the collider exactly as the impact left it when the projectile is disabled (Activate RigidBody) </summary>
        public virtual bool RestoreColliderOnDisable => true;

        /// <summary> True destroys the projectile when its Life elapses instead of returning it to the pool </summary>
        public virtual bool DestroyAtLifeEnd => false;

        /// <summary> Copy used when a launcher overrides a projectile's impact behaviour. Never share one instance
        /// between projectiles: behaviours keep per-shot state (Ricochet bounces, hit lists), so two in flight at once
        /// would corrupt each other. Override this to deep-copy anything mutable that a shallow copy would still share </summary>
        public virtual ProjectileImpactBehaviour Clone() => (ProjectileImpactBehaviour)MemberwiseClone();
    }

    /// <summary> The old [None] option: the projectile stops exactly at the hit point and waits for its Life Impact </summary>
    //If any of these types is ever renamed or moved to another namespace, add [MovedFrom] so existing
    //[SerializeReference] data keeps resolving. SerializeReference stores the assembly-qualified type name.
    [System.Serializable, AddTypeMenu("Stop In Place")]
    public class StopImpact : ProjectileImpactBehaviour
    {
        public override string DynamicName => "Stop In Place";

        public override bool OnImpact(MProjectile p, Collider collider, Transform closestTransform, Vector3 hitPosition, Vector3 normal)
        {
            p.Trans.position = hitPosition;
            MDebug.DrawWireSphere(hitPosition, Color.red, 0.1f, 2);
            return true;
        }
    }

    /// <summary> Parents the projectile to the hit surface (bone accurate), like an arrow stuck on a target </summary>
    [System.Serializable, AddTypeMenu("Stick On Surface")]
    public class StickImpact : ProjectileImpactBehaviour
    {
        public override string DynamicName => "Stick On Surface";

        //MWC: moved here from MProjectile, so the depth sits with the behaviour that uses it. A Ricochet ending in a
        //Stick now gets its own value too. The legacy [Penetration] field on MProjectile is migrated into this one
        [Tooltip("Distance to push the projectile INTO the surface, so it does not float on top of it")]
        public FloatReference penetration = new(0.1f);

        //MWC: a stuck projectile keeps its colliders SOLID, so a later projectile can still sweep into it and stick
        //(switching the body off with detectCollisions also hid it from SphereCast, which broke exactly that).
        //Only the pair with whatever it stuck INTO is ignored, so it cannot shove that character around.
        //Static geometry is skipped automatically - a wall cannot be pushed
        [Tooltip("Ignore collisions between this projectile and whatever it sticks into, so it cannot push that character around.\n" +
            "It stays solid to everything else, so another projectile can still stick into it. Restored when it returns to the pool")]
        public bool ignoreCollisionWithTarget = true;

        //MWC: a tumbling projectile (E.g. a thrown axe) reaches the wall at whatever spin phase it happens to be in,
        //so it sticks by the handle as often as by the blade. Forcing the rotation on impact makes the same part bite
        [Tooltip("Rotate the projectile on impact so the SAME part always bites into the surface.\n" +
            "Use it for a tumbling projectile (E.g. a thrown axe) that would otherwise stick by its handle")]
        public bool alignOnImpact = false;

        [Hide(nameof(alignOnImpact))]
        [Tooltip("LOCAL axis of the projectile that must end up pointing INTO the surface.\n" +
            "E.g. an axe whose blade faces its own forward uses (0,0,1); a blade along its local Right uses (1,0,0)")]
        public Vector3 biteAxis = Vector3.forward;

        [Hide(nameof(alignOnImpact))]
        [Tooltip("LOCAL axis kept pointing AWAY from the surface. It controls the roll around the Bite Axis, so the projectile cannot stick upside down")]
        public Vector3 upAxis = Vector3.up;

        [Hide(nameof(alignOnImpact))]
        [Range(-180, 180), Tooltip("Extra roll around the Bite Axis, in degrees")]
        public float rollOffset = 0;

        public override bool OnImpact(MProjectile p, Collider collider, Transform closestTransform, Vector3 hitPosition, Vector3 normal)
        {
            //MWC: the direction it was actually flying, resolved through the Movement's own travel direction
            var travel = p.TravelDirection;

            var surface = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.zero;

            //MWC: the direction the projectile buries itself in is THE WAY IT WAS THROWN. A real axe or arrow drives
            //in along its trajectory, and that is what this feature exists to keep: Align On Impact only forces WHICH
            //PART bites (the blade, not the handle). Keying the bite on the normal made every hit come out square to
            //the face it landed on - three axes thrown from the same spot at one cube stuck 90 degrees apart, one per
            //face, none of them along the throw
            var bite = travel;

            //...except on a FLOOR or a CEILING. A throw arrives there at a shallow descent, and burying the head along
            //that line stands the axe on end or tips it over; what it really does is lay into the ground. So the bite
            //goes straight down the surface normal and the trajectory shows in the HEADING instead, the shaft trailing
            //back along the throw. A wall is the opposite: the throw arrives near head-on, so the trajectory IS the bite
            var flatSurface = surface != Vector3.zero && Mathf.Abs(Vector3.Dot(surface, Vector3.up)) > 0.7f;

            if (flatSurface) bite = -surface;

            //Only a projectile that never moved (a magic particle, a teleported hit) has no trajectory at all
            if (bite.sqrMagnitude < 0.0001f)
                bite = surface != Vector3.zero ? -surface : p.Trans.forward;

            if (alignOnImpact) p.Trans.rotation = BiteRotation(bite, travel);

            //Bury it along the bite direction when aligned (so the blade goes in), along the travel direction otherwise
            p.Impact_StickOnSurface(closestTransform, hitPosition, penetration.Value,
                alignOnImpact ? bite : p.Direction_.normalized);

            // if (ignoreCollisionWithTarget) p.Collisions_IgnoreWith(collider);

            return true;
        }

        /// <summary> Rotation that maps the local [biteAxis] onto [bite] and rolls [upAxis] to keep the handle clear.
        /// Built as "target frame x the inverse of the model's own frame", so any authored axis pair works </summary>
        protected virtual Quaternion BiteRotation(Vector3 bite, Vector3 travel)
        {
            var localBite = biteAxis.sqrMagnitude > 0.0001f ? biteAxis.normalized : Vector3.forward;
            var localUp = upAxis.sqrMagnitude > 0.0001f ? upAxis.normalized : Vector3.up;

            //Two parallel local axes cannot describe a frame, so pick any perpendicular one instead
            if (Mathf.Abs(Vector3.Dot(localBite, localUp)) > 0.999f)
            {
                var side = Vector3.Cross(localBite, Vector3.right);
                localUp = side.sqrMagnitude > 0.001f ? side.normalized : Vector3.Cross(localBite, Vector3.forward).normalized;
            }

            //MWC: reference for the roll AROUND the bite axis, which is what decides where the HANDLE ends up.
            //On a wall the bite is roughly horizontal, so world up is free to hang the handle downwards, which is how
            //a wall-stuck axe should look
            var reference = Vector3.up;

            //Biting straight down or up (a floor or a ceiling) leaves world up useless - it IS the bite - so the
            //throw's own HEADING takes over and the projectile comes to rest lying along the direction it was thrown
            if (Mathf.Abs(Vector3.Dot(reference, bite.normalized)) > 0.99f)
            {
                reference = Vector3.ProjectOnPlane(travel, Vector3.up);
                if (reference.sqrMagnitude < 0.0001f) reference = Vector3.forward;
            }


            var target = Quaternion.LookRotation(bite, reference);     //where the bite axis must end up
            var model = Quaternion.LookRotation(localBite, localUp);   //where it starts, in the model's own space

            return Quaternion.AngleAxis(rollOffset, bite) * target * Quaternion.Inverse(model);
        }
    }

    /// <summary> Destroys the projectile the moment it impacts. It will never use the Projectile Pool </summary>
    [System.Serializable, AddTypeMenu("Destroy")]
    public class DestroyImpact : ProjectileImpactBehaviour
    {
        public override string DynamicName => "Destroy";

        /// <summary> A Destroy projectile is also destroyed (not pooled) when its Life elapses </summary>
        public override bool DestroyAtLifeEnd => true;

        public override bool OnImpact(MProjectile p, Collider collider, Transform closestTransform, Vector3 hitPosition, Vector3 normal)
        {
            p.Debugging("DestroyOnImpact", p);
            p.Release_Attached(); //Do not destroy a projectile stuck to this one
            Object.Destroy(p.gameObject);
            return false;
        }
    }

    /// <summary> Hands the projectile over to physics on impact, so it bounces and rolls away </summary>
    [System.Serializable, AddTypeMenu("Activate RigidBody")]
    public class ActivateRigidbodyImpact : ProjectileImpactBehaviour
    {
        public override string DynamicName => "Activate RigidBody";

        /// <summary> The collider was re-enabled for physics, keep it that way when the projectile is disabled </summary>
        public override bool RestoreColliderOnDisable => false;

        public override bool OnImpact(MProjectile p, Collider collider, Transform closestTransform, Vector3 hitPosition, Vector3 normal)
        {
            p.RigidBody_Enable();
            p.Collider_Enable();
            p.Debugging("Activate Rigid Body", p);
            return true;
        }
    }

    /// <summary> Returns the projectile to its pool on impact (destroys it when it has no pool) </summary>
    [System.Serializable, AddTypeMenu("Disable (Pool)")]
    public class DisableImpact : ProjectileImpactBehaviour
    {
        public override string DynamicName => "Disable (Pool)";

        public override bool OnImpact(MProjectile p, Collider collider, Transform closestTransform, Vector3 hitPosition, Vector3 normal)
        {
            p.Debugging("DisableOnImpact [Return to the Pool]", p);
            p.Pool_Release(p.gameObject); //Releases any stuck projectile first, and falls back to Destroy without a pool
            return false;
        }
    }
}
