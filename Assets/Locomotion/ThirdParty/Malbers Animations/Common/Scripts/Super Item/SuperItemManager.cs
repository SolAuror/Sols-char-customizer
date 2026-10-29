// Last Change: 9/30/2026 11:41am
using MalbersAnimations.Conditions;
using MalbersAnimations.Controller;
using MalbersAnimations.Events; //BoolEvent (OnLocked)
using MalbersAnimations.IK;
using MalbersAnimations.Reactions; //MWC — Reaction2 (default Equip/Holster reactions)
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using System.Collections; //IEnumerator (pending coroutine handles)
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events; //MWC — [Fast Weapons] UnityAction listeners

namespace MalbersAnimations.Items
{
    //[System.Serializable] public class SuperItemManagerEvent : UnityEvent<SuperItemManager> { }

    /// <summary>UnityEvent that sends an Item Action ID</summary>
    [System.Serializable] public class ItemActionIDEvent : UnityEvent<ItemActionID> { }

    /// <summary>Reactions played when the input of an Action Input row is pressed and released</summary>
    [System.Serializable]
    public class SIActionInputReaction
    {
        /// <summary>Action ID of the Action Input row these reactions belong to</summary>
        public ItemActionID ID;
        /// <summary>Reactions when the Action input is pressed. Dynamic Target [Super Item Manager]</summary>
        public Reaction2 OnPressed;
        /// <summary>Reactions when the Action input is released. Dynamic Target [Super Item Manager]</summary>
        public Reaction2 OnReleased;
    }

    /// <summary>What happens to the equipped Items when the Super Item Manager is Locked</summary>
    public enum LockItemsMode
    {
        /// <summary>Stored instantly in their Holsters (no Store animation). An Item with no Holster is hidden in place instead</summary>
        Holster,
        /// <summary>Deactivated in place, still on their Equip Point (no Store animation). Their Holsters are not touched</summary>
        Hide,
    }

    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/super-item/super-item-system/super-item-manager")]
    [AddComponentMenu("Malbers/Interaction/Super Item Manager"), DefaultExecutionOrder(1500)]
    /// <summary> Manages all Super Item related  / </summary>
    public class SuperItemManager : MonoBehaviour, IMDamagerSet
    {
        #region References
        [Tooltip("Reference for the Aim Component")]
        [RequiredField] public MAnimal Animal;
        [Tooltip("Reference for the Aim Component")]
        [RequiredField] public Aim Aim;
        [Tooltip("Reference for the Pouch Manager")]
        [RequiredField] public PouchManager Pouch;
        [Tooltip("Reference for the Combo Manager")]
        [RequiredField] public ComboManager ComboManager;

        [Tooltip("Reference for the IK Manager")]
        public IKManager IKManager;


        #region Items to hide when a state/action/stance is bloking it
        [Tooltip("List of States that require NO Items in the hands.\nEntering one holsters every equipped item instantly (no Unequip animation) and draws them back when the State is left")]
        public IDListCheck<StateID> NoItemsInState = new();
        [Tooltip("List of Modes that require NO Items in the hands.\nStarting one holsters every equipped item instantly (no Unequip animation) and draws them back when the Mode ends")]
        public IDListCheck<ModeID> NoItemsInMode;
        [Tooltip("List of Stances that require NO Items in the hands.\nEntering one holsters every equipped item instantly (no Unequip animation) and draws them back when the Stance is left")]
        public IDListCheck<StanceID> NoItemsInStance;

        //MWC — Items are ALWAYS put away instantly (no Store Weapon animation) when a No Items [State|Stance|Mode]
        //starts; only the way they come BACK is configurable.
        [Tooltip("How the Items held by a No Items [State|Stance|Mode] are drawn back when the block ends.\n\n" +
            "• OFF (default): the Equip Mode animation is played (e.g. when the Open Chest animation ends, the weapon is drawn normally).\n" +
            "• ON: the Items reappear in the hands instantly, with no Equip animation.\n\n" +
            "Putting them away is always instant, with no Unequip (Store Weapon) animation.")]
        public BoolReference FastEquipBack = new(false);

        //MWC — [Fast Weapons] (Assassin's Creed style): the Manager listens to the Action inputs of HOLSTERED items.
        //Pressing one draws the item instantly (no Equip Mode) and starts its [Fast Equip] Action, unless an Item that is mid-Action owns that press (see PlayingItemOwnsPress).
        [Tooltip("[Fast Weapons] (Assassin's Creed style): Pressing the input of a HOLSTERED item's [Fast Equip] Action " +
            "draws the item instantly (skipping the Equip animation) and starts that Action.\n" +
            "An equipped Item that is mid-Action keeps its own presses: the physical button of the Action it is playing, and any Action ID it also has. Other presses can still fast draw.\n" +
            "Equipped Actions with [Valid On Fast Weapons] disabled yield their input to it.")]
        public BoolReference useFastWeapons = new(false);

        //Global [Auto Equip]: the per-item flag was the only say on where a picked Item lands, so making a whole
        //character (or an AI) always draw, or always store, meant editing every Item prefab
        [Tooltip("Global [Auto Equip]: ON → every Item picked from the world (Interactor, AI Pick Task, Pick reactions) goes straight to the hands, " +
            "whatever the Item's own [Auto Equip] says.\nOFF (default) → the Item's own [Auto Equip] decides (hands or Holster).\n\n" +
            "Picks only: the starting Items authored on the Equip Points and Holsters are not affected.")]
        public BoolReference autoEquip = new(false);

        /// <summary>Global [Auto Equip] for picked Items: ON = every picked Item goes to the hands, OFF = the Item's own [Auto Equip] decides</summary>
        public bool AutoEquip { get => autoEquip.Value; set => autoEquip.Value = value; }

        /// <summary>True when a pick of this Item goes to the hands: the global <see cref="AutoEquip"/> when it is ON, the Item's own [Auto Equip] otherwise</summary>
        public bool ResolveAutoEquip(SuperItem item) => AutoEquip || (item != null && item.AutoEquip);

        //MWC — [Cross Item Priority]: Action Priority only ever arbitrated INSIDE one Item (item.MainAction.Priority).
        //Two Items answering the same Action ID (Shield [Parry] and Sword [Attack] both on the same input row) each
        //started on their own, so the lower priority one played too. This makes that comparison reach across Items.
        [Tooltip("Compare Action [Priority] ACROSS the equipped Items.\n\n" +
            "When two Items answer the SAME Action ID, only the highest Priority one starts: a Shield [Parry] with " +
            "Priority 1 wins over a Sword [Attack] with Priority 0.\n" +
            "Equal Priorities both start, so dual wielding keeps swinging both hands on one press.\n" +
            "It also holds while the winner is playing, so pressing again does not start the lower priority Action.\n\n" +
            "OFF: every Item starts its own Action, Priority is compared only inside each Item.")]
        public BoolReference crossItemPriority = new(true);

        /// <summary> [Cross Item Priority] runtime toggle: compare Action Priority across the equipped Items </summary>
        public bool CrossItemPriority { get => crossItemPriority.Value; set => crossItemPriority.Value = value; }

        /// <summary>True while a cross-Item priority probe is running, so asking "can the winner start?" cannot re-enter this check</summary>
        private bool probingPriority;

        /// <summary> [Fast Weapons] runtime toggle (Assassin's Creed style instant draw + attack) </summary>
        public bool UseFastWeapons { get => useFastWeapons.Value; set => useFastWeapons.Value = value; }

        //MWC — [Auto Store] moved here from the Equip Point: it is one behaviour for the whole character, not a
        //per-hand setting (it was practically undiscoverable buried in the Equip Points list)
        [Tooltip("Auto Holster: seconds an equipped Item has to sit doing NOTHING (no Action of its Active Set playing) " +
            "before it is stored back into its holster with the normal Store animation.\n" +
            "Zero or less disables the auto holster for every Item.")]
        public FloatReference AutoStore = new(3f);

        //Manager-wide gate for EVERY Item Action. It is checked before the Action's own [Owner]/[Target] Conditions
        //and before the Set Conditions, so it can veto any Action of any equipped Item from one place. ACTIVATION
        //only: a playing Action (Aim held through a Dodge) is never cut by it.
        //(e.g. [Mode NOT Playing: Dodge] → no shots, no swings while the character dodges).
        [Tooltip("Global Conditions every Item Action must pass to START.\n" +
            "Evaluated on this Manager (the character) before the Action's own Conditions and the Set Conditions.\n\n" +
            "E.g. [Mode is NOT Playing: Dodge] blocks every Action of every Item while the character dodges.\n" +
            "Activation only: an Action that is ALREADY playing (e.g. Aim held down) is never cut by it, so it keeps playing through the block.\n" +
            "Leave empty to skip the check.")]
        public Conditions2 ActionConditions;

        /// <summary> True when the Manager-wide <see cref="ActionConditions"/> pass (or are empty/inactive). Every Item Action checks this before it STARTS (never while playing) </summary>
        public bool CheckActionConditions() => !ActionConditions.Valid || ActionConditions.Evaluate(this);

        internal List<SuperItem> HoldItems = new();

        /// <summary>  If the value is greater than zero then store all the items equipped in the manager  </summary>
        public bool NoItemState => HoldByState;   //MWC — was never assigned; now reports the real State block
        public bool NoItemStance => HoldByStance; //MWC — was never assigned; now reports the real Stance block
        public int NoItemsEquippedMode { get; private set; }

        /// <summary>True while a State, a Stance or one or more Modes require NO Items in the hands.
        /// While it is true nothing can be drawn: every equip request is parked in <see cref="HoldItems"/> instead.</summary>
        public bool NoItemsBlocked => NoItemsEquippedMode > 0 || HoldByState || HoldByStance;

        /// <summary>True while nothing can be in the hands: a No Items [State|Stance|Mode] block, or the Manager is <see cref="Locked"/></summary>
        public bool HandsBlocked => NoItemsBlocked || locked;

        #endregion

        #region Lock (put every equipped Item away and freeze the Manager)
        //Lock: the character needs its hands for something else (carrying a box, climbing a rope...). Every equipped
        //Item is put away INSTANTLY (Holster or Hide, see Lock Mode) and remembered, and until Unlock the Manager
        //refuses every Item operation: Actions, Equip, Holster, Drop, Pick and Fast Weapons. Unlock draws them back
        //through the same Restore path the No Items [State|Stance|Mode] block uses (animated, or instant with Fast Equip Back).
        [Tooltip("What happens to the equipped Items when the Manager is [Locked] (e.g. while carrying a box):\n" +
            "• Holster (default): stored instantly in their Holsters, with no Store animation. An Item with no Holster is hidden in place instead.\n" +
            "• Hide: deactivated in place, still on their Equip Point. Their Holsters are not touched.\n\n" +
            "On Unlock they are drawn back: with the Equip animation, or instantly when [Fast Equip Back] is ON.\n" +
            "While Locked no Action, Equip, Holster, Drop, Pick or Fast Weapons draw is accepted.")]
        public LockItemsMode lockMode = LockItemsMode.Holster;

        [Tooltip("Invoked with TRUE right after the Manager is Locked (the Items are already away) and with FALSE right after it is Unlocked (before the Items are drawn back)")]
        public BoolEvent OnLocked = new();

        private bool locked;

        //Set while the Manager itself puts the Items away (Hold_Item, Initialize), so its own Item_Holster calls pass the lock gate
        private bool lockBypass;

        /// <summary>True while the Manager is Locked: every equipped Item was put away and no Action, Equip, Holster, Drop or Pick is accepted. Setting it calls <see cref="Lock(bool)"/></summary>
        public bool Locked { get => locked; set => Lock(value); }

        /// <summary>Locks (true) or Unlocks (false) the Manager, using the inspector [Lock Mode] to put the Items away</summary>
        public void Lock(bool value) { if (value) Lock(lockMode); else Unlock(); }

        /// <summary>Locks the Manager using the inspector [Lock Mode]</summary>
        public void Lock() => Lock(lockMode);

        /// <summary>Locks the Manager storing every equipped Item in its Holster (UnityEvent friendly)</summary>
        public void Lock_Holster() => Lock(LockItemsMode.Holster);

        /// <summary>Locks the Manager hiding every equipped Item in place (UnityEvent friendly)</summary>
        public void Lock_Hide() => Lock(LockItemsMode.Hide);

        /// <summary>Locks the Manager: every equipped Item is holstered or hidden right now (no animation) and remembered, and every Item operation is vetoed until <see cref="Unlock"/></summary>
        public virtual void Lock(LockItemsMode mode)
        {
            if (locked) return;

            locked = true; //First: anything a reaction fires while the Items go away (re-equip, drop...) is already vetoed

            Debugging($"[<color=yellow>Locked</color>] → Items [{mode}]", "orange");

            Temporal_Hold_Items(mode == LockItemsMode.Hide, "Lock");

            OnLocked.Invoke(true);
        }

        /// <summary>Unlocks the Manager and draws back every Item it put away (next frame; with the Equip animation, or instantly when [Fast Equip Back] is ON)</summary>
        public virtual void Unlock()
        {
            if (!locked) return;

            locked = false;

            Debugging("[<color=yellow>Unlocked</color>] → drawing the Items back", "orange");

            OnLocked.Invoke(false);

            Temporal_Release_Items(); //No-op while a No Items [State|Stance|Mode] block is still on: that block restores them when it ends
        }

        /// <summary>Lock gate for every public Item operation: true (and logged) when the Manager is Locked, so the caller must return</summary>
        private bool LockVeto(string operation, SuperItem item = null)
        {
            if (!locked || lockBypass) return false;

            Debugging($"[<color=yellow>{operation}</color>{(item != null ? $" {item.name}" : string.Empty)}] ignored → the Manager is <color=white>[Locked]</color>", "red");
            return true;
        }
        #endregion

        #region Events & Reactions
        //Manager-wide notifications: one place to listen to every Item of the character, instead of wiring the same
        //reaction on every Equip Point, Holster or Item. Fired after the per Equip Point / Holster ones.

        [Tooltip("Invoked when an Item is equipped on any Equip Point")]
        public SuperItemEvent OnItemEquipped = new();
        [Tooltip("Invoked when an Item is unequipped from any Equip Point")]
        public SuperItemEvent OnItemUnequipped = new();
        [Tooltip("Invoked when an Item is stored in any Holster")]
        public SuperItemEvent OnItemHolster = new();
        [Tooltip("Invoked when an Item is removed from any Holster (drawn out, evicted or dropped)")]
        public SuperItemEvent OnItemUnholster = new();
        [Tooltip("Invoked when an Item is dropped to the world")]
        public SuperItemEvent OnItemDropped = new();
        [Tooltip("Invoked when an Item picked from the world lands in the hands or in a Holster")]
        public SuperItemEvent OnItemPicked = new();
        [Tooltip("Invoked when the input of an Action Input row is pressed (or Action_Press is called). Sends the Action ID")]
        public ItemActionIDEvent OnActionPressed = new();
        [Tooltip("Invoked when the input of an Action Input row is released (or Action_Release is called). Sends the Action ID")]
        public ItemActionIDEvent OnActionReleased = new();

        [Tooltip("Reactions when an Item is equipped on any Equip Point. Dynamic Target [Equipped Item]")]
        public Reaction2 ItemEquippedReaction;
        [Tooltip("Reactions when an Item is unequipped from any Equip Point. Dynamic Target [Unequipped Item]")]
        public Reaction2 ItemUnequippedReaction;
        [Tooltip("Reactions when an Item is stored in any Holster. Dynamic Target [Holstered Item]")]
        public Reaction2 ItemHolsterReaction;
        [Tooltip("Reactions when an Item is removed from any Holster. Dynamic Target [Unholstered Item]")]
        public Reaction2 ItemUnholsterReaction;
        [Tooltip("Reactions when an Item is dropped to the world. Dynamic Target [Dropped Item]")]
        public Reaction2 ItemDroppedReaction;
        [Tooltip("Reactions when an Item picked from the world lands in the hands or in a Holster. Dynamic Target [Picked Item]")]
        public Reaction2 ItemPickedReaction;

        [Tooltip("Reactions when the input of ANY Action Input row is pressed. Dynamic Target [Super Item Manager]")]
        public Reaction2 AnyActionPressedReaction;
        [Tooltip("Reactions when the input of ANY Action Input row is released. Dynamic Target [Super Item Manager]")]
        public Reaction2 AnyActionReleasedReaction;

        [Tooltip("Pressed / Released reactions for each Action Input row. Dynamic Target [Super Item Manager]")]
        public List<SIActionInputReaction> ActionReactions = new();

        /// <summary>Event + Reactions: an Item was equipped</summary>
        protected virtual void Notify_Equipped(SuperItem item)
        {
            OnItemEquipped.Invoke(item);
            ItemEquippedReaction.TryReact(item);
        }

        /// <summary>Event + Reactions: an Item was unequipped</summary>
        protected virtual void Notify_Unequipped(SuperItem item)
        {
            OnItemUnequipped.Invoke(item);
            ItemUnequippedReaction.TryReact(item);
        }

        /// <summary>Event + Reactions: an Item was stored in a Holster</summary>
        protected virtual void Notify_Holster(SuperItem item)
        {
            OnItemHolster.Invoke(item);
            ItemHolsterReaction.TryReact(item);
        }

        /// <summary>Event + Reactions: an Item left its Holster</summary>
        protected virtual void Notify_Unholster(SuperItem item)
        {
            OnItemUnholster.Invoke(item);
            ItemUnholsterReaction.TryReact(item);
        }

        /// <summary>Event + Reactions: an Item was dropped to the world</summary>
        protected virtual void Notify_Dropped(SuperItem item)
        {
            OnItemDropped.Invoke(item);
            ItemDroppedReaction.TryReact(item);
        }

        /// <summary>Event + Reactions: a picked Item was equipped or holstered</summary>
        protected virtual void Notify_Picked(SuperItem item)
        {
            OnItemPicked.Invoke(item);
            ItemPickedReaction.TryReact(item);
        }

        /// <summary>Event + the Pressed/Released reactions of the Action. A press is ignored while Locked, a release always goes through</summary>
        protected virtual void Notify_ActionInput(ItemActionID actionID, bool pressed)
        {
            if (actionID == null) return;
            if (pressed && locked) return;

            if (pressed)
            {
                OnActionPressed.Invoke(actionID);
                AnyActionPressedReaction.TryReact(this);
            }
            else
            {
                OnActionReleased.Invoke(actionID);
                AnyActionReleasedReaction.TryReact(this);
            }

            for (int i = 0; i < ActionReactions.Count; i++)
            {
                var entry = ActionReactions[i];
                if (entry == null || entry.ID != actionID) continue;

                if (pressed) entry.OnPressed.TryReact(this);
                else entry.OnReleased.TryReact(this);
            }
        }

        //The Manager's input listeners are connected on OnEnable, the Items' ones later on Equip, so a press reaches the
        //Manager BEFORE the Item starts its Action: a reaction gated by [Action Is Playing] always read false.
        //Input presses are queued here and fired on LateUpdate, once every Item has handled them.
        private readonly List<(ItemActionID id, bool pressed)> pendingActionInputs = new();

        /// <summary>Fires the queued Action input notifications, in the order they were received</summary>
        private void Flush_ActionInputs()
        {
            if (pendingActionInputs.Count == 0) return;

            for (int i = 0; i < pendingActionInputs.Count; i++)
                Notify_ActionInput(pendingActionInputs[i].id, pendingActionInputs[i].pressed);

            pendingActionInputs.Clear();
        }

        /// <summary>One stable listener per ActionsInput row for the Action Pressed/Released events, so they can be disconnected again</summary>
        private UnityAction<bool>[] actionEventListeners;

        /// <summary>Connects (or disconnects) one listener per ActionsInput row that fires the Action Pressed/Released events and reactions</summary>
        protected void ConnectActionEventInputs(bool connect)
        {
            if (InputSource == null || ActionsInput == null) return;

            if (connect)
            {
                actionEventListeners ??= new UnityAction<bool>[ActionsInput.Count];

                for (int i = 0; i < ActionsInput.Count && i < actionEventListeners.Length; i++)
                {
                    var row = ActionsInput[i];
                    if (row == null || row.ID == null || actionEventListeners[i] != null) continue;

                    var actionID = row.ID; //Capture the row's ID, not the loop variable
                    actionEventListeners[i] = (value) => { if (Active) pendingActionInputs.Add((actionID, value)); }; //Fired on LateUpdate
                    InputSource.ConnectInput(row.value.Value, actionEventListeners[i]);
                }
            }
            else if (actionEventListeners != null)
            {
                for (int i = 0; i < actionEventListeners.Length && i < ActionsInput.Count; i++)
                {
                    if (actionEventListeners[i] == null) continue;

                    InputSource.DisconnectInput(ActionsInput[i].value.Value, actionEventListeners[i]);
                    actionEventListeners[i] = null;
                }

                pendingActionInputs.Clear(); //Disabled: nothing queued may fire later
            }
        }
        #endregion

        /// <summary>Reference for the Animator component</summary>
        [RequiredField, SerializeField] private Animator anim;

        [SerializeField] private MInteractor interactor;
        /// <summary> Interactor used to focus and pick Items from the ground (read-only; found automatically on the hierarchy) </summary>
        public MInteractor Interactor => interactor;
        #endregion

