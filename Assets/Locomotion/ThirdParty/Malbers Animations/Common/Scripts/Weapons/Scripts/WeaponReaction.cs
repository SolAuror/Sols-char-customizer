// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Scriptables;
using System;
using UnityEngine;

namespace MalbersAnimations.Reactions
{

    [System.Serializable]
    public abstract class WeaponManagerReactionBase : MReaction
    {
        public override Type ReactionType => typeof(MWeaponManager);
    }


    [System.Serializable, AddTypeMenu("Malbers/Weapon Manager/Weapon Manager Global Actions")]
    public class WeaponReaction : WeaponManagerReactionBase
    {

        public override string DynamicName
        {
            get
            {
                var display = $"Weapon Manager [{Actions}]"; //Name of the Reaction

                switch (Actions)
                {
                    case WeaponActions.Equip:
                        display += $" [{(Weapon != null ? Weapon.name : "None")}]";
                        break;
                    case WeaponActions.Unequip:
                        display += $" [Current Weapon]";
                        break;
                    case WeaponActions.EquipFast:
                        display += $" [{(Weapon != null ? Weapon.name : "None")}]";
                        break;
                    case WeaponActions.UnequipFast:
                        display += $" [Current Weapon]";
                        break;
                    case WeaponActions.HolsterClear:
                        display += $" [{(Holster != null ? Holster.name : "None")}]";
                        break;
                    case WeaponActions.HolsterClearAll:
                        break;
                    case WeaponActions.NextHolster:
                        break;
                    case WeaponActions.PreviousHolster:
                        break;
                    case WeaponActions.ResetCombat:
                        break;
                    case WeaponActions.StoreWeapon:
                        display += $" Current";
                        break;
                    case WeaponActions.DrawWeapon:
                        display += $" from active Holster";
                        break;
                    default:
                        break;
                }
                return display;
            }
        }

        public enum WeaponActions
        {
            Equip,
            Unequip,
            EquipFast,
            UnequipFast,
            HolsterClear,
            HolsterClearAll,
            NextHolster,
            PreviousHolster,
            ResetCombat,
            StoreWeapon,
            DrawWeapon
        }
        public WeaponActions Actions = WeaponActions.Equip;

        [Hide("Actions", 0, 2)]
        public GameObject Weapon;
        [Hide("Actions", 4)]
        public HolsterID Holster;

        protected override bool _TryReact(Component component)
        {
            var target = component as MWeaponManager;

            switch (Actions)
            {
                case WeaponActions.Equip:
                    if (target.UseHolsters)
                        target.Holster_SetWeapon(Weapon);
                    else
                        target.Equip_External(Weapon);
                    break;
                case WeaponActions.Unequip: target.UnEquip(); break;
                case WeaponActions.EquipFast: target.Equip_Fast(Weapon); break;
                case WeaponActions.UnequipFast: target.UnEquip_Fast(); break;
                case WeaponActions.HolsterClear: target.Holster_Clear(Holster); break;
                case WeaponActions.HolsterClearAll: target.HolsterClearAll(); break;
                case WeaponActions.NextHolster: target.Holster_Next(); break;
                case WeaponActions.PreviousHolster: target.Holster_Previous(); break;
                case WeaponActions.ResetCombat: target.ResetCombat(); break;
                case WeaponActions.StoreWeapon: target.Store_Weapon(); break;
                case WeaponActions.DrawWeapon: target.Draw_Weapon(); break;
                default: break;
            }
            return true;
        }
    }

    /// <summary>
    /// Reaction to Equip or Unequip a Weapon from the Weapon Manager
    /// </summary>
    [System.Serializable, AddTypeMenu("Malbers/Weapon Manager/Weapon Equip-Unequip")]
    public class WeaponReactionEquip_Unequip : WeaponManagerReactionBase
    {
        //null-safe: [Weapon] is a reference whose Value can be empty
        public override string DynamicName => Actions switch
        {
            WeaponActions.EquipExternal => $"Weapon Manager: Equip External [{(Weapon != null && Weapon.Value != null ? Weapon.Value.name : "None")}]",
            WeaponActions.UnequipByID => $"Weapon Manager: Unequip [{(WeaponID != null ? WeaponID.name : "None")}]",
            _ => "Weapon Manager: Unequip Current",
        };

