// Last Change: 9/13/2026 2:18pm
using MalbersAnimations.Reactions;
using UnityEngine;

namespace MalbersAnimations
{
    /// <summary>Damagee interface for components that can be damaged</summary>
    public interface IMDamage : ISurface
    {
        /// <summary>Last Direction the Damage came from</summary>
        Vector3 HitDirection { get; set; }

        /// <summary>Position of the Interaction </summary>
        Vector3 HitPosition { get; }

        Transform Transform { get; }

        ///// <summary>Last Position of the actual hit</summary>
        //Vector3 HitPosition { get; set; }

        /// <summary>Who is doing the Damage?</summary>
        GameObject Damager { get; set; }

        /// <summary>Who is Receiving the Damage?</summary>
        GameObject Damagee { get; }

        /// <summary>Damageable that receives the hits for the whole character (a Hit Box forwards to it). Returns itself when it is the main one, never null</summary>
        IMDamage Root { get; }

        /// <summary>What Collider got the interaction? </summary>
        Collider HitCollider { get; set; }

        /// <summary>Last Force Applied to the Damager</summary>
        ForceMode LastForceMode { get; set; }

        /// <summary>  Method to receive damage from an Attacker  </summary>
        /// <param name="Direction">Direction where the damage comes from</param>
        /// <param name="Damager">Who is sending the Damage?</param>
        /// <param name="stat">What stat to modify</param>
        /// <param name="IsCritical">was the damage critical</param>
        /// <param name="ignoreDamageeM">Ignore Damagee Multiplier</param>
        /// <param name="element">Element sent by the </param>
        void ReceiveDamage(
            Vector3 Direction,
            Vector3 Position,
            GameObject Damager,
            StatModifier stat,
            bool IsCritical,
            Reaction2 custom,
            bool ignoreDamageeM,
            StatElement element, bool missed);

        //void ReceiveDamage(DamageData damage);

        /// <summary>Method to receive damage from an Attacker (Simplified)</summary>
        void ReceiveDamage(StatID stat, float amount);

        /// <summary>True when the Can Receive Damage conditions allow a hit right now (e.g. false while the Character is Dodging). Damagers ask this to skip their hit effects</summary>
        bool CanReceiveDamage();

        /// <summary>Sets a Damage Profile on the main Damageable</summary>
        void Profile_Set(string name);

        /// <summary>Restore to the Default Profile</summary>
        void Profile_Restore();
    }

    /// <summary>The Damager Interface</summary>
    public interface IMDamager : IMLayer
    {
        /// <summary> ID of the Damager </summary>
        int Index { get; }

        /// <summary>Enable/Disable the Damager</summary>
        bool Active { get; set; }

        /// <summary>Owner of the Damager, Usually the Character. This is used to avoid Hitting yourself</summary>
        GameObject UserGo { get; set; }

        /// <summary>Do Damage with Attack Triggers</summary>
        void DoDamage(bool value, int profile);
    }

    /// <summary>Used to activate Damager GameObject By its ID (Damagers with Triggers). E.g An animal has several Damagers</summary>
    public interface IMDamagerSet
    {
        /// <summary> Activate an specific Damager by its ID. Zero(0) Deactivate all Damagers. -1 Activate all Damagers  </summary>
        /// <param name="ID"> ID of the Attack Trigger... this will enable or disable the Colliders</param>
        /// <param name="profile">Profile needed to activate the Damager</param>
        void ActivateDamager(int ID, int profile);

        void DamagerAnimationStart(int hash);

        void DamagerAnimationEnd(int hash);
    }

    public interface ISurface
    {
        SurfaceID Surface { get; }
    }
}