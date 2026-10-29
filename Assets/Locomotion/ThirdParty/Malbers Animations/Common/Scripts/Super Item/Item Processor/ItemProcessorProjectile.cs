// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Controller; //MWC — MDamager (the projectile's damage authoring) lives here
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Weapons;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Serialization; //MWC — [FormerlySerializedAs] to migrate the old [force] field into [MaxForce]

namespace MalbersAnimations.Items
{
    /// <summary> Where the Fire (Projectile) Processor takes the projectiles it fires from </summary>
    public enum ProjectileSource
    {
        /// <summary> The projectile and its ammo reserve come from the character's Pouch Manager, looked up by Projectile ID </summary>
        ProjectileID,
        /// <summary> A prefab instantiated (and pooled) by the Processor itself. No Pouch Manager and no ammo reserve involved </summary>
        Prefab,
    }

    [System.Serializable, AddTypeMenu("Projectile/Fire")]
    [HelpURL(ItemDocs.Processors + "projectile-fire")]
    public class ItemProcessorProjectile : ItemProcessor
    {
        public override string DynamicName =>
            $"Fire [{SourceName}] "
           + $"Amount: {fireAmount.Value} "
           //MWC — the cost only earns header space when it differs from the default 1
           + $"{(AmmoCost.Value != 1 ? $"Cost: {AmmoCost.Value} " : "")}"
           //MWC — show what drives the damage, so the damage source is visible on the collapsed header.
           //Reads the Modifier's own Source EXPRESSION (E.g. "Attack x Charge.Normalized"), which is also where the
           //charge scaling lives now. It was reading the legacy [Base] Stat, which the Sources migration nulls out,
           //so every migrated Item showed "<Missing>" here.
           + $"{(PassAttackStat ? $"[{(damage.from == StatValueFrom.Stat ? damage.SourceExpression : $"{damage.MinValue.Value} - {damage.MaxValue.Value}")}] " : "")}" // MWC — the [Stats From] tag is gone with the switch
        //+ $"{(SelfProjectile ? " [Self Projectile]" : "")}"
        ;

        [Tooltip("Where the fired projectiles come from.\n" +
            "[Projectile ID]: the projectile and its ammo reserve are taken from the character's Pouch Manager.\n" +
            "[Prefab]: the prefab is instantiated directly and pooled by this Processor. No Pouch Manager is needed and no ammo reserve is consumed")]
        public ProjectileSource projectileFrom = ProjectileSource.ProjectileID;

        [Tooltip("Projectile ID looked up on the character's Pouch Manager")]
        [Hide(nameof(projectileFrom), (int)ProjectileSource.ProjectileID)]
        public ProjectileID projectileID;

        [Tooltip("Projectile prefab fired directly, without a Pouch Manager. It needs an MProjectile (IProjectile) component")]
        [Hide(nameof(projectileFrom), (int)ProjectileSource.Prefab)]
        public GameObjectReference projectilePrefab = new();

        [Tooltip("Initial capacity of the Projectile Pool owned by this Processor")]
        [Hide(nameof(projectileFrom), (int)ProjectileSource.Prefab)]
        public IntReference poolInitialSize = new(10);

        [Tooltip("Max number of projectiles kept in the Pool owned by this Processor. Extra returned projectiles are destroyed")]
        [Hide(nameof(projectileFrom), (int)ProjectileSource.Prefab)]
        public IntReference poolMaxSize = new(50);

        public TransformReference ProjectileParent;

        [Tooltip("How many projectiles will be fired at the same time")]
        public IntReference fireAmount = new(1);

        //MWC — Ammo spent per SHOT, decoupled from [Fire Amount]. The cost used to be paid once per projectile, so a
        //shotgun firing 8 pellets drained 8 rounds. It is now charged a single time for the whole burst.
        [Tooltip("Ammo consumed for ONE shot, no matter how many projectiles [Fire Amount] spawns.\n" +
            "E.g. a Shotgun firing 8 pellets with an Ammo Cost of 1 consumes a single round.\n" +
            "It is taken from the Chamber when [Use Chamber] is on, and from the Pouch reserve when it is off.\n" +
            "Firing a [Prefab] without a Chamber has no reserve to pay from, so the shot is free.\n" +
            "Set it to 0 for a shot that consumes no ammo at all")]
        public IntReference AmmoCost = new(1);

        [Tooltip("If true, the projectile will be loaded in a chamber before being fired. If false, it will be fired directly from the pouch (or for free when firing a Prefab)")]
        public bool UseChamber = false;

        //MWC — the Chamber is now a Stat on the item's Stats component (next to the SuperItem):
        //Stat.Value = units in chamber, Stat.MaxValue = chamber size. Shared by Fire/Reload/Conditions/UI/upgrades.
        [Tooltip("Stat on the Item's Stats component used as the Chamber.\nValue = Units in the chamber. Max Value = Chamber Size")]
        [Hide(nameof(UseChamber))]
        public StatID ChamberStat;