        [Tooltip("Transform to Parent the item when its equipped (Use the Index)")]
        public List<SEquipPoint> EquipPoints = new();

        //MWC: Per-character offsets table — the ONLY source of Super Item offsets now. Equip Point entries
        //(Item + Equip Point → Offset + Anim Item Type override) and Holster entries (Item + Holster → Offset + Anim + Slot).
        //No asset or no entry → identity offset: Pos(0,0,0) Rot(0,0,0) Scale(1,1,1), Slot 0, Anim from the item's HolsterID.
        [Tooltip("Scriptable Object that stores ALL the offsets for the Super Items of this character:\n" +
            "• Equip Point entries (Item + Equip Point → Offset, Anim Item Type override)\n" +
            "• Holster entries (Item + Holster → Offset, Holster Anim, Holster Slot)\n" +
            "If no entry is found the identity offset is used (Pos 0, Rot 0, Scale 1) with Holster Slot 0.")]
        [CreateScriptableAsset] public SuperItemOffsets ItemOffsets;

        [Tooltip("List of all the holsters the Manager can have")]
        public List<SItemHolster> holsters = new();

        /// <summary>Runtime lookup of the holsters by Holster ID</summary>
        public Dictionary<HolsterID, SItemHolster> D_Holsters { get; set; }
        /// <summary>Runtime lookup of the equip points by Equip Point ID</summary>
        public Dictionary<EquipPointID, SEquipPoint> D_EquipPoints { get; set; }

        [Tooltip("If the weapon is dropped from a holster it will be dropped from this point, relative to the WeaponManager")]
        public TransformReference dropPoint = new();

        //MWC — Default Item (e.g. Unarmed): a fallback SuperItem auto-equipped whenever NO weapon occupies an equip
        //point, so the character can still act (e.g. unarmed combo attacks). It is NOT placed on an equip point — it is
        //equipped directly, so it never counts towards EquippedCount and never conflicts with real weapons.
        [Tooltip("Fallback Item (e.g. Unarmed) auto-equipped when NO weapon is in hand (no equip point occupied).\n" +
            "Leave empty to disable. It should be a child SuperItem with combo actions and equipDuration = 0 for an instant swap.")]
        public SuperItem DefaultItem;

        #region Animator Parameters

        [Tooltip("Item Type (OldWeaponType) to be used in the Animator Controller parameter to activate a Left Hand-Arm Layer")]
        public string m_ItemLeftHandType = "Left Item Type";
        [Tooltip("Item Type (OldWeaponType) to be used in the Animator Controller parameter to activate a Right Hand-Arm Layer")]
        public string m_ItemRightHandType = "Right Item Type";
        [Tooltip("Sends to the Animator the Weapon Hand Value. [True -> Left Hand] [False ->Right Hand]")]
        public string m_Mirror = "Mirror";

        /// <summary>Get all the Animator Parameters the Animal Controller has</summary>
        private List<int> animatorHashParams;
        private int Hash_ItemLeftHandType;
        private int Hash_ItemRightHandType;
        private int Hash_Mirror;
        #endregion

        [Tooltip("If true the item will be stored in the holster when the holster input is pressed. Otherwise pressing the same holster input will be ignored if the weapon of that holster is already equipped")]
        public BoolReference selfStoreHolster = new();

        [Tooltip("ON: dropping an Item in the hands is instant, with no animation (the old behavior).\n" +
            "OFF: the Item's Drop Mode animation is played first, and the Item leaves the hand after its Equip Duration.\n" +
            "Holstered Items are always dropped instantly")]
        public BoolReference InstantDrop = new(false);

        #region Properties
        public IInputSource InputSource { get; private set; }

        public IRider Rider { get; protected set; }
        public Animator Anim { get => anim; set => anim = value; }
        #endregion

        #region Layers
        [Tooltip("Overrode Super item Hit Layer to interact with Objects")]
        public LayerReference m_OverrideItemLayer = new(0);
        [SerializeField, Tooltip("What to do with Triggers")]
        protected QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
        [Tooltip("Tags Interation")]
        public MTags ItemTagInteraction;

        public LayerMask OverrideLayer { get => m_OverrideItemLayer.Value; set => m_OverrideItemLayer.Value = value; }
        public QueryTriggerInteraction TriggerInteraction { get => triggerInteraction; set => triggerInteraction = value; }
        #endregion

        #region Animator System.Actions
        /// <summary>Sets a bool Parameter on the Animator using the parameter Hash</summary>
        public System.Action<int, bool> SetBoolParameter { get; set; }
        /// <summary>Sets a float Parameter on the Animator using the parameter Hash</summary>
        public System.Action<int, float> SetFloatParameter { get; set; }

        /// <summary>Sets a Integer Parameter on the Animator using the parameter Hash</summary> 
        public System.Action<int, int> SetIntParameter { get; set; }

        /// <summary>Sets a Trigger Parameter on the Animator using the parameter Hash</summary> 
        public System.Action<int> SetTriggerParameter { get; set; }
        #endregion


        /// <summary> The Super Item is Active and it will not skip anyany code</summary>
        public bool Active => enabled && gameObject.activeInHierarchy;

        public List<IDString<ItemActionID>> ActionsInput;

        //public HashSet<SuperItem> EquippedItems { get; private set; }

        [SerializeField] private int Editor_Tabs1;
        [SerializeField] private int Editor_Tabs2;
        [SerializeField] private int SelectedEquipPointSlot;
        [SerializeField] private int SelectedHolster;
        [SerializeField] private int SelectedHolsterSlot; //MWC — Slot selected in the Slots list: the only one the Scene handles can edit
        [SerializeField] private bool debug;
        [SerializeField] private bool ShowWarnings = true; //MWC — show/hide the validation Warnings help box in the inspector
        [SerializeField] private bool EditOffsetsItems;

        //----------------------------------------------
        #region Unity Start Methods (Awake Enable Disable)
        protected virtual void Awake()
        {
            CheckReferences();
            ApplyOverrideLayerToTriggers();
            GetHashIDs();
            InputSource = gameObject.FindInterface<IInputSource>(); //Find if we have a InputSource

            Rider = gameObject.FindInterface<IRider>(); //Find if we have a Rider 

            D_Holsters = holsters.ToDictionary(h => h.ID, h => h);
            D_EquipPoints = EquipPoints.ToDictionary(e => e.ID, e => e);

            //MWC: Inject the shared Item Offsets table into every Equip Point and Holster so they can resolve
            //per-item Offsets (and Holster Slots) from the asset. It is the only source of offsets now.
            foreach (var e in EquipPoints) e.Offsets = ItemOffsets;
            foreach (var h in holsters) { h.Offsets = ItemOffsets; h.Manager = this; } //MWC — Manager: Dynamic Target of OnHolsterEmpty
        }

        /// <summary>Copies the Override Item Layer to every IMLayer child outside a Super Item (the unarmed Attack Triggers), so fists hit the same layers as the items</summary>
        protected virtual void ApplyOverrideLayerToTriggers()
        {
            if (OverrideLayer == 0) return; //Nothing to override

            foreach (var layered in GetComponentsInChildren<IMLayer>(true))
            {
                var c = layered as Component;
                if (c == null || c.gameObject == gameObject) continue;          //Skip the character root (Aim lives there)
                if (c.GetComponentInParent<SuperItem>() != null) continue;    //Items apply the override themselves
                if (layered is MDamager damager) damager.m_hitLayer = new LayerReference(OverrideLayer); //Detach from a shared Layer Variable before writing
                else layered.Layer = OverrideLayer;
            }
        }

        protected virtual void OnEnable()
        {
            //Connect to the animator
            SetBoolParameter += SetAnimParameter;
            SetIntParameter += SetAnimParameter;
            SetFloatParameter += SetAnimParameter;
            SetTriggerParameter += SetAnimParameter;

            //Listen to the Animal Events
            Animal.ModeStart += OnModeStart;
            Animal.ModeEnd += OnModeEnd;
            Animal.OnState += OnStateChange;
            Animal.OnStance += OnStanceChange;

            //Connect Inputs for the Actions Index
            //Connect Inputs for the Holster Equip stuff

            if (interactor != null)
            {
                interactor.OnFocusing += (OnInteractorFocused);
                interactor.OnUnFocusing += (OnInteractorUnfocused);
                interactor.OnInteract += (OnInteractorInteract);
            }


            if (IKManager != null)
            {
                IKManager.AnimatorIKAction += AnimatorIKAction;
                IKManager.UpdateIKAction += LateUpdateAction;
                IKManager.OnValidateAction += OnValidateAction;
            }

            if (Rider != null)
            {
                Rider.RiderStatus += GetRiderStatus;        //Get the notifications from the Rider
            }


            Sync_NoItemsBlocks(); //MWC — BEFORE Initialize: a No Items block already running must be known when the starting Items are placed

            Initialize(); //Delay the Initialize a bit to make sure everything is ready

            ConnectHolsterInput(true);
            ConnectFastWeaponInputs(true); //MWC — [Fast Weapons] manager-level listeners for holstered items
            ConnectActionEventInputs(true); //Action Pressed/Released events and reactions
        }



        private void OnDisable()
        {
            //Connect to the animator
            SetBoolParameter -= SetAnimParameter;
            SetIntParameter -= SetAnimParameter;
            SetFloatParameter -= SetAnimParameter;
            SetTriggerParameter -= SetAnimParameter;

            Animal.ModeStart -= OnModeStart;
            Animal.ModeEnd -= OnModeEnd;
            Animal.OnState -= OnStateChange;
            Animal.OnStance -= OnStanceChange;

            if (interactor != null)
            {
                interactor.OnFocusing -= (OnInteractorFocused);
                interactor.OnUnFocusing -= (OnInteractorUnfocused);
                interactor.OnInteract -= (OnInteractorInteract);
            }

            if (IKManager != null)
            {
                IKManager.AnimatorIKAction -= AnimatorIKAction;
                IKManager.UpdateIKAction -= LateUpdateAction;
            }

            if (Rider != null)
                Rider.RiderStatus -= GetRiderStatus;        //Get the notifications from the Rider


            ConnectHolsterInput(false);
            ConnectFastWeaponInputs(false); //MWC — [Fast Weapons]
            ConnectActionEventInputs(false);

            Settle_PendingCoroutines(); //MWC — nothing deferred may outlive the Manager (see the method)
        }

        private readonly HashSet<SuperItem> m_FocusedItems = new();


        /// <summary>  INTERACTOR OF THE SUPER ITEM USER CHECK  </summary>
        private void OnInteractorFocused(IInteractable interactable)
        {
            if (interactable is SuperItem sItem)
            {
                if (sItem.IsEquipped || sItem.IsInHolster) return;

                m_FocusedItems.Add(sItem); //Add the focused item to the set
            }
            else //Check focused Items while the Super Item is equipped
            {
                EquipPoints.ForEach(point => point.Item?.OnInteractorFocused(interactable));
            }
        }

        private void OnInteractorUnfocused(IInteractable interactable)
        {
            if (interactable is SuperItem sItem)
            {
                if (m_FocusedItems.Contains(sItem) && !sItem.IsEquipped && !sItem.IsInHolster)
                {
                    m_FocusedItems.Remove(sItem); //Remove the focused item from the set if is not equipped or in holster
                    //Debug.Log($"Removed SITEM {sItem}");
                }
            }
            else
            {
                EquipPoints.ForEach(point => point.Item?.OnInteractorUnfocused(interactable));
            }
        }

        public float CollectTime;


        private void OnInteractorInteract(IInteractable interactable)
        {
            if (interactable is SuperItem item)
            {
                Item_Pick(item); //MWC — the pick logic moved to Item_Pick so the AI Tasks / external code share the exact same path
                return;          //MWC — Item_Pick stamps CollectTime itself (and keeps the early-out when the pick window has not elapsed)
            }

            CollectTime = Time.time;
        }

        /// <summary>Picks an Item from the world (AI / external callers): plays the Item's Pick Mode, waits its Pick Duration, then Equips or Holsters it (global <see cref="AutoEquip"/> when ON, else the Item's own [Auto Equip]). Same path the Interactor uses</summary>
        public virtual bool Item_Pick(SuperItem item) //MWC — returns true when the pick was accepted (false: null, owned by someone else, or still inside the pick cooldown)
        {
            if (item == null) return false;
            return Item_Pick(item, ResolveAutoEquip(item)); //Global [Auto Equip] ON → hands; OFF → the Item's own flag
        }

        /// <summary>Picks an Item from the world forcing the outcome: <paramref name="equip"/> true = to the hands, false = to its Holster (ignores the Item's [Auto Equip]). Returns true when the pick was accepted</summary>
        public virtual bool Item_Pick(SuperItem item, bool equip)
        {
            if (item == null) return false;
            if (LockVeto("Pick", item)) return false;      //Locked: the hands are busy (carrying a box...), nothing is picked
            if (item.User != null && item.User != this && item.IsInManager) return false; //MWC — the item belongs to another character
            if (item.PickedBy != null && item.PickedBy != this) return false; //MWC — another character is already picking it up (its Pick Duration is still running)
            if (!MTools.ElapsedTime(CollectTime, item.PickDuration)) return false; //Avoid multiple interact calls in a short time

            Debugging($"Interacted with <color=white>{item.name}</color> Picking Item");

            if (item.PickDuration == 0)
            {
                if (equip) //MWC — was item.AutoEquip
                    Item_Equip(item); //Equip the item if it has AutoEquip enabled
                else
                    Item_Holster(item); //Equip the item after the pick duration

                item.NotifyPickedBy(this);
                Notify_Picked(item);
            }
            else
            {
                Animal.Mode_ForceActivate(item.PickMode.ID, item.PickMode.Value); //Activate the pick mode if it has one

                //MWC — tracked: a second pick of the same item replaces the first, and the callback is dropped when the
                //Manager is disabled (a fire-and-forget coroutine keeps running while the component is disabled)
                Cancel_PendingPick(item);

                item.PickedBy = this; //MWC — claim it for this Manager until the callback lands (see SuperItem.PickedBy)

                C_Picks[item] = this.Delay_Action(item.PickDuration, () =>
                {
                    C_Picks.Remove(item);
                    if (!Active || item == null) return; //Disabled meanwhile (or the item is gone): the pick is abandoned, the item stays in the world

                    if (item.PickedBy == this) item.PickedBy = null; //MWC — the claim ends here: the pick is resolved right now

                    //MWC — re-checked, not assumed: the entry guard ran a whole Pick Duration ago. Somebody else may
                    //own the Item by now (an instant pick, a cheat, a respawn), and equipping it would tear it out of
                    //their hands while THEY stay its User.
                    if (item.User != null && item.User != this && item.IsInManager) return;

                    if (equip) //MWC — was item.AutoEquip
                    {
                        var itemEquipTime = item.equipDuration; //Get the current time to check for the pick duration
                        item.equipDuration = 0; //Set the equip duration to zero so it can be equipped immediately after the pick duration
                        Item_Equip(item); //Equip the item if it has AutoEquip enabled
                        item.equipDuration = itemEquipTime; //Restore the equip duration
                    }
                    else
                        Item_Holster(item); //Equip the item after the pick duration

                    item.NotifyPickedBy(this);
                    Notify_Picked(item);
                }
                );
            }

            CollectTime = Time.time;
            return true; //MWC — pick accepted
        }

        #endregion

        public virtual void Initialize()
        {
            //Delay this so all other main scripts can initialize like the animal controller and the animator
            //MWC — tracked: OnEnable twice inside the 5 frames used to queue TWO callbacks (the second re-equipped an
            //already equipped item and fired the Equip Point reactions again), and a callback running while the
            //component was disabled cleared point.Item while Item_Equip refused the equip: the item stayed parented
            //to the hand but untracked, and the next Initialize could not find it anymore.
            this.Stop_Action(C_Initialize);
            C_Initialize = this.Delay_Action(5, () =>
            {
                C_Initialize = null;
                if (!Active) return; //Disabled meanwhile: leave the authored Items where they are; the next OnEnable initializes again

                //Locked before it initialized (a Lock reaction on Start, or re-enabled while Locked): the starting Items
                //are still placed (holstered/hidden and remembered) instead of being refused by the lock gate and lost
                var bypass = lockBypass;
                lockBypass = true;

                //Verify   if any weapon is already set to be equipped
                foreach (var point in EquipPoints)
                {
                    if (point.Item != null)
                    {
                        //MWC — the authored reference may be a PREFAB (dragged from the Project window): instantiate it,
                        //otherwise everything downstream (parenting, physics, events) would run on the Prefab Asset itself
                        var item = Item_Instance(point.Item);
                        point.Item = null;

                        if (locked) Hold_Item(item, lockMode == LockItemsMode.Hide, "Lock");
                        else Item_Equip(item, true);
                    }
                }

                //if there is a item in any of the holster parent it will be added to the holster
                foreach (var holster in holsters)
                {
                    if (holster.Item != null)
                    {
                        //MWC — same as the Equip Points above: a Prefab authored on the Holster is instantiated first,
                        //and the Holster head points at the INSTANCE (never at the Asset) from here on
                        var item = Item_Instance(holster.Item);
                        holster.Item = item;

                        if (holster.AutoEquip || item.AutoEquip)
                        {
                            //MWC — the authored head was never STORED (no StoredItems entry, IsInHolster false), so the
                            //UnHolster inside the draw could not clear it and the Holster kept claiming the Item for the
                            //whole session (HasItem/Contains true while it sat in the hand). Drop it before the draw,
                            //exactly like the Equip Point loop above clears its slot before Item_Equip.
                            holster.Item = null;

                            if (locked) Hold_Item(item, lockMode == LockItemsMode.Hide, "Lock");
                            else Item_Equip(item, true);
                        }
                        else
                            Item_Holster(item);
                    }
                }

                lockBypass = bypass;

                RefreshDefaultItem(); //MWC — start unarmed if no starting weapon ended up in hand
            });
        }

        /// <summary>Scene instance of an Item: a Prefab reference (authored on an Equip Point/Holster, or passed by a Reaction) is instantiated, anything already in a scene is returned unchanged</summary>
        public virtual SuperItem Item_Instance(SuperItem item)
        {
            //MWC — a Prefab Asset can never be equipped or holstered: SetParent on it throws, and its Awake/events
            //would run on the Asset. Every entry point (Initialize, Item_Equip, Item_Holster) routes through here.
            if (item == null || !item.gameObject.IsPrefab()) return item;

            //Spawned on the character so it never appears at the world origin if the equip/store is not immediate
            var instance = Instantiate(item, transform.position, transform.rotation);
            instance.name = item.name; //Drop the "(Clone)" suffix

            Debugging($"[<color=yellow>Instantiate</color> <color=white>{instance.name}</color>] the Item reference was a Prefab", instance, "orange");

            return instance;
        }

        protected virtual void GetHashIDs()
        {
            animatorHashParams = new List<int>();

            foreach (var parameter in Anim.parameters)
            {
                animatorHashParams.Add(parameter.nameHash);
            }

            Hash_Mirror = TryGetAnimParameter(m_Mirror);
            Hash_ItemLeftHandType = TryGetAnimParameter(m_ItemLeftHandType);
            Hash_ItemRightHandType = TryGetAnimParameter(m_ItemRightHandType);
        }


        //Send 0 if the Animator does not contain
        private int TryGetAnimParameter(string param)
        {
            var AnimHash = Animator.StringToHash(param);

            if (!animatorHashParams.Contains(AnimHash))
                return 0;
            return AnimHash;
        }

        private void Item_Equip_From_Holster(HolsterID ID, bool InputValue)
        {
            if (InputValue) Item_Equip_From_Holster(ID); //Only when the Input is true
        }

        /// <summary>  Equip the Item from the Holster it is assigned to  </summary>
        /// <param name="item"></param>
        public virtual void Item_Equip_From_Holster(SuperItem item)
        {
            if (!Active) return;

            if (item == null) return; //MWC — guard
            if (LockVeto("Equip From Holster", item)) return;

            //MWC: Dual Wielding - find the holster that actually stores it; a fall-through copy lives in the Secondary Holster
            var HolsterWithItem = FindHolsterOf(item);

            //MWC — was 'HolsterWithItem.Item == item' (head only), so a dual-wield item stored in a non-head slot could never be drawn.
            //Also null-checked: Find returns null when no holster matches, which used to throw on .IsValid.
            if (HolsterWithItem != null && HolsterWithItem.IsValid)
            {
                Item_Equip(item);
            }
        }

