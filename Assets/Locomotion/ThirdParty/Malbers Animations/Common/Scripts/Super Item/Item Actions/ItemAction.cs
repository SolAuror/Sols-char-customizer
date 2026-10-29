// Last Change: 9/21/2026 9:56am
using MalbersAnimations.Conditions;
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using UnityEngine;
using UnityEngine.Events;

namespace MalbersAnimations.Items
{
    public enum ItemActionType { Regular, Secondary, Uninterrupted }
    public enum ItemActivationType { Pressed, Toggle }

    [System.Serializable]
    public class ItemAction
    {
        [Tooltip("Name of the Action. This value is used for the Required action parameter")]
        public StringReference name = new("New Action");

        public string Name => name.Value;

        public BoolReference active = new(true); //If the Action Set is active or not

        [Tooltip("Cooldown between two ACTIVATIONS of the Action. The Action cannot start again until it elapses.\n" +
            "It does NOT throttle the Processors while the Action is playing (see Update Rate)")]
        public FloatReference rate = new(0);

        //MWC — split out of 'rate'. The CoolDown used to gate the whole per-frame Update(), so it also throttled every
        //[On Action Updated] Processor: a Charge with a 1 second Rate only charged once per second (and received a
        //single frame's delta). They are two different things now:
        // • rate       → cooldown BETWEEN activations.
        // • updateRate → repeat rate WHILE the Action is playing.
        [Tooltip("Repeat rate for the [On Action Updated] Processors while the Action is playing.\n\n" +
            "• 0 = every frame. Required by time based Processors (Charge, Wait).\n" +
            "• > 0 = automatic repetition. E.g. 0.1 on a Fire Processor flagged [On Action Updated] = 10 shots per second.\n\n" +
            "It does NOT affect the Rate (CoolDown) between activations")]
        public FloatReference updateRate = new(0);

        [Tooltip("If true, the action will interrupt itself if start action is called again")]
        public BoolReference interrupSelf = new(true);

        //MWC — [Restart On Conditions]: a held input keeps the Action ARMED. When the Conditions cut it short
        //(E.g. an [Aim Angle] limit leaving its range) the Action ends as usual, but it starts again by itself as soon
        //as the Conditions pass again — the player does not have to release and press the input a second time.
        [Tooltip("If the Action is ended by its Conditions (Action or Set) while the input is STILL pressed, it will start " +
            "again automatically as soon as the Conditions pass again.\n" +
            "E.g. aiming a bow while mounted: the aim angle leaves the allowed range so the Action ends, and it resumes " +
            "by itself when the angle is valid again.\n\n" +
            "Only a CONDITIONS failure arms it. A Duration that elapsed, an interruption, or a released input do not.\n\n" +
            "On a SECONDARY Action it also covers its Master: the Secondary goes down with the Master and, if its own " +
            "input is still pressed, it starts again by itself as soon as the Master is playing once more. " +
            "E.g. a [Charge] on the Fire Action resumes with the Aim it depends on")]
        public BoolReference restartOnConditions = new(false);

        /// <summary> True while the Action is armed, waiting for its Conditions to pass again to restart itself </summary>
        public bool WaitingForConditions { get; private set; }

        //MWC: Dual Wielding - avoid a double call when two identical items are held (one on a secondary/fall-through equip point)
        [Tooltip("Dual Wielding: Only run this action on the item held on its PRIMARY equip point.\n" +
            "A fall-through copy on a secondary point (e.g. the off-hand of two identical weapons) will NOT execute it, preventing a double call.")]
        public BoolReference primaryHandOnly = new(false);

        //MWC — [Fast Weapons] (Assassin's Creed style): the Manager listens for the inputs of HOLSTERED items and
        //draws the item instantly (no Equip Mode) and starts the pressed Action.
        //It is a RANK, not a flag, and one single scale: it ranks the holstered candidates against each other AND
        //against the Action the equipped Item has on the same input.
        [Tooltip("[Fast Weapons]: Pressing this Action's input while the Item is HOLSTERED draws it instantly " +
            "(skipping the Equip animation) and starts this Action.\n\n" +
            "• 0 = this Action is never fast equipped.\n" +
            "• Higher WINS: when several holstered Items answer the same input, the highest value is the one drawn.\n" +
            "E.g. Sword [Charge Attack] = 1 and Two Hand Axe [Charge Attack] = 2 → carrying both draws the Axe, " +
            "carrying only the Sword draws the Sword.\n\n" +
            "• It also ranks against the Item ALREADY IN HAND: an equipped Action keeps its input only while its own " +
            "value is not below the best holstered candidate's (a tie keeps it in hand).\n" +
            "E.g. an equipped Dagger [Attack] = 0 gives the press to a holstered Sword [Attack] = 2.\n\n" +
            "Requires [Use Fast Weapons] enabled on the Super Item Manager")]
        public IntReference fastEquip = new(0);

        [Tooltip("[Fast Weapons]: When disabled, this Action ALWAYS yields its input to another item's [Fast Equip] " +
            "Action while such a holstered candidate exists, whatever the two [Fast Equip] values are.\n" +
            "E.g. the Bow's [Quick Fire] on Attack1 is skipped so the holstered Sword is fast-drawn and attacks instead.\n\n" +
            "When ENABLED the Action still yields to a candidate that OUTRANKS its own [Fast Equip] value.\n" +
            "The Action works normally when no candidate is holstered or [Use Fast Weapons] is off")]
        public BoolReference validOnFastWeapons = new(true);

        //MWC — Hands this Action needs while it plays (e.g. Aim on a Bow pulls the string with the off hand).
        [Tooltip("Hands this Action FREES from the Rein while it is PLAYING.\n\n" +
            "The hand of the Equip Point holding the Item is always taken, and so are the hands the Action Set declares; " +
            "this only ADDS to them, for Actions that grab the second hand (Aiming a Bow, bracing a Rifle, a 2H swing).\n" +
            "Released as soon as the Action ends. Leave empty when the Action does not need an extra hand")]
        [Flag] public ItemHands FreeHands;

