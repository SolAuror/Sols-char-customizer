// Last Change: 10/6/2026 10:45am
using MalbersAnimations.Events;
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;


/* To Do List:
- DONE ---- Serialize Master Actions instead of using an String value to find it. 
- DONE ---- Add events when the Action failed to execute due to the conditions not being met, so the user can react to it (E.g. play a sound when trying to shoot without ammo)!
- Done ---- Pick Up and Drop Animations.
- Done ---- Items will automatically unequipped, ending active actions if the user enter in a state that doesn't allow the item to be equipped (E.g. Shield will be unequipped when the user is swimming or flying)
- DONE ---- Add a validator to make sure all the actions, processors and everything is properly set. All Processors need a validator returning a Debug Value. 

- Improve Input handling with the new Input System. Charging Actions and OnInput Release can be using the same input for different actions. E.g. Hold the attack button to charge an attack and release it to execute the attack. 
--Execute Processors on Previus Processor Completed.. maybe this will allow to have more complex actions and processors. E.g. Charge Attack -> Aim -> Shoot. This will allow to have more complex actions and processors.  
*/

namespace MalbersAnimations.Items
{
    [System.Serializable] public class SuperItemEvent : UnityEvent<SuperItem> { }
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/super-item/super-item-system/super-item")]
    [SelectionBase, DisallowMultipleComponent]
    [AddComponentMenu("Malbers/Interaction/Super Item")]
    public class SuperItem : MonoBehaviour, IInteractor, IInteractable//, ICollectable
    {
        //MWC: WeaponSide enum hoisted to MalbersAnimations namespace (EquipPointID.cs) so Equip Points can declare their side (Dual Wielding)
        #region General Stuff
        [Tooltip("The item will be auto picked by the user once is focused ")]
        public BoolReference autoPick = new(false);

        [Tooltip("True: the Item will be put directly on the Equip Point.\nFalse: The item will go to the corresponded holster")]
        public BoolReference m_autoEquip = new(true);

        //[Tooltip("This item cannot be equipped with other items")]
        //public BoolReference m_uniqueEquipment = new(true);

        [Tooltip("Mirror the Animations in the Animator controller using the Mirror Parameter")]
        public BoolReference mirror = new(false);

        [Tooltip("Target GameObject to be used in the Item. Useful when the item is used as a weapon.")]
        [SerializeField] private GameObject m_target;

        [Tooltip("Holster to Store the Item when is collected. This value also uses the ID value to play the correct Equip/Unequip animation")]
        [RequiredField, SerializeField] private HolsterID holster;

        [Tooltip("Stance required to equip the item")]
        public StanceID Stance;

        [Tooltip("Strafe while using the item.")]
        public BoolReference strafe = new(false);

        [Tooltip("Update the Thrower Target from the Aimer component")]
        public bool UpdateTargetFromAimer = false;

        //MWC: When enabled, the Item Target is updated from the User's Interactor whenever it focuses a matching interactable.
        [Tooltip("Update the Item Target from the User's Interactor when it focuses an interactable")]
        public BoolReference TargetFromInteractor = new(false);

        //MWC: Interactable ID (Index) to match the focused interactable. -1 means any focused interactable updates the target.
        [Tooltip("Interactable ID (Index) to match the focused interactable. -1 means ANY focused interactable will update the Target")]
        [Hide(nameof(TargetFromInteractor))]
        public IntReference InteractableID = new(-1);

        //MWC: When enabled, the Item Target is cleared (set to null) when a matching interactable is unfocused.
        [Tooltip("Clear the Item Target (set to null) when a matching interactable is unfocused by the User's Interactor")]
        [Hide(nameof(TargetFromInteractor))]
        public BoolReference ClearTargetOnUnfocus = new(false);

        [Tooltip("It Creates a Runtime Copy of each Ik Profile, so the values are not shared between multiple items of the same type. Disable this for Editor Settings")]
        public bool RuntimeIK = true;

        /// <summary> Holster ID to store the item when is collected</summary>
        public HolsterID HolsterID
        {
            get => holster;
            set => holster = value;
        }


        //MWC — HolsterAnim and holsterSlot removed: they now live per-character in the SuperItemOffsets asset (Holster entries)

        [Tooltip("Item Type (Old WeaponType) to be used in the Animator Controller parameter to activate a Hand-Arm Layer")]
        [RequiredField] public SuperItemID ItemType;

        [Tooltip("List of IDs this Item can be used with. Empty means the item cannot be used with other items")]
        public IDListCheck<SuperItemID> CanBeEquippedWith = new(false);

        //MWC — HolsterOffset removed: holster offsets now live per-character in the SuperItemOffsets asset

        [Tooltip("Index of the Equip Point list to place the item")]
        [RequiredField] public EquipPointID EquipPoint;

        /// <summary> When the item is equipped, store which equip point is using it </summary>
        public SEquipPoint CurrentEquipPoint { get; set; }

        //MWC: The item Side is no longer authored on the item; it is always taken from the Equip Point ID it lands on (see EffectiveSide)
        //MWC — EquipPointOffset and the Animator Item Type override removed: both now live per-character
        //in the SuperItemOffsets asset (Equip Point entries). The item only keeps its default EquipPointID + HolsterID.

        //MWC: Dual Wielding — fall-through equip point used when the primary point is occupied by a compatible item
        [Tooltip("Dual Wielding: Alternative Equip Point used when the Primary Equip Point is occupied by a compatible item " +
            "(e.g. Sword: Right Hand → Left Hand). Its Offset and Anim Item Type are stored in the character's SuperItemOffsets asset")]
        public EquipPointID SecondaryEquipPoint;

        //MWC: Dual Wielding — fall-through holster used when the primary holster has no free Slot left for this item.
        //It is a Holster of its own, so it gets its own row in the character's SuperItemOffsets asset: the second
        //copy can be stored with a completely different Offset, Slot list and Holster Anim.
        [Tooltip("Dual Wielding: Alternative Holster used when the Primary Holster has no free Slot left for this item " +
            "(e.g. a second Dagger). Its Offset, Slots and Anim are stored in the character's SuperItemOffsets asset")]
        public HolsterID SecondaryHolster;

        //MWC: Dual Wielding helpers ------------------------------------------------------

        /// <summary> True if the Equip Point ID is the primary point or the secondary (Dual Wielding) point </summary>
        public bool IsAllowedEquipPoint(EquipPointID id) => id != null && (EquipPoint == id || SecondaryEquipPoint == id);

        //MWC — Mirrors IsAllowedEquipPoint: holsters validate the items they accept against this, so the
        //Dual Wielding holster is not rejected for having a different ID than the item's primary Holster
        /// <summary> True if the Holster ID is the primary holster or the secondary (Dual Wielding) one </summary>
        public bool IsAllowedHolster(HolsterID id) => id != null && (holster == id || SecondaryHolster == id);

        //MWC — TryGetOffsetFor / GetAnimItemTypeID / GetOffsetReferenceFor removed:
        //offsets and the Animator Item Type override are resolved from the character's SuperItemOffsets asset now

        /// <summary> Side the item is actually on: taken from the Equip Point ID the item is currently parented to </summary>
        public WeaponSide EffectiveSide => CurrentEquipPoint?.ID != null ? CurrentEquipPoint.ID.Side : WeaponSide.None;

        /// <summary> The item's natural/home Side: the Side of its Primary Equip Point (used as the reference for the Mirror flip) </summary>
        public WeaponSide HomeSide => EquipPoint != null ? EquipPoint.Side : WeaponSide.None;

        /// <summary> True when the item is on its Primary Equip Point (i.e. not a Dual-Wield fall-through copy on a secondary point).
        /// A follower copy returns false so it can skip 'Primary Hand Only' actions. Unknown/unequipped counts as primary. </summary>
        public bool IsOnPrimaryPoint => CurrentEquipPoint == null || EquipPoint == null || CurrentEquipPoint.ID == EquipPoint;

        #region INTERACTIONS
        public Color HolsterColor = new(1, 0.92f, 0.16f, 0.5f);
        public Color EquipColor = new(0, 1, 0, 0.5f);

        /// <summary> Actions blocked on this Item, tracked by owner: an Action stays blocked while at least one owner (a Block Actions Processor on another Item, a Reaction) still blocks it. E.g. the Shield Block and Parry both block the Sword attacks, which come back only when both released </summary>
        public BlockedActionSet BlockedActions { get; set; } = new();

        [Tooltip("Hit Layer to interact with Objects")]
        public LayerReference m_hitLayer = new(-1);
        [SerializeField, Tooltip("What to do with Triggers")]
        protected QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        [Tooltip("When the item is equipped, the Layer, TriggerInteraction and Tags will be overwritten by the current user")]
        public BoolReference UserOverrideLayer = new(true);
        #endregion

        [Tooltip("Duration to play the Equip Animation. This is used to know when the item is actually equipped and can be used to delay the equip logic until the animation is finished")]
        public FloatReference equipDuration = new(0.3f);

        [Tooltip("Duration to play the Equip Animation. This is used to know when the item is actually equipped and can be used to delay the equip logic until the animation is finished")]
        public FloatReference PickDuration = new(0.3f);

        public float CurrentEquipTime { get; set; } //Store the current time of the equip animation to know when it's finished

        [Tooltip("Mode (ID) and Ability (Value) to Equip the Item from the Holster. Leave Empty if you don't want to use an equip animation")]
        public ModeID EquipMode;

        [Tooltip("Mode (ID) and Ability (Value) to Equip the Item from the Holster. Leave Empty if you don't want to use an equip animation")]

