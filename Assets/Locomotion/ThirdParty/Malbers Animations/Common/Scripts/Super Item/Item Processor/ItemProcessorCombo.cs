// Last Change: 9/17/2026 5:33pm
// Last Change: 9/10/2026 9:31pm
using MalbersAnimations.Controller;
using MalbersAnimations.Scriptables;
using System;
using UnityEngine;

namespace MalbersAnimations.Items
{

    [Serializable, AddTypeMenu("Action/Combo")]
    [HelpURL(ItemDocs.Processors + "combo")]
    public class ItemProcessorCombo : ItemProcessor
    {
        public override string DynamicName => $"Combo [{(Mode != null && comboType == ComboType.Local ? (Mode.name) : (comboSet != null ? comboSet.name : "⚠ MISSING ⚠"))}]  Branch [{Branch.Value}]";
        public override ExecuteProcessor InitialExecute => ExecuteProcessor.OnActionStart;

        public enum ComboType { Local, Asset }

        private ComboManager comboManager;

        public ComboType comboType;

        [Hide(nameof(comboType), 1)]
        public ComboSet comboSet;
        [Hide(nameof(comboType), 0)]
        [RequiredField] public ModeID Mode; // The Mode that will be used to play the Combo

        [Tooltip("Branch to Activate in the combo Manager")]
        public IntReference Branch = new(0);

        private Action ActionComboRestart;
        private Action ActionComboIncomplete;

        private bool ModeWasDisabled = false;

        private Mode ComboMode;

        public override void SetEnter(SuperItem item, ItemActionSet set, ItemAction action)
        {
            action.StopActionOnRelease = false;
        }

        public override void Awake(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (comboType == ComboType.Local && Mode == null) Debug.LogError($"[ItemProcessorCombo] The Item Processor Combo in the Item: {item.name} is missing the Mode ID reference.", item);
            else if (comboType == ComboType.Asset && comboSet == null) Debug.LogError($"[ItemProcessorCombo] The Item Processor Combo in the Item: {item.name} is missing the Combo Set reference.", item);
        }


        /// <summary>Plays the combo branch on the owner's Combo Manager. Ends the Action when the press could not start a combo and none is in flight.</summary>
        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (comboManager == null) return; // MWC — the manager is only cached on Equip; bail instead of NRE

            // MWC — the trailing comboManager.Play(Branch) that used to sit after this switch fired a second
            // time on every execute (twice for Local, and once more on top of the ComboSet for Asset).
            var played = false;

            switch (comboType)
            {
                case ComboType.Local:
                    comboManager.SetActiveCombo(Mode); // Set the Active Combo with the Mode of this item
                    played = comboManager.TryPlay(Branch);
                    break;
                case ComboType.Asset:
                    played = comboSet != null && comboSet.Play(comboManager, Branch);
                    break;
            }

            //The Action is already IsPlaying (StartAction set it before running the Processors) but the combo vetoed
            //the press (Mode not allowed in the current State, Combo/Mode in CoolDown, Mode disabled, Conditions…).
            //Its ONLY exit paths are the Combo callbacks (Restart / Incomplete), which need a Mode End of a playing
            //combo that will never come, so it stayed latched as playing forever: "Is Action Playing" reported true
            //while the character stood idle. A dropped press INSIDE a running combo (outside the activation window)
            //is different: the combo is still in flight and will end the Action itself, so it is left alone.
            if (!played && action.IsPlaying && !ComboInFlight(item))
            {
                if (item.User != null && action.debug)
                    item.User.Debugging($"[{item.name}] Action <color=white>{action.Name}</color> exited: the Combo could not start", item, "red");

                action.CanExit = true;
                action.Exit(item, set);
            }
        }

        /// <summary>True while the Combo Manager is playing a sequence of the active combo and the Animal is still inside that combo's Mode</summary>
        private bool ComboInFlight(SuperItem item)
        {
            if (!comboManager.PlayingCombo) return false;

            var mode = comboManager.ActiveCombo?.CachedMode;
            var animal = item.User != null ? item.User.Animal : null;

            return mode != null && animal != null && animal.ActiveMode == mode;
        }

        public override void ItemEquipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            //Cache the Combo Manager
            if (comboManager == null) { comboManager = item.User.ComboManager; }


            //check if is an asset or local combo, if is a local combo then we need to check if the mode is active or not

            if (comboType == ComboType.Local && Mode != null)
                ComboMode = item.User.Animal.Mode_Get(Mode);
            else if (comboType == ComboType.Asset && comboSet != null && comboSet.Mode != null)
            {
                ComboMode = item.User.Animal.Mode_Get(comboSet.Mode);
            }

            if (ComboMode != null && !ComboMode.Active)
            {
                ModeWasDisabled = true;
                ComboMode.Active = true; //Enable the Mode if it was disabled
            }

            if (comboManager)
            {
                if (comboType == ComboType.Local && Mode != null)
                    comboManager.SetActiveCombo(Mode); // Set the Active Combo with the Mode of this item
                else if (comboType == ComboType.Asset && comboSet != null)
                    comboManager.SetActiveCombo(comboSet); //MWC — select by asset identity; the Mode ID can be shared by other combos


                ActionComboRestart ??= () => OnComboRestart(item, set, action);
                ActionComboIncomplete ??= () => OnComboRestart(item, set, action);
                comboManager.OnComboRestart += ActionComboRestart;
                comboManager.OnComboIncomplete += ActionComboIncomplete;
            }
        }

        public override void StateChanged(SuperItem item, ItemActionSet set, ItemAction action, int State)
        {
            if (!action.IsPlaying || comboManager == null) return;

            // MWC — Asset combos keep their ModeID on the ComboSet; passing the null local Mode reset the manager to -1
            if (comboType == ComboType.Local && Mode != null)
                comboManager.SetActiveCombo(Mode);
            else if (comboType == ComboType.Asset && comboSet != null)
                comboManager.SetActiveCombo(comboSet); //MWC — select by asset identity; the Mode ID can be shared by other combos
        }

        public override void ItemUnequipped(SuperItem item, ItemActionSet set, ItemAction itemAction)
        {
            if (comboManager)
            {
                comboManager.OnComboRestart -= ActionComboRestart;
                comboManager.OnComboIncomplete -= ActionComboIncomplete;
            }

            if (ModeWasDisabled)
            {
                if (ComboMode != null && ComboMode.Active) ComboMode.Active = false; //Disable the Mode if it was disabled
            }
        }

        /// <summary> Exit the Action when the combo is restarted, this will allow the combo to be restarted without having to wait for the action to finish playing </summary>
        /// <param name="item"></param>
        /// <param name="set"></param>
        /// <param name="action"></param>
        private void OnComboRestart(SuperItem item, ItemActionSet set, ItemAction action)
        {
            //Debug.Log("COMBO RESTARTED");
            // action.IsPlaying = false;
            action.CanExit = true;
            action.Exit(item, set);
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (comboType == ComboType.Local && Mode == null)
                return $"Missing the Mode ID reference.";
            else if (comboType == ComboType.Asset && comboSet == null)
                return $"Missing the Combo Set reference.";
            return string.Empty;
        }
    }
}