        /// <summary> Equip the Item from the Holster it is assigned to by HolsterID  </summary>
        /// <param name="id">The ID of the holster to equip the item from </param>
        public virtual void Item_Equip_From_Holster(HolsterID id)
        {
            if (!Active) return;

            if (LockVeto("Equip From Holster")) return; //Locked: the Holster Inputs are ignored too

            if (D_Holsters.TryGetValue(id, out var holster))
            {
                if (holster.Item)
                {
                    //MWC — DRAW ALL: equip every item stored in this holster, not only the head one.
                    //Dual wielding stores several items per holster, so drawing only holster.Item left the rest behind.
                    var toDraw = new List<SuperItem>(holster.StoredItems);

                    if (toDraw.Count == 0) toDraw.Add(holster.Item); //Fallback: edit-time authored items may not be tracked yet

                    foreach (var drawItem in toDraw) Item_Equip(drawItem);
                }
                else //There's no Item in the holster
                {
                    if (selfStoreHolster.Value)
                    {
                        //MWC — STORE ALL: was EquipPoints.Find(...), which stored only the FIRST matching item.
                        //When dual wielding two items sharing a Holster ID (e.g. two Swords) the off-hand copy stayed equipped.
                        //Snapshot first: Item_Unequip clears the equip points while we iterate.
                        var toStore = new List<SuperItem>();

                        //MWC: Dual Wielding - an item also belongs to this holster when it is its Secondary one
                        foreach (var point in EquipPoints)
                            if (point.Item != null && point.Item.IsAllowedHolster(id)) toStore.Add(point.Item);

                        foreach (var storeItem in toStore) Item_Unequip(storeItem);
                    }
                    else
                    {
                        //MWC — the Holster is empty because its Item is IN HAND, and [Self Store Holster] is OFF, so the
                        //press is dropped here. It used to return in total silence, which reads as a broken input
                        Debugging($"[Holster: {(id != null ? id.name : "?")}] is empty (its Item is in hand) and " +
                            "[Self Store Holster] is OFF → holster input ignored", "red");
                    }
                }
            }
        }

        /// <summary>Draws the Items stored in a Holster by ID and nothing else: an empty Holster (Item already in hand) is ignored, so a repeated call never stores the Item back like Item_Equip_From_Holster does with [Self Store Holster]. Made for mobile Aim/Draw buttons that fire on every touch</summary>
        public virtual void Item_Draw_From_Holster(HolsterID id)
        {
            if (!Active || id == null) return;

            if (LockVeto("Draw From Holster")) return; //Locked: nothing may be drawn

            if (D_Holsters.TryGetValue(id, out var holster) && holster.Item)
            {
                var toDraw = new List<SuperItem>(holster.StoredItems);

                if (toDraw.Count == 0) toDraw.Add(holster.Item); //Fallback: edit-time authored items may not be tracked yet

                foreach (var drawItem in toDraw) Item_Equip(drawItem);
            }
        }

        public virtual void Item_Holster(SuperItem item)
        {
            if (!Active) return;
            if (item == null) return;

            if (LockVeto("Holster", item)) return; //Locked: only the Manager itself (Hold_Item, Initialize) may store, through lockBypass

            item = Item_Instance(item); //MWC — a Prefab reference is instantiated here, so the Holster only ever stores scene Items

            Unhide_Item(item); //MWC — it is going into a holster, so it must be visible again (no-op when it is not hidden)
            Cancel_PendingDraw(item); //MWC — stored mid-draw: the item stays put, the delayed draw must not pull it out later
            Cancel_PendingPick(item); //MWC — stored by another path while its pick delay was still running

            var holster = ResolveHolster(item); //MWC: Dual Wielding - primary holster, or the secondary one when the primary is full

            if (holster != null)
            {
                if (item.IsEquipped)
                    Item_Unequip_INTERNAL_FAST(item); //Unequip the item without delay if it is equipped, because we want to holster it immediately

                //MWC — ownership is taken HERE, not only on Equip. A pickup with [Auto Equip] off goes straight to the
                //Holster, and that path never assigned User: the item was physically stored yet still reported
                //User == null, so the Item_Pick guard (which requires a non-null User) let a SECOND character accept
                //the very same world item and re-parent it into ITS holster, leaving this one holding a stale entry.
                //Also fires [On Added To Manager], which the inspector documents for this exact path.
                item.AddToManager(this);

                EvictedItems.Clear();                                         //MWC: Capacity - Items pushed out of the holster
                holster.Holster(item, EvictedItems);
                item.InvokeUnfocused(gameObject);                          //OnUnfocused event + reaction (respects the item Focus Tags filter)
                item.InvokeHolstered(gameObject);                             //OnHolstered event + Holstered reaction (Dynamic Target: this Manager)
                Notify_Holster(item);

                Debugging($"[<color=yellow>Holster</color> {item.name}] → [{holster.ID.name} Slot:{holster.GetUsedSlot(item)}]", "orange"); //MWC — slot resolved by the holster (SuperItemOffsets asset)

                item.Active = false;

                //MWC — the holster had no room (every Slot taken, or its Capacity was reached): drop the evicted items
                //to the ground properly (unparent + restore physics) instead of leaving them orphaned, still parented
                //to the slot. Done last so the incoming item is fully settled first.
                if (EvictedItems.Count > 0)
                {
                    var evicted = EvictedItems.ToArray(); //Snapshot: Item_Release can re-enter and clear the list
                    EvictedItems.Clear();

                    foreach (var ev in evicted)
                    {
                        if (ev == null || ev == item) continue;

                        Debugging($"[<color=yellow>Holster</color> {holster.ID.name}] is full → <color=white>Dropping [{ev.name}]</color>", "red");
                        Item_Release(ev);
                    }
                }
            }
            else //There's no holster for that item
            {
                Item_Release(item); //There's no holder for that item so drop it ??? (NOT TESTED)

                var holsterName = item.HolsterID != null ? item.HolsterID.name : "[None]"; //MWC — an item with no Holster ID reaches this branch too

                Debugging($"[<color=yellow>Holster</color> {item.name}] → <color=red>Holster<color=white> [{holsterName}]</color> NOT Found! <<Dropping Item>>. " +
                    $"Please add <color=white>[{holsterName}]</color> to your Super Item Manager</color>", "red");
            }
        }

        /// <summary>Takes the Item OUT of its Holster and leaves it in the world: unparented, physics and interaction restored, ownership released</summary>
        //MWC — this used to be bookkeeping ONLY (fire the event, unparent, forget the slot) even though it is public
        //AND exposed as a standalone Reaction ([SI Item Equip/UnHolster], [SI Manager Equip/UnHolster]). Called on its
        //own it left the Item in a state it can never leave: unparented with IsInHolster false, but still unequipped,
        //still Active false (no interaction, so nobody can pick it up again), still physics/collider-disabled by
        //[Disable Colliders On Holster], and still owned by this Manager. It was neither a held Item nor a world Item.
        //The transition is completed here; the internal callers that only want the bookkeeping step (Item_Release,
        //which drops the Item itself, and Equip_INTERNAL_FAST, which puts it in the hand) use UnHolster_INTERNAL.
        public virtual void Item_UnHolster(SuperItem item)
        {
            if (!Active) return;
            if (item == null) return;
            if (!item.IsInHolster) return;
            if (LockVeto("UnHolster", item)) return;

            if (!UnHolster_INTERNAL(item)) return;

            //Complete the transition: the Item is going back to the world, so it must behave like any world pickup
            item.transform.localScale = item.DefaultTransformValue.localScale; //Restore the default scale (the holster slot may be scaled)
            item.Physics_Restore();       //Rigidbody + colliders back to their authored state ([Disable Colliders On Holster] undone)
            item.Active = true;           //Interactions on again: an Interactor can focus and pick it up
            item.RemoveFromManager();     //Ownership ends here, so any character may claim it (also fires [On Remove From Manager])
        }

        /// <summary>Holster bookkeeping only (event + unparent + forget the slot). Returns false when the Item was not stored in any Holster</summary>
        //MWC — the step shared by the public Item_UnHolster above and by the internal paths that immediately send the
        //Item somewhere else (the hand, or the ground). Kept separate so neither of them completes the transition twice.
        //It carries the [IsInHolster] guard itself, because both internal callers reach it for Items that may never
        //have been holstered at all ("remove it from the holster IF it was in one") and must not fire OnUnHolstered.
        private bool UnHolster_INTERNAL(SuperItem item)
        {
            if (item == null || !item.IsInHolster) return false;

            item.InvokeUnHolstered(gameObject);           //OnUnHolstered event + UnHolstered reaction (Dynamic Target: this Manager)

            //MWC: Dual Wielding - the item may be stored in any slot (not only as the holster head),
            //and in the secondary holster rather than its primary one
            var holster = FindHolsterOf(item);

            if (holster != null)
            {
                var slot = holster.GetUsedSlot(item); //MWC — capture the slot before Clear() forgets it
                holster.Clear(item); //MWC: remove only this item; the last stored one is promoted as the new head
                Debugging($"[<color=yellow>UnHolster</color> {item.name}] → [Holster: {holster.ID.name} Slot:{slot}]", "orange");
                Notify_Unholster(item);
                return true;
            }
            return false;
        }


        /// <summary>  When the Item is released from the holsters and or de equip points, restore the item to the world </summary>
        public virtual void Item_Release(SuperItem item)
        {
            //Find the item in EquippedItems
            if (!Active) return;
            if (item == null) return;

            if (LockVeto("Drop", item)) return;       //Locked: a held/hidden Item is never dropped (it comes back on Unlock)

            Unhide_Item(item);                        //MWC — it is going to the ground, so it must be visible again (no-op when it is not hidden)
            Cancel_PendingDraw(item);                 //MWC — dropped mid-draw: the delayed Equip_INTERNAL_FAST used to put the dropped item back in the hand
            Cancel_PendingPick(item);                 //MWC — dropped mid-pick: nothing to collect anymore

            Item_Unequip_INTERNAL_FAST(item);         //Unequip the item, no delay for the unequip, because we want to drop the item immediately
            UnHolster_INTERNAL(item);                 //MWC — bookkeeping only: item.Drop() below restores physics, interaction and ownership itself

            Drop_INTERNAL(item);
        }

        /// <summary> Puts an already unequipped/unholstered Item on the Drop Point and hands it back to the world </summary>
        private void Drop_INTERNAL(SuperItem item)
        {
            if (dropPoint.Value)
                item.transform.SetPositionAndRotation(dropPoint.position, dropPoint.rotation);

            item.Drop(this); //Call the Drop method from the item

            Debugging($"[Drop <color=white>{item.name}]</color>", "orange");

            Notify_Dropped(item);
        }

        public virtual void Item_Equip(SuperItem newItem, bool Fast = false)
        {
            if (!Active) return;
            if (newItem == null) return;

            if (LockVeto("Equip", newItem)) return; //Locked: the request is refused (NOT parked): only what was in hand at Lock time comes back on Unlock

            newItem = Item_Instance(newItem); //MWC — a Prefab reference is instantiated here, so only scene Items reach the Equip Points

            //MWC — a No Items [State|Stance|Mode] is blocking the hands (e.g. opening a chest): nothing can be drawn now.
            //The request is not lost: the item is stored and remembered, so it is drawn back when the block ends.
            if (NoItemsBlocked)
            {
                Hold_Item(newItem);
                Debugging($"[<color=yellow>Equip</color> {newItem.name}] blocked by [No Items in State|Stance|Mode]. It will be drawn when the block ends", "red");
                return;
            }

            //MWC: Swap - the new item is drawn only when its Equip animation ends (equipDuration), but the old item is
            //holstered RIGHT NOW, below. Reserve the new item's exit from its holster so its Slot already counts as free
            //and the old item lands on it, instead of being bumped to another slot (two items sharing one Holster Slot).
            var exitHolster = FindHolsterOf(newItem);
            if (exitHolster != null) exitHolster.Leaving = newItem;

            //Unequip any other item that is in the same equip point
            foreach (var E_p in EquipPoints)
            {
                var oldItem = E_p.Item;
                if (oldItem == null) continue; //Invalid item in equip point

                if (!Fast && oldItem.IsEquipping) { ReleaseExit(); return; } //The item is currently being equipped, so skip it to avoid conflicts //MWC: Swap - no draw happens, free the reservation

                newItem.InvokeUnfocused(gameObject); //OnUnfocused event + reaction (respects the item Focus Tags filter)

                if (oldItem == newItem && selfStoreHolster.Value)
                {
                    Debugging($"AutoHolster Self [[{newItem.name}]] {Fast}"); //MWC — gated behind manager debug (was un-gated build spam)
                    Item_Unequip(oldItem);
                    ReleaseExit();  //MWC: Swap - no draw happens, free the reservation
                    return;         //Skip itself
                }

                //Check if the new Item is unique (Solo Equipment) 
                if (newItem.UniqueEquipment || !newItem.CanBeEquippedWith.Check(oldItem.ItemType))
                {
                    if (oldItem.HolsterID == newItem.HolsterID) //If the item is in the same Holster
                    {
                        HideOldItem(oldItem, E_p);
                        Debugging($"New item is UNIQUE NEW {newItem.name}"); //MWC — gated behind manager debug
                    }
                    else
                    {
                        Debugging($"OLD item is NOT Compatible with new {newItem.name}"); //MWC — gated behind manager debug
                        Item_Holster(oldItem); //The old item does not share the same holster
                    }

                    E_p.Clear_Item();

                    newItem.InvokeUnfocused(gameObject); //OnUnfocused event + reaction (respects the item Focus Tags filter)
                }
                else if (oldItem.UniqueEquipment || !oldItem.CanBeEquippedWith.Check(newItem.ItemType))
                {
                    if (oldItem.HolsterID == newItem.HolsterID) //If the new item uses the same holster as the old item then try to equip the old item from the holster after equipping the new item
                    {
                        HideOldItem(oldItem, E_p);

                        Debugging("OLD ITEM IS UNIQUE, so holster it"); //MWC — gated behind manager debug
                    }
                    else
                    {
                        if (!newItem.UniqueEquipment && D_Holsters.TryGetValue(oldItem.HolsterID, out var oldItemHolster) && oldItemHolster.HasItem && !oldItemHolster.Item.UniqueEquipment)
                        {
                            Debugging("Equip old item from the holster from the equip"); //MWC — gated behind manager debug
                            //Item_UnHolster(oldItemHolster.Item); //Unholster the item
                            Equip_Internal_Wait(oldItemHolster.Item, Fast); //Equip the item from the holster
                        }

                    }

                    Item_Holster(oldItem); //Holster the old item
                    E_p.Clear_Item();
                }
                //Check if both items can live together, if not then holster the old item
                else if (newItem.CanBeEquippedWith.Check(oldItem.ItemType) && oldItem.CanBeEquippedWith.Check(newItem.ItemType))
                {
                    //They can live together, do nothing
                }
                else
                {
                    HideOldItem(oldItem, E_p);
                }
            }

            Equip_Internal_Wait(newItem, Fast);

            //MWC: Swap - the draw never started (no free Equip Point). The item stays in its slot, so drop the reservation.
            //When it did start, the reservation is released by the holster itself: Clear() on the actual UnHolster, or
            //Holster() if the item is stored again before the animation completes.
            if (!newItem.IsEquipping) ReleaseExit();

            void HideOldItem(SuperItem item, SEquipPoint E_P)
            {
                if (item.IsEquipped)
                    Item_Holster(item); //Holster the item
                else
                    Item_Release(item); //Drop the item

                E_P.Clear_Item();
            }

            //MWC: Swap - cancel the reserved holster exit of the new item (only if it is still ours)
            void ReleaseExit()
            {
                if (exitHolster != null && exitHolster.Leaving == newItem) exitHolster.Leaving = null;
            }
        }

        private void Equip_Internal_Wait(SuperItem item, bool Fast)
        {
            if (item == null) return;
            if (item.IsEquipping) return; //The item is currently being equipped, so skip it to avoid conflicts

            //MWC — a hidden (no Holster) item stays deactivated until the draw actually completes, otherwise it pops
            //back into the hand on the FIRST frame of its Equip animation. Equip_INTERNAL_FAST unhides it.
            if (!HiddenItems.Contains(item)) item.gameObject.SetActive(true); //Make sure the item is active

            //MWC: Dual Wielding - Resolve the Equip Point: primary, or first free secondary point when the primary holds a compatible item
            var equipPoint = ResolveEquipPoint(item);

            if (equipPoint != null)
            {
                //MWC — a draw is already in flight on this point (Reserved is set, Item is still null until the delayed
                //Equip_INTERNAL_FAST lands). Ignore this request: stacking a second delayed equip on the same point
                //parented BOTH items to it and leaked the first one (it stayed IsEquipped with no holster and no owner).
                //ResolveEquipPoint is reservation-aware, so a compatible Dual Wielding item already resolved to its
                //Secondary point before reaching here; only a genuine same-point collision gets this far.
                if (equipPoint.Item == null && equipPoint.Reserved != null && equipPoint.Reserved != item)
                {
                    Debugging($"[<color=yellow>Equip</color> {item.name}] ignored → [{equipPoint.Reserved.name}] is still being drawn on [{equipPoint.ID.name}]", "red");
                    return;
                }

                //MWC: No free allowed point → evict the occupant (original single-point behavior).
                //Fixes orphaned items: before, Set_Item silently overwrote the occupant of the point.
                if (equipPoint.Item != null && equipPoint.Item != item)
                {
                    var occupant = equipPoint.Item;

                    if (occupant.IsEquipped) Item_Holster(occupant); //Routes through Unequip_INTERNAL_FAST and clears the point
                    else Item_Release(occupant);

                    equipPoint.Clear_Item(); //Safety: edit-time placed occupants may not have CurrentEquipPoint set
                }

                //Activate the Equip Mode for the duration of the Equip Animation and then equip the item (Before the Delay)!
                if (!Fast && item.equipDuration > 0 && item.EquipMode != null)
                {
                    //MWC — Holster Anim now comes from the SuperItemOffsets asset; no entry → the item's HolsterID
                    var Ability = GetHolsterAnimID(item);

                    //MWC: Use the resolved Equip Point's side for the Equip animation
                    Ability *= (equipPoint.Side == WeaponSide.Left ? 1 : -1);

                    //MWC — reserve the point for the duration of the Equip animation so a concurrent equip resolves elsewhere
                    //(previously another equip during the animation could steal the captured point).
                    //Reserved BEFORE the Mode is activated: the Mirror parameter counts reserved points too, so the
                    //Equip animation already plays with the Dual Wielding value instead of the previous item's one.
                    equipPoint.Reserved = item;

                    UpdateMirrorParameter(); //MWC — the Equip animation needs the correct Mirror value

                    //MWC — the hand starts reaching for the Item now, so it drops the rein here instead of when the
                    //draw ends. The point is already Reserved, and UpdateReinHands counts reservations as busy.
                    //There is no mirror of this on the Unequip side on purpose: the hand still holds the Item while
                    //the Store animation plays, so it only takes the rein back when that animation ends.
                    UpdateReinHands();

                    Animal.Mode_ForceActivate(item.EquipMode.ID, Ability);

                    //MWC — tracked on the point (next to its reservation) so a drop/hold/disable can cancel the draw
                    equipPoint.C_Draw = this.Delay_Action(item.equipDuration, () => Draw_Delayed_End(item, equipPoint));
                }
                else
                {
                    Equip_INTERNAL_FAST(item, equipPoint);
                }
                //Set the current equip time to now, so the item can check for the equip duration if needed
                item.CurrentEquipTime = Time.time;
            }
        }

        //MWC: Dual Wielding - Resolves which Equip Point the item should use.
        //Primary free (or already holding this item) → primary. Primary occupied by a compatible item → the secondary point when it's free.
        //Nothing free (or incompatible occupant) → primary; the caller evicts the occupant (original behavior).
        private SEquipPoint ResolveEquipPoint(SuperItem item)
        {
            //MWC — Equip Point Equivalents: a missing point falls back to its Equivalent (e.g. Wolf: Right Hand → Head)
            var primaryID = MapEquipPoint(item.EquipPoint);
            if (primaryID == null || !D_EquipPoints.TryGetValue(primaryID, out var primary)) return null;

            //MWC — use Occupant (Item ?? Reserved) so an item still playing its Equip animation counts as holding the point.
            //Fixes two dual-wield items drawn in the same frame both resolving to the primary point.
            var occupant = primary.Occupant;
            if (occupant == null || occupant == item) return primary;

            //MWC — single secondary point: fall through only when both items can be equipped together and it is free
            var secondaryID = MapEquipPoint(item.SecondaryEquipPoint);

            if (AreCompatible(item, occupant) && secondaryID != null &&
                D_EquipPoints.TryGetValue(secondaryID, out var secondary) &&
                (secondary.Occupant == null || secondary.Occupant == item)) //Reservation-aware
                return secondary;

            return primary;
        }

        /// <summary> The Equip Point itself when the character has it, otherwise its first available Equivalent from the Item Offsets asset. Null when neither exists </summary>
        public EquipPointID MapEquipPoint(EquipPointID point)
        {
            if (point == null || D_EquipPoints == null) return null;
            if (D_EquipPoints.ContainsKey(point)) return point;

            return ItemOffsets != null ? ItemOffsets.GetEquivalent(point, D_EquipPoints) : null;
        }

        /// <summary> Action ID the Item plays for [actionID]: [actionID] when its Active Action Set has it, otherwise its first Action Equivalent from the Item Offsets asset </summary>
        public ItemActionID ResolveActionID(SuperItem item, ItemActionID actionID)
        {
            if (item == null || actionID == null || ItemOffsets == null) return actionID;
            return ItemOffsets.ResolveAction(actionID, item.ActiveActionSet);
        }

        /// <summary> Actions Input row that drives the Item's [actionID]: its own row, or the row of an Input Action that resolves to it through the Action Equivalents. Null when none </summary>
        internal IDString<ItemActionID> GetActionInput(SuperItem item, ItemActionID actionID)
        {
            if (actionID == null || ActionsInput == null) return null;

            var direct = ActionsInput.Find(a => a != null && a.ID == actionID);
            if (direct != null || ItemOffsets == null) return direct;

            for (int i = 0; i < ActionsInput.Count; i++)
            {
                var row = ActionsInput[i];
                if (row != null && row.ID != null && row.ID != actionID && ResolveActionID(item, row.ID) == actionID) return row;
            }
            return null;
        }