        public ModeID UnequipMode;

        [Tooltip("Mode (ID) and Ability (Value) to Pick the Item. Leave Empty if you don't want to use an pick animation")]

        public IDPair<ModeID, int> PickMode;

        [Tooltip("Mode (ID) and Ability (Value) to Drop the Item. Leave Empty if you don't want to use an drop animation")]
        public IDPair<ModeID, int> DropMode;

        //NOT IMPLEMENTED YET
        #region Data
        [SerializeField] protected SpriteReference m_UI = new();
        [SerializeField] protected StringReference description = new();

        //MWC — public read accessors so the data can be pulled out by reflection-driven tools
        //(MemberGetReaction / MemberValueCompare), which only see public instance members.
        /// <summary> Item description, resolved from its constant or its StringVar asset. </summary>
        public string Description => description.Value;

        /// <summary> Icon used to display this item on the UI, resolved from its constant or its SpriteVar asset. </summary>
        public Sprite UIIcon => m_UI.Value;
        #endregion
        #endregion

        #region Colliders and Rigidbody (Physics)
        [RequiredField] public Rigidbody rb;
        [Tooltip("Destroys Rigidbody while picked. This will remove completely the rigidbody from the Pickable object and it will restore it after is dropped.")]
        public BoolReference DestroyRbOnPick = new(false);

        //MWC — per-item choice: most Items must not collide with anything while owned, but a Shield needs its colliders
        //live in the hand to physically block. Both default to ON (the behaviour every Item had before the options existed).
        [Tooltip("Disable the Item colliders while it is Equipped (in the hands).\nOFF: they stay enabled, e.g. a Shield that must physically block while equipped")]
        public BoolReference DisableCollidersOnEquip = new(true);
        [Tooltip("Disable the Item colliders while it is stored in a Holster.\nOFF: they stay enabled while holstered")]
        public BoolReference DisableCollidersOnHolster = new(true);

        [ContextMenuItem(" -- Find Colliders --", nameof(FindColliders))]
        public Collider[] m_colliders;

        //MWC — Auto-derived from the item's hierarchy (see FindTriggers), never authored by hand: an empty slot left
        //in this array used to throw when the Animator activated the damagers. Shown read-only in the Inspector.
        [Tooltip("Trigger Proxies of this item. Auto-derived from the item's children on Enable — read only")]
        public TriggerProxy[] m_Triggers;
        #endregion

        //-----------------------------------------------------------------------------------------
        #region Item Actions 
        [Tooltip("All Extra Actions Set the Item can do")]
        public List<ItemActionSet> actionSets;

        [SerializeField, HideInInspector] private int SelectedActionSetIndex;
        //-----------------------------------------------------------------------------------------

        #endregion

        #region Item Stats (Chamber, Charge, Durability…)
        //MWC — Chamber/Charge data moved to a Stats component placed next to the SuperItem (Stat pattern).
        //Shared item resources (ammo in chamber, charge, durability…) are now Stats keyed by StatID:
        //processors reference a StatID instead of hardcoded fields, upgrades use StatReaction/ModifyStat with no extra code,
        //and Conditions use the generic [Stats] C2_Stats (Value/MaxValue/Full/Empty…).

        /// <summary> Stats component holding this item's resources (Chamber, Charge…). Optional — found on Awake </summary>
        public Stats Stats { get; private set; }

        /// <summary> Null-safe Stat lookup on this item's Stats component. Returns null if the item has no Stats or the Stat is missing </summary>
        public IMStat Stat_Get(StatID ID) => Stats != null && ID != null ? Stats.Stat_Get(ID) : null;
        #endregion

        //Reactions
        [Tooltip("Equip Reaction when the item is equipped by an user [Dynamic Target: Item User]")]
        public Reaction2 EquipReaction;
        [Tooltip("Unequip Reaction when the item is unequipped by an user [Dynamic Target: Item User]")]
        public Reaction2 UnequipReaction;


        [Tooltip("Only interactors that have any of these Tags (on themselves or a parent) fire the Focused/Unfocused events and reactions. Leave empty to allow everyone.\nE.g. add the [Player] tag so the pick-up UI does not show when an enemy focuses the item")]
        public MTags FocusTags = new();

        [Tooltip("Equip Reaction when the item is focused by an user [Dynamic Target: Item User]")]
        public Reaction2 FocusedByReaction;
        [Tooltip("Equip Reaction when the item is unfocused by an user [Dynamic Target: Item User]")]
        public Reaction2 UnFocusedByReaction;

        [Tooltip("Reaction when the item is stored in a holster [Dynamic Target: Item User]")]
        public Reaction2 HolsteredReaction;
        [Tooltip("Reaction when the item is removed from a holster [Dynamic Target: Item User]")]
        public Reaction2 UnHolsteredReaction;

        [Tooltip("Mesh used to display the item in the UI or in the world")]
        [ContextMenuItem(" -- Set Display Mesh --", nameof(ForceFindDisplayMesh))] //MWC — explicit action: it overrides
        public Mesh DisplayMesh;
        public Transform DisplayTransform;
        public Vector3 DisplayScale;

        #region Events
        //MWC — these two were declared but never invoked. They fire at the end of Equip()/Unequip() now.
        [Tooltip("Event when the item is equipped by an user.\nSends the Current User (the Owner's GameObject)")]
        public GameObjectEvent OnEquip = new();
        [Tooltip("Event when the item is unequipped by an user.\nSends the Current User (the Owner's GameObject)")]
        public GameObjectEvent OnUnequip = new();
        [Tooltip("Event when the item is focused by the character")]
        public GameObjectEvent OnFocused = new();
        [Tooltip("Event when the item is unfocused by the character")]
        public GameObjectEvent OnUnfocused = new();
        [Tooltip("Event when the item is stored in the holster")]
        public GameObjectEvent OnHolstered = new();
        [Tooltip("Event when the item is removed from the holster")]
        public GameObjectEvent OnUnHolstered = new();

        [Tooltip("Event when a character picks the item from the world.\nSends the character that picked it (the Manager's GameObject)")]
        public GameObjectEvent OnPickedBy = new();
        [Tooltip("Event when a character drops the item.\nSends the character that dropped it (the Manager's GameObject)")]
        public GameObjectEvent OnDroppedBy = new();

        [Tooltip("Event when the item used by the Manager (Equiped or Holstered)")]
        public GameObjectEvent OnAddedToManager = new();
        [Tooltip("Event when the item no longer used by the Manager (Neither Equiped nor Holstered)")]
        public GameObjectEvent OnRemoveFromManager = new();
        #endregion

        /// <summary>  Cache the Rigid body parameters  on start  </summary>
        protected RigidbodyParameters rigidbodyParameters;
        /// <summary>  Cache  if the RB is kinematic  </summary>
        protected bool defaultKinematic;
        /// <summary>  Cache   the RB  constraints </summary>
        protected RigidbodyConstraints defaultConstraints;

        #region Properties
        //MWC — HolsterSlot/Holder removed: the Holster Slot index now lives in the SuperItemOffsets asset (Holster entries)

        // public Rigidbody RigidBody => rb;
        /// <summary>  Store if the item has a rigidbody  </summary>
        public bool HasRigidbody { get; set; }

        /// <summary>
        /// The current user of the item. This value is set when the item is equipped and cleared when the item is unequipped.
        /// It can be used to know who is using the item at the moment and to access the user's properties and methods. If the current user is null, it means that the item is not being used by anyone.
        /// </summary>
        public GameObject UserGo => User ? User.gameObject : null;
        /// <summary>  Who is using the Weapon at the moment  </summary>
        public SuperItemManager User { get; internal set; }

        /// <summary>Takes ownership of this Item for <paramref name="owner"/> (equip OR holster) and fires [On Added To Manager] the first time. Returns false when it already belongs to someone else</summary>
        //MWC — the single place ownership is established. Two bugs shared one root cause:
        //  • A pickup that went DIRECTLY to a Holster never assigned User (only SuperItem.Equip did), so the Item was
        //    physically stored yet still looked unowned. The Item_Pick guard reads [User != null && ... IsInManager],
        //    so a SECOND Manager passed it and re-parented the very same Item into ITS holster, while the first
        //    holster kept a stale reference and both StoredItems lists reported holding it.
        //  • [On Added To Manager] was declared and documented in the inspector ("fires when the item is used by a
        //    manager, either equipped or holstered") but had no Invoke anywhere, while its pair
        //    [On Remove From Manager] was fired by Drop. Dispatching it here makes the pair symmetric.
        internal bool AddToManager(SuperItemManager owner)
        {
            if (owner == null) return false;

            //Owned by somebody who still HOLDS it (equipped or holstered): refuse. Mirrors the Item_Pick guard exactly.
            //A stale User whose Manager no longer holds the Item (unequipped without storing) is taken over instead of
            //leaving the Item stored in this Manager while it still reports the previous one as its owner.
            if (User != null && User != owner && IsInManager) return false;

            var isNewOwner = User == null;

            User = owner;

            //Fired once per ownership, NOT once per equip/holster transition: re-holstering or swapping hands must not repeat it
            if (isNewOwner) OnAddedToManager.Invoke(owner.gameObject);

            return true;
        }

        /// <summary>Fires [On Picked By] with the character that picked the item</summary>
        internal void NotifyPickedBy(SuperItemManager picker) => OnPickedBy.Invoke(picker != null ? picker.gameObject : null);

