// Last Change: 9/24/2026 1:14pm
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using UnityEngine;
using System.Collections.Generic;
using MalbersAnimations.Reactions;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    [AddComponentMenu("Malbers/Interaction/Interactor")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/global-components/interactor")]
    public class MInteractor : MonoBehaviour, IInteractor
    {
        [Tooltip("Layer for the Interact with colliders")]
        [SerializeField] private LayerReference Layer = new(-1);
        [SerializeField] private QueryTriggerInteraction TriggerInteraction = QueryTriggerInteraction.Ignore;

        [Tooltip("Include filter: only Interactables whose collider (or a parent/child of it) has one of these Tags are focused. Empty = any Interactable")]
        public MTags Tags;

        [Tooltip("ID for the Interactor")]
        public IntReference m_ID = new(0);

        [Tooltip("Collider set as Trigger to Find Interactables OnTrigger Enter")]
        public Collider InteractionArea;

        [Tooltip("Focus only one Interactable at a time: the newest to enter the Interaction Area takes the focus (the previous one gets Unfocused). " +
            "When the focused one leaves or is removed, the closest Interactable still inside is focused again.\n" +
            "OFF: every Interactable inside the area stays focused and Interact() fires on all of them")]
        public BoolReference SingleFocus = new(true);

        public GameObjectEvent OnFocused = new();
        public GameObjectEvent OnUnfocused = new();
        public GameObjectEvent OnInteractWithGO = new();
        public IntEvent OnInteractWith = new();

        public System.Action<IInteractable> OnFocusing;
        public System.Action<IInteractable> OnUnFocusing;
        public System.Action<IInteractable> OnInteract;

        public int ID => m_ID.Value;

        public bool Active { get => !enabled; set => enabled = !value; }
        public bool ItemInFocus { get; set; }

        public GameObject UserGo => RealRoot.gameObject;

        /// <summary>Interactables currently focused by this Interactor (a single one when [Single Focus] is On) </summary>
        public HashSet<IInteractable> FocusedInteractables { get; set; }

        /// <summary>Every Interactable currently inside the Interaction Area, focused or not (the Single Focus candidates)</summary>
        public HashSet<IInteractable> Inside { get; private set; } = new();

        /// <summary>Snapshot buffer: Focus/UnFocus listeners mutate the sets while we iterate</summary>
        private readonly List<IInteractable> _buffer = new(4);

        /// <summary>Interaction Trigger Proxy to Subscribe to OnEnter OnExit Trigger</summary>
        public TriggerProxy Proxy { get; set; }

        public List<MInteractorReaction> reactions = new();

        public List<MInteractorReactionFocused> focusReactions = new();

        private Transform RealRoot;

        //  public IInteractable FocusedItem;

        public bool debug;

        private void OnValidate()
        {
            if (InteractionArea != null) { InteractionArea.isTrigger = true; }
        }


        private void OnEnable()
        {
            FocusedInteractables = new();
            Inside.Clear();

            RealRoot = transform.FindObjectCore();

            if (InteractionArea == null) InteractionArea = GetComponent<Collider>();

            if (InteractionArea == null)
            {
                Debug.LogWarning($"<B>[{name}]</B> Interactor needs a Collider set as Trigger to find Interactables. Please assign one to the Interaction Area field or add a Collider to the Interactor GameObject", this);
            }


            if (InteractionArea)
            {
                Proxy = TriggerProxy.CheckTriggerProxy(InteractionArea, Layer, TriggerInteraction, RealRoot, false);

                if (Proxy)
                {
                    Proxy.OnTrigger_Enter.AddListener(TriggerEnter);
                    Proxy.OnTrigger_Exit.AddListener(TriggerExit);
                    Proxy.tags.Merge(Tags);
                }
            }
        }


        private void OnDisable()
        {
            //Iterate a snapshot: the Interactable calls back UnFocus(item) and mutates the set
            _buffer.Clear();
            _buffer.AddRange(FocusedInteractables);

            foreach (var item in _buffer)
            {
                if (item.IsUnityRefNull()) continue;

                if (item.UserGo)
                {
                    OnUnfocused.Invoke(item.UserGo);
                    OnUnFocusing?.Invoke(item);     //System.Action to notify the UnFocusing of an Interactable
                }

                //UnFocus the Interactable
                item.UnFocus(this);
            }

            FocusedInteractables = new();
            Inside.Clear();

            if (Proxy)
            {
                Proxy.OnTrigger_Enter.RemoveListener(TriggerEnter);
                Proxy.OnTrigger_Exit.RemoveListener(TriggerExit);
            }
        }


        /// <summary>Tracks and focuses every Interactable on a collider entering the Interaction Area (Layer and Tags include filter)</summary>
        private void TriggerEnter(Collider collider)
        {
            if (!MTools.Layer_in_LayerMask(collider.gameObject.layer, Layer)) return;   //The collider is not in the correct layer, ignore it

            //Include filter (same test as the TriggerProxy): with Tags set, the collider, a parent or a child must carry one of them
            if (Tags != null && Tags.Count > 0 &&
                !collider.HasMalbersTagInParent(Tags.tags) &&
                !collider.HasMalbersTagInChildren(Tags.tags)) return;

            var NewInteractables = collider.FindInterfaces<IInteractable>(); //Find all Interactables

            if (NewInteractables != null)
            {
                foreach (var item in NewInteractables)
                {
                    Inside.Add(item);                                 //Track it as a candidate while it stays inside the area
                    if (FocusedInteractables.Contains(item)) continue;

                    Focus(item); //Single Focus: the newest to enter takes the focus from the current one
                }
            }
        }

        /// <summary>Unfocuses every Interactable on a collider leaving the Interaction Area, then hands the focus to the next one inside (Single Focus)</summary>
        private void TriggerExit(Collider collider)
        {
            //TriggerProxy releases destroyed colliders through this event too: purge dead entries instead of resolving them
            if (collider == null)
            {
                Inside.RemoveWhere(i => i.IsUnityRefNull());
                FocusedInteractables.RemoveWhere(i => i.IsUnityRefNull());
                if (FocusedInteractables.Count == 0) ItemInFocus = false;
            }
            else
            {
                if (!MTools.Layer_in_LayerMask(collider.gameObject.layer, Layer)) return;   //The collider is not in the correct layer, ignore it

                var NewInteractable = collider.FindInterfaces<IInteractable>();

                if (NewInteractable != null)
                {
                    foreach (var item in NewInteractable)
                    {
                        if (item == null) continue;

                        //Another collider of the same Interactable is still inside (multi-collider objects), keep it
                        if (Proxy.StillInside(item, collider, ResolveInteractable)) continue;

                        Inside.Remove(item);
                        if (FocusedInteractables.Contains(item)) UnFocus(item); //means the interactor is exiting
                    }
                }
            }

            FocusNext(); //Single Focus: the focused one left, focus the closest Interactable still inside
        }

        /// <summary>Resolves the Interactable a tracked collider belongs to (same lookup the Enter uses)</summary>
        private static IInteractable ResolveInteractable(Collider c) => c.FindInterface<IInteractable>();

        /// <summary>Focuses an Interactable. With [Single Focus] the current one is Unfocused first, so only one is focused at a time</summary>
        public virtual void Focus(IInteractable item)
        {
            if (item == null || !item.Active) return;      //Ignore One Disable Interactors
            if (FocusedInteractables.Contains(item)) return; //Already focused

            if (SingleFocus.Value && FocusedInteractables.Count > 0)
            {
                //One at a time: the newest wins, the previous one gets its OnUnfocused (it stays inside as a candidate)
                _buffer.Clear();
                _buffer.AddRange(FocusedInteractables);
                foreach (var previous in _buffer) UnFocus(previous);
            }

            FocusedInteractables.Add(item);        //add to the list all the focus items
            item.Focus(this);                      //Focus the Interactable

            OnFocused.Invoke(item.UserGo);
            OnFocusing?.Invoke(item);              //System.Action to notify the Focusing of an Interactable
            focusReactions.ForEach(r => r.Focus(item.Index, item.UserGo)); //Focus with all the reactions
            ItemInFocus = true;
            if (item.Auto) Interact(item);         //Interact if the interactable is on Auto
        }

        /// <summary>Single Focus: when nothing is focused, focuses the closest Active Interactable still inside the Interaction Area</summary>
        public virtual void FocusNext()
        {
            if (!SingleFocus.Value || FocusedInteractables.Count > 0 || Inside.Count == 0) return;
            if (!isActiveAndEnabled || InteractionArea == null || !InteractionArea.enabled) return;

            IInteractable best = null;
            float bestDist = float.MaxValue;
            var center = InteractionArea.bounds.center;

            foreach (var candidate in Inside)
            {
                if (candidate.IsUnityRefNull() || !candidate.Active) continue;

                var dist = (candidate.transform.position - center).sqrMagnitude;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }

            if (best != null)
            {
                Debugging($"Focus next <B>[{best.transform.name}] [ID: {best.Index}]</B>");
                Focus(best);
            }
        }

        public void UnFocus(IInteractable item)
        {
            if (item != null && FocusedInteractables.Contains(item))
            {
                FocusedInteractables.Remove(item);

                if (item.UserGo)
                {
                    OnUnfocused.Invoke(item.UserGo);
                    OnUnFocusing?.Invoke(item); //System.Action to notify the UnFocusing of an Interactable

                    focusReactions.ForEach(r => r.UnFocus(item.Index, item.UserGo)); //UnFocus with all the reactions
                }

                //UnFocus the Interactable
                item.UnFocus(this);

                if (FocusedInteractables.Count == 0) ItemInFocus = false;

            }
        }


        /// <summary> Receive an Interaction from the Interacter </summary>
        public bool Interact(IInteractable inter)
        {
            if (inter.Interact(this))
            {
                OnInteractWithGO.Invoke(inter.UserGo);
                OnInteractWith.Invoke(inter.Index);

                OnInteract?.Invoke(inter); //System.Action to notify the Interaction of an Interactable

                reactions.ForEach(r => r.React(inter.Index, inter.UserGo)); //React with all the reactions

                Debugging($"Interact with <B>[{inter.transform.name}] [ID: {inter.Index}]</B>");

                return true;
            }
            return false;
        }

        /// <summary> Interact with multiple focused items at the same time (in reverse order) </summary>
        public void Interact()
        {
            if (FocusedInteractables == null || FocusedInteractables.Count == 0)
                return;

            var interactablesArray = new IInteractable[FocusedInteractables.Count];
            FocusedInteractables.CopyTo(interactablesArray);

            for (int i = interactablesArray.Length - 1; i >= 0; i--)
            {
                Interact(interactablesArray[i]);
            }
        }

        /// <summary>Removes an Interactable from the focus and the candidates (e.g. it was picked up), then hands the focus to the next one inside (Single Focus)</summary>
        public void RemoveFocusedItem(IInteractable item)
        {
            if (item == null) return;

            Inside.Remove(item); //No longer a candidate: its colliders are about to be disabled/parented to us

            if (FocusedInteractables != null && FocusedInteractables.Contains(item))
            {
                FocusedInteractables.Remove(item);
                item.UnFocus(this);
                OnUnfocused.Invoke(item.UserGo);
                OnUnFocusing?.Invoke(item);             //System.Action to notify the UnFocusing of an Interactable

                if (FocusedInteractables.Count == 0) ItemInFocus = false;
            }

            FocusNext();
        }

        public void Restart()
        {
            FocusedInteractables = new();
            Inside.Clear();
            ItemInFocus = false;
            OnUnfocused.Invoke(null);
            OnFocused.Invoke(null);

            OnUnFocusing?.Invoke(null);
            OnFocusing?.Invoke(null);
        }

        public void Interact(GameObject interactable)
        {
            if (interactable)
                Interact(interactable.FindInterface<IInteractable>());
        }

        public void Interact(Component interactable)
        {
            if (interactable)
                Interact(interactable.FindInterface<IInteractable>());
        }

        /// <summary>Console log for this Interactor, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("MALBERS_DEBUG")]
        private void Debugging(string msg)
        {
            if (debug)
                MDebug.Log($"<B><color=yellow>Interactor: </color>[{UserGo.name}]</B> -> [<color=yellow>{msg}</color>]", this);
        }


        [SerializeField] private int Editor_Tabs1;
    }


    [System.Serializable]
    public class MInteractorReaction
    {
        public string Description = "Reaction by Interactor";
        public ComparerNumber Is = ComparerNumber.Equal;
        [Tooltip("Interactable Index. Set it to Zero or 1 to use this reaction with all Interactables")]
        public IntReference Index = new();
        public Reaction2 reaction;

        public bool React(int ID, GameObject Target)
        {
            if (reaction.IsValid)
            {
                if (Index.Value <= 0 || Index.Value.MCompare(ID, Is))
                {
                    return reaction.TryReact(Target);
                }
            }
            return false;
        }
    }

    [System.Serializable]
    public class MInteractorReactionFocused
    {
        public string Description = "Reaction by Interactor";
        public ComparerNumber Is = ComparerNumber.Equal;
        [Tooltip("Interactable Index. Set it to Zero or 1 to use this reaction with all Interactables")]
        public IntReference Index = new();
        public Reaction2 OnFocused;
        public Reaction2 OnUnFocused;


        public bool Focus(int ID, GameObject Target)
        {
            if (OnFocused.IsValid)
            {
                if (Index.Value <= 0 || Index.Value.MCompare(ID, Is))
                {
                    return OnFocused.TryReact(Target);
                }
            }
            return false;
        }

        public bool UnFocus(int ID, GameObject Target)
        {
            if (OnUnFocused.IsValid)
            {
                if (Index.Value <= 0 || Index.Value.MCompare(ID, Is))
                {
                    return OnUnFocused.TryReact(Target);
                }
            }
            return false;
        }
    }




#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(MInteractor))]
    public class MInteractorEditor : Editor
    {
        SerializedProperty m_ID, InteractionArea, SingleFocus, Editor_Tabs1,
            OnFocusedInteractable,
            OnUnfocusedInteractable,
            OnInteractWithGO, OnInteractWith, Tags,
            reactions, focusReactions,
            debug,
            triggerInteraction, Layer;
        protected string[] Tabs1 = new string[] { "General", "Events", "Reactions" };

        MInteractor M;

        private void OnEnable()
        {
            M = (MInteractor)target;

            m_ID = serializedObject.FindProperty("m_ID");
            InteractionArea = serializedObject.FindProperty("InteractionArea");
            SingleFocus = serializedObject.FindProperty(nameof(MInteractor.SingleFocus));
            OnInteractWithGO = serializedObject.FindProperty("OnInteractWithGO");
            OnInteractWith = serializedObject.FindProperty("OnInteractWith");
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            OnFocusedInteractable = serializedObject.FindProperty("OnFocused");
            OnUnfocusedInteractable = serializedObject.FindProperty("OnUnfocused");
            Layer = serializedObject.FindProperty("Layer");
            triggerInteraction = serializedObject.FindProperty("TriggerInteraction");
            reactions = serializedObject.FindProperty("reactions");
            focusReactions = serializedObject.FindProperty("focusReactions");
            debug = serializedObject.FindProperty("debug");
            Tags = serializedObject.FindProperty("Tags");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription("Interactor element that invoke events when interacts with an Interactable");

            using (new GUILayout.HorizontalScope())
            {
                Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs1);
                MalbersEditor.DrawDebugIcon(debug);
            }

            switch (Editor_Tabs1.intValue)
            {
                case 0: DrawGeneral(); break;
                case 1: DrawEvents(); break;
                case 2: DrawReactions(); break;
                default: break;
            }

            if (Application.isPlaying)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (M.FocusedInteractables != null)
                        {
                            foreach (var item in M.FocusedInteractables)
                            {
                                if (item.IsUnityRefNull()) continue;
                                EditorGUILayout.ObjectField($"Focused Item [ID:{item.Index}]", item.transform.gameObject, typeof(GameObject), false);
                            }
                        }

                        foreach (var item in M.Inside)
                        {
                            if (item.IsUnityRefNull() || M.FocusedInteractables.Contains(item)) continue;
                            EditorGUILayout.ObjectField($"Inside [ID:{item.Index}]", item.transform.gameObject, typeof(GameObject), false);
                        }
                    }
                }
                Repaint();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawReactions()
        {
            EditorGUILayout.PropertyField(reactions);
            EditorGUILayout.PropertyField(focusReactions);
        }

        private void DrawGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(Layer);
                EditorGUILayout.PropertyField(triggerInteraction);
                EditorGUILayout.PropertyField(Tags);
                EditorGUILayout.PropertyField(m_ID);
                EditorGUILayout.PropertyField(InteractionArea);
                EditorGUILayout.PropertyField(SingleFocus);
            }
        }

        private void DrawEvents()
        {
            EditorGUILayout.PropertyField(OnInteractWithGO);
            EditorGUILayout.PropertyField(OnInteractWith);
            EditorGUILayout.PropertyField(OnFocusedInteractable);
            EditorGUILayout.PropertyField(OnUnfocusedInteractable);
        }
    }
#endif
}