        /// <summary> Can the Item be equipped on the Equip Point? Its own primary/secondary point or an Equivalent of them </summary>
        public bool CanUseEquipPoint(SuperItem item, EquipPointID point) =>
            item != null && (item.IsAllowedEquipPoint(point) || (ItemOffsets != null && ItemOffsets.IsEquivalent(item, point)));

        //MWC: Dual Wielding - Resolves which Holster the item should be stored in. Mirrors ResolveEquipPoint:
        //the primary holster wins while it still has a free Slot from the item's authored priority list; when it is
        //full the item falls through to its Secondary (Dual Wielding) Holster, which carries its own Offset/Slots/Anim
        //entry in the SuperItemOffsets asset. No secondary, or the secondary is full/missing → the primary, and the
        //caller evicts the oldest stored item there (original behavior).
        private SItemHolster ResolveHolster(SuperItem item)
        {
            if (item == null || item.HolsterID == null) return null;

            D_Holsters.TryGetValue(item.HolsterID, out var primary);

            if (primary != null && primary.HasRoomFor(item)) return primary;

            if (item.SecondaryHolster != null &&
                D_Holsters.TryGetValue(item.SecondaryHolster, out var secondary) &&
                secondary != null && secondary.HasRoomFor(item))
                return secondary;

            return primary;
        }

        //MWC: Dual Wielding - The holster that actually stores the item. It is not necessarily the item's primary
        //Holster ID: a fall-through copy lives in the Secondary Holster. Null when the item is not stored anywhere.
        private SItemHolster FindHolsterOf(SuperItem item)
        {
            if (item == null) return null;

            foreach (var h in holsters)
            {
                if (h != null && h.Contains(item)) return h;
            }
            return null;
        }

        //MWC: Mutual CanBeEquippedWith check (both items allow each other's Item Type)
        private static bool AreCompatible(SuperItem a, SuperItem b) =>
            !a.UniqueEquipment && !b.UniqueEquipment &&
            a.CanBeEquippedWith.Check(b.ItemType) && b.CanBeEquippedWith.Check(a.ItemType);

        //MWC — Holster Anim now lives in the SuperItemOffsets asset (per Item + Holster).
        //Use it when a matching entry has one; otherwise use the item's HolsterID as the mode ability to activate
        //during the equip duration, because some items might not have a holster but still want to activate a mode.
        private int GetHolsterAnimID(SuperItem item)
        {
            //MWC: Dual Wielding - read the entry of the holster the item is actually in (it may be the Secondary one),
            //or of the holster it is about to be stored in when it is still equipped. Falls back to its primary ID.
            var holster = FindHolsterOf(item) ?? ResolveHolster(item);
            var holsterID = holster != null ? holster.ID : item.HolsterID;

            var anim = ItemOffsets != null ? ItemOffsets.GetHolsterAnim(item.ItemType, holsterID) : null;
            return anim != null ? anim.ID : item.HolsterID;
        }

        //MWC — the Animator Item Type override now lives in the SuperItemOffsets asset (per Item + Equip Point).
        //No asset, no entry or empty override → the item's own Item Type.
        private int GetAnimItemTypeID(SuperItem item, EquipPointID point)
        {
            if (ItemOffsets != null && point != null)
            {
                var over = ItemOffsets.GetItemTypeOverride(item.ItemType, point);
                if (over != null) return over.ID;
            }
            return item.ItemType != null ? item.ItemType.ID : 0;
        }

        private void Equip_INTERNAL_FAST(SuperItem item, SEquipPoint equipPoint)
        {
            //MWC — a No Items [State|Stance|Mode] started while this item was still playing its Equip animation
            //(this call is the delayed end of that draw). Do NOT put it in the hand: free the reserved point and
            //park it in the Hold list so it is drawn back when the block ends.
            if (HandsBlocked) //No Items [State|Stance|Mode] block, or the Manager was Locked mid-draw
            {
                if (equipPoint != null && equipPoint.Reserved == item) equipPoint.Reserved = null;

                Hold_Item(item, locked && lockMode == LockItemsMode.Hide, locked ? "Lock" : "No Items in State|Stance|Mode");
                Debugging($"[<color=yellow>Equip</color> {item.name}] cancelled by [{(locked ? "Lock" : "No Items in State|Stance|Mode")}]. It will be drawn when the block ends", "red");
                return;
            }

            Cancel_PendingPick(item); //MWC — equipped by another path while its pick delay was still running: do not equip it twice
            Unhide_Item(item);    //MWC — the draw is complete: a hidden (no Holster) item becomes visible exactly now
            UnHolster_INTERNAL(item); //MWC — bookkeeping only: the item goes straight into the hand below, so it must NOT be restored to the world first

            //MWC — safety net for the delayed end of a draw: the point may have gained an occupant WHILE this Equip
            //animation was playing (another draw's delayed callback landed first). Set_Item overwrites Item without
            //unparenting, so the previous occupant used to stay parented here forever, still flagged IsEquipped.
            if (equipPoint.Item != null && equipPoint.Item != item)
            {
                var stale = equipPoint.Item;

                Debugging($"[<color=yellow>Equip</color> {item.name}] → [{equipPoint.ID.name}] was taken by [{stale.name}] during the draw. Storing it", "red");

                if (stale.IsEquipped) Item_Holster(stale); //Routes through Unequip_INTERNAL_FAST and clears the point
                else Item_Release(stale);

                equipPoint.Clear_Item();
            }

            equipPoint.Set_Item(item); //Parent the item to the equip point
            item.Equip(this);       //Call the Equip Item Method from the Item

            //MWC: Dual Wielding - Side is taken from the Equip Point ID the item landed on
            var side = item.EffectiveSide;

            UpdateMirrorParameter(); //MWC — resolved from the whole equipped state (Dual Wielding → never mirrored)
            UpdateReinHands();       //MWC — the hand is busy now: hand the rein off to the free one

            //MWC — the Item Type override now lives in the SuperItemOffsets asset (per Item + Equip Point); no entry → item's Item Type
            var animItemType = GetAnimItemTypeID(item, item.CurrentEquipPoint?.ID);

            if (side == WeaponSide.Left) TryAnimParameter(Hash_ItemLeftHandType, animItemType);    //Set the Left Hand Parameter on the animator
            if (side == WeaponSide.Right) TryAnimParameter(Hash_ItemRightHandType, animItemType);  //Set the Right Hand Parameter on the animator

            if (interactor != null) interactor.RemoveFocusedItem(item); //Remove the item from the interactor focused interactables

            Debugging($"[<color=yellow>Equip</color> {item.name}] → [Side: {side}] Unique: <color=white>{item.UniqueEquipment}]</color>", "orange");

            //MWC — was [OnEquip] only, so a Set listening to ambient state (Riding, State, Stance, Interactor, Target)
            //stayed unchecked when the Item was equipped AFTER that state had settled (mount first, draw the Bow later
            //→ the [Bow Riding] Set never activated). The Item now re-syncs with the whole ambient state on equip.
            foreach (var point in EquipPoints)
            {
                if (point.Item) point.Item.VerifyAutoSetActivation(SuperItem.EquipSyncTypes);
            }

            //MWC — Equip Point reactions, fired last so they see the completed equip state.
            //Dynamic Target is the item that was just equipped.
            equipPoint.OnEquip.TryReact(item);
            Notify_Equipped(item);

            RefreshDefaultItem(); //MWC — a real weapon is now in hand: put the Default Item away
        }

        /// <summary> Stores the equipped Item (Store animation), then puts it away as its Equip Point's [On Store] says </summary>
        public virtual void Item_Unequip(SuperItem item)
        {
            if (!Active) return;
            if (item == null) return;
            if (!item.IsEquipped) return;
            if (item.IsEquipping) return;
            if (LockVeto("Unequip", item)) return;

            Store_INTERNAL(item, item.CurrentEquipPoint != null ? item.CurrentEquipPoint.OnStore : SIStoreMode.Holster);
        }

        /// <summary> Plays the Store animation that matches <paramref name="mode"/> (Drop → the Item's Drop Mode, otherwise its Holster animation), then unequips and puts the Item away with that mode </summary>
        private void Store_INTERNAL(SuperItem item, SIStoreMode mode)
        {
            if (item.equipDuration > 0)
            {
                ModeID storeMode;
                int Ability;

                //MWC — Drop plays the Item's Drop animation instead of the Holster one (no Drop Mode → instant drop).
                //Same equipDuration window as the Holster store, so IsEquipping and the delayed end stay in sync
                if (mode == SIStoreMode.Drop)
                {
                    storeMode = item.DropMode.ID;
                    Ability = item.DropMode.Value;
                }
                else
                {
                    storeMode = item.UnequipMode;

                    //MWC — Holster Anim now comes from the SuperItemOffsets asset; no entry → the item's HolsterID
                    Ability = GetHolsterAnimID(item);
                    Ability *= (item.EffectiveSide == WeaponSide.Left ? 1 : -1); //MWC: Side from the Equip Point the item is actually on
                }

                if (storeMode)
                {
                    Animal.Mode_ForceActivate(storeMode.ID, Ability);
                    //MWC — tracked per item so a drop/holster/disable during the Store animation cancels it
                    Cancel_PendingStore(item);
                    C_Stores[item] = this.Delay_Action(item.equipDuration, () => Store_Delayed_End(item, mode));
                    //Set the current equip time to now, so the item can check for the equip duration if needed
                }
                else
                {
                    Item_Unequip_INTERNAL_FAST(item, mode);
                }
                item.CurrentEquipTime = Time.time;
                // Debug.Log("Item_Unequip Delay");
            }
            else
            {
                //Debug.Log("Item_Unequip Unequip_INTERNAL_FAST");
                Item_Unequip_INTERNAL_FAST(item, mode);
                //Set the current equip time to now, so the item can check for the equip duration if needed
                item.CurrentEquipTime = Time.time;
            }
        }

        /// <summary> Instant unequip. <paramref name="store"/> set = it is a Store (Store/Drop animation or input): the Item is then put away with that mode </summary>
        private void Item_Unequip_INTERNAL_FAST(SuperItem item, SIStoreMode? store = null)
        {
            Cancel_PendingStore(item); //MWC — the item is being unequipped right now: a Store still in flight must not run again later

            var wasEquipped = item.IsEquipped; //Drop and Holster route loose/holstered Items through here too: only a real unequip notifies

            // Debug.Log("Unequip_INTERNAL_FAST");
            //MWC: Dual Wielding - clear only the hand parameter of the side the item was actually on (CurrentEquipPoint is still set here)
            var side = item.EffectiveSide;
            if (side == WeaponSide.Left) TryAnimParameter(Hash_ItemLeftHandType, 0);    //Set the Left Hand Parameter on the animator
            if (side == WeaponSide.Right) TryAnimParameter(Hash_ItemRightHandType, 0);  //Set the Right Hand Parameter on the animator
            //remove item from equip points
            var equipPoint = item.CurrentEquipPoint;

            equipPoint?.Clear_Item();

            //MWC — the point is free now: recompute Mirror from what is left. It used to be set only on Equip, so
            //dropping one of two Dual Wielding items left the parameter stuck on the value of the item that is gone.
            UpdateMirrorParameter();
            UpdateReinHands();       //MWC — the hand is free again: it can take the rein back

            item.Unequip(this); //Call the Unequip Item Method from the Item

            //MWC — only a Store puts the Item away here. Drop, Holster and the No Items Hide unequip through this same
            //method and handle the Item themselves: deciding it here for every caller made each Drop holster the
            //Item first (firing OnHolstered) and forced Item_Holster/Hold_Item to guard against re-entering.
            if (store.HasValue) PutAway_INTERNAL(item, store.Value);

            //Notify all the items equipped about the unequip event
            EquipPoints.ForEach(x => x.Item?.VerifyAutoSetActivation(SetActivationType.OnUnequip));

            Debugging($"[<color=yellow>Unequip</color> {item.name}] → [Side: {side}]", "orange"); //MWC: effective side

            //MWC — Equip Point reactions, fired last so they see the completed unequip state (including
            //the [On Store] put-away above). Dynamic Target is the item that was just unequipped.
            equipPoint?.OnUnequip.TryReact(item);
            if (wasEquipped) Notify_Unequipped(item);

            RefreshDefaultItem(); //MWC — a hand just emptied: bring the Default Item back if no weapon is left
        }

        /// <summary> Puts a just-stored (already unequipped) Item away: into its Holster, to the world, or hidden in place </summary>
        private void PutAway_INTERNAL(SuperItem item, SIStoreMode mode)
        {
            switch (mode)
            {
                case SIStoreMode.Drop:
                    Drop_INTERNAL(item);
                    break;

                case SIStoreMode.Hide:
                    //Stays parented to the hand and owned by the Manager; Item_Equip draws it back (Equip_INTERNAL_FAST unhides it)
                    HiddenItems.Add(item);
                    item.gameObject.SetActive(false);
                    Debugging($"[<color=yellow>Store</color> {item.name}] → Hidden", "orange");
                    break;

                default:
                    Item_Holster(item); //No Holster for the Item → Item_Holster drops it
                    break;
            }
        }

        #region Item Colliders — MWC
        /// <summary> Runs an action on every Item the Manager owns right now: equipped, reserved (mid-draw), holstered (every slot), hidden, held and the Default Item. Each Item once </summary>
        public void ForEachOwnedItem(System.Action<SuperItem> action)
        {
            if (action == null) return;

            var seen = new HashSet<SuperItem>();

            void Visit(SuperItem item)
            {
                if (item != null && seen.Add(item)) action(item);
            }

            foreach (var p in EquipPoints)
            {
                if (p == null) continue;
                Visit(p.Item);
                Visit(p.Reserved);
            }

            foreach (var h in holsters)
            {
                if (h == null) continue;
                Visit(h.Item);
                foreach (var stored in h.StoredItems) Visit(stored);
            }

            foreach (var hidden in HiddenItems) Visit(hidden);
            foreach (var held in HoldItems) Visit(held);

            Visit(DefaultItem);
        }

        /// <summary> FALSE: disables the (non-trigger) colliders of every Item the Manager owns (equipped, holstered, hidden, Default). TRUE: restores each one to its authored state ([Disable Colliders On Equip] / [On Holster]). E.g. OFF while dodging so the Items do not push the character around, restored when the dodge ends </summary>
        public virtual void Items_Colliders(bool enable)
        {
            Debugging($"[Items Colliders] {(enable ? "Restored" : "Disabled")}", "orange");

            if (enable) ForEachOwnedItem(item => item.Colliders_Restore()); //Each Item decides: a Shield keeps them ON in the hand, a Rifle keeps them OFF
            else ForEachOwnedItem(item => item.InternalColliders(false));
        }

        /// <summary> Restores the colliders of every owned Item to their authored state (UnityEvent friendly) </summary>
        public void Items_Colliders_Enable() => Items_Colliders(true);

        /// <summary> Disables the colliders of every owned Item (UnityEvent friendly) </summary>
        public void Items_Colliders_Disable() => Items_Colliders(false);
        #endregion

        #region Pending Coroutines (delayed Pick / Draw / Store / Tap / Initialize) — MWC
        //MWC — every delayed step of the Manager used to be a fire-and-forget Delay_Action. Unity does NOT stop a coroutine when
        //the component is disabled (only when the GameObject is deactivated or destroyed, and then silently, with no callback),
        //so a draw/store/pick/initialize callback could land on a disabled Manager, on an item that was dropped or held
        //meanwhile, or never land at all leaving the Equip Point reserved forever. The handles below make every one of them
        //cancellable, and each callback re-validates its own state before doing anything.

        /// <summary> The delayed Initialize (5 frames after OnEnable). Restarted, never stacked, by Initialize </summary>
        private IEnumerator C_Initialize;

        /// <summary> Pending delayed Store (Item_Unequip with an Unequip animation) per equipped item </summary>
        private readonly Dictionary<SuperItem, IEnumerator> C_Stores = new();

        /// <summary> Pending delayed Pick (Pick Duration) per item being collected </summary>
        private readonly Dictionary<SuperItem, IEnumerator> C_Picks = new();

        /// <summary> Pending delayed Release of an Action pressed by <see cref="Action_Tap"/>, per Action ID (with the Item filter it was pressed with) </summary>
        private readonly Dictionary<ItemActionID, (IEnumerator coro, SuperItemID item)> C_Taps = new();

        /// <summary> Delayed end of a Tap: releases the Action input </summary>
        private void Tap_Delayed_End(ItemActionID actionID, SuperItemID itemID)
        {
            C_Taps.Remove(actionID);
            Action_Release(actionID, itemID); //No Active gate on purpose: a pressed input must always find its release
        }

        /// <summary> Cancels the pending release of a Tap. <paramref name="release"/> = release the input right now instead of leaving it pressed </summary>
        private void Cancel_PendingTap(ItemActionID actionID, bool release)
        {
            if (actionID == null) return;
            if (!C_Taps.Remove(actionID, out var tap)) return;

            this.Stop_Action(tap.coro);
            if (release) Action_Release(actionID, tap.item);
        }

        /// <summary> Delayed end of a draw: lands the item on its reserved Equip Point, unless the draw was cancelled meanwhile </summary>
        private void Draw_Delayed_End(SuperItem item, SEquipPoint equipPoint)
        {
            if (equipPoint != null) equipPoint.C_Draw = null;

            if (!Active) return; //Disabled meanwhile: Settle_PendingCoroutines already rolled the draw back

            //The reservation is the draw's ticket: Cancel_PendingDraw (drop, hold, holster, disable) takes it away
            if (item == null || equipPoint == null || equipPoint.Reserved != item)
            {
                Debugging($"[<color=yellow>Equip</color> {(item != null ? item.name : "?")}] draw cancelled before it ended → ignored", "red");
                return;
            }

            Equip_INTERNAL_FAST(item, equipPoint);
        }

        /// <summary> Delayed end of a Store: unequips the item and puts it away with <paramref name="mode"/>, unless it was already released/holstered/re-owned meanwhile </summary>
        private void Store_Delayed_End(SuperItem item, SIStoreMode mode)
        {
            C_Stores.Remove(item);

            if (!Active) return; //Disabled meanwhile: Settle_PendingCoroutines already rolled the store back

            if (item == null || !item.IsEquipped || item.User != this)
            {
                Debugging($"[<color=yellow>Unequip</color> {(item != null ? item.name : "?")}] store cancelled before it ended → ignored", "red");
                return;
            }

            Item_Unequip_INTERNAL_FAST(item, mode);
        }

        /// <summary> Cancels a pending draw of the item: stops its coroutine and frees the Equip Point reservation and the holster [Leaving] mark. The item stays where it is (its holster) </summary>
        private void Cancel_PendingDraw(SuperItem item)
        {
            if (item == null) return;

            bool cancelled = false;

            foreach (var p in EquipPoints)
            {
                if (p == null || p.Reserved != item) continue;

                this.Stop_Action(p.C_Draw);
                p.C_Draw = null;
                p.Reserved = null;
                cancelled = true;
            }

            //MWC: Swap - the item is not leaving its holster anymore, so its Slot must stop counting as free
            foreach (var h in holsters)
                if (h != null && h.Leaving == item) h.Leaving = null;

            //IsEquipping is derived from this stamp: a cancelled draw must not keep refusing equip/unequip requests for the rest of the window
            if (cancelled)
            {
                item.CurrentEquipTime = -999;
                Debugging($"[<color=yellow>Equip</color> {item.name}] pending draw cancelled", "orange");
            }
        }

        /// <summary> Cancels a pending Store of the item (the item stays equipped) </summary>
        private void Cancel_PendingStore(SuperItem item)
        {
            if (item == null) return;
            if (!C_Stores.Remove(item, out var coro)) return;

            this.Stop_Action(coro);
            item.CurrentEquipTime = -999; //IsEquipping must not keep blocking the item for the rest of the store window
            Debugging($"[<color=yellow>Unequip</color> {item.name}] pending store cancelled", "orange");
        }

        /// <summary> Cancels a pending Pick of the item (it stays in the world) </summary>
        private void Cancel_PendingPick(SuperItem item)
        {
            if (item == null) return;
            if (!C_Picks.Remove(item, out var coro)) return;

            if (item.PickedBy == this) item.PickedBy = null; //MWC — the claim dies with the pick, so the item is free for everyone again

            this.Stop_Action(coro);
            Debugging($"[<color=yellow>Pick</color> {item.name}] pending pick cancelled", "orange");
        }