        [Tooltip("Duration of the Action, this can be used to automatically end the action after a certain time. If time is zero the action will end if the input is released")]
        public FloatReference duration = new(0);

        [Tooltip("Priority of the Action. Regular actions can be interrupted by other actions, If a Higher Priority Action is playing, it cannot be interrupted by other actions")]
        public int priority;

        /// <summary>  Priority of the Action. Regular actions can be interrupted by other actions, If a Higher Priority Action is playing, it cannot be interrupted by other actions </summary>
        public int Priority => priority;

        public float Duration { get => duration; set => duration.Value = value; }
        /// <summary>
        /// Time.time the Action was last ACTIVATED. The Rate (CoolDown) and the Duration are both measured from it.
        /// </summary>
        //MWC — it is no longer re-stamped by Update(). Refreshing it every frame made 'ActionTime + Duration &lt; Time.time'
        //impossible to reach, so an Action with a Duration never ended by itself, and the CoolDown was counted from the
        //last update pass instead of from the activation.
        public float ActionTime { get; set; }

        /// <summary> Time.time of the last [On Action Updated] pass. Used only by <see cref="updateRate"/> </summary>
        public float LastUpdateTime { get; set; }
        public bool Active { get => active; set => active.Value = value; }

        [Tooltip("Action Activation by Input. Pressed or Toggle")]
        public ItemActivationType activation = ItemActivationType.Pressed;

        //MWC — was a hand-written property whose only reason to exist was a commented-out log
        public bool IsPlaying { get; set; }

        /// <summary>
        /// True once the release pass has run for the CURRENT activation. An Action that does not stop on release
        /// (a PlayOnce/Charged Mode: see <see cref="ItemProcessorMode.SetEnter"/>) stays IsPlaying until its Mode ends,
        /// and a press+release inside that window used to run the WHOLE release pass a second time — firing a second
        /// projectile from a single activation — even though the press itself had been rejected by the CoolDown.
        /// Cleared by <see cref="StartAction"/>, so a real re-activation releases again.
        /// </summary>
        public bool Released { get; private set; }

        /// <summary> True once the Processors were told this activation FAILED, so they are notified only once </summary>
        private bool NotifiedFailed;

        public bool CanExit { get; set; }

        /// <summary> If this is a Master action, is it being hold by the secondary action processor  </summary>
        public bool OnHoldBySecondary { get; set; }

        public bool InputValue { get; set; }

        [Tooltip("ID of the Item Action. This is connected with the inputs ")]
        public ItemActionID ActionID;

        public bool StopActionOnRelease { get; set; } = true;

        //[Tooltip("Name of the Action required to activate this. E.g. Firing arrow requires Aim to be active")]
        //public StringReference MasterActionName;

        [Tooltip("Extra conditions to activate this action using the Owner as the Conditions target")]
        public Conditions2 ConditionsOnOwner;

        [Tooltip("Extra conditions to activate this action using the Target as the Conditions target")]
        public Conditions2 ConditionsOnTarget;

        [SerializeReference]
        public ItemProcessor[] processors = new ItemProcessor[0]; //MWC: never null - the runtime loops and editor assume a non-null array

        //MWC — removed the unused 'SelectedActionTab' serialized int (written by nothing, read by nothing)

        public UnityAction<bool> OnInputActivated { get; set; }//Called when the Input is activated

        /// <summary>  Verify if this action requires another action to be active first  </summary>
        public bool IsSecondary => MasterAction != null;

        public ItemAction MasterAction { get; set; }

        public int masterIndex;

        [Tooltip("Trigger Proxy to use for this Action")]
        public TriggerProxy Trigger;

        public Reaction2 OnActionPressed = new();
        public Reaction2 OnActionReleased = new();

        public Reaction2 OnActionStart = new();
        public Reaction2 OnActionExit = new();

        public Reaction2 OnActionFailedOnOwner;
        public Reaction2 OnActionFailedOnTarget;

        public bool debug;

        //MWC — the per-Action 'EditorTab1' index is gone: the Action tabs are stored on the Super Item
        //(SuperItem.Editor_ActionTabs) so the selected tab no longer changes when you pick another Action.

        public virtual void Awake(SuperItem item, ItemActionSet set)
        {
            StopActionOnRelease = true; //By default, the action will stop when the input is released, but you can change this in the inspector

            for (int i = 0; i < processors.Length; i++)
            {
                if (processors[i] != null)
                {
                    processors[i].Awake(item, set, this);     //Awake the processors
                    processors[i].Index = i; //Set the index of the processor in the array so we can identify it later
                }
            }

            // Debug.Log($"{item.name}, Action [{Name}] masterIndex : [{item.name}, Action [{Name}] masterIndex {masterIndex}]");

            //MWC — guard the Master index. It goes stale when Actions are deleted from the Set (IndexOutOfRange),
            //and an Action must never be its own Master or Exit() would recurse until the stack blows up.
            MasterAction = null;

            if (masterIndex > 0) //0 means "no Master Action"
            {
                var master = masterIndex - 1;

                if (master >= set.actions.Count)
                    Debug.LogWarning($"[{item.name}] Action [{Name}] has an invalid Master index [{masterIndex}]. The Set [{set.Name}] only has {set.actions.Count} Actions. Master ignored.", item);
                else if (set.actions[master] == this)
                    Debug.LogWarning($"[{item.name}] Action [{Name}] cannot be its own Master Action. Master ignored.", item);
                else
                    MasterAction = set.actions[master];
            }

            CanExit = true;
            WaitingForConditions = false; //MWC — [Restart On Conditions] never survives a re-Awake (re-equipped/pooled Item)

            ActionTime = -rate; //Set the last start time to negative infinity so we can start the action immediately
        }

        private bool lastInputValue;