        /// <summary>Releases ownership and fires [On Remove From Manager] (no-op when the Item has no User)</summary>
        //MWC — mirror of AddToManager, so every ownership end goes through one place (Drop, and the standalone UnHolster)
        internal void RemoveFromManager()
        {
            if (User == null) return;

            OnRemoveFromManager.Invoke(UserGo); //Invoke the OnRemoveFromManager event with the user GameObject
            User = null;
        }

        /// <summary>Manager that claimed this free Item for a delayed Pick (null when no Pick is in flight)</summary>
        //MWC — a Pick with a Pick Duration only takes ownership when its delayed callback lands. Until then the Item
        //still looked free, so a SECOND character could accept the very same Item and its callback would reparent the
        //one the first character had already equipped. The claim makes the reservation visible to everyone.
        public SuperItemManager PickedBy { get; internal set; }

        /// <summary>Is any Interactor focusing this item?</summary>
        public bool Focused { get; set; }

        /// <summary>Last Interactor that focused this item (null when nobody does)</summary>
        public IInteractor FocusedBy { get; private set; }

        /// <summary>Every Interactor currently focusing this item (the Player and an AI can focus it at once)</summary>
        private readonly HashSet<IInteractor> focusedBy = new();

        /// <summary>Focusers that received OnFocused, so the paired OnUnfocused goes out exactly once (the Manager fires it on Pick/Holster/Equip and the Interactor again on trigger exit)</summary>
        private readonly HashSet<GameObject> focusNotified = new();

        /// <summary>  Active value for an Interactor  </summary>
      //  public bool Active { get; set; }
        public bool Active
        {
            get => interactionActive;
            set
            {
                interactionActive = value;
                //  Debug.Log($"{name} interactionActive {interactionActive}");
            }
        }

        private bool interactionActive;

        /// <summary>If the list of items that can be equipped with is empty, then this item is unique and cannot be equipped with other items</summary>
        public bool UniqueEquipment => CanBeEquippedWith.Empty;

        /// <summary> The item will be set directly on the Equip Point when picked. False: The item wil be set on the holster instead</summary>
        public bool AutoEquip { get => m_autoEquip.Value; set => m_autoEquip.Value = value; }

        /// <summary>Target GameObject used by the Actions/Processors. Assigning a DIFFERENT value dispatches [Target Changed] to the Auto Activation Sets</summary>
        //MWC — was a plain setter, so [Target Changed] was a selectable Auto Activation type that nothing ever
        //dispatched: the Set was only ever evaluated by the equip sync (EquipSyncTypes), never when the Target
        //actually changed while the Item was in the hand. AimerTarget routes through here, so it dispatches too.
        //Change-driven on purpose (like LockOnTarget.Target): re-assigning the same target must not swap Sets.
        //Only an EQUIPPED item dispatches: a loose/holstered one has no User for the Set conditions to evaluate,
        //and the equip sync already re-checks [Target Changed] the moment it reaches a hand.
        public GameObject Target
        {
            get => m_target;
            set
            {
                if (m_target == value) return; //No change: nothing to dispatch (Unity's == also folds a destroyed target into null)

                m_target = value;

                if (IsEquipped) VerifyAutoSetActivation(SetActivationType.TargetChanged);
            }
        }

        /// <summary>Current Active Action First</summary>
        public ItemAction MainAction { get; set; }

        /// <summary>Current Active Action First</summary>
        public ItemAction SlaveAction { get; set; }

        /// <summary>Is the Item equipped? </summary>
        public virtual bool IsEquipped { get; set; }


        /// <summary>Is the Item in the process of being equipped? This is true from the moment the Equip method is called until the equip animation is finished.
        /// It can be used to delay certain logic until the item is actually equipped and ready to be used.</summary>
        public bool IsEquipping => equipDuration > 0 && Time.time - CurrentEquipTime < equipDuration;

        /// <summary>Is the Item stored in a Holster? </summary>
        public virtual bool IsInHolster { get; set; }

        /// <summary>Is the Item equipped or stored in a Holster? </summary>
        public virtual bool IsInManager => (User != null && IsEquipped) || IsInHolster; //MWC — explicit parentheses; '&&' already binds before '||' but the intent was ambiguous

        /// <summary>True when the Item is lying free in the world (enabled, not equipped, not holstered). What an AI can go and pick</summary>
        public bool IsFree => !IsInManager && isActiveAndEnabled;

        //MWC — World registry used by the AI Brain [Find Super Item] decision: every enabled Item registers itself, so an NPC can
        //look for free items without physics queries or layers. Filter with IsFree at query time (equipped/holstered items stay registered).
        /// <summary>Every enabled Super Item in the scene (equipped, holstered or free). Filter with <see cref="IsFree"/></summary>
        public static HashSet<SuperItem> AllItems { get; } = new();

        /// <summary>Clears the registry when Domain Reload is disabled (Enter Play Mode options)</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAllItems() => AllItems.Clear();

        /// <summary>  Current Active item action Set  </summary>
        public ItemActionSet ActiveActionSet { get; set; }

        /// <summary>Starting Action Set</summary>
        public ItemActionSet DefaultActionSet { get; set; }

        /// <summary>
        /// Store the default transform values of the item to be able to restore them when the item is dropped or stored in the holster. 
        /// This is useful to avoid having the item with the wrong position, rotation or scale when is dropped or stored in the holster.
        /// </summary>
        public TransformValues DefaultTransformValue { get; set; }
        #endregion

        protected virtual void Awake()
        {
            if (!rb) rb = GetComponent<Rigidbody>();
            HasRigidbody = rb != null;

            if (m_colliders == null || m_colliders.Length == 0) m_colliders = GetComponentsInChildren<Collider>(true); //Get all the colliders in the item

            if (HasRigidbody) // Store the default value of the rigidbody parameters
            {
                defaultKinematic = rb.isKinematic;
                defaultConstraints = rb.constraints;
                rigidbodyParameters = new(rb);
            }

            Stats = GetComponent<Stats>(); //MWC — optional Stats component next to the SuperItem (Chamber, Charge…). Replaces ChamberUnits = 0

            if (actionSets == null || actionSets.Count == 0)
            {
                Debug.LogWarning($"<color=red><B>Item {name} has no Action Sets</B></color> Please create at least one", this);
            }

            //MWC — FirstOrDefault + fallback: an item whose sets have no [Default]/[Active] flag no longer throws (InvalidOperationException) and kills Awake
            DefaultActionSet = actionSets.FirstOrDefault(x => x.Default) ?? actionSets.FirstOrDefault();  //Default Action Set (fallback: first set)

            ActiveActionSet = actionSets.FirstOrDefault(x => x.Active) ?? DefaultActionSet;               //Active Action Set (fallback: the Default set)

            //MWC — help authoring: warn (editor only) when no set was flagged, since we silently fell back to the first one
            if (DefaultActionSet != null && !actionSets.Exists(x => x.Default))
                Debug.LogWarning($"<color=orange><B>Item {name}</B></color> has no Action Set marked as <B>Default</B>. Using '{DefaultActionSet.Name}'.", this);

            //Initialize the Item Action and processors
            foreach (var set in actionSets)
            {
                //MWC — the IK Profile is NOT resolved here any more. At Awake the Item has no User yet, so the owner's
                //rig Remap is unknown, and overwriting [set.IKProfile] with the Runtime copy destroyed the only key
                //that Remap can be looked up with. Both the remap and the Runtime copy now happen in Set_Enter, where
                //the User is known, and they re-run when the Item changes hands. See ItemActionSet.ResolveIKProfile.

                foreach (var itemAction in set.actions)
                    itemAction.Awake(this, set);
            }

            DefaultTransformValue = new TransformValues(transform); //Store the Default Transform Values

            Active = true; //Enable the Interactions by default


            CurrentEquipTime = -999; //Initialize the Current Equip Time to a very low value to avoid issues with the IsEquipping property when the item is equipped for the first time
        }

        protected virtual void OnEnable()
        {
            AllItems.Add(this); //MWC — world registry for the AI Brain
            FindTriggers(); //MWC — always rebuilt from the hierarchy, so the serialized list can never go stale or hold empty slots

            foreach (var Proxy in m_Triggers)
            {
                if (Proxy)
                {
                    Proxy.EnterTriggerInteraction += EnterTriggerInteraction;   //Set the Interact method to the Enter Trigger Interaction
                    Proxy.ExitTriggerInteraction += ExitTriggerInteraction;     //Set the Interact method to the Exit  Trigger Interaction
                    Proxy.OnActivated += ClearTriggerHits;                       //A new activation window hits everyone once again

                    Proxy.enabled = false; //Disable the Trigger Proxy Collider by default. It will be enabled when the Action is activated
                    Proxy.ownCollider.enabled = false; //Disable the Trigger Proxy Collider by default. It will be enabled when the Action is activated
                }
            }

            //Enable the Item Action and processors
            foreach (var set in actionSets)
                foreach (var itemAction in set.actions)
                    itemAction.OnEnable(this, set);

            //MWC — an EQUIPPED Item that was deactivated and switched back on re-subscribes its Actions to the
            //Manager input. OnDisable drops the subscription (see there), and nothing else would put it back:
            //Input_Connection(true) only runs on Equip and on an Action Set change, so the Item would come back
            //visible and equipped but deaf to its own buttons.
            if (IsEquipped) Input_Connection(true);
        }



