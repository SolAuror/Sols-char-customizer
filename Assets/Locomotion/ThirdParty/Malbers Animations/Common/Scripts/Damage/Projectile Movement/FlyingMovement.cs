// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Scriptables;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization; //MWC: Projectile Rotation - FormerlySerializedAs keeps the legacy rotation keys readable

namespace MalbersAnimations.Weapons
{
    /// <summary> What launchers do with a Flying projectile's Launch Angle </summary>
    public enum LaunchAngleMode
    {
        /// <summary> Keeps the Force and lifts the aim direction by the angle (MShootable's Aim Angle). The shot flies a higher arc but does not land exactly on the point. The default </summary>
        TiltAim = 0,
        /// <summary> Computes the Force needed to land the arc on the Target or aim point. The Force changes with the distance </summary>
        HitTarget = 1,
    }

    /// <summary> The original ballistic trajectory. Bullets, arrows, thrown axes, fireballs </summary>
    //If this type is ever renamed or moved to another namespace, add [MovedFrom] so existing
    //[SerializeReference] data keeps resolving. SerializeReference stores the assembly-qualified type name.
    [System.Serializable, AddTypeMenu("Flying")]
    public class FlyingMovement : ProjectileMovement
    {
        public override string DynamicName => "Flying";

        //MWC: Projectile Rotation - the rotation moved to the shared [rotationBehaviour] on MProjectile.
        //These stay serialized (hidden) so MigrateRotation can carry an existing asset's values across
        [SerializeField, HideInInspector, FormerlySerializedAs("rotation")]
        private ProjectileRotation m_legacyRotation = ProjectileRotation.None;
        [SerializeField, HideInInspector, FormerlySerializedAs("torque")]
        private FloatReference m_legacyTorque = new(50f);
        [SerializeField, HideInInspector, FormerlySerializedAs("torqueAxis")]
        private Vector3 m_legacyTorqueAxis = Vector3.up;
        [SerializeField, HideInInspector, FormerlySerializedAs("axisFollowTrajectory")]
        private bool m_legacyAxisFollowTrajectory = true;
        [SerializeField, HideInInspector, FormerlySerializedAs("trajectoryRoll")]
        private float m_legacyTrajectoryRoll = 0;

        /// <summary> Projectile Rotation: bakes this movement's legacy rotation fields into a shared behaviour </summary>
        public override bool TryExtractLegacyRotation(out ProjectileRotationBehaviour rotation)
        {
            rotation = null;
            if (m_legacyRotation == ProjectileRotation.None) return false;

            rotation = ProjectileRotationBehaviour.FromLegacy(m_legacyRotation,
                m_legacyTorque, m_legacyTorqueAxis, m_legacyTrajectoryRoll, m_legacyAxisFollowTrajectory);

            m_legacyRotation = ProjectileRotation.None; //Consumed: never refire over a user-emptied rotation
            return true;
        }

        [SerializeField, Tooltip("Gravity applied to the projectile while it flies. Zero makes it travel in a straight line.\nLaunchers read this to aim, so the predicted arc always matches the real one")]
        private Vector3Reference m_gravity = new(Physics.gravity);

        /// <summary> Gravity this projectile flies under. Owned here: Rolling and Homing never used it </summary>
        public Vector3 Gravity { get => m_gravity.Value; set => m_gravity.Value = value; }

        [SerializeField, Tooltip("Apply gravity only after this distance is travelled. Set 0 to apply gravity from the start")]
        private FloatReference m_afterDistance = new(0f);

        /// <summary> Distance the projectile travels before gravity starts applying. Owned here, nowhere else </summary>
        public float AfterDistance { get => m_afterDistance.Value; set => m_afterDistance.Value = value; }

        //MWC: Launch Angle - moved here from the Projectile processor. Only ballistic flight lobs, so it lives with the gravity
        [SerializeField, Range(0, 90)]
        [Tooltip("Launch angle launchers use to LOB this projectile at the Target, or at the point the Aimer is touching. See [Launch Angle Mode].\nSet 0 to disable the arc aiming and fire straight")]
        private float m_launchAngle = 0;

        /// <summary> Launch angle launchers use for the arc aiming. 0 disables it </summary>
        public float LaunchAngle { get => m_launchAngle; set => m_launchAngle = value; }

        [SerializeField]
        [Tooltip("Tilt Aim: keeps the Force and lifts the aim direction by the Launch Angle (MShootable's Aim Angle). The shot flies a higher arc but does not land exactly on the point.\n" +
            "Hit Target: computes the Force needed to land the arc on the Target or aim point instead. The Force changes with the distance. Needs a non-zero Gravity")]
        private LaunchAngleMode m_launchAngleMode = LaunchAngleMode.TiltAim;

        /// <summary> What launchers do with the Launch Angle: land on the point with a computed Force, or keep the Force and tilt the aim </summary>
        public LaunchAngleMode LaunchAngleMode { get => m_launchAngleMode; set => m_launchAngleMode = value; }

        //MWC: Time Multiplier - moved to the ProjectileMovement base, so Rolling and Homing have it too

        //MWC: Projectile Rotation - [FollowTrajectory] removed. MProjectile reads its own [rotationBehaviour] now