        internal void ActivateOnInput(bool inputValue, SuperItem item, ItemActionSet set)
        {

            if (lastInputValue != inputValue)
            {
                // if (JustActivate) return; //If we just activated the action, do not allow activating it again immediately to prevent double activation from the same input press

                if (activation == ItemActivationType.Toggle)
                {
                    if (inputValue) //Only act on the input press
                    {
                        InputValue = !InputValue; //Toggle the input value

                    }
                    else
                    {
                        lastInputValue = inputValue;
                        return; //Do nothing on false input if we are in toggle mode, because we only want to toggle the value on the input press, not on the release
                    }
                }
                else
                {
                    InputValue = inputValue; //Set the input value
                }

                //MWC — [Fast Weapons]: a yielded press is fully ignored (no OnActionPressed reaction, no start) so the
                //input falls through to the holstered candidate's fast equip. A Toggle turning itself OFF is never blocked.
                if (InputValue && YieldsToFastEquip(item))
                {
                    InputValue = false;          //MWC — un-latch (a Toggle would stay stuck ON otherwise)
                    lastInputValue = inputValue; //MWC — keep the edge detection coherent for the next press/release
                    return;
                }

                if (InputValue)
                {
                    OnActionPressed.React(item.UserGo); //Invoke the OnActionPressed event
                    StartAction(item, set); //Start the Action if the input is pressed
                }
                //Release the Action if the input is released and we don't have a duration,
                //because if we have a duration, the action will end when the duration is reached, so we don't need to check the input value on update
                else if (Duration <= 0) ReleasedAction(item, set);

            }
            lastInputValue = inputValue;
        }


        /// <summary>
        /// True only on the frame the Action started, so it cannot be re-entered while it is still starting
        /// (a Processor firing a Mode/State from inside _ActionStart). Same guard <see cref="Interrupt"/> uses.
        /// It is now derived from <see cref="StartFrame"/> instead of being a flag reset by a coroutine: Delay_Action
        /// does not run at all when the Item is disabled, and Unity kills the coroutine when the Item's GameObject is
        /// deactivated within the frame (holster/drop) — both left the old flag latched true forever, which
        /// permanently killed <see cref="StartAction"/>.
        /// </summary>
        public bool JustActivate => StartFrame == Time.frameCount;

        /// <summary> Frame this Action started playing. Used to ignore interruptions caused by the Action itself </summary>
        public int StartFrame { get; private set; } = -1;

        public virtual void StartAction(SuperItem item, ItemActionSet set)
        {
            if (JustActivate) return;

            //MWC: Dual Wielding - a follower copy (on a secondary equip point) never runs a Primary Hand Only action, whatever activated it
            if (primaryHandOnly.Value && !item.IsOnPrimaryPoint) return;

            //MWC — [Fast Weapons]: yield to a holstered [Fast Equip] candidate (covers Restart-On-Conditions and direct API calls)
            if (YieldsToFastEquip(item)) return;

            if (IsPlaying && !interrupSelf) return; //If the action is already playing and we cannot interrupt ourselves, do nothing

            if (CheckRate()) //Check the rate first
            {
                if (item.User != null && debug) item.User.Debugging($"<color=yellow><B>{Name}</B></color> cannot be activated yet. Time remaining: {(ActionTime + rate - Time.time):F2} seconds.");
                return; //If we have a rate, check if we can update based on the last start time
            }
            if (!PreRequisites(item, set))
            {
                //MWC — [Restart On Conditions] for a Secondary Action: the input was pressed while its Master was not
                //playing (pressed too early, or the Master is down). Arm it so it starts by itself as soon as the
                //Master is up again, instead of needing a second press. Only ever SETS the flag, so it cannot disarm
                //an Action that was already waiting.
                if (IsSecondary && restartOnConditions.Value && InputValue) WaitingForConditions = true;
                return;
            }
            if (!ManagerAllowsActivation(item)) return; //Manager-wide gate, activation only
            if (!CheckConditionals(item, set, true)) return;

            //MWC — stamped AFTER the vetoes, not before. An activation that never happened (Item not equipped, Action
            //blocked, Conditions failed) used to burn the CoolDown, so a failed press pushed the next VALID one a full
            //Rate away. Both stamps belong to a real activation: ActionTime feeds the CoolDown and the Duration,
            //LastUpdateTime makes the first repeated Execute wait a full updateRate.
            ActionTime = Time.time;
            LastUpdateTime = Time.time;
            Released = false;       //MWC — a new activation gets a new release pass
            NotifiedFailed = false; //MWC — and its own [Action Failed] notification


            //MWC — removed a dead re-check here: PreRequisites already rejects a Secondary Action whose Master is not
            //the playing Main Action, so this block could never be reached and its warning could never print.
            //The warning now lives in PreRequisites (gated by 'debug'), where it can actually fire.

            if (IsSecondary)
            {
                item.SlaveAction = this;
                MasterAction.OnHoldBySecondary = true;
                if (debug) Debug.Log($"<color=green> [Main Action :{MasterAction.Name}] </color>  ON HOLD by Secondary [{Name}]", item); //MWC — gated behind action debug (was un-gated build spam)
            }
            else
            {
                if (item.MainAction != null && item.MainAction != this) //if the Main Action of the item is not null and it's not this action, we need to end it before starting this one, because only one Main Action can be active at a time
                {
                    if (debug) Debug.Log($"<color=green> [Action Start: {Name}] </color> MAIN Action needs to END. Lower Priority  <color=green>[{item.MainAction.Name}]</color>", item); //MWC — gated behind action debug
                    item.MainAction.InputValue = false;
                    item.MainAction.Exit_Processor(item, set);
                    item.MainAction.Exit(item, set);
                }
                item.MainAction = this; //Set this action as the Main Action of the item
            }

            IsPlaying = true;
            StartFrame = Time.frameCount; //MWC — used by Interrupt() to ignore the events this Action itself triggers

            item.ClearTriggerHits(Trigger); //A new swing hits every target once again, even when the Trigger stays enabled

            item.RefreshHandsInUse(); //MWC — this Action may claim the off hand while it plays (Aim, 2H swing…)


            if (item.User != null && debug)
                item.User.Debugging($"[{item.name}] Action Start: <color=white>{name.Value}</Color>", item, "yellow");

            OnActionStart.React(item.UserGo); //Invoke the OnActionStart event

            //MWC — JustActivate is now derived from StartFrame (set above), so the coroutine that used to reset it is gone
            for (int i = 0; i < processors.Length; i++)
                processors[i]?._ActionStart(item, set, this); //Start the processors
        }