        /// <summary> Cached Chamber Stat (resolved on Item Equipped) </summary>
        private IMStat stat;

        [Tooltip("Where the projectile FLIES.\n" +
            "True: the velocity points from the Projectile Parent to the Aimer point (the crosshair hit), and that point is the destination of the Launch Angle when there is no Target.\n" +
            "False: the velocity follows the Projectile Parent forward, so the hand orientation decides")]
        public BoolReference useAimerDirection = new(true);

        //MWC — the single [force] is now a Min/Max range. With a Charge Stat the force is interpolated by its
        //normalized value; without one it is randomized inside the range. [FormerlySerializedAs] keeps the value
        //authored on existing prefabs (it becomes the Max Force).
        [Tooltip("Minimum force applied to the projectile. Used when the Charge Stat is empty")]
        public FloatReference MinForce = new(10);

        [Tooltip("Maximum force applied to the projectile. Used when the Charge Stat is full")]
        [FormerlySerializedAs("force")]
        public FloatReference MaxForce = new(10);

        [Tooltip("Optional Stat on the Item's Stats component used to interpolate the force between [Min Force] and [Max Force] " +
           "using its Normalized Value. (E.g. the same Charge Stat filled by a [Charge] Processor)\n" +
           "When empty, the force is randomized between Min and Max")]
        public StatID powerStat;
        /// <summary> Cached Charge Stat (resolved on Item Equipped). Null when no Stat is used </summary>
        private IMStat power_stat;

        /// <summary>
        /// Charge normalized value SAMPLED at the instant Execute is called, used by <see cref="ForceValue"/>.
        /// Negative means "no charge to use" (no Charge Stat), which falls back to a random force. It cannot be read
        /// later from the Stat itself because the force is computed inside a <see cref="SuperItem.Delay_Action"/>
        /// coroutine that always runs at least one frame after Execute, and by then the [Charge] Processor has already
        /// emptied the Stat on release. The DAMAGE does not need it: it is resolved at Execute time (see Execute).
        /// </summary>
        private float SampledPower = -1f;

        /// <summary> Random force rolled for the CURRENT projectile when there is no Charge Stat </summary>
        private float rolledForce;

        /// <summary> True while <see cref="rolledForce"/> holds the roll of the projectile being aimed or fired </summary>
        private bool forceRolled;

        public bool spreadProjectile = false;

        [Tooltip("Spread Angle for the Projectile")]
        [Hide(nameof(spreadProjectile))]
        public FloatReference spreadAngle = new(0);

        //[Tooltip("Stat on the Item's Stats component used to modify the spread angle.\n" +
        //    "The Stat Value is multiplied by the Spread Angle to get the final spread angle.\n" +
        //    "Leave it empty to use the Spread Angle as is.")]
        //[Hide(nameof(spreadProjectile))]
        //public StatID SpreadStat;
        //private Stat spread_stat;


        [Tooltip("Spread Multiplier the spread angle in X, Y, Z")]
        [Hide(nameof(spreadProjectile))]
        public Vector3Reference spreadDirection = new(Vector3.one);

        //MWC — deterministic spread: the projectiles are placed evenly around a cone ring instead of randomly
        [Tooltip("If true, the projectiles are spread evenly around a cone ring (stable pattern) instead of randomly.\n" +
            "Requires [Fire Amount] greater than 1. The Spread Mult scales the vertical (X) and horizontal (Y) radius of the ring")]
        [Hide(nameof(spreadProjectile))]
        public bool spreadEvenly = false;

        //MWC: gravity now comes from the projectile that will fly, so aiming and flight can never disagree.
        //Zero until the ammo resolves, which matches this field's old default and just skips the arc aiming
        /// <summary> Gravity the fired projectile flies under: the Movement Override wins, then the source projectile's Flying movement </summary>
        public Vector3 Gravity => movementOverride is Weapons.FlyingMovement f ? f.Gravity
            : SourceProjectile != null
            ? SourceProjectile.FlyingGravity
            : Vector3.zero;

        /// <summary> Launch Angle the fired projectile lobs at: the Movement Override wins, then the source projectile's Flying movement </summary>
        public float LaunchAngle => movementOverride is Weapons.FlyingMovement fa ? fa.LaunchAngle
            : SourceProjectile != null
            ? SourceProjectile.FlyingLaunchAngle
            : 0;

        /// <summary> What the Launch Angle does (land on the point, or tilt the aim keeping the Force): the Movement Override wins, then the source projectile's Flying movement </summary>
        public Weapons.LaunchAngleMode LaunchAngleMode => movementOverride is Weapons.FlyingMovement fm ? fm.LaunchAngleMode
            : SourceProjectile != null
            ? SourceProjectile.FlyingLaunchAngleMode
            : Weapons.LaunchAngleMode.TiltAim;

