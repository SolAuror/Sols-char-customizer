// Last Change: 9/18/2026 9:20am
using MalbersAnimations.Controller;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Items
{
    [AddTypeMenu("Animal/Play Mode"), System.Serializable]
    [HelpURL(ItemDocs.Processors + "play-mode")]
    public class ItemProcessorMode : ItemProcessor
    {
        public override string DynamicName =>
            $"Mode [{(mode != null ? ($"{mode.name}:{mode.ID}") : "<Empty>")}] - [{AbilityStatus}] " +
            $"Ability: [{(AbilityIndex.Value == -99 ? "-99 = Random" : AbilityIndex.Value)}]" +
            $"{(Forced ? " [Forced]" : "")}";

        public override ExecuteProcessor InitialExecute => ExecuteProcessor.OnActionStart;

        public ModeID mode;
        public IntReference AbilityIndex = new(-99);
        public AbilityStatus AbilityStatus = AbilityStatus.PlayOnce;

        [Hide(nameof(AbilityStatus), 1, 2)]
        [Tooltip("Duration of the Mode activation. If 0, the Ability will not override it's default duration")]
        public FloatReference Duration = new(0.5f);

        [Hide(nameof(AbilityStatus), (1))]
        [Tooltip("Charge Value to set the Ability when activated. If 0, the Ability will not override it's default duration. ModePower is updated in the Animator using this value. From 0 to [X]  ")]
        public FloatReference Charge = new(0);

        [Tooltip("Force the Mode to be activated even if the Animal is not in the correct state")]
        public bool Forced = false;

        private Mode CacheMode { get; set; }


        public override void SetEnter(SuperItem item, ItemActionSet set, ItemAction action)
        {
            CacheMode = item.User.Animal.Mode_Get(mode); //Store the Mode in a Cache when the item is equipped

            if (CacheMode == null)
            {
                Debug.LogWarning($"<B><color=yellow>{item.name}:</color> <color=white> The Mode Processor cannot find the Mode <color=cyan>{mode.name}</color> in the Animal Modes</color></B>", item);
                return;
            }

            CacheMode.SetActive(true); //Enable the Mode when the Set is equipped

            //MWC — [Charged] now joins PlayOnce/ActiveByTime. Releasing the input on a Charged ability does NOT end it:
            //Mode_Interrupt only sets ModeStatus to -2, which is the signal that DRIVES the release animation
            //(Charge Start → Charge Loop → Charge Release). The Mode keeps Playing until that last state exits and
            //fires ModeEnd. Leaving this true made ItemAction.ReleasedAction call Exit() on the release frame, killing
            //the Action — and every Processor teardown with it (the Charge reset) — while the release anim still played.
            action.StopActionOnRelease =
                AbilityStatus != AbilityStatus.PlayOnce &&
                AbilityStatus != AbilityStatus.ActiveByTime &&
                AbilityStatus != AbilityStatus.Charged;
        }

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (CacheMode == null) return;

            if (Forced)
            {
                if (CacheMode.ForceActivate(AbilityIndex, AbilityStatus, Duration, Charge)) //Force the Mode to be activated
                {
                    CacheMode.ActiveAbility.ChargeValue.Value = Charge.Value; //Set the charge value if the ability is found
                }
                return;
            }

            if (CacheMode.TryActivate(AbilityIndex, AbilityStatus, Duration, Charge))
            {
                CacheMode.ActiveAbility.ChargeValue.Value = Charge.Value; //Set the charge value if the ability is found
                CacheMode.InputValue = action.InputValue;
            }
            else
            {
                if (action.debug)
                    Debug.Log($"<B><color=yellow>{item.name}:</color>  <color=red>Failed </color> <color=white>to Activate the Mode <color=cyan>[{mode.name} [{mode.ID}]]</color> with Ability Index [{AbilityIndex.Value}] and Status [{AbilityStatus}]</color>. Ability Not Found</B>");
            }
        }

        public override void ActionReleased(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (execute == ExecuteProcessor.OnActionReleased) return; //If the Processor is set to execute on Action Released do nothing here because it will be executed in the Execute method

            if (CacheMode == null) return; //MWC — everything below dereferences it (SetEnter already warned it is missing)

            CacheMode.SetInputValue(false);  //Update the Input in the Mode

            //The input was released while the Mode is still PREPARING this Charged Ability (TryActivate ran, the
            //animator has not entered the state yet: a long-press input let go right as it fired). Mode.Interrupt()
            //does nothing here (no Mode is active yet) and PreActivate forces the Ability input ON for every Charged
            //activation, so AnimationTagEnter saw the button as held and started the charge loop with nothing holding
            //it: the character stayed charging, every other Mode failed with "needs to finish the current ability",
            //until the button was pressed AND released a second time. Clear the Ability input so the enter interrupts
            //the charge at once, and keep the Action alive: the Mode End of the release animation is what ends it.
            var preparingCharge = AbilityStatus == AbilityStatus.Charged && !CacheMode.Playing && item.User.Animal.IsPreparingMode
                && CacheMode.ActiveAbility != null && (AbilityIndex.Value == -99 || CacheMode.ActiveAbility.Index == AbilityIndex.Value);

            if (preparingCharge) CacheMode.ActiveAbility.InputValue = false;

            //MWC — [Charged]: Mode_Interrupt (ModeStatus = -2) is NOT a teardown here, it is what ADVANCES the animator
            //from the Charge Loop into the Charge Release state. It must run on every release, so it was hoisted above
            //the StopActionOnRelease guard — a Charged Action no longer stops on release, and used to never reach it.
            if (AbilityStatus == AbilityStatus.Charged) CacheMode.Interrupt();

            //MWC — the old switch collapsed to this: its other branches were empty, and PlayOnce/Charged only repeated
            //the SetInputValue(false) already done above.

            //MWC — the Mode is not playing: it failed to activate, or it already finished. There is no ModeEnd left to
            //wait for, so end the Action now. Checked BEFORE the guard below, otherwise an Action that does not stop on
            //release (PlayOnce / ActiveByTime / Charged) would hang playing forever when its Mode never started.
            if (!CacheMode.Playing && action.IsPlaying && !preparingCharge)
            {
                action.Exit(item, set);
                return;
            }

            //MWC — nothing else to do: an Action that stops on release is exited by ItemAction.ReleasedAction right
            //after this, and one that does not (PlayOnce / ActiveByTime / Charged) is exited by ModeEnd below.
        }

        /// <summary>A Mode ended on the Owner: end this Action, or re-arm it when its Input is still held</summary>
        public override void ModeEnd(SuperItem item, ItemActionSet set, ItemAction action, int Mode, int Ability)
        {
            if (!action.IsPlaying) return; //If the Action is on hold by a secondary action do nothing

            //A Mode End that lands on the very frame this Action started belongs to the PREVIOUS activation, not to
            //this one. Restarting a playing Action (interrupt self) runs its [On Action Start] Processors again, and
            //this Processor is usually the first: its ForceActivate exits the Ability that is still playing, and that
            //Exit raises Mode End synchronously, on this call stack. Handling it here ended the Action that had JUST
            //started (its [On Action End] Processors ran too), and StartAction then went on running the remaining
            //[On Action Start] Processors on a dead Action: a Shield Parry re-blocked the Sword attacks with nobody
            //left to unblock them. The fresh activation is ended by ITS own Mode End. Same rule as ItemAction.Interrupt.
            if (action.JustActivate) return;
            if (action.OnHoldBySecondary) return; //If the Action is on hold by a secondary action do nothing
            if (CacheMode == null) return; //MWC — everything below dereferences it (SetEnter already warned it is missing)

            if (action.IsPlaying && CacheMode.ID.ID != Mode || AbilityIndex.Value != -99 && AbilityIndex.Value != Ability)
            {
                if (item.MainAction == action && item.MainAction.InputValue)
                {
                    if (action.debug)
                        Debug.Log($"<B><color=yellow>{item.name}:</color> This Action [{action.Name}] is still playing!! but it was interrupted by other mode", item); //MWC — gated behind action debug (was un-gated build spam)

                    //This Mode End runs on the call stack of whoever is ending the Mode, and that caller is usually
                    //about to start the next Ability itself: a Mode.ForceActivate in flight (its Exit() is what fires
                    //this), or the [Exit Ability] handoff of a Mode Behaviour. Restarting the Action right here re-arms
                    //OUR Ability first and leaves IsPreparingMode ON, so the Ability that caused the exit is rejected
                    //with "already preparing another Mode" — a [Block Shield Hit] forced by the Damageable could not
                    //interrupt a held [Block Shield] nor itself, and the [Exit Ability] that returns the shield to the
                    //held Block lost to this restart too. Defer it instead: whoever is mid-activation wins, and the
                    //re-check next frame still puts the held Ability back when nothing took over.
                    var restart = item.User != null && item.User.Animal != null
                        ? item.User.Animal.Delay_Action(() => RestartHeldAction(item, set, action))
                        : null;

                    //Delay_Action returns null when the Animal is disabled or inactive: nothing is going to activate an
                    //Ability in that case either, so fall back to the immediate restart
                    if (restart == null) action.StartAction(item, set);

                    return;
                }
            }

            //Our Mode ended on a DIFFERENT Ability (a [Block Shield Hit] played by the Damageable reaction while the
            //Block was held) and the input is already released: nothing else will ever end this Action. The branch
            //above only handles the held input (restart), and the ability mismatch below used to return without a
            //teardown, so the Block stayed [Playing] with the shield down and its Secondary (Parry) kept stealing the
            //Attack input. The matching-ability path exits on a released input, this is the same rule for a mismatch.
            if (CacheMode.ID.ID == Mode && AbilityIndex.Value != -99 && AbilityIndex.Value != Ability && !action.InputValue)
            {
                action.CanExit = true;
                CacheMode.InputValue = false;
                action.Exit(item, set);
                return;
            }

            if (CacheMode.ID.ID != Mode || AbilityIndex.Value != -99 && AbilityIndex.Value != Ability) return;

            if (action.IsSecondary && AbilityStatus == AbilityStatus.PlayOnce)
            {
                // if (action.debug) Debug.Log($"End Secondary Action:{action.Name}]", item); //MWC — gated behind action debug

                CacheMode.InputValue = false;
                action.Exit(item, set);

                if (item.MainAction != null)
                {
                    if (item.MainAction.InputValue)
                    {
                        if (action.debug)
                            Debug.Log($"<B><color=yellow>{item.name}:</color> Play Main Action: <color=white>[{item.MainAction.Name}]</color>", item); //MWC — gated behind action debug

                        item.MainAction.StartAction(item, set); //Execute the Mode again because the Input is still pressed? only for mode Processors
                    }
                    else
                    {
                        if (action.debug)
                            Debug.Log($"<B><color=yellow>{item.name}:</color> Exit Main Action: <color=white>[{item.MainAction.Name}]</color>", item); //MWC — gated behind action debug

                        item.MainAction.Exit(item, set);
                    }
                    //return; //Do no exit the Action if the Input is still pressed
                }
            }


            ////Maybe Try to Activate after a a different mode has ended?
            ////MWC — gated behind action debug: this fired on EVERY Mode end, in builds too
            //if (action.debug)
            //    Debug.Log($"Cache <Color=White><B>[[{CacheMode.Name}]]</B></Color>. Mode End <Color=White><B>[Action:{action.Name}] </B></Color>" +
            //        $"Input <Color=White><B>[{action.InputValue}]]</B></Color> <Color=White><B>[Mode[{Mode}]]. Ab: [{Ability}]  OnHoldBySecondary {action.OnHoldBySecondary}</B></Color> ", item);

            if (!action.InputValue)
            {
                action.CanExit = true;
                CacheMode.InputValue = false;
                action.Exit(item, set);
            }

            if (AbilityStatus == AbilityStatus.Forever)
            {
                Execute(item, set, action); //Try to Activate the Mode again
            }
        }

        /// <summary>Re-arms the held Action one frame after a Mode End, unless an Ability already took over</summary>
        private void RestartHeldAction(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (item == null || set == null || action == null || CacheMode == null) return;

            //The Action ended by itself, or the input was released while we waited: there is nothing to re-arm
            if (!action.IsPlaying || !action.InputValue) return;

            //Another Action took the Item over while we waited (a Secondary put us on hold, or a different Main Action
            //started). Restarting from here would end that one instead — it owns the Item now
            if (item.MainAction != action || action.OnHoldBySecondary) return;

            //The Mode handed off to another Ability on its own ([Exit Ability]), or the Ability that interrupted us is
            //playing / still preparing. Either way something owns the Mode now, so leave it alone: its own Mode End
            //brings us back here when it finishes
            if (CacheMode.Playing || item.User.Animal.IsPreparingMode) return;

            action.StartAction(item, set);
        }

        public override void ActionExit(SuperItem item, ItemActionSet set, ItemAction itemAction)
        {
            if (CacheMode == null) return; //MWC — the Mode may not exist on the Animal (SetEnter already warned about it)

            CacheMode.InputValue = false; //Make sure the Input is set to false when the Action is exited
        }


        public override void SetExit(SuperItem item, ItemActionSet set, ItemAction itemAction)
        {
            if (CacheMode == null) return; //MWC — was dereferenced right after a null-conditional call

            CacheMode.SetInputValue(false); //Update the Input in the cache mode
            CacheMode.Interrupt();
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (mode == null) return $"Mode ID reference is missing.";
            return string.Empty;
        }
    }
}