        /// <summary>
        /// Check the rate (CoolDown) between activations.  
        /// <returns></returns>
        public virtual bool CheckRate() => rate > 0 && ActionTime + rate > Time.time;

        /// <summary>True when a press right now would start this Action: every StartAction veto except the Fast Weapons yield, evaluated with no side effects (no Failed reactions, no CoolDown stamp)</summary>
        internal bool CanActivate(SuperItem item, ItemActionSet set)
        {
            if (primaryHandOnly.Value && !item.IsOnPrimaryPoint) return false;
            if (IsPlaying && !interrupSelf) return false;
            if (CheckRate()) return false;
            if (!PreRequisites(item, set)) return false;
            if (!ManagerAllowsActivation(item, false)) return false; //silent: this is a probe, not a press
            return CheckConditionals(item, set, false, false);
        }

        /// <summary> [Fast Weapons]: true when this Action must yield its input to a holstered [Fast Equip] candidate </summary>
        internal bool YieldsToFastEquip(SuperItem item)
        {
            if (item == null || item.User == null || !item.User.UseFastWeapons) return false;

            //MWC — [Valid On Fast Weapons] off: yield to ANY candidate, whatever the values (Unarmed punches)
            if (!validOnFastWeapons.Value) return item.User.HasFastEquipCandidate(ActionID, item);

            //MWC — [Fast Equip] is ONE ranked scale across equipped and holstered Actions: the Item in hand keeps its
            //input only while its own value is at least the best holstered candidate's. A Dagger attack at [0] loses
            //the press to a holstered Sword attack at [2]; a tie goes to the Item already in hand.
            //Mirrored by SuperItemManager.EquippedActionHandles so arbitration stays listener-order-independent.
            return item.User.OutrankedByFastEquip(ActionID, fastEquip.Value, item);
        }

        public virtual void Exit(SuperItem item, ItemActionSet set) => Exit_Internal(item, set, false);

        /// <summary>
        /// Shared teardown. <paramref name="force"/> skips the CanExit hold and the PreRequisites veto, which an
        /// interruption must never be subject to: a Processor that pushed this ActionID into <c>item.BlockedActions</c>,
        /// a deactivated Set or a higher priority Main Action all used to make <see cref="Exit"/> bail out, leaving
        /// IsPlaying true and item.MainAction pointing at an Action that could never be ended again.
        /// </summary>
        private void Exit_Internal(SuperItem item, ItemActionSet set, bool force)
        {
            if (!force && !CanExit) return; //If the action cannot exit, do nothing.
            if (!IsPlaying) return; //Do not exit if the action is not
            if (!force && !PreRequisites(item, set)) return;

            //MWC — clear the flag BEFORE the Master/Slave teardown below. Exiting a Master that holds a Slave re-enters
            //this same method (Slave.Exit -> Master.Exit), and with the flag still set the Master ran its teardown twice,
            //firing OnActionExit twice. The old code only cleared it at the very end.
            IsPlaying = false;

            item.RefreshHandsInUse(); //MWC — the off hand this Action was holding is free again

            if (item.User != null && debug)
                item.User.Debugging($"[Item: {item.name}] Action Exit: <color=white>{Name}</Color>", "yellow");

            //Before Ending verify if there's a Slave action playing so you can stop it
            if (item.SlaveAction != null && item.SlaveAction != this && item.SlaveAction.IsPlaying)
            {
                if (item.User != null && debug)
                    item.User.Debugging($"[Item: {item.name}] Action Exit: -SLAVE ACTION needs to end END [[ {item.SlaveAction.Name}]]]", item, "yellow"); //MWC — gated behind action debug


                var slave = item.SlaveAction; //MWC — cache it, the nested Exit clears the slot

                //MWC — [Restart On Conditions] reaches the Secondary Action too. This Master is armed to come back
                //(its Conditions cut it short while its input is still held), so a Slave that is ALSO armed and whose
                //own input is STILL pressed has to come back with it. Clearing its InputValue below is what used to
                //leave a [Charge] on a Secondary Action dead until the player released and pressed the button again.
                var restartSlave = WaitingForConditions && slave.restartOnConditions.Value && slave.InputValue;

                if (!restartSlave) slave.InputValue = false;

                slave.Exit_Processor(item, set);

                //FORCED, whatever ended this Master. IsPlaying was cleared above, and the Slave's PreRequisites require
                //its Master to be PLAYING, so a non-forced exit bailed out right there: releasing the Block while the
                //Parry played left the Parry [Playing] forever, and its own Mode End could never reach it either
                //(ItemAction.ModeEnd runs the same PreRequisites). A Slave cannot outlive its Master.
                slave.Exit_Internal(item, set, true);
                if (item.SlaveAction == slave) item.SlaveAction = null; //MWC — only clear the slot if it is still ours

                //MWC — armed AFTER the teardown (nothing in it clears the flag). From here the Slave polls its own
                //PreRequisites every frame and starts again as soon as this Master is playing once more, restarting
                //its Processors from a clean state — see TryRestartOnConditions.
                if (restartSlave)
                {
                    slave.WaitingForConditions = true;

                    if (item.User != null && slave.debug)
                        item.User.Debugging($"[{item.name}] Secondary <color=white>{slave.Name}</Color> armed to restart with its Master <<{Name}>>", item, "yellow");
                }
            }

            if (IsSecondary) //If this is a secondary action, set the SlaveAction to null and release the Master Action from being on hold by the secondary
            {
                if (item.SlaveAction == this) item.SlaveAction = null; //MWC — do not clear another Action's slot
                MasterAction.OnHoldBySecondary = false;

                if (item.User != null && debug)
                    item.User.Debugging($"[{item.name}] Action Exit <color=white>{Name}</Color> | MasterAction <<{MasterAction.Name}>> [Hold by Secondary = False]", item, "yellow");

                if (!MasterAction.InputValue) //If the Master Action input is not being pressed, exit the Master Action
                {

                    if (item.User != null && debug)
                        item.User.Debugging($"[{item.name}] Action Exit <color=white>{Name}</Color> | MasterAction <<{MasterAction.Name}>> [Input = False]", item, "yellow");

                    MasterAction.Exit_Processor(item, set);
                    MasterAction.Exit_Internal(item, set, force); //MWC — propagate the force flag
                }
            }
            else
            {
                if (item.MainAction == this) item.MainAction = null; //MWC — do not clear another Action's slot
            }

            OnActionExit.React(item.UserGo); //Invoke the OnActionExit event

            //MWC — notify the Processors that the Action FINISHED. This is the only hook that fires on EVERY end path
            //(release, duration, Mode end, takeover, interruption); _ActionExit/Exit_Processor only runs on forced ones.
            //It is placed AFTER OnActionExit so those Reactions still see the Action's runtime data (e.g. the Charge),
            //and it never cancels a pending delayed Execute — that stays exclusive to Exit_Processor.
            for (int i = 0; i < processors.Length; i++)
                processors[i]?._ActionEnd(item, set, this);
        }