        /// <summary> After Distance the fired projectile flies flat for: the Movement Override wins, then the source projectile's Flying movement.
        /// Only the trajectory preview reads it; the projectile itself owns the value while it flies </summary>
        public float AfterDistance => movementOverride is Weapons.FlyingMovement fd ? fd.AfterDistance
            : SourceProjectile != null
            ? SourceProjectile.FlyingAfterDistance
            : 0;

        /// <summary> Name shown on the collapsed header: the Projectile ID, or the Prefab, whichever is the source </summary>
        private string SourceName => projectileFrom == ProjectileSource.Prefab
            ? (projectilePrefab.Value != null ? projectilePrefab.Value.name : "<Missing>")
            : (projectileID != null ? projectileID.name : "<Missing>");

        /// <summary> Projectile prefab component the aiming reads its trajectory from: the Pouch ammo's, or the direct Prefab's </summary>
        private MProjectile SourceProjectile => projectileFrom == ProjectileSource.Prefab
            ? prefabProjectile
            : ProjectileAmmo?.projectile;

        /// <summary> Pool the fired projectiles are taken from and return to: the Pouch ammo's, or the one owned by this Processor </summary>
        private IObjectPool<GameObject> Pool => projectileFrom == ProjectileSource.Prefab
            ? prefabPool
            : ProjectileAmmo?.Pool;

        //MWC: Movement Override - leave empty to use whatever movement the projectile prefab carries
        [SerializeReference, SubclassSelector]
        [Tooltip("Overrides the movement of every projectile this action fires. Leave empty to use the projectile's own")]
        public Weapons.ProjectileMovement movementOverride;

        //MWC: Projectile Rotation Override - same pattern, for the rotation behaviour
        [SerializeReference, SubclassSelector]
        [Tooltip("Overrides the rotation of every projectile this action fires (Follow Trajectory, Random, Axis...). Leave empty to use the projectile's own")]
        public Weapons.ProjectileRotationBehaviour rotationOverride;

        //MWC: OnImpact Override - same pattern as the Movement Override, for the impact behaviour
        [SerializeReference, SubclassSelector]
        [Tooltip("Overrides what every projectile this action fires does on impact (Stop, Stick, Destroy, Ricochet...). Leave empty to use the projectile's own")]
        public Weapons.ProjectileImpactBehaviour impactOverride;

        //MWC: Launch Angle - [m_angle] moved to FlyingMovement (each projectile authors its own arc).
        //This Processor reads it through [LaunchAngle], where the Movement Override wins over the ammo prefab


        [Tooltip("If true, the projectile will use the character rotation instead of the Projectile Parent rotation, for the initial direction")]
        public bool UseCharacterRotation;

        [Tooltip("How the projectile LOOKS when it spawns. Does not change the velocity.\n" +
            "True: the projectile is rotated to face the Aim direction on its first frame. Once it flies, its Rotation behaviour (Follow Trajectory...) takes over.\n" +
            "False: it keeps the Projectile Parent rotation")]
        public bool AlignWithAim = true;

        //MWC — pass the weapon's [Attack] Stat to the projectile so the Stat drives the damage (upgrades, buffs and UI
        //all work through the Stat). Only the damage VALUE is overridden: the projectile prefab keeps its own Stat to
        //modify, Critical Chance/Multiplier and Element, exactly like MShootable does when it fires.
        [Tooltip("If true, the damage of the fired projectile is taken from the Item's [Attack] Stat instead of the value authored on the projectile prefab")]
        public bool PassAttackStat = false;

        [Tooltip("Damage applied to whatever the projectile hits.\n" +
            "[Target] Stat to modify on the Damagee. Leave it EMPTY to keep the Stat and Stat Option authored on the projectile prefab.\n" +
            "[Value From] can be a Min-Max range, or a [Source] expression read from the [Source Stats] component.\n" +
            "Leave [Source Stats] EMPTY to read the Stats of the character firing the Item. Set it to read a specific Stats (E.g. the Item's own)")]
        [Hide(nameof(PassAttackStat))]
        public StatModifier damage = new(null, 10); // MWC — replaces the old [AttackStat] + [RandomMinMax] fields

        //MWC — true when the Source Stats were taken from the character on equip, so they are released on unequip
        //and an inspector-assigned Stats is never touched. Replaces the old [Stats From] Local/FromCharacter switch
        [System.NonSerialized] private bool sourceFromCharacter;

        private PouchManager pouch;

        /// <summary>
        /// The Projectile Ammo is the pool of projectiles that will be used to fire the projectiles.
        /// It is obtained from the Pouch Manager using the Projectile ID reference.
        /// The Projectile Ammo is used to get the projectiles from the pool and release them back to the pool when they are no longer needed.
        /// </summary>
        private Ammo ProjectileAmmo { get; set; }
        public Vector3 Velocity { get; set; }
        public float Power { get; set; }