        private void OnDisable()
        {
            AllItems.Remove(this); //MWC — world registry for the AI Brain

            //MWC — an equipped Item subscribes its Actions to the Manager's InputSource, and ONLY Unequip (or an
            //Action Set change) used to unsubscribe. Deactivating just the Item GameObject — which the package
            //itself exposes through [SI Item Toggle/GameObject] — left those delegates registered, so the button
            //still reached ActivateOnInput and started an Action on an inactive object: processors ran, IsPlaying
            //went true and Update() was never there to end it. Disconnected here; OnEnable re-connects.
            Input_Connection(false);

            //MWC — and end whatever was ALREADY playing when the GameObject went down. Update() stops running here, so
            //an Action mid-play would keep IsPlaying true forever (and its processors would never get their exit call),
            //leaving the Item stuck the next time it is switched back on. Forced: this is an external cancellation.
            ActionSet_Interrupt("Item Disabled", force: true);

            ReleaseFocusers(); //Interactors still focusing this item let it go (their pick-up prompt would stay on a disabled item)

            foreach (var Proxy in m_Triggers)
            {
                if (Proxy)
                {
                    Proxy.EnterTriggerInteraction -= EnterTriggerInteraction;   //Remove the Interact method to the Enter Trigger Interaction
                    Proxy.ExitTriggerInteraction -= ExitTriggerInteraction;     //Remove the Interact method to the Exit  Trigger Interaction
                    Proxy.OnActivated -= ClearTriggerHits;
                }
            }

            //Enable the Item Action and processors
            foreach (var set in actionSets)
                foreach (var itemAction in set.actions)
                    itemAction.OnDisable(this, set);
        }


        private void Update()
        {
            if (ActiveActionSet == null) return;
            foreach (var action in ActiveActionSet.actions)
                action.Update(this, ActiveActionSet, Time.deltaTime);
        }


        private void AimerTarget(Transform target)
        {
            if (UpdateTargetFromAimer) Target = target != null ? target.gameObject : null;
        }

        /// <summary>  Connect all the inputs to the actions when equipped and disconnect them when unequipped  </summary>
        protected void Input_Connection(bool connect)
        {
            if (ActiveActionSet == null) return;

            //MWC — User null-checked, not assumed. There is no input to wire on an Item nobody owns, but every
            //Action Set change calls this twice, and ActionSet_Active / ActionSet_ActiveByIndex are public AND
            //exposed as a Reaction: switching the Set on a LOOSE Item (multiple Sets, never picked up) threw a
            //NullReferenceException here on 'User.InputSource' and aborted the swap half-done.
            if (User == null || User.InputSource == null) return;

            foreach (var action in ActiveActionSet.actions)
            {
                //MWC: Dual Wielding - a follower copy (on a secondary equip point) does not subscribe Primary Hand Only actions to the input, avoiding a double call
                if (action.primaryHandOnly.Value && !IsOnPrimaryPoint) continue;

                //Find the Action Input
                var Input = User.GetActionInput(this, action.ActionID); //MWC — own row, or an Action Equivalent's row (e.g. Wolf: Attack → Attack Light)

                if (Input != null)
                {
                    action.OnInputActivated ??= new((inputValue) => action.ActivateOnInput(inputValue, this, ActiveActionSet));

                    //Connect the Input to the Action
                    if (connect)
                    {
                        User.InputSource.ConnectInput(Input.value.Value, action.OnInputActivated);
                    }
                    //Disconnect the Input to the Actions
                    else
                        User.InputSource.DisconnectInput(Input.value.Value, action.OnInputActivated);
                }
            }
        }


        private void ExitTriggerInteraction(GameObject obj_touched, Collider collider, TriggerProxy proxy)
        {
            for (int i = 0; i < ActiveActionSet.actions.Count; i++)
            {
                var action = ActiveActionSet.actions[i];

                //Call the Enter Trigger Interaction for each action in the Action Set
                action.ExitTriggerInteraction(obj_touched, collider, proxy, this, ActiveActionSet, User);
            }
        }

        /// <summary>Targets each Trigger Proxy already hit during its current activation (one hit per character per activation)</summary>
        private readonly Dictionary<TriggerProxy, HashSet<Object>> triggerHits = new();

        /// <summary>Forgets what the Trigger Proxy already hit: called when the Proxy is enabled again and when its Action starts</summary>
        internal void ClearTriggerHits(TriggerProxy proxy)
        {
            if (proxy != null && triggerHits.TryGetValue(proxy, out var hits)) hits.Clear();
        }

        /// <summary>True the first time this target is hit by the Proxy during its current activation. Keyed by the Root Damageable, so a hit box, the body and a held Item all count as the same character</summary>
        private bool FirstHit(TriggerProxy proxy, GameObject obj_touched, Collider collider)
        {
            //The Trigger Proxy only dedupes per GameObject while a collider stays inside: a held Item (a Shield is an
            //IObjectCore, so it resolves as its own root) and a collider that leaves and comes back during the same swing
            //both reached the Actions as new hits, and a Knock Back reaction played twice from one Parry.
            var damageable = collider.FindInterface<IMDamage>();
            Object target = damageable != null ? (damageable.Root as Object ?? obj_touched) : obj_touched;

            if (!triggerHits.TryGetValue(proxy, out var hits)) triggerHits[proxy] = hits = new();

            return hits.Add(target);
        }

        private void EnterTriggerInteraction(GameObject obj_touched, Collider collider, TriggerProxy proxy)
        {
            if (!FirstHit(proxy, obj_touched, collider)) return; //Already hit during this activation of the Trigger

            for (int i = 0; i < ActiveActionSet.actions.Count; i++)
            {
                var action = ActiveActionSet.actions[i];

                //Call the Enter Trigger Interaction for each action in the Action Set
                action.EnterTriggerInteraction(obj_touched, collider, proxy, this, ActiveActionSet, User);
            }
        }


        private void UpdateTriggerLayers(SuperItemManager owner)
        {
            if (owner.OverrideLayer == 0) return; //If we are not overriding the layer, do nothing

            foreach (var trigger in m_Triggers)
                if (trigger)
                {
                    trigger.SetLayer(owner.OverrideLayer, owner.TriggerInteraction, owner.transform, owner.ItemTagInteraction); //Set the Proxy Layer to the User Layer
                }
                else
                {
                    Debug.LogWarning($"Trigger Proxy is missing on the item {name}. Please add a Trigger Proxy to the item to be able to interact with it.", this);
                }
        }


        public virtual void Equip(SuperItemManager owner)
        {
            if (IsEquipped) return; //If the item is already equipped, do nothing

            AddToManager(owner); //MWC — was [User = owner]: ownership + [On Added To Manager] now share one entry point with the Holster path
            IsEquipped = true;            //Equip the Item
            IsInHolster = false;

            Physics_Disable(DisableCollidersOnEquip.Value); //MWC — colliders per [Disable Colliders On Equip]

            UpdateTriggerLayers(owner); //Update the Trigger Layers to match the owner's layer

            EquipReaction.React(owner);

            //MWC — Dual Wielding: Stance and Strafe are CHARACTER state shared by every equipped Item, so they are
            //recomputed from all of them instead of being written per item. This Item is the preferred one (it is the
            //one that just changed), so its Stance still wins when two Items declare one. See Sync_StanceAndStrafe.
            owner.Sync_StanceAndStrafe(this);

            actionSets.ForEach(set => Set_Equip(set)); //Call the Item Equipped for each action in the Action Set

            Set_Enter(ActiveActionSet); //Activate the Active Action Set

            Input_Connection(true); //Connect all the inputs to the actions when equipped

            if (User != null && User.Aim != null)
            {
                User.Aim.OnSetTarget.AddListener(AimerTarget);  //Add a listener to update the target from the Aimer

                //MWC — and adopt the target the Aimer ALREADY has. OnSetTarget is change-driven and never replays its
                //current value to a new listener, so an Item equipped while a target was locked stayed on a null Target
                //until the player happened to pick a different one: Actions and Processors aimed at nothing meanwhile.
                AimerTarget(User.Aim.AimTarget); //Honors [Update Target From Aimer] itself
            }


            Active = false; //Interactions with the weapon needs to be off when equipped

            //MWC — was never invoked. Fired last, so listeners see a fully equipped item (Action Set entered,
            //inputs connected) rather than a half-configured one. Sends the Current User, like the other Item events.
            OnEquip.Invoke(owner.gameObject);

            //MWC — null-safe: ActiveActionSet is null when the Item never initialized (inactive GameObject)
            Debugging($"Equipped by <color=white>[{owner.name}]</Color> Action Set: {(ActiveActionSet != null ? ActiveActionSet.Name : "None")}", this, "orange");
        }


        /// <summary>
        /// Unequip the Item from the User and send it to the holster
        /// </summary>
        /// <param name="owner"></param>
        public virtual void Unequip(SuperItemManager owner)
        {
            if (!IsEquipped) return; //If the item is not equipped, do nothing

            UnequipReaction.React(owner);

            Debugging($"Unequipped", this, "orange");

            actionSets.ForEach(set => Set_Unequip(set)); //Call the Item Equipped for each action in the Action Set
            Set_Exit(ActiveActionSet); //Deactivate the Active Action Set

            Input_Connection(false);   //Disconnect all the inputs to the actions when unequipped

            if (User != null && User.Aim != null)
                User.Aim.OnSetTarget.RemoveListener(AimerTarget);  //Remove the listener to update the target from the Aimer

            IsEquipped = false;
            Active = true; //Interactions with the weapon needs to be on when unequipped
            CurrentEquipPoint = null;

            //MWC — Dual Wielding: recomputed from what is STILL equipped, after IsEquipped went false so this Item is
            //no longer counted. It used to restore this Item's own private [wasStrafing] snapshot and reset the Stance
            //unconditionally, which stomped the requirements of the other Item the character was still holding:
            //storing the off-hand Sword turned Strafe off while the main-hand Sword was still asking for it.
            //No preferred Item: whatever remains equipped decides.
            owner.Sync_StanceAndStrafe(null);

            //MWC — was never invoked. Fired last, so listeners see the item already released (IsEquipped false,
            //inputs disconnected). Sends the Current User, like the other Item events.
            OnUnequip.Invoke(owner.gameObject);
        }