        /// <summary>
        /// Force this Action to end RIGHT NOW, no matter what started it (used by the Set Interruption sources:
        /// the Character entered an Interrupt State/Mode/Stance). Same teardown the other force-exit paths use
        /// (StartAction when a new Action takes over), so every pending processor delay is dropped and the Action
        /// never completes: <see cref="Exit_Processor"/> cancels the delays, <see cref="Exit"/> releases the Master/Slave links.
        /// </summary>
        /// <param name="force">True = an EXPLICIT external cancellation (the public Action_Interrupt API, an authored Reaction): it also wins on the Action's own start frame</param>
        /// <returns> True if the Action was playing and got interrupted </returns>
        public virtual bool Interrupt(SuperItem item, ItemActionSet set, bool force = false)
        {
            if (!IsPlaying) return false; //Nothing to interrupt

            //MWC — Re-entrancy guard: an Action cannot be interrupted on the very frame it started. Its own Processors fire
            //Mode Start (and often a State change) from inside StartAction, and those events would otherwise kill it instantly
            //whenever the Set uses an [✕ Exclude] interruption list.
            //'force' opts out of it: that noise is the Action interrupting ITSELF, while an external force-stop is a
            //deliberate order from outside. Refusing it left the Action latched IsPlaying with its input already
            //cleared, and Update never repairs a held Action whose input is false, so it stayed stuck forever.
            if (!force && StartFrame == Time.frameCount) return false;

            if (item.User != null && debug)
                item.User.Debugging($"[{item.name}] Action Interrupted: <color=white>{name.Value}</Color>", "yellow");

            InputValue = false;
            lastInputValue = false;     //MWC — the input is no longer ours; a held button must be re-pressed to restart
            CanExit = true;             //An interruption always wins, ignore any hold
            WaitingForConditions = false; //MWC — an interruption is not a Conditions failure: it must never resume by itself
            Exit_Processor(item, set);  //Cancels every pending delayed Execute and notifies the processors
            Exit_Internal(item, set, true); //MWC — forced: PreRequisites must not be able to veto an interruption

            return true;
        }

        public virtual void ActionOnAnimation(SuperItem item, ItemActionSet set)
        {
            if (!IsPlaying) return; //Do not exit if the action is not
            if (!PreRequisites(item, set)) return;

            for (int i = 0; i < processors.Length; i++)
                processors[i]?._ActionOnAnimation(item, set, this); //Start the processors on Animation
        }


