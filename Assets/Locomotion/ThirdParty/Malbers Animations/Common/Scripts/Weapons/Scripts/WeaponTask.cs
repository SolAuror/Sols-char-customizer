using MalbersAnimations.Scriptables;
using MalbersAnimations.Weapons;
using UnityEngine;


namespace MalbersAnimations.Controller.AI
{
    public enum BrainWeaponActions { Draw_Holster, Store_Weapon, Equip_Weapon, Unequip_Weapon, Aim, Attack, StopAttack, Reload }

    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Tasks/Weapon Tasks", fileName = "new Weapon Task")]
    public class WeaponTask : MTask
    {
        public override string DisplayName => "Weapons/Weapon Tasks";
        [Tooltip("Play the mode only when the animal has arrived to the target")]
        public bool near = false;

        //MWC: New option (same as the Play Mode Task): play the Weapon Action only when the AI is inside this radius from the Brain's Target.
        [Hide("near", true)]
        [Tooltip("Play the Action if the AI Animal is within this distance value from the Target. Ignored if this value is zero")]
        public FloatReference ActivateDistance = new();

        public BrainWeaponActions Actions = BrainWeaponActions.Attack;

        [Hide("Actions", (int)BrainWeaponActions.Equip_Weapon)]
        public MWeapon Weapon;
        [Hide("Actions", (int)BrainWeaponActions.Draw_Holster)]
        public HolsterID HolsterID;
        [Hide("Actions", (int)BrainWeaponActions.Aim)]
        public bool AimValue = true;

        [Hide("Actions", (int)BrainWeaponActions.Attack)]
        public IntReference Branch = new(0);

        [Hide("Actions", (int)BrainWeaponActions.Draw_Holster, (int)BrainWeaponActions.Store_Weapon)]
        [Tooltip("Ignore Draw and Store weapon animations")]
        public bool IgnoreDrawStore = false;


        //[Hide("Actions",(int)BrainWeaponActions.Attack)]
        //public float AttackRate = 0.33f;

        //MWC: True when the Activate Distance option is being used. Zero keeps the old behavior (no distance check).
        /// <summary>True when the Activate Distance option is in use (greater than zero)</summary>
        private bool UseActivateDistance => ActivateDistance > 0;

        //MWC: Per-agent runtime state. Lives on the brain (not on this shared SO) so each AI tracks its own started flag.
        private class WeaponTaskState : BrainState
        {
            /// <summary>True once the Weapon Action has been played by this task run</summary>
            public bool ActionStarted;
        }

        /// <summary>Caches the Weapon Manager and plays the Weapon Action if the AI is near/in range of the Target</summary>
        public override void StartTask(MAnimalBrain brain, int index)
        {
            var WeaponManager = brain.Animal.GetComponentInChildren<MWeaponManager>();

            brain.TasksVars[index].mono = WeaponManager; //Store the Weapon Manager on the TaskVars

            brain.TaskState<WeaponTaskState>(index).ActionStarted = false; //MWC: reset per activation (typed state persists across re-entries)

            if (near && !brain.AIControl.HasArrived) return; //Dont play if Play on target is true but we are not near the target.

            if (!InActivateDistance(brain)) return; //MWC: Too far from the Target... wait until we are in range (UpdateTask will play it)

            if (WeaponManager)
            {
                StartAction(brain, index, WeaponManager); //MWC: the Action switch moved to StartAction so it can be delayed by the Activate Distance
            }
            else
            {
                brain.TaskDone(index);
                Debug.Log("No Weapon Manager Found on the Animal", brain.Animal);
            }
        }

        //MWC: All the Start logic of the Task. Extracted from StartTask so it can also be called from UpdateTask
        //when the Action was delayed because the Target was outside the Activate Distance.
        /// <summary>Plays the Weapon Action on the Weapon Manager</summary>
        private void StartAction(MAnimalBrain brain, int index, MWeaponManager WeaponManager)
        {
            brain.TaskState<WeaponTaskState>(index).ActionStarted = true; //MWC: Mark that the Action was already played

            switch (Actions)
            {
                case BrainWeaponActions.Equip_Weapon:
                    WeaponManager.Equip_External(Weapon);
                    break;
                case BrainWeaponActions.Draw_Holster:
                    WeaponManager.UnEquip_Fast();
                    WeaponManager.IgnoreDraw = IgnoreDrawStore;
                    WeaponManager.Holster_Equip(HolsterID);
                    break;
                case BrainWeaponActions.Aim:
                    WeaponManager.Aim_Set(AimValue);
                    break;
                case BrainWeaponActions.Attack:
                    //  WeaponManager.MainAttack(); //This will be called on Update!
                    break;
                case BrainWeaponActions.Store_Weapon:
                    WeaponManager.IgnoreStore = IgnoreDrawStore;
                    WeaponManager.Aim_Set(false); //IMPORTANT
                    WeaponManager.Store_Weapon();
                    break;
                case BrainWeaponActions.Reload:
                    WeaponManager.ReloadWeapon();
                    break;
                case BrainWeaponActions.Unequip_Weapon:
                    WeaponManager.UnEquip();
                    brain.TaskDone(index);
                    break;
                case BrainWeaponActions.StopAttack:
                    if (WeaponManager.Weapon && WeaponManager.WeaponIsActive)
                        WeaponManager.MainAttackReleased();
                    break;
                default:
                    break;
            }
        }

