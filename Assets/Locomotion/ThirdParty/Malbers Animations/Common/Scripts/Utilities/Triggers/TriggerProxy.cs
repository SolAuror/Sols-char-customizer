// Last Change: 9/20/2026 11:20am
using UnityEngine;
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using System;
using UnityEngine.Events;
using MalbersAnimations.Conditions;
using MalbersAnimations.Reactions;
using System.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    /// <summary>
    /// This is used when the collider is in a different gameObject and you need to check the Collider Events
    /// Create this component at runtime and subscribe to the UnityEvents </summary>
    [AddComponentMenu("Malbers/Utilities/Colliders/Trigger Proxy")]
    public class TriggerProxy : MonoBehaviour
    {
        [Tooltip("Hit Layer for the Trigger Proxy")]
        [SerializeField] private LayerReference hitLayer = new(-1); //Everything by default, like TriggerEnter. (0 = Nothing silently detected no colliders)
        public LayerMask Layer { get => hitLayer.Value; set => hitLayer.Value = value; }


        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        [Tooltip("Search only Tags")] public MTags tags;

        public ColliderEvent OnTrigger_Enter = new();
        public ColliderEvent OnTrigger_Exit = new();
        public ColliderEvent OnTrigger_Stay = new();

        public GameObjectEvent OnGameObjectEnter = new();
        public GameObjectEvent OnGameObjectExit = new();
        public GameObjectEvent OnGameObjectStay = new();
        public UnityEvent OnEmpty = new();

        [Tooltip("ID of the Trigger Proxy. This is used to identify the Trigger Proxy in the Editor and at runtime")]
        public IntReference ID = new(-1);


        public Reaction2 TriggerEnterReaction;
        public Reaction2 TriggerExitReaction;
        public Reaction2 TriggerStayReaction;

        public Reaction2 GameObjectEnterReaction;
        public Reaction2 GameObjectExitReaction;
        public Reaction2 GameObjectStayReaction;

        [SerializeField] private bool m_debug = false;

        public BoolReference useOnTriggerStay = new();

        [Tooltip("Seconds between sweeps for colliders that were disabled or destroyed while inside the Trigger (Unity sends no Exit for those). 0 = Off. Not needed when [Use On Trigger Stay] is On, which sweeps every frame")]
        [Min(0)] public float sweepInterval = 0.25f;

        [Tooltip("Trigger will be disabled the first time it finds a valid collider")]
        public BoolReference OneTimeUse = new();
        [Tooltip("Do not Interact with static colliders")]
        public BoolReference ignoreStatic = new();

        /// <summary> All the Colliders that are inside the Trigger Proxy when they get activated </summary>
        protected internal HashSet<Collider> m_colliders = new(8);
        /// <summary>All the Gameobjects using the Proxy</summary>
        protected internal HashSet<GameObject> EnteringGameObjects = new(8);

        //Each sweep owns its snapshot buffer: an Exit listener can re-enter another sweep (e.g. Activate(false) -> OnDisable)
        //while the outer one is still enumerating, so they must never share a list.
        private readonly List<Collider> _exitBuffer = new(8);          //OnDisable
        private readonly List<Collider> _resetBuffer = new(8);         //ResetTrigger
        private readonly List<Collider> _disabledBuffer = new(8);      //RemoveDisabledColliders
        private readonly List<Collider> _stayColliders = new(8);       //CheckOntriggerStay
        private readonly List<GameObject> _stayObjects = new(8);       //CheckOntriggerStay
        private readonly List<GameObject> _orphanObjects = new(4);     //RemoveOrphanGameObjects

        /// <summary>True while RemoveDisabledColliders is enumerating, so a listener cannot re-enter it</summary>
        private bool _sweeping;
        /// <summary>True while RemoveOrphanGameObjects is enumerating, so a listener cannot re-enter it</summary>
        private bool _purgingOrphans;
        /// <summary>Set by One Time Use right before disabling, so OnDisable releases the colliders without firing Exit events</summary>
        private bool _skipExitOnDisable;
        /// <summary>The interval sweep coroutine, running only while the trigger has colliders inside and Stay is Off</summary>
        private Coroutine _sweep;

        [Tooltip("Extra conditions to check to filter the colliders entering OnTrigger Enter")]
        public Conditions2 Conditions = new();

        public Action<GameObject, Collider, TriggerProxy> EnterTriggerInteraction;
        public Action<GameObject, Collider, TriggerProxy> ExitTriggerInteraction;
        /// <summary>Raised when the Proxy is enabled (a new activation window). Listeners that remember what they already hit reset here</summary>
        public Action<TriggerProxy> OnActivated;

        /// <summary> Is this component enabled? /summary>
        public bool Active { get => enabled; set => enabled = value; }

        //public int ID { get => m_ID.Value; set => m_ID.Value = value; }

        public QueryTriggerInteraction TriggerInteraction { get => triggerInteraction; set => triggerInteraction = value; }

        /// <summary> Collider Component used for the Trigger Proxy </summary>
        [RequiredField] public Collider ownCollider;
        public Transform Owner { get; set; }


        public virtual void Awake()
        {
            if (ownCollider == null) ownCollider = GetComponent<Collider>();

            if (ownCollider) ownCollider.isTrigger = true;
            else
                Debug.LogWarning("This Script requires a Collider, please add any type of collider", this);

            if (Owner == null) Owner = transform;

            ResetTrigger();
        }

        public virtual void OnEnable()
        {
            _skipExitOnDisable = false;
            //MWC: null-guard so a proxy without a collider doesn't throw on enable
            if (ownCollider) ownCollider.enabled = true; //Enable the Collider when the Trigger Proxy is enabled
            ResetTrigger();
            OnActivated?.Invoke(this);
        }

        public virtual void OnDisable()
        {
            if (_skipExitOnDisable)
            {
                //One Time Use: release everything silently. Firing per-collider Exit here would undo, in the
                //same frame, the Enter that just fired for the object that consumed the trigger.
                _skipExitOnDisable = false;
                ResetTrigger();
            }
            else if (m_colliders.Count > 0)
            {
                //MWC: route every tracked collider through the normal RemoveTrigger path so full exit semantics
                //fire on disable — OnTrigger_Exit + TriggerExitReaction per collider, and OnGameObjectExit +
                //GameObjectExitReaction + ExitTriggerInteraction + OnEmpty as each owning GameObject empties.
                //Iterate a copy because RemoveTrigger mutates m_colliders.
                _exitBuffer.Clear();
                _exitBuffer.AddRange(m_colliders);

                foreach (var c in _exitBuffer)
                {
                    if (m_colliders.Contains(c)) //a listener may have already released it
                        RemoveTrigger(c, true); //the last removal empties the set and calls ResetTrigger()/OnEmpty
                }
            }
            else
            {
                ResetTrigger(); //MWC: nothing was inside, but still clear state / stop the Stay coroutine
            }

            if (m_debug) Debug.Log($"<b>{name}</b> [Exit All Colliders and Triggers] ", this);

            //MWC: guard against a missing collider so disabling the proxy never throws
            if (ownCollider) ownCollider.enabled = false; //Disable the Collider when the Trigger Proxy is disabled
        }

        public virtual bool TrueConditions(Collider other)
        {
            if (!Active) return false;
            if (other == null) return false;        // you are CALLING A ELIMINATED ONE (must run before anything touches 'other')
            if (ownCollider == null) return false;  // You don't have a trigger

            if (tags != null && tags.Length > 0)
            {
                if (!other.HasMalbersTagInParent(tags) &&
                    !other.HasMalbersTagInChildren(tags)) return false;
            }

            //if (other.gameObject.isStatic && ignoreStatic.Value) return false; // Do not interact with static colliders

            if (triggerInteraction == QueryTriggerInteraction.Ignore && other.isTrigger) return false; // Check Trigger Interactions 
            if (!MTools.Layer_in_LayerMask(other.gameObject.layer, Layer)) return false;
            if (transform.SameHierarchy(other.transform)) return false;                 // Do not Interact with yourself

            var otherRoot = other.GetComponentInParent<IObjectCore>();

            if (otherRoot != null && transform.SameHierarchy(otherRoot.transform)) return false;    // Do not Interact with `your Mount

            if (Owner != null && other.transform.SameHierarchy(Owner)) return false;    // Do not Interact with yourself

            if (!Conditions.Evaluate(other)) return false; // Check the conditions

            return true;
        }

        public virtual void OnTriggerEnter(Collider other)
        {
            if (!TrueConditions(other)) return;

            GameObject realRoot = MTools.FindRealRoot(other);

            if (m_colliders.Add(other)) //if the entering collider is not already on the list add it
            {
                //MWC: fire Collider-enter events only when it's actually a new collider (not a duplicate re-send)
                OnTrigger_Enter.Invoke(other); //Invoke when a Collider enters the Trigger
                TriggerEnterReaction.React(other);

                if (m_debug) Debug.Log($"<b>{name}</b> [Entering Collider] -> [{other.name}]", this);

                //A listener may have disabled the Proxy (Activate(false)): OnDisable already released everything,
                //so stop here instead of re-tracking the collider and firing Enter on a disabled Proxy.
                if (!Active) return;

                AddTarget(other);
                StartSweep();

                RemoveDisabledColliders();

                //Only go on to the GameObject stage while the collider is genuinely tracked: a listener may have
                //disabled the Proxy, or destroyed/disabled the collider itself, and the sweep just released it.
                if (!Active || !m_colliders.Contains(other)) return;
            }

            if (realRoot == null) return;                       //a listener destroyed it; never track a dead root
            if (EnteringGameObjects.Contains(realRoot)) return;

            EnterTriggerInteraction?.Invoke(realRoot, other, this);
            EnteringGameObjects.Add(realRoot);
            OnGameObjectEnter.Invoke(realRoot);
            GameObjectEnterReaction.React(realRoot);

            if (m_debug) Debug.Log($"<b>{name}</b> [Entering GameObject] -> [{realRoot.name}]", this);

            if (OneTimeUse.Value)
            {
                _skipExitOnDisable = true; //disable without firing a synthetic Exit for the object that just entered
                enabled = false;
            }
        }

        /// <summary>Releases every tracked collider that was disabled or destroyed while inside the trigger</summary>
        private void RemoveDisabledColliders()
        {
            if (_sweeping) return; //an Exit listener re-entered the sweep; the outer pass already covers the snapshot
            _sweeping = true;

            try
            {
                _disabledBuffer.Clear();
                _disabledBuffer.AddRange(m_colliders);

                foreach (var col in _disabledBuffer)
                {
                    if (!m_colliders.Contains(col)) continue; //already released by a listener
                    if (col == null || !col.enabled) RemoveTrigger(col, true);
                }
            }
            finally { _sweeping = false; }
        }

        public virtual void OnTriggerExit(Collider other) => TriggerExit(other, true);

        public virtual void TriggerExit(Collider other, bool remove)
        {
            //MWC: Exit must NOT re-evaluate the enter conditions. Active/Layer/Tags/Conditions can change
            //while a collider is inside; if we re-checked them here a changed condition would strand the
            //collider in the set forever (no exit, no OnGameObjectExit, no OnEmpty). Gate on membership instead.
            //A destroyed collider still hashes by instance ID, so it is found and released here as well:
            //do not use Unity's fake-null check, it would strand it in the set forever.
            if (other is not null && m_colliders.Contains(other))
                RemoveTrigger(other, remove);
        }

        public virtual void RemoveTrigger(Collider other, bool remove)
        {
            //MWC: 'other' can be a destroyed (null) collider here (e.g. RemoveDisabledColliders detecting a
            //destroyed member). FindRealRoot dereferences collider.transform, so guard against null.
            GameObject realRoot = other != null ? MTools.FindRealRoot(other) : null;

            OnTrigger_Exit.Invoke(other);
            TriggerExitReaction.React(other);

            m_colliders.Remove(other);
            RemoveTarget(other, remove);

            if (m_debug && other) Debug.Log($"<b>{name}</b> [Exit Collider] -> [{other.name}]", this);

            //MWC: only resolve/clean the owning GameObject when we still have a valid root
            if (realRoot != null && EnteringGameObjects.Contains(realRoot)) //Means that the Entering GameObject still exist
            {
                // 0 allocation and lightweight method.
                bool anyMatchingColliders = false;
                foreach (var c in m_colliders)
                {
                    if (c && c.transform.SameHierarchy(realRoot.transform))
                    {
                        anyMatchingColliders = true;
                        break;
                    }
                }

                if (!anyMatchingColliders)
                {
                    EnteringGameObjects.Remove(realRoot);
                    OnGameObjectExit.Invoke(realRoot);
                    GameObjectExitReaction.React(realRoot);
                    ExitTriggerInteraction?.Invoke(realRoot, other, this);

                    if (m_debug) Debug.Log($"<b>{name}</b> [Leaving Gameobject] -> [{realRoot.name}]", this);
                }
            }
            else if (realRoot == null)
            {
                //The collider was destroyed, so its root could not be resolved. Its GameObject would stay in
                //EnteringGameObjects for good (no Exit, no OnEmpty) whenever something else is still inside.
                RemoveOrphanGameObjects();
            }

            if (m_colliders.Count == 0) ResetTrigger();

        }

        /// <summary>Releases tracked GameObjects that have no collider left inside, used when a destroyed collider made its root unresolvable</summary>
        protected virtual void RemoveOrphanGameObjects()
        {
            if (EnteringGameObjects.Count == 0) return;
            if (_purgingOrphans) return; //an Exit listener re-entered; the outer pass already owns the snapshot
            _purgingOrphans = true;

            try
            {
                _orphanObjects.Clear();

                foreach (var root in EnteringGameObjects)
                {
                    bool anyMatchingColliders = false;

                    foreach (var c in m_colliders)
                    {
                        if (c && root && c.transform.SameHierarchy(root.transform))
                        {
                            anyMatchingColliders = true;
                            break;
                        }
                    }

                    if (!anyMatchingColliders) _orphanObjects.Add(root);
                }

                foreach (var root in _orphanObjects)
                {
                    if (!EnteringGameObjects.Remove(root)) continue; //already released by a listener

                    OnGameObjectExit.Invoke(root);
                    GameObjectExitReaction.React(root);

                    //ExitTriggerInteraction is deliberately NOT raised here: its subscribers expect a usable Collider
                    //and the only one we could pass was destroyed. The GameObject-level exit is what listeners need.

                    if (m_debug) Debug.Log($"<b>{name}</b> [Leaving Gameobject (orphan)] -> [{(root ? root.name : "Destroyed")}]", this);
                }
            }
            finally { _purgingOrphans = false; }
        }

        /// <summary>Add a Trigger Target to every new Collider found</summary>
        protected virtual void AddTarget(Collider other)
        {
            if (!other) return;
            var triggerTarget = TriggerRegistry.GetTargetForCollider(other);

            if (!triggerTarget)
            {
                triggerTarget = other.gameObject.AddComponent<TriggerTarget>();
                triggerTarget.AutoAdded = true; //MWC: flag so the component removes itself when no proxies reference it
            }

            triggerTarget.AddProxy(this);
        }


        /// <summary>OnTrigger exit Logic</summary>
        internal void RemoveTarget(Collider other, bool remove)
        {
            var triggerTarget = TriggerRegistry.GetTargetForCollider(other);

            if (!triggerTarget)
            {
                return;
            }

            if (remove)
                triggerTarget.RemoveProxy(this);
        }

        /// <summary>Clears every tracked collider and GameObject without firing Exit events, then fires OnEmpty</summary>
        public virtual void ResetTrigger()
        {
            //Detach from every TriggerTarget first. A manual Reset (e.g. MPickUp after a pick up) used to skip
            //RemoveTarget, so auto-added TriggerTargets kept a stale reference to this Proxy and never cleaned up.
            if (m_colliders.Count > 0)
            {
                _resetBuffer.Clear();
                _resetBuffer.AddRange(m_colliders);
                foreach (var col in _resetBuffer) RemoveTarget(col, true);
            }

            m_colliders.Clear();
            EnteringGameObjects.Clear();
            OnEmpty.Invoke();

            StopAllCoroutines();
            _sweep = null;

            //MWC: Unity throws "Coroutine couldn't be started because the game object is inactive" if we start
            //while the component is disabled (e.g. ResetTrigger called from OnDisable). Guard on active & enabled.
            if (useOnTriggerStay.Value && isActiveAndEnabled)
            {
                StartCoroutine(C_TriggerStay());
            }
        }



        public virtual void Activate(bool value)
        {
            if (value && !gameObject.activeSelf)
                gameObject.SetActive(true); //Activate the GameObject if it is not active

            enabled = value;
        }

        /// <summary>Read-only view of every Collider currently tracked inside the Trigger (listeners use it to hand focus to the next object still inside)</summary>
        public IReadOnlyCollection<Collider> Colliders => m_colliders;

        /// <summary>True when a tracked and enabled Collider other than [ignore] resolves to [target] through [resolve]. Used by Exit listeners to keep multi-collider objects focused while one of their colliders is still inside</summary>
        public bool StillInside<T>(T target, Collider ignore, Func<Collider, T> resolve) where T : class
        {
            if (target == null) return false;

            foreach (var c in m_colliders)
            {
                if (c == null || c == ignore || !c.enabled) continue;
                if (resolve(c) == target) return true;
            }
            return false;
        }
        IEnumerator C_TriggerStay()
        {
            while (true)
            {
                yield return null;
                CheckOntriggerStay();
            }
        }

        /// <summary>Starts the interval sweep when the first collider enters. No-op when Stay is On (it sweeps every frame), when the interval is 0, or when already running</summary>
        private void StartSweep()
        {
            if (_sweep != null || useOnTriggerStay.Value || sweepInterval <= 0 || !isActiveAndEnabled) return;
            _sweep = StartCoroutine(C_Sweep());
        }

        /// <summary>Purges disabled/destroyed members every [sweepInterval] seconds while anything is inside, then ends itself</summary>
        IEnumerator C_Sweep()
        {
            var wait = new WaitForSeconds(sweepInterval);

            while (m_colliders.Count > 0)
            {
                yield return wait;
                RemoveDisabledColliders();
            }
            _sweep = null;
        }

        /// <summary> Purges colliders that were disabled/destroyed while inside the trigger. Unity may not send OnTriggerExit
        /// for a Collider.enabled=false, and a destroyed one may arrive as fake-null. The [sweepInterval] coroutine (or the
        /// per-frame Stay sweep) already does this automatically; call it to force an immediate refresh. </summary>
        public void RefreshColliders() => RemoveDisabledColliders();

        /// <summary>Fires the Stay events for every tracked GameObject and Collider (called every frame by the Stay coroutine)</summary>
        public virtual void CheckOntriggerStay()
        {
            //MWC: sweep out any colliders that were disabled/destroyed since last frame so Stay/exit stay accurate
            RemoveDisabledColliders();

            //Iterate snapshots: a Stay listener/reaction can release the very entry being visited (deactivate, kill...),
            //which would throw 'Collection was modified' on the live set and silently kill this coroutine for good.
            _stayObjects.Clear();
            _stayObjects.AddRange(EnteringGameObjects);

            foreach (var gos in _stayObjects)
            {
                if (!gos || !EnteringGameObjects.Contains(gos)) continue; //released by a previous listener this frame
                OnGameObjectStay.Invoke(gos);
                GameObjectStayReaction.React(gos);
            }

            _stayColliders.Clear();
            _stayColliders.AddRange(m_colliders);

            foreach (var col in _stayColliders)
            {
                if (!col || !m_colliders.Contains(col)) continue; //released by a previous listener this frame
                OnTrigger_Stay.Invoke(col);
                TriggerStayReaction.React(col);
            }
        }

        public virtual void SetLayer(LayerMask mask, QueryTriggerInteraction triggerInteraction, Transform Owner, Tag[] tags = null)
        {
            TriggerInteraction = triggerInteraction;

            if (this.tags == null || this.tags.Length == 0)
                this.tags = new MTags(tags);
            else
                this.tags.Merge(tags);

            Layer = mask;
            this.Owner = Owner;

        }

        public virtual void SetLayer(LayerMask mask, QueryTriggerInteraction triggerInteraction, Transform Owner, MTags tags)
        {
            TriggerInteraction = triggerInteraction;

            if (this.tags == null || this.tags.Length == 0)
                this.tags = new MTags(tags);
            else
                this.tags.Merge(tags);


            Layer = mask;
            this.Owner = Owner;

        }

        public static TriggerProxy CheckTriggerProxy
            (Collider col, LayerMask Layer, QueryTriggerInteraction TriggerInteraction, Transform Owner, bool overrideValue = false)
        {
            TriggerProxy Proxy = null;

            if (col == null) return Proxy;

            if (!col.TryGetComponent(out Proxy))
            {
                Proxy = col.gameObject.AddComponent<TriggerProxy>();
                Proxy.SetLayer(Layer, TriggerInteraction, Owner);
            }

            if (overrideValue)
                Proxy.SetLayer(Layer, TriggerInteraction, Owner);
            else
            {
                //merge layer values
                Proxy.Layer |= Layer;
            }

            col.gameObject.SetLayer(2, false);      //Force the Trigger Area to be on the Ignore Raycast Layer
            col.isTrigger = true;                   //Force to be a Trigger

            return Proxy;
        }

        private void OnValidate()
        {
            if (ownCollider == null || !ownCollider.transform.SameHierarchy(transform))
                ownCollider = GetComponent<Collider>();
        }

        [HideInInspector] public int Editor_Tabs1;
    }

    #region Inspector