        public virtual void Update(SuperItem item, ItemActionSet set, float delta)
        {
            //MWC — cheapest and most frequent early-out (this runs for every Action of the Active Set, every frame).
            //A NOT playing Action still gets one chance to restart itself: see [Restart On Conditions]
            if (!IsPlaying)
            {
                TryRestartOnConditions(item, set);
                return;
            }

            //MWC — the Rate (CoolDown) check that used to sit here is GONE. It gated the whole Update, so a CoolDown
            //also silenced every [On Action Updated] Processor. The repeat throttle now lives at the bottom of this
            //method and uses its own field ('updateRate') and its own stamp ('LastUpdateTime').

            if (Duration <= 0 && InputValue == false) return; //MWC — was 'Duration == 0'; a negative duration slipped past this and then never ended at the '> 0' check below

            //MWC — evaluated separately (they used to be one short-circuited 'if') so [Restart On Conditions] can tell
            //WHY the Action is ending: only a Conditions failure arms the restart.
            var preRequisites = PreRequisites(item, set);               //PreRequisites can veto an Action mid-play if the Item is disabled, the Set is deactivated, the Action is blocked, or a higher-priority Main Action takes over
            //MWC — sendFailed:FALSE. The [On Action Failed] Reactions are ACTIVATION feedback ("the press was
            //rejected": the empty click on a Pistol with no round left). They used to fire from here too, and that is
            //wrong twice over:
            // • Self-consumption: an Action whose own Processors spend the resource its Conditions guard fails this
            //   re-check on its LAST successful use. Firing the 8th and last round emptied the Chamber Stat, so the
            //   very next frame [Chamber NOT Empty] failed and the empty click played on the shot that DID fire.
            //   The click belongs to the 9th press, which never starts.
            // • Spam: this branch runs every frame the Conditions keep failing, and an Action that does not stop on
            //   release stays IsPlaying while the input is held. NotifiedFailed only guarded the PROCESSOR
            //   notification below, so the Reaction itself re-fired once per frame.
            //A playing Action that loses its Conditions is still released and still ends here — the Processors hear
            //about it through NotifyFailed. Only the user facing Reactions are now activation-only.
            var conditions = preRequisites && CheckConditionals(item, set, false);
            var durationEnded = Duration > 0 && ActionTime + Duration < Time.time; //If we have a duration, check if we need to end the action

            if (!preRequisites || !conditions || durationEnded)
            {
                //MWC — arm the restart ONLY when the Conditions cut a still-pressed input short. A lost PreRequisite
                //or an elapsed Duration is a real end, and a released input has nothing to resume.
                WaitingForConditions = restartOnConditions.Value && InputValue && preRequisites && !conditions && !durationEnded;

                ReleasedAction(item, set);

                //MWC — tell the Processors the Action did not end normally, it FAILED. Placed AFTER the release pass
                //on purpose: the [On Action Released] Processors must still see the Action's real runtime data
                //(a Projectile scaling its force by the Charge), and this is the teardown that follows them.
                NotifyFailed(item, set);

                //MWC — the failure drags the LINKED Action down with it: Exit_Internal ends the Slave, and a Secondary
                //ends its Master. Those teardowns run neither a release pass nor this branch (the linked Action is
                //already !IsPlaying by the time its own Update runs), so it would never hear about the failure —
                //which is exactly how a [Charge] on a Secondary Action stayed latched at full after the Conditions failed.
                NotifyFailed(item, set, item.SlaveAction);
                if (IsSecondary) NotifyFailed(item, set, MasterAction);

                //MWC — [Restart On Conditions]: the Action must REALLY end, so it can start again from a clean state.
                //An Action that does NOT stop on release (a PlayOnce/ActiveByTime/Charged Mode keeps it alive until its
                //Mode ends — and that Mode's own end path bails out precisely because the input is still held) would
                //otherwise stay IsPlaying with its release pass already spent: nothing would ever run its
                //[On Action Start] Processors again. Forced, like an interruption: the Conditions outrank a CanExit
                //hold. InputValue is deliberately left alone — it is what keeps the Action armed for the restart.
                if (WaitingForConditions && IsPlaying)
                {
                    CanExit = true;
                    Exit_Processor(item, set);      //Cancels every pending delayed Execute and notifies the Processors
                    Exit_Internal(item, set, true); //Forced: PreRequisites/CanExit must not veto it
                }

                return;
            }

            WaitingForConditions = false; //MWC — playing with valid Conditions: nothing left to wait for

            //MWC — repeat throttle for the [On Action Updated] Processors (automatic fire). Placed AFTER the
            //PreRequisites/Conditions/Duration block on purpose: a playing Action must still be able to end on time
            //while it is throttled. It never touches ActionTime, so the CoolDown and the Duration keep measuring
            //from the activation.
            if (updateRate > 0)
            {
                if (LastUpdateTime + updateRate > Time.time) return;

                delta = Time.time - LastUpdateTime; //Real elapsed time since the last pass, not a single frame
                LastUpdateTime = Time.time;
            }

            for (int i = 0; i < processors.Length; i++)
                processors[i]?._ActionUpdated(item, set, this, delta); //Update the processors
        }

        /// <summary>
        /// Tell this Action's Processors that the activation FAILED (a Condition or PreRequisite stopped being
        /// valid). Fires ONCE per activation: the failure branch in <see cref="Update"/> runs every frame the
        /// Conditions keep failing, and an Action that does not stop on release stays there while the input is held.
        /// </summary>
        private void NotifyFailed(SuperItem item, ItemActionSet set)
        {
            if (NotifiedFailed) return;
            NotifiedFailed = true;

            for (int i = 0; i < processors.Length; i++)
                processors[i]?._ActionFailed(item, set, this);
        }

        /// <summary> Same, for an Action that is going down BECAUSE this one failed (its Slave, or its Master) </summary>
        private void NotifyFailed(SuperItem item, ItemActionSet set, ItemAction linked)
        {
            if (linked == null || linked == this || !linked.IsPlaying) return;

            linked.NotifyFailed(item, set);
        }

        /// <summary> [Restart On Conditions]: start the Action again once its Conditions pass, while the input is still pressed </summary>
        private void TryRestartOnConditions(SuperItem item, ItemActionSet set)
        {
            if (!WaitingForConditions) return;

            //The input was released (or the option was turned off) while waiting: the Action is no longer armed
            if (!InputValue || !restartOnConditions.Value)
            {
                WaitingForConditions = false;
                return;
            }

            //SendFailed:false — this is not an activation attempt, it is a poll that runs every frame while the input
            //is held, so it would spam the [Action Failed] Reactions. (The mid-play re-check in Update no longer
            //sends them either: they are activation-only now. See the note there.)
            if (!PreRequisites(item, set) || !ManagerAllowsActivation(item) || !CheckConditionals(item, set, false)) return; //a restart is an activation: the Manager gate applies

            StartAction(item, set);

            //Disarm only when the Action REALLY started: a Rate (CoolDown) or any other veto inside StartAction can
            //push it back, and while the input is held the next Update must try again.
            if (!IsPlaying) return;

            WaitingForConditions = false;

            if (item.User != null && debug)
                item.User.Debugging($"[{item.name}] Action Restarted (Conditions valid again): <color=white>{name.Value}</Color>", item, "yellow");
        }

        public virtual void ReleasedAction(SuperItem item, ItemActionSet set)
        {
            if (!IsPlaying) return; //Do not release if the action is not playing

            //MWC — one release pass per activation. IsPlaying alone was not enough: an Action that does not stop on
            //release stays playing until its Mode ends, so spamming the input inside that window re-ran every
            //[On Action Released] Processor (Play Mode re-forcing the Mode, Projectile firing a second arrow) on an
            //activation that had already been released. The extra press was even rejected by the CoolDown: it never
            //started anything, it only produced this second release.
            if (Released) return;
            Released = true;

            //Release the processors
            for (int i = 0; i < processors.Length; i++)
                processors[i]?._ActionReleased(item, set, this);


            OnActionReleased.React(item.UserGo); //Invoke the OnActionReleased event

            if (StopActionOnRelease)
                Exit(item, set); //End the action if the input is released and the action is set to stop on release
        }


