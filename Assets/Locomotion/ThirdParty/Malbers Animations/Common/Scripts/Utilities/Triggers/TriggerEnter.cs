// Last Change: 9/17/2026 5:14pm
using MalbersAnimations.Conditions;
using MalbersAnimations.Events;
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    /// <summary> This is used when the collider is in a different gameObject and you need to check the Trigger Events
    /// Create this component at runtime and subscribe to the UnityEvents </summary>
    [AddComponentMenu("Malbers/Utilities/Colliders/Trigger Enter")]
    [SelectionBase]
    public class TriggerEnter : MonoBehaviour
    {
        public LayerReference Layer = new(-1);
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        [Tooltip("On Trigger Enter only works with the first colliders that enters")]
        public bool UseOnce;

        [Tooltip("On Trigger Enter only works. Disables the entire gameObject after its use")]
        public bool DisableAfterUse = true;
        [Tooltip("Destroy the GameObject after the Trigger Enter")]
        public bool m_Destroy = false;
        [Tooltip("Destroy after x Seconds")]
        [Min(0)] public float DestroyAfter = 0;


        [Tooltip("Extra conditions to check to filter the colliders entering OnTrigger Enter")]
        public Conditions2 Conditions = new();

        [Tooltip("Search only Tags")]
        public MTags Tags;

        public Reaction2 EnterColliderReaction;
        public Reaction2 EnterObjectReaction;


        public ColliderEvent onTriggerEnter = new();
        public GameObjectEvent onCoreObject = new();
        public RigidbodyEvent OnRigidBodyEnter = new();


        public int tabIndex = 0;

        /// <summary> Collider Component used for the Trigger Proxy </summary>
        public Collider OwnCollider { get; private set; }
        public bool Active { get => enabled; set => enabled = value; }
        public QueryTriggerInteraction TriggerInteraction { get => triggerInteraction; set => triggerInteraction = value; }

        /// <summary>Fetches the collider and forces it to be an enabled trigger (Use Once disables it, so re-enabling must restore it)</summary>
        protected virtual void OnEnable()
        {
            OwnCollider = GetComponent<Collider>();

            Active = true;

            if (OwnCollider)
            {
                OwnCollider.isTrigger = true;
                OwnCollider.enabled = true; //Use Once turned it off; a re-enabled trigger was silently dead without this
            }
            else
            {
                Active = false;
                Debug.LogError("This Script requires a Collider, please add any type of collider", this);
            }
        }

        /// <summary>True if the entering collider passes every filter (Layer, Trigger Interaction, Tags, Conditions, not self)</summary>
        public bool TrueConditions(Collider other)
        {
            if (!Active) return false;
            if (other == null) return false;        // you are CALLING A ELIMINATED ONE (must run before anything touches 'other')
            if (OwnCollider == null) return false;  // you have no trigger

            if (Tags != null && Tags.Length > 0)
            {
                if (!other.gameObject.HasMalbersTagInParent(Tags)) return false;
            }

            if (triggerInteraction == QueryTriggerInteraction.Ignore && other.isTrigger) return false; // Check Trigger Interactions
            if (!MTools.Layer_in_LayerMask(other.gameObject.layer, Layer)) return false;
            if (transform.IsChildOf(other.transform)) return false;                 // Do not Interact with yourself
            if (other.transform.SameHierarchy(transform)) return false;    // Do not Interact with yourself

            if (!Conditions.Evaluate(other)) return false;

            return true;
        }
        void OnTriggerEnter(Collider other)
        {
            if (TrueConditions(other))
            {
                var core = other.GetComponentInParent<IObjectCore>();

                var CoreObject = core != null ? core.transform.gameObject : other.transform.root.gameObject;

                onCoreObject.Invoke(CoreObject);
                EnterObjectReaction.React(CoreObject);

                onTriggerEnter.Invoke(other);
                EnterColliderReaction.React(other);


                if (other.attachedRigidbody) OnRigidBodyEnter.Invoke(other.attachedRigidbody);

                if (UseOnce)
                {
                    Active = false;
                    OwnCollider.enabled = false;
                    if (DisableAfterUse) gameObject.SetActive(false);

                    if (m_Destroy)
                    {
                        Destroy(gameObject, DestroyAfter);
                    }
                }
            }
        }
    }

#if UNITY_EDITOR

    [CustomEditor(typeof(TriggerEnter))]
    public class TriggerEnterEditor : Editor
    {
        SerializedProperty Layer, triggerInteraction, UseOnce, DisableAfterUse, m_Destroy, DestroyAfter, tabIndex,
            Conditions, tags, EnterColliderReaction, EnterObjectReaction, onTriggerEnter, onCoreObject, OnRigidBodyEnter

            ;
        protected virtual void OnEnable()
        {
            Layer = serializedObject.FindProperty("Layer");
            triggerInteraction = serializedObject.FindProperty("triggerInteraction");
            UseOnce = serializedObject.FindProperty("UseOnce");
            DisableAfterUse = serializedObject.FindProperty("DisableAfterUse");
            m_Destroy = serializedObject.FindProperty("m_Destroy");
            DestroyAfter = serializedObject.FindProperty("DestroyAfter");

            Conditions = serializedObject.FindProperty("Conditions");
            tags = serializedObject.FindProperty("Tags");
            EnterColliderReaction = serializedObject.FindProperty("EnterColliderReaction");
            EnterObjectReaction = serializedObject.FindProperty("EnterObjectReaction");
            onTriggerEnter = serializedObject.FindProperty("onTriggerEnter");
            onCoreObject = serializedObject.FindProperty("onCoreObject");
            OnRigidBodyEnter = serializedObject.FindProperty("OnRigidBodyEnter");
            tabIndex = serializedObject.FindProperty("tabIndex");
        }


        private readonly string[] tabsEditor = new string[3] { "General", "Reactions", "Events" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Simple OnTrigger Enter Interaction");


            tabIndex.intValue = GUILayout.Toolbar(tabIndex.intValue, tabsEditor);


            switch (tabIndex.intValue)
            {
                case 0:
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.PropertyField(Layer);
                        EditorGUILayout.PropertyField(triggerInteraction);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(Conditions);
                        EditorGUI.indentLevel--;
                        EditorGUILayout.PropertyField(tags);

                        EditorGUILayout.PropertyField(UseOnce);
                        if (UseOnce.boolValue)
                        {
                            //Destroy is honored at runtime regardless of Disable After Use, so draw it at the same level
                            //(nesting it hid a still-active Destroy whenever Disable After Use was turned off)
                            EditorGUILayout.PropertyField(DisableAfterUse);
                            EditorGUILayout.PropertyField(m_Destroy);
                            if (m_Destroy.boolValue)
                            {
                                EditorGUILayout.PropertyField(DestroyAfter);
                            }
                        }
                    }
                    break;

                case 1:

                    EditorGUILayout.PropertyField(EnterColliderReaction);
                    EditorGUILayout.PropertyField(EnterObjectReaction);
                    break;

                case 2:
                    EditorGUILayout.PropertyField(onTriggerEnter);
                    EditorGUILayout.PropertyField(onCoreObject);
                    EditorGUILayout.PropertyField(OnRigidBodyEnter);
                    break;
                default:
                    break;
            }



            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}