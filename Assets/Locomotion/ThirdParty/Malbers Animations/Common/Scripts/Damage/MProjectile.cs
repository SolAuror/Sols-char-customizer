// Last Change: 10/8/2026 11:36am
using MalbersAnimations.Scriptables;
using System.Collections.Generic; //MWC: List used to collect projectiles stuck to this one before unparenting them
using UnityEngine;
using UnityEngine.Events;
using MalbersAnimations.Controller;
using UnityEngine.Pool;
using UnityEngine.Serialization; //MWC: Projectile Movement - FormerlySerializedAs keeps the legacy [projectileType] YAML key readable
using MalbersAnimations.Events;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Weapons
{
    /// <summary> Legacy rotation type. Kept only so pre-split assets can be migrated to a [ProjectileRotationBehaviour] </summary>
    public enum ProjectileRotation { None, FollowTrajectory, Random, Axis };

    /// <summary>  Pool - Added DisableOnImpact for ProjectilePool </summary>

    //MWC: removed StickOnHitSurface; StickOnSurface keeps the bone-accurate closest-transform path (kept enum order so existing serialized values stay valid)
    /// <summary> Legacy impact type. Kept only so pre-split assets can be migrated to a [ProjectileImpactBehaviour] </summary>
    public enum ImpactBehaviour { None, StickOnSurface, DestroyOnImpact, ActivateRigidBody, DisableOnImpact };
    /// <summary> Pool - End </summary>

    /// <summary> Legacy movement type. Kept only so pre-split assets can be migrated to a [ProjectileMovement] </summary>
    public enum ProjectileType { FlyingProjectile, RollingProjectile };

    /// <summary> How a [ProjectileImpact] call must be treated </summary>
    public enum ProjectileImpactKind
    {
        /// <summary> A real collision. Latches HasImpacted, stops the movement and runs the Impact Behaviour </summary>
        Impact,
        /// <summary> A Rolling Projectile passing through a target. Applies damage/effects and fires OnHit, but keeps travelling </summary>
        Pierce
        //MWC: [Particle] removed with MMagicParticleTriggers, the only thing that ever raised it
    }


    [AddComponentMenu("Malbers/Damage/Projectile")]
    [SelectionBase]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/secondary-components/mdamager/mprojectile")]
    public class MProjectile : MDamager, IProjectile
    {
        //MWC: OnImpact Behaviour - what happens on a real impact is a pluggable strategy (Stop / Stick / Destroy / Ricochet / your own)
        [SerializeReference, SubclassSelector]
        [Tooltip("What happens when the projectile impacts something.\nDestroy will not use the ProjectilePool!")]
        public ProjectileImpactBehaviour onImpact;
        //NO field initializer on purpose. null is the "this asset predates the impact split" signal that
        //MigrateImpact() keys off. A default here would silently swallow the legacy [impactBehaviour] value.

        //MWC: Legacy storage. Stays serialized (hidden) so MigrateImpact can carry an existing prefab's value across
        [SerializeField, HideInInspector, FormerlySerializedAs("impactBehaviour")]
        private ImpactBehaviour m_legacyImpactBehaviour = ImpactBehaviour.None;


        //MWC: Projectile Movement - the travel behaviour is a pluggable strategy (Flying / Rolling / Homing / your own) ----
        [SerializeReference, SubclassSelector]
        [Tooltip("How this projectile travels. Flying is the ballistic trajectory, Rolling is a ground sweep, Homing chases a target")]
        public ProjectileMovement movement;

        //MWC: Projectile Rotation - shared by every movement. The movement supplies the direction and up hints
        [SerializeReference, SubclassSelector]
        [Tooltip("Rotation applied while the projectile travels, whatever its Movement is. Empty keeps the rotation it was fired with")]
        public ProjectileRotationBehaviour rotationBehaviour;
        //NO field initializer on purpose. null is the "this asset predates the movement split" signal that
        //MigrateMovement() keys off. A default here would silently swallow the legacy [m_projectileType] value.

        //Legacy storage. These fields moved into the movement classes. They stay serialized (hidden) so
        //MigrateMovement can carry an existing prefab's values across, and can be deleted a version later
        [SerializeField, HideInInspector, FormerlySerializedAs("projectileType")]
        private ProjectileType m_projectileType = ProjectileType.FlyingProjectile;
        [SerializeField, HideInInspector, FormerlySerializedAs("rotation")]
        private ProjectileRotation m_legacyRotation = ProjectileRotation.None;
        [SerializeField, HideInInspector, FormerlySerializedAs("torque")]
        private FloatReference m_legacyTorque = new(50f);
        [SerializeField, HideInInspector, FormerlySerializedAs("torqueAxis")]
        private Vector3 m_legacyTorqueAxis = Vector3.up;
        [SerializeField, HideInInspector, FormerlySerializedAs("TrajectoryRoll")]
        private float m_legacyTrajectoryRoll = 0;
        //MWC: [TrajectoryRollOffset] was deleted, not migrated. Nothing ever read it, in the old code or the new
        [SerializeField, HideInInspector, FormerlySerializedAs("m_AfterDistance")]
        private FloatReference m_legacyAfterDistance = new(0f);


        /// <summary> Legacy shim so user scripts written against the old enum still compile </summary>
        [System.Obsolete("Use the [onImpact] field. This only reports which impact behaviour is assigned.")]
        public ImpactBehaviour impactBehaviour => onImpact switch
        {
            StickImpact => ImpactBehaviour.StickOnSurface,
            DestroyImpact => ImpactBehaviour.DestroyOnImpact,
            ActivateRigidbodyImpact => ImpactBehaviour.ActivateRigidBody,
            DisableImpact => ImpactBehaviour.DisableOnImpact,
            _ => ImpactBehaviour.None,
        };


        /// <summary> Legacy shim so user scripts written against the old enum still compile </summary>
        [System.Obsolete("Use the [movement] field. This only reports which movement type is assigned.")]
        public ProjectileType projectileType => movement is RollingMovement ? ProjectileType.RollingProjectile : ProjectileType.FlyingProjectile;

        /// <summary> Legacy shim. The rotation is a [ProjectileRotationBehaviour] on [rotationBehaviour] now </summary>
        [System.Obsolete("Use the [rotationBehaviour] field. This only reports which rotation behaviour is assigned.")]
        public ProjectileRotation rotation => rotationBehaviour switch
        {
            FollowTrajectoryRotation => ProjectileRotation.FollowTrajectory,
            RandomRotation => ProjectileRotation.Random,
            AxisRotation => ProjectileRotation.Axis,
            _ => ProjectileRotation.None,
        };

        /// <summary> Legacy shim. The rotation is a [ProjectileRotationBehaviour] on [rotationBehaviour] now </summary>
        [System.Obsolete("Moved to RandomRotation.torque / AxisRotation.torque on the [rotationBehaviour] field")]
        public float torque => rotationBehaviour is RandomRotation rr ? rr.torque.Value
            : rotationBehaviour is AxisRotation ar ? ar.torque.Value : 0;

        /// <summary> Legacy shim. The rotation is a [ProjectileRotationBehaviour] on [rotationBehaviour] now </summary>
        [System.Obsolete("Moved to AxisRotation.torqueAxis on the [rotationBehaviour] field")]
        public Vector3 torqueAxis => rotationBehaviour is AxisRotation ar ? ar.torqueAxis : Vector3.up;

        /// <summary> Legacy shim. The rotation is a [ProjectileRotationBehaviour] on [rotationBehaviour] now </summary>
        [System.Obsolete("Moved to FollowTrajectoryRotation.roll on the [rotationBehaviour] field")]
        public float TrajectoryRoll => rotationBehaviour is FollowTrajectoryRotation ft ? ft.roll : 0;

        /// <summary> Set by a movement while routing a hit that matched the Tags filter, so the events go to OnHitTag instead of OnHit </summary>
        public bool RollingHitWithTag { get; set; }
        //MWC: Projectile Movement - End ------------------------------------------------------------------------------

        //MWC: [Penetration] moved to StickImpact, next to the behaviour that uses it. Kept serialized (hidden)
        //so Migrate() can carry an authored value across, then it is consumed for good
        [SerializeField, HideInInspector, FormerlySerializedAs("Penetration")]
        private float m_legacyPenetration = 0.1f;

        /// <summary> Legacy shim. The penetration depth lives on the Stick On Surface impact behaviour now </summary>
        [System.Obsolete("Moved to StickImpact.penetration on the [onImpact] field")]
        public float Penetration => onImpact is StickImpact s ? s.penetration.Value : Mathf.Max(0, m_legacyPenetration);

        [SerializeField, Tooltip("Keep Projectile Damage and Layer Values, The Throwable-Shootable component will not override the Damage Values")]
        protected BoolReference m_KeepDamageValues = new(false);

        //MWC: [gravity] moved to FlyingMovement. Rolling and Homing never read it, and the launchers now take it
        //from the projectile so their aiming maths and the predicted arc match the real flight
        [SerializeField, HideInInspector, FormerlySerializedAs("gravity")]
        private Vector3Reference m_legacyGravity = new(Physics.gravity);

        //MWC: [AfterDistance] lives on FlyingMovement only. It gates when gravity starts applying, which is
        //meaningless to a ground sweep or a homing missile, so neither MProjectile nor IProjectile carries it

        /// <summary> After Distance this projectile will actually use. 0 when it does not fly ballistically.
        /// Read-only and non-mutating, so a trajectory preview can call it on a prefab asset safely </summary>
        public float FlyingAfterDistance
        {
            get
            {
                if (movement is FlyingMovement f) return f.AfterDistance;

                //A pre-split asset that has not been migrated yet still has its value in the legacy field
                if (movement == null && m_projectileType == ProjectileType.FlyingProjectile)
                    return m_legacyAfterDistance.Value;

                return 0;
            }
        }

        /// <summary> Launch Angle of the Flying movement, or 0 when this projectile does not fly ballistically.
        /// Read-only and non-mutating, so a launcher can call it on a prefab asset while aiming </summary>
        public float FlyingLaunchAngle => movement is FlyingMovement f ? f.LaunchAngle : 0;

        /// <summary> What launchers do with the Flying movement's Launch Angle. Tilt Aim when this projectile does not fly ballistically. Non-mutating, safe on a prefab asset </summary>
        public LaunchAngleMode FlyingLaunchAngleMode => movement is FlyingMovement f ? f.LaunchAngleMode : LaunchAngleMode.TiltAim;

        [Tooltip("Life of the Projectile on the air, if it has not touch anything on this time it will destroy it self")]
        public FloatReference Life = new(10f);
        [Tooltip("Life of the Projectile After Impact. If the projectile is not destroyed on impact, then wait this time to do it. (0 -> Ignores it) ")]
        public FloatReference LifeImpact = new(0f);

        [Tooltip("Multiplier of the Force to Apply to the object the projectile impact ")]
        public FloatReference PushMultiplier = new(1);

        [Tooltip("Offset to position the projectile when is Instantiated on the weapon. E.g. (Arrow in the Bow) ")]
        public Vector3 m_PosOffset;

        [Tooltip("Offset to rotation the projectile when is Instantiated on the weapon. E.g. (Arrow in the Bow) ")]
        public Vector3 m_RotOffset;

        [Tooltip("Offset to scale the projectile when is Instantiated on the weapon. E.g. (Arrow in the Bow) ")]
        public Vector3 m_ScaleOffset;

        //MWC: [useRadius] removed. Nothing ever read it - every movement sweeps with [Radius] unconditionally - so
        //the checkbox did nothing at all. Honouring it instead would have silently switched every existing
        //projectile to a bare raycast, since it defaulted to false and nobody could have set it meaningfully
        [Tooltip("Radius of the projectile to cast a ray to find targets better")]
        public FloatReference Radius = new(0.01f);

        /// <summary> Radius the movements sweep with. Floored, so a Radius of 0 cannot silently turn the sweep
        /// into a bare raycast and let a thin fast projectile slip past an edge. Homing and Ricochet already clamped
        /// this locally and Flying did not, which is exactly the inconsistency this removes </summary>
        public float SweepRadius => Mathf.Max(0.01f, Radius.Value);

        public UnityEvent OnFire = new();                       //Send the transform to the event

        //MWC: Rolling Projectile events. The two "WithoutImpact" ones went with the Particle impact kind, since
        //nothing raised them once MMagicParticleTriggers was removed ------------------------------------------
        [Tooltip("Triggered when the projectile main collider hits a Tagged gameObject (Rolling Projectile)")]
        public TransformEvent OnHitTag = new();
        [Tooltip("Triggered when a Rolling Projectile reaches max travel distance (True); deflection or slope angle sends False")]
        public BoolEvent OnReachedMaxDistance = new();
        //MWC: Rolling Projectile - End events -----------------------------------------------------------------

        [Tooltip("Reference for the Projectile Rigidbody")]
        public Rigidbody rb;
        [Tooltip("Reference for the Projectile collider")]
        public Collider m_collider;

        [Tooltip("Reference for the trail renderer")]
        public TrailRenderer m_trail;

        protected Vector3 Prev_pos;

        protected Transform T;

        #region Properties
        /// <summary>Initial Velocity (Direction * Power) </summary>
        public Vector3 Velocity { get; set; }

        /// <summary>Has the projectile impacted with something</summary>
        public bool HasImpacted { get; set; }

        /// <summary>Do Fly Raycast</summary>
        protected bool doRayCast;

        /// <summary>Is the Projectile Flying</summary>
        public bool IsFlying { get; set; }


        /// <summary> Forwards to the Flying movement, which owns it. Required by IProjectile and set by Prepare </summary>
        public Vector3 Gravity
        {
            get => movement is FlyingMovement f ? f.Gravity : Vector3.zero;
            set
            {
                MigrateMovement(); //Prepare can run before anything else has built the movement
                if (movement is FlyingMovement f) f.Gravity = value;
            }
        }

        /// <summary> Gravity this projectile will actually fly under, or zero when it does not fly ballistically.
        /// Read-only and non-mutating, so a launcher can call it on a prefab asset while aiming </summary>
        public Vector3 FlyingGravity
        {
            get
            {
                if (movement is FlyingMovement f) return f.Gravity;

                //A pre-split asset that has not been migrated yet still has its value in the legacy field
                if (movement == null && m_projectileType == ProjectileType.FlyingProjectile)
                    return m_legacyGravity.Value;

                return Vector3.zero;
            }
        }

        /// <summary> Gravity the given projectile prefab will fly under. Physics.gravity when it is not an
        /// MProjectile at all, since a plain Rigidbody projectile does fall under real gravity </summary>
        public static Vector3 GravityOf(GameObject projectile)
            => projectile != null && projectile.TryGetComponent<MProjectile>(out var p) ? p.FlyingGravity : Physics.gravity;

        public bool KeepValues { get => m_KeepDamageValues.Value; set => m_KeepDamageValues.Value = value; }

        public Vector3 TargetHitPosition { get; set; }
        /// <summary> True when the rotation behaviour turns the projectile to face its trajectory </summary>
        public bool FollowTrajectory => rotationBehaviour is FollowTrajectoryRotation; //MWC: Projectile Rotation - was on FlyingMovement
        /// <summary> True when the assigned impact behaviour destroys the projectile instead of pooling it </summary>
        public bool DestroyOnImpact => onImpact != null ? onImpact.DestroyAtLifeEnd
            : m_legacyImpactBehaviour == ImpactBehaviour.DestroyOnImpact; //A pre-split asset that has not been migrated yet

        /// <summary> True when this projectile travels as a kinematic ground sweep instead of a ballistic trajectory </summary>
        public bool IsRolling => movement is RollingMovement;

        public Vector3 PosOffset { get => m_PosOffset; set => m_PosOffset = value; }
        public Vector3 RotOffset { get => m_RotOffset; set => m_RotOffset = value; }
        // public Vector3 ScaleOffset { get => m_ScaleOffset; set => m_ScaleOffset = value; }
        #endregion

        public RayCastHitEvent OnRayCastHit = new();

        /// <summary> Pool - End  </summary>  
        public IObjectPool<GameObject> Pool { get; set; }
        [HideInInspector] public int Editor_Tabs1;

        private bool colliderInitialEnable;

        /// <summary> Every collider on this projectile, cached at Awake. A projectile can carry several (E.g. an
        /// axe with one box for the head and one for the handle), so anything that stops it colliding must cover them all </summary>
        private Collider[] m_allColliders;
        private bool[] m_allCollidersEnabled;

        protected virtual void Awake()
        {
            T = transform;

            //MWC: safety net for assets that were never opened in the editor after the updates (movement, impact, rotation)
            Migrate();

            if (!rb) TryGetComponent(out rb);
            if (!m_collider) m_collider = GetComponentInChildren<Collider>();

            //MWC: Stick On Surface destroys the body, so its authored settings are captured here while they are intact
            StoreRigidBodyState();

            //MWC: this used to live inside the [if (!m_collider)] block, so a collider wired in the inspector left
            //[colliderInitialEnable] false and OnDisable killed the collider on every pooled reuse
            colliderInitialEnable = m_collider != null && m_collider.enabled;

            //MWC: [m_collider] is only ever the FIRST collider found, so the full set is cached for Collisions_Disable
            m_allColliders = GetComponentsInChildren<Collider>(true);
            m_allCollidersEnabled = new bool[m_allColliders.Length];
            for (int i = 0; i < m_allColliders.Length; i++) m_allCollidersEnabled[i] = m_allColliders[i].enabled;

            m_audio = gameObject.GetOrAddComponent<AudioSource>(); //Gets|Add the AudioSource
            m_audio.spatialBlend = 1;
            m_audio.maxDistance = 50;

            //MWC: OnImpact Behaviour - the Stick link is created lazily in Stick_On_Surface now. Awake can no longer
            //know whether sticking will happen: a Ricochet can end in a Stick On Surface as its Final Impact
        }


        protected virtual void OnDisable()
        {
            StopAllCoroutines();
            CancelInvoke();

            if (m_collider && (onImpact == null || onImpact.RestoreColliderOnDisable)) //MWC: OnImpact Behaviour decides
                m_collider.enabled = colliderInitialEnable; //The pool might enable or disable the collider
        }

        /// <summary> Initialize the Projectile main references and parameters</summary>
        protected virtual void Initialize()
        {
            HasImpacted = false;

            //MWC: Projectile Movement - Rolling owns its own Life/MaxDistance timers.
            //Scheduling this one too gave two competing destroy paths racing on the same [Life] value
            if (movement is RollingMovement) return;

            if (Life > 0f)
                this.Delay_Action(Life, () => DestroyProjectile());
        }

        /// <summary>
        /// Returns the last calculated normalized direction of the projectile. (updated while flying)
        /// </summary>
        public Vector3 NormalizedDirection { get; set; }
        /// <summary> Thrower that fired this projectile (null when pushed or shot by a component that does not set it) </summary>
        public IThrower Thrower { get; internal set; }

        /// <summary> Prepare the Projectile for firing </summary>
        public virtual void Prepare(GameObject Owner, Vector3 Gravity,
            Vector3 ProjectileVelocity, LayerMask HitLayer, QueryTriggerInteraction triggerInteraction, IObjectPool<GameObject> thisProjectilePool)
        {
            if (!KeepValues)
            {
                this.Layer = HitLayer;
                this.TriggerInteraction = triggerInteraction;
            }

            this.UserGo = Owner;
            this.Gravity = Gravity;
            this.Velocity = ProjectileVelocity;
            this.MinForce = this.MaxForce = Velocity.magnitude; //CustomPatch: removed extra redundant magnitude calculation
            /// <summary> Pool - Set thisProjectilePool  </summary>
            this.Pool = thisProjectilePool;
            /// <summary> Pool - End </summary>  
            Debugging("Projectile Prepared", this);
        }

        public virtual void Fire(Vector3 ProjectileVelocity)
        {
            this.Velocity = ProjectileVelocity;
            this.MinForce = this.MaxForce = Velocity.magnitude; //CustomPatch: removed extra redundant magnitude calculation
            Fire();
        }

        public virtual void Fire()
        {
            RestoreRigidBody();   //MWC: Stick On Surface destroys the body, and a reused projectile must fly with it back
            Collisions_Restore(); //MWC: a pooled projectile may have had its collisions switched off when it stuck
            //MWC: must run before Initialize(), which asks the movement whether to schedule the Life timer
            Migrate();
            onImpact?.Fire(this);          //MWC: OnImpact Behaviour - reset its per-shot state (Ricochet bounces, hit lists...)
            rotationBehaviour?.Fire(this); //MWC: Projectile Rotation - capture the launch rotation, reset the spin

            Initialize();

            if (m_trail != null) m_trail.Clear(); //Clear the Trail when the Projectile is Fired

            gameObject.SetActive(true); //Just to make sure is working  
            Active = true;

            if (Velocity == Vector3.zero) //Hack when the Velocity is not set
            {
                Velocity = T.forward;
                this.MaxForce = 1;
                this.MinForce = 1;
            }

            doRayCast = true;

            //MWC: the ballistic rigidbody launch moved into FlyingMovement.Fire, along with the torque parameters it uses.
            //A movement that drives the transform itself (Rolling, Homing) sets up its own body the same way
            if (m_collider && movement is FlyingMovement) //MWC: no longer gated on [rb]; the Rigidbody is optional
            {
                //MWC: the collider has to be OFF for the delay below to mean anything. EnableCollider only ever
                //enables, so a pooled projectile came back with its collider already live and could hit whatever
                //was sitting at the spawn point on frame one, before it had travelled at all
                if (!m_collider.isTrigger) m_collider.enabled = false;

                EnableCollider(0.1f); //Don't enable it right away so it does not collide with the thrower
                doRayCast = m_collider.isTrigger;
            }

            movement.Fire(this);
            TeleportBody(); //After movement.Fire: it may have just switched interpolation on
            StartCoroutine(movement.Movement(this)); //All travel logic lives in the ProjectileMovement


            OnFire.Invoke();

            //if (TryGetComponent<ICollectable>(out var pickable))
            //{
            //    pickable.Drop(); //if the Projectile is a pickable then drop it?
            //}

            Debugging("Projectile Fired", this);
        }

        /// <summary> Moves the Rigidbody onto the launch pose and holds its interpolation off for one physics step </summary>
        //A pooled projectile is placed through its Transform, but the body still holds the physics pose of its LAST
        //flight (or of the spot it was parked at). With interpolation on, Unity blends from that old pose to the first
        //new one and writes the result over the Transform: a one frame jump far from the launcher and a very long trail.
        //Teleporting the body alone is not enough, the interpolation history still holds the old pose until a step runs
        protected virtual void TeleportBody()
        {
            if (rb == null) return;

            var wanted = rb.interpolation;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.position = T.position;
            rb.rotation = T.rotation;

            if (wanted != RigidbodyInterpolation.None)
                StartCoroutine(RestoreInterpolation(rb, wanted));
        }

        /// <summary> Gives the interpolation back once a physics step has run with the body on its launch pose </summary>
        private System.Collections.IEnumerator RestoreInterpolation(Rigidbody body, RigidbodyInterpolation wanted)
        {
            yield return waitForFixedUpdate;

            //Skipped when the body was swapped (Stick On Surface), or something parked it again in the meantime
            if (rb == body && body.interpolation == RigidbodyInterpolation.None && !HasImpacted)
                body.interpolation = wanted;
        }

        public void EnableCollider(float time) => this.Delay_Action(time, Enable_Collider); //MWC: replaced reflection-based Invoke with the project's coroutine-based Delay_Action

        protected virtual void Enable_Collider()
        {
            if (m_collider && !m_collider.isTrigger)
                m_collider.enabled = true;
        }

        //MWC: Attached projectiles ------------------------------------------------------------------------------
        /// <summary> Scratch list for [Release_AttachedProjectiles]. The hierarchy is mutated while unparenting,
        /// so the matches are collected first instead of reparenting mid-walk </summary>
        //MWC: per instance, not static. Releasing walks the list while calling Parent_Lost on each entry, and a
        //static list means any re-entrant release from inside that loop clears the collection being iterated
        private readonly List<Transform> AttachedProjectiles = new();

        /// <summary> Unparents any projectile stuck to this one before it leaves the scene or returns to the
        /// pool. Stops at the FIRST MProjectile down each branch, so a projectile stuck to THAT one stays attached
        /// to it. Note they are not direct children: Stick_On_Surface inserts a [Link] object in between </summary>
        protected virtual void Release_AttachedProjectiles()
        {
            if (!Application.isPlaying) return; //Scene teardown reparents nothing

            AttachedProjectiles.Clear();
            Collect_AttachedProjectiles(T);

            //MWC: while stuck, this projectile hangs UNDER its link, so anything stuck to the LINK (a projectile
            //that hit near the same point) is not in T's subtree. It was being smuggled into the pool when the
            //link was reclaimed below in DestroyProjectile, and re-fired later still attached (the double arrow)
            if (LinkStickOnSurface != null && !LinkStickOnSurface.transform.IsChildOf(T))
                Collect_AttachedProjectiles(LinkStickOnSurface.transform);

            foreach (var attached in AttachedProjectiles)
            {
                attached.SetParent(null, true); //Keep it exactly where it is in the world

                //MWC: whatever it was stuck to has gone, so it would hang in mid air. Hand it back to physics
                if (attached.TryGetComponent<MProjectile>(out var released)) released.Parent_Lost();

                Debugging($"Released [{attached.name}], it was stuck to this one", this);
            }

            AttachedProjectiles.Clear();
        }

        /// <summary> Walks down and takes the nearest MProjectile on each branch, never descending past one </summary>
        private void Collect_AttachedProjectiles(Transform parent) //MWC: instance method now, it must recognize its own transform
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);

                if (child == T) continue; //MWC: the link's subtree contains this projectile itself while stuck; never self-release

                if (child.TryGetComponent<MProjectile>(out _))
                    AttachedProjectiles.Add(child); //Found one: stop here, keep whatever is stuck to it
                else
                    Collect_AttachedProjectiles(child); //A [Link] or a mesh, keep looking
            }
        }
        //MWC: Attached projectiles - End -------------------------------------------------------------------------

        /// <summary> / Destroy Projectile on Life Time End  </summary>
        protected virtual void DestroyProjectile()
        {
            HasImpacted = false;

            //MWC: whatever is stuck to this projectile must not be dragged into the pool, or destroyed with it
            Release_AttachedProjectiles();
            if (onImpact != null && onImpact.DestroyAtLifeEnd) //MWC: OnImpact Behaviour decides
            {
                Debugging($"Life time elapsed [{Life}]. Destroy Projectile", null);
                Destroy(gameObject);
                if (LinkStickOnSurface) Destroy(LinkStickOnSurface);
            }
            /// <summary> Pool - Leave option to destroy and for everything else use Pool  </summary>
            else
            {
                Debugging($"Life time elapsed [{LifeImpact.Value}].[Projectile returned to the Pool]", null);

                Reclaim_StickLink(); //If the Projectile is sticking on the surface then remove the parent

                if (m_trail != null) m_trail.Clear(); //Clear the Trail when the Projectile is Destroyed or Returned to the Pool

                StopAllCoroutines();

                RestoreRigidBody(); //MWC: it goes back to the pool as the prefab authored it, body included

                //MWC: without a Pool this was [Pool?.Release] and did NOTHING: the projectile stayed in the scene,
                //still enabled, still Active and with its collider live, so it kept damaging whatever walked into it.
                //Pool_Release always had this fallback, this path did not
                if (Pool != null) Pool.Release(gameObject);
                else Destroy(gameObject);
            }
        }

        /// <summary> Takes the stick [Link] back out of whatever this projectile was stuck into and parks it
        /// under the projectile again. EVERY path that removes the projectile has to do this: the Link is reused for
        /// the projectile's whole life, so leaving it behind abandons an empty [Link ...] object on the old target </summary>
        protected virtual void Reclaim_StickLink()
        {
            if (LinkStickOnSurface == null) return;
            if (LinkStickOnSurface.transform.IsChildOf(T)) return; //Already parked under us, it never stuck to anything

            T.SetParent(null, true);                               //Out of the Link, so it does not travel with us
            LinkStickOnSurface.transform.SetParent(T);
        }

        public virtual void Pool_Release(GameObject gameObject)
        {
            Release_AttachedProjectiles(); //MWC: never take a stuck projectile into the pool with us
            Reclaim_StickLink();           //MWC: nor leave our Link behind on whatever we were stuck into
            RestoreRigidBody();            //MWC: it goes back to the pool as the prefab authored it, body included

            if (Pool != null)
            {
                Pool.Release(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public virtual void ReturnToPool()
        {
            Release_AttachedProjectiles(); //MWC: never take a stuck projectile into the pool with us
            Reclaim_StickLink();           //MWC: nor leave our Link behind on whatever we were stuck into
            RestoreRigidBody();            //MWC: it goes back to the pool as the prefab authored it, body included
            Pool.Release(gameObject);
        }

        public virtual GameObject Pool_Get() => Pool.Get();


        protected virtual void OnCollisionEnter(UnityEngine.Collision other)
        {
            if (rb && rb.isKinematic) return;
            //MWC: Projectile Movement - a movement that finds its own targets (Rolling) would double-fire here
            if (movement != null && (movement.IgnorePhysicsCallbacks || !movement.IsImpactValid(this, other.collider))) return;
            if (HasImpacted) return; //Do not check new Collisions
            if (IsInvalid(other.collider)) return;
            if (!enabled) return;

            if (Prev_pos == Vector3.zero) Prev_pos = T.position;

            ProjectileImpact(other.rigidbody, other.collider, other.contacts[0].point, (other.collider.bounds.center - m_collider.transform.position).normalized);

        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            //MWC: Projectile Movement - a movement that finds its own targets (Rolling) would double-fire here
            if (movement != null && (movement.IgnorePhysicsCallbacks || !movement.IsImpactValid(this, other))) return;
            if (HasImpacted) return; //Do not check new Collisions
            if (IsInvalid(other)) return;
            if (!enabled) return;

            if (Prev_pos == Vector3.zero) Prev_pos = T.position;

            ProjectileImpact(other.attachedRigidbody, other, Prev_pos, (other.bounds.center - m_collider.transform.position).normalized);
        }

        private readonly WaitForFixedUpdate waitForFixedUpdate = new();

        //MWC: Projectile Movement - the surface a ProjectileMovement needs to drive the projectile -------------------
        #region Movement API

        /// <summary> Cached transform. Movements drive this instead of calling transform every step </summary>
        public Transform Trans => T;

        /// <summary> Shared WaitForFixedUpdate, so a movement coroutine does not allocate one per shot </summary>
        public WaitForFixedUpdate WaitFixed => waitForFixedUpdate;

        /// <summary> Attack direction. Exposed so a movement can keep it in sync while it steers </summary>
        public Vector3 Direction_ { get => Direction; set => Direction = value; }

        /// <summary> Last position of the projectile, updated by the movement each step </summary>
        public Vector3 Prev_Pos { get => Prev_pos; set => Prev_pos = value; }

        /// <summary> Public entry to the destroy/pool path, so a movement can end the projectile </summary>
        public void Destroy_Projectile() => DestroyProjectile();

        /// <summary> Public entry to re-enable the collider, so a movement can control it </summary>
        public void Collider_Enable() => Enable_Collider();

        /// <summary> Public entry to wake the rigidbody for a physics-driven launch </summary>
        public void RigidBody_Enable() => EnableRigidBody();

        /// <summary> Projectile Rotation: one step of the shared rotation behaviour. The movement supplies the
        /// direction and up hints (Rolling sends the ground normal). Empty behaviour keeps the fired rotation </summary>
        public void ApplyRotation(Vector3 direction, Vector3 up, float deltaTime)
            => rotationBehaviour?.ApplyRotation(this, direction, up, deltaTime);

        /// <summary> Replaces this projectile's movement with a copy of the launcher's. Null leaves the projectile's own </summary>
        public virtual void SetMovementOverride(ProjectileMovement source)
        {
            if (source == null) return;                                 //Empty override, keep whatever the prefab has

            //Cloned, never shared: movements keep per-shot state, so two projectiles pointing at the launcher's
            //single instance would overwrite each other's direction, distance and hit list mid-flight
            //Re-cloned on EVERY shot on purpose: a pooled projectile used to keep its first clone for good, so
            //any later edit to the launcher.s override never reached it again (values looked stuck while authoring)
            movement = source.Clone();

            Debugging($"Movement overridden by the launcher [{movement.DynamicName}]", this);
        }

        /// <summary> Replaces this projectile's rotation behaviour with a copy of the launcher's. Null leaves the projectile's own </summary>
        public virtual void SetRotationOverride(ProjectileRotationBehaviour source)
        {
            if (source == null) return;                                  //Empty override, keep whatever the prefab has

            //Cloned, never shared: behaviours keep per-shot state (spin angle, launch rotation), so two projectiles
            //pointing at the launcher's single instance would overwrite each other's spin mid-flight
            //Re-cloned on EVERY shot on purpose: a pooled projectile used to keep its first clone for good, so
            //any later edit to the launcher.s override never reached it again (values looked stuck while authoring)
            rotationBehaviour = source.Clone();

            Debugging($"Rotation overridden by the launcher [{rotationBehaviour.DynamicName}]", this);
        }

        /// <summary> Replaces this projectile's impact behaviour with a copy of the launcher's. Null leaves the projectile's own </summary>
        public virtual void SetImpactOverride(ProjectileImpactBehaviour source)
        {
            if (source == null) return;                               //Empty override, keep whatever the prefab has

            //Cloned, never shared: behaviours keep per-shot state (Ricochet bounces, hit lists), so two projectiles
            //pointing at the launcher's single instance would overwrite each other's state mid-flight
            //Re-cloned on EVERY shot on purpose: a pooled projectile used to keep its first clone for good, so
            //any later edit to the launcher.s override never reached it again (values looked stuck while authoring)
            onImpact = source.Clone();

            Debugging($"Impact behaviour overridden by the launcher [{onImpact.DynamicName}]", this);
        }

        #endregion
        //MWC: Projectile Movement - End API ---------------------------------------------------------------------------

        //MWC: OnImpact Behaviour - the surface a ProjectileImpactBehaviour needs on top of the Movement API ------------
        #region Impact API

        /// <summary> Public entry to the bone-accurate stick, so an impact behaviour can parent the projectile to the surface </summary>
        public void Impact_StickOnSurface(Transform surface, Vector3 hitPosition, float penetration, Vector3 pushDirection)
            => Stick_On_Surface(surface, hitPosition, penetration, pushDirection);

        /// <summary> This projectile was unparented because whatever it was stuck to went away (E.g. the
        /// projectile it was stuck into returned to the pool). Sticking switched its physics off, so it would just
        /// float there: give it its collisions and its Rigidbody back and let it drop </summary>
        public virtual void Parent_Lost()
        {
            //MWC: the link was left behind on whatever went away (E.g. a pooled projectile), where it would be
            //dragged into the pool or destroyed with it. Reclaim it, matching the pool-return parking convention
            if (LinkStickOnSurface != null && !LinkStickOnSurface.transform.IsChildOf(T))
                LinkStickOnSurface.transform.SetParent(T);

            RestoreRigidBody();     //MWC: sticking DESTROYED the body. Without it back there is nothing that can fall

            if (rb == null) return; //No body to hand it over to, so there is nothing that can make it fall

            Collisions_Restore();   //It needs its colliders back, or it drops straight through the world
            EnableRigidBody();      //Dynamic and under gravity again
            rb.interpolation = RigidbodyInterpolation.Interpolate; //Cleared when it stuck; a falling body wants it back

            Debugging("Lost the object it was stuck to. Rigid Body re-activated so it falls", this);
        }

        /// <summary> Colliders this projectile was told to ignore while stuck, so the pairs can be undone on reuse </summary>
        private readonly List<Collider> ignoredColliders = new();

        /// <summary> Stops this projectile pushing the thing it stuck INTO, while staying solid to everything else.
        /// Switching the whole body off (detectCollisions) also hides it from SphereCast, which broke a second projectile
        /// sticking into this one. Ignoring the pair keeps it findable, and static geometry is skipped since a wall
        /// cannot be pushed anyway. Fully reversible, so a pooled projectile comes back intact </summary>
        public virtual void Collisions_IgnoreWith(Collider hit)
        {
            if (hit == null || m_allColliders == null) return;

            //Only something that can actually be shoved is worth ignoring: a dynamic body or a character controller
            Transform root = null;

            if (hit.attachedRigidbody != null && !hit.attachedRigidbody.isKinematic) root = hit.attachedRigidbody.transform;

            if (root == null) return; //Static geometry, nothing to push

            //Every collider on it, ragdoll bones included
            foreach (var theirs in root.GetComponentsInChildren<Collider>(true))
            {
                if (theirs == null) continue;

                foreach (var mine in m_allColliders)
                {
                    if (mine == null) continue;
                    Physics.IgnoreCollision(mine, theirs, true);
                }

                ignoredColliders.Add(theirs);
            }

            if (debug) Debugging($"Ignoring collisions with [{root.name}] while stuck to it ({ignoredColliders.Count} colliders)", this);
        }

        /// <summary> Undoes Collisions_IgnoreWith for a pooled projectile. The movement re-applies interpolation at Fire </summary>
        public virtual void Collisions_Restore()
        {
            if (rb)
            {
                rb.detectCollisions = true;
                rb.constraints = RigidbodyConstraints.None;
            }

            if (m_allColliders != null)
            {
                for (int i = 0; i < m_allColliders.Length; i++)
                    if (m_allColliders[i]) m_allColliders[i].enabled = m_allCollidersEnabled[i];

                foreach (var theirs in ignoredColliders)
                {
                    if (theirs == null) continue; //It was destroyed or pooled meanwhile

                    foreach (var mine in m_allColliders)
                        if (mine != null) Physics.IgnoreCollision(mine, theirs, false);
                }
            }

            ignoredColliders.Clear();
        }

        /// <summary> Public entry to unparent any projectile stuck to this one before removing it </summary>
        public void Release_Attached() => Release_AttachedProjectiles();

        /// <summary> Builds [onImpact] from the legacy [impactBehaviour] enum on assets saved before the impact split.
        /// Runs at Awake for builds and at OnValidate so the editor bakes it into the asset </summary>
        protected void MigrateImpact()
        {
            //A non-null behaviour means this asset is already on the new format. Never overwrite it
            if (onImpact != null) return;

            onImpact = m_legacyImpactBehaviour switch
            {
                ImpactBehaviour.StickOnSurface => new StickImpact(),
                ImpactBehaviour.DestroyOnImpact => new DestroyImpact(),
                ImpactBehaviour.ActivateRigidBody => new ActivateRigidbodyImpact(),
                ImpactBehaviour.DisableOnImpact => new DisableImpact(),
                _ => new StopImpact(), //The old [None]
            };

            Debugging($"Migrated to [{onImpact.DynamicName}] impact behaviour", this);
        }

        #endregion
        //MWC: OnImpact Behaviour - End API ------------------------------------------------------------------------------

        /// <summary> Builds [movement] from the legacy [m_projectileType] on assets saved before the movement split.
        /// Runs at Awake for builds and at OnValidate so the editor bakes it into the asset </summary>
        protected void MigrateMovement()
        {
            //A non-null movement means this asset is already on the new format. Never overwrite it
            if (movement != null) return;

            //MWC: Projectile Rotation - the rotation fields are no longer copied here. MigrateRotation builds the
            //shared [rotationBehaviour] from the same legacy fields instead
            if (m_projectileType == ProjectileType.RollingProjectile)
            {
                movement = new RollingMovement();
            }
            else
            {
                movement = new FlyingMovement
                {
                    AfterDistance = m_legacyAfterDistance.Value,
                    Gravity = m_legacyGravity.Value,
                };
            }

            Debugging($"Migrated to [{movement.DynamicName}] movement", this);
        }

        /// <summary> Projectile Rotation: builds [rotationBehaviour] from the legacy rotation data. Two generations
        /// exist: fields nested inside a movement (assets saved after the movement split), and this component's own
        /// pre-split fields. Both are cleared after baking, so a user emptying the rotation later is never re-migrated </summary>
        protected bool MigrateRotation()
        {
            //MWC: the legacy data is ALWAYS drained, even when a rotation already exists. A dormant flag (e.g. a fresh
            //RollingMovement's Align To Ground default) must never resurrect after the user empties the rotation
            ProjectileRotationBehaviour extracted = null;
            var changed = movement != null && movement.TryExtractLegacyRotation(out extracted);

            //Pre-split assets keep their rotation on this component. The movement's own legacy fields win: newer generation
            if (m_legacyRotation != ProjectileRotation.None)
            {
                extracted ??= ProjectileRotationBehaviour.FromLegacy(m_legacyRotation, m_legacyTorque, m_legacyTorqueAxis, m_legacyTrajectoryRoll);
                m_legacyRotation = ProjectileRotation.None; //Consumed
                changed = true;
            }

            //Only an EMPTY rotation takes the extracted one. An authored rotation is never overwritten
            if (rotationBehaviour == null && extracted != null)
            {
                rotationBehaviour = extracted;
                Debugging($"Migrated to [{rotationBehaviour.DynamicName}] rotation behaviour", this);
            }

            return changed;
        }

        /// <summary> Hands the legacy [Penetration] over to whatever Stick behaviour this projectile ends up with.
        /// Drained EXACTLY once (a negative value is the consumed marker), so a depth the user edits afterwards is
        /// never overwritten the way a dormant default would </summary>
        protected bool MigratePenetration()
        {
            if (m_legacyPenetration < 0) return false; //Already consumed

            if (onImpact is StickImpact stick) stick.penetration = new FloatReference(m_legacyPenetration);
            else if (onImpact is RicochetImpact ricochet && ricochet.finalImpact is StickImpact finalStick)
                finalStick.penetration = new FloatReference(m_legacyPenetration);

            m_legacyPenetration = -1; //Consumed whether or not anything was sticking, so it can never come back
            return true;
        }

        /// <summary> Runs every migration (movement, impact, rotation). True when anything was built or consumed.
        /// Public so the [Migrate Projectiles To Movements] tool can bake assets through the same logic </summary>
        public bool Migrate()
        {
            var changed = false;

            if (movement == null) { MigrateMovement(); changed = true; }
            if (onImpact == null) { MigrateImpact(); changed = true; }
            changed |= MigrateRotation();
            changed |= MigratePenetration();

            return changed;
        }

        /// <summary> A real collision. Same as calling it with [ProjectileImpactKind.Impact] </summary>
        public virtual void ProjectileImpact(Rigidbody targetRB, Collider collider, Vector3 hitPosition, Vector3 normal)
            => ProjectileImpact(targetRB, collider, hitPosition, normal, ProjectileImpactKind.Impact);

        //MWC: [kind] replaced an older 'hasImpacted' bool, which conflated "stop the projectile" with "which events
        //fire". Pierce needs the OnHit events of a real impact but must keep travelling, so the two concerns are
        //separate: [stops] and [kind].
        public virtual void ProjectileImpact(Rigidbody targetRB, Collider collider, Vector3 hitPosition, Vector3 normal, ProjectileImpactKind kind)
        {
            if (!Active) return;

            //Only a real Impact latches HasImpacted, stops the movement and runs the Impact Behaviour
            var stops = kind == ProjectileImpactKind.Impact;

            //MWC: consumed here, not in the finally, so a missed attack cannot leak the flag into the next hit
            var hitWithTag = RollingHitWithTag;
            RollingHitWithTag = false;

            if (debug) Debugging($"<color=yellow><b>Projectile Impact</b>{collider.name} [{kind}]</color>", this); //MWC: the interpolation runs before the call, so the check is here too
            bool attackMissed = false; //CustomPatch: added flag to keep track of missed attack state in this method for the end result

            //CustomPatch: added try-finally block to call the hit events at the correct time after adjusting final projectile hit position
            Transform colliderTransform = null;

            try
            {
                this.HitPosition = hitPosition; //Store the Hit position of the Projectile

                if (stops)
                {
                    HasImpacted = true;
                    StopAllCoroutines();
                }

                if (MissAttack())
                {
                    attackMissed = true;
                    //MWC: a real impact that misses destroys the projectile (original behaviour); a particle/pierce miss just skips this target
                    if (stops)
                    {
                        Debugging("Destroy Projectile Missed", this);
                        Destroy(gameObject);
                    }
                    return;
                }

                //if there's no collider OR the projectile collider is a trigger
                //MWC: only freeze/disable the rigidbody on a real impact (particle and pierce damage must keep moving)
                if (stops && (!m_collider || m_collider.isTrigger))
                {
                    DisableRigidBody();
                    if (rb) rb.constraints = RigidbodyConstraints.FreezeAll;
                }

                TryInteract(collider.gameObject);

                var damagee = collider.GetComponentInParent<IMDamage>();                      //Get the Animal on the Other collider
                //Store the Last Collider that the animal hit
                if (damagee != null) { damagee.HitCollider = collider; }

                TryDamage(damagee, statModifier);

                //TryPhysics(targetRB, collider, Direction, Force);
                //Add a force to the Target RigidBody
                //MWC: gated now. A simple prop (a crate, a barrel, a ball) is shoved; a character is left alone,
                //since its knockback arrives through ReceiveDamage and a raw force here only fights its controller
                TryPushRigidbody(targetRB, hitPosition);

                colliderTransform = collider.transform;

                var hasAnimator = collider.gameObject.GetComponentInParent<Animator>();
                var RootBone = colliderTransform;
                if (hasAnimator != null) { RootBone = hasAnimator.avatarRoot; }

                //CustomPatch: replaced with cached collider transform
                var ClosestTransform = colliderTransform; //If the collider is a MeshCollider then use the same transform (To avoid errors with the ClosestPoint

                //MWC: removed StickOnHitSurface gate so StickOnSurface always runs the bone-accurate closest-transform search
                if (!collider.isTrigger && collider is not MeshCollider && collider is not TerrainCollider && hasAnimator)
                {
                    ClosestTransform = MTools.GetClosestTransform(hitPosition, RootBone, Layer);

                    //Meaning it found a nearest transform
                    if (ClosestTransform != colliderTransform) //CustomPatch: use cached transform
                    {
                        var colTransform = ClosestTransform.GetComponent<Collider>();

                        if (colTransform != null && !colTransform.isTrigger && colTransform is not MeshCollider)
                        {
                            hitPosition = colTransform.ClosestPoint(hitPosition);
                        }
                        else
                        {
                            //find the closes point in the upper bone or the lower bone
                            var MainPos = ClosestTransform.position;

                            //find the parent bone
                            var parentPoint = ClosestTransform.parent != null ? ClosestTransform.parent.position : MainPos;

                            //find the child bone
                            var ChildPoint = ClosestTransform.childCount > 0 ? ClosestTransform.GetChild(0).position : MainPos;

                            var P1 = MTools.ClosestPointOnLine(hitPosition, ChildPoint, MainPos);
                            var P2 = MTools.ClosestPointOnLine(hitPosition, parentPoint, MainPos);

                            var Dist1 = Vector3.Distance(P1, MainPos);
                            var Dist2 = Vector3.Distance(P2, MainPos);

                            hitPosition = Dist1 < Dist2 ? P1 : P2;
                        }
                    }
                }

                TryHitEffectProjectile(hitPosition, normal, ClosestTransform, damagee);

                //MWC: impact-only behaviour (stick/destroy/activate-rb + life-impact). Particle and Pierce damage skip all of this so the projectile keeps travelling.
                if (stops)
                {
                    //MWC: OnImpact Behaviour - the pluggable strategy replaces the old enum switch.
                    //False means the projectile was destroyed, pooled or relaunched (Ricochet), so nothing below may touch it
                    if (onImpact != null && !onImpact.OnImpact(this, collider, ClosestTransform, hitPosition, normal))
                        return;

                    //In case the projectile lives in the scene it needs to be destroyed after life impact has elapsed (Destroy or sent back to the pool)
                    if (LifeImpact > 0)
                    {
                        //Debug.Log("DO LIFE IMPACT WITH DEST");
                        this.Delay_Action(LifeImpact, DestroyProjectile); //MWC: replaced reflection-based Invoke with the project's coroutine-based Delay_Action
                    }
                    //MWC: Rolling stays Active after an impact, so a pierce sweep can keep damaging what it passes
                    //through - [ProjectileImpact] returns early on a projectile that is not Active. Flying disables
                    //as before. (This used to be justified by the Magic Particle damage, which no longer exists)
                    else if (!IsRolling)
                    {
                        Active = false; //Disable the projectile it has already impacted with something
                    }
                }
            }
            finally
            {
                //  moved hit events at the end of the execution of this method to make sure they're called with the latest up-to-date post-processed impact information (penetration adjustment, projectile re-parenting, hit position adjustments, etc)
                if (!attackMissed)
                {
                    //MWC: Impact and Pierce share the same hit events. A Tagged target routes through OnHitTag instead
                    if (hitWithTag)
                    {
                        OnHitTag.Invoke(colliderTransform);
                    }
                    else
                    {
                        if (Thrower is MDamager damager) damager.OnHit.Invoke(colliderTransform); //Relay the hit to a Damager Thrower (e.g. the legacy MShootable)
                        OnHit.Invoke(colliderTransform);
                    }

                    OnHitPosition.Invoke(hitPosition);
                }
            }
        }

        /// <summary> Direction the projectile was actually travelling. Used to push what it hits and to align it
        /// on impact. The Movement keeps [NormalizedDirection] up to date while it travels, and the travel delta and the
        /// launch velocity are the fallbacks, so a movement that never fills it in cannot silently zero either </summary>
        public Vector3 TravelDirection
        {
            get
            {
                if (NormalizedDirection.sqrMagnitude > 0.0001f) return NormalizedDirection.normalized;
                if (Direction.sqrMagnitude > 0.0001f) return Direction.normalized;   //The per-step travel delta
                return Velocity.normalized;                                          //Never moved, so the launch direction is all there is
            }
        }

        /// <summary> True when the hit Rigidbody is a plain physics prop the projectile may shove. A character
        /// drives its own Rigidbody and takes its knockback through ReceiveDamage, so pushing it here fights the
        /// controller instead of adding to it. Static and kinematic bodies cannot be pushed at all </summary>
        public virtual bool IsPushable(Rigidbody target)
        {
            if (target == null || target.isKinematic) return false;

            if (target.GetComponentInParent<MAnimal>() != null) return false;               //An Animal Controller character (ragdoll bones included)

            return true; //A crate, a barrel, a ball... and a damageable that is not a character is still a prop
        }

        /// <summary> Shoves the Rigidbody the projectile hit, when it is a simple prop </summary>
        protected virtual bool TryPushRigidbody(Rigidbody target, Vector3 hitPosition)
        {
            if (PushMultiplier.Value <= 0 || !IsPushable(target)) return false;

            var force = PushMultiplier.Value * Velocity.magnitude * TravelDirection;

            target.AddForceAtPosition(force, hitPosition, forceMode);
            if (debug) Debugging($"Push [{target.name}] with {force} [{forceMode}]", this);
            return true;
        }

        //MWC: Stick On Surface - the authored Rigidbody, captured at Awake before anything can change it, so the body
        //Stick_On_Surface destroys can be put back exactly as the prefab had it
        private RigidBodyState m_rbState;

        /// <summary> Snapshot of an authored Rigidbody, so a removed one can be rebuilt exactly as it was </summary>
        protected struct RigidBodyState
        {
            /// <summary> False when the projectile never had a Rigidbody, so nothing is ever added to it </summary>
            public bool exists;
            public float mass, linearDamping, angularDamping, maxAngularVelocity, maxDepenetrationVelocity, sleepThreshold;
            public bool useGravity, isKinematic, detectCollisions, automaticCenterOfMass, automaticInertiaTensor;
            public Vector3 centerOfMass, inertiaTensor;
            public Quaternion inertiaTensorRotation;
            public RigidbodyInterpolation interpolation;
            public CollisionDetectionMode collisionDetectionMode;
            public RigidbodyConstraints constraints;
            public LayerMask excludeLayers, includeLayers;
        }

        /// <summary> Stores the authored Rigidbody settings. Called once at Awake, never after </summary>
        protected virtual void StoreRigidBodyState()
        {
            m_rbState = default;
            if (rb == null) return;

            m_rbState.exists = true;
            m_rbState.mass = rb.mass;
            m_rbState.linearDamping = rb.linearDamping;
            m_rbState.angularDamping = rb.angularDamping;
            m_rbState.maxAngularVelocity = rb.maxAngularVelocity;
            m_rbState.maxDepenetrationVelocity = rb.maxDepenetrationVelocity;
            m_rbState.sleepThreshold = rb.sleepThreshold;
            m_rbState.useGravity = rb.useGravity;
            m_rbState.isKinematic = rb.isKinematic;
            m_rbState.detectCollisions = rb.detectCollisions;
            m_rbState.interpolation = rb.interpolation;
            m_rbState.collisionDetectionMode = rb.collisionDetectionMode;
            m_rbState.constraints = rb.constraints;
            m_rbState.excludeLayers = rb.excludeLayers;
            m_rbState.includeLayers = rb.includeLayers;

            //Reading these turns nothing off, but WRITING them does, so the automatic flags decide which are restored
            m_rbState.automaticCenterOfMass = rb.automaticCenterOfMass;
            m_rbState.centerOfMass = rb.centerOfMass;
            m_rbState.automaticInertiaTensor = rb.automaticInertiaTensor;
            m_rbState.inertiaTensor = rb.inertiaTensor;
            m_rbState.inertiaTensorRotation = rb.inertiaTensorRotation;
        }

        /// <summary> Destroys the Rigidbody, so nothing but the Transform hierarchy drives the stuck projectile </summary>
        protected virtual void RemoveRigidBody()
        {
            if (rb == null) return;

            var body = rb;

            //MWC: Destroy is DEFERRED to the end of the frame, and until it lands the body keeps driving the Transform.
            //It was still holding the pose it had while it flew, so it wrote that back over the one the impact had just
            //built and Align On Impact (Bite Axis / Up Axis) was lost on whichever projectile lost the race.
            //Parked and handed the final pose, anything it writes before it dies is the pose it was told to hold
            body.interpolation = RigidbodyInterpolation.None; //Or it smears an interpolated pose over the Transform instead
            body.isKinematic = true;
            body.position = T.position;
            body.rotation = T.rotation;

            rb = null;      //Cleared FIRST: every [if (rb)] around the codebase would still answer true until the
            Destroy(body);  //deferred destroy lands
        }

        /// <summary> Puts the authored Rigidbody back on a projectile that had it removed while it was stuck </summary>
        protected virtual void RestoreRigidBody()
        {
            if (!m_rbState.exists || rb != null) return;

            //Adding a second one while the removed body is still waiting for the end of the frame is an error, so a
            //body that is still there is reused. It only happens if a stick is undone within the same frame
            if (!TryGetComponent(out rb)) rb = gameObject.AddComponent<Rigidbody>();

            rb.mass = m_rbState.mass;
            rb.linearDamping = m_rbState.linearDamping;
            rb.angularDamping = m_rbState.angularDamping;
            rb.maxAngularVelocity = m_rbState.maxAngularVelocity;
            rb.maxDepenetrationVelocity = m_rbState.maxDepenetrationVelocity;
            rb.sleepThreshold = m_rbState.sleepThreshold;
            rb.useGravity = m_rbState.useGravity;
            rb.isKinematic = m_rbState.isKinematic;
            rb.detectCollisions = m_rbState.detectCollisions;
            rb.interpolation = m_rbState.interpolation;
            rb.collisionDetectionMode = m_rbState.collisionDetectionMode;
            rb.constraints = m_rbState.constraints;
            rb.excludeLayers = m_rbState.excludeLayers;
            rb.includeLayers = m_rbState.includeLayers;

            //A new Rigidbody starts automatic, so only an authored MANUAL value is worth writing back
            if (!m_rbState.automaticCenterOfMass) rb.centerOfMass = m_rbState.centerOfMass;

            if (!m_rbState.automaticInertiaTensor)
            {
                rb.inertiaTensor = m_rbState.inertiaTensor;
                rb.inertiaTensorRotation = m_rbState.inertiaTensorRotation;
            }

            //The pose is whatever the hierarchy left it at, not the one it had when it stuck
            rb.position = T.position;
            rb.rotation = T.rotation;

            Debugging("Rigid Body restored", this);
        }

        protected virtual void EnableRigidBody()
        {
            if (rb)
            {
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.constraints = RigidbodyConstraints.None;
                rb.detectCollisions = true;
            }
        }

        protected virtual void DisableRigidBody()
        {
            if (rb)
            {
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.useGravity = false;
                rb.isKinematic = true;
                //MWC: a parked body must stop writing its own interpolated pose over the transform, or a projectile
                //stuck into a moving bone lags behind the parent that is carrying it
                rb.interpolation = RigidbodyInterpolation.None;
            }
        }

        public void PrepareDamage(StatModifier modifier, float CriticalChance, float CriticalMultiplier, StatElement element)
        {
            if (!KeepValues)
            {
                statModifier = new StatModifier(modifier);
                this.CriticalChance = CriticalChance;
                this.CriticalMultiplier = CriticalMultiplier;
                this.element = element;
            }
        }

        /// <summary> Parents the projectile to the hit surface through a scale-fixer [Link] and parks its physics </summary>
        protected virtual void Stick_On_Surface(Transform colliderTransform, Vector3 HitPosition, float penetration, Vector3 pushDirection) //CustomPatch: renamed the previous "collider" param to avoid confusion (good practices creates an easier to maintain code base ;) )
        {
            if (debug) Debugging($"Stick on Surface [{colliderTransform.name}]", this);
            MDebug.DrawWireSphere(HitPosition, Color.red, 0.05f);

            //MWC: OnImpact Behaviour - created lazily instead of at Awake, since any behaviour (e.g. Ricochet's Final Impact) can end up sticking
            if (LinkStickOnSurface == null)
            {
                LinkStickOnSurface = new GameObject($"Link [{name}]");
                LinkStickOnSurface.AddComponent<HierarchyLink>(); //MWC: closest-bone searches must skip this graft and its subtree
                LinkStickOnSurface.transform.SetParent(transform);
                LinkStickOnSurface.transform.ResetLocal();
            }

            LinkStickOnSurface.transform.parent = null; //remove the Parent from the link stick
            transform.SetParentScaleFixer(colliderTransform, HitPosition, LinkStickOnSurface);


            //MWC: depth and direction come from the Stick behaviour now (it buries along the blade when it aligns)
            T.position += pushDirection * penetration; //Put the Projectile a bit deeper in the collider

            //MWC: the body is REMOVED, not parked. A Rigidbody keeps writing its own world pose back OVER the Transform,
            //which pinned the projectile at the impact point while whatever it stuck into moved away. Parking it
            //(kinematic, no interpolation, asleep) never stopped that, and freezing it only traded the problem for a
            //projectile that slid along with the surface without ever turning with it. With no body at all the parent
            //hierarchy is the only thing driving the projectile, so it follows the movement AND the rotation of its
            //surface. RestoreRigidBody puts the authored one back when it is fired again or returns to the pool
            RemoveRigidBody();
        }

        private GameObject LinkStickOnSurface;

        /// <summary> Plays the pooled Surface Hit Effect at the impact point. [m_HitEffect] and [hitSound] were removed </summary>
        protected virtual void TryHitEffectProjectile(Vector3 HitPosition, Vector3 Normal, Transform hitTransform, IMDamage damagee)
        {
            var HitRotation = Quaternion.FromToRotation(Vector3.up, Normal);

            if (debug) MDebug.DrawWireSphere(HitPosition, Color.red, 0.05f, 1);

            //The pooled effect releases itself, so it survives the projectile being destroyed/disabled/pooled on impact
            var instance = PlaySurfaceEffect(damagee, HitPosition, HitRotation, hitTransform);

            if (instance != null)
                if (debug) Debugging($"<color=yellow> <b>[HitEffect] </b> [{instance.name}] , {HitPosition} </color>", this);
        }


#if UNITY_EDITOR
        //MWC: Projectile Movement - bakes the migration into the asset the first time it is inspected after the update.
        //Never migrate in OnAfterDeserialize: Unity forbids creating managed references during deserialization
        protected virtual void OnValidate()
        {
            //MWC: every managed reference migrates independently (movement, impact, rotation)
            if (Migrate())
                EditorUtility.SetDirty(this); //Without this the new managed references are never written to disk

            if (m_audio == null) m_audio = GetComponent<AudioSource>();
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (m_collider == null) m_collider = GetComponentInChildren<Collider>();
        }

        protected override void Reset()
        {
            base.Reset();
            movement ??= new FlyingMovement(); //MWC: a brand new component starts on the ballistic trajectory
            onImpact ??= new StopImpact();     //MWC: OnImpact Behaviour - and stops in place on impact
            rb = GetComponent<Rigidbody>();
            m_collider = GetComponentInChildren<Collider>();


            m_audio = GetComponent<AudioSource>(); //Gets the Weapon Source

            if (!m_audio) m_audio = gameObject.AddComponent<AudioSource>(); //Create an AudioSource if there's no Audio Source on the weapon

            m_audio.spatialBlend = 1;
            m_audio.maxDistance = 50;


            // Make the object and all the children to be in the Ignore Raycast Layer
            int ignoreLayer = LayerMask.NameToLayer("Item");
            if (ignoreLayer == -1) ignoreLayer = 2; // Fallback to Unity's default index for Ignore Raycast

            var transforms = GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t != null && t.gameObject != null)
                    t.gameObject.layer = ignoreLayer;
            }
        }

        protected void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow + Color.red;
            // Gizmos.DrawSphere(transform.position, Radius);
            Gizmos.DrawWireSphere(transform.position, Radius);
        }