        /// <summary>
        /// Verify the prerequisites for the action to be activated or updated (Enabled,Active,Equipped,Block any action?.
        /// This method is called at the beginning of the StartAction, Update, and other methods to ensure that the action can be performed before executing the rest of the code.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="set"></param>
        /// <returns></returns>
        private bool PreRequisites(SuperItem item, ItemActionSet set)
        {
            //If the Item is not enable, do nothing
            //MWC — [isActiveAndEnabled], was [enabled]: that flag stays TRUE when only the Item GAMEOBJECT is
            //deactivated, which the package exposes through [SI Item Toggle/GameObject]. An inactive GameObject runs
            //no Update, so an Action started from one could set IsPlaying and never end. SuperItem.OnDisable now drops
            //the input subscription that reached here in the first place; this is the second line of defence, and it
            //also ends an Action that is already mid-play when the GameObject goes down.
            if (!item.isActiveAndEnabled)
            {
                // if (debug) Debug.Log($"[{Name} Failed] -> Item not enabled or its GameObject is inactive");
                return false;
            }
            //If the action or set is not active, do nothing
            if (!Active || !set.Active)
            {
                // if (debug) Debug.Log($"[{Name} Failed] -> Action or Set not Active. Action Active: {Active} - Set Active: {set.Active}");
                return false;
            }
            if (!item.IsEquipped)
            {
                // if (debug) Debug.Log($"[{Name} Failed] -> Not Equipped");
                return false;
            }

            //MWC — a DISABLED Manager must be inert. Its public API already refuses every press, but the Item keeps the
            //input listeners SuperItem.Equip connected (only Unequip disconnects them), so a configured button still
            //reached this Action and started it on a character whose Manager was switched off.
            //Checked here rather than in ActivateOnInput so it also ends an Action that is mid-play when the Manager goes down.
            if (item.User == null || !item.User.Active)
            {
                // if (debug) Debug.Log($"[{Name} Failed] -> The Item's Manager is disabled");
                return false;
            }

            //If the action is blocked by the item, do nothing
            if (item.BlockedActions.IsBlocked(ActionID))
            {
                // if (debug) Debug.Log($"[{Name} Failed] -> Blocked Action");
                return false;
            }

            //If the Main Action has a higher priority than this action, do nothing
            if (item.MainAction != null && item.MainAction.IsPlaying && item.MainAction.Priority > Priority)
            {
                //if (debug) Debug.Log($"[{Name} Failed] -> Bigger Priority");
                return false;
            }

            //If this is a secondary action, check if the Master Action is active and playing, if not, do nothing
            if (IsSecondary && (item.MainAction != MasterAction || !item.MainAction.IsPlaying))
            {
                //MWC — moved here from StartAction, where it sat behind a condition this very check made unreachable
                //if (debug) Debug.Log($"<color=green>[Action: <B>[{name.Value}]</b> requires <b>[{MasterAction.Name}]</b> to be active first</color>", item);
                return false;
            }
            return true;
        }


        /// <summary> Manager-wide gates. ACTIVATION only: a playing Action is never cut by them, so an Aim held through a Dodge keeps aiming </summary>
        private bool ManagerAllowsActivation(SuperItem item, bool log = true)
        {
            if (item.User == null) return true;

            if (!item.User.CheckActionConditions())
            {
                //A global veto (e.g. the character is dodging) is not a failed press of THIS Action, so the
                //[On Action Failed] Reactions (empty click, etc.) do not fire for it.
                if (debug && log)
                    item.User.Debugging($"[{item.name}] -> Failed <color=white>[Manager]</Color> Action Conditions: <color=white>[{Name}]</Color>", item, "red");

                return false;
            }

            //MWC — [Cross Item Priority]: another Item answers this same Action ID with a higher Priority Action
            //(Shield [Parry] over Sword [Attack]). Losing the press is not a failed press either, so no Reactions fire
            if (item.User.TryGet_HigherPriorityAction(ActionID, item, Priority, out var winnerItem, out var winner))
            {
                if (debug && log)
                    item.User.Debugging($"[{item.name}] -> <color=white>[{Name}]</Color> yields to <color=white>[{winnerItem.name}] → [{winner.Name}]</Color> Priority: [{winner.Priority} > {Priority}]", item, "red");

                return false;
            }

            return true;
        }

        private bool CheckConditionals(SuperItem item, ItemActionSet set, bool sendFailed, bool log = true)
        {
            //MWC — the three Debug.Log calls in this method were un-gated: they shipped in builds and fired from
            //Update() and from every trigger crossing. They are now behind the Action's 'debug' toggle.

            if (ConditionsOnOwner.Valid && item.User && !ConditionsOnOwner.Evaluate(item.User))
            {
                if (sendFailed) OnActionFailedOnOwner.React(item.UserGo); //React to the failed conditions on owner

                if (item.User != null && debug && log)
                    item.User.Debugging($"[{item.name}] -> Failed <color=white>[Owner]</Color> Conditions: <color=white>[{Name}]</Color>", item, "red");

                return false;  //If we have Owner conditions skip the action activation when the set conditions are not met
            }

            //MWC — was '(Valid && Target && !Evaluate) || (Valid && Target == null)'. Identical result, one Valid read
            if (ConditionsOnTarget.Valid && (item.Target == null || !ConditionsOnTarget.Evaluate(item.Target)))
            {
                if (item.User != null && debug && log)
                    item.User.Debugging($"[{item.name}] -> Failed <color=white>[Target:{item.Target?.name ?? "None"}]</Color> Conditions: <color=white>[{Name}]</Color>", item, "red");

                if (sendFailed) OnActionFailedOnTarget.React(item.Target); //React to the failed conditions on Target
                return false;   //if we have Target conditions skip the action activation when the set conditions are not met
            }

            if (set.SetConditions.Valid && item.User && !set.SetConditions.Evaluate(item.User))
            {
                if (log) item.User.Debugging($"[{item.name}] -> Failed Set Conditions: <color=white>[{set.Name}]</Color>", item, "red");

                return false;   //if we have Set conditions skip the action activation when the set conditions are not met
            }

            return true;
        }

