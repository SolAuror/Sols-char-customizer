// Last Change: 10/14/2026 2:45pm
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using MalbersAnimations.Reactions;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;
using System.Runtime.CompilerServices;
using UnityEngine.Audio;







#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace MalbersAnimations.Controller
{
    /// <summary> Core Class to cause damage to the stats</summary>
    // [AddComponentMenu("Malbers/Damage/Damager")]
    public abstract class MDamager : MonoBehaviour, IMDamager, IInteractor
    {
        #region Public Variables
        [SerializeField, Tooltip("Index of the Damager, You can have multiple swords ... this identifies if a sword is different from another")]
        protected int index = 1;

        /// <summary>Enable/Disable the Damager</summary>
        [SerializeField, Tooltip("Enable/Disable the Damager")]
        protected BoolReference m_Active = new(true);


        [Tooltip("Hit Layer to interact with Objects"), ContextMenuItem("Get Layer from Root", "GetLayerFromRoot")]
        public LayerReference m_hitLayer = new(-1);

        [Tooltip("Search only Tags")]
        public MTags Tags;

        public virtual bool CanCauseDamage { get; set; }


        [Tooltip("True: the Attack Direction is calculated using the movement.\nFalse: The Attack Direction is the Character Forward Direction")]
        public bool AttackDirection = false;

        [SerializeField, Tooltip("What to do with Triggers")]
        protected QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;


        [SerializeField, Tooltip("Owner. usually the Character Owns the Damager")]
        [ContextMenuItem("Find Owner", "Find_Owner")]
        protected GameObject owner;

        //MWC — [m_HitEffect] and [hitSound] were removed. The Surface Effects asset is now the only source for Hit Effects
        [Tooltip("Hit Effects played on Impact. The entry [0] of the asset is used as the Default one")]
        public SurfaceEffects hitEffects;

        [Tooltip("Time before the Hit Effect is released back to its pool. If this value is zero, the effect is never released (it must disable itself)")]
        [Min(0)] public float HitEffectDuration = 5f; //MWC — replaces [DestroyHitEffect]. Renamed on purpose so every old asset resets to this default

        [Tooltip("Don't Hit any objects on the Owner's hierarchy")]
        public BoolReference dontHitOwner = new(true);

        [Tooltip("Don't hit targets that share a Malbers Tag with the Owner (E.g. two wolves tagged [Enemy] of the same pack). Uses the Owner's Tags component")]
        public BoolReference dontHitAllies = new(false);

        [Tooltip("Don't use the Default Reaction of the Damageable Component")]
        public Reaction2 CustomReaction;

        [Tooltip("Type of element damage the Damager can do")]
        public StatElement element;

        [Tooltip("Interactor ID to enable with who interactable the Interactor can react")]
        public IntReference interactorID = new(0);

        /// <summary> Extra Transform to Ignore Damage. E.g. The Mount Animal</summary>
        public virtual Transform IgnoreTransform { get; set; }

        [Tooltip("Damager can activate interactables")]
        public BoolReference interact = new(true);

        [Tooltip("If true the Damage Receiver will not apply its Multipliers (Profile Multiplier and Element Multipliers)")]
        public BoolReference pureDamage = new(false);

        [Tooltip("Stat to modify on the Damagee")]
        [ContextMenuItem("Set Default Damage", "Set_DefaultDamage")]
        public StatModifier statModifier = new();


        /// <summary>Miss Change (0 - 1)</summary>
        [SerializeField, Tooltip("Miss Chance (0 - 1)\n1 means it will always Miss")]
        protected FloatReference m_MissChance = new(0);

        /// <summary>Critical Change (0 - 1)</summary>
        [SerializeField, Tooltip("Critical Change (0 - 1)\n1 means it will be always critical")]
        protected FloatReference m_cChance = new(0);

        /// <summary>If the Damage is critical, the Stat modifier value will be multiplied by the Critical Multiplier</summary>
        [SerializeField, Tooltip("If the Damage is critical, the Stat modifier value will be multiplied by the Critical Multiplier")]
        protected FloatReference cMultiplier = new(2);

        [SerializeField, Tooltip("MAX Force to Apply to RigidBodies when the Damager hit them")]
        protected FloatReference m_Force = new(50f);

        [SerializeField, Tooltip("MIN Force to Apply to RigidBodies when the Damager hit them")]
        protected FloatReference minForce = new(20f);        //Weapon min Force to push rigid bodies;

        [Tooltip("Force mode to apply to the Object that the Damager Hits")]
        public ForceMode forceMode = ForceMode.VelocityChange;

        [Tooltip("Prevent damaging the same damageable twice in the same attack. Used by Attack Triggers (one hit per character each time the trigger is enabled) and Melee Weapons. Projectiles and Explosions ignore it")]
        public bool preventDuplicateDamage; //CustomPatch: Avoid multiple hits on the same damageable in the same attack

#if UNITY_6000_4_OR_NEWER
        protected HashSet<EntityId> lastHitDamageablesSet = new(5); //CustomPatch: Avoid multiple hits on the same damageable in the same attack
#else
        protected HashSet<int> lastHitDamageablesSet = new(5); //CustomPatch: Avoid multiple hits on the same damageable in the same attack
#endif
        [Tooltip("Stores the Direction of the Attack. Used to apply the Force and to know the Direction of the Hit from the Damager")]
        protected Vector3 Direction { get; set; }


        /// <summary>Point of of Contact</summary>
        public Vector3 HitPosition { get; protected set; }

        /// <summary>Rotation of the Point of contact (Normal)</summary>
        public Quaternion HitRotation { get; protected set; }

        public TransformEvent OnHit = new();
        public Vector3Event OnHitPosition = new();
        public IntEvent OnHitInteractable = new();
        public IntEvent OnProfileChanged = new();
        public GameObjectEvent OnAttackMissed = new();
        public UnityEvent OnCritical = new();

        //[Tooltip("When the Attack Trigger Touches a valid collider, it will stop the animator to give an extra effect")]
        //public BoolReference StopAnimator = new BoolReference(false);
        [Tooltip("If there's an Animator Controller it will be stopped")]
        [ContextMenuItem("Find Animator", "Find_Animator")]
        [ContextMenuItem("Clear Animator", "Clear_Animator")]
        public Animator animator;
        [Tooltip("Value of the Animator Speed when its stopped")]
        public FloatReference AnimatorSpeed = new(0.05f);
        [Tooltip("Time the Animator will be stopped. If its zero, stopping the animator is ignored")]
        public FloatReference AnimatorStopTime = new(0.1f);

        [Tooltip("Profiles to change the values of a Damager")]
        public List<DamagerProfile> Profiles;


        /// <summary>Multiplier Damage applied to the Damager stat Modifier </summary>
        private float DamageMultiplier = 1;
        public virtual void SetDamageMultiplier(float multiplier) => DamageMultiplier = multiplier;

        /// <summary>  Current Profile Index for the Damager </summary>
        protected int CurrentProfileIndex = 0;

        public DamagerProfile DefaultProfile;


        /// <summary>Damagee that can be Damaged</summary>
        //protected IMDamage damagee;

        #endregion

        #region Properties
        /// <summary>Owner of the Damager</summary>
        public virtual GameObject UserGo { get => owner; set => owner = value; }

        // MWC — Stats of the Owner/User of the Damager. Used to resolve StatModifiers whose value comes
        // from a Source Stat ID. Cached and re-resolved whenever the Owner changes (E.g. pooled projectiles)
        private Stats ownerStats;
        private GameObject ownerStatsGO;

        /// <summary> Stats component on the Owner of the Damager. Null when the Owner has no Stats </summary>
        public virtual Stats OwnerStats
        {
            get
            {
                if (ownerStatsGO != UserGo)
                {
                    ownerStatsGO = UserGo;
                    ownerStats = UserGo != null ? UserGo.FindComponent<Stats>() : null;
                }
                return ownerStats;
            }
        }

        //Tags of the Owner, cached and re-resolved whenever the Owner changes (E.g. pooled projectiles)
        private MalbersAnimations.Tags ownerTags;
        private GameObject ownerTagsGO;

        /// <summary>Tags component on the Owner of the Damager. Null when the Owner has no Tags</summary>
        public virtual MalbersAnimations.Tags OwnerTags
        {
            get
            {
                if (ownerTagsGO != UserGo)
                {
                    ownerTagsGO = UserGo;
                    ownerTags = UserGo != null ? UserGo.FindComponent<MalbersAnimations.Tags>() : null;
                }
                return ownerTags;
            }
        }

        /// <summary>True if the Damagee shares a Malbers Tag with the Owner</summary>
        public virtual bool IsAlly(GameObject damagee)
        {
            var tags = OwnerTags;
            return tags != null && tags.tags.Count > 0 && damagee.HasMalbersTagInParent(tags.tags);
        }

        /// <summary>Force of the Damager</summary>
        public virtual float Force => Mathf.Lerp(MinForce, MaxForce, UnityEngine.Random.Range(0f, 1f));

        /// <summary>Minimum Force the Weapon can do to a Rigid Body</summary>
        public virtual float MinForce { get => minForce.Value; set => minForce.Value = value; }

        /// <summary>Maximum Force the Weapon can do to a Rigid Body</summary>
        public virtual float MaxForce { get => m_Force.Value; set => m_Force.Value = value; }

        public LayerMask Layer { get => m_hitLayer.Value; set => m_hitLayer.Value = value; }

        public QueryTriggerInteraction TriggerInteraction { get => triggerInteraction; set => triggerInteraction = value; }

        /// <summary>If the Damage is critical, the Stat modifier value will be multiplied by the Critical Multiplier</summary>
        public float CriticalMultiplier { get => cMultiplier.Value; set => cMultiplier.Value = value; }

        /// <summary>>Critical Change (0 - 1)</summary>
        public float CriticalChance { get => m_cChance.Value; set => m_cChance.Value = value; }

        /// <summary>The Critical Chance reference itself (read-only). Check UseConstant before writing CriticalChance: a FloatVar is shared by every damager using it</summary>
        public FloatReference CriticalChanceReference => m_cChance;

        /// <summary>The Critical Multiplier reference itself (read-only). Check UseConstant before writing CriticalMultiplier: a FloatVar is shared by every damager using it</summary>
        public FloatReference CriticalMultiplierReference => cMultiplier;


        /// <summary>>Miss Change (0 - 1)</summary>
        public float MissChance { get => m_MissChance.Value; set => m_MissChance.Value = value; }

        /// <summary>>Index of the Damager</summary>
        public virtual int Index => index;
        public virtual int ID => interactorID.Value;


        /// <summary>Does the hit was Critical</summary>
        public bool IsCritical { get; set; }
        public bool debug;

        public AudioSource m_audio;
        //protected bool playingSound; //CustomPatch: removed because not useable in a scalable manner

        protected void PlaySound(AudioResource newSound)
        {
            if (m_audio && m_audio.isActiveAndEnabled)
            {
                m_audio.playOnAwake = false; //Make sure playOnAwake is false

                //HACK FOR THE SOUND
                // this.Delay_Action(2, () =>
                {
                    m_audio.resource = newSound;
                    m_audio.Play();

                    //playingSound = false;
                    // Debug.Log($"newSound: {newSound.name}");
                }
                //  );
            }
        }

        protected virtual bool MissAttack()
        {
            //CustomPatch: Fixed small possibility of missing the attack when the miss chance is 0
            if (MissChance == 0f)
                return false; //If the Miss Chance is 0 then we will never miss an attack

            var MissedAttack = MissChance >= UnityEngine.Random.value;  //Calculate if is critical  
            if (MissedAttack) OnAttackMissed.Invoke(owner);
            return MissedAttack;
        }

        //CustomPatch: Avoid multiple hits on the same damageable in the same attack
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void RegisterHitDamageable(IMDamage damagee)
        {
            if (damagee is UnityEngine.Object unityObj && unityObj != null)
#if UNITY_6000_4_OR_NEWER
                lastHitDamageablesSet.Add(unityObj.GetEntityId());
#else
                lastHitDamageablesSet.Add(unityObj.GetInstanceID());
#endif
        }

        //CustomPatch: Avoid multiple hits on the same damageable in the same attack
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsHitDamageableRegistered(IMDamage damagee)
        {
            if (damagee is UnityEngine.Object unityObj && unityObj != null)
#if UNITY_6000_4_OR_NEWER
                return lastHitDamageablesSet.Contains(unityObj.GetEntityId());
#else
                return lastHitDamageablesSet.Contains(unityObj.GetInstanceID());
#endif

            return false;
        }

        //CustomPatch: Avoid multiple hits on the same damageable in the same attack
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ClearHitDamageablesCache() => lastHitDamageablesSet.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: compiler hint to optimize out the cal for this method
        protected void CheckAudioSource()
        {
            if (!m_audio) m_audio = gameObject.FindComponent<AudioSource>(); //Gets the Weapon Source
            if (!m_audio)
            {
                m_audio = gameObject.AddComponent<AudioSource>(); //Create an AudioSource if there's no Audio Source on the weapon //CustomPatch: TODO: consider removing automatic audio sources creation at runtime and allow this through an optional flag (there are a lot of games using their own audio pooling systems) => this would also make it more flexible for integrating custom pooled audio sources
                m_audio.playOnAwake = false; // if the AudioSource is created from code, make sure it has playOnAwake disabled (although not recommended adding audio sources at runtime)
            }
            m_audio.spatialBlend = 1;
        }



        /// <summary>  Set/Get the Damager Active  </summary>
        public virtual bool Active
        {
            get => m_Active.Value;
            set => m_Active.Value = enabled = value;
        }


        protected IEnumerator C_Direction;
        protected IEnumerator I_CalculateDirection(Collider Trigger)
        {
            var lastPos = Trigger.bounds.center;

            var debColor = (Color.red + Color.yellow) / 2;
            debColor.a = 0.7f;


            while (CanCauseDamage)
            {
                // Debug.Log("CANCAUSE");
                var newDir = (Trigger.bounds.center - lastPos).normalized;                    //Calculate the direction of the attack
                if (newDir != Vector3.zero) Direction = newDir;
                //Direction = Owner.transform.forward;
                lastPos = Trigger.bounds.center;
                if (debug) MDebug.Draw_Arrow(Trigger.bounds.center, Direction, debColor, .5f);
                yield return null;
            }
        }

        #endregion

        /// <summary>  The Damagee does not have all the conditions to apply the Damage  </summary>
        public virtual bool IsInvalid(Collider damagee)
        {
            if (Tags != null && Tags.Length > 0)
            {
                if (!damagee.gameObject.HasMalbersTagInParent(Tags)) return true;
            }

            if (damagee.isTrigger && TriggerInteraction == QueryTriggerInteraction.Ignore) return true;    //just collapse when is a collider what we are hitting
            if (!MTools.Layer_in_LayerMask(damagee.gameObject.layer, Layer)) { return true; }        //Just hit what is on the HitMask Layer
            if (dontHitOwner && UserGo != null && damagee.transform.IsChildOf(UserGo.transform)) { return true; }   //Dont hit yourself!
            if (dontHitAllies && UserGo != null && IsAlly(damagee.gameObject)) { return true; }                       //Dont hit your pack!

            // if (damagee.gameObject.isStatic) return true;
            return false;
        }


        /// <summary>  Applies the Damage to the Game object  </summary>
        /// <returns>is False if the other gameobject didn't had a IMDamage component attached</returns>
        /// <summary> True when the last TryDamage was refused by the Damagee's Can Receive Damage conditions (e.g. it was Dodging). Damagers use it to skip their hit effects </summary>
        public bool DamageIgnored { get; protected set; }

        protected virtual bool TryDamage(IMDamage damagee, StatModifier stat, bool missedAttack = false)
        {
            DamageIgnored = false;

            if (damagee != null)
            {
                damagee.LastForceMode = forceMode; //Save the last force mode applied

                if (!stat.IsNull)
                {
                    //Ask first so the Damager can skip its hit effects. ReceiveDamage still runs so the Damagee fires its OnDamageIgnored event
                    DamageIgnored = !damagee.CanReceiveDamage();

                    var criticalStat = CheckCriticalCheckMultiplier(stat);
                    damagee.ReceiveDamage(Direction, HitPosition, UserGo, criticalStat, IsCritical, CustomReaction, pureDamage.Value, element, missedAttack);

                    if (DamageIgnored)
                    {
                        Debugging($"Damage Ignored by [{damagee.Damagee.name}]", damagee.Damagee);
                        return false;
                    }

                    Debugging($"Do Damage to [{damagee.Damagee.name}]", damagee.Damagee);
                    return true;
                }
            }
            return false;
        }

        protected void TryHitEffect(Collider col, Vector3 DamageCenter, IMDamage damagee)
        {
            if (col is MeshCollider meshCollider && !meshCollider.convex) return; //Do not hit NonConvex Collider //CustomPatch: improved: removed redundant type cast

            if (col is TerrainCollider) return; //Do not hit  a Terrain Collider

            HitPosition = col.ClosestPoint(DamageCenter); //Find the closest point on the Collider hit
            HitRotation = Quaternion.FromToRotation(Vector3.up, col.bounds.center - DamageCenter);
            OnHitPosition.Invoke(HitPosition);

            if (debug) MDebug.DrawWireSphere(HitPosition, Color.red, 0.175f, 1);

            PlaySurfaceEffect(damagee, HitPosition, HitRotation, col.transform); //MWC — the Surface Effect is the only Hit Effect now

            OnHit.Invoke(col.transform);
        }

        /// <summary> Plays the pooled Hit Effect that matches the Damagee's Surface, or the hit object's Surface Tag. Entry [0] of the asset is the Default one </summary>
        protected virtual GameObject PlaySurfaceEffect(IMDamage damagee, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (hitEffects == null) return null;

            //The Damagee's Surface wins; a plain prop (no Damagee) can still declare its Surface with a Surface Tag (ISurface)
            var surface = damagee?.Surface;
            if (surface == null && parent != null) surface = parent.FindInterface<ISurface>()?.Surface;

            var eff = hitEffects.Get(surface); //No Surface found means the Default entry [0] is used
            if (eff == null) return null;

            var instance = eff.Play(position, rotation, parent, HitEffectDuration);
            if (instance == null) return null;

            CheckHitEffect(instance);

            return instance;
        }

        //Check if the Hit Effect has a MDamager so pass the Layer and Owner (E.g. Explosions)
        protected void CheckHitEffect(GameObject hit)
        {
            //Check if the Hit Effect has a MDamager so pass the Layer and Owner (E.g. Explosions)
            var isDamager = hit.GetComponent<MDamager>();
            if (isDamager)
            {
                isDamager.UserGo = UserGo;
                isDamager.Layer = Layer;
                isDamager.TriggerInteraction = TriggerInteraction;
            }

            if (!hit.IsPrefab())
            {
                // hit.SetActive(false);
                hit.SetActive(true);
            }
        }

        protected virtual bool TryDamage(GameObject other, StatModifier stat)
        {
            return TryDamage(other.FindInterface<IMDamage>(), stat, MissAttack());
        }

        /// <summary>  Activates the Damager in case the Damager uses a Trigger</summary>
        /// <param name="value"> Value used on a Child class to enable or disable the Damager</param>
        /// <param name="profileIndex">The profile needed for the Damage</param>
        /// <param name="multiplier"></param>
        public virtual void DoDamage(bool value, int profileIndex)
        {
            if (Profiles == null) return; //Do nothing if the profiles are null

            //Debug.Log($"profileIndex: {profileIndex}, current {CurrentProfileIndex}",this);

            if (profileIndex != CurrentProfileIndex)
            {
                if (profileIndex == 0)
                {
                    DefaultProfile.Modify(this);
                    CurrentProfileIndex = 0; //Update the Profile Index
                    OnProfileChanged.Invoke(CurrentProfileIndex);
                    Debugging($"Setting Default Profile", this);
                }
                else if (profileIndex <= Profiles.Count)
                {
                    Profiles[profileIndex - 1].Modify(this);
                    CurrentProfileIndex = profileIndex; //Update the Profile Index
                    OnProfileChanged.Invoke(CurrentProfileIndex);
                    Debugging($"Setting Profile {CurrentProfileIndex}", this);
                }
            }
        }

        protected void TryStopAnimator()
        {
            if (animator != null && C_StopAnim == null)
            {
                C_StopAnim = C_StopAnimator();
                StartCoroutine(C_StopAnim);
            }
        }

        protected IEnumerator C_StopAnim;
        protected float defaultAnimatorSpeed = 1;

        protected IEnumerator C_StopAnimator()
        {
            animator.speed = AnimatorSpeed;
            yield return new WaitForSeconds(AnimatorStopTime.Value);

            if (animator)
                animator.speed = defaultAnimatorSpeed;

            C_StopAnim = null;
        }

        /// <summary>Damager can Activate Interactables </summary>
        protected bool TryInteract(GameObject damagee)
        {
            if (interact)
            {
                var interactable = damagee.FindInterface<IInteractable>();
                if (!interactable.IsUnityRefNull() && interactable.Active) //: corrected null check for unity object interface type
                {
                    return Interact(interactable);              //if we have an Local Interactor then use it instead of this Damager
                }
            }
            return false;
        }


        public void Focus(IInteractable item)
        {
            if (item.Active) //Ignore One Disable Interactors
            {
                item.Focus(this);
                if (item.Auto) Interact(item); //Interact if the interactor is on Auto
            }
        }

        public void UnFocus(IInteractable item)
        {
            if (!item.IsUnityRefNull()) //CustomPatch: corrected null check for unity object interface type
            {
                item.UnFocus(this); //Unfocus the Interactor
            }
        }

        /// <summary> Interact locally  </summary>
        public virtual bool Interact(IInteractable interactable)
        {
            if (!interactable.IsUnityRefNull())
            {
                Debugging($"Interact with <B>[{interactable.UserGo}]</B>", interactable.UserGo);
                if (interactable.Interact(this))
                {
                    OnHitInteractable.Invoke(interactable.Index);
                    return true;
                }
                return false;
            }
            return false;
        }

        public virtual void Interact() { } //Emptuy Interact of Interactor


        /// <summary> Restart method from Interactor </summary>
        public virtual void Restart() { }

        /// <summary>Apply Physics to the Damagee </summary>
        protected virtual bool TryPhysics(Rigidbody rb, Collider col, Vector3 Origin, float force)
        {
            if (rb && force > 0)
            {
                this.Direction *= force;

                if (col) //When using collider
                {
                    var HitPoint = col.ClosestPoint(Origin);
                    rb.AddForceAtPosition(Direction, HitPoint, forceMode);

                    if (debug)
                    {
                        MDebug.DrawWireSphere(HitPoint, Color.red, 0.1f, 2f);
                        MDebug.Draw_Arrow(HitPoint, Direction, Color.red, 2f);
                    }
                }
                else
                    rb.AddForce(Direction, forceMode);

                Debugging($"Apply Force to [{rb.name}]", this);


                return true;
            }
            return false;
        }

        public virtual void SetOwner(GameObject owner) => UserGo = owner;
        public virtual void SetOwner(Transform owner) => UserGo = owner.gameObject;

        /// <summary>  Prepare the modifier value to change it if is critical  </summary>
        protected virtual StatModifier CheckCriticalCheckMultiplier(StatModifier mod)
        {
            IsCritical = m_cChance > Random.value;  //Calculate if is critical

            //MWC — bake the value from the Owner Source Stat (if the modifier uses one) before it travels to the Damageable
            var modifier = new StatModifier(mod.Resolve(OwnerStats));

            if (IsCritical && CriticalChance > 0)
            {
                modifier.Value *= CriticalMultiplier;        //apply the Critical Damage and Animation Multiplier // MWC — use the resolved value

                OnCritical.Invoke(); //Invoke the Critical Event
            }

            modifier.MinValue.Value *= DamageMultiplier;
            modifier.MaxValue.Value *= DamageMultiplier;

            DamageMultiplier = 1; //Reset the Damage Multiplier always!!!

            return modifier;
        }

        protected void Find_Owner()
        {
            if (UserGo == null)
                UserGo = transform.root.gameObject;

            MTools.SetDirty(this);
        }

        protected void Find_Animator()
        {
            if (animator == null) animator = gameObject.FindComponent<Animator>();
            MTools.SetDirty(this);
        }

        protected void Clear_Animator()
        {
            animator = null;
            MTools.SetDirty(this);
        }

        /// <summary>Context Menu: sets the Stat Modifier to the default damage (Subtract 10 Health)</summary>
        protected void Set_DefaultDamage()
        {
            statModifier = new StatModifier()
            {
                ID = MTools.GetInstance<StatID>("Health"),
                modify = StatOption.SubstractValue,
                MaxValue = new FloatReference(10),
                MinValue = new FloatReference(10)
            };
            MTools.SetDirty(this);
        }

        /// <summary>Context Menu: copies the Hit Layer and Trigger Interaction from the Layer component (E.g. Aim) on the Owner, or on the root when there's no Owner</summary>
        protected void GetLayerFromRoot()
        {
            var root = UserGo != null ? UserGo : transform.root.gameObject;

            foreach (var layered in root.GetComponents<IMLayer>())
            {
                if (ReferenceEquals(layered, this)) continue;

                m_hitLayer = new LayerReference(layered.Layer); //A new Reference: writing to a shared Layer Variable would change every Damager using it
                triggerInteraction = layered.TriggerInteraction;
                MTools.SetDirty(this);
                return;
            }

            Debug.LogWarning($"[{name}] The root [{root.name}] has no component with a Hit Layer (E.g. Aim). The Hit Layer was not changed", this);
        }

        public virtual void Stat_SetMaxValue(float value) => statModifier.MaxValue = value;
        public virtual void Stat_SetMinValue(float value) => statModifier.MinValue = value;

        /// <summary> Cache all the default values of a Damager  </summary>
        public virtual void SetDefaultProfile() => DefaultProfile = GetProfile();

        public virtual DamagerProfile GetProfile()
        {
            var newProfile = new DamagerProfile()
            {
                Name = "Default",
                CustomReaction = this.CustomReaction,
                element = this.element,
                forceMode = this.forceMode,
                interact = new(this.interact),
                interactorID = new(this.interactorID),
                maxForce = new(this.m_Force),
                minForce = new(this.minForce),

                m_cChance = new(this.m_cChance),
                cMultiplier = new(this.cMultiplier),

                pureDamage = new(this.pureDamage), //MWC — [m_HitEffect] removed from the Profile
                statModifier = new(this.statModifier),
                modify = (DamagerProfile.DamageProfileModif)(-1)//Set to modify everything
            };

            return newProfile;
        }

        /// <summary> Set the values of a Damager from a profile </summary>
        protected virtual void SetProfile(DamagerProfile newProfile) => newProfile.Modify(this);


#if UNITY_EDITOR
        protected virtual void Reset()
        {
            statModifier = new StatModifier()
            {
                ID = MTools.GetInstance<StatID>("Health"),
                modify = StatOption.SubstractValue,
                MaxValue = new FloatReference(10),
                MinValue = new FloatReference(10)
            };

            m_hitLayer.Variable = MTools.GetInstance<LayerVar>("Hit Layer");
            m_hitLayer.UseConstant = false;

            var core = transform.GetComponentInParent<IObjectCore>();

            owner = !core.IsUnityRefNull() ? core.transform.gameObject : transform.root.gameObject; //CustomPatch: corrected null check for unity object interface type
        }



        public static void DrawTriggers(Transform transform, Collider Trigger, Color DebugColor, bool selected = false)
        {
#if MALBERS_DEBUG
            if (Trigger == null) return;

            Gizmos.color = DebugColor;
            Gizmos.matrix = Trigger.transform.localToWorldMatrix;

            var DColorFlat = new Color(DebugColor.r, DebugColor.g, DebugColor.b, 1f);

            if (selected) DColorFlat = Color.yellow;

            if (Trigger is BoxCollider)
            {
                BoxCollider _C = Trigger as BoxCollider;

                var pos = _C.center;
                var sca = _C.size;

                if (!Application.isPlaying || Application.isPlaying && Trigger.enabled)
                {
                    Gizmos.DrawCube(pos, sca);
                    Gizmos.color = DColorFlat;
                    Gizmos.DrawWireCube(pos, sca);
                }

            }
            else if (Trigger is SphereCollider)
            {
                SphereCollider _C = Trigger as SphereCollider;

                if (!Application.isPlaying || Application.isPlaying && Trigger.enabled)
                {
                    Gizmos.DrawSphere(_C.center, _C.radius);
                    Gizmos.color = DColorFlat;
                    Gizmos.DrawWireSphere(_C.center, _C.radius);
                }
            }
#endif
        }

#endif

        //MWC: the body was already editor-only, but the CALLERS still built their interpolated strings in a player
        //build and threw them away here. [Conditional] removes the call AND its arguments at every call site when
        //UNITY_EDITOR is not defined, so those allocations disappear from builds entirely. Behaviour is unchanged:
        //this method already did nothing there
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void Debugging(string value, Object obj, string m_color = "yellow")
        {
#if UNITY_EDITOR
            if (debug)
                MDebug.Log($"<B>[{name}]</B> → <color={m_color}> <B>[{value}]</B> </color>", obj);
#endif
        }
    }

    [System.Serializable]
    public struct DamagerProfile
    {
        public enum DamageProfileModif
        {
            Damage = 1,
            Interact = 2,
            Reaction = 4,
            CriticalChance = 8,
            Force = 16,
            ElementalDamage = 32,
            MissChance = 64
        }

        [Tooltip("Name of the Profile")]
        public string Name;

        [Flag]
        public DamageProfileModif modify;

        [Tooltip("Damager can activate interactables")]
        public BoolReference interact;
        [Tooltip("Interactor ID to enable with who interactable the Interactor can react")]
        public IntReference interactorID;

        [Tooltip("Damager allows the Damagee to apply an animal reaction")]
        public BoolReference react;

        [Tooltip("If true the Damage Receiver will not apply its Default Multiplier")]
        public BoolReference pureDamage;

        [Tooltip("Stat to modify on the Damagee")]
        public StatModifier statModifier;

        [Tooltip("Critical Change (0 - 1)\n1 means it will be always critical")]
        public FloatReference m_cChance;

        [Tooltip("Miss Chance (0 - 1)\n1 means it will always Miss")]
        public FloatReference m_MissChance;

        [Tooltip("If the Damage is critical, the Stat modifier value will be multiplied by the Critical Multiplier")]
        public FloatReference cMultiplier;

        [SerializeField, Tooltip("MAX Force to Apply to RigidBodies when the Damager hit them")]
        public FloatReference maxForce;

        [SerializeField, Tooltip("MIN Force to Apply to RigidBodies when the Damager hit them")]
        public FloatReference minForce;

        [Tooltip("Force mode to apply to the Object that the Damager Hits")]
        public ForceMode forceMode;

        //MWC — [m_HitEffect] removed. Hit Effects come from the Damager's Surface Effects asset

        [Tooltip("Don't use the Default Reaction of the Damageable Component")]
        public Reaction2 CustomReaction;

        [Tooltip("Type of element damage the Damager can do")]
        public StatElement element;

        public readonly void Modify(MDamager damager)
        {
            if (modify == 0) return; //Means that the animal have no modification

            if (Modify(DamageProfileModif.Damage))
            {
                damager.statModifier = new StatModifier(statModifier);
                damager.pureDamage = pureDamage;
            }

            if (Modify(DamageProfileModif.Interact))
            {
                damager.interact = interact;
                damager.interactorID = interactorID;
            }
            if (Modify(DamageProfileModif.Reaction))
            {
                damager.CustomReaction = CustomReaction;
            }

            if (Modify(DamageProfileModif.CriticalChance))
            {
                damager.CriticalChance = m_cChance;
                damager.CriticalMultiplier = cMultiplier.Value;
            }
            if (Modify(DamageProfileModif.Force))
            {
                damager.MaxForce = maxForce;
                damager.MinForce = minForce;
            }

            if (Modify(DamageProfileModif.ElementalDamage))
            {
                damager.element = element;
            }
        }

        private readonly bool Modify(DamageProfileModif modifier) => ((modify & modifier) == modifier);
    }


    ///--------------------------------INSPECTOR-------------------
    ///
#if UNITY_EDITOR
    //CustomPatch: added "editorForChildClasses" to auto-offer the weapon editor to custom weapons as a default editor
    [CustomEditor(typeof(MDamager), editorForChildClasses: true), CanEditMultipleObjects]

    public class MDamagerEd : Editor
    {
        protected MDamager MD;
        protected SerializedProperty Force, minForce, forceMode, index, Tags,
            statModifier, onhit, OnAttackMissed,
            OnHitPosition, OnHitInteractable, OnProfileChanged, OnCritical,
            dontHitOwner, dontHitAllies, owner, m_Active, debug, m_audio,
            hitLayer, triggerInteraction, AttackDirection,
            m_cChance, cMultiplier, element, pureDamage, CustomReaction, interact, m_MissChance,
            HitEffects, //MWC — [m_HitEffect] and [hitSound] removed
            interactorID, HitEffectDuration, Profiles,
            StopAnimator, AnimatorSpeed, AnimatorStopTime, animator;

        //CustomPatch: Avoid multiple hits on the same damageable in the same attack
        protected SerializedProperty preventDuplicateDamage;


        private ReorderableList Reo_Profiles;

        protected virtual void OnEnable() => FindBaseProperties();

        protected virtual void FindBaseProperties()
        {
            MD = (MDamager)target;
            index = serializedObject.FindProperty("index");
            HitEffects = serializedObject.FindProperty("hitEffects"); //MWC — [m_HitEffect] and [hitSound] removed
            OnHitPosition = serializedObject.FindProperty("OnHitPosition");
            m_Active = serializedObject.FindProperty("m_Active");
            hitLayer = serializedObject.FindProperty("m_hitLayer");
            triggerInteraction = serializedObject.FindProperty("triggerInteraction");
            dontHitOwner = serializedObject.FindProperty("dontHitOwner");
            dontHitAllies = serializedObject.FindProperty("dontHitAllies");


            AttackDirection = serializedObject.FindProperty("AttackDirection");
            owner = serializedObject.FindProperty("owner");
            interactorID = serializedObject.FindProperty("interactorID");
            HitEffectDuration = serializedObject.FindProperty(nameof(MD.HitEffectDuration));
            element = serializedObject.FindProperty("element");

            Profiles = serializedObject.FindProperty("Profiles");

            CustomReaction = serializedObject.FindProperty("CustomReaction");
            OnCritical = serializedObject.FindProperty("OnCritical");

            interact = serializedObject.FindProperty("interact");
            pureDamage = serializedObject.FindProperty("pureDamage");

            m_cChance = serializedObject.FindProperty("m_cChance");
            cMultiplier = serializedObject.FindProperty("cMultiplier");

            m_MissChance = serializedObject.FindProperty("m_MissChance");

            minForce = serializedObject.FindProperty("minForce");
            Force = serializedObject.FindProperty("m_Force");
            forceMode = serializedObject.FindProperty("forceMode");
            Tags = serializedObject.FindProperty("Tags");

            statModifier = serializedObject.FindProperty("statModifier");

            onhit = serializedObject.FindProperty("OnHit");
            OnAttackMissed = serializedObject.FindProperty("OnAttackMissed");
            OnHitInteractable = serializedObject.FindProperty("OnHitInteractable");
            OnProfileChanged = serializedObject.FindProperty("OnProfileChanged");
            debug = serializedObject.FindProperty("debug");


            StopAnimator = serializedObject.FindProperty("StopAnimator");
            animator = serializedObject.FindProperty("animator");
            AnimatorSpeed = serializedObject.FindProperty("AnimatorSpeed");
            AnimatorStopTime = serializedObject.FindProperty("AnimatorStopTime");
            m_audio = serializedObject.FindProperty("m_audio");


            preventDuplicateDamage = serializedObject.FindProperty(nameof(MD.preventDuplicateDamage)); //CustomPatch: Avoid multiple hits on the same damageable in the same attack


            Reo_Profiles = new ReorderableList(serializedObject, Profiles, true, true, true, true)
            {
                onAddCallback = (list) =>
                {
                    var count = Profiles.arraySize;
                    Profiles.InsertArrayElementAtIndex(count);
                    var element = Profiles.GetArrayElementAtIndex(count);
                    serializedObject.ApplyModifiedProperties();

                    var newPro = MD.GetProfile();
                    newPro.Name = $"New Profile [{count + 1}]";
                    MD.Profiles[count] = newPro;
                    MTools.SetDirty(target);
                },
                drawHeaderCallback = (rect) =>
                {
                    EditorGUI.LabelField(rect, "[Index]      [Damager Profiles]");
                },
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    var elementRect = new Rect(rect);
                    elementRect.height = EditorGUIUtility.singleLineHeight;

                    var IndexRect = new Rect(elementRect);
                    var ModifyRect = new Rect(elementRect);
                    IndexRect.width = 25;
                    elementRect.x += 30;
                    elementRect.width = rect.width * 0.6f - 30;
                    ModifyRect.width = rect.width * 0.4f - 10;
                    ModifyRect.x += elementRect.width + 40;

                    var element = Profiles.GetArrayElementAtIndex(index);
                    var name = element.FindPropertyRelative("Name");
                    var modify = element.FindPropertyRelative("modify");

                    //draw the modify ss


                    elementRect.y += 2;
                    ModifyRect.y += 2;

                    EditorGUI.LabelField(IndexRect, $"[{index + 1}]");
                    EditorGUI.PropertyField(elementRect, name, GUIContent.none);

                    EditorGUIUtility.labelWidth = 40;
                    EditorGUI.PropertyField(ModifyRect, modify);
                    EditorGUIUtility.labelWidth = 0;
                }
            };
        }



        protected void DrawProfiles()
        {
            Reo_Profiles.DoLayoutList();

            if (Reo_Profiles.index != -1 && Reo_Profiles.index < Reo_Profiles.count)
            {
                var e = Profiles.GetArrayElementAtIndex(Reo_Profiles.index);


                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(e, false);
                    EditorGUI.indentLevel--;

                    if (e.isExpanded)
                    {
                        var modify = e.FindPropertyRelative("modify");

                        var m = (DamagerProfile.DamageProfileModif)modify.intValue;

                        if (((m & DamagerProfile.DamageProfileModif.Damage) == DamagerProfile.DamageProfileModif.Damage))
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.PropertyField(e.FindPropertyRelative("statModifier"));
                                EditorGUILayout.PropertyField(e.FindPropertyRelative("pureDamage"));
                            }
                        }

                        if (((m & DamagerProfile.DamageProfileModif.Interact) == DamagerProfile.DamageProfileModif.Interact))
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.PropertyField(e.FindPropertyRelative("interact"));
                                EditorGUILayout.PropertyField(e.FindPropertyRelative("interactorID"));
                            }
                        }

                        if (((m & DamagerProfile.DamageProfileModif.Reaction) == DamagerProfile.DamageProfileModif.Reaction))
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.PropertyField(e.FindPropertyRelative("react"));
                                EditorGUILayout.PropertyField(e.FindPropertyRelative("CustomReaction"));
                            }
                        }

                        if (((m & DamagerProfile.DamageProfileModif.CriticalChance) == DamagerProfile.DamageProfileModif.CriticalChance))
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                using (new GUILayout.HorizontalScope())
                                {
                                    EditorGUILayout.PropertyField(e.FindPropertyRelative("m_cChance"), new GUIContent("Chance [0-1]"), GUILayout.MinWidth(50));
                                    EditorGUIUtility.labelWidth = 47;
                                    EditorGUILayout.PropertyField(e.FindPropertyRelative("cMultiplier"), new GUIContent("Mult"), GUILayout.MinWidth(50));
                                    EditorGUIUtility.labelWidth = 0;
                                }
                            }

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.PropertyField(e.FindPropertyRelative("m_MissChance"));
                            }
                        }

                        if (((m & DamagerProfile.DamageProfileModif.Force) == DamagerProfile.DamageProfileModif.Force))
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.PropertyField(e.FindPropertyRelative("minForce"));
                                EditorGUILayout.PropertyField(e.FindPropertyRelative("maxForce"));
                            }
                        }

                        if (((m & DamagerProfile.DamageProfileModif.ElementalDamage) == DamagerProfile.DamageProfileModif.ElementalDamage))
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.LabelField("Elemental Damage", EditorStyles.boldLabel);
                                EditorGUILayout.PropertyField(e.FindPropertyRelative("element"));
                            }
                        }
                    }
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDescription("Damager Core Logic");
            // DrawScript();
            DrawGeneral();
            DrawPhysics();
            DrawCriticalDamage();
            DrawStatModifier();
            DrawMisc();
            DrawEvents();
            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DrawEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (var X = new GUILayout.ScrollViewScope(ScrollEvents, GUILayout.MaxHeight(400)))
                {
                    ScrollEvents = X.scrollPosition;
                    EditorGUILayout.PropertyField(onhit);
                    EditorGUILayout.PropertyField(OnAttackMissed);
                    EditorGUILayout.PropertyField(OnHitPosition);
                    EditorGUILayout.PropertyField(OnHitInteractable);
                    EditorGUILayout.PropertyField(OnProfileChanged);
                    EditorGUILayout.PropertyField(OnCritical);
                    DrawCustomEvents();
                }

            }
        }

        private Vector2 ScrollEvents;

        protected virtual void DrawCustomEvents() { }


        protected virtual void DrawMisc(bool drawbox = true)
        {
            if (drawbox) EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            interact.isExpanded = MalbersEditor.Foldout(interact.isExpanded, "Interactions");
            if (interact.isExpanded)
            {
                EditorGUILayout.PropertyField(CustomReaction);

                EditorGUILayout.PropertyField(interact);

                if (MD.interact.Value)
                    EditorGUILayout.PropertyField(interactorID);
            }

            HitEffects.isExpanded = MalbersEditor.Foldout(HitEffects.isExpanded, "Effects");

            if (HitEffects.isExpanded)
            {
                //MWC — [Hit Sound] and [Hit Effect] removed. Everything comes from the Surface Effects asset
                EditorGUILayout.PropertyField(m_audio, new GUIContent("Audio Source"));

                EditorGUILayout.PropertyField(HitEffects, new GUIContent("Hit Effects"));

                if (MD.hitEffects != null)
                {
                    EditorGUILayout.PropertyField(HitEffectDuration, new GUIContent("Effect Duration"));

                    EditorGUILayout.HelpBox(
                        MD.HitEffectDuration > 0 ?
                        "The Hit Effect is taken from the Surface Effects pool, positioned and oriented using the hit position, and released back after the Effect Duration" :
                        "Effect Duration is zero: the Hit Effect will never return to its pool. Set a value or make the effect disable itself",
                        MD.HitEffectDuration > 0 ? MessageType.Info : MessageType.Warning);
                }
            }
            //  EditorGUILayout.Space();

            AnimatorStopTime.isExpanded = MalbersEditor.Foldout(AnimatorStopTime.isExpanded, "Stop Animator");

            if (AnimatorStopTime.isExpanded)
            {
                EditorGUILayout.PropertyField(AnimatorStopTime);

                if (MD.AnimatorStopTime.Value > 0)
                {
                    EditorGUILayout.PropertyField(AnimatorSpeed);
                    EditorGUILayout.PropertyField(animator);
                }
            }
            if (drawbox) EditorGUILayout.EndVertical();
        }

        protected virtual void DrawGeneral(bool drawbox = true)
        {
            if (drawbox) EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(m_Active);
                MalbersEditor.DrawDebugIcon(debug);
            }

            EditorGUILayout.PropertyField(index);
            EditorGUILayout.PropertyField(hitLayer);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(Tags);
            //CustomPatch: Avoid multiple hits on the same damageable in the same attack
            EditorGUILayout.PropertyField(preventDuplicateDamage);
            EditorGUI.indentLevel--;
            EditorGUILayout.PropertyField(triggerInteraction);
            EditorGUILayout.PropertyField(AttackDirection);

            EditorGUILayout.PropertyField(dontHitOwner, new GUIContent("Don't hit Owner"));
            EditorGUILayout.PropertyField(dontHitAllies, new GUIContent("Don't hit Allies"));
            if (MD.dontHitOwner.Value || MD.dontHitAllies.Value) //Both options need the Owner
            {
                EditorGUILayout.PropertyField(owner);
                // Debug.Log("MD = " + MD.Owner);
            }

            if (drawbox) EditorGUILayout.EndVertical();
        }

        protected virtual void DrawPhysics(bool drawbox = true)
        {
            if (drawbox) EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                Force.isExpanded = MalbersEditor.Foldout(Force.isExpanded, "Physics Force");

                if (Force.isExpanded)
                {
                    EditorGUILayout.PropertyField(minForce, new GUIContent("Min Force", "Minimum Force to apply to a hit rigid body"));
                    EditorGUILayout.PropertyField(Force, new GUIContent("Max Force", "Maximum Force to apply to a hit rigid body"));
                    EditorGUILayout.PropertyField(forceMode);
                }
            }
            if (drawbox) EditorGUILayout.EndVertical();
        }

        protected virtual void DrawCriticalDamage(bool drawbox = true)
        {
            if (drawbox) EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Critical Damage", EditorStyles.boldLabel);

            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(m_cChance, new GUIContent("Chance [0-1]"), GUILayout.MinWidth(50));
                EditorGUIUtility.labelWidth = 47;
                EditorGUILayout.PropertyField(cMultiplier, new GUIContent("Mult"), GUILayout.MinWidth(50));
                EditorGUIUtility.labelWidth = 0;
            }


            EditorGUILayout.PropertyField(m_MissChance);

            if (drawbox) EditorGUILayout.EndVertical();
        }

        protected virtual void DrawStatModifier(bool drawbox = true)
        {
            if (drawbox) EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(statModifier,
                new GUIContent("Stat Modifier", "Which Stat will be affected on the Object to hit after Impact"), true);
            EditorGUILayout.PropertyField(pureDamage);

            DrawElement();

            if (drawbox) EditorGUILayout.EndVertical();
        }

        protected void DrawElement()
        {
            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Element", "Type of Element this weapon can inflict"), GUILayout.Width(60));
                EditorGUILayout.PropertyField(element);
            }
        }


        protected void DrawDescription(string desc) => MalbersEditor.DrawDescription(desc);


        public bool Foldout(bool prop, string name)
        {
            EditorGUI.indentLevel++;
            prop = GUILayout.Toggle(prop, name, EditorStyles.foldoutHeader);
            EditorGUI.indentLevel--;
            return prop;
        }
    }
#endif
}