        /// <summary> MProjectile on the direct [Prefab] asset (resolved on Item Equipped), read for the trajectory. Null when the source is a Projectile ID </summary>
        private MProjectile prefabProjectile;
        /// <summary> Pool owned by this Processor when the source is a [Prefab]. Built once and kept across equip/unequip </summary>
        private IObjectPool<GameObject> prefabPool;
        /// <summary> Parent of the idle pooled prefabs, a child of the Item so it lives and dies with it </summary>
        private Transform prefabPoolParent;
        /// <summary> Instances created by the prefab pool so far (only names the pooled objects) </summary>
        private int prefabCreated;


        public override void ItemEquipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (projectileFrom == ProjectileSource.Prefab)
            {
                //Direct Prefab: the Pouch Manager is skipped altogether, the Processor pools the prefab itself
                if (!PreparePrefabPool(item))
                {
                    Active = false;
                    return;
                }
            }
            else
            {
                pouch = item.User.Pouch;

                if (pouch == null)
                {
                    Debug.LogWarning($"<B><color=yellow>{item.name}:</color> <color=white> The Projectile Processor requires a Pouch Manager fire the projectiles, Disabling Projectile Processor</color></B>", item);
                    Active = false;
                    return;
                }

                ProjectileAmmo = pouch.GetProjectile(projectileID);

                if (ProjectileAmmo == null)
                {
                    Debug.LogWarning($"<B><color=yellow>{item.name}:</color> <color=white> The Projectile Processor requires a Pouch Manager component in the Animal to fire the projectiles</color></B>", item);
                    Active = false;
                }
            }


            if (ProjectileParent.Value == null)
                ProjectileParent.Value = item.User.Aim ? item.User.Aim.AimOrigin : item.transform;

            //MWC — optional Charge Stat used to interpolate the force (null → the force is randomized Min↔Max)
            power_stat = powerStat != null ? item.Stat_Get(powerStat) : null;
            //  spread_stat = SpreadStat != null ? item.Stat_Get(SpreadStat) : null;

            //MWC — resolve the Chamber Stat from the item's Stats component (same disable pattern as the missing Pouch above)
            if (UseChamber)
            {
                stat = item.Stat_Get(ChamberStat);

                if (stat == null)
                {
                    Debug.LogWarning($"<B><color=yellow>{item.name}:</color> <color=white> The Projectile Processor uses a Chamber but the Chamber Stat " +
                        $"[{(ChamberStat != null ? ChamberStat.name : "<Empty>")}] was not found. Add a Stats component next to the Super Item with that Stat. Disabling Projectile Processor</color></B>", item);
                    Active = false;
                    return;
                }
            }

            //MWC — default Source Stats: the wielding character. An inspector-assigned Stats (E.g. the Item's own) is kept
            if (damage.SourceStats == null && item.User != null)
            {
                damage.SourceStats = item.User.FindComponent<Stats>();
                sourceFromCharacter = damage.SourceStats != null;
            }
        }

        //MWC — release the character Stats so an unequipped item stops referencing its last wielder
        public override void ItemUnequipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (sourceFromCharacter)
            {
                damage.SourceStats = null;
                sourceFromCharacter = false;
            }

