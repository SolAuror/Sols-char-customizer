// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Scriptables;
using UnityEngine;
using MalbersAnimations.Events;

#if UNITY_EDITOR
using UnityEditor; //MWC — was an unguarded using: UnityEditor in a runtime file breaks player builds (see ItemProcessorReload)
#endif

namespace MalbersAnimations.Items
{
    //MWC — WHEN the Charge is cleared (both the Charge Stat and the Animator parameter)
    public enum ChargeResetOn
    {
        [InspectorName("Action End")] ActionEnd = 0,
        [InspectorName("Action Released")] ActionReleased = 1,
    }

    [System.Serializable, AddTypeMenu("Action/Charge")]
    [HelpURL(ItemDocs.Processors + "charge")]
    public class ItemProcessorCharge : ItemProcessor
    {
        public FloatReference ChargeTime = new(1);

        public StringReference ChargeAnimParam = new("Charge");

        //MWC — the shared Charge value lives in a Stat on the item's Stats component (next to the SuperItem).
        //It is NO LONGER normalized: the charge fills the Stat across its own Min → Max range, so the Stat can be
        //authored with whatever scale the game needs. Full charge == Stat.IsFull.
        //Optional: when empty (or the Stat is missing) the processor still works standalone using its internal timer.
        [Tooltip("Optional Stat on the Item's Stats component used to publish the Charge.\n" +
            "The charge fills the Stat from its Min Value to its Max Value over the Charge Time.\n" +
            "Other Actions, Conditions ([Stats] Full/Value…), UI and upgrades can read it")]
        public StatID ChargeStat;

        //MWC — some release/fire animations are driven BY the charge, so emptying it the instant the input is
        //released snaps the pose. This chooses the moment the charge is cleared.
        [Tooltip("When the Charge is cleared (the Charge Stat and the Animator parameter).\n\n" +
            "• Action Released: cleared as soon as the input is released. The Charge Stat still survives the release " +
            "pass (one extra frame) so the Processors below can read it.\n\n" +
            "• Action End: the charge KEEPS its value after the release and is only cleared when the Action really " +
            "ends (Mode/animation finished, duration elapsed, interrupted). Use this when the release animation is " +
            "driven by the Charge Animator parameter or reads the Charge Stat.")]
        public ChargeResetOn ResetOn = ChargeResetOn.ActionEnd;

        //MWC — a FAILED Action is not a normal end: with [Reset On: Action End] the charge would stay latched on the
        //Stat and the Animator for as long as the Action hangs (a Charged Mode does not stop on release), leaving the
        //bow visually drawn while the Action waits to restart.
        [Tooltip("Clear the Charge as soon as the Action FAILS — its Conditions (Action, Target or Set) or its " +
            "PreRequisites stopped being valid while it was playing — whatever [Reset On] says.\n" +
            "E.g. the aim angle leaves the allowed range mid draw: the bow does not stay charged while the Action " +
            "waits to restart")]
        public bool ResetOnFailed = true;

        public bool ShowEvents = true; // show the charge events in the Inspector


        [Hide(nameof(ShowEvents))]
        public GameObjectEvent OnChargeStart = new();
        [Hide(nameof(ShowEvents))]
        public GameObjectEvent OnChargeCompleted = new();

        [Hide(nameof(ShowEvents))]
        public GameObjectEvent OnChargeInterrupted = new();


        [Hide(nameof(ShowEvents))]
        public GameObjectEvent OnChargeReleased = new();

        //MWC — charge bookkeeping is processor-private now (was item.CurrentCharge / item.ChargeCompleted on the SuperItem;
        //nothing else read those, and two Charge processors on one item used to fight over them)
        private float ElapsedCharge;    //MWC — elapsed seconds [0-ChargeTime]
        private bool Completed;         //MWC — replaces item.ChargeCompleted

        //MWC — bumped on every ActionStart. The deferred Stat reset (see ActionReleased) captures it and only applies
        //when it is still the same charge, so it can never wipe a charge the player already restarted.
        private int ChargeSession;

        /// <summary> Cached Charge Stat (resolved on Item Equipped). Null when no Stat is used </summary>
        private IMStat Charge;

        private int AnimatorHash;
        private Animator CharAnimator;

        //MWC — surface the non-default reset moment on the collapsed row
        public override string DynamicName =>
            $"Charge: {ChargeTime.Value} seconds{(ResetOn == ChargeResetOn.ActionEnd ? " [Reset On Action End]" : "")}";

