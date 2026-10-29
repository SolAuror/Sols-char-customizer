// Last Change: 9/30/2026 10:15am
using System;
using UnityEngine;
using UnityEngine.Pool;


namespace MalbersAnimations
{
    /// <summary> Interface to identify Projectiles   </summary>
    public interface IProjectile : IMLayer, IPoolGameObject
    {
        /// <summary>Initial Velocity (Direction * Power) </summary>
        Vector3 Velocity { get; set; }

        /// <summary>Gravity Direction Vector</summary>
        Vector3 Gravity { get; set; }

        //MWC: [AfterDistance] was removed from this interface. It only ever meant anything to a ballistic
        //trajectory, so it now lives on FlyingMovement alone. Launchers set it with a Movement Override

        /// <summary>Offset to position the projectile</summary>
        Vector3 PosOffset { get; set; }

        /// <summary>Offset to rotate the projectile </summary>
        Vector3 RotOffset { get; set; }

        /// <summary>Has the projectile impacted with something</summary>
        bool HasImpacted { get; set; }

        /// <summary> Pool - Added IObjectPool<GameObject> thisProjectilePool new parameter to Prepare  </summary>
        /// <summary>Prepares the Projectile to be fired</summary>
        void Prepare(GameObject Owner, Vector3 Gravity, Vector3 ProjectileVelocity, LayerMask HitLayer, QueryTriggerInteraction triggerInteraction, IObjectPool<GameObject> thisProjectilePool);

        /// <summary>Multiplies the Damage for a value, Same as the MDamager</summary>
        void SetDamageMultiplier(float multiplier);

        /// <summary>Prepares the Bullet Damage</summary>
        void PrepareDamage(StatModifier modifier, float CriticalChance, float CriticalMultiplier, StatElement element);

        /// <summary>Fires the Projectile after being prepared</summary>
        void Fire();

        /// <summary>Fires the Projectile after being prepared</summary>
        void Fire(Vector3 Velocity);
    }

    public interface ITrajectory
    {
        /// <summary> Projectile Launch Velocity(v3) Direction*Power </summary>
        Vector3 Velocity { get; }

        /// <summary>Start Position of the Thrower</summary>
        Transform AimOrigin { get; }

        /// <summary>Sends if the Trajectory can be predicted or not</summary>
        Action<bool> Predict { get; set; }
    }

    /// <summary>Interface to Identify Thrower components</summary>
    public interface IThrower : ITrajectory
    {
        /// <summary>Gravity Direction Vector</summary>
        Vector3 Gravity { get; }

        //MWC: [AfterDistance] was removed from this interface too. It lives on FlyingMovement alone, and
        //MDisplayTrajectory reads it from the projectile so the predicted arc matches what actually flies

        /// <summary>Layers to Interact</summary>
        LayerMask Layer { get; set; }
        QueryTriggerInteraction TriggerInteraction { get; set; }

        GameObject UserGo { get; }

        /// <summary>Shoot the projectile </summary>
        void Fire();

        /// <summary>Projectile prefab the thrower is currently set to fire</summary>
        GameObject Projectile { get; }

        /// <summary>Set a projectile to shoot</summary>
        void SetProjectile(GameObject newProjectile);
    }
}