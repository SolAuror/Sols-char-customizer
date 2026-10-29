// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Weapons;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable]
    public abstract class C2_MWeaponConditions : ConditionCore
    {
        [Hide(nameof(LocalTarget))] public MWeapon Target;
        public virtual void SetTarget(MWeapon n) => Target = n;
        protected override void _SetTarget(Object target) => VerifyComponent(target, ref Target); //MWC — the result of VerifyComponent was discarded, so Target was never assigned
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;
    }

    [System.Serializable, AddTypeMenu("Weapon/Is Equipped")]
    public class C2_WeaponEquipped : C2_MWeaponConditions
    {
        protected override bool _Evaluate() => Target.IsEquipped;
    }
}