#if UNITY_EDITOR
    [CanEditMultipleObjects, CustomEditor(typeof(TriggerProxy))]
    public class TriggerProxyEditor : Editor
    {
        SerializedProperty debug, ID, sweepInterval,
            OnTrigger_Enter, OnTrigger_Exit, OnEmpty, useOnTriggerStay, OnTrigger_Stay, ignoreStatic, Editor_Tabs1, OneTimeUse,
            triggerInteraction, hitLayer, OnGameObjectEnter, OnGameObjectExit, OnGameObjectStay, Tags, Conditions, ownCollider,
            TriggerEnterReaction, TriggerExitReaction, TriggerStayReaction,
            GameObjectEnterReaction, GameObjectExitReaction, GameObjectStayReaction
            ;

        TriggerProxy m;

        protected string[] Tabs1 = new string[] { "General", "Events", "Reactions" };

        private void OnEnable()
        {
            m = (TriggerProxy)target;
            OnEmpty = serializedObject.FindProperty("OnEmpty");
            ownCollider = serializedObject.FindProperty("ownCollider");
            triggerInteraction = serializedObject.FindProperty("triggerInteraction");
            useOnTriggerStay = serializedObject.FindProperty("useOnTriggerStay");
            sweepInterval = serializedObject.FindProperty("sweepInterval");
            hitLayer = serializedObject.FindProperty("hitLayer");
            debug = serializedObject.FindProperty("m_debug");
            ignoreStatic = serializedObject.FindProperty("ignoreStatic");
            ID = serializedObject.FindProperty("ID");

            OnTrigger_Enter = serializedObject.FindProperty("OnTrigger_Enter");
            OnTrigger_Exit = serializedObject.FindProperty("OnTrigger_Exit");
            OnTrigger_Stay = serializedObject.FindProperty("OnTrigger_Stay");


            OnGameObjectEnter = serializedObject.FindProperty("OnGameObjectEnter");
            OnGameObjectExit = serializedObject.FindProperty("OnGameObjectExit");
            OnGameObjectStay = serializedObject.FindProperty("OnGameObjectStay");

            Tags = serializedObject.FindProperty("tags");
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            OneTimeUse = serializedObject.FindProperty("OneTimeUse");
            Conditions = serializedObject.FindProperty("Conditions");


            TriggerEnterReaction = serializedObject.FindProperty("TriggerEnterReaction");
            TriggerExitReaction = serializedObject.FindProperty("TriggerExitReaction");
            TriggerStayReaction = serializedObject.FindProperty("TriggerStayReaction");

            GameObjectEnterReaction = serializedObject.FindProperty("GameObjectEnterReaction");
            GameObjectExitReaction = serializedObject.FindProperty("GameObjectExitReaction");
            GameObjectStayReaction = serializedObject.FindProperty("GameObjectStayReaction");
        }


        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription("Use this component to do quick OnTrigger Enter/Exit logics");

            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs1);


            switch (Editor_Tabs1.intValue)
            {
                case 0: DrawGeneral(); break;
                case 1: DrawEvents(); break;
                case 2: DrawReactions(); break;
            }

            if (Application.isPlaying)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);

                        //   EditorGUILayout.ObjectField("Own Collider", m.trigger, typeof(Collider), false);

                        EditorGUILayout.LabelField("GameObjects (" + m.EnteringGameObjects.Count + ")", EditorStyles.boldLabel);
                        foreach (var item in m.EnteringGameObjects)
                        {
                            if (item != null) EditorGUILayout.ObjectField(item.name, item, typeof(GameObject), false);
                        }

                        EditorGUILayout.LabelField("Colliders (" + m.m_colliders.Count + ")", EditorStyles.boldLabel);

                        foreach (var item in m.m_colliders)
                        {
                            if (item != null) EditorGUILayout.ObjectField(item.name, item, typeof(Collider), false);
                        }

                        //EditorGUILayout.LabelField("Targets (" + m.TriggerTargets.Count + ")", EditorStyles.boldLabel);

                        //foreach (var item in m.TriggerTargets)
                        //{
                        //    if (item != null) EditorGUILayout.ObjectField(item.name, item, typeof(Collider), false);
                        //}
                    }
                    Repaint();
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawReactions()
        {
            EditorGUILayout.PropertyField(TriggerEnterReaction);
            EditorGUILayout.PropertyField(TriggerExitReaction);
            if (m.useOnTriggerStay.Value)
                EditorGUILayout.PropertyField(TriggerStayReaction);

            EditorGUILayout.PropertyField(GameObjectEnterReaction);
            EditorGUILayout.PropertyField(GameObjectExitReaction);
            if (m.useOnTriggerStay.Value)
                EditorGUILayout.PropertyField(GameObjectStayReaction);
        }

        private void DrawGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(ID);
                    MalbersEditor.DrawDebugIcon(debug);
                }
                EditorGUILayout.PropertyField(ownCollider, new GUIContent("Trigger"));
                EditorGUILayout.PropertyField(hitLayer, new GUIContent("Layer"));

                EditorGUILayout.PropertyField(triggerInteraction);
                EditorGUILayout.PropertyField(useOnTriggerStay);
                if (!m.useOnTriggerStay.Value)
                    EditorGUILayout.PropertyField(sweepInterval);
                EditorGUILayout.PropertyField(OneTimeUse);
                EditorGUILayout.PropertyField(ignoreStatic);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(Tags, true);
                EditorGUILayout.PropertyField(Conditions);
                EditorGUI.indentLevel--;
            }
        }



        private void DrawEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(OnTrigger_Enter, new GUIContent("On Trigger Enter"));
                EditorGUILayout.PropertyField(OnTrigger_Exit, new GUIContent("On Trigger Exit"));
                EditorGUILayout.PropertyField(OnEmpty);
                if (m.useOnTriggerStay.Value)
                    EditorGUILayout.PropertyField(OnTrigger_Stay, new GUIContent("On Trigger Stay"));


                EditorGUILayout.PropertyField(OnGameObjectEnter, new GUIContent("On GameObject Enter "));
                EditorGUILayout.PropertyField(OnGameObjectExit, new GUIContent("On GameObject Exit"));
                if (m.useOnTriggerStay.Value)
                    EditorGUILayout.PropertyField(OnGameObjectStay, new GUIContent("On GameObject Stay"));
            }
        }
    }
#endif
    #endregion
}