        public virtual void Drop(SuperItemManager owner)
        {
            transform.SetParent(null); //Remove the item from the parent
            transform.localScale = DefaultTransformValue.localScale; //Restore the default scale
            Physics_Restore(); //Call the Drop method to apply the Drop Logic
            IsInHolster = false;
            Unequip(owner); //Call the Unequip method to apply the Unequip Logic
            Active = true;

            OnDroppedBy.Invoke(owner != null ? owner.gameObject : null); //Fired while the item is released, before the ownership ends

            RemoveFromManager(); //MWC — was the Invoke + [User = null] inline; routed through the mirror of AddToManager
        }

        public virtual void Set_Unequip(ItemActionSet set)
        {
            foreach (var action in set.actions)
                action?.ItemUnequipped(this, set);

            set.OnUnequipReaction.TryReact(User);
        }

        public virtual void Set_Equip(ItemActionSet set)
        {
            foreach (var action in set.actions)
                action?.ItemEquipped(this, set);

            set.OnEquipReaction.TryReact(User);
        }

        /// <summary> Validates that an Action Set can be entered/exited, warning (instead of throwing) when it cannot </summary>
        //MWC — an Item whose GameObject (or any parent) is inactive never runs Awake(), so ActiveActionSet stays null.
        //Warn naming the disabled GameObject instead of throwing a NullReferenceException.
        private bool ValidActionSet(ItemActionSet set, string method)
        {
            if (set != null && set.actions != null) return true;

            var disabled = InactiveGameObject();

            Debug.LogWarning($"<color=orange><B>[{name}] {method}</B></color> has no valid Action Set. " +
                (disabled != null
                    ? $"The Item is <B>inactive in the hierarchy</B> (disabled GameObject: <B>[{disabled.name}]</B>) so it never initialized (Awake) its Action Sets. Enable it to use this Item."
                    : "The <B>Active Action Set</B> is null. Make sure the Item has at least one Action Set."), this);

            return false;
        }

        /// <summary> Returns the topmost disabled GameObject on this Item's branch, or null if the Item is active </summary>
        //MWC — helper for ValidActionSet: points at the GameObject that must be enabled (usually a parent, not the Item)
        private GameObject InactiveGameObject()
        {
            GameObject found = null;

            for (var t = transform; t != null; t = t.parent)
                if (!t.gameObject.activeSelf) found = t.gameObject;

            return found;
        }

        public virtual void Set_Enter(ItemActionSet set)
        {
            if (User == null) return; //If the Current User is null, do nothing

            if (!ValidActionSet(set, nameof(Set_Enter))) return; //MWC — null Action Set (uninitialized/disabled Item)

            set.actions.ForEach(x => x?.Set_Enter(this, set));
            set.OnEnterSetReaction.TryReact(User); //Call the OnEnterReaction if it's valid
                                                   //   set.OnEnterSet.Invoke(User.gameObject); //Invoke the OnEnter event with the item GameObject

            if (set.IKTargets != null) //First 
            {
                foreach (var ikTarget in set.IKTargets)
                    User.IKManager.Target_Add_Global(ikTarget); //Remove the IK Targets from the current user based on the tags
            }


            //MWC — resolve the rig specific Profile (and its Runtime copy) for THIS owner, then initialize that one.
            //Cached per IK Manager, so entering the same Set again on the same character costs nothing.
            var profile = set.ResolveIKProfile(User.IKManager, RuntimeIK);

            if (profile)
            {
                profile.Initialize(User.IKManager); //IMPORTANT TO INITIALIZE THE SET WITH THE CURRENT IK MANAGER
            }
        }

        public virtual void Set_Exit(ItemActionSet set)
        {
            if (User == null) return; //If the Current User is null, do nothing

            if (!ValidActionSet(set, nameof(Set_Exit))) return; //MWC — null Action Set (uninitialized/disabled Item)

            set.actions.ForEach(x => x?.Set_Exit(this, set));
            set.OnExitSetReaction.TryReact(User); //Call the OnExitReaction if it's valid

            if (set.IKTargets != null)
                foreach (var ikTarget in set.IKTargets)
                    User.IKManager.Target_Remove_Global(ikTarget); //Remove the IK Targets from the current user based on the tags
        }

        /// <summary> Disables the Item physics (Rigidbody) and its colliders, as when it is picked (ICollectable) </summary>
        public void Physics_Disable() => Physics_Disable(true);

        /// <summary> Disables the Item physics (Rigidbody). <paramref name="disableColliders"/> false keeps the colliders enabled (e.g. a Shield in the hand) </summary>
        public void Physics_Disable(bool disableColliders)
        {
            //MWC — the colliders are set on BOTH paths. The [Destroy Rb On Pick] branch used to return before touching
            //them, so an Item with that flag kept its colliders live in the hand and in the holster, pushing the
            //character around (dodges, rolls). Physics_Restore re-enables them on both paths.
            InternalColliders(!disableColliders);

            if (DestroyRbOnPick.Value) //Destroy the rigidbody if we are picking the item
            {
                Destroy(rb);
                rb = null;
                return;
            }

            if (rb)
            {
                rb.useGravity = false;

                rb.isKinematic = true; //MWC — was 'false', which left the equipped item non-kinematic while parented + FreezeAll (self-contradictory & the branch below was dead). Equipped items must be kinematic.

                if (rb.isKinematic)
                    rb.collisionDetectionMode = CollisionDetectionMode.Discrete; //Kinematic bodies require a Discrete (non-continuous) collision mode

                rb.constraints = RigidbodyConstraints.FreezeAll; //Freeze the rotation of the item
            }

        }

        public void Physics_Restore()
        {
            // Debug.Log($"Physics_Restore [[{name}]] ");

            if (DestroyRbOnPick.Value && !rb) //It means we need to restore the rigidbody again
            {
                rb = this.gameObject.AddComponent<Rigidbody>(); //Add the rigidbody again

                if (rb)
                {
                    rb.isKinematic = defaultKinematic; //Restore the kinematic value
                    rb.constraints = defaultConstraints; //Restore the constraints
                }
            }

            rigidbodyParameters.RestoreRigidBody(rb); //Restore the rigidbody parameters

            InternalColliders(true);               //enable all colliders
        }


        /// <summary> Enable/Disable internal physic</summary>
        public virtual void InternalColliders(bool enabled)
        {
            foreach (var col in m_colliders) col?.SetEnable(enabled);
        }

        /// <summary> Sets the colliders back to their authored state for the Item's current situation: Equipped → [Disable Colliders On Equip], Holstered → [Disable Colliders On Holster], free in the world → enabled </summary>
        public virtual void Colliders_Restore()
        {
            var enabled = IsEquipped ? !DisableCollidersOnEquip.Value
                        : IsInHolster ? !DisableCollidersOnHolster.Value
                        : true;

            InternalColliders(enabled);
        }

        /// <summary>  When the Interactor is focused, we need to check if we need to change the Action Set based on the Auto Activation Type of the Action Sets.  </summary>
        public void OnInteractorFocused(IInteractable focused)
        {
            if (focused == null) return;

            //MWC: Update the Item Target from the focused interactable when enabled and its ID matches (-1 = any).
            if (TargetFromInteractor.Value && (InteractableID.Value == -1 || InteractableID.Value == focused.Index))
                Target = focused.UserGo;

            // Debug.Log("OnInteractorFocused");
            VerifyAutoSetActivation(SetActivationType.Interactor);
        }

        /// <summary>  When the Interactor is unfocused, we need to check if we need to change the Action Set based on the Auto Activation Type of the Action Sets.  </summary>
        public void OnInteractorUnfocused(IInteractable focused)
        {
            if (focused == null) return;

            //MWC: Clear the Item Target when the matching interactable is unfocused (if enabled and the ID matches; -1 = any).
            if (TargetFromInteractor.Value && ClearTargetOnUnfocus.Value &&
                (InteractableID.Value == -1 || InteractableID.Value == focused.Index))
                Target = null;

            VerifyAutoSetActivation(SetActivationType.Interactor);
        }


        #region Animal Listener Methods

        /// <summary>
        /// Force-end every playing Action of the Active Set. Used by the Set Interruption sources
        /// (Interrupt States/Modes/Stances) so an Action that is waiting on a processor delay never completes.
        /// The Action Set itself stays active, only its playing Actions are killed.
        /// </summary>
        /// <param name="force">True = an EXPLICIT external cancellation (the public API, an authored Reaction): it also wins on an Action's own start frame</param>
        /// <returns> True if at least one Action was playing and got interrupted </returns>
        public bool ActionSet_Interrupt(string source = null, bool force = false)
        {
            if (ActiveActionSet == null || ActiveActionSet.actions == null) return false;

            var interrupted = false;

            foreach (var action in ActiveActionSet.actions)
                if (action != null && action.Interrupt(this, ActiveActionSet, force)) interrupted = true;

            if (interrupted)
            {
                Debugging($"<color=red>Action Set [[{ActiveActionSet.Name}]] Interrupted by {source}</color>", this);
                ActiveActionSet.OnInterruptReaction.TryReact(User);
            }

            return interrupted;
        }