        //MWC: The Activate Distance check. Zero (default) keeps the old behavior: the distance is ignored.
        /// <summary>True if the Activate Distance is ignored (zero) or the Brain Target is inside that radius</summary>
        private bool InActivateDistance(MAnimalBrain brain)
        {
            if (!UseActivateDistance) return true;                              //Ignore the distance check
            if (brain.Target == null) return false;                             //No Target... so we are not in range
            return Vector3.Distance(brain.transform.position, brain.Target.position) < ActivateDistance;
        }

        /// <summary>Updates the Weapon Action and plays it when the AI finally gets in range of the Target</summary>
        public override void UpdateTask(MAnimalBrain brain, int index)
        {
            var WeaponManager = brain.TasksVars[index].mono as MWeaponManager;

            if (WeaponManager == null) return;

            var Arrived = !near || brain.AIControl.HasArrived;      //MWC: The Arrive check (when 'near' is disabled it is always true)
            var InRange = InActivateDistance(brain);                //MWC: The Activate Distance check (true when the distance is zero)

            //MWC: The Action was skipped on StartTask because the Target was too far. Play it as soon as we are in range
            if (UseActivateDistance && Arrived && InRange && !brain.TaskState<WeaponTaskState>(index).ActionStarted)
            {
                StartAction(brain, index, WeaponManager);
            }

            if (!Arrived || !InRange)
            {
                if (Actions != BrainWeaponActions.Attack) //Attack needs to release the Attack when the Character is Far
                    return; //Dont play if Play on target is true but we are not near the target.
            }

            switch (Actions)
            {
                case BrainWeaponActions.Draw_Holster:

                    //The weapon tried to be Draw but it did not draw
                    if (WeaponManager.DrawWeapon && WeaponManager.WeaponAction == Weapon_Action.None)
                    {
                        WeaponManager.UnEquip_Fast();
                        WeaponManager.IgnoreDraw = IgnoreDrawStore;
                        WeaponManager.Holster_Equip(HolsterID);
                    }

                    //Set the Task to Done if we have finish Drawing the weapon
                    if (WeaponManager.ActiveHolster == HolsterID && WeaponManager.WeaponAction == Weapon_Action.Idle)
                    {
                        brain.TaskDone(index);
                    }
                    break;
                case BrainWeaponActions.Store_Weapon:
                    //Set the Task to Done if we have finish Storing the weapon
                    if (WeaponManager.WeaponAction == Weapon_Action.None)
                    {
                        brain.TaskDone(index);
                    }
                    break;
                case BrainWeaponActions.Aim:
                    if (WeaponManager.Weapon /* && WeaponManager.Weapon.IsReady*/) //??????????????????????????????????
                    {
                        //Debug.Log("Aim Ready!!!");
                        brain.TaskDone(index);
                    }
                    break;
                case BrainWeaponActions.Attack:
                    {
                        if (!Arrived || !InRange) //MWC: meaning the target has gone far (Arrive Distance or Activate Distance)
                        {
                            WeaponManager.MainAttackReleased();

                            if (WeaponManager.Weapon) WeaponManager.Weapon.Input = false;
                            return;
                        }

                        if (WeaponManager.Weapon)
                        {
                            if (WeaponManager.Weapon is MMelee)
                            {
                                WeaponManager.MainAttack(Branch);
                            }
                            else //
                            {
                                if (!WeaponManager.Weapon.Input ||
                                    (WeaponManager.Weapon as MShootable).releaseProjectile == MShootable.Release_Projectile.OnAttackStart)
                                {
                                    WeaponManager.MainAttack(Branch);
                                }
                                else
                                {
                                    WeaponManager.MainAttackReleased();
                                }
                            }
                        }
                        else
                        {
                            WeaponManager.MainAttack(); //No weapons attack
                        }


                        //Debug.Log("ATTACK1");
                    }
                    break;
                case BrainWeaponActions.Reload:
                    if (!WeaponManager.IsReloading)
                    {
                        //Debug.Log("Reload Done");
                        brain.TaskDone(index);
                    }
                    break;
                default:
                    break;
            }

        }


        //public override void ExitAIState(MAnimalBrain brain, int index)
        //{
        //    base.ExitAIState(brain, index);

        //    var WeaponManager = brain.TasksVars[index].mono as MWeaponManager; //Get the cache Weapon Manager

        //    if (WeaponManager != null)
        //    {



        ////    if (Actions == BrainWeaponActions.Attack) { WeaponManager.MainAttackReleased(); }

        //        //if (WeaponManager)
        //        //{
        //        //    switch (Actions)
        //        //    {
        //        //        case BrainWeaponActions.Equip_Weapon:
        //        //           // WeaponManager.Equip_External(Weapon);
        //        //            break;
        //        //        case BrainWeaponActions.Draw_Holster:
        //        //            //WeaponManager.Holster_Equip(HolsterID);
        //        //            break;
        //        //        case BrainWeaponActions.Aim:
        //        //            WeaponManager.Aim_Set(AimValue);
        //        //            break;
        //        //        case BrainWeaponActions.Attack:
        //        //            WeaponManager.MainAttackReleased();
        //        //            break;
        //        //        case BrainWeaponActions.Store_Weapon:
        //        //           // WeaponManager.Store_Weapon();
        //        //            break;
        //        //        case BrainWeaponActions.Reload:
        //        //            WeaponManager.ReloadWeapon();
        //        //            break;
        //        //        case BrainWeaponActions.Unequip_Weapon:
        //        //            WeaponManager.UnEquip();
        //        //            brain.TaskDone(index);
        //        //            break;
        //        //        default:
        //        //            break;
        //        //    }
        //        //}

        //    }
        //}


        void Reset()
        { Description = "Use common Methods of the Weapon Manager to play on the AI Character"; }
    }
}