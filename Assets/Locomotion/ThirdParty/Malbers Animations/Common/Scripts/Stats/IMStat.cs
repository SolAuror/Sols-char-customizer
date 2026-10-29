using MalbersAnimations.Events;
using System;
using UnityEngine.Events;

namespace MalbersAnimations
{
    /// <summary>Contract of a Stat as seen by the Stats component and every consumer. Stat is the authored implementation;
    /// wrap it (see Stats.WrapStat) to intercept reads and writes, 
    /// E.g. for multiplayer. Originates from KariVargr's IMStatsPlus branch</summary>
    public interface IMStat
    {
        #region Identity
        /// <summary>Key Identifier of the Stat</summary>
        StatID ID { get; }
        /// <summary>Name of the Stat (from the ID)</summary>
        string Name { get; }
        /// <summary>Stats component holding this Stat. Null until InitializeStat runs</summary>
        Stats Owner { get; }
        #endregion

        #region Values
        /// <summary>Is the Stat enabled? A disabled Stat ignores modifications</summary>
        bool Active { get; set; }
        /// <summary>Current value of the Stat</summary>
        float Value { get; set; }
        /// <summary>Maximum value of the Stat</summary>
        float MaxValue { get; set; }
        /// <summary>Minimum value of the Stat</summary>
        float MinValue { get; set; }
        /// <summary>Multiplier applied to every Modify call</summary>
        float Multiplier { get; set; }
        /// <summary>Value divided by Max Value</summary>
        float NormalizedValue { get; }
        /// <summary>True when Min and Max are both zero: a plain number, never clamped, never Empty or Full</summary>
        bool HasNoRange { get; }
        /// <summary>Value equals Max Value</summary>
        bool IsFull { get; }
        /// <summary>Value equals Min Value</summary>
        bool IsEmpty { get; }
        /// <summary>While true the Stat cannot be modified</summary>
        bool IsImmune { get; set; }
        #endregion

        #region Regeneration and Degeneration
        /// <summary>Is the Stat set to regenerate over time</summary>
        bool Regenerate { get; set; }
        /// <summary>Is the Stat set to degenerate over time</summary>
        bool Degenerate { get; set; }
        /// <summary>Is regeneration allowed at all</summary>
        bool CanRegenerate { get; set; }
        /// <summary>Is degeneration allowed at all</summary>
        bool CanDegenerate { get; set; }
        /// <summary>Is the regeneration coroutine running</summary>
        bool IsRegenerating { get; }
        /// <summary>Is the degeneration coroutine running</summary>
        bool IsDegenerating { get; }
        /// <summary>Current regeneration rate (units per second)</summary>
        float RegenRate { get; }
        /// <summary>Current degeneration rate (units per second)</summary>
        float DegenRate { get; }
        /// <summary>Seconds to wait before regenerating</summary>
        float RegenWaitTime { get; }
        /// <summary>Seconds to wait before degenerating</summary>
        float DegenWaitTime { get; }
        /// <summary>Seconds the Stat stays immune after a modification</summary>
        float ImmuneTime { get; }
        #endregion

        #region Default values (captured on InitializeStat)
        /// <summary>Value captured on initialization</summary>
        float DefaultValue { get; }
        /// <summary>Max Value captured on initialization</summary>
        float DefaultMaxValue { get; }
        /// <summary>Min Value captured on initialization</summary>
        float DefaultMinValue { get; }
        /// <summary>Multiplier captured on initialization</summary>
        float DefaultMultiplier { get; }
        /// <summary>Regeneration Rate captured on initialization</summary>
        float DefaultRegenRate { get; }
        /// <summary>Degeneration Rate captured on initialization</summary>
        float DefaultDegenRate { get; }
        #endregion

        #region Events
        /// <summary>Invoked when the Value reaches the Max Value</summary>
        UnityEvent OnStatFull { get; }
        /// <summary>Invoked when the Value reaches the Min Value</summary>
        UnityEvent OnStatEmpty { get; }
        /// <summary>Generic Stat event</summary>
        UnityEvent OnStat { get; }
        /// <summary>Invoked once when the Value drops below the Below threshold</summary>
        UnityEvent OnStatBelow { get; }
        /// <summary>Invoked once when the Value rises above the Above threshold</summary>
        UnityEvent OnStatAbove { get; }
        /// <summary>Invoked with the normalized Value whenever it changes</summary>
        FloatEvent OnValueChangeNormalized { get; }
        /// <summary>Invoked with the Value whenever it changes</summary>
        FloatEvent OnValueChange { get; }
        /// <summary>Invoked with the new Max Value</summary>
        FloatEvent OnMaxValueChange { get; }
        /// <summary>Invoked with the new Min Value</summary>
        FloatEvent OnMinValueChange { get; }
        /// <summary>Invoked when degeneration starts (true) or stops (false)</summary>
        BoolEvent OnDegenerate { get; }
        /// <summary>Invoked when regeneration starts (true) or stops (false)</summary>
        BoolEvent OnRegenerate { get; }
        /// <summary>Invoked when the Stat is enabled or disabled</summary>
        BoolEvent OnActive { get; }
        #endregion

