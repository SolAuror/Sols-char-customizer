// Last Change: 9/21/2026 9:40am
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using UnityEngine;
using MalbersAnimations.Reactions;
using System.Collections.Generic;
using MalbersAnimations.Conditions;



#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Controller
{
    [AddComponentMenu("Malbers/Interaction/Pick Up - Drop")]
    public class MPickUp : MonoBehaviour, IAnimatorListener
    {
        [System.Serializable]
        public struct ExtraHolder
        {
            public Transform transform;
            public Vector3 position;
            public Vector3 rotation;
        }

        [RequiredField, Tooltip("Trigger used to find Items that can be picked Up")]
        public Collider PickUpArea;
        [SerializeField, Tooltip("When an Item is Picked and Hold, the Pick Trigger area will be disabled")]
        private BoolReference m_HidePickArea = new(true);
        //public bool AutoPick { get => m_AutoPick.Value; set => m_AutoPick.Value = value; }

        [Tooltip("Transform to Parent the Picked Item")]
        public Transform Holder;
        public Vector3 PosOffset;
        public Vector3 RotOffset;

        [Tooltip("Conditions to allow the Pick Up Action")]
        public Conditions2 PickUpCondition;

        [Tooltip("Conditions to allow the Drop Action")]
        public Conditions2 DropCondition;
        [Tooltip("Can the Character Pick Up Items?")]
        [SerializeField] private BoolReference canPick = new(true);
        [Tooltip("Can the Character Drop Items?")]
        [SerializeField] private BoolReference canDrop = new(true);
        [Tooltip("MWC: If the focused Item is already held by another character, allow this character to take it away " +
            "(forcing the previous owner to cleanly drop it first).\nWhen OFF, items held by someone else cannot be focused or picked until dropped.")]
        [SerializeField] private BoolReference m_AllowTakeAway = new(false);

        public List<ExtraHolder> extraHolders;

        [Tooltip("Check for tags on the Pickable items")]
        public MTags Tags;


        [Tooltip("Layer for the Interact with colliders")]
        [SerializeField] private LayerReference Layer = new(-1);
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        /// <summary> Real Root of the Picker Object  </summary>
        public Transform Root { get; set; }

        [Tooltip("Invokes a reaction if the Pickable is a collectable")]
        [SerializeReference] public Reaction CollectableReaction;

        [Tooltip("Ignore the Item Pick Delay, so the item can be picked instantly")]
        public BoolReference IgnoreItemPickDelay = new(false);

        [Tooltip("Ignore the Item Drop Delay, so the item can be dropped instantly")]
        public BoolReference IgnoreItemDropDelay = new(false);

        // [Header("Events")]
        public BoolEvent CanPickUp = new();
        public GameObjectEvent OnItemPicked = new();
        public GameObjectEvent OnItemDrop = new();
        public GameObjectEvent OnFocusedItem = new();
        public IntEvent OnPicking = new();
        public IntEvent OnDropping = new();

        public bool debug;
        public float DebugRadius = 0.02f;
        public Color DebugColor = Color.yellow;

        protected ICharacterAction character;

        /// <summary>  Who is the owner of this Pick Up Script  </summary>
        public GameObject Owner { get; private set; }

        [SerializeField] private TriggerProxy Proxy;

        /// <summary>Does the Animal is holding an Item</summary>

        public bool Has_Item => Item != null;

        protected bool PickingItem = false;

        [SerializeField] private ICollectable item;
        public virtual ICollectable Item
        {
            get => item;
            set
            {
                item = value;
                //   OnItem.Invoke(item != null ? item.gameObject : null);
                //  Debug.Log("item: " + item);
            }
        }

        private ICollectable focusedItem;
        public virtual ICollectable FocusedItem
        {
            get => focusedItem;
            set
            {
                focusedItem = value;
                OnFocusedItem.Invoke(focusedItem?.gameObject);
                CanPickUp.Invoke(focusedItem != null);
            }
        }

        public bool CanPick { get => canPick; set => canPick = value; }
        public bool CanDrop { get => canDrop; set => canDrop = value; }

        /// <summary>Can this Picker take an Item that is already held by another Picker?</summary>
        public bool AllowTakeAway { get => m_AllowTakeAway.Value; set => m_AllowTakeAway.Value = value; }

        protected virtual void Awake()
        {
            character = gameObject.FindInterface<ICharacterAction>();

            Owner = character != null ? character.gameObject : gameObject;  //Set the Owner of the Pick Up Script

            CheckTriggerProxy();
        }

        protected virtual void CheckTriggerProxy()
        {
            Root = transform.FindObjectCore();

            if (PickUpArea)
            {
                Proxy = TriggerProxy.CheckTriggerProxy(PickUpArea, Layer, triggerInteraction, Root, false);
                Proxy.tags.Merge(Tags);
            }
            else
            {
                Debug.LogWarning("Please set a Pick up Area");
            }
        }

        protected virtual void OnEnable()
        {
            if (Proxy == null)
            {
                Debug.LogWarning("Pick Up Area is not set. Please add a [Trigger] Collider. Disabling component.");
                enabled = false;
                return;
            }


            Proxy.OnTrigger_Enter.AddListener(OnGameObjectEnter);
            Proxy.OnTrigger_Exit.AddListener(OnGameObjectExit);

            if (Has_Item) PickUpItem();         //If the animal has an item at start then make all the stuff to pick it up
        }

        protected virtual void OnDisable()
        {
            if (Proxy == null)
            {
                return;
            }

            Proxy.OnTrigger_Enter.RemoveListener(OnGameObjectEnter);
            Proxy.OnTrigger_Exit.RemoveListener(OnGameObjectExit);
        }

        protected virtual void OnGameObjectEnter(Collider col)
        {
            if (!MTools.Layer_in_LayerMask(col.gameObject.layer, Layer)) return;   //The collider is not in the correct layer, ignore it

            //MWC: route through the shared focus logic (validation, unfocus previous, auto-pick)
            SetFocusedItem(col.FindInterface<ICollectable>());
        }

        /// <summary> Central focus routine. Validates the item, unfocuses any previous one and auto-picks if needed. </summary>
        protected virtual void SetFocusedItem(ICollectable newItem)
        {
            if (!CanFocus(newItem)) return;

            //If we are choosing another focused Item then unfocused the old one
            if (newItem != FocusedItem && FocusedItem != null)
                FocusedItem.SetFocused(Owner, false);

            FocusedItem = newItem;
            FocusedItem.SetFocused(Owner, true);

            Debugging("Focused Item - " + FocusedItem.gameObject.name);

            if (FocusedItem.AutoPick) TryPickUp();
        }

        /// <summary> Can this Picker focus the given item? Rejects null/inactive items and items already
        /// held by another Picker (unless Take Away is enabled). </summary>
        protected virtual bool CanFocus(ICollectable newItem)
        {
            if (newItem == null || !newItem.Active) return false;

            //Item is held by a different Picker and we are not allowed to take it away
            if (newItem.IsPicked && newItem.Picker != null && newItem.Picker != Owner && !AllowTakeAway)
                return false;

            //MWC: honor the Pickable's own CanBePickedIf condition at focus time, so a non-pickable item
            //is never focused and its OnFocused/OnFocusedBy events are not invoked
            if (newItem is Pickable pickable && pickable.CanBePickedIf.Valid && !pickable.CanBePickedIf.Evaluate(this))
                return false;

            return true;
        }

        /// <summary> True when this Picker may take the item right now — it is free, already ours, or Take Away is enabled. </summary>
        protected virtual bool ItemIsFree(ICollectable it) =>
            it != null && (!it.IsPicked || it.Picker == null || it.Picker == Owner || AllowTakeAway);


        public virtual void FocusItem(Component newObject)
        {
            if (newObject == null) //Means there's no New Focused Item
            {
                UnfocusedCurrentItem();
                return;
            }
            FocusItem(newObject.gameObject);
        }

        public virtual void FocusItem(GameObject newObject)
        {
            if (newObject == null) //Means there's no New Focused Item
            {
                UnfocusedCurrentItem();
                return;
            }

            var newItem = newObject.FindInterface<ICollectable>();

            if (newItem == null || !MTools.Layer_in_LayerMask(newItem.gameObject.layer, Layer.Value))  //there's no Pickable Item or the layer is not the correct one
            {
                // Debug.Log("there's no Pickable Item or the layer is not the correct one");
                UnfocusedCurrentItem();
                return;
            }

            SetFocusedItem(newItem); //MWC: reuse the shared focus logic
        }

        private void UnfocusedCurrentItem()
        {
            if (FocusedItem != null)
            {
                Debugging("Unfocused Item - " + FocusedItem.gameObject.name);
                FocusedItem.SetFocused(Owner, false);
                FocusedItem = null;
            }
        }

        protected virtual void OnGameObjectExit(Collider col)
        {
            //TriggerProxy releases destroyed colliders through this event too, so the null check must come first
            if (col != null && !MTools.Layer_in_LayerMask(col.gameObject.layer, Layer)) return;   //The collider is not in the correct layer, ignore it
            if (PickingItem) return; //Do not unfocus the item while it is being picked up (Aligning to the Holder)

            var exiting = col != null ? col.FindInterface<ICollectable>() : null;

            if (FocusedItem != null)
            {
                if (exiting != null && exiting != FocusedItem)
                {
                    exiting.SetFocused(Owner, false); //Another item left, make sure it stays unfocused
                    return;
                }

                //The focused item has another collider still inside the trigger (multi-collider items), keep the focus
                if (Proxy.StillInside(FocusedItem, col, ResolveCollectable)) return;

                UnfocusedCurrentItem();
            }

            FocusNextItem(exiting); //Hand the focus to the next item still inside the trigger
        }

        /// <summary>Resolves the Collectable a tracked collider belongs to (same lookup the Enter uses)</summary>
        private static ICollectable ResolveCollectable(Collider c) => c.FindInterface<ICollectable>();

        /// <summary>Focuses the closest valid item still inside the Pick Up Area when nothing is focused. [exclude] is the item that just left or was picked</summary>
        protected virtual void FocusNextItem(ICollectable exclude = null)
        {
            if (FocusedItem != null) return;                                    //Something is already focused
            if (Proxy == null || !Proxy.Active || !PickUpArea.enabled) return;  //The area is hidden (holding an item) or the proxy is off

            ICollectable best = null;
            float bestDist = float.MaxValue;
            var center = PickUpArea.bounds.center;

            foreach (var c in Proxy.Colliders)
            {
                if (c == null || !c.enabled) continue;

                var candidate = ResolveCollectable(c);

                if (candidate == null || candidate == exclude || candidate == Item) continue; //Skip the item that left and the one we hold
                if (!CanFocus(candidate)) continue;

                var dist = (c.bounds.center - center).sqrMagnitude;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }

            if (best != null)
            {
                Debugging("Focus Next Item - " + best.gameObject.name);
                SetFocusedItem(best);
            }
        }

        public virtual void TryPickUpDrop()
        {
            if (character != null && character.IsPlayingAction) return; //Do not try if the Character is doing an action

            if (!Has_Item) TryPickUp();
            else TryDrop();
        }

        public virtual void TryDrop()
        {
            if (!enabled) return; //Do nothing if this script is disabled
            if (!Has_Item) return; //MWC: nothing to drop (Item may be null after a take-away) - avoids NRE
            if (!DropCondition.Evaluate(Item.transform)) return;   //Check the Drop Conditions
            if (!CanDrop) return; //Check if the Character can Drop Items

            if (item != null && !item.InCoolDown)
            {
                if (character != null && !character.IsPlayingAction)
                {
                    Item.PreDrop(gameObject);
                }

                Debugging("Item Try Drop - " + Item.gameObject.name);

                if (IgnoreItemDropDelay.Value)
                    DropItem();
                else if (!item.ByAnimation)
                    Invoke(nameof(DropItem), Item.DropDelay);
            }
        }

        /// <summary>  Tries the pickup logic checking all the correct conditions if the character does not have an item.  </summary>
        public virtual void TryPickUp()
        {
            if (!isActiveAndEnabled) return;                                //Do nothing if this script is disabled
            if (FocusedItem == null) return;                                //No Focused Item to Pick
            if (!CanPick) return; //Can Pick is disabled 

            if (!PickUpCondition.Evaluate(FocusedItem.transform)) return;
            if (FocusedItem is Pickable picable && picable.CanBePickedIf.Valid && !picable.CanBePickedIf.Evaluate(this)) return; //Check the Pickable's own conditions

            //MWC: don't even start picking an item that is held by another Picker (unless Take Away is enabled)
            if (!ItemIsFree(FocusedItem))
            {
                FocusedItem.PickedFailed(Owner);
                Debugging("Item held by another Picker - Pick Failed - " + FocusedItem.transform.name);
                return;
            }

            if (!FocusedItem.Active)
            {
                FocusedItem.PickedFailed(character.gameObject);
                Debugging("Item Picked Failed - " + FocusedItem.transform.name, FocusedItem.transform);
            }
            else if (!FocusedItem.InCoolDown)
            {
                //Try Picking UP WHEN THE CHARACTER IS NOT MAKING ANY ANIMATION
                if (character != null && !character.IsPlayingAction)
                {
                    //Align_Item();
                    PickingItem = true;
                    FocusedItem.PrePicked(character.gameObject); //Do the On Picked First  
                }
                Debugging("Try Pick Up");

                if (IgnoreItemPickDelay.Value)
                    PickUpItem();
                else if (!FocusedItem.ByAnimation)
                    Invoke(nameof(PickUpItem), FocusedItem.PickDelay);
            }
        }


        /// <summary> Drops the item logic</summary>
        public virtual void DropItem()
        {
            if (!enabled) return; //Do nothing if this script is disabled
            if (!Has_Item) return;
            if (!CanDrop) return; //Check if the Character can Drop Items

            if (!DropCondition.Evaluate(Item.transform)) return;   //Check the Drop Conditions

            Debugging("Item Dropped - " + Item.gameObject.name);

            Item.Drop();                                    //Tell the item is being dropped
            OnItemDrop.Invoke(Item.gameObject);
            OnDropping.Invoke(Item.ID);                     //Invoke the method

            Item = null;                                    //Remove the Item

            if (m_HidePickArea.Value)
                PickUpArea.enabled = (true);                //Enable the Pick up Area

            if (FocusedItem != null && !FocusedItem.AutoPick) Proxy.ResetTrigger();

            FocusNextItem(); //Hands are free again: focus the closest item still inside the area (no-op when something is focused or the area was hidden)
        }


        /// <summary>Pick Up Logic. It can be called by the Animator</summary>
        public virtual void PickUpItem()
        {
            if (!isActiveAndEnabled) return; //Do nothing if this script is disabled
            if (!CanPick) return; //Can Pick is disabled 


            Item ??= FocusedItem; //Check for the Picked Item

            if (Item != null)
            {
                //MWC: The Item is currently held by a DIFFERENT Picker
                if (Item.IsPicked && Item.Picker != null && Item.Picker != Owner)
                {
                    if (!AllowTakeAway) //Blocked: cannot steal an item another character is holding
                    {
                        Item.PickedFailed(Owner);
                        Debugging("Item already held by another Picker - Pick Failed - " + Item.gameObject.name);
                        Item = null;            //Clear the stale reference so we never think we hold it
                        PickingItem = false;
                        UnfocusedCurrentItem(); //Release focus, it belongs to someone else
                        return;
                    }

                    Item.ForceDrop();           //Take Away: force the current owner to cleanly drop it first

                    //MWC: if the previous owner refused to drop (its drop conditions/cooldown blocked it),
                    //abort instead of stealing with stale ownership on the other Picker
                    if (Item.IsPicked && Item.Picker != null && Item.Picker != Owner)
                    {
                        Item.PickedFailed(Owner);
                        Debugging("Take Away failed - current owner could not drop - " + Item.gameObject.name);
                        Item = null;
                        PickingItem = false;
                        UnfocusedCurrentItem();
                        return;
                    }
                }

                if (!Item.Active) //Check first if the item cannot be picked
                {
                    Item.PickedFailed(Owner);
                    Debugging("Item Picked Failed - " + Item.gameObject.name, Item.gameObject);
                    Item = null;                //MWC: clear stale reference on failure
                    PickingItem = false;        //MWC: don't leave the pick flag stuck
                    return;
                }

                Debugging("Item Picked - " + Item.gameObject.name);

                //if (TryAlign != null) StopCoroutine(TryAlign);

                PickingItem = false; //Try picking set to false

                ParentItemToHolster();

                Item.Picker = Owner;                            //MWC: record the real owner (fixes ForceDrop & ownership checks)
                Item.Pick();                                    //Tell the Item that it was picked
                FocusedItem = null;                             //Remove the Focused Item

                OnItemPicked.Invoke(Item.gameObject);           //Invoke the Event
                OnPicking.Invoke(Item.ID);                      //Invoke the Event
                var item = Item; //Store before collectable

                //Check if the item is a collectable so Pick it and remove it from the 
                if (Item.Collectable)
                {
                    Item = null;

                    //Enable Disable to find new collectables in the same area
                    PickUpArea.enabled = false;
                    this.Delay_Action(() => PickUpArea.enabled = true);

                    CollectableReaction?.React(item.gameObject);
                    Proxy.ResetTrigger();
                }
                else if (m_HidePickArea.Value)
                {
                    PickUpArea.enabled = false;        //Disable the Pick Up Area
                    Proxy.ResetTrigger();
                }
                else
                {
                    //The area stays on: release the picked item's (now disabled) colliders and hand the focus to the
                    //next item still inside. ResetTrigger would forget those items and Unity never re-sends Enter for them.
                    Proxy.RefreshColliders();
                    FocusNextItem(item);
                }
            }
        }

        protected virtual void ParentItemToHolster()
        {
            var Holder = this.Holder;
            var PosOffset = this.PosOffset;
            var RotOffset = this.RotOffset;

            //Use extra holders 
            if (Item.Holder > -1 && Item.Holder < extraHolders.Count)
            {
                Holder = extraHolders[Item.Holder].transform;
                PosOffset = extraHolders[Item.Holder].position;
                RotOffset = extraHolders[Item.Holder].rotation;
            }

            if (Holder) Parent(Holder, PosOffset, RotOffset); //Parent the Item to the Holder
        }

        public virtual void Parent(Transform parent, Vector3 pos, Vector3 rot)
        {
            var localScale = Item.transform.localScale;
            Item.transform.parent = parent;               //Parent it to the Holder
            Item.transform.localPosition = pos;           //Offset the Position
            Item.transform.localEulerAngles = rot;        //Offset the Rotation
            Item.transform.localScale = localScale;       //Offset the Rotation
        }


        /// <summary>Editor only console log for this Pick Up, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Debugging(string msg) => Debugging(msg, this);


        /// <summary>Editor only console log for this Pick Up, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Debugging(string msg, Object ob)
        {
#if UNITY_EDITOR
            if (debug) Debug.Log($"[{Root.name}] - [{msg}]", ob);
#endif
        }

        public virtual bool OnAnimatorBehaviourMessage(string message, object value) => this.InvokeWithParams(message, value);

        #region Context Menu



#if UNITY_EDITOR

        private void Reset()
        {
            PickUpArea = this.FindComponent<Collider>();
            if (PickUpArea == null)
            {
                PickUpArea = this.gameObject.AddComponent<BoxCollider>();
                PickUpArea.isTrigger = true;
            }
        }

        [ContextMenu("Connect to Weapon Manager (Holster_SetWeapon)")]
        private void ConnectToWeaponManagerHolster()
        {
            var method = this.GetUnityAction<GameObject>("MWeaponManager", "Holster_SetWeapon");
            if (method != null) UnityEditor.Events.UnityEventTools.AddPersistentListener(OnItemPicked, method);
            MTools.SetDirty(this);
        }



        [ContextMenu("Connect to Weapon Manager (Equip_External)")]
        private void ConnectToWeaponManagerExternal()
        {
            var method = this.GetUnityAction<GameObject>("MWeaponManager", "Equip_External");
            if (method != null) UnityEditor.Events.UnityEventTools.AddPersistentListener(OnItemPicked, method);
            MTools.SetDirty(this);
        }
#endif

        #endregion

#if MALBERS_DEBUG
        private void OnDrawGizmosSelected()
        {
            if (debug)
            {
                if (Holder)
                {
                    Gizmos.color = DebugColor;
                    Gizmos.DrawWireSphere(Holder.TransformPoint(PosOffset), DebugRadius);
                    Gizmos.DrawSphere(Holder.TransformPoint(PosOffset), DebugRadius);
                }

                DropCondition.Gizmos(this);
                PickUpCondition.Gizmos(this);


                foreach (var item in extraHolders)
                {
                    if (item.transform)
                    {
                        Gizmos.color = DebugColor;
                        Gizmos.DrawWireSphere(item.transform.TransformPoint(item.position), DebugRadius);
                        Gizmos.DrawSphere(item.transform.TransformPoint(item.position), DebugRadius);

                    }
                }
            }
        }
#endif
        [SerializeField] private int Editor_Tabs1;
    }

    #region INSPECTOR
#if UNITY_EDITOR
    [CustomEditor(typeof(MPickUp)), CanEditMultipleObjects]
    public class MPickUpEditor : Editor
    {

        private SerializedProperty
            PickUpArea, FocusedItem, Editor_Tabs1, Holder, RotOffset, extraHolders, IgnoreItemPickDelay, IgnoreItemDropDelay,
            item, m_HidePickArea, OnFocusedItem, CollectableReaction,

            PickUpCondition, DropCondition,

            canPick, canDrop, m_AllowTakeAway,

            Layer, triggerInteraction, OnItemDrop,
            PosOffset, CanPickUp, OnDropping, OnPicking, DebugRadius, OnItem, DebugColor, debug, Tags;

        protected string[] Tabs1 = new string[] { "General", "Events" };

        private MPickUp M;


        protected virtual void OnEnable()
        {
            M = (MPickUp)target;

            canPick = serializedObject.FindProperty("canPick");
            canDrop = serializedObject.FindProperty("canDrop");
            m_AllowTakeAway = serializedObject.FindProperty("m_AllowTakeAway");
            PickUpArea = serializedObject.FindProperty("PickUpArea");
            PickUpArea = serializedObject.FindProperty("PickUpArea");
            Layer = serializedObject.FindProperty("Layer");
            triggerInteraction = serializedObject.FindProperty("triggerInteraction");
            m_HidePickArea = serializedObject.FindProperty("m_HidePickArea");

            Holder = serializedObject.FindProperty("Holder");
            PosOffset = serializedObject.FindProperty("PosOffset");
            RotOffset = serializedObject.FindProperty("RotOffset");
            Tags = serializedObject.FindProperty("Tags");
            CollectableReaction = serializedObject.FindProperty("CollectableReaction");

            FocusedItem = serializedObject.FindProperty("focusedItem");
            item = serializedObject.FindProperty("item");
            extraHolders = serializedObject.FindProperty("extraHolders");

            CanPickUp = serializedObject.FindProperty("CanPickUp");
            //CanDrop = serializedObject.FindProperty("CanDrop");


            IgnoreItemPickDelay = serializedObject.FindProperty("IgnoreItemPickDelay");
            IgnoreItemDropDelay = serializedObject.FindProperty("IgnoreItemDropDelay");


            OnPicking = serializedObject.FindProperty("OnPicking");
            OnPicking = serializedObject.FindProperty("OnPicking");
            OnItem = serializedObject.FindProperty("OnItemPicked");
            OnItemDrop = serializedObject.FindProperty("OnItemDrop");
            OnDropping = serializedObject.FindProperty("OnDropping");
            OnFocusedItem = serializedObject.FindProperty("OnFocusedItem");


            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            DebugColor = serializedObject.FindProperty("DebugColor");
            DebugRadius = serializedObject.FindProperty("DebugRadius");
            debug = serializedObject.FindProperty("debug");

            PickUpCondition = serializedObject.FindProperty("PickUpCondition");
            DropCondition = serializedObject.FindProperty("DropCondition");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription("Pick Up Logic for Pickable Items");


            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs1);
            if (Editor_Tabs1.intValue == 0) DrawGeneral();
            else DrawEvents();

            if (debug.boolValue)
            {
                using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(DebugRadius);
                    EditorGUILayout.PropertyField(DebugColor, GUIContent.none, GUILayout.MaxWidth(40));
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGeneral()
        {
            //MalbersEditor.DrawScript(script);
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(PickUpArea, new GUIContent("Pick Up Trigger"));
                    MalbersEditor.DrawDebugIcon(debug);
                }

                EditorGUILayout.PropertyField(Layer);
                EditorGUILayout.PropertyField(triggerInteraction);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(Tags);
                EditorGUI.indentLevel--;
                EditorGUILayout.PropertyField(m_HidePickArea, new GUIContent("Hide Trigger"));

                EditorGUILayout.PropertyField(IgnoreItemPickDelay);
                EditorGUILayout.PropertyField(IgnoreItemDropDelay);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(canPick);
                EditorGUILayout.PropertyField(canDrop);
                EditorGUILayout.PropertyField(m_AllowTakeAway, new GUIContent("Allow Take Away",
                    "Allow taking an Item that is already held by another character (forces the previous owner to drop it first)"));
            }


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(PickUpCondition);
                EditorGUILayout.PropertyField(DropCondition);
                EditorGUI.indentLevel--;
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(Holder, new GUIContent("Default Holder"));
                if (Holder.objectReferenceValue)
                {
                    EditorGUILayout.LabelField("Offsets", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(PosOffset, new GUIContent("Position", "Position Local Offset to parent the item to the holder"));
                    EditorGUILayout.PropertyField(RotOffset, new GUIContent("Rotation", "Rotation Local Offset to parent the item to the holder"));
                }

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(extraHolders, true);
                EditorGUI.indentLevel--;
            }

            if (Application.isPlaying)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ObjectField("Picked Item", M.Item?.gameObject, typeof(GameObject), false);
                    using (new EditorGUI.DisabledGroupScope(true))
                        EditorGUILayout.ObjectField("Focused Item", M.FocusedItem?.gameObject, typeof(GameObject), false);

                    Repaint();
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(CollectableReaction);
            }

        }

        private void DrawEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(CanPickUp, new GUIContent("On Can Pick Item"));
                EditorGUILayout.PropertyField(OnFocusedItem, new GUIContent("On Item Focused"));
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(OnItem, new GUIContent("On Item Picked"));
                EditorGUILayout.PropertyField(OnItemDrop, new GUIContent("On Item Dropped"));
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(OnPicking);
                EditorGUILayout.PropertyField(OnDropping);
            }

        }
    }
#endif
    #endregion
}