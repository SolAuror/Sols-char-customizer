using MalbersAnimations.Weapons;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Arrived to Target", order = -100)]
    public class WeaponDecision : MAIDecision
    {
        public override string DisplayName => "Weapon/Check Weapon";

        public enum WeaponDecisionOptions { WeaponEquipped, WeaponIs, IsReloading, IsAiming, IsAttacking, AmmoInChamber, TotalAmmo, ChamberSize, NoWeapon }

        public Affected CheckOn = Affected.Self;
        public WeaponDecisionOptions weapon = WeaponDecisionOptions.WeaponIs;

        [Hide(nameof(weapon), (int)WeaponDecisionOptions.WeaponIs)]
        public WeaponID weaponType;

        [Hide(nameof(weapon), (int)WeaponDecisionOptions.AmmoInChamber, (int)WeaponDecisionOptions.TotalAmmo, (int)WeaponDecisionOptions.ChamberSize)]
        public ComparerNumber comparer = ComparerNumber.Equal;
        [Hide(nameof(weapon), (int)WeaponDecisionOptions.AmmoInChamber, (int)WeaponDecisionOptions.TotalAmmo, (int)WeaponDecisionOptions.ChamberSize)]
        public int value;

        public override void PrepareDecision(MAnimalBrain brain, int Index)
        {
            switch (CheckOn)
            {
                case Affected.Self:
                    brain.DecisionsVars[Index].mono = brain.Animal.FindComponent<MWeaponManager>(); //Cache the Weapon Manager in the Animal
                    break;
                case Affected.Target:
                    brain.DecisionsVars[Index].mono = brain.Target.FindComponent<MWeaponManager>(); //Cache the Weapon Manager in the Target
                    break;
                default:
                    break;
            }
        }


        public override bool Decide(MAnimalBrain brain, int index)
        {
            var WM = brain.DecisionsVars[index].mono as MWeaponManager;

            return (weapon) switch
            {
                WeaponDecisionOptions.WeaponEquipped => WM.Weapon != null,
                WeaponDecisionOptions.WeaponIs => WM.Weapon != null && WM.Weapon.WeaponID == weaponType,
                WeaponDecisionOptions.IsReloading => WM.IsReloading,
                WeaponDecisionOptions.IsAiming => WM.Aim,
                WeaponDecisionOptions.IsAttacking => WM.IsAttacking,
                WeaponDecisionOptions.AmmoInChamber => (WM.Weapon != null && WM.Weapon is MShootable s1) && s1.AmmoInChamber.MCompare(value, comparer),
                WeaponDecisionOptions.TotalAmmo => (WM.Weapon != null && WM.Weapon is MShootable s2) && s2.TotalAmmo.MCompare(value, comparer),
                WeaponDecisionOptions.ChamberSize => (WM.Weapon != null && WM.Weapon is MShootable s3) && s3.ChamberSize.MCompare(value, comparer),
                WeaponDecisionOptions.NoWeapon => WM.Weapon == null,
                _ => false,
            };
        }
    }
}