        /// <summary>  Call this when the Item is equipped all processors  </summary>
        internal virtual void ItemEquipped(SuperItem item, ItemActionSet set)
        {
            for (int i = 0; i < processors.Length; i++)
                if (processors[i] != null && processors[i].Active) processors[i].ItemEquipped(item, set, this);
        }


        /// <summary>  Call this when the Item is unequipped all processors  </summary>
        internal virtual void ItemUnequipped(SuperItem item, ItemActionSet set)
        {
            for (int i = 0; i < processors.Length; i++)
                processors[i]?._ItemUnequipped(item, set, this); //MWC: wrapper cancels any pending delayed Execute (Active check moved inside)
        }

        internal void Set_Enter(SuperItem item, ItemActionSet set)
        {
            for (int i = 0; i < processors.Length; i++)
                if (processors[i] != null && processors[i].Active) processors[i].SetEnter(item, set, this);
        }

        /// <summary>  Call this when the Item is Set has exit</summary>
        internal virtual void Set_Exit(SuperItem item, ItemActionSet set)
        {
            InputValue = false;
            lastInputValue = false; //MWC — otherwise a button still held while the Set is swapped never produces a new press

            for (int i = 0; i < processors.Length; i++)
                processors[i]?._SetExit(item, set, this); //MWC: wrapper cancels any pending delayed Execute (Active check moved inside)

            IsPlaying = false;
            WaitingForConditions = false; //MWC — a stale arm must never survive a Set swap

            //MWC — release the Item slots. They used to keep pointing at this Action after the Set was swapped, so the
            //next Set's first StartAction force-exited a stale Action and handed its Processors the WRONG Set.
            if (item.MainAction == this) item.MainAction = null;
            if (item.SlaveAction == this) item.SlaveAction = null;

            CanExit = true;            //MWC — a hold left over from the old Set must not block the next activation
            OnHoldBySecondary = false;
        }


        /// <summary>  Call this when the Item is unequipped all processors  </summary>
        internal void EnterTriggerInteraction(GameObject obj_touched, Collider collider, TriggerProxy proxy, SuperItem item, ItemActionSet set, SuperItemManager user)
        {
            if (!PreRequisites(item, set)) return;
            //MWC — sendFailed:false. A trigger crossing is not an activation attempt: firing OnActionFailed on every
            //collider that touches the Item (for Actions that are not even playing) was pure noise.
            if (!CheckConditionals(item, set, false)) return;

            for (int i = 0; i < processors.Length; i++)
                if (processors[i] != null && processors[i].Active) processors[i]._EnterTrigger(obj_touched, collider, proxy, item, set, this, user);
        }

        internal void ExitTriggerInteraction(GameObject obj_touched, Collider collider, TriggerProxy proxy, SuperItem item, ItemActionSet set, SuperItemManager user)
        {
            if (!PreRequisites(item, set)) return;
            if (!CheckConditionals(item, set, false)) return; //MWC — sendFailed:false, see EnterTriggerInteraction

            for (int i = 0; i < processors.Length; i++)
                if (processors[i] != null && processors[i].Active) processors[i]._ExitTrigger(obj_touched, collider, proxy, item, set, this, user);
        }


        //-----------------LISTEN TO USER -------------------------
        internal void ModeStart(SuperItem item, ItemActionSet set, int mode, int ability)
        {
            if (!PreRequisites(item, set)) return;

            for (int i = 0; i < processors.Length; i++)
                if (processors[i] != null && processors[i].Active) processors[i].ModeStart(item, set, this, mode, ability);
        }

        internal void ModeEnd(SuperItem item, ItemActionSet set, int mode, int ability)
        {
            if (!PreRequisites(item, set)) return;

            for (int i = 0; i < processors.Length; i++)
                if (processors[i] != null && processors[i].Active) processors[i].ModeEnd(item, set, this, mode, ability);
        }

        internal void StanceChanged(SuperItem item, ItemActionSet set, int stance)
        {
            if (!PreRequisites(item, set)) return;

            for (int i = 0; i < processors.Length; i++)
                if (processors[i] != null && processors[i].Active) processors[i].StanceChanged(item, set, this, stance);
        }

        internal void StateChanged(SuperItem item, ItemActionSet set, int state)
        {
            if (!PreRequisites(item, set)) return;

            for (int i = 0; i < processors.Length; i++)
                if (processors[i] != null && processors[i].Active) processors[i].StateChanged(item, set, this, state);
        }

        internal void OnEnable(SuperItem item, ItemActionSet set)
        {
            for (int i = 0; i < processors.Length; i++)
                processors[i]?.OnEnable(item, set, this);
        }

        internal void Exit_Processor(SuperItem item, ItemActionSet set)
        {
            for (int i = 0; i < processors.Length; i++)
                processors[i]?._ActionExit(item, set, this); //MWC: wrapper cancels any pending delayed Execute
        }

        internal void OnDisable(SuperItem item, ItemActionSet set)
        {
            for (int i = 0; i < processors.Length; i++)
                processors[i]?._OnDisable(item, set, this); //MWC: wrapper cancels any pending delayed Execute
        }


        /// <summary>  Get the first processor of the specified type. This is used to access the processors from the Action without having to know the index of the processor in the array.  </summary>
        /// <typeparam name="T"> Type of the processor to get</typeparam>
        /// <returns> The first processor of the specified type, or null if no processor of that type is found</returns>
        internal T GetProcessor<T>() where T : ItemProcessor
        {
            for (int i = 0; i < processors.Length; i++)
            {
                if (processors[i] is T pt) return pt;
            }
            return default;
        }
    }
}