        /// <summary> OnDisable: stops every pending coroutine and rolls each one back to a stable state: a draw in flight goes back to being holstered (reservation freed), a store in flight stays equipped, a tapped Action is released, picks and the delayed Initialize are dropped </summary>
        private void Settle_PendingCoroutines()
        {
            this.Stop_Action(C_Initialize);
            C_Initialize = null;

            //Draws: the item never left its holster (Equip_INTERNAL_FAST does the UnHolster), so freeing the reservation IS the rollback.
            //Not completed instantly on purpose: Active is already false here, so Item_UnHolster/Item_Holster would refuse to run.
            foreach (var p in EquipPoints)
            {
                if (p == null || p.Reserved == null) continue;
                Cancel_PendingDraw(p.Reserved);
            }

            //Stores: the item is still equipped and tracked by its Equip Point, so it simply stays in the hand.
            //Completing it here would clear the point while Item_Holster (Active gate) refuses to store it: an untracked item glued to the hand.
            if (C_Stores.Count > 0)
            {
                var pending = C_Stores.Keys.ToArray(); //Cancel_PendingStore mutates the dictionary
                foreach (var item in pending) Cancel_PendingStore(item);
            }

            if (C_Picks.Count > 0)
            {
                var pending = C_Picks.Keys.ToArray();
                foreach (var item in pending) Cancel_PendingPick(item);
            }

            //Taps: the input was pressed by the Manager itself, so it releases it (Action_Release has no Active gate) rather than leaving the Action held forever
            if (C_Taps.Count > 0)
            {
                var pending = C_Taps.Keys.ToArray();
                foreach (var id in pending) Cancel_PendingTap(id, release: true);
            }
        }
        #endregion

        #region Default Item (Unarmed fallback) — MWC
        /// <summary>True when a Default Item is assigned and currently equipped.</summary>
        public bool DefaultItemActive => DefaultItem != null && DefaultItem.IsEquipped;

        //MWC — the Default Item is hidden while a state/stance/mode requires no items, or the Manager is Locked (reuses the temporal-hold flags)
        private bool DefaultSuppressed => HandsBlocked;

        /// <summary> Reconcile the Default Item with the current hand state: equip it when no weapon is in hand
        /// (and it is not suppressed), unequip it when a weapon is drawn. Idempotent — safe to call from any settle point. </summary>
        public void RefreshDefaultItem()
        {
            if (DefaultItem == null) return;

            var wantDefault = EquippedCount == 0 && !DefaultSuppressed; //No weapon in hand → fists

            if (wantDefault && !DefaultItem.IsEquipped) Equip_DefaultItem();
            else if (!wantDefault && DefaultItem.IsEquipped) Unequip_DefaultItem();
        }

        //MWC — equip the Default Item directly (no equip point): SuperItem.Equip flips IsEquipped and connects its
        //action inputs, so its actions respond exactly like a real weapon. Silent: bypasses the mode-animated Item_Equip.
        private void Equip_DefaultItem()
        {
            if (DefaultItem == null || DefaultItem.IsEquipped) return;

            DefaultItem.gameObject.SetActive(true);

            //MWC — SetActive(true) does nothing while a parent is disabled (e.g. 'Internal Components'): the Item never
            //runs Awake, so its Action Sets stay null. Warn and skip instead of equipping a half-initialized Item.
            if (!DefaultItem.gameObject.activeInHierarchy)
            {
                Debug.LogWarning($"<color=orange><B>[{name}] Default Item [{DefaultItem.name}]</B></color> cannot be equipped: it is <B>inactive in the hierarchy</B> (a parent GameObject is disabled). Enable it to use the Default Item.", DefaultItem);
                return;
            }

            DefaultItem.Equip(this);

            Debugging($"[<color=yellow>Default Item</color> {DefaultItem.name}] → <color=white>Equipped</color> (No weapons)", "orange");
        }

        //MWC — unequip the Default Item directly (no holster/drop): SuperItem.Unequip clears IsEquipped and disconnects input
        private void Unequip_DefaultItem()
        {
            if (DefaultItem == null || !DefaultItem.IsEquipped) return;

            DefaultItem.Unequip(this);

            Debugging($"[<color=yellow>Default Item</color> {DefaultItem.name}] → <color=white>Unequipped</color> (weapon drawn / suppressed)", "orange");
        }
        #endregion

        #region Stance & Strafe (shared character state)
        // Network replicas display attachments/animation without owning controller locomotion.
        [System.NonSerialized] public bool SyncLocomotion = true;
        //MWC — Stance and Strafe belong to the CHARACTER, not to one Item, but SuperItem.Equip/Unequip used to write
        //them per item: Equip snapshotted [wasStrafing] privately and wrote its own value, Unequip restored that
        //private snapshot and called Stance_Reset_To_Default unconditionally. With two Items in the hands
        //(Dual Wielding) the one being put away overwrote the requirement of the one still being held — storing the
        //off-hand Sword turned Strafe off while the main-hand Sword was still asking for it, and reset a Stance that
        //the remaining Item still needed. Both transitions now recompute from everything that is actually equipped.

        /// <summary>Strafe value the character had before the FIRST Item was equipped, restored when the last one leaves</summary>
        private bool noItemsStrafe;

        /// <summary>True while <see cref="noItemsStrafe"/> holds a valid snapshot (i.e. at least one Item is equipped)</summary>
        private bool noItemsStrafeStored;

        /// <summary>True while a Stance requested by an Item is applied, so the reset only runs when this Manager set one</summary>
        private bool itemStanceApplied;

        /// <summary>Recomputes the Animal Stance and Strafe from EVERY equipped Item. <paramref name="preferred"/> (the Item that just changed) wins the Stance when it declares one</summary>
        internal void Sync_StanceAndStrafe(SuperItem preferred)
        {
            if (Animal == null || !SyncLocomotion || Animal.ActiveState == null) return;

            //Strafe is an OR across the equipped Items: while ANY of them asks for it, the character strafes.
            //Nothing equipped → the value the character had before the first Item went in.
            var anyEquipped = false;
            var wantStrafe = false;
            StanceID wantStance = null;

            if (preferred != null && preferred.IsEquipped)
            {
                anyEquipped = true;
                wantStrafe = preferred.strafe.Value;
                wantStance = preferred.Stance; //The Item that just changed wins the Stance when it declares one
            }

            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var other = EquipPoints[i]?.Item;
                if (other == null || other == preferred || !other.IsEquipped) continue;

                anyEquipped = true;
                wantStrafe |= other.strafe.Value;
                wantStance ??= other.Stance;
            }

            //The Default Item sits on no Equip Point (Equip_DefaultItem equips it directly), so it is counted here
            if (DefaultItem != null && DefaultItem != preferred && DefaultItem.IsEquipped)
            {
                anyEquipped = true;
                wantStrafe |= DefaultItem.strafe.Value;
                wantStance ??= DefaultItem.Stance;
            }

            if (anyEquipped)
            {
                //Captured on the 0 → N transition, before any Item has written Strafe, so it really is the "no Items" value
                if (!noItemsStrafeStored)
                {
                    noItemsStrafe = Animal.Strafe;
                    noItemsStrafeStored = true;
                }

                Animal.Strafe = wantStrafe;
            }
            else if (noItemsStrafeStored)
            {
                Animal.Strafe = noItemsStrafe;
                noItemsStrafeStored = false;
            }

            //Stance: an Item with no [Stance] never touches it (Stance_Set(null) is a no-op on the Animal anyway), and
            //the reset only fires when an Item stance was the one applied — exactly the old [if (Stance != null)] rule.
            if (wantStance != null)
            {
                Animal.Stance_Set(wantStance);
                itemStanceApplied = true;
            }
            else if (itemStanceApplied)
            {
                Animal.Stance_Reset_To_Default();
                itemStanceApplied = false;
            }
        }
        #endregion

        //MWC — The Mirror parameter describes the CURRENT equipped state, not the item being equipped:
        //• Two or more items held (Dual Wielding) → false. Those animations are authored for both hands already,
        //  so mirroring them would flip the whole set.
        //• Exactly one item → its own Mirror flag, flipped when it landed on the opposite hand from its Home Side
        //  (its Primary Equip Point, e.g. a Right Hand sword equipped on the Left Hand).
        //• Nothing held → false, so the parameter never stays on after unequipping, holstering or dropping.
        //Reserved points count as held: an item still playing its Equip animation is already on its way to the hand.
        private void UpdateMirrorParameter()
        {
            SuperItem single = null;
            var singleSide = WeaponSide.None;
            int count = 0;

            foreach (var point in EquipPoints)
            {
                var occupant = point?.Occupant; //Equipped item, or the one still playing its Equip animation

                if (occupant == null) continue;

                count++;
                single = occupant;
                singleSide = point.Side; //Side of the point it is (or is landing) on: valid for reserved items too
            }

            var mirrored = false;

            if (count == 1)
            {
                mirrored = single.mirror.Value;

                if (single.HomeSide != WeaponSide.None && singleSide != WeaponSide.None && singleSide != single.HomeSide)
                    mirrored = !mirrored;
            }

            TryAnimParameter(Hash_Mirror, mirrored); //Item Hand parameter on the animator (for Holster/Unholster)
        }

        /// <summary>Sets the Auto Activation External for all the equipped items</summary>
        public virtual void Set_AutoActivation_External()
        {
            foreach (var point in EquipPoints)
            {
                if (point.Item) point.Item.VerifyAutoSetActivation(SetActivationType.External);
            }
        }

        /// <summary>Hands currently taken by the equipped Items. Recomputed by <see cref="UpdateReinHands"/>.</summary>
        public ItemHands HandsInUse { get; private set; }

        /// <summary>True when that hand is taken by an Item, its Active Set or one of its playing Actions.</summary>
        public bool IsHandInUse(WeaponSide side) => side switch
        {
            WeaponSide.Left => HandsInUse.HasFlag(ItemHands.Left),
            WeaponSide.Right => HandsInUse.HasFlag(ItemHands.Right),
            _ => false,
        };

        //MWC — Rein hand-off: a hand taken by an Item cannot hold the rein. Recomputed from the whole equipped state
        //(like UpdateMirrorParameter) instead of toggling per item, so Dual Wielding, swaps and drops always end with
        //the correct hands free. Polarity: TRUE = the rein IS in that hand, FALSE = the hand is busy with an Item
        //(see MRider.IK_Reins and the legacy MWeaponManager.CheckReinHandsEquip). Items on an Equip Point with no
        //Side (head, back…) never take a rein. Safe to call while dismounted: MRider ignores it with no Mount.
        /// <summary>Frees the rein from every hand taken by an Item, and gives it back to the free ones.</summary>
        public virtual void UpdateReinHands()
        {
            HandsInUse = GetHandsInUse(); //Kept up to date even with no Rider: it is a public query of its own

            if (Rider == null) return;

            Rider.ReinLeftHand(!HandsInUse.HasFlag(ItemHands.Left));
            Rider.ReinRightHand(!HandsInUse.HasFlag(ItemHands.Right));

            //  Debugging($"Rein Hands → [In Use: {(HandsInUse == 0 ? "None" : HandsInUse.ToString())}]", "orange");
        }

        //MWC — UNION of everything that can claim a hand. Nothing here can give a hand back, so an empty
        //[Free Hands] on a Set or an Action means "no extra hand needed" and old content behaves as before:
        // • the Equip Point Side  → the hand physically holding the Item (always taken)
        // • the Active Set        → held for as long as that Set is active (e.g. a Bow's Drawn set)
        // • each playing Action   → only while it plays (e.g. Aim pulling the string with the off hand)
        private ItemHands GetHandsInUse()
        {
            ItemHands hands = 0;

            foreach (var p in EquipPoints)
            {
                //Occupant = the equipped Item, or the one still playing its Equip animation (the hand is already reaching for it)
                var item = p?.Occupant;
                if (item == null) continue;

                if (p.Side == WeaponSide.Left) hands |= ItemHands.Left;
                else if (p.Side == WeaponSide.Right) hands |= ItemHands.Right;

                var set = item.ActiveActionSet;
                if (set == null) continue;

                if (set.actions == null) continue;

                foreach (var action in set.actions)
                    if (action != null && action.IsPlaying) hands |= action.FreeHands;
            }

            return hands;
        }

        /// <summary>Sets the Auto Activation Riding for all the equipped items</summary>
        public virtual void Set_AutoActivation_Riding()
        {
            foreach (var point in EquipPoints)
            {
                if (point.Item) point.Item.VerifyAutoSetActivation(SetActivationType.Riding);
            }
        }


        public virtual void Item_Equip(GameObject item) => Item_Equip(item.FindComponent<SuperItem>());
        public virtual void Item_Holster(GameObject item) => Item_Holster(item.FindComponent<SuperItem>());

        public virtual void Item_HolsterAll()
        {
            foreach (var point in EquipPoints)
            {
                if (point.Item) Item_Holster(point.Item);
            }
        }

        public virtual void Item_HolsterAll(IDList<SuperItemID> items, bool include)
        {
            foreach (var point in EquipPoints)
            {
                if (point.Item)
                {
                    if (include && items.Contains(point.Item.ID))
                    {
                        Item_Holster(point.Item);
                    }
                    else if (!include && !items.Contains(point.Item.ID))
                    {
                        Item_Holster(point.Item);
                    }
                }
            }
        }

        public virtual void Item_HolsterAllBut(SuperItem item)
        {
            foreach (var point in EquipPoints)
            {
                if (point.Item && point.Item != item) Item_Holster(point.Item);
            }
        }



        /// <summary> Drops the Item. In the hands with [Instant Drop] OFF → its Drop Mode animation plays first; otherwise (holstered, mid Store/draw) it drops instantly </summary>
        public virtual void Item_Drop(SuperItem item)
        {
            if (!Active) return;
            if (item == null) return;

            //MWC — mid Store/Drop animation (IsEquipping) the old instant drop still wins: Item_Release cancels the pending store
            if (!InstantDrop.Value && item.IsEquipped && !item.IsEquipping)
            {
                if (LockVeto("Drop", item)) return;

                Store_INTERNAL(item, SIStoreMode.Drop);
                return;
            }

            Item_Release(item);
        }

        /// <summary> Drops the Item equipped on that Equip Point (Drop animation unless [Instant Drop]) </summary>
        public virtual void Item_Drop(EquipPointID equipPoint)
        {
            var id = D_EquipPoints != null ? MapEquipPoint(equipPoint) : equipPoint; //MWC — Equip Point Equivalents
            var point = EquipPoints.FirstOrDefault(x => x.ID == id);
            if (point != null && point.Item != null)
            {
                Item_Drop(point.Item);
            }
        }

        public virtual void Item_Drop(HolsterID holsterID)
        {
            var holster = holsters.FirstOrDefault(x => x.ID == holsterID);
            if (holster != null && holster.Item != null)
            {
                Item_Release(holster.Item);
            }
        }

        public virtual void Pick_and_Holster(SuperItem item)
        {
            if (!Active) return;
            if (item == null) return;

            Item_Holster(item); //Add the item to the holster
        }

        public virtual void Pick_and_Equip(SuperItem item)
        {
            if (!Active) return;
            if (item == null) return;

            Item_Equip(item); //Add the item to the holster
        }

        #region AI / External API
        //MWC — Public door for the AI Brain (Super Item Tasks/Decisions) and external code. Actions are driven through
        //ActivateOnInput, the very same path a real input press takes (OnActionPressed reaction, Toggle, Fast-Weapons yield),
        //so an NPC and the player get identical behaviour.

        /// <summary>Finds an Action by ID on the equipped Items (or the Default Item). Null Item ID = first equipped Item that has the Action</summary>
        public bool TryGet_Action(ItemActionID actionID, out SuperItem item, out ItemAction action, SuperItemID itemID = null)
        {
            item = null;
            action = null;
            if (actionID == null) return false;

            //Two equipped Items can answer the same Action ID (the Sword and the Shield both have a [Block] and a
            //[Parry]). Ranking, best first:
            //  2 - the Action already owns the input (playing, or its input still pressed): a Release must reach the
            //      very Item that received the Press
            //  1 - the Action can start right now: Conditions, Rate, Blocked list, Master rule and the Manager gate
            //      all pass. The Goblin's Sword Block has a 'Shield not equipped' Owner Condition, so with the
            //      Shield equipped the press has to reach the Shield's Block instead of dying on the Sword's
            //  0 - first match, the fallback when nothing can start
            SuperItem bestItem = null;
            ItemAction bestAction = null;
            int bestRank = -1;

            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var point = EquipPoints[i];
                if (point == null || point.Item == null) continue;
                if (itemID != null && point.Item.ID != itemID) continue;

                if (TryGet_Action(point.Item, actionID, out var found))
                    Consider(point.Item, found);
            }

            //The Default Item (fists) is equipped directly, with no Equip Point
            if (DefaultItemActive && (itemID == null || DefaultItem.ID == itemID) && TryGet_Action(DefaultItem, actionID, out var defaultFound))
                Consider(DefaultItem, defaultFound);

            item = bestItem;
            action = bestAction;
            return action != null;