        public override ExecuteProcessor InitialExecute => ExecuteProcessor.OnActionUpdated;

        public override void ItemEquipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            CharAnimator = item.User.Anim;

            if (CharAnimator && MTools.FindAnimatorParameter(CharAnimator, AnimatorControllerParameterType.Float, ChargeAnimParam.Value))
            {
                AnimatorHash = Animator.StringToHash(ChargeAnimParam.Value);
            }

            Charge = item.Stat_Get(ChargeStat); //MWC — optional shared Charge Stat (null → internal timer only)
        }

        //MWC — write the charge into the shared Charge Stat (when one is assigned), in the Stat's own range
        private void PublishCharge(float value)
        {
            if (Charge != null)
            {
                Charge.Value = value;
            }
            //MWC — removed an un-gated Debug.Log here. It shipped in builds and now that the Action's CoolDown no
            //longer throttles Update(), this runs EVERY frame while charging.
        }

        //MWC — charge value for a [0-1] progress: mapped onto the Stat's Min → Max range, or the raw progress
        //when no Stat is used. Also guards the Stat being null, which the old Mathf.Lerp(Charge.MinValue…) did not.
        private float ChargeValue(float progress) =>
            Charge != null ? Mathf.Lerp(Charge.MinValue, Charge.MaxValue, progress) : progress;

        //MWC — the Stat/Animator value the charge starts and resets to (the Stat's Min, or zero without a Stat)
        private float EmptyCharge => ChargeValue(0);