#endif
    }



    /// ----------------------------------------
    /// EDITOR
    /// ----------------------------------------

    #region Inspector


#if UNITY_EDITOR
    [CustomEditor(typeof(MProjectile))]
    public class MProjectileEditor : MDamagerEd
    {
        private SerializedProperty PushMultiplier
     , Editor_Tabs1, KeepDamageValues, Radius, OnRayCastHit, Life, LifeImpact,
        OnFire, onImpact, m_PosOffset, m_RotOffset, rb, m_collider, m_trail, //MWC: OnImpact Behaviour - was [impactBehaviour]
         movement, rotationBehaviour, OnHitTag, OnReachedMaxDistance; //MWC: Projectile Rotation

        protected string[] Tabs1 = new string[] { "General", "Damage", "Physics", "Events" };
        private MProjectile M;


        protected override void OnEnable()
        {
            FindBaseProperties();
            M = (MProjectile)target;


            OnFire = serializedObject.FindProperty("OnFire");
            Radius = serializedObject.FindProperty("Radius");


            Life = serializedObject.FindProperty("Life");
            LifeImpact = serializedObject.FindProperty("LifeImpact");
            onImpact = serializedObject.FindProperty("onImpact"); //MWC: OnImpact Behaviour - was [impactBehaviour]

            PushMultiplier = serializedObject.FindProperty("PushMultiplier");

            m_PosOffset = serializedObject.FindProperty("m_PosOffset");
            m_RotOffset = serializedObject.FindProperty("m_RotOffset");
            KeepDamageValues = serializedObject.FindProperty("m_KeepDamageValues");

            //  InstantiateOnImpact = serializedObject.FindProperty("InstantiateOnImpact");
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            rb = serializedObject.FindProperty("rb");
            m_trail = serializedObject.FindProperty("m_trail");
            m_collider = serializedObject.FindProperty("m_collider");

            //  sphereCastZOffset = serializedObject.FindProperty("sphereCastZOffset"); //CustomPatch: added sphereCastZOffset property
            OnRayCastHit = serializedObject.FindProperty("OnRayCastHit");

            //MWC: Projectile Movement - the movement reference plus the events its implementations drive
            movement = serializedObject.FindProperty("movement");
            rotationBehaviour = serializedObject.FindProperty("rotationBehaviour"); //MWC: Projectile Rotation
            OnHitTag = serializedObject.FindProperty("OnHitTag");
            OnReachedMaxDistance = serializedObject.FindProperty("OnReachedMaxDistance");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawDescription("Logic for Projectiles. When is fired by a Thrower component, use the method Prepare() to transfer all the properties from the thrower");

            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs1);

            int Selection = Editor_Tabs1.intValue;
            if (Selection == 0) DrawGeneral();
            else if (Selection == 1) DrawDamage();
            else if (Selection == 2) DrawExtras();
            else if (Selection == 3) DrawEvents();
            // EditorGUILayout.PropertyField(debug);

            // EditorGUILayout.PropertyField(sphereCastZOffset);

            serializedObject.ApplyModifiedProperties();
        }

        protected void DrawExtras()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPhysics(false);
                EditorGUILayout.PropertyField(PushMultiplier);
                //MWC: Projectile Movement - the [Movement] reference moved to the General tab
            }

            DrawMisc();
        }

        protected void DrawDamage()
        {
            EditorGUILayout.PropertyField(KeepDamageValues, new GUIContent("Keep Values"));
            if (!M.KeepValues)
            {
                EditorGUILayout.HelpBox("If the Projectile is thrown by a Throwable, the Stat will be set by the Throwable. [E.g. The Arrow will get the Damage from the bow]", MessageType.Info);
            }
            else
            {
                DrawStatModifier();
                DrawCriticalDamage();
            }

            DrawMisc();
        }

        protected override void DrawGeneral(bool drawbox = true)
        {
            base.DrawGeneral(drawbox);

            //MWC: Projectile Movement - moved here from the Physics tab, so how the projectile travels sits next to what it does on impact
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(movement, new GUIContent("Movement"));

                if (movement.managedReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("No Movement assigned. Pick one, or the projectile will not travel.", MessageType.Warning);
                }
                else if (movement.managedReferenceValue is RollingMovement)
                {
                    EditorGUI.indentLevel++;
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.HelpBox(
                            "Rolling is a kinematic ground sweep: it ignores gravity, mass and drag.\n" +
                            "Layers in its Ground Mask are what it walks on and is blocked by, and they never cause damage. " +
                            "Everything in the Hit Layer is damaged as it passes.", MessageType.Info);

                        EditorGUILayout.PropertyField(OnHitTag);
                        EditorGUILayout.PropertyField(OnReachedMaxDistance);
                    }
                    EditorGUI.indentLevel--;
                }

                //MWC: Projectile Rotation - shared by every movement. Empty keeps the fired rotation
                EditorGUILayout.PropertyField(rotationBehaviour, new GUIContent("Rotation"));

                //MWC: OnImpact Behaviour - one managed reference replaces the old enum. It draws its own parameters
                EditorGUILayout.PropertyField(onImpact, new GUIContent("On Impact"));
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Life.isExpanded = MalbersEditor.Foldout(Life.isExpanded, "Projectile Life");

                if (Life.isExpanded)
                {
                    EditorGUILayout.PropertyField(Life);
                    EditorGUILayout.PropertyField(LifeImpact);
                    EditorGUILayout.PropertyField(Radius);
                }

                m_PosOffset.isExpanded = MalbersEditor.Foldout(m_PosOffset.isExpanded, "Offsets");

                if (m_PosOffset.isExpanded)
                {
                    EditorGUILayout.PropertyField(m_PosOffset, new GUIContent("Position"));
                    EditorGUILayout.PropertyField(m_RotOffset, new GUIContent("Rotation"));
                }

                rb.isExpanded = MalbersEditor.Foldout(rb.isExpanded, "References");

                if (rb.isExpanded)
                {
                    EditorGUILayout.PropertyField(rb, new GUIContent("Rigid Body"));
                    EditorGUILayout.PropertyField(m_collider, new GUIContent("Collider"));
                    EditorGUILayout.PropertyField(m_trail);
                }
            }

        }

        protected override void DrawCustomEvents()
        {
            EditorGUILayout.PropertyField(OnFire);
            EditorGUILayout.PropertyField(OnRayCastHit);
        }
    }
#endif

    #endregion
}