            void Consider(SuperItem candidateItem, ItemAction candidate)
            {
                var rank = candidate.IsPlaying || candidate.InputValue ? 2
                         : candidate.CanActivate(candidateItem, candidateItem.ActiveActionSet) ? 1 : 0;

                if (rank <= bestRank) return;
                bestRank = rank;
                bestItem = candidateItem;
                bestAction = candidate;
            }
        }

        /// <summary>Finds an Action by ID on the Item's Active Action Set (or its Action Equivalent when the Set has no such ID)</summary>
        private bool TryGet_Action(SuperItem item, ItemActionID actionID, out ItemAction action)
        {
            action = null;
            var set = item.ActiveActionSet;
            if (set == null || set.actions == null) return false;

            actionID = ResolveActionID(item, actionID); //MWC — Action Equivalents (e.g. Wolf: Attack → Attack Light)

            for (int a = 0; a < set.actions.Count; a++)
            {
                var act = set.actions[a];
                if (act != null && act.ActionID == actionID) { action = act; return true; }
            }
            return false;
        }

        //MWC — [Cross Item Priority] the cross-Item half of ItemAction.ManagerAllowsActivation. Only STRICTLY higher
        //priorities are considered, so equal ones both start (dual wielding swings both hands on one press), and only
        //the same Action ID competes: a press that no other Item answers is never arbitrated.
        /// <summary>True when another equipped Item answers this Action ID with a strictly higher Priority Action that is playing, or could start right now</summary>
        internal bool TryGet_HigherPriorityAction(ItemActionID actionID, SuperItem asking, int priority,
            out SuperItem winnerItem, out ItemAction winner)
        {
            winnerItem = null;
            winner = null;

            if (!CrossItemPriority || probingPriority || actionID == null || !Active) return false;

            //Locals, not the out parameters: a local function cannot capture them
            SuperItem bestItem = null;
            ItemAction best = null;

            probingPriority = true; //asking a candidate if it can start must not re-enter this check

            try
            {
                for (int i = 0; i < EquipPoints.Count; i++)
                {
                    var point = EquipPoints[i];
                    if (point == null || point.Item == null) continue;

                    Consider(point.Item);
                }

                //The Default Item (fists) is equipped with no Equip Point, so the loop above never sees it
                if (DefaultItemActive) Consider(DefaultItem);
            }
            finally { probingPriority = false; }

            winnerItem = bestItem;
            winner = best;

            return winner != null;

            void Consider(SuperItem candidateItem)
            {
                if (candidateItem == asking) return;                              //the Item's own Actions are arbitrated by item.MainAction
                if (!TryGet_Action(candidateItem, actionID, out var candidate)) return;
                if (candidate.Priority <= priority) return;                       //equal Priorities both start
                if (best != null && candidate.Priority <= best.Priority) return;  //keep the best one found so far

                //A playing winner keeps the veto up; otherwise it has to be able to start RIGHT NOW to claim the press
                if (!candidate.IsPlaying && !candidate.CanActivate(candidateItem, candidateItem.ActiveActionSet)) return;

                bestItem = candidateItem;
                best = candidate;
            }
        }

        /// <summary>Presses the input of an Action by ID (AI / external). Falls back to the [Fast Weapons] draw when no equipped Item answers. Returns true if the Action is playing afterwards</summary>
        public virtual bool Action_Press(ItemActionID actionID, SuperItemID itemID)
        {
            if (!Active || actionID == null) return false;
            if (LockVeto($"Action Press [{actionID.name}]")) return false; //Locked: nothing is in the hands and nothing may be drawn

            var playing = false;

            if (TryGet_Action(actionID, out var item, out var action, itemID))
            {
                action.ActivateOnInput(true, item, item.ActiveActionSet);
                playing = action.IsPlaying;
            }
            //Nothing equipped answers this ID: let [Fast Weapons] draw a holstered candidate and start its Action
            else if (UseFastWeapons && (itemID == null || IsItemHolstered(itemID)))
            {
                OnFastWeaponInput(actionID, true, itemID); //MWC — the Item ID filter follows the request into the draw
                playing = TryGet_Action(actionID, out _, out action, itemID) && action.IsPlaying;
            }

            Notify_ActionInput(actionID, true); //After the Item handled it, so [Action Is Playing] sees the started Action
            return playing;
        }

        /// <summary>Releases the input of an Action by ID (AI / external). Returns true if an Action received the release</summary>
        public virtual bool Action_Release(ItemActionID actionID, SuperItemID itemID)
        {
            if (actionID == null) return false;

            var released = false;

            if (TryGet_Action(actionID, out var item, out var action, itemID))
            {
                action.ActivateOnInput(false, item, item.ActiveActionSet);
                released = true;
            }

            if (Active) Notify_ActionInput(actionID, false); //Same Released event/reactions a real input release fires, after the Item handled it
            return released;
        }

        /// <summary>Presses the input of an Action by ID and releases it after [holdTime] seconds (zero = on the next frame). The pending release is tracked per Action: a new Tap replaces it, and disabling the Manager releases it instead of leaving the input pressed. Returns true if the Action was pressed</summary>
        public virtual bool Action_Tap(ItemActionID actionID, SuperItemID itemID, float holdTime = 0f)
        {
            if (actionID == null) return false;

            Cancel_PendingTap(actionID, release: false); //Re-tapped before the release: the press below re-arms the input, one release is enough

            if (!Action_Press(actionID, itemID)) return false;

            var coro = holdTime > 0
                ? this.Delay_Action(holdTime, () => Tap_Delayed_End(actionID, itemID))
                : this.Delay_Action(() => Tap_Delayed_End(actionID, itemID));

            //Delay_Action returns null only when the Manager is inactive, and Action_Press already refused that case
            if (coro != null) C_Taps[actionID] = (coro, itemID);

            return true;
        }

        //Single-parameter versions for UnityEvents/MEvents (mobile buttons, reactions): a method with an optional
        //parameter still counts as a 2-parameter method for UnityEvent, so it can never be picked in the inspector.

        /// <summary>Presses the input of an Action by ID on any equipped Item (UnityEvent friendly version of Action_Press)</summary>
        public virtual void Action_Press(ItemActionID actionID) => Action_Press(actionID, null);

        /// <summary>Releases the input of an Action by ID on any equipped Item (UnityEvent friendly version of Action_Release)</summary>
        public virtual void Action_Release(ItemActionID actionID) => Action_Release(actionID, null);

        /// <summary>Presses and releases the input of an Action by ID on any equipped Item (UnityEvent friendly version of Action_Tap)</summary>
        public void Action_Tap(ItemActionID actionID) => Action_Tap(actionID, null, 0f);

        /// <summary>Force-stops a playing Action by ID on every equipped Item. Null ID = interrupt every Action of every equipped Item</summary>
        public virtual void Action_Interrupt(ItemActionID actionID = null)
        {
            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var point = EquipPoints[i];
                if (point == null || point.Item == null) continue;
                Action_Interrupt(point.Item, actionID);
            }

            if (DefaultItemActive) Action_Interrupt(DefaultItem, actionID);
        }

        /// <summary>Interrupts one Item: the Action with the given ID, or its whole Active Set when the ID is null</summary>
        private void Action_Interrupt(SuperItem item, ItemActionID actionID)
        {
            //MWC — force: this is the public force-stop, never an Action interrupting itself through its own events.
            //Without it a press and its cancellation in the SAME frame left the Action latched: the input was cleared
            //here and the Interrupt below was refused by the start-frame guard.
            if (actionID == null)
            {
                item.ActionSet_Interrupt("AI", force: true);
            }
            else if (TryGet_Action(item, actionID, out var action) && action.IsPlaying)
            {
                action.InputValue = false;
                action.Interrupt(item, item.ActiveActionSet, force: true);
            }
        }

        /// <summary>True while any Equip Point has an animated draw/store in flight (an AI should wait before using the Item)</summary>
        public bool IsEquipping => AnyEquipInFlight();

        /// <summary>Free Items currently inside the Interactor's trigger (not equipped, not holstered). Read-only</summary>
        public IReadOnlyCollection<SuperItem> FocusedItems => m_FocusedItems;

        /// <summary>Number of equipped Items with this ID (two swords dual wielded = 2). Null ID = 0</summary>
        public int Count_Equipped(SuperItemID ID)
        {
            if (ID == null) return 0;
            int count = 0;

            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var point = EquipPoints[i];
                if (point != null && point.Item != null && point.Item.ID == ID) count++;
            }
            return count;
        }

        /// <summary>Number of Items with this ID stored in the holsters (every slot counts, not only the head Item). Null ID = 0</summary>
        public int Count_Holstered(SuperItemID ID)
        {
            if (ID == null) return 0;
            int count = 0;

            for (int h = 0; h < holsters.Count; h++)
            {
                var holster = holsters[h];
                if (holster == null || holster.StoredItems == null) continue;

                for (int s = 0; s < holster.StoredItems.Count; s++)
                {
                    var item = holster.StoredItems[s];
                    if (item != null && item.ID == ID) count++;
                }
            }
            return count;
        }

        /// <summary>Number of Items with this ID equipped OR holstered</summary>
        public int Count_Owned(SuperItemID ID) => Count_Equipped(ID) + Count_Holstered(ID);
        #endregion

        /// <summary> Store the current Damager Profile sent by the Animator  </summary>
        public int CurrentDamagerProfile { get; set; }

        /// <summary> Store the current Damager ID sent by the Animator  </summary>
        public int CurrentDamagerID { get; set; }

        /// <summary> Activate the Damager of a Weapon. E.g. the Attack Trigger of a Melee Weapon    </summary>
        public virtual void ActivateDamager(int ID, int prof)
        {
            CurrentDamagerProfile = prof; //Store the Current Profile
            CurrentDamagerID = ID;

            foreach (var point in EquipPoints)
            {
                var item = point.Item;
                if (item == null || item.m_Triggers == null) continue;

                foreach (var p in item.m_Triggers)
                {
                    //MWC — skip empty slots: an unassigned entry in the Triggers array (array size bumped by hand, or a
                    //Trigger Proxy deleted from the item) threw a NullReferenceException here and killed the attack.
                    //Every other Triggers loop in the Super Item code already guards this way.
                    if (p == null) continue;

                    switch (ID)
                    {
                        case 0: p.Activate(false); break;                       //Disable all the Trigger Proxies
                        case -1: p.Activate(true); break;                       //Enable all the Trigger Proxies
                        default: if (p.ID == ID) p.Activate(true); break;       //Enable only the ones with the correct ID
                    }
                }
            }
        }

        // private int LastAttackTriggerHash;
        public virtual void DamagerAnimationStart(int hash) { }


        protected void ConnectHolsterInput(bool connect)
        {
            if (InputSource == null) return;

            for (int i = 0; i < holsters.Count; i++)
            {
                var a = holsters[i];

                if (connect)
                {
                    ////Very important to use the same listener, so it can be added or removed.
                    if (a.InputListener == null)
                    {
                        a.InputListener = (value) => Item_Equip_From_Holster(a.ID, value);
                        InputSource.ConnectInput(a.Input.Value, a.InputListener);
                        // Debug.Log($"CONECT: {a.Input.Value} ");
                    }
                }
                else
                {
                    ////Very important to use the same listener, so it can be added or removed.
                    if (a.InputListener != null)
                    {
                        InputSource.DisconnectInput(a.Input.Value, a.InputListener);
                        a.InputListener = null;
                    }
                }
            }
        }

        public virtual void DamagerAnimationEnd(int hash) { }

        #region Fast Weapons (MWC)
        //MWC — [Fast Weapons]: holstered items have NO input subscriptions (Input_Connection only runs on Equip), so the
        //Manager listens once per ActionsInput row and routes a press to the best holstered [Fast Equip] candidate.
        //Arbitration with EQUIPPED actions is order-independent: EquippedActionHandles mirrors the exact yield rule the
        //equipped Action's own StartAction gate applies (ItemAction.YieldsToFastEquip), so whichever listener fires
        //first, the outcome is the same.

        /// <summary> [Fast Weapons] One stable listener per ActionsInput row, so they can be disconnected again </summary>
        private UnityAction<bool>[] fastWeaponListeners;

        /// <summary> [Fast Weapons] Connects one manager-level listener per ActionsInput row (stays connected; the router checks the toggle) </summary>
        protected void ConnectFastWeaponInputs(bool connect)
        {
            if (InputSource == null) return; //No input source, no fast weapons

            if (connect)
            {
                fastWeaponListeners ??= new UnityAction<bool>[ActionsInput.Count];

                for (int i = 0; i < ActionsInput.Count; i++)
                {
                    var row = ActionsInput[i];
                    if (row == null || row.ID == null) continue;

                    if (fastWeaponListeners[i] == null) //MWC — same stable-listener pattern as ConnectHolsterInput
                    {
                        var actionID = row.ID; //MWC — capture the row's ID, not the loop variable
                        fastWeaponListeners[i] = (value) => OnFastWeaponInput(actionID, value);
                        InputSource.ConnectInput(row.value.Value, fastWeaponListeners[i]);
                    }
                }
            }
            else if (fastWeaponListeners != null)
            {
                for (int i = 0; i < fastWeaponListeners.Length && i < ActionsInput.Count; i++)
                {
                    if (fastWeaponListeners[i] != null)
                    {
                        InputSource.DisconnectInput(ActionsInput[i].value.Value, fastWeaponListeners[i]);
                        fastWeaponListeners[i] = null;
                    }
                }
            }
        }

        /// <summary> [Fast Weapons] entry point: on press, fast-equips the best holstered candidate for this Action ID and starts its Action </summary>
        private void OnFastWeaponInput(ItemActionID actionID, bool value, SuperItemID itemID = null)
        {
            if (!value) return;                 //press only; the release routes to the (now equipped) item's own input
            if (!Active || !UseFastWeapons) return;

            //MWC — 'itemID' comes from a filtered Action_Press. It used to be dropped here, so the unfiltered search
            //could pick a DIFFERENT holstered Item: asking for the Fireball's [Aim] drew the Bow and started ITS Aim.
            var candidate = FindFastEquipCandidate(actionID, null, out var candidateAction, itemID);
            if (candidate == null) return;      //MWC — nothing holstered wants this input

            //MWC — every veto below used to be a silent early return placed BEFORE the candidate search, so a refused
            //fast draw left nothing at all in the Console: the press simply did nothing and there was no way to tell
            //which gate ate it. They are evaluated here instead (all of them are pure reads) so the log only talks
            //about presses a holstered Item actually wanted.
            var veto =
                HandsBlocked ? "the hands are blocked (No Items State/Stance/Mode, or Locked)" :
                PlayingItemOwnsPress(actionID) ? "an Item mid-Action owns this press (same Action ID, or the same physical button)" :
                AnyEquipInFlight() ? "an animated draw/store is still in flight" :
                !CheckActionConditions() ? "the Manager Action Conditions are false" :
                EquippedActionHandles(actionID, candidateAction.fastEquip.Value)
                    ? $"an equipped Action owns this press (its [Fast Equip] is not below this candidate's [{candidateAction.fastEquip.Value}])" : null;

            if (veto != null)
            {
                Debugging($"[Fast Weapons] Fast Equip [{candidate.name}] → Action ID [{actionID.name}] SKIPPED: {veto}", "yellow");
                return;
            }

            FastEquipAndStart(candidate, actionID);
        }

        /// <summary> [Fast Weapons] True while any Equip Point has a draw/store in flight (Reserved pending or occupant still IsEquipping) </summary>
        private bool AnyEquipInFlight()
        {
            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var point = EquipPoints[i];
                if (point == null) continue;
                if (point.Reserved != null) return true;                    //MWC — an animated draw is pending
                if (point.Item != null && point.Item.IsEquipping) return true; //MWC — also covers the Store window
            }
            return false;
        }

        /// <summary> [Fast Weapons] Best HOLSTERED item whose Active Set has a [Fast Equip] Action on this ID (null when none). 'ignore' excludes the asking item, 'itemID' restricts the search to one Super Item ID </summary>
        internal SuperItem FindFastEquipCandidate(ItemActionID actionID, SuperItem ignore, out ItemAction candidateAction, SuperItemID itemID = null)
        {
            candidateAction = null;
            SuperItem candidate = null;

            if (actionID == null) return null;

            for (int h = 0; h < holsters.Count; h++)
            {
                var holster = holsters[h];
                if (holster == null || holster.StoredItems == null) continue;

                for (int s = 0; s < holster.StoredItems.Count; s++)
                {
                    var item = holster.StoredItems[s];

                    if (item == null || item == ignore) continue;
                    if (itemID != null && item.ID != itemID) continue; //MWC — filtered request (Action_Press by Item ID): only that Item may answer
                    if (!item.IsInHolster || item.IsEquipped) continue;                 //MWC — must be stored, not in a hand
                    if (!item.enabled || !item.gameObject.activeInHierarchy) continue;  //MWC — mirrors PreRequisites; skips hidden items

                    //MWC — a BLOCKED Action can never be a Fast Equip candidate. This gate was missing entirely, and
                    //it is the one gate that cannot be applied afterwards: FastEquipAndStart equips the item FIRST and
                    //only then calls ActivateOnInput, where PreRequisites finally rejects the blocked Action. The press
                    //correctly reported failure and nothing played — but the weapon was already drawn and out of its
                    //holster, with no way to undo it. Mirrors the PreRequisites check, which is per Item + Action ID.
                    var itemActionID = ResolveActionID(item, actionID); //MWC — Action Equivalents (e.g. Wolf: Attack → Attack Light)

                    if (item.BlockedActions.IsBlocked(itemActionID)) continue;

                    var set = item.ActiveActionSet;
                    if (set == null || !set.Active) continue;

                    for (int a = 0; a < set.actions.Count; a++)
                    {
                        var action = set.actions[a];

                        if (action == null || action.ActionID != itemActionID) continue;
                        if (action.fastEquip.Value <= 0 || !action.Active) continue; //MWC — 0 = not a fast equip Action
                        if (action.masterIndex > 0) continue;   //MWC — Secondary Actions are never candidates
                        if (action.CheckRate()) continue;       //MWC — respect the cooldown: never draw without acting

                        //MWC — highest [Fast Equip] value wins; ties by holster order, then StoredItems order
                        if (candidateAction == null || action.fastEquip.Value > candidateAction.fastEquip.Value)
                        {
                            candidate = item;
                            candidateAction = action;
                        }
                    }
                }
            }
            return candidate;
        }

        /// <summary> [Fast Weapons] True when a holstered [Fast Equip] candidate would take over this press (used by the [Valid On Fast Weapons] yield) </summary>
        //MWC — every candidate has fastEquip > 0 (FindFastEquipCandidate rejects 0), so "a candidate exists" and
        //"the best rank is above zero" are the same question
        internal bool HasFastEquipCandidate(ItemActionID actionID, SuperItem ignore = null)
            => BestFastEquipRank(actionID, ignore) > 0;

        /// <summary> [Fast Weapons] True when a holstered candidate outranks an EQUIPPED Action's own [Fast Equip] value (ties go to the Item in hand) </summary>
        internal bool OutrankedByFastEquip(ItemActionID actionID, int myFastEquip, SuperItem ignore = null)
            => BestFastEquipRank(actionID, ignore) > myFastEquip;

        /// <summary> [Fast Weapons] Highest [Fast Equip] value among the holstered candidates answering this press (0 = none) </summary>
        internal int BestFastEquipRank(ItemActionID actionID, SuperItem ignore = null)
        {
            var best = 0;

            if (FindFastEquipCandidate(actionID, ignore, out var onThisID) != null)
                best = onThisID.fastEquip.Value;

            //MWC — one physical button can drive SEVERAL input rows (e.g. 'Action1', 'Light Attack' and 'Heavy Attack'
            //are three rows on Left Click), and each row carries a different Action ID. A candidate waiting on any of
            //them takes over this very press, so this Action has to yield to it too — otherwise a Bow's [Quick Fire]
            //on 'Action1' fires while the Sword's [Fast Equip] on 'Light Attack' is drawing from the same click.
            var myInput = InputOf(actionID);
            if (string.IsNullOrEmpty(myInput)) return best;

            for (int i = 0; i < ActionsInput.Count; i++)
            {
                var row = ActionsInput[i];

                if (row == null || row.ID == null || row.ID == actionID) continue;
                if (!SamePhysicalInput(myInput, row.value.Value)) continue;

                if (FindFastEquipCandidate(row.ID, ignore, out var onRow) != null && onRow.fastEquip.Value > best)
                    best = onRow.fastEquip.Value;
            }
            return best;
        }

        /// <summary> [Fast Weapons] Input name mapped to an Action ID on the Actions Input table (null when it has no row) </summary>
        private string InputOf(ItemActionID actionID)
        {
            if (actionID == null) return null;

            for (int i = 0; i < ActionsInput.Count; i++)
            {
                var row = ActionsInput[i];
                if (row != null && row.ID == actionID) return row.value.Value;
            }

            //MWC — Action Equivalents: an Item Action with no row of its own is driven by the Input Action that maps to it
            if (ItemOffsets != null)
            {
                for (int i = 0; i < ActionsInput.Count; i++)
                {
                    var row = ActionsInput[i];
                    if (row != null && ItemOffsets.IsActionEquivalent(row.ID, actionID)) return row.value.Value;
                }
            }
            return null;
        }

        /// <summary> [Fast Weapons] True when two input names are driven by the SAME physical control (duplicated input rows) </summary>
        private bool SamePhysicalInput(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (a == b) return true;

            //MWC — an Input Source that cannot report its physical controls falls back to plain name comparison
            if (InputSource is not IInputPhysicalID physical) return false;

            var idA = physical.PhysicalInputID(a);

            return !string.IsNullOrEmpty(idA) && idA == physical.PhysicalInputID(b);
        }

        /// <summary> [Fast Weapons] True when an Item that is mid-Action answers this press itself (its own Action, or a Secondary of the one playing) </summary>
        //MWC — this replaced a blanket [IsAnyActionPlaying] veto. That veto existed for [Aim] → [Fire]: Fire is a
        //Secondary, so EquippedActionHandles skips it and a holstered candidate listening on the Fire input would
        //have stolen the shot mid draw. But it also refused EVERY fast draw while anything at all was playing, which
        //is the whole point of Fast Weapons — right click to drop the Sword mid swing and come up aiming the Bow.
        //The Sword's [Attack Light] has no [Aim] Action, so it has no claim on that press and no longer keeps it.
        private bool PlayingItemOwnsPress(ItemActionID actionID)
        {
            var myInput = InputOf(actionID);

            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var point = EquipPoints[i];
                if (point != null && ItemOwnsPress(point.Item, actionID, myInput)) return true;
            }

            //MWC — the Default Item (fists) is equipped with no Equip Point, so the loop above never sees it
            if (DefaultItemActive && ItemOwnsPress(DefaultItem, actionID, myInput)) return true;

            return false;
        }

        /// <summary> [Fast Weapons] True when this busy Item answers the press: the Action it is playing runs off the SAME physical button, or it has an Action on this very ID </summary>
        private bool ItemOwnsPress(SuperItem item, ItemActionID actionID, string myInput)
        {
            if (item == null || !item.enabled || !item.gameObject.activeInHierarchy) return false;

            actionID = ResolveActionID(item, actionID); //MWC — Action Equivalents
            if (item.BlockedActions.IsBlocked(actionID)) return false;

            var set = item.ActiveActionSet;
            if (set == null || !set.Active) return false;

            var busy = false;

            for (int a = 0; a < set.actions.Count; a++)
            {
                var action = set.actions[a];
                if (action == null || !action.IsPlaying) continue;

                busy = true;

                //MWC — one physical button drives SEVERAL input rows ('Action1' on Press, 'Attack Light' on Up), each
                //carrying a different Action ID. The Dagger's attack starts on the DOWN of Left Click, and a holstered
                //Sword's [Fast Equip] on 'Attack Light' answers the UP of that SAME click: one click, two attacks.
                //While an Item is mid-Action the whole button is its own, whichever row the next edge arrives on.
                if (SamePhysicalInput(myInput, InputOf(action.ActionID))) return true;
            }

            if (!busy) return false; //MWC — not busy: this Item has no claim on the press

            for (int a = 0; a < set.actions.Count; a++)
            {
                var action = set.actions[a];

                //MWC — Secondaries are INCLUDED here, unlike EquippedActionHandles: what makes a Secondary
                //reachable is its Master already playing, and a playing Action is exactly what was found above
                if (action != null && action.ActionID == actionID && action.Active) return true;
            }
            return false;
        }

        /// <summary> [Fast Weapons] True when an EQUIPPED item (incl. the Default Item) has a connectable Action on this ID that outranks the holstered candidate </summary>
        private bool EquippedActionHandles(ItemActionID actionID, int candidateRank)
        {
            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var point = EquipPoints[i];
                if (point != null && ItemHandles(point.Item, actionID, candidateRank)) return true;
            }

            //MWC — the Default Item (fists) is equipped with no Equip Point, so the loop above never sees it
            if (DefaultItemActive && ItemHandles(DefaultItem, actionID, candidateRank)) return true;

            return false;

            bool ItemHandles(SuperItem item, ItemActionID actionID, int candidateRank)
            {
                if (item == null || !item.enabled || !item.gameObject.activeInHierarchy) return false;

                actionID = ResolveActionID(item, actionID); //MWC — Action Equivalents

                var set = item.ActiveActionSet;
                if (set == null || !set.Active) return false;
                if (item.BlockedActions.IsBlocked(actionID)) return false;

                for (int a = 0; a < set.actions.Count; a++)
                {
                    var action = set.actions[a];

                    if (action == null || action.ActionID != actionID || !action.Active) continue;
                    if (action.primaryHandOnly.Value && !item.IsOnPrimaryPoint) continue;    //MWC — never subscribed anyway
                    if (action.masterIndex > 0) continue;                                     //MWC — a Secondary needs its playing Master; PlayingItemOwnsPress is what covers those
                    if (!action.validOnFastWeapons.Value) continue;                           //MWC — the yield rule mirrored (see ItemAction.YieldsToFastEquip)

                    //MWC — [Fast Equip] is ONE ranked scale across equipped and holstered: an Action in hand that the
                    //candidate outranks gives up the press (a Dagger attack at [0] vs a holstered Sword attack at [2]).
                    //A tie keeps the press in hand, so the common 0-vs-no-candidate case is unchanged
                    if (action.fastEquip.Value < candidateRank) continue;

                    return true; //MWC — this equipped Action will consume the press
                }
                return false;
            }
        }

        /// <summary> [Fast Weapons] Instantly equips the item (evicting any conflicting item instantly too) and starts its matching Action </summary>
        private void FastEquipAndStart(SuperItem item, ItemActionID actionID)
        {
            Debugging($"[Fast Weapons] Fast Equip [{item.name}] → Action ID [{actionID.name}]", "cyan");

            Item_Equip(item, Fast: true);       //MWC — instant draw; conflicting items in the hand are stored instantly

            if (!item.IsEquipped) return;       //MWC — parked by a NoItemsBlocked race, or no free Equip Point

            //MWC — re-resolve on the POST-equip Active Set: Equip_INTERNAL_FAST re-syncs the auto Sets and may have swapped it
            var set = item.ActiveActionSet;
            if (set == null) return;

            actionID = ResolveActionID(item, actionID); //MWC — Action Equivalents, resolved on the post-equip Set

            //MWC — same rule the candidate search used: the highest [Fast Equip] value on this Action ID
            ItemAction toStart = null;

            for (int a = 0; a < set.actions.Count; a++)
            {
                var action = set.actions[a];

                if (action == null || action.ActionID != actionID) continue;
                if (action.fastEquip.Value <= 0 || action.masterIndex > 0) continue;

                if (toStart == null || action.fastEquip.Value > toStart.fastEquip.Value) toStart = action;
            }

            //MWC — manual press; the physical release arrives via the input the Item just connected on Equip
            toStart?.ActivateOnInput(true, item, set);
        }
        #endregion


        #region Riding Connections
        //MWC — Every Rider notification (Start/End Mount, Start/End Dismount, Mount Trigger, Call Mount) re-checks the
        //Action Sets listening to [Riding]. The Set's own AutoActivationOn/Off conditions do the filtering
        //(e.g. [Riding/Rider is Riding]), so one flag covers the whole mount cycle.
        private void GetRiderStatus(RiderAction action)
        {
            Debugging($"Rider Status [{action}]", "orange");

            //MWC — the Mount only exists from the moment the Rider takes it, so the rein state is re-applied here:
            //items equipped on foot never reached MRider (it ignores the calls with no Mount).
            UpdateReinHands();

            Set_AutoActivation_Riding();
        }

        #endregion

        #region Animal Connections

        /// <summary>Re-reads the No Items [State|Stance|Mode] blocks from the Animal's CURRENT status, instead of waiting for the next change event</summary>
        //MWC — OnEnable only SUBSCRIBES to Mode Start/End, State and Stance. An event that already fired is never
        //replayed to a late listener, so a Manager enabled while a No Items Mode was running saw NoItemsBlocked false
        //and drew its starting Item straight into the blocked hands. The same recompute also clears the mirror case:
        //the counter survives a disable, so a Mode that ENDED while the Manager was off (its Mode End never arrived)
        //used to block the hands forever after the next enable.
        private void Sync_NoItemsBlocks()
        {
            if (Animal == null) return;

            var wasBlocked = NoItemsBlocked;

            //Recomputed, never incremented: only the Animal's live status decides, so this is idempotent on re-enable
            NoItemsEquippedMode =
                Animal.IsPlayingMode && Animal.ActiveMode != null && NoItemsInMode.Check(Animal.ActiveMode.ID) ? 1 : 0;

            HoldByState = NoItemsInState.Check(Animal.ActiveStateID);
            HoldByStance = NoItemsInStance.Check(Animal.Stance);

            //A block ended while we were disabled: bring back whatever is still parked in the Hold list.
            //Nothing is HELD from here on purpose: the Equip Points may still carry authored PREFAB references that
            //only the delayed Initialize instantiates, and Item_Equip already parks them when the block IS set.
            if (wasBlocked && !NoItemsBlocked) Temporal_Release_Items();
        }

        //The Default Item (Unarmed) is equipped with NO Equip Point, so the loops below never saw it: its Actions never
        //heard Mode Start/End, State or Stance changes. A [Charged] Block on the fists stayed [Playing] forever after the
        //release, because the Mode End that ends a Charged Action is delivered through this path.
        private void OnModeStart(int mode, int ability)
        {
            foreach (var p in EquipPoints)
                if (p.Item != null) p.Item.OnModeStart(mode, ability);

            if (DefaultItemActive) DefaultItem.OnModeStart(mode, ability);

            if (NoItemsInMode.Check(mode))
            {
                NoItemsEquippedMode++;
                Temporal_Hold_Items();
            }
        }



        private void OnModeEnd(int mode, int ability)
        {
            foreach (var p in EquipPoints)
                if (p.Item != null) p.Item.OnModeEnd(mode, ability);

            if (DefaultItemActive) DefaultItem.OnModeEnd(mode, ability);

            if (NoItemsInMode.Check(mode))
            {
                //MWC — clamped: a Mode End without a matching Mode Start (manager enabled mid-mode, mode force-exited
                //twice…) used to drive the counter negative, and then NO release ever brought the items back.
                NoItemsEquippedMode = Mathf.Max(0, NoItemsEquippedMode - 1);
                Temporal_Release_Items();
            }
        }

        private bool HoldByState;
        private bool HoldByStance;

        private void OnStateChange(int state)
        {
            foreach (var p in EquipPoints)
                if (p.Item != null) p.Item.OnStateChange(state);

            if (DefaultItemActive) DefaultItem.OnStateChange(state);

            if (NoItemsInState.Check(state))
            {
                HoldByState = true;
                Temporal_Hold_Items();
            }
            else if (HoldByState)
            {
                HoldByState = false;
                Temporal_Release_Items();
            }
        }

        private void OnStanceChange(int stance)
        {
            foreach (var p in EquipPoints)
                if (p.Item != null) p.Item.OnStanceChange(stance);

            if (DefaultItemActive) DefaultItem.OnStanceChange(stance);

            if (NoItemsInStance.Check(stance))
            {
                HoldByStance = true;
                Temporal_Hold_Items();
            }
            else if (HoldByStance)
            {
                HoldByStance = false;
                Temporal_Release_Items();
            }
        }



        private void AnimatorIKAction(Animator animator, float GlobalWeight, float deltatime, int LayerIndex)
        {
            foreach (var p in EquipPoints)
                if (p.Item != null) p.Item.AnimatorIKAction(animator, GlobalWeight, deltatime, LayerIndex);
        }

        private void LateUpdateAction(Animator animator, float GlobalWeight, float deltatime)
        {
            foreach (var p in EquipPoints)
                if (p.Item != null) p.Item.LateUpdateAction(animator, GlobalWeight, deltatime);
        }

        /// <summary>Fires the Action input notifications queued this frame, now that every Item has handled the press</summary>
        private void LateUpdate() => Flush_ActionInputs();

        /// <summary> [Auto Holster] per-frame idle timers: an Item with no Action playing for [AutoStore] seconds is stored (normal Store animation) </summary>
        private void Update()
        {
            if (AutoStore.Value <= 0) return; //MWC — [Auto Holster] disabled for the whole character

            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var point = EquipPoints[i];
                var item = point?.Item;

                if (item == null) continue;

                //MWC — "doing nothing" is decided by the ITEM alone now: no Action of its Active Set is playing (a held
                //Aim counts as playing) and no equip/store animation is in flight. It used to also reset on ANY Mode of
                //the character, so attacking with one hand kept the OTHER hand's Item out for as long as the combo ran
                if (item.IsEquipping || item.IsActionPlaying((ItemActionID)null))
                {
                    point.LastActiveTime = Time.time;
                    continue;
                }

                if (Time.time - point.LastActiveTime < AutoStore.Value) continue; //Still inside the idle window

                //MWC — idle long enough, but the store has to land on a clean frame: a No Items [State|Stance|Mode]
                //already owns the Item through the hold/restore path, and a Mode in flight would be cut short by the
                //Store animation. The timer is left elapsed (not stamped), so it fires on the first free frame
                if (HandsBlocked || Animal.IsPlayingMode) continue;

                //MWC — stamped BEFORE the call: Item_Unequip can early-out (e.g. the Item is no longer flagged as
                //equipped) leaving every condition above unchanged, which re-ran this branch and its log EVERY frame.
                //Re-stamping makes a failed store retry one idle window later instead
                point.LastActiveTime = Time.time;

                Debugging($"[Auto Holster] [{item.name}] idle for {AutoStore.Value:F1}s → Store", "cyan");
                Item_Unequip(item); //MWC — normal ANIMATED store
            }
        }

        private void OnValidateAction(IKManager manager)
        {
            if (!Application.isPlaying) return;

            foreach (var p in EquipPoints)
                if (p.Item != null && p.Item.ActiveActionSet != null && p.Item.ActiveActionSet.CurrentIKProfile) //MWC — resolved Profile
                    p.Item.ActiveActionSet.CurrentIKProfile.ValidateIK(manager);
        }



        #endregion

        /// <summary>Temporally puts away every equipped Item (a No Items [State|Stance|Mode] started, or the Manager was Locked). <paramref name="hide"/> = deactivate them in place instead of holstering (Lock Mode: Hide)</summary>
        private void Temporal_Hold_Items(bool hide = false, string reason = "No Items in State|Stance|Mode")
        {
            //MWC — snapshot first: Hold_Item holsters the items, which clears the Equip Points (and may evict/drop
            //another item) while we are walking them.
            var toHold = new List<SuperItem>();

            foreach (var p in EquipPoints)
            {
                if (p == null) continue;

                //MWC — Occupant, not Item: an item still playing its Equip animation only holds the point as 'Reserved'.
                //It used to be missed here, so it landed in the hand in the middle of the blocking Mode.
                var occupant = p.Occupant;

                if (occupant != null) toHold.Add(occupant); //Hold_Item cancels its pending draw (reservation + coroutine)
            }

            foreach (var item in toHold) Hold_Item(item, hide, reason);

            Unequip_DefaultItem(); //MWC — also hide the Default Item (Unarmed) while a state/stance/mode requires no items
        }

        //MWC — Items held by a No Items block that have NO Holster to go to: they are deactivated in place instead of
        //being dropped on the ground. Kept here so they stay hidden until their Equip animation actually ends.
        internal readonly HashSet<SuperItem> HiddenItems = new(); //internal: read by the Debug tab

        //MWC: Capacity - Items pushed out of a Holster by the last Item_Holster call (Slots full or Capacity reached)
        private readonly List<SuperItem> EvictedItems = new();

        /// <summary> FAST put-away used by the No Items [State|Stance|Mode] block: the item goes straight to its
        /// holster with NO Unequip (Store Weapon) animation, and is remembered so it can be drawn back on release.
        /// Item_Holster already routes through Item_Unequip_INTERNAL_FAST, which is the instant, animation-less unequip.
        /// <para>No Holster for the item, or <paramref name="hide"/> (Lock Mode: Hide) → it is hidden in place (deactivated) rather than dropped on the ground.</para></summary>
        private void Hold_Item(SuperItem item, bool hide = false, string reason = "No Items in State|Stance|Mode")
        {
            if (item == null) return;

            //Skip duplicates: two block reasons (State + Mode) can hold the same item, and it must be drawn back only once
            if (!HoldItems.Contains(item)) HoldItems.Add(item);

            //MWC — the item is staying put: an item blocked mid-draw gives its Equip Point reservation and its holster
            //'Leaving' mark back, and the delayed end of that draw is STOPPED (it used to rely on the HandsBlocked guard
            //inside Equip_INTERNAL_FAST, which never ran at all when the GameObject was deactivated meanwhile).
            Cancel_PendingDraw(item);

            if (item.IsInHolster || HiddenItems.Contains(item)) return; //Already put away (e.g. parked by a blocked equip request)

            if (!hide && ResolveHolster(item) != null)
            {
                //The Manager is storing the Item itself: its own Item_Holster call must pass the Lock gate
                var bypass = lockBypass;
                lockBypass = true;
                Item_Holster(item); //Instant unequip (no Store Weapon animation) + stored in the holster
                lockBypass = bypass;

                Debugging($"[<color=yellow>Hold</color> {item.name}] → Holstered by [{reason}]", "orange");
            }
            else
            {
                //MWC — no Holster for this item (or Lock Mode: Hide): HIDE it instead of dropping it.
                HiddenItems.Add(item);

                Item_Unequip_INTERNAL_FAST(item);
                item.gameObject.SetActive(false);

                Debugging($"[<color=yellow>Hold</color> {item.name}] → Hidden by [{reason}]{(hide ? string.Empty : " (it has no Holster)")}", "orange");
            }
        }

        /// <summary> Brings a hidden (no Holster) item back into the world. Called the moment it actually lands
        /// on the Equip Point, so it never pops into the hand while its Equip animation is still playing. </summary>
        private void Unhide_Item(SuperItem item)
        {
            if (item != null && HiddenItems.Remove(item)) item.gameObject.SetActive(true);
        }

        //MWC — a restore is already scheduled for the next frame (see Temporal_Release_Items)
        internal bool ReleasePending; //internal: read by the Debug tab

        /// <summary>
        /// Re equip all the items that were temporally hold
        /// </summary>
        private void Temporal_Release_Items()
        {
            if (HandsBlocked) return;    //Another State/Stance/Mode (or the Lock) is still blocking the hands
            if (ReleasePending) return;  //Already scheduled for the next frame

            ReleasePending = true;

            //MWC — deferred ONE FRAME on purpose. This runs inside MAnimal's Mode/State/Stance change callback, and the
            //restore can force a new Mode (the Equip animation). Activating a Mode from inside the ActiveMode setter
            //re-enters it and corrupts LastMode/ActiveMode mid-callback, which is why the weapon often never came back.
            if (this.Delay_Action(() => Restore_Hold_Items()) == null)
                Restore_Hold_Items(); //Cannot defer (object inactive): do it right now so the items are never lost
        }

        /// <summary> Draws back every item held by a No Items [State|Stance|Mode] block.
        /// Fast Equip Back ON → they appear in the hands instantly. OFF (default) → the Equip Mode animation is played. </summary>
        private void Restore_Hold_Items()
        {
            ReleasePending = false;

            if (HandsBlocked) return; //A new block (or a Lock) started during the frame we waited: keep everything stored

            if (HoldItems.Count > 0)
            {
                //Snapshot + clear FIRST: Item_Equip re-enters the manager (holstering, evicting, blocked-equip parking)
                //and can add to HoldItems while we iterate it.
                var toRestore = new List<SuperItem>(HoldItems);
                HoldItems.Clear();

                foreach (var item in toRestore)
                {
                    if (item == null) continue; //Destroyed while it was stored

                    Debugging($"[<color=yellow>Restore</color> {item.name}] → Fast Equip: <color=white>{FastEquipBack.Value}</color>", "orange");

                    Item_Equip(item, FastEquipBack.Value); //Fast → no Equip animation. Otherwise the Equip Mode is played

                    //MWC — safety: the draw never started (the item's Equip Point ID is not on this manager). Never
                    //leave a hidden item deactivated forever, or it silently disappears from the game.
                    if (!item.IsEquipped && !item.IsEquipping) Unhide_Item(item);
                }
            }

            RefreshDefaultItem(); //MWC — bring the Default Item (Unarmed) back if hands ended up empty
        }

        /// <summary> Escape hatch: force-clears every No Items [State|Stance|Mode] block and draws back all the
        /// items being held (unless the Manager is Locked: then they wait for <see cref="Unlock"/>).
        /// Use it from a UnityEvent/Reaction if a Mode is interrupted without ever firing its Mode End. </summary>
        public void Items_ReleaseHold()
        {
            NoItemsEquippedMode = 0;
            HoldByState = false;
            HoldByStance = false;

            Temporal_Release_Items();
        }

        #region Animator Connections
        public virtual void TryAnimParameter(int Hash, float value) { if (Hash != 0) SetFloatParameter?.Invoke(Hash, value); }
        public virtual void TryAnimParameter(int Hash, int value) { if (Hash != 0) SetIntParameter?.Invoke(Hash, value); }
        public virtual void TryAnimParameter(int Hash, bool value) { if (Hash != 0) SetBoolParameter?.Invoke(Hash, value); }
        public virtual void TryAnimParameter(int Hash) { if (Hash != 0) SetTriggerParameter?.Invoke(Hash); }


        /// <summary>Set a Integer on the Animator</summary>
        public void SetAnimParameter(int hash, int value) => Anim.SetInteger(hash, value);

        /// <summary>Set a float on the Animator</summary>
        public void SetAnimParameter(int hash, float value) => Anim.SetFloat(hash, value);

        /// <summary>Set a Bool on the Animator</summary>
        public void SetAnimParameter(int hash, bool value) => Anim.SetBool(hash, value);

        /// <summary>Set a trigger on the Animator</summary>
        public void SetAnimParameter(int hash) => Anim.SetTrigger(hash);
        #endregion

        private void CheckReferences()
        {
            if (Animal == null || Animal.transform.SameHierarchy(transform)) Animal = this.FindComponent<MAnimal>();
            if (Aim == null || Aim.transform.SameHierarchy(transform)) Aim = this.FindComponent<Aim>();
            if (Pouch == null || Pouch.transform.SameHierarchy(transform)) Pouch = this.FindComponent<PouchManager>();
            if (anim == null || anim.transform.SameHierarchy(transform)) anim = this.FindComponent<Animator>();
            if (ComboManager == null || ComboManager.transform.SameHierarchy(transform)) ComboManager = this.FindComponent<ComboManager>();
            if (interactor == null || interactor.transform.SameHierarchy(transform)) interactor = this.FindComponent<MInteractor>();
            if (IKManager == null || IKManager.transform.SameHierarchy(transform)) IKManager = this.FindComponent<IKManager>();
        }

        //MWC — every query below used LINQ (Any/Count/FirstOrDefault) over a List<T>. Reached through IEnumerable<T>
        //that BOXES the List enumerator (1 heap object per call) and, when the lambda captures the ID, allocates a
        //closure + a delegate on top of it. Conditions call these EVERY FRAME (an IK Processor evaluates its
        //Conditions once per Animator IK-Pass layer plus once in LateUpdate — 5x per frame on the Human v6 rig),
        //so a plain indexed loop is used instead: same result, zero allocation, ~4x faster.
        public virtual bool IsItemEquippedAny(List<SuperItemID> IDs)
        {
            if (IDs == null || IDs.Count == 0) return false;

            for (int i = 0; i < IDs.Count; i++)
                if (IsItemEquipped(IDs[i])) return true;

            return false;
        }


        /// <summary> True when an equipped Item (Equip Points + the active Default Item) holds the Malbers Tags on its own Tags component. [all] = every Tag must sit on the same Item </summary>
        public bool IsItemEquippedWithTag(List<Tag> tags, bool all = false)
        {
            if (tags == null || tags.Count == 0) return false;

            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var item = EquipPoints[i]?.Item;
                if (item == null) continue;
                if (all ? item.gameObject.HasAllMalbersTag(tags) : item.gameObject.HasMalbersTag(tags)) return true;
            }

            //The Default Item (fists) is equipped directly, with no Equip Point, so the loop above never sees it
            if (DefaultItemActive)
                return all ? DefaultItem.gameObject.HasAllMalbersTag(tags) : DefaultItem.gameObject.HasMalbersTag(tags);

            return false;
        }

        public virtual bool IsItemEquipped(SuperItemID ID)
        {
            if (ID == null) return false;

            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var point = EquipPoints[i];
                if (point != null && point.Item != null && point.Item.ID == ID) return true;
            }
            return false;
        }

        public SuperItem Get_ItemEquipped(SuperItemID ID) //MWC — public for the AI Brain / external code
        {
            if (ID == null) return null;

            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var point = EquipPoints[i];
                if (point != null && point.Item != null && point.Item.ID == ID) return point.Item;
            }
            return null;
        }

        //MWC: Holster query helpers — mirror the equip-point checks above so Conditions can ask about stored items.
        /// <summary>Returns true if there's any item stored in any holster.</summary>
        public bool IsAnyItemHolstered() //MWC — public for the AI Brain / external code
        {
            for (int i = 0; i < holsters.Count; i++)
                if (holsters[i] != null && holsters[i].HasItem) return true;

            return false;
        }

        /// <summary>Returns true if an item with the given Super Item ID is stored in any holster (every slot, not only the head).</summary>
        public virtual bool IsItemHolstered(SuperItemID ID) //MWC — public for the AI Brain / external code
        {
            //MWC — was head-only (h.Item). A Holster with several slots legitimately stores more than one Item, so a
            //Shield stored BEFORE a Bow became invisible to every ID query the moment the Bow took over as the head:
            //IsItemHolstered/OwnsItem said false and the AI refused to draw an Item it was actually carrying.
            return Get_ItemHolstered(ID) != null;
        }

        /// <summary>Returns true if the holster with the given Holster ID currently holds an item.</summary>
        public bool IsHolsterOccupied(HolsterID holsterID) //MWC — public for the AI Brain / external code
        {
            if (holsterID == null) return false;

            for (int i = 0; i < holsters.Count; i++)
            {
                var h = holsters[i];
                if (h != null && h.GetID == holsterID.ID && h.HasItem) return true;
            }
            return false;
        }

        /// <summary>Returns the item stored in any holster that matches the given Super Item ID, head first and then every slot (or null).</summary>
        public SuperItem Get_ItemHolstered(SuperItemID ID) //MWC — public for the AI Brain / external code
        {
            if (ID == null) return null;

            for (int i = 0; i < holsters.Count; i++)
            {
                var h = holsters[i];
                if (h == null) continue;

                //MWC — was 'h.Item.ID == ID' (head only): see IsItemHolstered. Get_Item walks the head AND every slot
                var found = h.Get_Item(ID);
                if (found != null) return found;
            }
            return null;
        }

        //MWC: Possession / capacity / runtime-state helpers used by the [Manager] Conditions.
        /// <summary>Returns true if an item with the given ID is equipped OR holstered.</summary>
        public bool OwnsItem(SuperItemID ID) => IsItemEquipped(ID) || IsItemHolstered(ID); //MWC — public for the AI Brain / external code

        /// <summary>Number of equip points that currently hold an item.</summary>
        public int EquippedCount //MWC — public for the AI Brain / external code. Was EquipPoints.Count(lambda): LINQ boxes the List enumerator every call
        {
            get
            {
                int count = 0;
                for (int i = 0; i < EquipPoints.Count; i++)
                    if (EquipPoints[i] != null && EquipPoints[i].HasItem) count++;

                return count;
            }
        }

        /// <summary>Number of holsters that currently hold an item.</summary>
        public int HolsteredCount //MWC — public for the AI Brain / external code
        {
            get
            {
                int count = 0;
                for (int i = 0; i < holsters.Count; i++)
                    if (holsters[i] != null && holsters[i].HasItem) count++;

                return count;
            }
        }

        /// <summary>Returns true if at least one equip point is free (no item).</summary>
        public bool HasFreeEquipPoint() //MWC — public for the AI Brain / external code
        {
            for (int i = 0; i < EquipPoints.Count; i++)
                if (EquipPoints[i] != null && !EquipPoints[i].HasItem) return true;

            return false;
        }

        /// <summary>Returns true if at least one holster is free (no item).</summary>
        public bool HasFreeHolster() //MWC — public for the AI Brain / external code
        {
            for (int i = 0; i < holsters.Count; i++)
                if (holsters[i] != null && !holsters[i].HasItem) return true;

            return false;
        }

        /// <summary>Returns true if an equipped item is playing the given Action. Null Item ID = ANY equipped item, null Action ID = ANY playing Action.</summary>
        //MWC — deliberately NOT written over a yield-return helper (an iterator allocates a state machine object on
        //every call) nor over LINQ. This runs several times per frame from the IK Conditions, so it stays a plain loop.
        public bool IsActionPlaying(SuperItemID itemID, ItemActionID actionID) //MWC — public for the AI Brain / external code
        {
            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var point = EquipPoints[i];
                if (point == null || point.Item == null) continue;

                if (itemID != null && point.Item.ID != itemID) continue;  //MWC: not the item we were asked about
                if (point.Item.IsActionPlaying(ResolveActionID(point.Item, actionID))) return true;    //MWC: a null actionID matches ANY playing Action; Action Equivalents resolved per Item
            }

            //MWC — the Default Item (fists) is equipped directly, with no Equip Point, so the loop above never sees it.
            if (DefaultItemActive && (itemID == null || DefaultItem.ID == itemID))
                return DefaultItem.IsActionPlaying(ResolveActionID(DefaultItem, actionID));

            return false;
        }

        /// <summary>True when an equipped Item (optionally filtered by ID) plays an Action that passes the Include/Exclude list. Empty list = any playing Action</summary>
        public bool IsActionPlaying(SuperItemID itemID, IDListCheck<ItemActionID> actions)
        {
            if (actions == null || actions.Empty) return IsActionPlaying(itemID, (ItemActionID)null);

            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var point = EquipPoints[i];
                if (point == null || point.Item == null) continue;
                if (itemID != null && point.Item.ID != itemID) continue;
                if (PlaysActionInList(point.Item, actions)) return true;
            }

            //The Default Item (fists) is equipped directly, with no Equip Point, so the loop above never sees it
            return DefaultItemActive && (itemID == null || DefaultItem.ID == itemID) && PlaysActionInList(DefaultItem, actions);
        }

        /// <summary>True when one of the Item's playing Actions passes the Include/Exclude list. Action Equivalents are resolved per Item</summary>
        private bool PlaysActionInList(SuperItem item, IDListCheck<ItemActionID> actions)
        {
            var set = item.ActiveActionSet;
            if (set == null || set.actions == null) return false;

            foreach (var action in set.actions)
            {
                if (action == null || !action.IsPlaying) continue;

                var inList = false;

                foreach (var id in actions.IDs)
                {
                    if (id == null) continue;
                    if (action.ActionID == id || action.ActionID == ResolveActionID(item, id)) { inList = true; break; }
                }

                if (inList == actions.include) return true;
            }
            return false;
        }

        /// <summary>Returns true if any equipped item currently has an action that is playing (firing, aiming, blocking…).</summary>
        //MWC — routed through IsActionPlaying so it also covers the Default Item (Unarmed): punching used to report "no item in use".
        public bool IsAnyActionPlaying() => IsActionPlaying(null, (ItemActionID)null); //MWC — public for the AI Brain / external code

        /// <summary>Returns true if any equipped item's active Action Set matches the given name.</summary>
        internal bool HasActiveActionSet(string setName)
        {
            if (string.IsNullOrEmpty(setName)) return false;

            foreach (var point in EquipPoints)
            {
                var set = point?.Item != null ? point.Item.ActiveActionSet : null;
                if (set != null && set.Name == setName) return true;
            }
            return false;
        }

        //MWC: Dual Wielding queries used by the [Manager] Is Dual Wielding condition.
        /// <summary>True when items are held on both a Right-side and a Left-side equip point (generic dual wielding).</summary>
        internal bool IsDualWielding()
        {
            bool right = false, left = false;
            foreach (var p in EquipPoints)
            {
                if (p == null || !p.HasItem) continue;

                if (p.Side == WeaponSide.Right) right = true;
                else if (p.Side == WeaponSide.Left) left = true;

                if (right && left) return true;
            }
            return false;
        }

        /// <summary>True when two DISTINCT hand items are held, one matching each ID list (hand-agnostic).
        /// An empty list matches ANY hand item for that slot; the same ID in both lists needs two separate items (e.g. two Daggers).
        /// Only items on equip points with a Side (Left/Right) count, so head/none items are ignored.</summary>
        //MWC — the temporary List<SuperItem> of hand items is gone: it allocated on EVERY call, and this is a
        //Condition that can be evaluated several times per frame. The Equip Points are paired in place instead.
        internal bool IsDualWielding(IDList<SuperItemID> firstHand, IDList<SuperItemID> secondHand)
        {
            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var first = HandItem(i);
                if (first == null || !SlotMatches(firstHand, first)) continue;

                for (int j = 0; j < EquipPoints.Count; j++)
                {
                    if (j == i) continue;

                    var second = HandItem(j);
                    if (second != null && SlotMatches(secondHand, second)) return true; //two distinct items, one per list
                }
            }
            return false;

            //Item held on a hand (Left/Right) Equip Point, or null: head/none points are ignored
            SuperItem HandItem(int index)
            {
                var p = EquipPoints[index];
                return p != null && p.HasItem && p.Side != WeaponSide.None ? p.Item : null;
            }

            static bool SlotMatches(IDList<SuperItemID> list, SuperItem item)
                => list == null || !list.HasItems || (item.ItemType != null && list.Contains(item.ItemType));
        }


        /// <summary> Is there any item in the Holster? </summary>
        public virtual bool AnyItemInHolster(HolsterID holster) => D_Holsters[holster].Item != null;
        /// <summary> Is there any item in the inEquipPoint </summary>
        public virtual bool AnyItemInEquipPoint(EquipPointID point)
        {
            var id = MapEquipPoint(point); //MWC — Equip Point Equivalents
            return id != null && D_EquipPoints[id].Item != null;
        }

        /// <summary>Editor only console log for the Manager, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void Debugging(string value, string color = "white") => Debugging(value, this, color);


        /// <summary>Editor only console log for the Manager, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void Debugging(string value, UnityEngine.Object target, string color = "white")
        {
#if UNITY_EDITOR
            if (debug) MDebug.Log($"<B><color=orange>[{name}] - [SIM]</color> → <color={color}>{value}</color></B>", target);
#endif
        }