        public override void ActionStart(SuperItem item, ItemActionSet set, ItemAction action)
        {
            ElapsedCharge = 0;              //MWC — was item.CurrentCharge = 0
            Completed = false;              //MWC — was item.ChargeCompleted = false
            ChargeSession++;                //MWC — invalidates a deferred reset still pending from the previous charge

            var empty = EmptyCharge;
            PublishCharge(empty);           //MWC — reset the shared Stat

            OnChargeStart.Invoke(item.UserGo);         // Invoke the charge start event

            if (AnimatorHash != 0) CharAnimator.SetFloat(AnimatorHash, empty); // Reset the animator parameter at the start of charging
        }

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action, float delta)
        {
            if (Completed) return; //MWC — was item.ChargeCompleted

            if (ElapsedCharge <= ChargeTime)
            {
                var progress = ElapsedCharge / ChargeTime.Value;    // Charge progress (0 to 1)
                var chargeValue = ChargeValue(progress);            //MWC — mapped onto the Stat's own range

                PublishCharge(chargeValue);                         //MWC — keep the shared Stat in sync while charging

                if (AnimatorHash != 0) CharAnimator.SetFloat(AnimatorHash, chargeValue);

                ElapsedCharge += delta; //MWC — was item.CurrentCharge

                if (ElapsedCharge >= ChargeTime.Value)
                {
                    ElapsedCharge = ChargeTime.Value;

                    var full = ChargeValue(1);
                    PublishCharge(full); //MWC — full charge → Stat.IsFull / OnStatFull fire on the Stats side

                    if (AnimatorHash != 0) CharAnimator.SetFloat(AnimatorHash, full);

                    OnChargeCompleted.Invoke(item.UserGo);
                    Completed = true; //MWC — was item.ChargeCompleted

                    ProcessorCompleted(item, set, action); // Mark the processor as completed when the charge is completed
                }
            }
        }

        public override void ActionReleased(SuperItem item, ItemActionSet set, ItemAction action)
        {
            Completed = true; // Mark the charge as completed to prevent further charging //MWC — was item.ChargeCompleted

            if (ElapsedCharge < ChargeTime.Value) //MWC — was CurrentCharge < ChargeMax (compared seconds against the output scale: latent bug)
            {
                OnChargeInterrupted.Invoke(item.UserGo);
                ProcessorInterrupted(item, set, action); //MWC — signal following processors flagged OnPrevProcessorInterrupted
            }

            //MWC — [Action End]: the charge must OUTLIVE the release (the fire/release animation is driven by it).
            //It is cleared later, in ActionEnd — or by the SetExit/Unequip/Disable safety nets below.
            if (ResetOn == ChargeResetOn.ActionEnd) return;

            //MWC — the Animator parameter is a pose driver: it must drop to empty on this same frame, nothing else reads it.
            if (AnimatorHash != 0) CharAnimator.SetFloat(AnimatorHash, EmptyCharge);

            //MWC — the Charge STAT, on the other hand, must survive the rest of the release pass. ItemAction.ReleasedAction
            //walks the Processors in order, so every Processor BELOW this one flagged [On Action Released] — typically
            //[Projectile - Fire] scaling its force by the charge — is released AFTER this call and used to read an already
            //emptied Stat. Deferring the reset by one frame lets the whole release pass see the real charge.
            var session = ChargeSession;
            var deferred = item.Delay_Action(0f, () =>
            {
                if (session == ChargeSession) PublishCharge(EmptyCharge); //Only if no new charge started meanwhile
            });

            //Delay_Action returns null when the Item is disabled or inactive (holstered/dropped on release): no coroutine
            //will ever run, so empty it right away. Nothing is left to read the charge in that case anyway.
            if (deferred == null) PublishCharge(EmptyCharge);

            OnChargeReleased.Invoke(item.UserGo);
        }

        #region MWC — [Reset On: Action End] teardown

        /// <summary> Clear the charge right now: Stat back to empty and Animator parameter back to empty </summary>
        private void ResetCharge()
        {
            ChargeSession++;    //Invalidate any deferred reset still pending from the release pass
            ElapsedCharge = 0;
            Completed = true;   //Nothing left to charge until the next ActionStart

            PublishCharge(EmptyCharge);

            //CharAnimator can already be destroyed when this comes from OnDisable at scene teardown
            if (AnimatorHash != 0 && CharAnimator != null) CharAnimator.SetFloat(AnimatorHash, EmptyCharge);
        }

        //MWC — the Action really finished (Mode/animation ended, duration elapsed, released with Stop On Release,
        //taken over by another Action, or interrupted). This is where the charge is cleared in [Action End] mode.
        public override void ActionEnd(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (ResetOn == ChargeResetOn.ActionEnd) ResetCharge();
        }

        //MWC — [Reset On Failed]: the Action was ended by a failed Condition/PreRequisite. Cleared here no matter what
        //[Reset On] says, and it also cancels the deferred reset the release pass may have just queued.
        public override void ActionFailed(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (ResetOnFailed) ResetCharge();
        }

        //MWC — safety nets. These paths end the Action WITHOUT going through Exit_Internal, so ActionEnd never fires
        //and a held charge would stay latched on the Animator/Stat. Only needed in [Action End] mode: in the default
        //mode the charge is already empty by then.
        public override void SetExit(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (ResetOn == ChargeResetOn.ActionEnd) ResetCharge();
        }

        public override void ItemUnequipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (ResetOn == ChargeResetOn.ActionEnd) ResetCharge();
        }

        public override void OnDisable(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (ResetOn == ChargeResetOn.ActionEnd) ResetCharge();
        }

        #endregion

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            //MWC — validate the optional Charge Stat authoring (edit-time: SuperItem.Stats is not set yet, so use TryGetComponent)
            if (ChargeStat != null)
            {
                if (!item.TryGetComponent<Stats>(out var stats)) return $"A Charge Stat is assigned but the Item has no [Stats] component. Add one next to the Super Item with a [{ChargeStat.name}] Stat.";

                var chargeStat = stats.Stat_Get(ChargeStat);
                if (chargeStat == null) return $"The Item's [Stats] component does not contain the [{ChargeStat.name}] Stat.";

                //MWC — the "Max Value must be 1" rule is gone: the charge is no longer normalized, it fills the
                //Stat's own Min → Max range. Only a range that cannot hold a charge is worth reporting.
                if (chargeStat.MaxValue <= chargeStat.MinValue)
                    return $"The [{ChargeStat.name}] Stat has an empty range (Max {chargeStat.MaxValue} ≤ Min {chargeStat.MinValue}), so the charge can never fill it.";
            }

            return string.Empty;
        }

#if UNITY_EDITOR
        public override void OnCreated(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (ChargeStat == null) ChargeStat = MTools.GetInstance<StatID>("Charge");
            EnsureChargerStat(item); //MWC — was assigned to an unused local
        }

        //MWC — ensure the Item's GameObject has a Stats component containing the Charger Stat; returns it (or null)
        private Stats EnsureChargerStat(SuperItem item)
        {
            if (ChargeStat == null) return null;

            var stats = item.GetComponent<Stats>();
            if (stats == null) stats = Undo.AddComponent<Stats>(item.gameObject); //Add the Stats component if missing

            if (!stats.stats.Exists(s => s != null && s.ID == ChargeStat))
            {
                stats.stats.Add(new Stat
                {
                    ID = ChargeStat,
                    maxValue = new(1f),
                });
            }

            EditorUtility.SetDirty(stats);
            return stats;
        }
#endif
    }
}