        internal virtual void OnModeStart(int mode, int ability)
        {
            //MWC — the Character started a Mode listed as an interruption source for the Active Set
            if (ActiveActionSet != null && ActiveActionSet.InterruptedByMode(mode))
                ActionSet_Interrupt($"Mode [{mode}]");

            VerifyAutoSetActivation(SetActivationType.CheckMode);

            if (ActiveActionSet != null)
                foreach (var action in ActiveActionSet.actions)
                    action.ModeStart(this, ActiveActionSet, mode, ability); //Call the Item Mode End for each action
        }



        internal virtual void OnModeEnd(int mode, int ability)
        {

            // Debug.Log($"OnModeEnd :  Super Item");

            VerifyAutoSetActivation(SetActivationType.CheckMode);

            if (ActiveActionSet != null)
                foreach (var action in ActiveActionSet.actions)
                    action.ModeEnd(this, ActiveActionSet, mode, ability); //Call the Item Mode End for each action
        }



        internal virtual void OnAnimmation()
        {
            if (ActiveActionSet != null)
                foreach (var action in ActiveActionSet.actions)
                    action.ActionOnAnimation(this, ActiveActionSet); //Call the Item State Change for each action
        }

        internal virtual void OnStateChange(int state)
        {
            //MWC — the Character entered a State listed as an interruption source for the Active Set
            if (ActiveActionSet != null && ActiveActionSet.InterruptedByState(state))
                ActionSet_Interrupt($"State [{state}]");

            VerifyAutoSetActivation(SetActivationType.CheckState);

            if (ActiveActionSet != null)
                foreach (var action in ActiveActionSet.actions)
                    action.StateChanged(this, ActiveActionSet, state); //Call the Item State Change for each action
        }

        internal virtual void OnStanceChange(int stance)
        {
            //MWC — the Character entered a Stance listed as an interruption source for the Active Set
            if (ActiveActionSet != null && ActiveActionSet.InterruptedByStance(stance))
                ActionSet_Interrupt($"Stance [{stance}]");

            VerifyAutoSetActivation(SetActivationType.CheckStance);

            if (ActiveActionSet != null)
                foreach (var action in ActiveActionSet.actions)
                    action.StanceChanged(this, ActiveActionSet, stance); //Call the Item Stance Change for each action
        }

        public void Set_AutoActivation_External() => VerifyAutoSetActivation(SetActivationType.External);

        /// <summary>Re-check the Action Sets listening to [Riding] (called by the Manager on every Rider notification)</summary>
        public void Set_AutoActivation_Riding() => VerifyAutoSetActivation(SetActivationType.Riding);

        //MWC — Ambient world state re-sync mask, used when an Item is equipped. The state these types describe
        //(Riding, State, Stance, Interactor, Target) exists whether or not the Item is in hand, so an Item that is
        //equipped AFTER that state settled has to catch up with it: mounting first and drawing the Bow four frames
        //later used to leave the [Riding] Set unchecked, because only [OnEquip] was verified on equip.
        //[OnAction] and [External] are excluded on purpose: those are events, not state.
        internal const SetActivationType EquipSyncTypes =
            SetActivationType.OnEquip | SetActivationType.Riding | SetActivationType.CheckState |
            SetActivationType.CheckStance | SetActivationType.Interactor | SetActivationType.TargetChanged;


        //MWC — FIRST MATCH WINS: the sets are checked top-down and the first one whose On conditions pass is the one
        //that activates. Previously every matching set called ActionSet(), so when two sets were true at once the LAST
        //one in the list silently overwrote the earlier ones (a broad "Is Dual Wielding" set stealing the activation
        //from a specific "Shield Equipped" one). Reorder the sets in the inspector to change which one takes priority.
        internal void VerifyAutoSetActivation(SetActivationType type)
        {
            ActionSet_CheckIgnored(); //MWC — the Active Set may have just been flagged as Ignored

            foreach (var set in actionSets)
            {
                if (!AutoActivates(set, type)) continue;

                if (set.AutoActivationOn.Valid && set.AutoActivationOn.Evaluate(User))
                {
                    Debugging($"<color=green>Action Set [[{set.Name}]] Auto Activated</color>", this);
                    ActionSet(set);  //Swap the Action Set to the one found
                    return;          //Winner found: the sets below it don't get to override it
                }
            }

            //MWC — no set claimed the activation: let the ACTIVE one go back to Default when its Off conditions pass
            foreach (var set in actionSets)
            {
                if (!AutoActivates(set, type)) continue;

                if (set.Active && set.AutoActivationOff.Valid && set.AutoActivationOff.Evaluate(User)) //Only Check Deactivation when the Set is the one active
                {
                    Debugging($"<color=green>Action Set [[{set.Name}]] Restore to Default</color>", this);
                    ActionSet_RestoreToDefault(); //Return to Default if the conditions are not met and the Action Set is the current one
                    return;
                }
            }
        }

        //MWC — the Set listens to this activation type (None skips it entirely).
        //Bitwise ANY, was HasFlag (= ALL): a combined mask like [EquipSyncTypes] must match a Set that listens to
        //just one of its flags. Identical behaviour for the single-flag calls.
        private static bool AutoActivates(ItemActionSet set, SetActivationType type) =>
            !set.Ignored && set.AutoActivation != 0 && (set.AutoActivation & type) != 0; //MWC — an Ignored Set is invisible to every Auto Activation

        #endregion

        /// <summary> Change the Active action set by name </summary>
        public virtual void ActionSet_Active(string name)
        {
            var ActiveActionSet = actionSets.Find(x => x.Name == name) ?? DefaultActionSet;
            ActionSet(ActiveActionSet);
        }

        public virtual void ActionSet_Active(int index)
        {
            var ActiveActionSet = actionSets[index % actionSets.Count];
            ActionSet(ActiveActionSet);
        }

        /// <summary> Change the Active action set by index </summary>
        public virtual void ActionSet_ActiveByIndex(int index)
        {
            var ActiveActionSet = actionSets[index % actionSets.Count];
            ActionSet(ActiveActionSet);
        }

        /// <summary> Change the Default action set by index </summary>
        public virtual void ActionSet_Default(int index) => DefaultActionSet = actionSets[index % actionSets.Count];

        /// <summary> Change the Default action set by index </summary>
        public virtual void ActionSet_Default(string name) => DefaultActionSet = actionSets.Find(x => x.Name == name) ?? DefaultActionSet;


        /// <summary>  Restore the current action set to the default one </summary>
        public virtual void ActionSet_RestoreToDefault() => ActionSet(DefaultActionSet);

        /// <summary>Leaves the Active Set for the Default one when it has been flagged as [Ignore].</summary>
        public void ActionSet_CheckIgnored()
        {
            if (ActiveActionSet != null && ActiveActionSet.Ignored) ActionSet_RestoreToDefault();
        }

        private void ActionSet(ItemActionSet NextActionSet)
        {
            if (ActiveActionSet == NextActionSet) return; //If the Action Set is the same, do nothing

            NextActionSet ??= DefaultActionSet; //If the Active Action Set is null, set it to the Default Action Set

            //MWC — an Ignored Set can never become the Active one: fall back to the Default Set. The guard against the
            //Default itself is what stops a recursion when no Set is marked Default and the fallback (the first Set) is
            //the Ignored one. [Ignored] already reports false for the Set flagged as Default.
            if (NextActionSet != null && NextActionSet.Ignored && NextActionSet != DefaultActionSet)
            {
                Debugging($"<color=orange>Action Set [[{NextActionSet.Name}]] is Ignored → Restoring the Default Set</color>", this);
                NextActionSet = DefaultActionSet;

                if (ActiveActionSet == NextActionSet) return; //The Default Set is already the active one
            }

            Set_Exit(ActiveActionSet);
            ActiveActionSet.Active = false; //Set the current Action Set to not active
            Input_Connection(false); //Disconnect all the inputs to the actions when unequipped
            ActiveActionSet = NextActionSet;
            Set_Enter(ActiveActionSet); //Activate the Action Set
            ActiveActionSet.Active = true; //Set the Default Action Set to active
            Input_Connection(true); //Connect all the inputs to the actions when equipped

            RefreshHandsInUse(); //MWC — the new Set can declare different [Free Hands] than the one that just exited
        }

        /// <summary>Re-evaluate which hands the Items take (Equip Point side + Active Set + playing Actions).</summary>
        internal void RefreshHandsInUse()
        {
            if (User != null) User.UpdateReinHands();
        }

        #region MWC — Runtime State Queries (used by the [Super Item] Conditions)

        /// <summary> True when an Action of the Active Set is currently playing.
        /// Pass null to check for ANY playing Action on this item </summary>
        public bool IsActionPlaying(ItemActionID actionID)
        {
            if (ActiveActionSet == null || ActiveActionSet.actions == null) return false;

            foreach (var action in ActiveActionSet.actions)
            {
                if (action == null || !action.IsPlaying) continue;
                if (actionID == null || action.ActionID == actionID) return true;
            }
            return false;
        }

        /// <summary> True when a playing Action of the Active Set passes the Include/Exclude list. Empty list = ANY playing Action </summary>
        public bool IsActionPlaying(IDListCheck<ItemActionID> actions)
        {
            if (actions == null || actions.Empty) return IsActionPlaying((ItemActionID)null);
            if (ActiveActionSet == null || ActiveActionSet.actions == null) return false;

            foreach (var action in ActiveActionSet.actions)
            {
                if (action == null || !action.IsPlaying) continue;
                if (actions.Check(action.ActionID)) return true;
            }
            return false;
        }

