// Last Change: 9/17/2026 6:05pm
using UnityEngine;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Events;
using MalbersAnimations.Reactions;
using System.Collections.Generic; //MWC: needed to track multiple focusers
using MalbersAnimations.Conditions;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Controller
{
    [SelectionBase, AddComponentMenu("Malbers/Interaction/Pickable")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/secondary-components/pickable")]
    public class Pickable : MonoBehaviour, ICollectable
    {
        //public bool Align = false;
        //[Min(0)] public float AlignTime = 0.15f;

        [Tooltip("Delay time after calling the Pick() method. the item will be parented to the PickUp component after this time has passed")]
        public FloatReference PickDelay = new(0);
        [Tooltip("Delay time after calling the Drop() method. the item will be unparented from the PickUp component after this time has passed")]
        public FloatReference DropDelay = new(0);
        [Tooltip("Cooldown needed to pick or drop again the collectable")]
        public FloatReference coolDown = new(0f);
        [Tooltip("When an Object is Collectable it means that the Picker can still pick objects, the item was collected by another component (E.g. Weapons or Inventory)")]

        public BoolReference m_Collectable = new(false);
        [Tooltip("The Pick Up Drop Logic will be called via animator events/messages. Use These methods on the Animator: TryPick(), TryDrop(), TryPickUpDrop()")]
        public BoolReference m_ByAnimation = new(false);
        [Tooltip("The Pick Up Drop Logic will be called via animator events/messages")]
        public BoolReference m_DestroyOnPick = new(false);

        [Tooltip(" Amount Pickable Item can store.. that it can be use for anything")]
        public IntReference m_Amount = new(1); //Not done yet

        //[Tooltip("The pickable can be picked. Set it to false to temporally disabled the Pick method at runtime")]
        //public BoolReference canBePicked = new(true); //Not done yet

        [Tooltip("Conditions to check if the Pickable can be picked")]
        public Conditions2 CanBePickedIf;


        [Tooltip("The pickable will be pick automatically when it enters the focus area from the picker")]
        public BoolReference m_AutoPick = new(false); //Not done yet
        public IntReference m_ID = new();         //Not done yet

        /// <summary>Who Did the Picking </summary>
        public GameObject Picker { get; set; }

        /// <summary>Who is Focusing (last focuser). See <see cref="focusedBy"/> for the full set </summary>
        public GameObject Focusing { get; set; }

        /// <summary>All pickers currently focusing this item. Supports 2+ focusers at once so one
        /// picker leaving/unfocusing does not wipe the focus state of the others</summary>
        private readonly HashSet<GameObject> focusedBy = new();

        [Tooltip("Set a World rotation when the item is dropped")]
        public bool UseDropRotation = false;
        [Tooltip("World rotation value when the item is dropped")]
        public Vector3 DropRotation = Vector3.zero;
        [Tooltip("Set a local rotation value when the item is dropped")]
        public bool UsePickRotation = false;
        [Tooltip("Local rotation value when the item is dropped")]
        public Vector3 PickRotation = Vector3.zero;


        [Tooltip("What holder will the item be parent to. -1: Default Holder. >=0 : Index of the Extra Holder list")]
        public int holder = -1;

        public BoolEvent OnFocused = new();
        public GameObjectEvent OnFocusedBy = new();
        public GameObjectEvent OnUnfocusedBy = new();
        public GameObjectEvent OnPicked = new();
        public GameObjectEvent OnPickedFailed = new();
        public GameObjectEvent OnPrePicked = new();
        public GameObjectEvent OnDropped = new();
        public GameObjectEvent OnPreDropped = new();

        public Reaction2 FocusedByReaction;
        public Reaction2 UnFocusedByReaction;
        public Reaction2 PickedReaction;
        public Reaction2 PrePickedReaction;
        public Reaction2 DroppedReaction;
        public Reaction2 PreDroppedReaction;

        [SerializeField] private Rigidbody rb;


        [Tooltip("Destroys Rigidbody while picked. This will remove completely the rigidbody from the Pickable object and it will restore it after is dropped.")]
        public BoolReference DestroyRbOnPick = new(false);

        [RequiredField] public Collider[] m_colliders;

        [Tooltip("If the item is picked it will be kinematic")]
        public BoolReference kinematicOnPick = new(true);

        [Tooltip("Disable Colliders when the Item is picked (Colliders will be enabled back when the item is dropped")]
        public BoolReference disableColliders = new(true);

        /// <summary>  Cache the Rigid body parameters  on start  </summary>
        protected RigidbodyParameters rigidbodyParameters;
        /// <summary>  Cache  if the RB is kinematic  </summary>
        protected bool defaultKinematic;
        protected RigidbodyConstraints defaultConstraints;

        protected float currentPickTime;

        public bool debug;

        /// <summary>Is this Object being picked </summary>
        public bool IsPicked { get; set; }

        public int Holder => holder;

        /// <summary>Current value of the Item</summary>
        public int Amount { get => m_Amount.Value; set => m_Amount.Value = value; }

        /// <summary>The Item will be auto picked if the Picker is focusing it</summary>
        public bool AutoPick { get => m_AutoPick.Value; set => m_AutoPick.Value = value; }

        // /// <summary>The pickable can be picked. Set it to false to temporally disabled the Pick method at runtime</summary>
        // public bool CanBePicked { get => canBePicked.Value; set => canBePicked.Value = value; }

        public bool Collectable { get => m_Collectable.Value; set => m_Collectable.Value = value; }
        public Rigidbody RigidBody => rb;

        /// <summary>The Pick Up Drop Logic will be called via animator events/messages</summary>
        public bool ByAnimation { get => m_ByAnimation.Value; set => m_ByAnimation.Value = value; }
        public bool DestroyOnPick { get => m_DestroyOnPick.Value; set => m_DestroyOnPick.Value = value; }
        public bool InCoolDown => !MTools.ElapsedTime(CurrentPickTime, coolDown);
        public int ID { get => m_ID.Value; set => m_ID.Value = value; }

        public bool Active { get => enabled; set => enabled = value; }

        protected Vector3 DefaultScale;

        protected bool focused;
        public virtual bool Focused
        {
            get => focused;
            protected set
            {
                focused = value;
                OnFocused.Invoke(focused);
            }
        }

        public virtual void SetFocused(GameObject FocusBy, bool isFocused)
        {
            if (isFocused)
            {
                if (FocusBy != null) focusedBy.Add(FocusBy); //MWC: track every focuser

                Focused = true;
                OnFocusedBy.Invoke(FocusBy);
                FocusedByReaction.React(FocusBy);

                Focusing = FocusBy; //Set the Focusing GameObject
            }
            else
            {
                if (FocusBy != null) focusedBy.Remove(FocusBy); //MWC: this picker stops focusing

                OnUnfocusedBy.Invoke(FocusBy);
                UnFocusedByReaction.React(FocusBy);

                //MWC: only fully unfocus when NOBODY is focusing anymore (prevents one picker wiping another's focus)
                if (focusedBy.Count == 0)
                {
                    //Maybe this can be removed after
                    OnFocusedBy.Invoke(null);
                    FocusedByReaction.React((Component)null);

                    Focused = false;
                    Focusing = null; //Reset the Focusing GameObject
                }
                else
                {
                    //Hand the "Focusing" reference to a remaining focuser
                    foreach (var f in focusedBy) { Focusing = f; break; }
                }
            }
        }

        /// <summary>Game Time the Pickable was Picked</summary>
        public float CurrentPickTime { get => currentPickTime; set => currentPickTime = value; }
        float ICollectable.DropDelay { get => this.DropDelay.Value; }
        float ICollectable.PickDelay { get => this.PickDelay.Value; }

        protected virtual void OnDisable()
        {
            focusedBy.Clear(); //MWC: drop all focusers when disabled so focus state can't get stuck
            Focused = false;
        }

        protected virtual void Awake()
        {
            FindReferences();

            CurrentPickTime = -coolDown;

            DefaultScale = transform.localScale;

            if (rb)
            {
                defaultKinematic = rb.isKinematic; //Store the default value of the kinematic
                defaultConstraints = rb.constraints; //Store the default value of the constraints
                rigidbodyParameters = new(rb);
            }
        }

        private void FindReferences()
        {
            rb = GetComponent<Rigidbody>();
            if (m_colliders == null || m_colliders.Length == 0) m_colliders = GetComponents<Collider>();
        }

        public virtual void Pick()
        {
            Physics_Disable();                 //Disable all physics when the item is picked
            IsPicked = !Collectable;                //Check if the Item is collectable 

            //Weapons can be picked without having a picker
            focusedBy.Clear();      //MWC: a picked item is focused by no-one anymore (clears every focuser)
            SetFocused(null, false);

            OnPicked.Invoke(Picker);             //Call the Event
            PickedReaction.React(Picker);

            CurrentPickTime = Time.time;            //Store the time it was picked
            if (Collectable) enabled = false;

            if (DestroyOnPick)
            {
                if (TryGetComponent(out IPoolGameObject poolObject))
                {
                    poolObject.Pool_Release(gameObject); //Go back to the Pool
                }
                else
                {
                    DestroyPickUp();
                }
            }
            else
            {
                if (UsePickRotation) transform.localEulerAngles = PickRotation; //Set the Local Pick Rotation after it was parented
            }

            MDebuggin($"Picked by <b>[{((Picker != null) ? Picker.name : "Unknown")}]</b>");
        }

        protected virtual void DestroyPickUp()
        {
            //Needs to check first if the item is from a Pool
            Destroy(gameObject);
        }

        public virtual void Drop()
        {
            Physics_Restore();
            IsPicked = false;
            enabled = true;

            SetParent(null);                                        //UnParent
            transform.localScale = DefaultScale;                    //Restore the Scale

            OnDropped.Invoke(Picker);
            DroppedReaction.React(Picker);

            MDebuggin($"Dropped by <b>[{((Picker != null) ? Picker.name : "Unknown")}]</b>");


            Picker = null;                                          //Reset who did the picking
            CurrentPickTime = Time.time;

            if (UseDropRotation) transform.eulerAngles = DropRotation; //Set the World Drop Rotation after it was unparented

        }

        public virtual void SetParent(Transform parent)
        {
            transform.parent = parent;
        }

        /// <summary> Call this in case a picker has still the item </summary>
        public virtual void ForceDrop()
        {
            if (Picker == null) return; //MWC: nobody holds it, nothing to force-drop

            var picker = Picker.FindComponent<MPickUp>(); //Check if the Picker has the IPickup

            if (picker)
            {
                MDebuggin($"Force Drop by <b>[{((picker != null) ? picker.name : "Unknown")}]</b>");
                picker.DropItem();
            }
        }

        public virtual void Physics_Disable()
        {
            if (rb == null) return;

            if (DestroyRbOnPick) //Destroy the Rigidbody
            {
                Destroy(rb);
                rb = null;
            }


            if (RigidBody)
            {
                RigidBody.useGravity = false;

                RigidBody.isKinematic = kinematicOnPick.Value;

                if (RigidBody.isKinematic)
                    RigidBody.collisionDetectionMode = CollisionDetectionMode.Discrete;

                RigidBody.constraints = RigidbodyConstraints.FreezeAll; //Freeze the rotation of the item

            }

            if (disableColliders.Value)
            {
                foreach (var c in m_colliders)
                {
                    if (c) c.enabled = false; //Disable all colliders
                }
            }

            MDebuggin("Physics Disabled");
        }

        public virtual void Physics_Restore()
        {
            if (RigidBody)
            {
                RigidBody.useGravity = true;
                RigidBody.isKinematic = defaultKinematic; //Restore the kinematic value of the item

                if (!RigidBody.isKinematic)
                    RigidBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                // RigidBody.detectCollisions = true;

                RigidBody.constraints = defaultConstraints; //Restore the constraints of the item
            }

            if (DestroyRbOnPick) //Restore the Rigidbody
            {
                rb = gameObject.AddComponent<Rigidbody>();
                rigidbodyParameters.RestoreRigidBody(rb);
            }

            foreach (var c in m_colliders)
            {
                c.enabled = true; //Enable all colliders
            }

            MDebuggin("Physics Restored");

        }

        private void MDebuggin(string log)
        {
            if (debug) Debug.Log($"<B>[Pickable] {name}:</B> {log}", this);
        }

        public virtual void SetEnable(bool enable)
        {
            this.enabled = enable;
        }

        public void PreDrop(GameObject gameObject)
        {
            OnPreDropped.Invoke(gameObject);
            PreDroppedReaction.React(gameObject);
        }

        public void PrePicked(GameObject gameObject)
        {
            OnPrePicked.Invoke(gameObject);
            PrePickedReaction.React(gameObject);
        }

        public void PickedFailed(GameObject FailedByPicker) => OnPickedFailed.Invoke(FailedByPicker);


        [HideInInspector] public int EditorTabs = 0;
    }

    //INSPECTOR
#if UNITY_EDITOR
    [CustomEditor(typeof(Pickable)), CanEditMultipleObjects]
    public class PickableEditor : Editor
    {
        private SerializedProperty //   PickAnimations, PickUpMode, PickUpAbility, DropMode, DropAbility,DropAnimations, 
                                   // Align, AlignTime, AlignDistance, AlignPos,
            EditorTabs,
            m_AutoPick, DropDelay, PickDelay, rb, kinematicOnPick, disableColliders, DestroyRbOnPick,
            // canBePicked,
            //kinematicOnDrop,
            CoolDown,// SceneRoot,
            OnFocused, OnFocusedBy, OnUnfocusedBy,
            OnPrePicked, OnPicked, OnDropped, OnPreDropped, holder, OnPickedFailed, debug,

            PrePickedReaction, PreDroppedReaction, DroppedReaction, PickedReaction, UnFocusedByReaction, FocusedByReaction,



            UseDropRotation, DropRotation, UsePickRotation, PickRotation, CanBePickedIf,

            Amount, IntID, m_collider, m_Collectable, m_ByAnimation, m_DestroyOnPick;

        private Pickable m;

        protected string[] Tabs1 = new string[] { "General", "Events", "Reactions" };

        protected virtual void OnEnable()
        {
            m = (Pickable)target;

            EditorTabs = serializedObject.FindProperty("EditorTabs");
            //SceneRoot = serializedObject.FindProperty("SceneRoot");

            rb = serializedObject.FindProperty("rb");
            CanBePickedIf = serializedObject.FindProperty("CanBePickedIf");
            kinematicOnPick = serializedObject.FindProperty("kinematicOnPick");
            disableColliders = serializedObject.FindProperty("disableColliders");
            DestroyRbOnPick = serializedObject.FindProperty("DestroyRbOnPick");
            //kinematicOnDrop = serializedObject.FindProperty("kinematicOnDrop");


            PickDelay = serializedObject.FindProperty("PickDelay");
            DropDelay = serializedObject.FindProperty("DropDelay");
            m_Collectable = serializedObject.FindProperty("m_Collectable");
            m_ByAnimation = serializedObject.FindProperty("m_ByAnimation");
            m_DestroyOnPick = serializedObject.FindProperty("m_DestroyOnPick");


            //  Align = serializedObject.FindProperty("Align");
            // AlignTime = serializedObject.FindProperty("AlignTime");
            // AlignDistance = serializedObject.FindProperty("AlignDistance");
            //AlignPos = serializedObject.FindProperty("AlignPos");

            // canBePicked = serializedObject.FindProperty("canBePicked");
            OnPickedFailed = serializedObject.FindProperty("OnPickedFailed");
            OnFocused = serializedObject.FindProperty("OnFocused");
            OnFocusedBy = serializedObject.FindProperty("OnFocusedBy");
            OnUnfocusedBy = serializedObject.FindProperty("OnUnfocusedBy");
            OnPicked = serializedObject.FindProperty("OnPicked");
            OnPrePicked = serializedObject.FindProperty("OnPrePicked");
            OnDropped = serializedObject.FindProperty("OnDropped");
            OnPreDropped = serializedObject.FindProperty("OnPreDropped");
            debug = serializedObject.FindProperty("debug");


            holder = serializedObject.FindProperty("holder");


            UseDropRotation = serializedObject.FindProperty("UseDropRotation");
            DropRotation = serializedObject.FindProperty("DropRotation");
            UsePickRotation = serializedObject.FindProperty("UsePickRotation");
            PickRotation = serializedObject.FindProperty("PickRotation");


            FocusedByReaction = serializedObject.FindProperty("FocusedByReaction");
            UnFocusedByReaction = serializedObject.FindProperty("UnFocusedByReaction");
            PickedReaction = serializedObject.FindProperty("PickedReaction");
            DroppedReaction = serializedObject.FindProperty("DroppedReaction");
            PreDroppedReaction = serializedObject.FindProperty("PreDroppedReaction");
            PrePickedReaction = serializedObject.FindProperty("PrePickedReaction");



            //ShowEvents = serializedObject.FindProperty("ShowEvents");
            Amount = serializedObject.FindProperty("m_Amount");
            IntID = serializedObject.FindProperty("m_ID");
            m_collider = serializedObject.FindProperty("m_colliders");

            //Collectable = serializedObject.FindProperty("Collectable");
            m_AutoPick = serializedObject.FindProperty("m_AutoPick");
            CoolDown = serializedObject.FindProperty("coolDown");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription("Pickable - Collectable object");
            EditorTabs.intValue = GUILayout.Toolbar(EditorTabs.intValue, Tabs1);
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (Application.isPlaying)
                {
                    using (new EditorGUI.DisabledGroupScope(true))
                    {
                        EditorGUILayout.ToggleLeft("Is Picked", m.IsPicked);
                        EditorGUILayout.ToggleLeft("Is Focused", m.Focused);

                        EditorGUILayout.ObjectField("Picker-Focuser", m.Picker, typeof(GameObject), true);
                        // EditorGUILayout.ObjectField("Focuser",m.Focused)
                    }
                }

                if (EditorTabs.intValue == 0)
                    DrawGeneral();
                else if (EditorTabs.intValue == 1)
                    DrawEvents();
                else
                {
                    DrawReactions();
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawEvents()
        {
            EditorGUILayout.PropertyField(OnFocused);
            EditorGUILayout.PropertyField(OnFocusedBy);
            EditorGUILayout.PropertyField(OnUnfocusedBy);
            if (m.PickDelay > 0 || m.m_ByAnimation.Value)
                EditorGUILayout.PropertyField(OnPrePicked, new GUIContent("On Pre-Picked By"));
            EditorGUILayout.PropertyField(OnPicked, new GUIContent("On Picked By"));
            if (m.DropDelay > 0 || m.m_ByAnimation.Value)
                EditorGUILayout.PropertyField(OnPreDropped, new GUIContent("On Pre-Dropped By"));
            EditorGUILayout.PropertyField(OnDropped, new GUIContent("On Dropped By"));

            EditorGUILayout.PropertyField(OnPickedFailed);

        }

        private void DrawReactions()
        {
            EditorGUILayout.PropertyField(FocusedByReaction);
            EditorGUILayout.PropertyField(UnFocusedByReaction);
            if (m.PickDelay > 0 || m.m_ByAnimation.Value)
                EditorGUILayout.PropertyField(PrePickedReaction);
            if (m.DropDelay > 0 || m.m_ByAnimation.Value)
                EditorGUILayout.PropertyField(PreDroppedReaction);
            EditorGUILayout.PropertyField(PickedReaction);
            EditorGUILayout.PropertyField(DroppedReaction);

        }

        private void DrawGeneral()
        {
            m_AutoPick.isExpanded = MalbersEditor.Foldout(m_AutoPick.isExpanded, "Pickable Item");

            if (m_AutoPick.isExpanded)
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(IntID, new GUIContent("ID", "Int value the Pickable Item can store. This ID is used by the Picker component to Identify each Pickable Object"));
                    MalbersEditor.DrawDebugIcon(debug);
                }
                EditorGUILayout.PropertyField(holder);
                EditorGUILayout.PropertyField(Amount);

                EditorGUILayout.PropertyField(m_AutoPick, new GUIContent("Auto", "The Item will be Picked Automatically"));
                EditorGUILayout.PropertyField(m_ByAnimation,
                    new GUIContent("Use Animation", "The Item will Pre-Picked/Dropped by the Picker Animator." +
                    " Pick-Drop Logic is called by Animation Event or Animator Message Behaviour.\nUse the Methods: TryPickUpDrop(); TryPickUp(); TryDrop();"));

                //   EditorGUILayout.PropertyField(SceneRoot);


                EditorGUILayout.PropertyField(m_Collectable, new GUIContent("Collectable", "The Item will Picked by the Pickable and it will be stored"));
                if (m.Collectable)
                    EditorGUILayout.PropertyField(m_DestroyOnPick, new GUIContent("Destroy Collectable", "The Item will be destroyed after is picked"));


                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(CanBePickedIf);
                EditorGUI.indentLevel--;
            }

            rb.isExpanded = MalbersEditor.Foldout(rb.isExpanded, "References");
            if (rb.isExpanded)
            {
                EditorGUILayout.PropertyField(rb, new GUIContent("Rigid Body"));
                EditorGUILayout.PropertyField(kinematicOnPick);
                EditorGUILayout.PropertyField(DestroyRbOnPick);
                // EditorGUILayout.PropertyField(kinematicOnDrop);

                EditorGUILayout.PropertyField(disableColliders);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_collider);
                EditorGUI.indentLevel--;
            }


            CoolDown.isExpanded = MalbersEditor.Foldout(CoolDown.isExpanded, "Delays");

            if (CoolDown.isExpanded)
            {
                EditorGUILayout.PropertyField(CoolDown);
                EditorGUILayout.PropertyField(PickDelay);
                EditorGUILayout.PropertyField(DropDelay);
            }

            UseDropRotation.isExpanded = MalbersEditor.Foldout(UseDropRotation.isExpanded, "Rotation Offsets");

            if (UseDropRotation.isExpanded)
            {
                EditorGUILayout.PropertyField(UsePickRotation);
                if (UsePickRotation.boolValue)
                    EditorGUILayout.PropertyField(PickRotation);
                EditorGUILayout.PropertyField(UseDropRotation);
                if (UseDropRotation.boolValue)
                    EditorGUILayout.PropertyField(DropRotation);
            }

            //   Align.isExpanded = MalbersEditor.Foldout(Align.isExpanded, "Alignment");

            //if (Align.isExpanded)
            //{
            //    EditorGUILayout.PropertyField(Align, new GUIContent("Align On Pick", "Align the Item to the Picker Position"));

            //    if (Align.boolValue)
            //    {

            //        //using (new GUILayout.HorizontalScope())
            //        //{

            //        //    EditorGUILayout.PropertyField(AlignPos, new GUIContent("Align Pos", "align the Position"));

            //        //    EditorGUIUtility.labelWidth = 60;
            //        //    EditorGUILayout.PropertyField
            //        //        (AlignDistance, new GUIContent("Distance", "Distance to move the Animal towards the Item"), GUILayout.MinWidth(50));
            //        //    EditorGUIUtility.labelWidth = 0;
            //        //}
            //        EditorGUILayout.PropertyField(AlignTime, new GUIContent("Time", "Time required to do the alignment"));
            //    }
            //}
        }
    }
#endif
}