            StopPredict();
        }

        /// <summary>
        /// Clear the trajectory prediction. A ZERO velocity is the "nothing to predict" signal for the
        /// listeners (<see cref="MalbersAnimations.Weapons.MDisplayTrajectory"/> hides its line on it).
        /// <see cref="CalculateVelocity"/> only ever sends real velocities, so without this the last one stayed
        /// on screen after the Action ended and the trail was left hanging in the air.
        /// </summary>
        private void StopPredict() => Predict?.Invoke(default); //MWC: a default (zero Velocity) is still the "nothing to predict" signal

        //MWC — the Action FAILED (E.g. an [Aim Angle] limit): no shot is coming, drop the prediction right away
        public override void ActionFailed(SuperItem item, ItemActionSet set, ItemAction action) => StopPredict();

        //MWC — every other end path (released, duration, Mode ended, taken over, interrupted)
        public override void ActionEnd(SuperItem item, ItemActionSet set, ItemAction action) => StopPredict();

        //MWC — safety nets: these end the Action without going through the normal exit
        public override void SetExit(SuperItem item, ItemActionSet set, ItemAction action) => StopPredict();

        public override void OnDisable(SuperItem item, ItemActionSet set, ItemAction action) => StopPredict();

        //MWC — PreloadInChamber / ReturnToPouchFromChamber removed: the Chamber is now purely the Stat.
        //The magazine (Chamber.Value) persists on the item's Stats component across equip/unequip, and rounds are spawned
        //straight from the Pouch pool at fire time (see Execute). Reload (ItemProcessorReload) refills the Stat independently.

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            //MWC — sampled ONCE for the whole burst, so every projectile of a [Fire Amount] > 1 is fired with the
            //same draw strength (and the Stat is read a single time).
            SampledPower = power_stat != null ? Mathf.Clamp01(power_stat.NormalizedValue) : -1f;

            var amount = fireAmount.Value; //MWC — hoisted: the even spread needs a stable total for the whole burst

            //MWC — the whole burst costs [Ammo Cost] ONCE. This used to sit inside the loop, charging one round per
            //projectile, which made a multi-projectile shot (a shotgun spread, a multi-bolt spell) drain [Fire Amount]
            //rounds. Paying up front also means a burst is all-or-nothing instead of stopping halfway when it runs dry.
            if (!ConsumeAmmo(item)) return;

            //Extract the projectile from the pouch and fire it
            for (int i = 0; i < amount; i++)
            {
                //MWC — the reserve/chamber was already charged by ConsumeAmmo, so every projectile of the burst is
                //taken from the pool WITHOUT reducing anything (this was [UseChamber ? GetPooled() : Get()]).
                GameObject projectileGO = GetProjectileInstance();

                if (projectileGO == null)
                {
                    item.Debugging("Null Projectile", item); //MWC — gated (was raw Debug.Log)
                    return;
                }

                if (!projectileGO.TryGetComponent<IProjectile>(out var MProjectile)) return;

                //MWC — the pooled object comes back from a previous flight: deactivating it runs MProjectile.OnDisable,
                //which stops its coroutines (the Life timer) and restores the collider before this shot is set up.
                projectileGO.SetActive(false);

                //MWC — the shot is built RIGHT HERE now. It used to be wrapped in a Delay_Action coroutine for the
                //[Release Delay] field, which is gone: the Processor's own [Delay] already postpones this whole method,
                //and unlike that coroutine it is cancelled when the Action is interrupted.
                projectileGO.SetActive(true); // Activate the projectile right before firing it

                CalculateVelocity(item, action);

                if (UseCharacterRotation)
                    projectileGO.transform.SetPositionAndRotation(ProjectileParent.position, item.User.transform.rotation);
                else
                    projectileGO.transform.SetPositionAndRotation(ProjectileParent.position, ProjectileParent.rotation);


                if (AlignWithAim)
                    projectileGO.transform.rotation = Quaternion.LookRotation(item.User.Aim.RawAimDirection, item.User.Animal.UpVector);

                // Apply spread if needed
                if (spreadProjectile && spreadAngle > 0)
                {
                    // var spreadMult = spread_stat != null ? spread_stat.Value * spreadAngle.Value : spreadAngle.Value;
                    var spreadMult = spreadAngle.Value;

                    //MWC — [Spread Evenly]: deterministic cone ring using this projectile's index in the burst
                    Velocity = spreadEvenly
                        ? Velocity.SpreadDirectionEvenly(spreadMult, spreadDirection, i, amount)
                        : Velocity.SpreadDirectionByAngle(spreadMult, spreadDirection);
                }

                MProjectile.Prepare(item.User.gameObject, Gravity, Velocity, item.User.OverrideLayer, item.User.TriggerInteraction, Pool);

                //MWC: Movement Override - empty leaves the projectile prefab's own movement alone.
                //Only MProjectile has movements, so this is a type check rather than an IProjectile member
                if (MProjectile is Weapons.MProjectile mProjectile)
                {
                    mProjectile.SetMovementOverride(movementOverride);
                    mProjectile.SetRotationOverride(rotationOverride); //MWC: Projectile Rotation Override - same pattern
                    mProjectile.SetImpactOverride(impactOverride); //MWC: OnImpact Override - same pattern
                }

                //MWC — damage rolled per projectile, so every pellet of a burst gets its own value
                ApplyAttackDamage(MProjectile, PassAttackStat ? DamageValue(item) : 0f);

                MProjectile.Fire();
                ProcessorCompleted(item, set, action); // Mark the processor as completed after firing the projectile

                //The roll belongs to the projectile that just flew: the next pellet of the burst (and the next shot)
                //gets its own force, while the FIRST one keeps the force the trajectory preview was drawn with
                forceRolled = false;
            }
        }



        /// <summary> Next projectile to fire, taken from the source pool without touching any reserve </summary>
        protected virtual GameObject GetProjectileInstance()
        {
            if (projectileFrom == ProjectileSource.Prefab)
                return prefabPool?.Get();

            return ProjectileAmmo?.GetPooled();
        }

        /// <summary> Resolve the direct [Prefab] and build its pool once. Returns false (with a warning) when the prefab cannot be fired </summary>
        protected virtual bool PreparePrefabPool(SuperItem item)
        {
            var prefab = projectilePrefab.Value;

            if (prefab == null)
            {
                Debug.LogWarning($"<B><color=yellow>{item.name}:</color> <color=white> The Projectile Processor is set to fire a Prefab but none is assigned. Disabling Projectile Processor</color></B>", item);
                return false;
            }

            if (!prefab.TryGetComponent<IProjectile>(out _))
            {
                Debug.LogWarning($"<B><color=yellow>{item.name}:</color> <color=white> The Projectile Prefab [{prefab.name}] has no Projectile component (MProjectile). Disabling Projectile Processor</color></B>", item);
                return false;
            }

            prefab.TryGetComponent(out prefabProjectile); //Null for a custom IProjectile: the aiming then flies straight (zero gravity)

            //Built once per Item instance and kept across equip/unequip. Rebuilt only if the Item that owned it was destroyed
            if (prefabPool == null || prefabPoolParent == null)
            {
                prefabPoolParent = new GameObject($"{prefab.name} Pool").transform;
                prefabPoolParent.SetParent(item.transform, false);
                prefabPoolParent.ResetLocal();
                prefabCreated = 0;

                prefabPool = new ObjectPool<GameObject>(CreatePooledPrefab, OnTakePrefabFromPool, OnPrefabReturnedToPool, OnDestroyPooledPrefab,
                    false, Mathf.Max(1, poolInitialSize.Value), Mathf.Max(1, poolMaxSize.Value));
            }

            return true;
        }

        /// <summary> Instantiate a disabled copy of the [Prefab] under the pool parent </summary>
        private GameObject CreatePooledPrefab()
        {
            var instance = Object.Instantiate(projectilePrefab.Value, prefabPoolParent);
            instance.SetActive(false);
            instance.name = instance.name.Replace("(Clone)", $"(Pool - {prefabCreated})");
            instance.transform.ResetLocal();
            prefabCreated++;
            return instance;
        }

        /// <summary> Unparent and enable a pooled prefab about to be fired </summary>
        private void OnTakePrefabFromPool(GameObject go)
        {
            go.transform.SetParent(null, true);
            go.SetActive(true);
        }

        /// <summary> Park a returned prefab under the pool parent. Destroys it when the owning Item is already gone </summary>
        private void OnPrefabReturnedToPool(GameObject go)
        {
            if (prefabPoolParent == null) //The Item (and the parent with it) was destroyed while this projectile was flying
            {
                Object.Destroy(go);
                return;
            }

            go.transform.SetParent(prefabPoolParent, false);
            go.transform.ResetLocal();
            go.SetActive(false);
        }

        /// <summary> Destroy a pooled prefab the pool no longer keeps (over its Max Size) </summary>
        private void OnDestroyPooledPrefab(GameObject go)
        {
            if (go) Object.Destroy(go);
        }

        /// <summary> Pay the [Ammo Cost] for ONE shot. Returns false when it cannot be paid, which denies the whole burst </summary>
        protected virtual bool ConsumeAmmo(SuperItem item)
        {
            var cost = AmmoCost.Value;

            if (cost <= 0) return true; //Free shot: the ammo is not what gates this Action (E.g. a spell paid with Mana)

            //Chamber mode: the magazine pays. The Pouch reserve was already spent when the Chamber was reloaded.
            if (UseChamber)
            {
                if (stat == null || stat.Value - cost < stat.MinValue)
                {
                    item.Debugging($"Chamber has less than [{cost}] rounds. Reload required.", item);
                    return false;
                }

                stat.Value -= cost; //Fires the Stat events / UI counters
                return true;
            }

            //No Chamber and no Pouch reserve (direct Prefab): there is nothing to pay from, the shot is free
            if (ProjectileAmmo == null) return true;

            //No Chamber: the Pouch reserve pays. ReduceAmmo refuses a partial payment and is a no-op while Infinite
            if (!ProjectileAmmo.ReduceAmmo(cost))
            {
                item.Debugging($"Not enough [{(projectileID != null ? projectileID.name : "<Missing>")}] in the Pouch to pay the Ammo Cost of [{cost}].", item);
                return false;
            }

            return true;
        }

        //MWC — override the projectile's damage with this Processor's [Stat Modifier]. The Critical Chance/Multiplier
        //and Element always stay as the projectile prefab authored them. The Target Stat and the Stat Option are taken
        //from the Modifier, and fall back to the projectile's own when the Modifier's [Target] is empty.
        //Note: a projectile with [Keep Damage Values] enabled ignores this on purpose — that flag exists to let a prefab
        //keep its own damage no matter who fires it.
        //MWC — takes the already rolled <paramref name="damageValue"/> (see Execute): the number must be resolved at
        //fire time, this only stamps it onto the projectile.
        protected virtual void ApplyAttackDamage(IProjectile projectile, float damageValue)
        {
            if (!PassAttackStat || projectile is not MDamager damager) return;

            var SendStat = damage;

            //MWC — no Target authored on the Modifier: keep what the projectile prefab damages, and how
            if (SendStat.ID == null)
            {
                SendStat.ID = damager.statModifier.ID;
                SendStat.modify = damager.statModifier.modify;
            }

            SendStat.Value = damageValue; //The setter bakes the number and pins [Value From] back to [Use Value]

            projectile.PrepareDamage(SendStat, damager.CriticalChance, damager.CriticalMultiplier, damager.element);
        }

        //MWC — damage for ONE shot: whatever the [Stat Modifier] resolves to. A Min-Max roll, or the [Source]
        //expression evaluated against the modifier's [Source Stats] (E.g. Attack x Charge.Normalized — the expression is what
        //scales the damage by the charge now, the old [Scale Damage By Charge] flag is gone).
        public virtual float DamageValue(SuperItem item)
        {
            //MWC — the modifier's own [Source Stats] win (character by default, or an inspector-assigned Stats).
            //The Item's own Stats are only the fallback for an Item fired without a User
            return damage.GetValue(item.Stats); //Honors [Value From]: Min-Max roll or the Source expression
        }

        //MWC — resulting force for a shot: interpolated between Min and Max using the Charge Stat normalized value.
        //Without a Charge Stat there is nothing to interpolate with, so the force is randomized inside the range.
        public virtual float ForceValue()
        {
            //MWC — uses the charge SAMPLED at Execute time, not the live Stat: by the time this runs (inside the
            //Delay_Action coroutine) the [Charge] Processor has already emptied the Stat, which always gave 0.
            if (SampledPower >= 0) return Mathf.Lerp(MinForce.Value, MaxForce.Value, SampledPower);

            //Without a Charge Stat the force is random, but it is rolled ONCE and then kept: CalculateVelocity also
            //runs every frame to feed the trajectory preview, so re-rolling here made the drawn arc (and its hit point)
            //jump anywhere between Min and Max Force on every single frame while aiming, without the aim moving at all.
            //The roll is released per projectile in Execute, so the aimed arc is the one the first projectile flies
            if (!forceRolled)
            {
                rolledForce = Random.Range(MinForce.Value, MaxForce.Value);
                forceRolled = true;
            }

            return rolledForce;
        }

        /// <summary> A new Action means a new shot: forget the force rolled for the previous one </summary>
        public override void ActionStart(SuperItem item, ItemActionSet set, ItemAction action) => forceRolled = false;

        public virtual void CalculateVelocity(SuperItem item, ItemAction action)
        {
            var Target = item.Target ? item.Target.transform : null;

            var Aimer = item.User.Aim;
            var AimOriginPos = ProjectileParent.position;

            Power = ForceValue(); //MWC — was force.Value (single value); now a Min↔Max range driven by the Charge Stat

            var Gravity = this.Gravity;     //MWC: hoisted, the property resolves override/ammo every access
            var angle = LaunchAngle;        //MWC: Launch Angle - authored on the projectile's Flying movement now

            //MWC: Launch Angle - the arc aiming works WITHOUT a Target too: the Target wins as the destination,
            //otherwise the shot lobs at the point the Aimer is touching. Angle 0 disables the arc and fires straight
            var destination = Vector3.zero;
            var hasDestination = false;

            if (Target) { destination = Target.position; hasDestination = true; }
            else if (Aimer && useAimerDirection.Value) { destination = Aimer.RawPoint; hasDestination = true; }

            if (angle > 0 && LaunchAngleMode == Weapons.LaunchAngleMode.TiltAim)
            {
                //Tilt Aim: keeps the Force and lifts the aim direction by the angle instead (MShootable's Aim Angle).
                //Aims at the Target or the Aimer point when there is one, along the Projectile Parent otherwise
                var direction = hasDestination ? (destination - AimOriginPos).normalized : ProjectileParent.forward;

                Velocity = MTools.TiltDirection(direction, angle, Gravity) * Power;
            }
            else if (angle > 0 && Gravity != Vector3.zero && hasDestination)
            {
                var target_Direction = (destination - AimOriginPos).normalized;

                float TargetAngle = 90 - Vector3.Angle(target_Direction, -Gravity) + 0.1f; //in case the angle is smaller than the target height

                if (TargetAngle < angle)
                    TargetAngle = angle;

                Power = MTools.PowerFromAngle(AimOriginPos, destination, TargetAngle, Gravity.y); //MWC: aim with the projectile's own gravity
                Velocity = MTools.VelocityFromPower(AimOriginPos, Power, TargetAngle, destination);
            }
            else if (Aimer && useAimerDirection.Value)
            {
                Velocity = (Aimer.RawPoint - AimOriginPos).normalized * Power;
            }
            else
            {
                Velocity = ProjectileParent.forward * Power;
            }

            //MWC: push everything the preview needs, so it never has to reach back into the Item system to find it
            Predict?.Invoke(new TrajectoryData
            {
                Velocity = Velocity,
                Gravity = Gravity,
                AfterDistance = AfterDistance,
                Owner = item.User != null ? item.User.gameObject : null,
            });
        }

        //MWC: Predict is now a 4-param event. The preview used to receive only the Velocity and had to guess the rest,
        //so a Super Item shot was drawn with Physics.gravity and no After Distance. Everything it needs is pushed now,
        //which also keeps the Weapons and the Items systems free of any reference to each other
        [Tooltip("Pushes the predicted trajectory to a listener (E.g. MDisplayTrajectory.DisplayTrajectory).\nVelocity, Gravity, After Distance and the Owner")]
        public TrajectoryEvent Predict;


        public override void ActionUpdatedInternal(SuperItem item, ItemActionSet set, ItemAction action, float delta)
        {
            SampledPower = power_stat != null ? power_stat.NormalizedValue : -1f;
            CalculateVelocity(item, action);
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            var validation = string.Empty;

            if (projectileFrom == ProjectileSource.Prefab)
            {
                var prefab = projectilePrefab.Value;

                if (prefab == null) validation = $"Missing the Projectile Prefab reference.\n";
                else if (!prefab.TryGetComponent<IProjectile>(out _)) validation = $"The Projectile Prefab [{prefab.name}] has no Projectile component (MProjectile).\n";

                if (poolMaxSize.Value < poolInitialSize.Value) validation += $"[Pool Max Size] ({poolMaxSize.Value}) is lower than [Pool Initial Size] ({poolInitialSize.Value}).\n";
            }
            else if (projectileID == null) validation = $"Missing the Projectile ID reference.\n";

            //MWC — a negative cost would REFUND ammo on every shot, which is never what the author meant
            if (AmmoCost.Value < 0) validation += $"[Ammo Cost] ({AmmoCost.Value}) is negative. Use 0 for a shot that consumes no ammo.\n";
            if (ProjectileParent.Value == null) validation += $"Missing the Projectile Parent reference.";

            //MWC — validate the Min/Max Force range and the optional Charge Stat (edit-time: use TryGetComponent, SuperItem.Stats is not set yet)
            if (MaxForce.Value < MinForce.Value) validation += $"[Max Force] ({MaxForce.Value}) is lower than [Min Force] ({MinForce.Value}).\n";

            if (powerStat != null)
            {
                if (!item.TryGetComponent<Stats>(out var stats))
                    validation += $"A Charge Stat is assigned but the Item has no [Stats] component. Add one next to the Super Item with a [{powerStat.name}] Stat.\n";
                else if (stats.Stat_Get(powerStat) == null)
                    validation += $"The Item's [Stats] component does not contain the [{powerStat.name}] Stat.\n";

                //MWC — a [Delay] pushes Execute out of the release pass, and a [Charge] Processor on [Reset On: Action
                //Released] only holds its Stat for that pass, so the charge would already be empty by the time the shot
                //is built. On [Reset On: Action End] the charge survives until the Action really ends, which is exactly
                //what a delayed shot needs — so only the first case is reported.
                if (executeDelay > 0 && action.processors != null)
                {
                    foreach (var processor in action.processors)
                    {
                        if (processor is ItemProcessorCharge charge && charge.ChargeStat == powerStat
                            && charge.ResetOn == ChargeResetOn.ActionReleased)
                        {
                            validation += $"This Processor has a Delay ({executeDelay}s), which makes it read the [{powerStat.name}] Stat " +
                                $"after the [Charge] Processor emptied it, so the force would always be the Min Force. " +
                                $"Set the [Charge] Processor's [Reset On] to [Action End] so the charge survives the delay.\n";
                            break;
                        }
                    }
                }
            }

            //MWC — [Spread Evenly] needs more than one projectile to build the ring
            if (spreadProjectile && spreadEvenly && fireAmount.Value <= 1)
                validation += $"[Spread Evenly] requires a [Fire Amount] greater than 1.\n";

            //MWC — validate the Modifier's [Source] Stats. Only an inspector-assigned [Source Stats] can be checked at edit
            //time; when it is empty the Stats come from the character firing the Item (unknown here), so only the Source ID presence is checked.
            if (PassAttackStat && damage.from == StatValueFrom.Stat)
            {
                if (damage.SourceStat == null)
                    validation += "[Value From] is set to [Use Sources] but no Source Stat ID is assigned.\n";
                else
                    validation += damage.ValidateSourceStats(damage.SourceStats);
            }

            //MWC — the Chamber is validated against the Stats component next to the SuperItem (edit-time: Stats property is not set yet, so use TryGetComponent)
            if (UseChamber)
            {
                if (ChamberStat == null) validation += $"[Use Chamber] is enabled but no Chamber Stat ID is assigned.\n";
                else if (!item.TryGetComponent<Stats>(out var stats)) validation += $"[Use Chamber] is enabled but the Item has no [Stats] component. Add one next to the Super Item with a [{ChamberStat.name}] Stat.\n";
                else
                {
                    var chamberStat = stats.Stat_Get(ChamberStat);
                    if (chamberStat == null) validation += $"The Item's [Stats] component does not contain the [{ChamberStat.name}] Stat.\n";
                    else if (chamberStat.MaxValue <= 0) validation += $"The [{ChamberStat.name}] Stat Max Value is 0. Set the Max Value to the Chamber Size.\n";
                    //MWC — a cost bigger than the magazine can never be paid, so the weapon would never fire
                    else if (AmmoCost.Value > chamberStat.MaxValue)
                        validation += $"[Ammo Cost] ({AmmoCost.Value}) is higher than the [{ChamberStat.name}] Chamber Size ({chamberStat.MaxValue}). The weapon could never fire.\n";
                }
            }

            return validation;
        }
    }
}