        /// <summary> True when the Active Action Set matches the given name.
        /// An empty/null name only checks that the item HAS an Active Set </summary>
        public bool IsActionSetActive(string setName)
        {
            if (ActiveActionSet == null) return false;
            return string.IsNullOrEmpty(setName) || ActiveActionSet.Name == setName;
        }

        /// <summary> Item held by the User on the OPPOSITE side of this one (Dual Wielding partner).
        /// Null when this item is not equipped, has no Side, or nothing is held on the other hand </summary>
        public SuperItem DualWieldPartner
        {
            get
            {
                if (!IsEquipped || User == null) return null;

                var side = EffectiveSide;
                if (side == WeaponSide.None) return null;

                foreach (var point in User.EquipPoints)
                {
                    if (point == null || !point.HasItem || point.Item == this) continue;

                    //Only points with an authored Side count, so Head/None points are never a "second hand"
                    if (point.Side != WeaponSide.None && point.Side != side) return point.Item;
                }
                return null;
            }
        }
        #endregion

        #region Interactor Stuff
        /// <summary>
        /// Interactor ID NOT
        /// </summary>
        public int ID => ItemType;

        public bool Interact(IInteractable interactable)
        {
            if (interactable != null && interactable.Active) //: corrected null check for unity object interface type
            {
                //  Debug.Log($"------------ID : [{ID}], gameObject: {gameObject}   ");
                return interactable.Interact(ID, gameObject);
            }

            return false; //If the Interactable is null or not active, return false
        }

        public void UnFocus(IInteractable interactable) { } //Empty from the IInteractor Interface
        public void Focus(IInteractable interactable) { } //Empty from the IInteractor Interface
        public void Restart() { } //Empty from the IInteractor Interface
        #endregion

        #region Interactable Stuff
        public bool SingleInteraction => true;

        public bool Auto { get => autoPick.Value; set => autoPick.Value = value; }

        public int Index => ID;

        public bool Interact(IInteractor interactor)
        {
            if (IsEquipped) return false; //It cannot be an interactable while the Item is equipped. It transform to an Interactor
            return Interact(interactor.ID, interactor.UserGo);
        }

        public bool Interact(int InteracterID, GameObject interactor)
        {
            return true; //Empty from the IInteractable Interface, return true to indicate that the interaction was successful
        }


        //Empty from the IInteractor Interface
        public void Interact()
        {
            //  Debug.Log("INTERACTABLE: Interact()");
        }

        /// <summary>Focused by an Interactor. Tracks the focuser so OnFocused fires once per Interactor and [Focused] stays true while anyone focuses the item</summary>
        public virtual void Focus(IInteractor focuser)
        {
            if (focuser == null || !focusedBy.Add(focuser)) return; //Already focusing: never fire OnFocused twice

            FocusedBy = focuser;
            Focused = true;
            InvokeFocused(focuser.UserGo);
        }

        /// <summary>Unfocused by an Interactor. Ignored when that Interactor was not focusing the item, so OnUnfocused is never fired without its OnFocused</summary>
        public virtual void UnFocus(IInteractor focuser)
        {
            if (focuser == null || !focusedBy.Remove(focuser)) return; //It was not focusing this item: nothing to undo

            if (FocusedBy == focuser) FocusedBy = focusedBy.FirstOrDefault(); //Hand the reference to a remaining focuser
            Focused = focusedBy.Count > 0;
            InvokeUnfocused(focuser.UserGo);
        }

        /// <summary>Tells every Interactor still focusing this item to let it go (each one calls back UnFocus here), then clears the focus state</summary>
        protected virtual void ReleaseFocusers()
        {
            if (focusedBy.Count > 0)
            {
                foreach (var focuser in focusedBy.ToArray())
                    if (!focuser.IsUnityRefNull()) focuser.UnFocus(this);
            }

            focusedBy.Clear();
            focusNotified.Clear();
            FocusedBy = null;
            Focused = false;
        }

        /// <summary> True when the focuser passes the [Focus Tags] filter (empty filter allows everyone) </summary>
        public bool FocusTagsMatch(GameObject focuser)
        {
            if (FocusTags == null || !FocusTags.ValidObjects) return true;
            return focuser != null && focuser.HasMalbersTagInParent(FocusTags.tags);
        }

        /// <summary> Fires OnFocused and the Focused Reaction for the focuser, if it passes the [Focus Tags] filter and did not get it already </summary>
        public void InvokeFocused(GameObject focuser)
        {
            if (!FocusTagsMatch(focuser)) return;
            if (focuser != null && !focusNotified.Add(focuser)) return; //This focuser already received OnFocused

            OnFocused?.Invoke(focuser);
            FocusedByReaction.React(focuser);
        }

        /// <summary> Fires OnUnfocused and the Unfocused Reaction for the focuser, once per OnFocused it received (paired: never fires for a focuser that was not notified) </summary>
        public void InvokeUnfocused(GameObject focuser)
        {
            if (!ReleaseNotified(focuser)) return;

            OnUnfocused?.Invoke(focuser);
            UnFocusedByReaction.React(focuser);
        }

        /// <summary>Removes the focuser from the notified set. Also matches a notified object in the same hierarchy, since the Manager (Pick/Holster/Equip) and its Interactor (trigger exit) may sit on different GameObjects of one character</summary>
        private bool ReleaseNotified(GameObject focuser)
        {
            if (focuser == null || focusNotified.Count == 0) return false;
            if (focusNotified.Remove(focuser)) return true;

            foreach (var notified in focusNotified)
            {
                if (notified && (notified.transform.SameHierarchy(focuser.transform) || focuser.transform.SameHierarchy(notified.transform)))
                {
                    focusNotified.Remove(notified);
                    return true;
                }
            }
            return false;
        }

        /// <summary> Fires OnHolstered and the Holstered Reaction for the user that stored the item in a holster </summary>
        public void InvokeHolstered(GameObject user)
        {
            OnHolstered?.Invoke(user);
            HolsteredReaction.React(user);
        }

        /// <summary> Fires OnUnHolstered and the UnHolstered Reaction for the user that removed the item from a holster </summary>
        public void InvokeUnHolstered(GameObject user)
        {
            OnUnHolstered?.Invoke(user);
            UnHolsteredReaction.React(user);
        }
        #endregion

        #region IK
        //MWC — [CurrentIKProfile], not [IKProfile]: the rig specific Profile resolved for the current owner
        internal virtual void AnimatorIKAction(Animator animator, float GlobalWeight, float deltatime, int layerIndex)
        {
            if (UserGo != null && ActiveActionSet != null && ActiveActionSet.CurrentIKProfile != null && animator != null)
                ActiveActionSet.CurrentIKProfile.set.OnAnimatorIK(animator, GlobalWeight, deltatime, layerIndex);
        }

        internal virtual void LateUpdateAction(Animator animator, float GlobalWeight, float deltatime)
        {
            if (UserGo != null && ActiveActionSet != null && ActiveActionSet.CurrentIKProfile != null && animator != null)
            {
                //MWC — cache the animated pose BEFORE the Set writes its IK, exactly like the IKManager does for its
                //own Sets. Item driven Sets reach the Manager through [UpdateIKAction], which never called CacheValues:
                //IKGenericLookAt kept m_AnimatedRotation at default(Quaternion) = (0,0,0,0), an invalid rotation, so
                //every Slerp starting from the animated pose collapsed onto the target and BOTH the Processor Weight
                //and the Rotation Blend had no effect at all.
                ActiveActionSet.CurrentIKProfile.set.CacheValues(animator);

                ActiveActionSet.CurrentIKProfile.set.LateUpdate(animator, GlobalWeight, deltatime);
            }
        }

        //MWC — the Runtime copies of the IK Profiles are ScriptableObject instances owned by this Item: they are not
        //collected with the GameObject, so they have to be released explicitly.
        protected virtual void OnDestroy()
        {
            if (actionSets != null)
                foreach (var set in actionSets) set?.ReleaseIKProfile();
        }
        #endregion

        //-------EDITOR Stuffs----------------------------------------------------------------------------------------------------------------------------------------

        public bool debug;
        public Color DebugColor = Color.red; //Color to be used in the Debugging method

        //-------EDITOR Stuffs----------------------------------------------------------------------------------------------------------------------------------------


        //MWC — Single source for the Trigger Proxy list: every proxy under the item, inactive ones included, so a proxy
        //that starts disabled (e.g. the Parry trigger) is still wired to the Enter/Exit callbacks and can be switched
        //on later by ActivateDamager. GetComponentsInChildren never returns null entries, which is the whole point.
        internal void FindTriggers()
        {
            m_Triggers = GetComponentsInChildren<TriggerProxy>(true);
        }

        //MWC — This used to bail out when the list already had entries, so [Find] and the Context Menu did NOTHING on
        //an Item that was already set up: colliders added or deleted afterwards were never picked up. It now always
        //REBUILDS both lists from the Item's children, overriding whatever was there.

        /// <summary>Rebuilds (overrides) the Colliders and Trigger Proxies lists from the Item's children.</summary>
        [ContextMenu("Find Colliders and Triggers")]
        internal void FindColliders()
        {
            var OnlyColliders = GetComponentsInChildren<Collider>(true); //Get all the colliders in the item

            List<Collider> m_colliders = new();

            for (int i = 0; i < OnlyColliders.Length; i++)
            {
                if (!OnlyColliders[i].isTrigger)
                {
                    m_colliders.Add(OnlyColliders[i]); //Store the colliders. Triggers are derived by FindTriggers()
                }
            }

            this.m_colliders = m_colliders.ToArray();
            FindTriggers(); //MWC — same derivation the runtime uses

            MTools.SetDirty(this);
        }


