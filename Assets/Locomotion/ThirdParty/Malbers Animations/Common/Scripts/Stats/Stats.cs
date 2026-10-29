// Last Change: 10/14/2026 2:45pm
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace MalbersAnimations
{
    /// <summary> Component managing Stat Logic</summary>
    /// <remarks>Extension seam: subclass Stats and override WrapStat(Stat) to wrap every authored Stat in your own IMStat
    /// (E.g. a network-synced Stat). The authored list, the inspector and the prefab data stay untouched.</remarks>
    [AddComponentMenu("Malbers/Stats/Stats Manager")]
    public class Stats : MonoBehaviour, IAnimatorListener, IRestart
    {
#pragma warning disable CS0414 // Add readonly modifier
        [SerializeField] private int Selected_StatIndex = 0; //Editor Only
#pragma warning restore CS0414 // Add readonly modifier

        /// <summary>Authored list of Stats (serialized). Runtime access goes through the dictionary, which may hold wrappers (see WrapStat)</summary>
        public List<Stat> stats = new();
        /// <summary>Runtime Stats by ID. Built in Initialize() from 'stats' through WrapStat()</summary>
        protected Dictionary<int, IMStat> stats_D;

        /// <summary>Runtime Stats by ID. Null until Initialize() runs</summary>
        public Dictionary<int, IMStat> Stats_Dictionary() => stats_D;

        /// <summary>Stored Stat to use the 'Pin' Methods</summary>
        // MWC — must NOT be serialized: this is a runtime cache overwritten by every Stat_Get() call.
        // Serializing it wrote a duplicate copy of a stat to disk (e.g. the pinned stat), and the aliased/duplicated
        // data made inspector edits on that stat revert back. [NonSerialized] keeps it a pure runtime field.
        [System.NonSerialized] public IMStat PinnedStat;

        public virtual bool OnAnimatorBehaviourMessage(string message, object value) => this.InvokeWithParams(message, value);

        /// <summary>Build the runtime dictionary from the authored list, wrapping each Stat through WrapStat()</summary>
        public virtual void Initialize()
        {
            StopAllCoroutines();

            stats_D = new();

            foreach (var stat in stats)
            {
                if (stat.ID == null)
                {
                    Debug.LogError("One of the Stats has an Empty ID", gameObject);
                    break;
                }
                stats_D[stat.ID] = WrapStat(stat); //Replace it
            }
        }

        /// <summary>Runtime object stored for an authored Stat. Override to wrap it (E.g. a network-synced Stat); default returns the Stat itself</summary>
        protected virtual IMStat WrapStat(Stat stat) => stat;

        protected virtual void Awake()
        {
            Initialize();
        }

        //MWC — Single pass. 'stats_D' is built from 'stats' in Initialize(), so the two loops were initializing the
        //SAME Stat objects: InitializeStat ran twice per Stat, firing Reset On Enable, OnMaxValueChange and the
        //delayed ValueEvents() twice on every enable. Iterating 'stats' also covers entries that share an ID, which
        //the dictionary collapses. A Stat with no ID is skipped instead of aborting the loop — one mis-authored
        //entry used to leave every Stat after it uninitialized.
        protected virtual void OnEnable()
        {
            foreach (var (statID, stat) in stats_D)
            {
                if (stat.ID == null)
                {
                    Debug.LogError("One of the Stats has an Empty ID", gameObject);
                    continue;
                }

                stat.InitializeStat(this);
            }

            RefreshParentLink(); // MWC — link to the Stats above (E.g. a weapon already equipped when it is enabled)
        }


        /// <summary>Restart all Stat parameters IRestart interface</summary>
        public virtual void Restart()
        {
            if (!gameObject.activeInHierarchy) return;

            StopAllCoroutines();

            SetParentStats(null); // MWC — reset the OWN values, then link again so the parent contribution is re-applied

            foreach (var s in stats_D)
            {
                s.Value.Active = true;
                s.Value.ResetValue();
            }

            RefreshParentLink();
        }

        /// <summary>Unlink from the parent Stats and stop every Stat coroutine</summary>
        protected virtual void OnDisable()
        {
            SetParentStats(null); // MWC — remove the parent contribution and unsubscribe, so re-enabling never applies it twice
            StopAllCoroutines();
        }

        #region Parent Link
        //MWC — Parent Link: a Stat can follow the same Stat on the closest [Stats] component ABOVE it in the hierarchy.
        //E.g. a sword with a local [Attack 20] equipped by a character with [Attack 30] reads 50 while it is in the
        //character's hands or holster, and goes back to 20 when it is dropped. Driven by OnTransformParentChanged
        //(same pattern as EffectManager / MWeaponTrail) plus OnEnable, since Instantiate(prefab, parent) does not
        //raise the message. The link is LIVE: the child follows the parent Stat while it is linked.

        /// <summary>Closest Stats component above this one in the hierarchy this component is linked to. Null when unlinked</summary>
        public Stats ParentStats { get; private set; }

        /// <summary>True when at least one Stat opts into the Parent Link</summary>
        public bool HasParentLinks => stats.Exists(s => s != null && s.UsesParentLink);

        /// <summary>Unity message: the object was re-parented (equipped, holstered, dropped…)</summary>
        private void OnTransformParentChanged()
        {
            if (!isActiveAndEnabled) return; // OnEnable will take care of the link when it comes back
            RefreshParentLink();
        }

        /// <summary>Find the closest Stats ABOVE this component (never itself) and link the Stats that opt in</summary>
        public virtual void RefreshParentLink()
        {
            if (!HasParentLinks) { SetParentStats(null); return; } // nothing opted in: make sure nothing stays linked

            //Search from the PARENT, GetComponentInParent on this transform would return this very component
            var parent = transform.parent != null ? transform.parent.GetComponentInParent<Stats>(true) : null;
            SetParentStats(parent);
        }

        /// <summary>Link every opted-in Stat to the same Stat on <paramref name="parent"/>. Null unlinks and restores the own values</summary>
        public virtual void SetParentStats(Stats parent)
        {
            if (parent == this) parent = null;

            ParentStats = parent;

            foreach (var (statID, stat) in stats_D)
            {
                if (stat == null) continue;

                IMStat parentStat = null;

                if (parent != null && stat.UsesParentLink && stat.ID != null)
                    parentStat = parent.Stat_Get(stat.ID);

                stat.LinkParent(parentStat);
            }
        }
        #endregion


        /// <summary>Updates all Stats</summary>
        public virtual void Stats_Update()
        {
            foreach (var (statID, stat) in stats_D) stat.UpdateStat();
        }

        /// <summary>Updates a stat logic by its Stat ID</summary>
        public virtual void Stats_Update(StatID ID) => Stats_Update(ID.ID);

        public virtual void Stats_Update(int ID) => Stat_Get(ID)?.UpdateStat();

        /// <summary> Reset a Stat to the Default Max Value</summary>
        public virtual void Stat_Reset_to_Max(StatID ID) => Stat_Get(ID)?.Reset_to_Max();

        /// <summary> Reset a Stat to the Default Min Value</summary>
        public virtual void Stat_Reset_to_Min(StatID ID) => Stat_Get(ID)?.Reset_to_Min();

        /// <summary>Disable a stat</summary>
        public virtual void Stat_Disable(StatID ID) => Stat_Get(ID)?.SetActive(false);

        /// <summary>Disable a stat Degeneration logic</summary>
        public virtual void Stat_Degenerate_Off(StatID ID) => Stat_Get(ID)?.SetDegeneration(false);
        public virtual void Stat_Degenerate_Off(int ID) => Stat_Get(ID)?.SetDegeneration(false);

        /// <summary>Enable a stat Degeneration logic</summary>
        public virtual void Stat_Degenerate_On(StatID ID) => Stat_Get(ID)?.SetDegeneration(true);
        public virtual void Stat_Degenerate_On(int ID) => Stat_Get(ID)?.SetDegeneration(true);

        /// <summary>Disable a stat Regeneration logic</summary>
        public virtual void Stat_Regenerate_Off(StatID ID) => Stat_Get(ID)?.SetRegeneration(false);
        public virtual void Stat_Regenerate_Off(int ID) => Stat_Get(ID)?.SetRegeneration(false);

        /// <summary>Enable a stat Regeneration logic</summary>
        public virtual void Stat_Regenerate_On(StatID ID) => Stat_Get(ID)?.SetRegeneration(true);
        public virtual void Stat_Regenerate_On(int ID) => Stat_Get(ID)?.SetRegeneration(true);


        /// <summary>Disable a stat Degeneration logic</summary>
        public virtual void Stat_Can_Degenerate_Off(StatID ID) => Stat_Get(ID)?.SetCanDegenerate(false);

        /// <summary>Enable a stat Degeneration logic</summary>
        public virtual void Stat_Can_Degenerate_On(StatID ID) => Stat_Get(ID)?.SetCanDegenerate(true);

        /// <summary>Disable a stat Regeneration logic</summary>
        public virtual void Stat_Can_Regenerate_Off(StatID ID) => Stat_Get(ID)?.SetCanRegenerate(false);

        /// <summary>Enable a stat Regeneration logic</summary>
        public virtual void Stat_Can_Regenerate_On(StatID ID) => Stat_Get(ID)?.SetCanRegenerate(true);



        #region Callbacks with StatID parameters
        /// <summary>Enable a stat</summary>
        public virtual void Stat_Enable(StatID iD) => Stat_Get(iD)?.SetActive(true);

        /// <summary>Set PinnedStat searching for a StatID</summary>
        public virtual void Stat_Pin(StatID ID) => Stat_Get(ID.ID);

        /// <summary>Find a Stat Using a StatID and Return if the Stat is on the List. Also Saves it to the PinnedStat</summary>
        public virtual IMStat Stat_Get(StatID ID) => Stat_Get(ID.ID);


        // <summary>Set the Inmune Value of a Stat to true</summary>
        public virtual void Stat_Immune_Activate(StatID ID) => Stat_Get(ID)?.SetImmune(true);

        /// <summary>Set the Inmune Value of a Stat to false</summary>
        public virtual void Stat_Immune_Deactivate(StatID ID) => Stat_Get(ID)?.SetImmune(false);
        #endregion


        /// <summary>Set PinnedStat searching for a Stat Name</summary>
        public virtual void Stat_Pin(string name) => Stat_Get(name);

        /// <summary>Set PinnedStat searching for a int ID value</summary>
        public virtual void Stat_Pin(int ID) => Stat_Get(ID);


        /// <summary>Find a Stat Using its name for the ID and Return if the Stat is on the List. Also Saves it to the PinnedStat</summary>
        public virtual IMStat Stat_Get(string Name)
        {
            if (stats_D == null) Initialize(); //Initialize the Dictionary if is not initialized

            PinnedStat = null;

            foreach (var stat in stats_D.Values)
            {
                if (stat.Name == Name) { PinnedStat = stat; break; }
            }

            return PinnedStat;
        }

        /// <summary>Find a Stat Using a int Value for the ID and Return if the Stat is on the List. Also Saves it to the PinnedStat</summary>
        public virtual IMStat Stat_Get(int ID)
        {
            if (stats_D == null) Initialize(); //Initialize the Dictionary if is not initialized

            if (stats_D.TryGetValue(ID, out PinnedStat))
                return PinnedStat;
            return null;
        }

        /// <summary>Find a Stat by its int ID without touching the Pinned Stat</summary>
        public virtual bool TryGetValue(int ID, out IMStat result)
        {
            if (stats_D == null) Initialize(); //Initialize the Dictionary if is not initialized

            return stats_D.TryGetValue(ID, out result);
        }


        #region Member Get (Used for Member Get Reactions/Conditions)
        public virtual IMStat Stat_Get(IntVar ID) => Stat_Get(ID.Value);
        public virtual float Stat_GetValue(StatID ID) => Stat_Get(ID)?.Value ?? 0;
        public virtual float Stat_GetValue(string name) => Stat_Get(name)?.Value ?? 0;
        public virtual float Stat_GetMax(StatID ID) => Stat_Get(ID)?.MaxValue ?? 0;
        public virtual float Stat_GetMax(string name) => Stat_Get(name)?.MaxValue ?? 0;
        public virtual float Stat_GetMin(StatID ID) => Stat_Get(ID)?.MinValue ?? 0;
        public virtual float Stat_GetMin(string name) => Stat_Get(name)?.MinValue ?? 0;
        public virtual float Stat_GetNormalized(StatID ID) => Stat_Get(ID)?.NormalizedValue ?? 0;
        public virtual float Stat_GetMultiplier(StatID ID) => Stat_Get(ID)?.Multiplier ?? 0;
        public virtual bool Stat_Empty(StatID ID) => Stat_Get(ID) != null && Stat_Get(ID).IsEmpty;
        public virtual bool Stat_Full(StatID ID) => Stat_Get(ID) != null && Stat_Get(ID).IsFull;
        public virtual bool Stat_IsBelow(StatID ID, float value) => Stat_Get(ID) != null && Stat_Get(ID).Value < value;
        public virtual bool Stat_IsAbove(StatID ID, float value) => Stat_Get(ID) != null && Stat_Get(ID).Value > value;
        public virtual bool Stat_IsRegenerating(StatID ID) => Stat_Get(ID) != null && Stat_Get(ID).IsRegenerating;
        public virtual bool Stat_IsDegenerating(StatID ID) => Stat_Get(ID) != null && Stat_Get(ID).IsDegenerating;
        #endregion



        public virtual void Stat_SetValue(StatID ID, float Value) => Stat_Get(ID)?.SetValue(Value);
        public virtual void Stat_SetValue(int ID, float Value) => Stat_Get(ID)?.SetValue(Value);
        public virtual void Stat_SetValue(string Name, float Value) => Stat_Get(Name)?.SetValue(Value);

        public virtual void Stat_ModifyValue(StatID ID, float Value) => Stat_Get(ID)?.Modify(Value);
        public virtual void Stat_ModifyValue(int ID, float Value) => Stat_Get(ID)?.Modify(Value);
        public virtual void Stat_ModifyValue(string Name, float Value) => Stat_Get(Name)?.Modify(Value);
        public virtual void Stat_ModifyValue(StatID ID, float Value, StatOption Type) => Stat_Get(ID)?.Modify(Value, Type);
        public virtual void Stat_ModifyValue(string Name, float Value, StatOption Type) => Stat_Get(Name)?.Modify(Value, Type);

        /// <summary>Modify Stat Value instantly (Add/Remove to the Value)</summary>
        public virtual void Stat_Pin_ModifyValue(float Value) => PinnedStat?.Modify(Value);

        /// <summary>Modify Stat Value instantly (Add/Remove to the Value)</summary>
        public virtual void Stat_Pin_ModifyValue(FloatVar Value) => PinnedStat?.Modify(Value.Value);

        /// <summary>Modify Stat Value instantly (Add/Remove to the Value)</summary>
        public virtual void Stat_Pin_SetMult(float value) => PinnedStat?.SetMultiplier(value);

        /// <summary>Modify Stat Value instantly (Add/Remove to the Value)</summary>
        public virtual void Stat_Pin_SetMult(FloatVar value) => PinnedStat?.SetMultiplier(value.Value);

        /// <summary>Modify Stat Value in a X time period(Add/Remove to the Value)</summary>
        public virtual void Stat_Pin_ModifyValue(float value, float time) => PinnedStat?.Modify(value, time);

        /// <summary>Modify Stat Value in 1 second period(Add/Remove to the Value)</summary>
        public virtual void Stat_Pin_ModifyValue_1Sec(float value) => PinnedStat?.Modify(value, 1);

        /// <summary>Set  Stat Value to a fixed Value</summary>
        public virtual void Stat_Pin_SetValue(float value) => PinnedStat.SetValue(value);

        /// <summary>Modify the Pinned Stat MAX Value (Add or remove to the Max Value) </summary>
        public virtual void Stat_Pin_ModifyMaxValue(float value) => PinnedStat?.ModifyMAX(value);

        /// <summary>Set the Pinned Stat MAX Value </summary>
        public virtual void Stat_Pin_SetMaxValue(float value) => PinnedStat?.SetMAX(value);

        /// <summary> Enable/Disable the Pinned Stat Regeneration Rate </summary>
        public virtual void Stat_Pin_Modify_RegenRate(float value) => PinnedStat?.ModifyRegenRate(value);

        /// <summary> Enable/Disable the Pinned Stat Degeneration </summary>
        public virtual void Stat_Pin_Degenerate(bool value) => PinnedStat?.SetDegeneration(value);

        /// <summary>Set the Pinned Stat Degeneration Rate and start degenerating</summary>
        public virtual void Stat_Pin_DegenerateOn(float value)
        {
            if (PinnedStat != null)
            {
                PinnedStat.SetDegenRate(value);
                PinnedStat.SetDegeneration(true);
            }
        }

        /// <summary>Set the Pinned Stat Regeneration Rate and start regenerating</summary>
        public virtual void Stat_Pin_RegenerateOn(float value)
        {
            if (PinnedStat != null)
            {
                PinnedStat.SetRegenRate(value);
                PinnedStat.SetRegeneration(true);
            }
        }

        /// <summary> Enable/Disable the Pinned Stat Degeneration </summary>
        public virtual void Stat_Pin_SetInmune(bool value) => PinnedStat?.SetImmune(value);

        /// <summary>Enable/Disable the Pinned Stat Regeneration </summary>
        public virtual void Stat_Pin_Regenerate(bool value) => PinnedStat?.SetRegeneration(value);
        //  public virtual void _PinStatRegenerate(bool value) { Stat_Pin_Regenerate(value); }

        /// <summary> Enable/Disable the Pinned Stat</summary>
        public virtual void Stat_Pin_Enable(bool value) => PinnedStat?.SetActive(value);

        /// <summary>Modify the Pinned Stat value with a 'new Value',  'ticks' times , every 'timeBetweenTicks' seconds</summary>
        public virtual void Stat_Pin_ModifyValue(float newValue, int ticks, float timeBetweenTicks) => PinnedStat?.Modify(newValue, ticks, timeBetweenTicks);

        /// <summary> Clean the Pinned Stat from All Regeneration/Degeneration and Modify Tick Values </summary>
        public virtual void Stat_Pin_CleanCoroutines() => PinnedStat?.CleanRoutines();


#if UNITY_EDITOR

        [ContextMenu("Create/Stamina")]
        private void ConnectStamina()
        {
            var staminaID = MTools.GetInstance<StatID>("Stamina");

            if (staminaID != null)
            {
                if (stats.Find(x => x.ID == staminaID) == null)
                {
                    stats.Add(new Stat()
                    {
                        ID = staminaID,
                        value = new FloatReference(100),
                        ImmuneTime = new FloatReference(0.5f),
                        regenerate = new BoolReference(true),
                        RegenRate = new FloatReference(40),
                        DegenRate = new FloatReference(20),
                        RegenWaitTime = new FloatReference(2),
                        Above = 15f,
                        Below = 10f,
                    });
                }
                MTools.SetDirty(this);
            }
        }

        [ContextMenu("Connect/Stamina to Animal Sprint")]
        void ConnectStaminaToAnimal()
        {

            var staminaID = MTools.GetInstance<StatID>("Stamina");
            var staminaStat = stats.Find(x => x.ID == staminaID);

            if (staminaStat != null)
            {
                //Connect to the Animal Controller in case it exist
                var method = this.GetUnityAction<bool>("MAnimal", "UseSprint");

                if (method != null)
                {
                    UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(staminaStat.OnStatBelow, method, false);
                    UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(staminaStat.OnStatAbove, method, true);
                }

                var onSprintEnable = this.GetFieldClass<BoolEvent>("MAnimal", "OnSprintEnabled");

                if (onSprintEnable != null)
                {
                    UnityEditor.Events.UnityEventTools.AddObjectPersistentListener<StatID>(onSprintEnable, Stat_Pin, staminaID);
                    UnityEditor.Events.UnityEventTools.AddPersistentListener(onSprintEnable, Stat_Pin_Degenerate);
                }
            }
        }


        [ContextMenu("Connect/Stamina to UI Events")]
        void ConnectStaminatoUI()
        {
            var staminaID = MTools.GetInstance<StatID>("Stamina");
            var staminaStat = stats.Find(x => x.ID == staminaID);

            if (staminaStat != null)
            {
                MEvent UIStamina = MTools.GetInstance<MEvent>("UI Stamina Stat");

                if (UIStamina)
                {
                    UnityEditor.Events.UnityEventTools.AddPersistentListener(staminaStat.OnValueChangeNormalized, UIStamina.Invoke);
                    UnityEditor.Events.UnityEventTools.AddPersistentListener(staminaStat.OnStatFull, UIStamina.Invoke);
                }

                MTools.SetDirty(this);
            }
        }


        [ContextMenu("Create/Health")]
        void CreateHealth()
        {
            var health = MTools.GetInstance<StatID>("Health");

            if (health != null)
            {
                var HealthStat = new Stat()
                {
                    ID = health,
                    value = new FloatReference(100),
                    DisableOnEmpty = new BoolReference(true),
                    ImmuneTime = new FloatReference(0.1f)
                };
                stats.Add(HealthStat);
                MTools.SetDirty(this);
            }
        }

        [ContextMenu("Connect/Health to UI Events")]

        void ConnectHealthUI()
        {
            var health = MTools.GetInstance<StatID>("Health");
            var HealthStat = stats.Find(x => x.ID == health);

            if (HealthStat != null)
            {
                MEvent UIHealth = MTools.GetInstance<MEvent>("UI Health Stat");

                if (UIHealth)
                {
                    UnityEditor.Events.UnityEventTools.AddPersistentListener(HealthStat.OnValueChangeNormalized, UIHealth.Invoke);
                    UnityEditor.Events.UnityEventTools.AddPersistentListener(HealthStat.OnStatFull, UIHealth.Invoke);
                }

                MTools.SetDirty(this);
            }
        }

        [ContextMenu("Connect/Health Empty to Death State")]
        void ConnectHealthDeath()
        {
            var health = MTools.GetInstance<StatID>("Health");
            var deathID = MTools.GetInstance<StateID>("Death");
            var HealthStat = stats.Find(x => x.ID == health);

            if (HealthStat != null)
            {
                var method = this.GetUnityAction<StateID>("MAnimal", "State_Activate");
                if (method != null)
                    UnityEditor.Events.UnityEventTools.AddObjectPersistentListener(HealthStat.OnStatEmpty, method, deathID);
            }
        }


        [ContextMenu("Create/Mana")]
        void CreateMana()
        {
            var Mana = MTools.GetInstance<StatID>("Mana");

            if (Mana != null)
            {

                var HealthStat = new Stat()
                {
                    ID = Mana,
                    value = new FloatReference(100),
                    DisableOnEmpty = new BoolReference(true),
                    ImmuneTime = new FloatReference(0),
                    regenerate = new BoolReference(true),
                    RegenWaitTime = new FloatReference(2),
                    RegenRate = new FloatReference(10),
                    DegenRate = new FloatReference(10)
                };
                stats.Add(HealthStat);
                MTools.SetDirty(this);
            }
        }

        [ContextMenu("Connect/Mana to UI Events")]
        void ConnectManaUI()
        {
            var Mana = MTools.GetInstance<StatID>("Mana");
            var ManaStat = stats.Find(x => x.ID == Mana);

            if (ManaStat != null)
            {

                MEvent ManaUIStat = MTools.GetInstance<MEvent>("UI Mana Stat");

                if (ManaUIStat)
                {
                    UnityEditor.Events.UnityEventTools.AddPersistentListener(ManaStat.OnValueChangeNormalized, ManaUIStat.Invoke);
                    UnityEditor.Events.UnityEventTools.AddPersistentListener(ManaStat.OnStatFull, ManaUIStat.Invoke);
                }

                MTools.SetDirty(this);
            }
        }

        void Reset()
        {
            stats = new List<Stat>();
        }

#endif
    }
    public enum StatCondition { HasStat, Enabled, Full, Empty, Regenerating, Degenerating, Inmune, Value, ValueNormalized, MaxValue, MinValue }

    ///──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
    ///──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
    ///STAT CLASS
    ///──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
    ///──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Serializable]
    public class Stat : IMStat
    {
        #region Variables 

        [Tooltip("Enable/Disable the Stat. Disable Stats cannot be modified")]
        public bool active = true;
        [Tooltip("Key Identifier for the Stat")]
        public StatID ID;
        [Tooltip("Current Value of the Stat")]
        public FloatReference value = new(0);
        [Tooltip("Maximum Value of the Stat")]
        public FloatReference maxValue = new(100);
        [Tooltip("Minimum Value of the Stat")]
        public FloatReference minValue = new();
        [Tooltip("If the Stat is Empty it will be disabled to avoid future changes")]
        public BoolReference DisableOnEmpty = new();

        [Tooltip("Round the Stat value to decimal values.\n0 will be set to integer\n-1 will ignore the round Logic")]
        public IntReference Round = new(-1);

        /// <summary>Multiplier to modify the Stat value</summary>
        [SerializeField] internal FloatReference multiplier = new(1);

        /// <summary>Can the Stat regenerate overtime</summary>
        [SerializeField] internal BoolReference regenerate = new(false);
        /// <summary>Can the Stat degenerate overtime</summary>
        [SerializeField] internal BoolReference degenerate = new(false);

        /// <summary>Can the Stat regenerate overtime</summary>
        [SerializeField] internal BoolReference can_Regenerate = new(true);
        /// <summary>Can the Stat degenerate overtime</summary>
        [SerializeField] internal BoolReference can_Degenerate = new(true);

        [Tooltip("Invoke the Stat Events on Enable. This will invoke the OnStatFull, OnStatEmpty, OnStatBelow, OnStatAbove, OnValueChangeNormalized and OnValueChange events")]
        [SerializeField] internal BoolReference InvokeEventsOnEnable = new(true);

        /// <summary>Regeneration Rate. Change the Speed of the Regeneration</summary>
        public FloatReference RegenRate = new();
        /// <summary>Regeneration Rate. When the value is modified this will increase or decrease it over time.</summary>
        public FloatReference RegenWaitTime = new(0);
        /// <summary>Regeneration Rate. When the value is modified this will increase or decrease it over time.</summary>
        public FloatReference DegenWaitTime = new(0);
        /// <summary>Degeneration Rate. Change the Speed of the Degeneration</summary>
        public FloatReference DegenRate = new();

        /// <summary>If greater than zero, the Stat cannot be modify until the inmune time have passed</summary>
        [FormerlySerializedAs("InmuneTime")]
        public FloatReference ImmuneTime = new();

        [Tooltip("Set the Stat to be immune. The stat values cannot be changed when this value is true")]
        public BoolReference immune = new();


        //MWC — replaces the old [resetTo] enum + [ResetOnEnable] bool pair. Deliberately a NEW field name: existing
        //prefabs have no data for it and fall back to None, so they keep behaving exactly as they did before
        //(the old ResetOnEnable bool never fired at startup, so nothing auto-reset). Opt in per Stat.
        [Tooltip("What the Stat does when it is enabled.\n" +
            "None: keep the serialized Value.\n" +
            "Reset To Min Value / Reset To Max Value: set the Value on enable.\n" +
            "This also picks the target used by Restart(), Restore All and the [Reset] Stat Option (None → Max Value)")]
        public ResetOnEnable resetOnEnable = ResetOnEnable.None;

        //MWC — Parent Link. Each number of the Stat picks how it follows the SAME Stat on the closest Stats above it
        //in the hierarchy (See Stats.RefreshParentLink). None keeps existing prefabs untouched.
        [Tooltip("How the Value follows the same Stat on the closest [Stats] component above this one in the hierarchy (E.g. the character holding this weapon).\n" +
            "None: the Value is not linked.\n" +
            "Additive: own Value + parent Value.\n" +
            "Override: the parent Value replaces the own Value.\n" +
            "Multiply: own Value x parent Value.\n" +
            "The own Value comes back when the object is unparented (E.g. dropped)")]
        public StatParentLink parentValue = StatParentLink.None;

        [Tooltip("How the Max Value follows the same Stat on the closest [Stats] component above this one in the hierarchy")]
        public StatParentLink parentMax = StatParentLink.None;

        [Tooltip("How the Multiplier follows the same Stat on the closest [Stats] component above this one in the hierarchy")]
        public StatParentLink parentMultiplier = StatParentLink.None;

        /// <summary>Raised whenever the Value, Max Value or Multiplier changes. Used by the Parent Link to keep child Stats in sync</summary>
        public event Action<IMStat> OnStatChanged;

        /// <summary>Stat on the parent Stats this Stat is currently linked to. Null when unlinked</summary>
        public IMStat ParentStat { get; private set; }

        /// <summary>True when at least one number of this Stat opts into the Parent Link</summary>
        public bool UsesParentLink => parentValue != StatParentLink.None || parentMax != StatParentLink.None || parentMultiplier != StatParentLink.None;

        /// <summary>True while a parent contribution is applied on this Stat</summary>
        public bool IsLinkedToParent => ParentStat != null;

        //MWC — what the parent contributed, so it can be removed again without touching modifications made meanwhile.
        //Additive stores the amount added, Multiply the factor, Override the own value it replaced.
        [NonSerialized] private float linkValue_Contribution, linkValue_Base;
        [NonSerialized] private float linkMax_Contribution, linkMax_Base;
        [NonSerialized] private float linkMult_Contribution, linkMult_Base;
        [NonSerialized] private bool linkApplied;

        /// <summary> Save the Last State of the Regeneration bool</summary>
        private bool regenerate_LastValue;
        /// <summary> Save the Last State of the Regeneration bool</summary>
        private bool degenerate_LastValue;
        /// <summary> Is the Stat Below the Below Value</summary>
        private bool isBelow = false;
        /// <summary> Is the Stat Above the Above Value</summary>
        private bool isAbove = false;


        #region Default Values
        /// <summary>Base Max Value (captured on the first Initialize only). RestoreMax returns to it plus the Max Modifiers</summary>
        public float DefaultMaxValue { get; private set; }

        /// <summary>True once the Base Max Value was captured, so re-enabling the Stats never folds bonuses into it</summary>
        [NonSerialized] private bool defaultMaxCaptured;

        /// <summary>Additive Max Value modifiers by source (See SetMaxModifier)</summary>
        [NonSerialized] private Dictionary<object, float> maxModifiers;

        /// <summary>Sum of every Max Modifier currently applied on the Max Value</summary>
        public float MaxModifierTotal { get; private set; }

        /// <summary>Default Value</summary>
        public float DefaultValue { get; private set; }

        /// <summary>Default value to for min value to add or remove buff conditions</summary>
        public float DefaultMinValue { get; private set; }

        /// <summary>Default value to for Multiplier to add or remove buff conditions</summary>
        public float DefaultMultiplier { get; private set; }

        /// <summary>Default value to for Regeneration Rate to add or remove buff conditions</summary>
        public float DefaultRegenRate { get; private set; }

        /// <summary>Default value to for Degeneration Rate to add or remove buff conditions</summary>
        public float DefaultDegenRate { get; private set; }
        #endregion


        public bool isPercent = true;
        public bool debug = false;
        #endregion
        #region Events
        public UnityEvent OnStatFull = new();
        public UnityEvent OnStatEmpty = new();
        public UnityEvent OnStat = new();
        public float Below;
        public float Above;
        public UnityEvent OnStatBelow = new();
        public UnityEvent OnStatAbove = new();
        public FloatEvent OnValueChangeNormalized = new();
        public FloatEvent OnValueChange = new();
        public FloatEvent OnMaxValueChange = new();
        public FloatEvent OnMinValueChange = new();
        public BoolEvent OnDegenerate = new();
        public BoolEvent OnRegenerate = new();
        public BoolEvent OnActive = new();
        #endregion

        #region IMStat explicit members
        //Explicit implementations: the public fields keep their serialized names, the interface reads them through these
        /// <summary>IMStat: Key Identifier of the Stat</summary>
        StatID IMStat.ID => ID;
        /// <summary>IMStat: Stat Full event</summary>
        UnityEvent IMStat.OnStatFull => OnStatFull;
        /// <summary>IMStat: Stat Empty event</summary>
        UnityEvent IMStat.OnStatEmpty => OnStatEmpty;
        /// <summary>IMStat: generic Stat event</summary>
        UnityEvent IMStat.OnStat => OnStat;
        /// <summary>IMStat: Stat Below event</summary>
        UnityEvent IMStat.OnStatBelow => OnStatBelow;
        /// <summary>IMStat: Stat Above event</summary>
        UnityEvent IMStat.OnStatAbove => OnStatAbove;
        /// <summary>IMStat: normalized Value change event</summary>
        FloatEvent IMStat.OnValueChangeNormalized => OnValueChangeNormalized;
        /// <summary>IMStat: Value change event</summary>
        FloatEvent IMStat.OnValueChange => OnValueChange;
        /// <summary>IMStat: Max Value change event</summary>
        FloatEvent IMStat.OnMaxValueChange => OnMaxValueChange;
        /// <summary>IMStat: Min Value change event</summary>
        FloatEvent IMStat.OnMinValueChange => OnMinValueChange;
        /// <summary>IMStat: Degenerate event</summary>
        BoolEvent IMStat.OnDegenerate => OnDegenerate;
        /// <summary>IMStat: Regenerate event</summary>
        BoolEvent IMStat.OnRegenerate => OnRegenerate;
        /// <summary>IMStat: Active event</summary>
        BoolEvent IMStat.OnActive => OnActive;
        /// <summary>IMStat: current Regeneration Rate</summary>
        float IMStat.RegenRate => RegenRate.Value;
        /// <summary>IMStat: current Degeneration Rate</summary>
        float IMStat.DegenRate => DegenRate.Value;
        /// <summary>IMStat: current Regeneration wait time</summary>
        float IMStat.RegenWaitTime => RegenWaitTime.Value;
        /// <summary>IMStat: current Degeneration wait time</summary>
        float IMStat.DegenWaitTime => DegenWaitTime.Value;
        /// <summary>IMStat: current Immune time</summary>
        float IMStat.ImmuneTime => ImmuneTime.Value;
        #endregion

        #region Properties
        /// <summary>Is the Stat Enabled? when Disable no modification can be done. All current modification can't be stopped</summary>
        public bool Active
        {
            get => active;
            set
            {
                active = value;

                OnActive.Invoke(value);

                if (active && resetOnEnable != ResetOnEnable.None) ResetValue(); //MWC — Reset the Stat if it opts in

                Debbuging($"Active: {value}");

                if (value)
                    StartRegeneration(); //If the Stat was activated start the regeneration
                else
                    StopRegeneration();
            }
        }

        /// <summary>  Name of the Stat (Using the ID)  </summary>
        public string Name
        {
            get
            {
                if (ID != null) return ID.name;
                return string.Empty;
            }
        }

        /// <summary> Current value of the Stat</summary>
        public float Value
        {
            get => value.Value;
            set => SetValue(value);
        }

        public bool IsFull => Value == MaxValue;

        /// <summary>  Check if the Stat is equal to the minimum value </summary>
        public bool IsEmpty => Value == MinValue;

        /// <summary> Current Multiplier of the Stat</summary>
        public float Multiplier
        {
            get => multiplier.Value;
            set
            {
                multiplier.Value = value;
                OnStatChanged?.Invoke(this); // MWC — Parent Link
            }
        }

        /// <summary>Returns the Normalized value of the Stat</summary>
        public float NormalizedValue => Value / MaxValue;

        /// <summary>If True: The Stat cannot be modify </summary>
        public bool IsImmune { get => immune.Value; set => immune.Value = value; }

        /// <summary>Maximum Value of the Stat</summary>
        public float MaxValue
        {
            get => maxValue.Value;

            set
            {
                maxValue.Value = value;
                OnMaxValueChange.Invoke(value);
                OnValueChangeNormalized.Invoke(NormalizedValue); //Normalized value needs to be updated because the Max has changed too
                OnStatChanged?.Invoke(this); // MWC — Parent Link
            }
        }

        /// <summary>Minimun Value of the Stat </summary>
        public float MinValue
        {
            get => minValue.Value;
            set
            {
                minValue.Value = value;
                OnMinValueChange.Invoke(value);
            }
        }

        /// <summary> A Stat with Min and Max both at zero has no range at all, so the Value is left alone:
        /// it is not clamped, and it is never considered Empty or Full. Use it for Stats that are a plain number
        /// (E.g. a weapon [Attack] value) instead of a bar </summary>
        public bool HasNoRange => MinValue == 0 && MaxValue == 0;

        /// <summary>Is the Stat Regenerating?</summary>
        public bool IsRegenerating { get; private set; }

        /// <summary>Is the Stat Degenerating?</summary>
        public bool IsDegenerating { get; private set; }

        [SerializeField] internal int EditorTabs = 0;
        public bool CanRegenerate { get => can_Regenerate.Value; set => can_Regenerate.Value = value; }
        public bool CanDegenerate { get => can_Degenerate.Value; set => can_Degenerate.Value = value; }

        /// <summary>Can the Stat Regenerate over time</summary>
        public bool Regenerate
        {
            get => regenerate.Value;
            set
            {
                if (!CanRegenerate) value = false; //If it cannot Regenerate force it to false

                regenerate.Value = value;
                regenerate_LastValue = regenerate;           //In case Regenerate is changed 

                //OnRegenerate.Invoke(value);

                Debbuging($"Regenerating: {(value ? "Yes" : "No")}");

                if (regenerate)
                {
                    //Do not Degenerate if we are Regenerating
                    degenerate.Value = false;
                    StopDegeneration();
                    StartRegeneration();
                }
                else
                {
                    //If we are no longer Regenerating Start Degenerating again in case the Degenerate was true
                    degenerate.Value = degenerate_LastValue;
                    StopRegeneration();
                    StartDegeneration();
                }
            }
        }

        /// <summary> Can the Stat Degenerate over time </summary>
        public bool Degenerate
        {
            get => degenerate.Value;
            set
            {
                if (!CanDegenerate) value = false; //If it cannot Degenerate force it to false


                degenerate.Value = value;
                degenerate_LastValue = degenerate;           //In case Regenerate is changed 


                Debbuging($"Degenerating: {(value ? "Yes" : "No")}");

                if (degenerate)
                {
                    regenerate.Value = false;     //Do not Regenerate if we are Degenerating
                    StartDegeneration();
                    StopRegeneration();
                }
                else
                {
                    regenerate.Value = regenerate_LastValue;   //If we are no longer Degenerating Start Regenerating again in case the Regenerate was true
                    StopDegeneration();
                    StartRegeneration();
                }
            }
        }

        #endregion

        [NonSerialized] private WaitForSeconds InmuneWait;

        /// <summary>Bind the Stat to its holder and capture the default values (called by Stats on enable)</summary>
        public void InitializeStat(Stats holder) => Initialize_Stat(holder);

        /// <summary>Overridable body of InitializeStat</summary>
        protected virtual void Initialize_Stat(Stats holder)
        {
            isAbove = isBelow = false;
            Owner = holder;                                     //Save the Monobehaviour to save coroutines

            if (value.Value >= Above) isAbove = true;           //This means that The Stat Value is over the Above value
            else if (value.Value <= Below) isBelow = true;      //This means that The Stat Value is under the Below value

            regenerate_LastValue = Regenerate;
            degenerate_LastValue = Degenerate;

            if (!HasNoRange && MaxValue < Value) MaxValue = Value; // MWC — a Stat with no range keeps Max at zero


            //Store all the Default values for the stats
            //The Base Max is captured only once: re-enabling the Stats must not turn add-on bonuses (Max Modifiers) into the default
            if (!defaultMaxCaptured)
            {
                DefaultMaxValue = MaxValue - MaxModifierTotal;
                defaultMaxCaptured = true;
            }
            DefaultMinValue = MinValue;
            DefaultValue = Value;

            DefaultMultiplier = Multiplier;
            DefaultDegenRate = DegenRate.Value;
            DefaultRegenRate = RegenRate.Value;



            I_Regeneration = null;
            I_Degeneration = null;
            I_ModifyPerTicks = null;

            InmuneWait = new WaitForSeconds(ImmuneTime);

            if (Active)
            {
                //MWC — [Reset On Enable] now actually happens on Enable. It used to be checked ONLY inside the Active
                //property setter, so a Stat serialized as Active never ran it: nothing assigns Active at startup, the
                //setter never fired, and the value stayed at whatever was serialized (e.g. a Charge stat stuck at 0).
                //Placed after the Default* values are captured above, so RestoreValue() still returns the authored value.
                if (resetOnEnable != ResetOnEnable.None) ResetValue();

                Regenerate = regenerate.Value; //Initialize the Regen
                Degenerate = degenerate.Value; //Initialize the Degen

                if (InvokeEventsOnEnable.Value) holder.Delay_Action(2, () => ValueEvents()); //Opt-out: skip the startup Value/Empty/Full/Below/Above events

                OnMaxValueChange.Invoke(maxValue);
            }

            Debbuging($"Initialized");
        }

        public void RestoreMultiplier() => SetMultiplier(DefaultMultiplier);
        /// <summary>Restore the Max Value to the Base Max plus the active Max Modifiers</summary>
        public void RestoreMax() => MaxValue = DefaultMaxValue + MaxModifierTotal;

        /// <summary>Sets a new Base Max Value (what RestoreMax returns to, before Max Modifiers) and applies it</summary>
        public void SetDefaultMax(float baseMax, bool clampValue = false)
        {
            DefaultMaxValue = baseMax;
            defaultMaxCaptured = true;
            SetMaxValue(baseMax + MaxModifierTotal, clampValue);
        }

        /// <summary>Sets the Max Value. When clampValue is true the current Value is lowered to the new Max if it is above it</summary>
        public void SetMaxValue(float newMax, bool clampValue)
        {
            MaxValue = newMax;
            if (clampValue && !HasNoRange && Value > MaxValue) Value = MaxValue;
        }

        /// <summary>Adds (or replaces) an additive Max Value modifier for a source. Several sources stack without overwriting each other</summary>
        public void SetMaxModifier(object source, float amount, bool clampValue = true)
        {
            if (source == null) return;

            maxModifiers ??= new();
            maxModifiers.TryGetValue(source, out var old);

            if (amount == 0) maxModifiers.Remove(source);
            else maxModifiers[source] = amount;

            var delta = amount - old;
            if (delta == 0) return;

            MaxModifierTotal += delta;
            SetMaxValue(MaxValue + delta, clampValue);
            if (Active && delta > 0) StartRegeneration(); //More room to regenerate
        }

        /// <summary>Removes the Max Value modifier of a source</summary>
        public void RemoveMaxModifier(object source, bool clampValue = true) => SetMaxModifier(source, 0, clampValue);

        /// <summary>Current Max Value modifier of a source (0 if it has none)</summary>
        public float GetMaxModifier(object source) => source != null && maxModifiers != null && maxModifiers.TryGetValue(source, out var v) ? v : 0;
        public void RestoreMin() => MinValue = DefaultMinValue;
        public void RestoreRegenRate() => SetRegenRate(DefaultRegenRate);
        public void RestoreDegenRate() => SetDegenRate(DefaultDegenRate);
        /// <summary>Restore the Value captured on Initialize</summary>
        public void RestoreValue() => Value = DefaultValue;

        public void RestoreAll()
        {
            RestoreMax();
            RestoreMin();
            RestoreMultiplier();
            ResetValue();
            RestoreDegenRate();
            RestoreRegenRate();
            Debbuging($"Restore Stat");
        }

        public virtual void SetMultiplier(float value) => Multiplier = value; // MWC — through the property so the Parent Link hears it

        #region Parent Link
        //MWC — see Stats.RefreshParentLink. Apply order is Multiplier → Max → Value (so an Additive Value is not clamped
        //by a Max that has not grown yet) and removal is the reverse. The contribution is tracked, not the value:
        //modifications made while linked survive the unlink (E.g. durability wear on a linked Attack).

        /// <summary>Link this Stat to the same Stat on the parent Stats. Null unlinks it and restores the own numbers</summary>
        public void LinkParent(IMStat parent)
        {
            if (parent == this) parent = null;
            if (ParentStat == parent) return;

            if (ParentStat != null)
            {
                ParentStat.OnStatChanged -= OnParentStatChanged;
                RemoveParentContribution();
            }

            ParentStat = parent;

            if (ParentStat != null)
            {
                ParentStat.OnStatChanged += OnParentStatChanged;
                ApplyParentContribution();
            }

            Debbuging(ParentStat != null ? $"Linked to Parent [{(ParentStat.Owner != null ? ParentStat.Owner.name : "?")}]" : "Unlinked from Parent");
        }

        /// <summary>The parent Stat changed: swap the old contribution for the new one (live link)</summary>
        private void OnParentStatChanged(IMStat parent)
        {
            RemoveParentContribution();
            ApplyParentContribution();
        }

        /// <summary>Compose the own number with the parent one and remember what was contributed</summary>
        private static float Compose(StatParentLink link, float own, float parent, out float contribution, out float baseValue)
        {
            baseValue = own;
            contribution = parent;

            return link switch
            {
                StatParentLink.Additive => own + parent,
                StatParentLink.Override => parent,
                StatParentLink.Multiply => own * parent,
                _ => own,
            };
        }

        /// <summary>Take the remembered contribution out of the current number</summary>
        private static float Decompose(StatParentLink link, float current, float contribution, float baseValue)
        {
            return link switch
            {
                StatParentLink.Additive => current - contribution,
                StatParentLink.Override => baseValue,
                StatParentLink.Multiply => contribution != 0 ? current / contribution : baseValue,
                _ => current,
            };
        }

        /// <summary>Apply the parent numbers on top of the own ones (Multiplier → Max → Value)</summary>
        private void ApplyParentContribution()
        {
            if (ParentStat == null || linkApplied) return;
            linkApplied = true;

            if (parentMultiplier != StatParentLink.None)
                Multiplier = Compose(parentMultiplier, Multiplier, ParentStat.Multiplier, out linkMult_Contribution, out linkMult_Base);

            if (parentMax != StatParentLink.None)
                MaxValue = Compose(parentMax, MaxValue, ParentStat.MaxValue, out linkMax_Contribution, out linkMax_Base);

            if (parentValue != StatParentLink.None)
            {
                var own = Value;
                SetLinkedValue(Compose(parentValue, own, ParentStat.Value, out linkValue_Contribution, out linkValue_Base));

                //MWC — remember what was REALLY added after the Min/Max clamp and rounding, not the raw parent value.
                //Otherwise a clamped link (Attack 20 + 10 clipped to Max 25) takes 10 back out on unlink and the item drifts down
                linkValue_Contribution = AppliedContribution(parentValue, own, Value, linkValue_Contribution);
            }
        }

        /// <summary>The contribution that survived the clamp: the difference (Additive) or ratio (Multiply) between the applied and the own Value</summary>
        private static float AppliedContribution(StatParentLink link, float own, float applied, float contribution)
        {
            return link switch
            {
                StatParentLink.Additive => applied - own,
                StatParentLink.Multiply => own != 0 ? applied / own : contribution,
                _ => contribution,
            };
        }

        /// <summary>Remove the parent numbers, restoring the own ones (Value → Max → Multiplier)</summary>
        private void RemoveParentContribution()
        {
            if (!linkApplied) return;
            linkApplied = false;

            if (parentValue != StatParentLink.None)
                SetLinkedValue(Decompose(parentValue, Value, linkValue_Contribution, linkValue_Base));

            if (parentMax != StatParentLink.None)
                MaxValue = Decompose(parentMax, MaxValue, linkMax_Contribution, linkMax_Base);

            if (parentMultiplier != StatParentLink.None)
                Multiplier = Decompose(parentMultiplier, Multiplier, linkMult_Contribution, linkMult_Base);
        }

        /// <summary>Write a linked Value. Clamps and rounds like SetValue but ignores [Active], so the tracked contribution is always really applied</summary>
        private void SetLinkedValue(float newValue)
        {
            if (!HasNoRange) newValue = Mathf.Clamp(newValue, MinValue, MaxValue);
            if (Round >= 0) newValue = (float)System.Math.Round(newValue, Round.Value);

            if (value.Value == newValue) return;

            value.Value = newValue;
            ValueEvents();
            OnStatChanged?.Invoke(this);
        }
        #endregion

        public virtual void ValueEvents()
        {
            if (!Active) return; //Do not Invoke Events if the Stat is Disabled!!!!

            OnValueChangeNormalized.Invoke(NormalizedValue);
            OnValueChange.Invoke(value.Value);

            //MWC — a Stat with no range (Min and Max both zero) is a plain number: it is never snapped to an end,
            //and it is never Empty or Full, so [Disable On Empty] cannot fire on it either
            if (!HasNoRange)
            {
                if (this.value <= minValue.Value)
                {
                    this.value.Value = minValue.Value;
                    OnStatEmpty.Invoke();   //if the Value is 0 invoke Empty Stat

                    if (DisableOnEmpty.Value)
                    {
                        SetActive(false);
                        return;
                    }

                }
                else if (this.value >= maxValue.Value)
                {
                    this.value.Value = maxValue.Value;
                    OnStatFull.Invoke();    //if the Value is 0 invoke Empty Stat
                }
            }

            if (Is_Above(value) && !isAbove)
            {
                OnStatAbove.Invoke();
                isAbove = true;
                isBelow = false;
            }
            else if (Is_Below(value) && !isBelow)
            {
                OnStatBelow.Invoke();
                isBelow = true;
                isAbove = false;
            }
        }

        public bool Is_Below(float value)
        {
            if (isPercent)
                return (value / MaxValue) * 100 <= Below;
            else
                return value <= Below;
        }

        public bool Is_Above(float value)
        {
            if (isPercent)
                return (value / MaxValue) * 100 >= Above;
            else
                return value >= Above;
        }

        public virtual void SetValue(float value)
        {
            if (!Active) return;

            var RealValue = HasNoRange ? value : Mathf.Clamp(value, MinValue, MaxValue); // MWC — no range, no clamp

            if ((!Active) ||                                    //If the  Stat is not Active do nothing 
                (this.value.Value == RealValue)) return;        //If the values are equal do nothing. Avoid Stack Overflow

            if (Round >= 0) RealValue = (float)System.Math.Round(RealValue, Round.Value);

            this.value.Value = RealValue;

            //Debbuging($"Value: {RealValue}");

            ValueEvents();
            OnStatChanged?.Invoke(this); // MWC — Parent Link
        }

        /// <summary>Enable or Disable a Stat </summary>
        public void SetActive(bool value) => Active = value;
        public virtual void SetRegeneration(bool value)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            Regenerate = value;
        }

        public virtual void SetDegeneration(bool value)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            Degenerate = value;
        }

        public virtual void SetCanRegenerate(bool value)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            CanRegenerate = value;
        }

        public virtual void SetCanDegenerate(bool value)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            CanDegenerate = value;
        }

        public virtual void SetImmune(bool value)
        {
            if (!Active) return;            //Ignore if the Stat is Disable

            IsImmune = value;
            Debbuging($"Is Inmune: {value}");
        }



        /// <summary>Adds or remove to the Stat Value </summary>
        public virtual void Modify(float newValue)
        {
            if (!IsImmune && Active)
            {
                Value += newValue * Multiplier; //Apply the Multiplier!

                if (Owner == null) return; //Not initialized yet: nothing to delay or regenerate

                JustModified = true; //This is to avoid the Stat to be modified while the Coroutine is running
                //Reset the JustModified after a frame 
                Owner.Delay_Action(() => { JustModified = false; });

                StartRegeneration();
                if (!Regenerate)
                    StartDegeneration();

                SetInmune();
            }
        }

        private bool JustModified = false;

        public virtual void UpdateStat()
        {
            SetValue(value);
            StartRegeneration();
            if (!Regenerate)
                StartDegeneration();
        }

        /// <summary>Adds or remove to the Stat Value</summary>
        public virtual void Modify(float newValue, float time)
        {
            if (!IsImmune && Active && Owner != null)
            {
                StopCoroutine(I_ModifySlow); //If there was a modification active .... interrupt it
                I_ModifySlow = C_SmoothChangeValue(newValue, time);
                Owner.StartCoroutine(I_ModifySlow);
                SetInmune();
            }
        }

        /// <summary>  Modify the Stat value with a 'new Value',  'ticks' times , every 'timeBetweenTicks' seconds </summary>
        public virtual void Modify(float newValue, int ticks, float timeBetweenTicks)
        {
            if (!Active || Owner == null) return; //Ignore if the Stat is Disable or not initialized
            StopCoroutine(I_ModifyPerTicks);
            I_ModifyPerTicks = C_ModifyTicksValue(newValue, ticks, timeBetweenTicks);
            Owner.StartCoroutine(I_ModifyPerTicks);
        }

        /// <summary> Add or Remove Value the 'MaxValue' of the Stat </summary>
        public virtual void ModifyMAX(float newValue)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            MaxValue += newValue;
            StartRegeneration();
        }

        /// <summary>Sets the 'MaxValue' of the Stat </summary>
        public virtual void SetMAX(float newValue)
        {
            if (!Active) return;            //Ignore if the Stat is Disable 
            MaxValue = newValue;
            StartRegeneration();
        }
        public virtual void ModifyMIN(float newValue)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            MinValue += newValue;
        }
        public virtual void SetMIN(float newValue)
        {
            if (!Active) return;            //Ignore if the Stat is Disable 
            MinValue = newValue;
        }

        public virtual void SetRegenerationWait(float newValue) => SetRegenWait(newValue); //Name Consistance change to below but keeps it in for now
        public virtual void SetRegenerationRate(float newValue) => SetRegenRate(newValue);

        /// <summary>Add or Remove Rate to the Regeneration Rate</summary>
        public virtual void ModifyRegenRate(float newValue)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            RegenRate.Value += newValue;
            StartRegeneration();
        }
        /// <summary>Sets Wait time to Regeneration</summary>
        public virtual void SetRegenWait(float newValue)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            RegenWaitTime.Value = newValue;
            if (RegenWaitTime < 0) RegenWaitTime.Value = 0;
        }
        /// <summary>Add or Remove Rate to the Degeneration Rate</summary>
        public virtual void ModifyDegenRate(float newValue)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            DegenRate.Value += newValue;
            StartDegeneration();
        }
        /// <summary>Sets Wait time to Degeneration</summary>
        public virtual void SetDegenWait(float newValue)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            DegenWaitTime.Value = newValue;
            if (DegenWaitTime < 0) DegenWaitTime.Value = 0;
        }
        /// <summary>Set a new Regeneration Rate</summary>
        public virtual void SetRegenRate(float newValue)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            RegenRate.Value = newValue;
        }
        /// <summary>Set a new Degeneration Rate</summary>
        public virtual void SetDegenRate(float newValue)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            DegenRate.Value = newValue;
        }
        /// <summary>Set a new Immune Time</summary>
        public virtual void SetImmuneTime(float newValue)
        {
            if (!Active) return;            //Ignore if the Stat is Disable
            ImmuneTime.Value = newValue;
            if (ImmuneTime < 0) ImmuneTime.Value = 0;
        }

        //MWC — None falls back to Max Value, which was the old 'resetTo' default. That keeps Restart(), RestoreAll()
        //and the [Reset] Stat Option restoring a Stat to full on prefabs that don't opt into resetting on enable.
        /// <summary> Reset the Stat to its configured reset target (None → Max Value)</summary>
        public virtual void ResetValue() => Value = (resetOnEnable == ResetOnEnable.ResetToMin) ? MinValue : MaxValue;

        /// <summary> Reset the Stat to the Default Min or Max Value</summary>
        public virtual void Reset_to_Max()
        {
            Value = MaxValue;
            // SetActive(true);
        }

        /// <summary> Reset the Stat to the Default Min  Value</summary>
        public virtual void Reset_to_Min()
        {
            Value = MinValue;
            //  SetActive(true);
        }

        /// <summary>Clean all Coroutines</summary>
        public void CleanRoutines()
        {
            StopDegeneration();
            StopRegeneration();
            StopTickDamage();
            StopSlowModification();
        }

        /// <summary>Regenerate for 'time' seconds, or start the normal regeneration when time is zero</summary>
        public virtual void RegenerateOverTime(float time)
        {
            if (Owner == null) return; //Not initialized yet

            if (time <= 0)
            {
                StartRegeneration();
            }
            else
            {
                Owner.StartCoroutine(C_RegenerateOverTime(time));
            }
        }

        protected virtual void SetInmune()
        {
            if (ImmuneTime > 0)
            {
                if (Owner != null && Owner.enabled && Owner.gameObject.activeInHierarchy)
                {
                    StopCoroutine(I_IsInmune); //If there was a active .... interrupt it
                    I_IsInmune = C_InmuneTime();
                    Owner.StartCoroutine(I_IsInmune);
                }
            }
        }

        /// <summary>Stop a coroutine on the Owner, if both exist</summary>
        private void StopCoroutine(IEnumerator Cor)
        {
            if (Cor != null && Owner != null)
                Owner.StopCoroutine(Cor);
        }

        /// <summary>Restart the regeneration coroutine when the Stat can regenerate</summary>
        protected virtual void StartRegeneration()
        {
            if (Owner == null || Owner.enabled == false || Owner.gameObject.activeInHierarchy == false) return; //If the Owner is missing, not enabled or not active do nothing

            StopRegeneration();

            if (RegenRate == 0 || !Regenerate || !CanRegenerate) return;   //Means if there's no Regeneration

            I_Regeneration = C_Regenerate();
            Owner.StartCoroutine(I_Regeneration);
        }


        /// <summary>Restart the degeneration coroutine when the Stat can degenerate</summary>
        protected virtual void StartDegeneration()
        {
            if (Owner == null || Owner.enabled == false || Owner.gameObject.activeInHierarchy == false) return; //If the Owner is missing, not enabled or not active do nothing

            StopDegeneration();

            if (DegenRate == 0 || !Degenerate || !CanDegenerate) return;  //Means there's no Degeneration

            I_Degeneration = C_Degenerate();
            Owner.StartCoroutine(I_Degeneration);
        }

        protected virtual void StopRegeneration()
        {
            if (I_Regeneration != null)
            {
                StopCoroutine(I_Regeneration);    //If there was a regeneration active .... interrupt it
                OnRegenerate.Invoke(false);
            }

            I_Regeneration = null;
            IsRegenerating = false;
        }

        protected virtual void StopDegeneration()
        {
            if (I_Degeneration != null)
            {
                StopCoroutine(I_Degeneration);    //if it was ALREADY Degenerating.. stop
                OnDegenerate.Invoke(false);
            }

            I_Degeneration = null;
            IsDegenerating = false;
        }

        protected virtual void StopTickDamage()
        {
            StopCoroutine(I_ModifyPerTicks);   //if it was ALREADY Degenerating.. stop...
            I_ModifyPerTicks = null;
        }

        protected virtual void StopSlowModification()
        {
            StopCoroutine(I_ModifySlow);       //If there was a regeneration active .... interrupt it
            I_ModifySlow = null;
        }

        /// <summary>Modify the Stats on an animal </summary>
        public void Modify(float Value, StatOption modify)
        {
            switch (modify)
            {
                case StatOption.AddValue: Modify(Value); break;
                case StatOption.SetValue: this.Value = Value; break;
                case StatOption.SubstractValue: Modify(-Value); break;
                case StatOption.ModifyMaxValue: ModifyMAX(Value); break;
                case StatOption.SetMaxValue: MaxValue = Value; break;
                case StatOption.Degenerate:
                    DegenRate.Value = Value;
                    Debbuging($"DegenRate: {DegenRate.Value}");
                    Degenerate = true;
                    break;
                case StatOption.Regenerate:
                    RegenRate.Value = Value;
                    Debbuging($"RegenRate: {Value}");
                    Regenerate = true;
                    break;
                case StatOption.DegenerateOff: Degenerate = false; break;
                case StatOption.RegenerateOff: Regenerate = false; break;
                case StatOption.Reset: ResetValue(); break;
                case StatOption.ReduceByPercent: Modify(-(MaxValue * Value / 100)); break;
                case StatOption.IncreaseByPercent: Modify(MaxValue * Value / 100); break;
                case StatOption.Multiplier: SetMultiplier(Value); break;
                case StatOption.ResetToMax: Reset_to_Max(); break;
                case StatOption.ResetToMin: Reset_to_Min(); break;
                case StatOption.Enable: SetActive(Value != 0); break;
                case StatOption.Inmune: SetImmune(Value != 0); break;
                case StatOption.RegenerateOn: Regenerate = true; break;
                case StatOption.DegenerateOn: Degenerate = true; break;
                case StatOption.RestoreRegeneration: RestoreRegenRate(); break;
                case StatOption.RestoreDegeneration: RestoreDegenRate(); break;
                case StatOption.RestoreValue: RestoreValue(); break;
                case StatOption.RestoreMax: RestoreMax(); break;
                case StatOption.RestoreMin: RestoreMin(); break;
                case StatOption.RestoreMultiplier: RestoreMultiplier(); break;
                case StatOption.MultiplierModify: SetMultiplier(Multiplier + Value); break;
                case StatOption.None: break;
                default:
                    break;
            }
        }


        #region Coroutines
        /// <summary>  I need this to use coroutines in this class because it does not inherit from Monobehaviour, Also to Identify where is this Stat coming from  </summary>
        public Stats Owner { get; private set; }
        private IEnumerator I_Regeneration;
        private IEnumerator I_Degeneration;
        private IEnumerator I_ModifyPerTicks;
        private IEnumerator I_ModifySlow;
        private IEnumerator I_IsInmune;


        protected IEnumerator C_RegenerateOverTime(float time)
        {
            float ReachValue = RegenRate > 0 ? MaxValue : MinValue;                                //Set to the default or 0
            bool Positive = RegenRate > 0;                                                          //Is the Regeneration Positive?
            float currentTime = Time.time;

            while (Value != ReachValue || currentTime > time)
            {
                Value += (RegenRate * Time.deltaTime);

                if (Positive && Value > MaxValue)
                {
                    Value = MaxValue;
                }
                else if (!Positive && Value < 0)
                {
                    Value = MinValue;
                }
                currentTime += Time.deltaTime;

                yield return null;
            }
            yield return null;
        }

        protected IEnumerator C_InmuneTime()
        {
            IsImmune = true;
            yield return InmuneWait;
            IsImmune = false;
        }

        protected IEnumerator C_Regenerate()
        {
            yield return null;

            if (RegenWaitTime > 0)
                yield return new WaitForSeconds(RegenWaitTime);          //Wait a time to regenerate

            IsRegenerating = true;
            OnRegenerate.Invoke(true);

            while (Regenerate && Value < MaxValue)
            {
                if (JustModified) //If the Stat was modified while regenerating, stop the regeneration
                {
                    Debbuging($"Regeneration Stopped because the Stat was just modified");
                    yield break;
                }

                yield return null;
                Value += (RegenRate * Time.deltaTime);
            }

            IsRegenerating = false;
            OnRegenerate.Invoke(false);

            yield return null;
        }

        protected IEnumerator C_Degenerate()
        {
            yield return null;

            if (DegenWaitTime > 0)
                yield return new WaitForSeconds(DegenWaitTime);          //Wait a time to regenerate

            IsDegenerating = true;
            OnDegenerate.Invoke(true);


            while (Degenerate && Value > MinValue)
            {
                if (JustModified) //If the Stat was modified while regenerating, stop the regeneration
                {
                    Debbuging($"Degeneration Stopped because the Stat was just modified");
                    yield break;
                }

                yield return null;
                Value -= (DegenRate * Time.deltaTime);
            }

            IsDegenerating = false;
            OnDegenerate.Invoke(false);

            yield return null;
        }

        protected IEnumerator C_ModifyTicksValue(float value, int Ticks, float time)
        {
            var WaitForTicks = new WaitForSeconds(time);

            for (int i = 0; i < Ticks; i++)
            {
                Value += value;
                if (Value <= MinValue)
                {
                    Value = MinValue;
                    break;
                }
                yield return WaitForTicks;
            }

            yield return null;

            StartRegeneration();
        }

        protected IEnumerator C_SmoothChangeValue(float newValue, float time)
        {
            StopRegeneration();
            float currentTime = 0;
            float currentValue = Value;
            newValue = Value + newValue;

            yield return null;
            while (currentTime <= time)
            {

                Value = Mathf.Lerp(currentValue, newValue, currentTime / time);
                currentTime += Time.deltaTime;

                yield return null;
            }
            Value = newValue;

            yield return null;
            StartRegeneration();
        }

        /// <summary>Editor-only console log, active when the Stat has Debug enabled</summary>
        internal void Debbuging(string value)
        {
#if UNITY_EDITOR
            if (debug) Debug.Log($"<b><color=orange>[{(Owner != null ? Owner.name : "?")} <color=yellow>{Name}</color>]</color> - <color=white> [{value}]</color></b>", Owner);
#endif
        }


        #endregion

        //MWC — was 'ResetTo {MinValue, MaxValue}'. None is the new default so a Stat only resets when asked to.
        public enum ResetOnEnable
        {
            None,
            ResetToMin,
            ResetToMax
        }
    }

    /// <summary>How a number of a Stat follows the same Stat on the closest Stats component above it in the hierarchy</summary>
    public enum StatParentLink
    {
        /// <summary>Not linked to the parent</summary>
        None,
        /// <summary>Own + Parent</summary>
        Additive,
        /// <summary>The parent number replaces the own one</summary>
        Override,
        /// <summary>Own x Parent</summary>
        Multiply,
    }
    public enum StatOption
    {
        None,
        /// <summary>Add to the Stat Value </summary>
        [InspectorName("Value/Add[+]")]
        AddValue,
        /// <summary>Set a new Stat Value </summary>
        [InspectorName("Value/Set")]
        SetValue,
        /// <summary>Remove to the Stat Value </summary>
        [InspectorName("Value/Substract[-]")]
        SubstractValue,
        /// <summary>Modify Add|Remove the Stat MAX Value </summary>
        [InspectorName("Max Value/Modify")]
        ModifyMaxValue,
        /// <summary>Set a new Stat MAX Value </summary>
        [InspectorName("Max Value/Set")]
        SetMaxValue,
        /// <summary>Enable the Degeneration </summary>
        [InspectorName("Degenerate/Value")]
        Degenerate,
        /// <summary>Disable the Degeneration </summary>
        [InspectorName("Degenerate/Stop")]
        DegenerateOff,
        /// <summary>Enable the Regeneration </summary>
        [InspectorName("Regenerate/Value")]
        Regenerate,
        /// <summary>Disable the Regeneration </summary>
        [InspectorName("Regenerate/Stop")]
        RegenerateOff,
        /// <summary>Reset the Stat to the Default Min or Max Value </summary>
        [InspectorName("Value/Reset")]
        Reset,
        /// <summary>Reduce the Value of the Stat by a percent</summary>
        [InspectorName("Value/Reduce by percent")]
        ReduceByPercent,
        /// <summary>Increase the Value of the Stat by a percent</summary>
        [InspectorName("Value/Increase by percent")]
        IncreaseByPercent,
        /// <summary>Sets the multiplier of a stat</summary>
        [InspectorName("Multiplier/Set")]
        Multiplier,
        /// <summary>Reset the Stat to the Max Value</summary>
        [InspectorName("Value/Reset to Max")]
        ResetToMax,
        /// <summary>Reset the Stat to the Min Value</summary>
        [InspectorName("Value/Reset to Min")]
        ResetToMin,
        /// <summary>Enable Disable the Stat</summary>
        Enable,
        /// <summary>Set the Immune Option of the Stat</summary>
        Inmune,
        [InspectorName("Regenerate/Start")]
        RegenerateOn,
        [InspectorName("Degenerate/Start")]
        DegenerateOn,

        [InspectorName("Regenerate/Default")]
        RestoreRegeneration,

        [InspectorName("Degenerate/Default")]
        RestoreDegeneration,

        [InspectorName("Value/Default")]
        RestoreValue,

        [InspectorName("Max Value/Default")]
        RestoreMax,

        [InspectorName("Min Value/Default")]
        RestoreMin,

        [InspectorName("Multiplier/Default")]
        RestoreMultiplier,

        [InspectorName("Multiplier/Modify")]
        MultiplierModify,
    }
}