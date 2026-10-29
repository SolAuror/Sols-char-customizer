// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Conditions;
using MalbersAnimations.Scriptables;
using UnityEngine;


namespace MalbersAnimations.Items
{
    [System.Serializable]
    public abstract class C2_PouchManager : ConditionCore
    {
        [Hide(nameof(LocalTarget))] public PouchManager Target;

        protected override void _SetTarget(Object target) => VerifyComponent(target, ref Target);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;

    }

    [System.Serializable, AddTypeMenu("Super Item/Pouch/Has Ammo")]
    public class C2_Pouch_HasAmmo : C2_PouchManager
    {
        public override string DynamicName => $"Has Ammo in Pouch [{(ID == null ? "<Select>" : ID.name)}]";

        [Tooltip("Projectile to check if the pouch has ammo for it")]
        public ProjectileID ID;
        [Tooltip("Check if the pouch has more, less or the same amount of ammo than the specified amount")]
        public ComparerNumber compare = ComparerNumber.Greater;
        [Tooltip("Amount of ammo to check in the pouch")]
        public IntReference Amount = new(0);
        protected override bool _Evaluate()
        {
            if (Target == null) return false;

            return Target.HasAmmo(ID, Amount, compare);
        }
    }
}