        #region Parent Link
        /// <summary>True when at least one number of this Stat follows the same Stat on the parent Stats</summary>
        bool UsesParentLink { get; }
        /// <summary>True while a parent contribution is applied</summary>
        bool IsLinkedToParent { get; }
        /// <summary>Stat on the parent Stats this Stat is linked to. Null when unlinked</summary>
        IMStat ParentStat { get; }
        /// <summary>Link to the same Stat on the parent Stats. Null unlinks and restores the own numbers</summary>
        void LinkParent(IMStat parent);
        /// <summary>Raised whenever the Value, Max Value or Multiplier changes (used by the Parent Link)</summary>
        event Action<IMStat> OnStatChanged;
        #endregion

        #region Lifecycle
        /// <summary>Bind the Stat to its holder and capture the default values</summary>
        void InitializeStat(Stats holder);
        /// <summary>Re-apply the Value and restart regeneration or degeneration</summary>
        void UpdateStat();
        /// <summary>Invoke the value events (change, full, empty, above, below)</summary>
        void ValueEvents();
        /// <summary>Stop every running coroutine of the Stat</summary>
        void CleanRoutines();
        #endregion

        #region Value operations
        /// <summary>Enable or disable the Stat</summary>
        void SetActive(bool value);
        /// <summary>Set the Value (clamped unless the Stat has no range)</summary>
        void SetValue(float value);
        /// <summary>Add or remove to the Value applying the Multiplier</summary>
        void Modify(float value);
        /// <summary>Apply a Stat Option with the given value</summary>
        void Modify(float value, StatOption type);
        /// <summary>Add or remove to the Value smoothly over 'time' seconds</summary>
        void Modify(float value, float time);
        /// <summary>Add or remove 'value' every 'timeBetweenTicks' seconds, 'ticks' times</summary>
        void Modify(float value, int ticks, float timeBetweenTicks);
        /// <summary>Set the Max Value</summary>
        void SetMAX(float value);
        /// <summary>Add or remove to the Max Value</summary>
        void ModifyMAX(float value);
        /// <summary>Set the Min Value</summary>
        void SetMIN(float value);
        /// <summary>Add or remove to the Min Value</summary>
        void ModifyMIN(float value);
        /// <summary>Set the Multiplier</summary>
        void SetMultiplier(float value);
        /// <summary>Reset the Value to its configured reset target (None means Max Value)</summary>
        void ResetValue();
        /// <summary>Set the Value to the Max Value</summary>
        void Reset_to_Max();
        /// <summary>Set the Value to the Min Value</summary>
        void Reset_to_Min();
        /// <summary>Restore the Value captured on initialization</summary>
        void RestoreValue();
        /// <summary>Restore the Max Value captured on initialization</summary>
        void RestoreMax();
        /// <summary>Restore the Min Value captured on initialization</summary>
        void RestoreMin();
        /// <summary>Restore the Multiplier captured on initialization</summary>
        void RestoreMultiplier();
        /// <summary>Restore the Regeneration Rate captured on initialization</summary>
        void RestoreRegenRate();
        /// <summary>Restore the Degeneration Rate captured on initialization</summary>
        void RestoreDegenRate();
        /// <summary>Restore every number captured on initialization</summary>
        void RestoreAll();
        #endregion

        #region Regeneration operations
        /// <summary>Start or stop regenerating</summary>
        void SetRegeneration(bool value);
        /// <summary>Start or stop degenerating</summary>
        void SetDegeneration(bool value);
        /// <summary>Allow or forbid regeneration</summary>
        void SetCanRegenerate(bool value);
        /// <summary>Allow or forbid degeneration</summary>
        void SetCanDegenerate(bool value);
        /// <summary>Set the Regeneration Rate</summary>
        void SetRegenRate(float value);
        /// <summary>Set the seconds to wait before regenerating</summary>
        void SetRegenWait(float value);
        /// <summary>Add or remove to the Regeneration Rate</summary>
        void ModifyRegenRate(float value);
        /// <summary>Set the Degeneration Rate</summary>
        void SetDegenRate(float value);
        /// <summary>Set the seconds to wait before degenerating</summary>
        void SetDegenWait(float value);
        /// <summary>Add or remove to the Degeneration Rate</summary>
        void ModifyDegenRate(float value);
        /// <summary>Set the Stat immune (cannot be modified) or not</summary>
        void SetImmune(bool value);
        /// <summary>Set the seconds the Stat stays immune after a modification</summary>
        void SetImmuneTime(float value);
        /// <summary>Regenerate for 'time' seconds, or start the normal regeneration when time is zero</summary>
        void RegenerateOverTime(float time);
        #endregion

        #region Thresholds
        /// <summary>Is the given value above the Above threshold (percent or absolute)</summary>
        bool Is_Above(float value);
        /// <summary>Is the given value below the Below threshold (percent or absolute)</summary>
        bool Is_Below(float value);
        #endregion
    }
}