#if UNITY_EDITOR
        private void Reset()
        {
            CheckReferences();

            ActionsInput = new List<IDString<ItemActionID>>()
            {
            new() { ID = MTools.GetInstance<ItemActionID>("Attack"), value = new("Action1"), active = true },
            new() { ID = MTools.GetInstance<ItemActionID>("Aim"), value = new("Action2"), active = true },
            new() { ID = MTools.GetInstance<ItemActionID>("Block"), value = new("Action2"), active = true },
            new() { ID = MTools.GetInstance<ItemActionID>("Parry"), value = new("Action1"), active = true },
            new() { ID = MTools.GetInstance<ItemActionID>("Reload"), value = new("Reload"), active = true },
            new() { ID = MTools.GetInstance<ItemActionID>("Interact"), value = new("Interact"), active = true },
            new() { ID = MTools.GetInstance<ItemActionID>("Attack Heavy"), value = new("Heavy Attack"), active = true },
            new() { ID = MTools.GetInstance<ItemActionID>("Attack Light"), value = new("Light Attack"), active = true },
            };

            holsters = new List<SItemHolster>()
            {
            new() { ID = MTools.GetInstance<HolsterID>("Left Holster"), OnHolsterItemChanged = HolsterIconReaction("Weapon Holster Left"), OnHolsterEmpty = HolsterEmptyReaction("Weapon Holster Left") },
            new() { ID = MTools.GetInstance<HolsterID>("Right Holster"), OnHolsterItemChanged = HolsterIconReaction("Weapon Holster Right"), OnHolsterEmpty = HolsterEmptyReaction("Weapon Holster Right") },
            };

            var rHand = Anim != null && Anim.avatar != null && Anim.isHuman ? Anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            var lHand = Anim != null && Anim.avatar != null && Anim.isHuman ? Anim.GetBoneTransform(HumanBodyBones.LeftHand) : null;

            var REquipPoint = new GameObject("Equip Point R-Hand");
            var LEquipPoint = new GameObject("Equip Point L-Hand");

            var DropPoint = new GameObject("Drop Point");

            REquipPoint.transform.SetParent(rHand != null ? rHand : transform, false);
            LEquipPoint.transform.SetParent(lHand != null ? lHand : transform, false);

            DropPoint.transform.SetParent(transform, false);
            DropPoint.transform.localPosition = new Vector3(0, 1.5f, 0.6f);

            dropPoint.Value = DropPoint.transform;

            REquipPoint.transform.ResetLocal();
            LEquipPoint.transform.ResetLocal();


            EquipPoints = new List<SEquipPoint>()
            {
            new() { ID = MTools.GetInstance<EquipPointID>("Left Hand"), Point = LEquipPoint.transform,
                    OnEquip = EquipReactions("WM Equiped Weapon OffHand", "SI Item Equipped", false),
                    OnUnequip = EquipReactions("WM Equiped Weapon OffHand", "SI Item Unequipped", true) },
            new() { ID = MTools.GetInstance<EquipPointID>("Right Hand"), Point = REquipPoint.transform,
                    OnEquip = EquipReactions("WM Equiped Weapon", "SI Item Equipped", false),
                    OnUnequip = EquipReactions("WM Equiped Weapon", "SI Item Unequipped", true) },
            };
        }

        /// <summary> Reset helper: [MEvent] reaction that sends the Dynamic Target's SuperItem.UIIcon to the event (Dynamic Target [Item]) </summary>
        private static MEventReaction UIIconReaction(string eventName) => new()
        {
            m_Event = MTools.GetInstance<MEvent>(eventName),
            Param = MEventParam.Member,
            m_Member = new MemberValueReader()
            {
                targetType = typeof(SuperItem),
                memberName = nameof(SuperItem.UIIcon),
                memberKind = MMemberKind.Property,
                valueKind = MGetKind.Sprite,
                valueTypeName = typeof(Sprite).AssemblyQualifiedName,
            },
        };

        /// <summary> Reset helper: [MEvent] reaction that sends the Empty Sprite to the event, clearing the UI icon </summary>
        private static MEventReaction EmptyIconReaction(string eventName)
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("Empty Sprite t:Sprite");
            var empty = guids.Length > 0 ? UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0])) : null;

            return new()
            {
                m_Event = MTools.GetInstance<MEvent>(eventName),
                Param = MEventParam.Sprite,
                m_Sprite = new SpriteReference(empty),
            };
        }

        /// <summary> Reset helper: OnEquip / OnUnequip → UI icon of the Equip Point + the Item Equipped / Unequipped event (Sender = the item) </summary>
        private static Reaction2 EquipReactions(string iconEvent, string itemEvent, bool unequip) => new()
        {
            reactions = new Reaction[]
            {
                unequip ? EmptyIconReaction(iconEvent) : UIIconReaction(iconEvent),
                new MEventReaction() { desc = "Super Item Actions UI", m_Event = MTools.GetInstance<MEvent>(itemEvent), SetSender = true, Param = MEventParam.Component },
            }
        };

        /// <summary> Reset helper: OnHolsterItemChanged → UI icon of the Item on top of the Holster </summary>
        private static Reaction2 HolsterIconReaction(string eventName) => new(UIIconReaction(eventName));

        /// <summary> Reset helper: OnHolsterEmpty → clear the Holster UI icon </summary>
        private static Reaction2 HolsterEmptyReaction(string eventName) => new(EmptyIconReaction(eventName));

        private void OnValidate()
        {
            CheckReferences();
        }

        private void OnDrawGizmos()
        {
            if (dropPoint.Value)
            {
                Gizmos.matrix = Matrix4x4.TRS(dropPoint.position, dropPoint.rotation, dropPoint.localScale);
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(Vector3.zero, 0.05f);
                Gizmos.color = new Color(1, 1, 0, 0.3f);
                Gizmos.DrawSphere(Vector3.zero, 0.05f);
            }


            if (Application.isPlaying) return;

            if (Editor_Tabs1 == 1)
            {
                foreach (var p in EquipPoints)
                {
                    var item = p.Item;
                    if (p.Point.Value && item)
                    {
                        var Point = p.Point.Value;

                        //MWC — offset from the SuperItemOffsets asset (per Item + Equip Point); no asset/entry → identity
                        var Offset = ItemOffsets != null ? ItemOffsets.GetOffset(item.ItemType, p.ID) : new TransformOffset(0);

                        GetItemGizmoMatrices(item, Point, Offset, out var anchor, out var meshMatrix);

                        //MWC — markers at the ITEM ROOT anchor (the same pivot the Scene View handles use), unscaled so they stay round
                        Gizmos.matrix = Matrix4x4.TRS(anchor.GetPosition(), anchor.rotation, Vector3.one);

                        Gizmos.color = Color.green;
                        Gizmos.DrawWireSphere(Vector3.zero, 0.02f);
                        Gizmos.color = new Color(0, 1, 0, 0.3f);
                        Gizmos.DrawSphere(Vector3.zero, 0.02f);

                        Gizmos.color = CanUseEquipPoint(item, p.ID) ? item.EquipColor : new Color(1, 0, 0, 0.5f);
                        Gizmos.DrawCube(Vector3.zero, Vector3.one * 0.01f);

                        //MWC — the mesh has its own matrix: the Offset scale and the whole Display Transform chain are already baked in it
                        if (item.DisplayMesh)
                        {
                            Gizmos.matrix = meshMatrix;
                            Gizmos.DrawMesh(item.DisplayMesh, Vector3.zero, Quaternion.identity, item.DisplayScale);
                        }
                    }
                }
            }
            else if (Editor_Tabs1 == 2)
            {
                foreach (var h in holsters)
                {
                    var item = h.Item;
                    if (item == null) continue;

                    //MWC — Holster Slot and Offset now come from the SuperItemOffsets asset (no asset/entry → slot 0, identity offset)
                    var slot = ItemOffsets != null ? ItemOffsets.GetHolsterSlot(item.ItemType, h.ID) : 0;
                    var holsterOffset = ItemOffsets != null ? ItemOffsets.GetHolsterOffset(item.ItemType, h.ID) : new TransformOffset(0);

                    //MWC — guard the slot index and the unassigned slot Transforms (this used to throw on every repaint)
                    if (h.Slots == null || slot < 0 || slot >= h.Slots.Count) continue;

                    var Point = h.Slots[slot].Value;
                    if (Point == null) continue;

                    GetItemGizmoMatrices(item, Point, holsterOffset, out var anchor, out var meshMatrix);

                    //MWC — marker at the ITEM ROOT anchor (the same pivot the Scene View handles use), unscaled
                    Gizmos.matrix = Matrix4x4.TRS(anchor.GetPosition(), anchor.rotation, Vector3.one);

                    Gizmos.color = h.ValidItem() ? item.HolsterColor : new Color(1, 0, 0, 0.5f);
                    Gizmos.DrawCube(Vector3.zero, Vector3.one * 0.01f);

                    //MWC — the mesh has its own matrix: the Offset scale and the whole Display Transform chain are already baked in it
                    if (item.DisplayMesh)
                    {
                        Gizmos.matrix = meshMatrix;
                        Gizmos.DrawMesh(item.DisplayMesh, Vector3.zero, Quaternion.identity, item.DisplayScale);
                    }
                }
            }
        }

        //MWC — Radius of the Equip Point / Holster Slot markers. The row selected in the inspector list uses the bigger one.
        private const float PointGizmoRadius = 0.015f;
        private const float SelectedPointGizmoRadius = 0.025f;

        //MWC — Opaque orange for the Holster Slots (MTools.MOrange is a GUI tint: its alpha is 0.5)
        private static readonly Color HolsterGizmoColor = new(1f, 0.4f, 0f);

        /// <summary>Previews every Equip Point (yellow) and Holster Slot (orange) Transform with a sphere,
        /// the same way the MRider previews the Left/Right Hand rein points.
        /// The row selected in the inspector list is drawn bigger and green.</summary>
        private void OnDrawGizmosSelected()
        {
            for (int i = 0; i < EquipPoints.Count; i++)
            {
                var e = EquipPoints[i];
                if (e == null) continue;

                var selected = i == SelectedEquipPointSlot; //MWC — the row highlighted in the Equip Points list
                Gizmos.color = selected ? Color.green : Color.yellow;

                DrawPointGizmo(e.Point.Value, selected);
            }

            for (int i = 0; i < holsters.Count; i++)
            {
                var h = holsters[i];
                if (h == null || h.Slots == null) continue;

                var selected = i == SelectedHolster; //MWC — every Slot of the highlighted Holster row turns green…
                Gizmos.color = selected ? Color.green : HolsterGizmoColor;

                for (int s = 0; s < h.Slots.Count; s++)
                    if (h.Slots[s] != null)
                        DrawPointGizmo(h.Slots[s].Value, selected && s == SelectedHolsterSlot); //…but only the selected Slot (the editable one) is bigger
            }
        }

        /// <summary>Marker sphere on a Point using the current Gizmos color.</summary>
        private static void DrawPointGizmo(Transform point, bool selected)
        {
            if (point != null) Gizmos.DrawSphere(point.position, selected ? SelectedPointGizmoRadius : PointGizmoRadius);
        }

        /// <summary> Resolves the two matrices needed to preview an Item on a Point (Equip Point or Holster Slot).
        /// <para><b>anchor</b>: where the ITEM ROOT lands at runtime → Point ▸ Offset (exactly what SEquipPoint.ParentItem() does).</para>
        /// <para><b>meshMatrix</b>: where the DISPLAY MESH ends up → anchor ▸ Display Transform expressed in ITEM ROOT space.</para>
        /// The Display Transform is usually a grandchild of the item root (e.g. Rifle Collectable ▸ Rifle AK ▸ Rifle) and every parent
        /// in that chain can carry its own local rotation/scale. The old math used DisplayTransform.localPosition/localRotation, which
        /// describe the mesh relative to its IMMEDIATE parent — only correct for a direct child, hence the wrong rotation and position. </summary>
        private static void GetItemGizmoMatrices(SuperItem item, Transform Point, TransformOffset offset,
            out Matrix4x4 anchor, out Matrix4x4 meshMatrix)
        {
            //Point.localToWorldMatrix (instead of position/rotation) so the character/bone scale is included, like at runtime
            anchor = Point.localToWorldMatrix * Matrix4x4.TRS(offset.Position, Quaternion.Euler(offset.Rotation), offset.Scale);

            var display = item.DisplayTransform;

            if (display == null || display == item.transform) { meshMatrix = anchor; return; } //No nested mesh: the root IS the mesh

            //Full Display Transform relative to the ITEM ROOT: accumulates every parent rotation/scale in between
            var localPos = item.transform.InverseTransformPoint(display.position);
            var localRot = Quaternion.Inverse(item.transform.rotation) * display.rotation;

            meshMatrix = anchor * Matrix4x4.TRS(localPos, localRot, Vector3.one);
        }
#endif
    }
}
