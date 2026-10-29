// Last Change: 9/24/2026 1:16pm
using MalbersAnimations.Reactions;
using MalbersAnimations.Conditions;
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using System.Collections;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [DisallowMultipleComponent]
    /// <summary> Damager Receiver</summary>
    [AddComponentMenu("Malbers/Damage/MDamageable")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/secondary-components/mdamageable")]
    public class MDamageable : MonoBehaviour, IMDamage, IRestart
    {
        [Tooltip("Animal Reaction to apply when the damage is done")]
        public Component character;

        [Tooltip("Animal Reaction to apply when the damage is done")]
        public Reaction2 damageReaction;

        [Tooltip("Animal Reaction when it receives a critical damage")]
        public Reaction2 criticalReaction;

        [Tooltip("Reaction sent to the Damager if it hits this Damageable")]
        public Reaction2 damagerReaction;

        [Tooltip("Type of surface the Damageable is. (Flesh, Metal, Wood,etc)")]
        public SurfaceID surface;

        [Tooltip("Conditions that must be true for this Damageable to receive damage (Default Profile). Each Profile can override them. Target = the Character. E.g. skip the damage while the Animal is playing the Dodge Mode")]
        public Conditions2 canReceiveDamage = new(0);

        public Transform Transform => transform;

        [Tooltip("The Damageable will ignore the Reaction coming from the Damager. Use this when this Damager Needs to have the Default Reaction")]
        [SerializeField] private BoolReference ignoreDamagerReaction = new();

        [Tooltip("Stats component to apply the Damage")]
        public Stats stats;

        [Tooltip("Multiplier for the Stat modifier Value. Use this to increase or decrease the final value of the Stat")]
        public FloatReference multiplier = new(1);

        [Tooltip("When Enabled the animal will rotate towards the Damage direction")]
        public BoolReference AlignToDamage = new();

        [Tooltip("Only Align to Damage when Movement is Not Detected")]
        public BoolReference OnlyOnMovementZero = new(true);

        [Tooltip("Time to align to the damage direction")]
        public FloatReference AlignTime = new(0.25f);
        [Tooltip("Alignment curve")]
        public AnimationCurve AlignCurve = new(MTools.DefaultCurve);

        [Tooltip("Point Forward to align the animal to the Damage, It will rotate around this point")]
        public FloatReference AlignOffset = new();

        [Tooltip("Main Damageable of the character when this one is a Hit Box (Head, Chest…). Damage events and the Damager are forwarded to it, and its Receive Damage conditions apply here too")]
        public MDamageable Root;

        /// <summary>IMDamage.Root: the main Damageable of the character (this one when it has no Root)</summary>
        IMDamage IMDamage.Root => Root != null ? Root : this;
        public DamagerEvents events;

        public Vector3 HitDirection { get; set; }
        public Vector3 HitPosition { get; set; }
        public GameObject Damager { get; set; }
        public Collider HitCollider { get; set; }
        public ForceMode LastForceMode { get; set; }
        public SurfaceID Surface => CurrentProfile.surface;
        public GameObject Damagee => gameObject;

        public bool IgnoreDamagerReaction { get => ignoreDamagerReaction; set => ignoreDamagerReaction.Value = value; }

        public DamageData LastDamage;

        [Tooltip("Elements that affect the MDamageable")]
        public List<ElementMultiplier> elements = new();

        public MDamageableProfile Default;
        public MDamageableProfile CurrentProfile;

        [Tooltip("The Damageable can Change profiles to Change the way the Animal React to the Damage")]
        public List<MDamageableProfile> profiles = new();

        [HideInInspector] public int Editor_Tabs1;

        private ICharacterMove characterMove;

        private void Start()
        {
            //Store the default values
            Default = new("Default", surface,
                damageReaction, criticalReaction, damagerReaction, ignoreDamagerReaction,
                multiplier, AlignToDamage, elements)
            {
                OnProfileEnter = events.OnProfileDefaultEnter,
                OnProfileExit = events.OnProfileDefaultExit,
                canReceiveDamage = this.canReceiveDamage //The Default profile wraps the top-level conditions
            };
            Default.OnProfileEnter?.Invoke();

            CurrentProfile = Default;

            profiles ??= new();
        }

        protected void OnEnable()
        {
            if (stats == null)
                stats = this.FindComponent<Stats>();

            if (stats != null)
            {
                //Only resolve the Character when it was not assigned in the Inspector
                if (character == null && damageReaction.IsValid)
                    character = stats.GetComponent(damageReaction.ReactionType); //Find the character where the Stats are

                if (character == null) character = stats.transform; //Fallback: the Stats owner
            }

            //Check if we have a Character Move in the Animal
            if (character != null) characterMove = character.FindInterface<ICharacterMove>();

            PreDamage.ClearData();
        }

        protected void OnDisable()
        {
            StopAllCoroutines();
            IApplyDamageNextFrame = null; //Clear the handle, otherwise no buffered damage can start again after re-enabling
            IAlignCoroutine = null;
        }

        /// <summary> Restore the Default Damageable profile </summary>
        public virtual void Profile_Restore()
        {
            CurrentProfile.OnProfileExit?.Invoke();
            CurrentProfile = Default;
            CurrentProfile.OnProfileEnter?.Invoke();
        }

        //public virtual MDamageableProfile GetCurrentProfile() => CurrentProfile;


        public virtual void Profile_Set(string name)
        {
            if (string.IsNullOrEmpty(name) || string.Equals(name, "default", System.StringComparison.OrdinalIgnoreCase))
            {
                Profile_Restore();
            }
            else
            {
                var index = profiles.FindIndex(p => p.name == name);

                if (index != -1)
                {
                    CurrentProfile.OnProfileExit?.Invoke();
                    CurrentProfile = new(profiles[index]);
                    CurrentProfile.OnProfileEnter?.Invoke();
                }
            }
        }


        public virtual void Profile_Set(MDamageableProfile newProfile)
        {
            CurrentProfile.OnProfileExit?.Invoke();
            CurrentProfile = new(newProfile);
            CurrentProfile.OnProfileEnter?.Invoke();
        }

        public DamageData PreDamage;

        public virtual void PrepareDamage(Vector3 Direction, Vector3 HitPosition)
        {
            PreDamage.Direction = Direction;
            PreDamage.HitPosition = HitPosition;
            BufferApplyDamage();
        }

        /// <summary> Prepare the Damage setting a Custom Reaction </summary>
        public virtual void PrepareDamage(Reaction2 customReaction)
        {
            PreDamage.reaction = customReaction;
            BufferApplyDamage();
        }

        /// <summary> Prepare the Damage setting the Damager GameObject </summary>
        public virtual void PrepareDamage(GameObject Damager)
        {
            PreDamage.Damager = Damager;
            BufferApplyDamage();
        }

        /// <summary> Prepare the Damage applying the Critical Multiplier </summary>
        public virtual void PrepareDamage(bool critical, float Multiplier)
        {
            if (critical)
                PreDamage.MultiplyAll(Multiplier);                     //MWC — scale ALL accumulated modifiers (was only PreDamage.stat)

            PreDamage.critical = critical;
            BufferApplyDamage();
        }

        /// <summary> Prepare the Damage applying the Critical Multiplier </summary>
        public virtual void PrepareDamage(bool missDamage)
        {
            PreDamage.Missed = missDamage;
            BufferApplyDamage();
        }

        public virtual void PrepareDamage(StatModifier stat, bool pure)
        {
            PreDamage.AddStat(stat); //MWC — additive (was 'PreDamage.stat = stat'): several Damager processors stack into one hit
            PreDamage.Pure = pure;
            BufferApplyDamage();
        }

        /// <summary> Prepare the Damage applying the Element Multiplier </summary>
        public virtual void PrepareDamage(ElementMultiplier element)
        {
            PreDamage.Element = element;
            BufferApplyDamage();
        }

        //Per-hit Profile override: a Damager (E.g. a Heavy Attack breaking a Block) can make the NEXT hit resolve with a different
        //Profile without moving the character off the one it is on. The hit uses the override's Multiplier, Reactions, Elements
        //and Conditions; once it is applied the previous Profile carries on untouched.
        /// <summary> True when a Profile override is waiting for the buffered hit </summary>
        private bool hasHitProfile;
        /// <summary> Profile the next buffered hit resolves with </summary>
        private MDamageableProfile hitProfile;

        /// <summary> Prepare the Damage resolving this hit with the given Profile instead of the current one (the current Profile is kept afterwards) </summary>
        public virtual void PrepareDamage(MDamageableProfile profile)
        {
            hitProfile = new(profile);
            hasHitProfile = true;
            BufferApplyDamage();
        }

        /// <summary> Prepare the Damage resolving this hit with one of this Damageable's own Profiles found by name ("Default" = the Default Profile). An unknown name leaves the current Profile in charge </summary>
        public virtual void PrepareDamage(string profileName)
        {
            if (TryGetProfile(profileName, out var found)) PrepareDamage(found);
        }

        /// <summary> Finds one of this Damageable's Profiles by name. Empty or "Default" returns the Default Profile </summary>
        public virtual bool TryGetProfile(string name, out MDamageableProfile profile)
        {
            if (string.IsNullOrEmpty(name) || string.Equals(name, "default", System.StringComparison.OrdinalIgnoreCase))
            {
                profile = Default;
                return true;
            }

            var index = profiles != null ? profiles.FindIndex(p => p.name == name) : -1;
            profile = index != -1 ? profiles[index] : default;
            return index != -1;
        }

        /// <summary> True while the Damageable is on its Default Profile (no Block, Parry… Profile is active) </summary>
        public bool IsOnDefaultProfile => string.Equals(CurrentProfile.name, Default.name);

        protected virtual void BufferApplyDamage()
        {
            if (IApplyDamageNextFrame == null)
            {
                IApplyDamageNextFrame = ApplyDamageNextFrame();
                StartCoroutine(IApplyDamageNextFrame);
            }
        }

        IEnumerator IApplyDamageNextFrame;
        IEnumerator ApplyDamageNextFrame()
        {
            yield return null; //Wait for the next frame to apply the damage
            ApplyPreparedDamage();
        }


        /// <summary>
        /// Applies the damage that has been prepared using the PrepareDamage methods. 
        /// This method should be called after all necessary damage parameters have been set.
        /// </summary>
        public virtual void ApplyPreparedDamage()
        {
            // MWC: Cancel the pending coroutine so calling this directly doesn't cause a second
            // zero-data hit when the coroutine resumes next frame.
            if (IApplyDamageNextFrame != null)
            {
                StopCoroutine(IApplyDamageNextFrame);
                IApplyDamageNextFrame = null;
            }

            //Per-hit Profile override (E.g. a Heavy Attack resolving against [Guard Break] while the character is on [Block])
            var swapProfile = hasHitProfile;
            var prevProfile = CurrentProfile;

            if (swapProfile)
            {
                hasHitProfile = false;
                CurrentProfile = hitProfile;
                CurrentProfile.OnProfileEnter?.Invoke();
            }

            ReceiveDamage(PreDamage.Direction, PreDamage.HitPosition, PreDamage.Damager, PreDamage.stat, PreDamage.critical, PreDamage.reaction, PreDamage.Pure, PreDamage.Element.element, PreDamage.Missed);

            //MWC — apply any additional DISTINCT-target modifiers accumulated this hit (same-target damage was already summed
            //into PreDamage.stat above). Each is a full ReceiveDamage, so a hit that damages two different Stats reacts per Stat.
            if (PreDamage.extraStats != null)
            {
                for (int i = 0; i < PreDamage.extraStats.Count; i++)
                    ReceiveDamage(PreDamage.Direction, PreDamage.HitPosition, PreDamage.Damager, PreDamage.extraStats[i],
                        PreDamage.critical, PreDamage.reaction, PreDamage.Pure, PreDamage.Element.element, PreDamage.Missed);
            }

            //Put the previous Profile back, unless a Reaction inside the hit already moved the Damageable to another Profile: keep that one
            if (swapProfile && string.Equals(CurrentProfile.name, hitProfile.name))
            {
                CurrentProfile.OnProfileExit?.Invoke();
                CurrentProfile = prevProfile;
            }

            //Clear the PreDamage
            PreDamage.ClearData();
        }


        //-*********************************************************************--
        /// <summary>  Main Receive Damage Method!!! </summary>
        /// <param name="Direction">The Direction the Damage is coming from</param>
        /// <param name="Position">The position of the hit</param>
        /// <param name="Damager">Game Object doing the Damage</param>
        /// <param name="damage">Stat Modifier containing the Stat ID, what to modify and the Value to modify</param>
        /// <param name="isCritical">is the Damage Critical?</param>
        /// <param name="react">Does the Damage that is coming has a Custom Reaction? </param>
        /// <param name="customReaction">The Attacker Brings a custom Reaction to override the Default one</param>
        /// <param name="pureDamage">Pure damage means that the multipliers wont be applied</param>
        /// <param name="element"></param>
        public virtual void ReceiveDamage(Vector3 Direction, Vector3 Position, GameObject Damager, StatModifier damage,
            bool isCritical, Reaction2 customReaction, bool pureDamage, StatElement element, bool missed)
        {
            if (!enabled) return;       //This makes the Animal Immortal.
            if (!CanReceiveDamage()) //The Receive Damage conditions failed (e.g. the Animal is Dodging)
            {
                events.OnDamageIgnored.Invoke(Damager);
                if (Root) Root.events.OnDamageIgnored.Invoke(Damager);
                return;
            }

            HitDirection = Direction;   //Store the Last Direction
            HitPosition = Position;   //Store the Last Position

            SetDamageable(Direction, Damager);                                  //Store the Damager BEFORE the reactions so the Damager Reaction hits the current attacker
            if (Root) Root.SetDamageable(Direction, Damager);                     //Send the Direction and Damager to the Root

            if (missed) //The attack missed: store it for the UI and notify, but play no reaction and leave the Stat untouched
            {
                LastDamage = new DamageData(Direction, HitPosition, Damager, gameObject, damage, isCritical, new ElementMultiplier(element, 1), customReaction, true);
                if (Root) Root.LastDamage = LastDamage;

                events.OnDamageMissed.Invoke(Damager);
                if (Root) Root.events.OnDamageMissed.Invoke(Damager);
                return;
            }

            var DamageReaction = ReactionLogic(isCritical, customReaction);

            ElementMultiplier statElement = new(element, 1);

            //Apply the Element Multiplier. Pure damage bypasses every multiplier, the Elements included
            if (element != null && !pureDamage && CurrentProfile.elements != null)
            {
                var profileElements = CurrentProfile.elements;
                var found = false;

                for (int i = 0; i < profileElements.Count; i++) //Plain loop: List.Find allocated a delegate on every hit
                {
                    if (profileElements[i].element != null && profileElements[i].element.ID == element.ID)
                    {
                        statElement = profileElements[i];
                        found = true;
                        break;
                    }
                }

                if (found && statElement.multiplier != null)
                {
                    damage.Value *= statElement.multiplier;
                    events.OnElementDamage.Invoke(statElement.element.ID);
                    if (Root) Root.events.OnElementDamage.Invoke(statElement.element.ID);
                }
            }

            //Store the last damage applied to the Damageable
            LastDamage = new DamageData(Direction, HitPosition, Damager, gameObject, damage, isCritical, statElement, DamageReaction, missed);

            if (Root) Root.LastDamage = LastDamage;


            if (isCritical)
            {
                events.OnCriticalDamage.Invoke();
                if (Root) Root.events.OnCriticalDamage.Invoke();
            }

            //MWC — apply the child/profile multiplier (e.g. headshot) BEFORE the events so UI damage indicators use the final value
            if (damage.ID && !pureDamage)
                damage.Value *= CurrentProfile.Multiplier;               //Apply to the Stat modifier a new Modification

            events.OnReceivingDamage.Invoke(damage.Value);
            events.OnDamager.Invoke(Damager);

            //Send the Events on the Root
            if (Root)
            {
                Root.events.OnReceivingDamage.Invoke(damage.Value);
                Root.events.OnDamager.Invoke(Damager);
            }


            if (damage.ID)
            {
                if (stats == null)
                {
                    WarnMissingStat(damage.ID, $"[{name}] received damage on the Stat [{damage.ID.name}] but it has no Stats component to apply it. Assign one on the Damageable.");
                }
                else
                {
                    damage.ModifyStat(stats); //Apply the Stat Modification
                    var stat = stats.Stat_Get(damage.ID);

                    //The Damager targets a Stat this character does not have (E.g. a Mana damage on a character with Health only)
                    if (stat == null)
                    {
                        WarnMissingStat(damage.ID, $"[{name}] received damage on the Stat [{damage.ID.name}], but [{stats.name}] has no Stat with that ID. The Stat was not modified.");
                    }
                    //Invoke if the Stat is empty.. (forwarded to the Root like the other damage events)
                    else if (stat.IsEmpty)
                    {
                        events.OnStatEmpty.Invoke(stat.ID);
                        if (Root) Root.events.OnStatEmpty.Invoke(stat.ID);
                    }
                }
            }

            AlignmentLogic(Damager);
        }

        /// <summary> Stat IDs already reported as missing, so a combo does not flood the Console </summary>
        private HashSet<int> warnedMissingStats;

        /// <summary> Logs a warning once per Stat ID when a hit targets a Stat this Damageable cannot modify </summary>
        protected void WarnMissingStat(StatID id, string message)
        {
            warnedMissingStats ??= new();
            if (warnedMissingStats.Add(id.ID)) Debug.LogWarning(message, this);
        }

        /// <summary> Restart the Damageable after a Respawn: enabled again (E.g. the Death wiring disabled it), Default Profile back and no buffered damage </summary>
        public virtual void Restart()
        {
            if (!startEnabled) return; //Disabled on purpose in the Inspector (or Awake has not run yet, so nothing changed): keep it that way

            if (IApplyDamageNextFrame != null) { StopCoroutine(IApplyDamageNextFrame); IApplyDamageNextFrame = null; }
            if (IAlignCoroutine != null) { StopCoroutine(IAlignCoroutine); IAlignCoroutine = null; }

            PreDamage.ClearData();
            hasHitProfile = false;

            if (!string.IsNullOrEmpty(Default.name) && !IsOnDefaultProfile) Profile_Restore(); //Start already ran: go back to the Default Profile

            enabled = true;
        }

        /// <summary> True when the Damageable was enabled on Awake. Restart() never enables a Damageable that was disabled in the Inspector </summary>
        [System.NonSerialized] private bool startEnabled;

        /// <summary> Caches the authored enabled state before any Death wiring can change it </summary>
        protected virtual void Awake() => startEnabled = enabled;

        /// <summary> True when the Receive Damage conditions of the active Profile (and of the Root) allow the damage. The conditions target is the Character </summary>
        public virtual bool CanReceiveDamage()
        {
            //The active Profile owns the conditions (the Default profile wraps the top-level ones). Before Start the top-level field is used
            var conditions = string.IsNullOrEmpty(CurrentProfile.name) ? canReceiveDamage : CurrentProfile.canReceiveDamage;

            if (conditions.Valid)
            {
                UnityEngine.Object target = character != null ? character : this;
                if (!conditions.Evaluate(target)) return false;
            }

            if (Root != null && Root != this && !Root.CanReceiveDamage()) return false; //The Root conditions apply to every child Damageable (e.g. Hit Boxes)

            return true;
        }

        protected virtual void AlignmentLogic(GameObject Damager)
        {
            if (Damager == null) return;

            if (CurrentProfile.AlignToDamage.Value)
            {
                if (OnlyOnMovementZero.Value && characterMove != null && characterMove.MovementDetected) return; //Do not Align if the Animal is moving

                AlignToDamageDirection(Damager);
            }
        }

        protected virtual Reaction2 ReactionLogic(bool isCritical, Reaction2 customReaction)
        {
            if (Damager)
                CurrentProfile.damagerReaction.React(Damager); //React to the Damager if it has a Reaction

            if (isCritical)
            {
                CurrentProfile.criticalReaction.React(character);     //if the damage is Critical then react with the critical reaction instead
                return CurrentProfile.criticalReaction;
            }


            //Use the Damager's custom Reaction only when it is valid AND the current profile does not ignore it
            var ignoreCustom = CurrentProfile.ignoreDamagerReaction != null && CurrentProfile.ignoreDamagerReaction.Value;

            if (customReaction.IsValid && !ignoreCustom)
            {
                customReaction.React(character); //Custom Reaction from the Damager
                return customReaction;
            }
            else
            {
                CurrentProfile.damageReaction.React(character);    //React Default
                return CurrentProfile.damageReaction;
            }
        }

        protected virtual void AlignToDamageDirection(GameObject DirectionGameObj)
        {
            if (!isActiveAndEnabled || DirectionGameObj == null || character == null) return; //No Character to rotate

            //Stop only the previous alignment. StopAllCoroutines() also killed the buffered-damage coroutine and left its handle set, so no PrepareDamage could ever apply again
            if (IAlignCoroutine != null) StopCoroutine(IAlignCoroutine);

            IAlignCoroutine = MTools.AlignLookAtTransform(
                character.transform, DirectionGameObj.transform.position, AlignOffset, AlignTime.Value,
                (stats != null ? stats.transform : character.transform).localScale.y, AlignCurve);
            StartCoroutine(IAlignCoroutine);
        }

        /// <summary> Handle of the running Align To Damage coroutine </summary>
        private IEnumerator IAlignCoroutine;

        /// <summary>  Receive Damage from external sources simplified </summary>
        /// <param name="stat"> What stat will be modified</param>
        /// <param name="amount"> value to subtract to the stat</param>
        public virtual void ReceiveDamage(StatID stat, float amount)
        {
            var modifier = new StatModifier() { ID = stat, modify = StatOption.SubstractValue, Value = amount };
            ReceiveDamage(Vector3.forward, transform.position, null, modifier, false, null, false, null, false);
        }

        /// <summary>  Receive Damage from external sources simplified </summary>
        /// <param name="stat"> What stat will be modified</param>
        /// <param name="amount"> value to subtract to the stat</param>
        public virtual void ReceiveDamage(StatID stat, float amount, StatOption modifyStat = StatOption.SubstractValue)
        {
            var modifier = new StatModifier() { ID = stat, modify = modifyStat, Value = amount };
            ReceiveDamage(Vector3.forward, transform.position, null, modifier, false, null, false, null, false);
        }


        /// <summary>  Receive Damage from external sources simplified </summary>
        /// <param name="Direction">Where the Damage is coming from</param>
        /// <param name="Damager">Who is doing the Damage</param>
        /// <param name="modifier">What Stat will be modified</param>
        /// <param name="modifyStat">Type of modification applied to the stat</param>
        /// <param name="isCritical">is the Damage Critical?</param>
        /// <param name="react">Does Apply the Default Reaction?</param>
        /// <param name="pureDamage">if is pure Damage, do not apply the default multiplier</param>
        /// <param name="stat"> What stat will be modified</param>
        /// <param name="amount"> value to subtract to the stat</param>
        public virtual void ReceiveDamage(Vector3 Direction, GameObject Damager, StatID stat, float amount, StatOption modifyStat = StatOption.SubstractValue,
             bool isCritical = false, Reaction customReaction = null, bool pureDamage = false, StatElement element = null)
        {
            var modifier = new StatModifier() { ID = stat, modify = modifyStat, Value = amount };
            ReceiveDamage(Direction, transform.position, Damager, modifier, isCritical, customReaction, pureDamage, element, false);
        }


        /// <summary>  Receive Damage from external sources simplified </summary>
        /// <param name="Direction">Where the Damage is coming from</param>
        /// <param name="Damager">Who is doing the Damage</param> 
        /// <param name="isCritical">is the Damage Critical?</param> 
        /// <param name="pureDamage">if is pure Damage, do not apply the default multiplier</param>
        /// <param name="stat"> What stat will be modified</param>
        /// <param name="amount"> value to subtract to the stat</param>
        public virtual void ReceiveDamage(Vector3 Direction, GameObject Damager, StatID stat,
            float amount, bool isCritical = false, Reaction customReaction = null, bool pureDamage = false)
        {
            var modifier = new StatModifier() { ID = stat, modify = StatOption.SubstractValue, Value = amount };
            ReceiveDamage(Direction, transform.position, Damager, modifier, isCritical, customReaction, pureDamage, null, false);
        }


        /// <summary>  Receive Damage from external sources simplified </summary>
        /// <param name="Direction">Where the Damage is coming from</param>
        /// <param name="Damager">Who is doing the Damage</param>
        /// <param name="modifier">What Stat will be modified</param>
        /// <param name="isCritical">is the Damage Critical?</param>
        /// <param name="react">Does Apply the Default Reaction?</param>
        /// <param name="pureDamage">if is pure Damage, do not apply the default multiplier</param>
        /// <param name="stat"> What stat will be modified</param>
        /// <param name="amount"> value to subtract to the stat</param>
        public virtual void ReceiveDamage(Vector3 Direction, GameObject Damager, StatModifier damage,
        bool isCritical, Reaction customReaction, bool pureDamage) =>
         ReceiveDamage(Direction, transform.position, Damager, damage, isCritical, customReaction, pureDamage, null, false);

        /// <summary>  Fill the Local Values of the MDamageable  </summary>
        internal void SetDamageable(Vector3 Direction, GameObject Damager)
        {
            HitDirection = Direction;
            this.Damager = Damager;
        }

        [System.Serializable]
        //CustomPatch: TODO: architecture improvement: this can easily be a struct instead of class (leads to more uniform memory allocation instead of more random heap allocations)
        public class DamagerEvents
        {
            public FloatEvent OnReceivingDamage = new();
            public UnityEvent OnCriticalDamage = new();
            public GameObjectEvent OnDamager = new();
            public IntEvent OnElementDamage = new();
            public IntEvent OnStatEmpty = new();
            [Tooltip("Invoked when the Can Receive Damage conditions fail and the damage is skipped. Sends the Damager")]
            public GameObjectEvent OnDamageIgnored = new();
            [Tooltip("Invoked when the attack Missed (Damager Miss Chance). No reaction plays and the Stat is not modified. Sends the Damager")]
            public GameObjectEvent OnDamageMissed = new();
            public UnityEvent OnProfileDefaultEnter = new();
            public UnityEvent OnProfileDefaultExit = new();
        }




#if UNITY_EDITOR
        private void Reset()
        {
            // reaction = MTools.GetInstance<ModeReaction>("Damaged");

            damageReaction = new(new ModeReaction() { ID = MTools.GetInstance<ModeID>("Damage"), });
            criticalReaction = new(new ModeReaction() { ID = MTools.GetInstance<ModeID>("Damage"), });

            surface = MTools.GetInstance<SurfaceID>("Flesh");

            stats = this.FindComponent<Stats>();
            if (transform.parent != null) Root = transform.root.GetComponentInParent<MDamageable>();     //Check if there's a Damageable on the Root
            if (Root == this) Root = null;


            //Add Stats if it not exist
            if (stats == null) stats = gameObject.AddComponent<Stats>();

            profiles = new List<MDamageableProfile>();
        }

        [SerializeField, HideInInspector] private bool FirstTime = false; //Debug Mode to show the Damageable in the Inspector


        private void OnValidate()
        {
            if (FirstTime) return; //Only Validate once

            if (!damageReaction.IsValid)
            {
                damageReaction = new(new ModeReaction() { ID = MTools.GetInstance<ModeID>("Damage"), });
                criticalReaction = new(new ModeReaction() { ID = MTools.GetInstance<ModeID>("Damage"), });
                MTools.SetDirty(this);
            }
            FirstTime = true; //Set the First Time to True so it doesn't validate again
        }


        private void OnDrawGizmosSelected()
        {
            canReceiveDamage.Gizmos(this);

            if (AlignOffset != 0)
            {
                Vector3 Offset = transform.position + AlignOffset * transform.localScale.y * transform.forward; //Use Offset

                Gizmos.color = Color.white;
                Gizmos.DrawSphere(Offset, 0.075f);
            }
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(MDamageable))]
    public class MDamageableEditor : Editor
    {
        SerializedProperty reaction, damagerReaction, criticalReaction, surface,
            stats,
            multiplier, ignoreDamagerReaction, events, Root, OnlyOnMovementZero,
            // OnProfileDefaultEnter, OnProfileDefaultExit,
            AlignTime, AlignCurve, AlignToDamage, AlignOffset,
            Editor_Tabs1, elements, profiles, canReceiveDamage;
        MDamageable M;

        protected string[] Tabs1 = new string[] { "General", "Profiles", "Events" };


        GUIContent plus;

        protected virtual void OnEnable()
        {
            M = (MDamageable)target;

            reaction = serializedObject.FindProperty("damageReaction");
            criticalReaction = serializedObject.FindProperty("criticalReaction");
            damagerReaction = serializedObject.FindProperty("damagerReaction");
            stats = serializedObject.FindProperty("stats");
            multiplier = serializedObject.FindProperty("multiplier");

            events = serializedObject.FindProperty("events");
            //OnProfileDefaultEnter = serializedObject.FindProperty("OnProfileDefaultEnter");
            //OnProfileDefaultExit = serializedObject.FindProperty("OnProfileDefaultExit");


            Root = serializedObject.FindProperty("Root");
            OnlyOnMovementZero = serializedObject.FindProperty("OnlyOnMovementZero");

            AlignToDamage = serializedObject.FindProperty("AlignToDamage");
            AlignCurve = serializedObject.FindProperty("AlignCurve");
            AlignTime = serializedObject.FindProperty("AlignTime");
            AlignOffset = serializedObject.FindProperty("AlignOffset");


            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            elements = serializedObject.FindProperty("elements");
            profiles = serializedObject.FindProperty("profiles");
            surface = serializedObject.FindProperty("surface");
            ignoreDamagerReaction = serializedObject.FindProperty("ignoreDamagerReaction");
            canReceiveDamage = serializedObject.FindProperty("canReceiveDamage");

            if (plus == null) plus = UnityEditor.EditorGUIUtility.IconContent("d_Toolbar Plus");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription("Connects the Damage received to the Stat and Animal components");




            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs1);


            if (Application.isPlaying)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.TextField("Current Profile", M.CurrentProfile.name);
                    }
                }
            }


            switch (Editor_Tabs1.intValue)
            {
                case 0: DrawGeneral(); break;
                case 1: DrawProfiles(); break;
                case 2: DrawEvents(); break;
                default: break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawProfiles()
        {
            var before = profiles.arraySize;
            EditorGUILayout.PropertyField(profiles, true);

            //A brand-new element is zero-initialized: every ReferenceVar shows an empty Var slot. Seed it as a real Profile
            if (profiles.arraySize > before) SeedNewProfiles(before);
        }

        /// <summary> Turns every zero-initialized element from 'from' on into a ready-to-use Profile (a duplicated element is left as it is) </summary>
        private void SeedNewProfiles(int from)
        {
            serializedObject.ApplyModifiedProperties();
            var seeded = false;

            for (int i = from; i < M.profiles.Count; i++)
            {
                var p = M.profiles[i];
                var zeroInit = string.IsNullOrEmpty(p.name) && p.multiplier != null && !p.multiplier.UseConstant && p.multiplier.Variable == null;
                if (!zeroInit) continue;

                if (!seeded) Undo.RecordObject(M, "New Damageable Profile");
                M.profiles[i] = MDamageableProfile.New("New Damage Profile", M.surface);
                seeded = true;
            }

            if (seeded) EditorUtility.SetDirty(M);
            serializedObject.Update();
        }

        private void DrawGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(stats);

                if (M.transform.parent != null)
                    EditorGUILayout.PropertyField(Root);

                EditorGUILayout.PropertyField(surface);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(canReceiveDamage, new GUIContent("Can Receive Damage", canReceiveDamage.tooltip));
                EditorGUI.indentLevel--;
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                stats.isExpanded = MalbersEditor.Foldout(stats.isExpanded, "Reactions");

                if (stats.isExpanded)
                {
                    EditorGUI.indentLevel++;

                    EditorGUILayout.PropertyField(reaction);
                    EditorGUILayout.PropertyField(criticalReaction);
                    EditorGUILayout.PropertyField(damagerReaction);
                    EditorGUILayout.PropertyField(ignoreDamagerReaction);
                    EditorGUI.indentLevel--;

                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                multiplier.isExpanded = MalbersEditor.Foldout(multiplier.isExpanded, "Multipliers");

                if (multiplier.isExpanded)
                {
                    EditorGUILayout.PropertyField(multiplier);
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(elements);
                    EditorGUI.indentLevel--;
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                AlignToDamage.isExpanded = MalbersEditor.Foldout(AlignToDamage.isExpanded, "Alignment");

                if (AlignToDamage.isExpanded)

                {
                    EditorGUILayout.PropertyField(AlignToDamage);

                    if (M.AlignToDamage.Value)
                    {
                        EditorGUILayout.PropertyField(OnlyOnMovementZero);

                        using (new GUILayout.HorizontalScope())
                        {
                            EditorGUILayout.PropertyField(AlignTime);
                            EditorGUILayout.PropertyField(AlignCurve, GUIContent.none, GUILayout.MaxWidth(75));
                        }
                        EditorGUILayout.PropertyField(AlignOffset);
                    }
                }
            }
        }

        private void DrawEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(events, true);
                EditorGUI.indentLevel--;
            }
        }
    }
#endif
}