        public enum WeaponActions { EquipExternal, UnequipCurrent, UnequipByID }

        [Hide("Actions", 0, 2)]
        public GameObjectReference Weapon;

        [Hide("Actions", 2)]
        public WeaponID WeaponID;

        public WeaponActions Actions = WeaponActions.EquipExternal;

        protected override bool _TryReact(Component component)
        {
            if (component is MWeaponManager manager)
            {
                switch (Actions)
                {
                    case WeaponActions.EquipExternal: manager.Equip_External(Weapon.Value); break;

                    case WeaponActions.UnequipCurrent:
                        manager.UnEquip();
                        break;
                    case WeaponActions.UnequipByID:
                        if (manager.Weapon == null && manager.Weapon.ID == WeaponID)
                        {
                            manager.UnEquip();
                        }
                        break;
                    default:
                        break;
                }

            }
            return false;
        }


        [System.Serializable, AddTypeMenu("Malbers/Weapon Manager/Weapon Holster-UnHolster (Draw-Store)")]
        public class WeaponReactionHolster_UnHolster : WeaponManagerReactionBase
        {

            //the Holster is only shown for the actions that use one (same rule as its [Hide] attribute)
            public override string DynamicName => Actions is WeaponActions.HolsterClear or WeaponActions.DrawWeapon
                ? $"Weapon Manager [{Actions}] [{(Holster != null ? Holster.name : "None")}]"
                : $"Weapon Manager [{Actions}]";

            public enum WeaponActions { HolsterClear, HolsterClearAll, NextHolster, PreviousHolster, DrawWeapon, StoreCurrentWeapon }

            [Hide("Actions", true, 0, 4)]
            public HolsterID Holster;

            public WeaponActions Actions;


            protected override bool _TryReact(Component reactor)
            {
                if (reactor is MWeaponManager manager)
                {
                    switch (Actions)
                    {
                        case WeaponActions.HolsterClear: manager.Holster_Clear(Holster); break;
                        case WeaponActions.HolsterClearAll: manager.HolsterClearAll(); break;
                        case WeaponActions.NextHolster: manager.Holster_Next(); break;
                        case WeaponActions.PreviousHolster: manager.Holster_Previous(); break;
                        case WeaponActions.DrawWeapon: manager.Holster_Equip(Holster); break;
                        case WeaponActions.StoreCurrentWeapon: manager.Store_Weapon(); break;
                        default:
                            break;
                    }
                }
                return false;
            }
        }

        [System.Serializable, AddTypeMenu("Malbers/Weapon Manager/Weapon Drop")]
        public class WeaponReactionDrop : WeaponManagerReactionBase
        {
            public override string DynamicName => $"Weapon Manager [Drop Current Weapon]";

            public TransformReference DropPoint;
            public bool ResetLocalPosition = true;
            public bool DisableRigidbody = false;
            public bool ParentToDropPoint = false;


            protected override bool _TryReact(Component reactor)
            {
                if (reactor is MWeaponManager manager)
                {
                    var Weapon = manager.Weapon;

                    if (Weapon != null)
                    {
                        if (DropPoint.Value != null)
                            manager.Drop_Weapon(DropPoint.Value, ResetLocalPosition);
                        else
                            manager.Drop_Weapon();

                        if (DisableRigidbody)
                        {
                            if (Weapon.TryGetComponent<Rigidbody>(out var rb)) rb.isKinematic = true;
                        }

                        if (ParentToDropPoint && DropPoint.Value != null)
                        {
                            Weapon.transform.SetParent(DropPoint.Value);
                            if (ResetLocalPosition)
                            {
                                Weapon.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                            }
                        }

                        return true;
                    }
                }
                return false;
            }
        }
    }
}