        /// <summary> Gravity and AfterDistance stay settable at runtime, so a shallow copy would let one
        /// projectile's values leak into every other copy cloned from the same override </summary>
        public override ProjectileMovement Clone()
        {
            var copy = (FlyingMovement)base.Clone();

            copy.m_afterDistance = new FloatReference
            {
                UseConstant = m_afterDistance.UseConstant,
                ConstantValue = m_afterDistance.ConstantValue,
                Variable = m_afterDistance.Variable,
            };

            copy.m_gravity = new Vector3Reference
            {
                UseConstant = m_gravity.UseConstant,
                ConstantValue = m_gravity.ConstantValue,
                Variable = m_gravity.Variable,
            };

            return copy; //MWC: the base Clone already deep-copied the shared Time Multiplier
        }

        public override void Fire(MProjectile p)
        {
            if (p.rb == null) return;

            //MWC: the trajectory maths drive position and rotation every FixedUpdate, so the body is kinematic
            //and gravity comes from the maths, not from physics. It used to be launched dynamic with
            //AddForce + AddTorque and then fought by MovePosition every step: the force was overwritten
            //immediately, and the torque only span the Rigidbody path, never the Transform one
            MakeKinematic(p.rb);
        }

        //MWC: Projectile Rotation - [ApplyRotation] and its spin state moved into the shared ProjectileRotationBehaviour

        public override IEnumerator Movement(MProjectile p)
        {
            var T = p.Trans;

            Vector3 start = T.position;
            p.Prev_Pos = start;

            p.Direction_ = p.Velocity.normalized;

            //MWC: a STABLE up reference for the rotation, resolved once. This used to pass the projectile's own
            //[T.up], which fed the previous step's rotation straight back in: the roll compounded every step
            //(it accelerated instead of spinning steadily) and an Axis spin drifted off its authored plane.
            //Opposite gravity is the natural "up" of a ballistic arc, and equals Vector3.up under normal gravity
            var upRef = Gravity.sqrMagnitude > 0.0001f ? -Gravity.normalized : Vector3.up;

            //MWC: Time Multiplier - time is ACCUMULATED now instead of [deltatime * step], so the multiplier can be
            //a Float Var that changes mid-flight (slow motion, time stop) without the projectile jumping along its arc
            float time = 0;
            float GravityTime = 0;

            float TraveledDistance = 0;

            var rb = p.rb;

            while (!p.HasImpacted && p.enabled)
            {
                //MWC: the scaled step. Everything below advances by this, so the whole flight slows down or speeds
                //up together. Scaling TIME (not the velocity) keeps the arc shape and the landing point identical,
                //which is what lets the predicted trajectory line still match the real flight
                var scaledDelta = ScaledDelta;

                time += scaledDelta;
                GravityTime += scaledDelta; //Taken back below while the projectile is still inside [AfterDistance]

                Vector3 next_pos = (start + p.Velocity * time) + (GravityTime * GravityTime * Gravity / 2);

                //Position: the Rigidbody path only differs in how the move is applied
                if (rb) rb.MovePosition(p.Prev_Pos);
                else T.position = p.Prev_Pos;

                p.Direction_ = next_pos - p.Prev_Pos;

                //MWC: the push applied to whatever this projectile hits reads [NormalizedDirection]. Flying was the
                //only movement that never filled it in (Rolling and Homing both do), so every arrow, bullet and
                //thrown axe reached its impact with a ZERO direction and shoved its target with a zero force vector
                p.NormalizedDirection = p.Direction_.normalized;

                //MWC: Projectile Rotation - the shared behaviour orients it, against the stable up resolved above.
                //Fed the SCALED step, so a slowed projectile also spins slowly instead of tearing itself apart
                p.ApplyRotation(p.Direction_, upRef, scaledDelta);

                //MWC: gated on [debug] now. This drew a line - and two wire spheres - for EVERY flying projectile on
                //EVERY FixedUpdate whether debugging was on or not. Rolling and Homing both gate theirs
                if (p.debug)
                {
                    Debug.DrawLine(p.Prev_Pos, next_pos, Color.yellow);
                    MDebug.DrawWireSphere(p.Prev_Pos, Color.yellow, p.SweepRadius);
                    MDebug.DrawWireSphere(next_pos, Color.yellow, p.SweepRadius);
                }

                var Length = Vector3.Distance(next_pos, p.Prev_Pos);

                //MWC: [SweepRadius] floors the radius, so a Radius of 0 does not degrade this into a bare raycast
                if (Physics.SphereCast(p.Prev_Pos, p.SweepRadius, p.Direction_, out RaycastHit hit, Length, p.Layer, p.TriggerInteraction))
                {
                    if (!p.IsInvalid(hit.collider))
                    {
                        //Deliberate one frame wait: the impact is processed on the FixedUpdate after the cast found it
                        yield return p.WaitFixed;

                        p.OnRayCastHit.Invoke(hit);

                        p.ProjectileImpact(hit.rigidbody, hit.collider, hit.point, hit.normal);
                        yield break;
                    }
                }

                //Check if the gravity can be applied after distance
                if (TraveledDistance < AfterDistance)
                {
                    TraveledDistance += p.Direction_.magnitude;
                    GravityTime -= scaledDelta; //MWC: no gravity yet, so this step never counted toward the fall
                }

                p.Prev_Pos = next_pos;

                yield return p.WaitFixed;
            }

            yield return null;
        }
    }
}