        //MWC — Split in two entry points: Reset()/OnValidate() keep the old non-destructive behaviour (they must never
        //overwrite an authored Mesh), while the Inspector [Find] button and the Context Menu are an explicit user
        //action and therefore OVERRIDE whatever is stored, the same way [Find] does for the Colliders.

        /// <summary>Finds the Display Mesh only when the Item has none. Used by Reset() and OnValidate().</summary>
        private void FindDisplayMesh() => FindDisplayMesh(false);

        /// <summary>Finds the Display Mesh, its Transform and Scale, overriding the stored ones.</summary>
        [ContextMenu("Find Display Mesh")]
        internal void ForceFindDisplayMesh() => FindDisplayMesh(true);

        /// <summary>Finds the Display Mesh, Transform and Scale from the Item's renderers.</summary>
        /// <param name="overrideCurrent">Replace the stored Mesh instead of keeping it.</param>
        private void FindDisplayMesh(bool overrideCurrent)
        {
            if (DisplayMesh != null && !overrideCurrent) return; //If the Display Mesh is already set, do nothing

            var staticMesh = GetComponentInChildren<MeshFilter>(false);

            var skinnedMesh = GetComponentInChildren<SkinnedMeshRenderer>(false);
            if (skinnedMesh != null)
            {
                DisplayMesh = skinnedMesh.sharedMesh;
                DisplayTransform = skinnedMesh.transform;
            }

            else if (staticMesh != null)
            {
                DisplayMesh = staticMesh.sharedMesh;
                DisplayTransform = staticMesh.transform;
            }
            DisplayScale = Vector3.one;

            //MWC — no mesh found (e.g. adding SuperItem to a plain GameObject): DisplayTransform is unassigned, so skip the scale walk to avoid a NullReferenceException in Reset()/OnValidate()
            if (DisplayTransform == null) return;

            var DTrans = DisplayTransform;

            while (DTrans != null && DTrans != this.transform) //MWC — guard against DTrans becoming null if DisplayTransform is not a descendant of this.transform
            {
                DisplayScale.Scale(DTrans.localScale);

                DTrans = DTrans.parent;

                // Debug.Log($"Mesh : DisplayScale {DTrans.localScale}");
            }

            if (DisplayMesh != null) Debug.Log($"[{name}]  Mesh Display Found: DisplayScale {DisplayScale}");
        }


        private void Reset()
        {

#if UNITY_EDITOR

            actionSets = new List<ItemActionSet>()
            {
                new ("Default")
                {
                    actions = new List<ItemAction>()
                    {
                        new() { name = new("Attack"),  ActionID = MTools.GetInstance<ItemActionID>("Attack"),   }
                    }, Active = true, Default = true
                }
            };

            UnequipMode = MTools.GetInstance<ModeID>("Weapon Store");
            EquipMode = MTools.GetInstance<ModeID>("Weapon Draw");


            EquipPoint = MTools.GetInstance<EquipPointID>("Right Hand");
            holster = MTools.GetInstance<HolsterID>("Right Holster");

            PickMode = new IDPair<ModeID, int>(MTools.GetInstance<ModeID>("PickUp"), 1);
            DropMode = new IDPair<ModeID, int>(MTools.GetInstance<ModeID>("PickUp"), 2);

            //MWC — HolsterOffset/EquipPointOffset init removed: offsets live in the SuperItemOffsets asset now

            if (rb == null) rb = base.gameObject.GetComponent<Rigidbody>();

            MTools.SetLayer(transform, 30); //Set the layer to Items Layer


            //find the OnFocused Prefab and instantiate it
            MTools.AddOnFocusedPrefab(transform, OnFocused, OnUnfocused);


            rb = GetComponent<Rigidbody>();

            if (rb == null)
                rb = gameObject.AddComponent<Rigidbody>();

            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.05f;

            FindDisplayMesh();
#endif
        }


        private void OnValidate()
        {
            if (Application.isPlaying) return;

            foreach (var s in actionSets)
            {
                if (s == null) continue;

                foreach (var a in s.actions)
                {
                    if (a == null || a.processors == null || a.processors.Length == 0) continue;
                    foreach (var p in a.processors)
                    {
                        if (p == null) continue;
                        p.OnValidate(this, s, a);
                    }
                }
            }

            //MWC — chamber clamps removed: Chamber is now a Stat (min/max clamping is built into Stat.SetValue)

            //MWC — secondary-offset scale guard removed: the Dual Wield point is a single EquipPointID now
            //(its offset lives in the SuperItemOffsets asset)

            FindTriggers(); //MWC — keep the Inspector list matching the hierarchy at edit time too

            FindDisplayMesh();
        }


        /// <summary>Editor only console log for this Super Item, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void Debugging(string value, Object obj, string m_color = "yellow")
        {
#if UNITY_EDITOR
            if (debug)
                MDebug.Log($"<B>[SI: {name}]</B> → <color={m_color}> <B>[{value}]</B> </color>", obj);
#endif
        }


        private void OnDrawGizmos()
        {
            //MWC — everything that used to live in OnDrawGizmosSelected (the Conditions and the Processors' SELECTED
            //gizmos) is drawn from here instead. Unity calls OnDrawGizmosSelected only for the EXACT GameObject
            //selected, and the Item ROOT is rarely the one you click — picking its mesh child hid every one of them.
            //This callback runs for every Item, so it can check the whole selection hierarchy itself.
#if UNITY_EDITOR
            var selected = InSelectionHierarchy();

            if (selected)
            {
                DrawConditionsGizmos();
                DrawIKSetGizmos();
            }
#endif

            foreach (var set in actionSets)
            {
                foreach (var action in set.actions)
                {
                    if (!action.debug) continue;

                    foreach (var processor in action.processors)
                    {
                        processor?.OnDrawGizmos(this, set, action);

#if UNITY_EDITOR
                        if (selected) processor?.OnDrawGizmosSelected(this, set, action);
#endif
                    }
                }
            }
        }

#if UNITY_EDITOR
        /// <summary> Gizmos of the Active Set's IK Profile (Weight Processors + selected IK Processor), same as the IK Manager draws for its own Sets </summary>
        private void DrawIKSetGizmos()
        {
            //The IK Processors cache their bones on Start, which only runs once the Item has an owner
            if (!Application.isPlaying || User == null || User.IKManager == null || User.Anim == null) return;

            var profile = ActiveActionSet?.CurrentIKProfile;
            if (profile == null) return;

            var set = profile.set;
            if (set == null || !set.active || !set.Gizmos) return;

            if (set.weightProcessors != null)
                foreach (var weightP in set.weightProcessors)
                    weightP?.OnDrawGizmos(set, User.Anim);

            if (set.Processors == null) return;

            var index = set.SelectedIKProcessor;

            if (index >= 0 && index < set.Processors.Count)
            {
                var link = set.Processors[index];
                if (link != null && link.Active) link.OnDrawGizmos(User.IKManager, set, User.Anim, User.IKManager.Weight);
            }
        }

        /// <summary>
        /// Conditions gizmos, drawn whenever the Item is ANYWHERE in the selection: the root itself, a child of
        /// it (the mesh, the aim origin — what you actually click in the Scene view), or the character holding it.
        /// They used to live in <see cref="OnDrawGizmosSelected"/>, which Unity only calls for the EXACT GameObject
        /// selected, so picking the Item's mesh child silently hid every Condition gizmo.
        /// </summary>
        private void DrawConditionsGizmos()
        {
            //MWC — the Conditions target: the character while equipped, the Item itself at edit time (no User yet).
            //It's what every Condition receives on its DrawGizmos (E.g. [Aim Angle] draws its allowed arc there).
            var ConditionsOwner = User != null ? User : (Component)this;

            foreach (var set in actionSets)
            {
                //MWC — Set Conditions are drawn once per Set, not once per Action. Each Condition still gates
                //itself on its own [Debug] toggle inside Conditions2.Gizmos
                set.SetConditions.Gizmos(ConditionsOwner);

                foreach (var action in set.actions)
                {
                    if (!action.debug) continue;

                    //MWC — each Action's Conditions, drawn against the target they are evaluated with
                    action.ConditionsOnOwner.Gizmos(ConditionsOwner);
                    action.ConditionsOnTarget.Gizmos(Target != null ? Target.transform : ConditionsOwner);
                }
            }
        }

        /// <summary> Is this Item part of the current selection, as the selected object, its child or its parent? </summary>
        private bool InSelectionHierarchy()
        {
            var selected = UnityEditor.Selection.activeTransform;

            //IsChildOf returns true for the transform itself, so the root being selected is covered by the first check
            return selected != null && (selected.IsChildOf(transform) || transform.IsChildOf(selected));
        }
#endif


        [SerializeField, HideInInspector] private int Editor_Tabs1;
        [SerializeField, HideInInspector] private bool ShowWarnings = true; //MWC — show/hide the Warnings help boxes in the inspector
        [SerializeField, HideInInspector] private int Editor_Tabs2;

        //MWC — Action tabs (Processors/Conditions/Reactions/IK/[Set] Auto) are stored per ITEM, not per Action.
        //They used to live on each ItemAction, so selecting another Action jumped to whatever tab that Action had
        //open last. Keeping the index here means the tab you are working in stays put while you browse Actions.
        [SerializeField, HideInInspector] private int Editor_ActionTabs;
    }

    //MWC — the SecondaryEquipPoint wrapper class is gone: with its Offset and Anim Item Type moved to the
    //SuperItemOffsets asset it only held an ID, so SuperItem stores a single EquipPointID directly.
}
