// Last Change: 9/30/2026 12:25pm
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using MalbersAnimations.Reactions;

#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif
namespace MalbersAnimations.Utilities
{
    [AddComponentMenu("Malbers/Utilities/Effects - Audio/Effect Manager")]
    public class EffectManager : MonoBehaviour, IAnimatorListener
    {
        [RequiredField, Tooltip("Root Gameobject of the Hierarchy")]
        public Transform Owner;

        public List<Effect> Effects;

        public int SelectedEffect = -1;

        /// <summary>Inspector tab shown for the selected Effect (General, Reactions, Events).</summary>
        [HideInInspector] public int Editor_Tabs1;

        [SerializeField] private bool debug;
        [SerializeField] private bool editOffset;

        public Color debugColor = new(1, 0.6f, 0, 0.333f);

        private Effect Pin_Effect;

        #region Registry

        // MWC — Same pattern as AnimatorEventSounds: Effect Managers announce themselves to the Animator
        // above them, so an Animator State does not have to sweep the hierarchy with GetComponentsInChildren.
        // OnTransformParentChanged is what makes it correct when a weapon carrying its own Effect Manager is
        // equipped, holstered or swapped between hands at runtime.
        private static readonly Dictionary<Animator, List<EffectManager>> Registry = new();

        /// <summary>Increases every time the Registry changes, so cached lookups know when to revalidate.</summary>
        public static int Version { get; private set; }

        /// <summary>Animator this manager registered on, kept so it can unregister from the right list.</summary>
        private Animator registeredOn;

        /// <summary>Animator this manager is currently linked to.</summary>
        public Animator Anim => registeredOn;

        /// <summary>Clears the static caches when entering Play Mode with Domain Reload disabled.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registry.Clear();
            Version = 0;
        }

        /// <summary>Finds the Animator on this hierarchy and adds this manager to its Registry list.</summary>
        private void Register()
        {
            registeredOn = GetComponentInParent<Animator>(true);

            if (registeredOn == null) return;

            if (!Registry.TryGetValue(registeredOn, out var list))
                Registry[registeredOn] = list = new List<EffectManager>();

            if (!list.Contains(this)) list.Add(this);

            Version++;

            if (debug) Debug.Log($"[{name}] Effect Manager registered on Animator [{registeredOn.name}]", this);
        }

        /// <summary>Removes this manager from the Registry list of the Animator it joined.</summary>
        private void UnRegister()
        {
            if (registeredOn != null && Registry.TryGetValue(registeredOn, out var list))
            {
                list.Remove(this);
                if (list.Count == 0) Registry.Remove(registeredOn);
                Version++;
            }

            registeredOn = null;
        }

        private void OnTransformParentChanged()
        {
            if (!isActiveAndEnabled) return; // OnEnable will take care of the registration when it comes back

            UnRegister();                    // leave the old Animator
            Register();                      // join the new Animator and bump the Version
        }

        /// <summary>All the active Effect Managers linked to an Animator. Returns null when there are none.</summary>
        public static List<EffectManager> GetAll(Animator animator)
            => animator != null && Registry.TryGetValue(animator, out var list) ? list : null;

        #endregion

        private void Awake()
        {
            foreach (var e in Effects)
            {
                e.Initialize();
            }
        }

        private void OnEnable() => Register();

        private void OnDisable()
        {
            Stop_Effects(Effects);  //Stop all effects if the Effect Manager is disabled
            UnRegister();
        }


        /// <summary>Plays an Effect using its ID value</summary>
        public virtual void PlayEffect(int ID)
        {
            List<Effect> effects = Effects.FindAll(effect => effect.ID == ID && effect.active == true);

            if (effects != null)
                foreach (var effect in effects) Play(effect);
        }


        public virtual void Effect_Pin(string name)
        {
            Pin_Effect = Effects.Find(effect => effect.Name == name && effect.active == true);
        }


        public virtual void Effect_Pin(int ID)
        {
            Pin_Effect = Effects.Find(effect => effect.ID == ID && effect.active == true);
        }


        public virtual void Effect_Pin_Root(Transform root)
        {
            Pin_Effect.root = root;
        }

        /// <summary>Plays an Effect using its ID value</summary>
        public virtual void PlayEffect(string name)
        {
            List<Effect> effects = Effects.FindAll(effect => effect.Name == name && effect.active == true);

            if (effects != null)
                foreach (var effect in effects) Play(effect);
        }

        /// <summary>Stops an Effect using its ID value</summary>
        public virtual void StopEffect(string name)
        {
            var effects = Effects.FindAll(effect => effect.Name == name && effect.active == true);

            Stop_Effects(effects);
        }

        /// <summary>Stops an Effect using its ID value</summary>
        public virtual void StopEffect(int ID) => Effect_Stop(ID);

        /// <summary>Plays an Effect using its ID value</summary>
        public virtual void Effect_Play(int ID) => PlayEffect(ID);
        public virtual void EffectPlay(int ID) => PlayEffect(ID);
        public virtual void Effect_Play(string name) => PlayEffect(name);
        public virtual void EffectPlay(string name) => PlayEffect(name);

        /// <summary>Stops an Effect using its ID value</summary>
        public virtual void Effect_Stop(int ID)
        {
            var effects = Effects.FindAll(effect => effect.ID == ID && effect.active == true);

            Stop_Effects(effects);
        }

        private void Stop_Effects(List<Effect> effects)
        {
            if (effects != null)
            {
                foreach (var e in effects)
                {
                    StopEffect(e, e.Instance);
                }
            }
        }

        public virtual void StopEffect(Effect e, GameObject instance)
        {
            //Stop the Effect only if is playing
            if (e.IsPlaying || e.life == 0)
            {
                e.OnStopReaction?.React(Owner);
                e.OnStop.Invoke();

                e.IsPlaying = false;

                if (e.effect != null)
                {
                    if (!e.effect.IsPrefab())
                    {
                        if (e.disableOnStop)
                            instance?.SetActive(false);
                    }
                    else
                        Destroy(instance);

                }

                if (debug)
                    Debug.Log($"<B>{Owner.name}</B> Effect Stop: <B>[{e.Name}]</B>", (instance != null ? instance : this));
            }
        }


        /// <summary>Stops an Effect using its ID value</summary>
        public virtual void Effect_Stop(string name)
        {
            var effects = Effects.FindAll(effect => effect.Name == name && effect.active == true);
            Stop_Effects(effects);
        }


        protected virtual void Play(Effect e)
        {
            //e.Modifier?.PreStart(e);        //Execute the Method PreStart Effect if it has a modifier

            if (e.effect != null && e.life > 0 && e.IsPlaying) return; //Do not play a effect that is already playing

            //Delay an action
            this.Delay_Action(e.delay,
                () =>
                {
                    e.IsPlaying = true; //Do not change is Playing to true if the time is 0

                    //Play Audio
                    if (!e.Clip.NullOrEmpty() && e.audioSource != null)
                    {
                        if (e.audioSource.isPlaying) e.audioSource.Stop();

                        e.Clip.Play(e.audioSource);
                    }

                    if (e.effect != null)
                    {
                        if (e.effect.IsPrefab())                        //If instantiate is active (meaning is a prefab)
                        {
                            e.Instance = Instantiate(e.effect);         //Instantiate!
                            //remove the clone name
                            e.Instance.name = e.effect.name + " [Effect]";
                            e.Instance.SetActive(false);

                            e.Instance.transform.localScale *= e.scale;
                        }
                        else
                        {
                            e.Instance = e.effect;                     //Use the effect as the gameobject
                        }

                        if (Owner == null) Owner = transform.root;
                        if (e.Owner == null) e.Owner = Owner;  //Save in all effects that the owner of the effects is this transform


                        if (e.Instance)
                        {
                            //Apply Offsets
                            if (e.isChild)
                            {
                                e.Instance.transform.parent = e.root;
                                e.Offset.RestoreTransform(e.Instance.transform); //Restore the Offset
                                e.Instance.transform.localScale *= e.scale;       //RestoreTransform overwrites the scale, apply the multiplier again
                            }
                            else
                            {
                                //Move to the root position
                                if (e.useRootPosition) e.Instance.transform.position = e.root.position;

                                //Orient to the root rotation
                                if (e.useRootRotation) e.Instance.transform.rotation = e.root.rotation;
                            }


                            e.Instance.SetActive(true);


                            if (e.effect.IsPrefab()) //get the trail renderer and particle system from the Instance instead of the prefab
                            {
                                e.IsTrailRenderer = e.Instance.FindComponent<TrailRenderer>();
                                e.IsParticleSystem = e.Instance.FindComponent<ParticleSystem>();
                            }

                            if (e.IsTrailRenderer) e.IsTrailRenderer.Clear();
                            if (e.IsParticleSystem) e.IsParticleSystem.Play();
                        }
                    }

                    if (e.life > 0)
                    {
                        this.Delay_Action(e.life, () => StopEffect(e, e.Instance));
                    }

                    if (debug)
                        Debug.Log($"<B>{Owner.name}</B> Effect Play: <B>[{e.Name}]</B>", (e.Instance != null ? e.Instance : this));

                    e.OnPlay.Invoke();                 //Invoke the Play Event
                    e.OnPlayReaction?.React(Owner);    //Play the Reaction
                }
            );
        }


        /// <summary>IAnimatorListener function </summary>
        public virtual bool OnAnimatorBehaviourMessage(string message, object value) => this.InvokeWithParams(message, value);

        //─────────────────────────────────CALLBACKS METHODS───────────────────────────────────────────────────────────────────

        /// <summary>Disables all effects using their name </summary>
        public virtual void Effect_Disable(string name)
        {
            List<Effect> effects = Effects.FindAll(effect => effect.Name.ToUpper() == name.ToUpper());

            if (effects != null)
            {
                foreach (var e in effects) e.active = false;
            }
            else
            {
                Debug.LogWarning("No effect with the name: " + name + " was found");
            }
        }

        /// <summary> Disables all effects using their ID</summary>
        public virtual void Effect_Disable(int ID)
        {
            List<Effect> effects = Effects.FindAll(effect => effect.ID == ID);

            if (effects != null)
            {
                foreach (var e in effects) e.active = false;
            }
            else
            {
                Debug.LogWarning("No effect with the ID: " + ID + " was found");
            }
        }

        /// <summary>Enable all effects using their name</summary>
        public virtual void Effect_Enable(string name)
        {
            List<Effect> effects = Effects.FindAll(effect => effect.Name.ToUpper() == name.ToUpper());

            if (effects != null)
            {
                foreach (var e in effects) e.active = true;
            }
            else
            {
                Debug.LogWarning("No effect with the name: " + name + " was found");
            }
        }


        /// <summary> Enable all effects using their ID</summary>
        public virtual void Effect_Enable(int ID)
        {
            List<Effect> effects = Effects.FindAll(effect => effect.ID == ID);

            if (effects != null)
            {
                foreach (var e in effects) e.active = true;
            }
            else
            {
                Debug.LogWarning("No effect with the ID: " + ID + " was found");
            }
        }



#if UNITY_EDITOR
        private void Reset()
        {
            Owner = transform.root;
        }

        private void OnValidate()
        {
            if (Owner == null) Owner = transform.root;

            foreach (var e in Effects)
            {
                if (e != null && e.root == null) e.root = transform; //Make sure the owner is not null
            }
        }

        [ContextMenu("Create Event Listeners")]
        void CreateListeners()
        {
            MEventListener listener = gameObject.FindComponent<MEventListener>();

            if (listener == null) listener = gameObject.AddComponent<MEventListener>();
            if (listener.Events == null) listener.Events = new List<MEventItemListener>();

            MEvent effectEnable = MTools.GetInstance<MEvent>("Effect Enable");
            MEvent effectDisable = MTools.GetInstance<MEvent>("Effect Disable");

            if (listener.Events.Find(item => item.Event == effectEnable) == null)
            {
                var item = new MEventItemListener()
                {
                    Event = effectEnable,
                    useVoid = false,
                    useString = true,
                    useInt = true
                };

                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseInt, Effect_Enable);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseString, Effect_Enable);
                listener.Events.Add(item);

                Debug.Log("<B>Effect Enable</B> Added to the Event Listeners");
            }

            if (listener.Events.Find(item => item.Event == effectDisable) == null)
            {
                var item = new MEventItemListener()
                {
                    Event = effectDisable,
                    useVoid = false,
                    useString = true,
                    useInt = true
                };

                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseInt, Effect_Disable);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseString, Effect_Disable);
                listener.Events.Add(item);

                Debug.Log("<B>Effect Disable</B> Added to the Event Listeners");
            }

            UnityEditor.EditorUtility.SetDirty(listener);
        }

        /// <summary>Every MeshFilter / SkinnedMeshRenderer found inside the selected Effect (drawn by the gizmo).</summary>
        [System.NonSerialized] private readonly List<Component> gizmoMeshes = new();
        /// <summary>Effect GameObject the gizmo mesh cache was built from.</summary>
        [System.NonSerialized] private GameObject gizmoMeshesOwner;

        /// <summary>Collects all the meshes of the selected Effect for the Scene View gizmo.</summary>
        public void FindSelectedEffectMesh()
        {
            gizmoMeshes.Clear();
            gizmoMeshesOwner = null;

            if (Effects == null || Effects.Count == 0 || SelectedEffect < 0 || SelectedEffect >= Effects.Count) return;

            var Effect = Effects[SelectedEffect]; //Cache the selected Effect

            if (Effect == null || Effect.effect == null) return;

            gizmoMeshesOwner = Effect.effect;

            foreach (var filter in Effect.effect.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) gizmoMeshes.Add(filter);

            foreach (var skinned in Effect.effect.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skinned.sharedMesh != null) gizmoMeshes.Add(skinned);
        }

        /// <summary>World matrix the Effect root gets when it is played (mirrors the placement done in PlayEffect).</summary>
        private static Matrix4x4 GetEffectRootMatrix(Effect Effect)
        {
            var effectT = Effect.effect.transform;

            if (Effect.isChild)
            {
                //Parented to the Root, Offset restored as local position/rotation/scale
                if (Effect.root == null) return effectT.localToWorldMatrix;
                return Effect.root.localToWorldMatrix * Matrix4x4.TRS(Effect.Offset.Position, Quaternion.Euler(Effect.Offset.Rotation), Effect.Offset.Scale * Effect.scale);
            }

            bool isPrefab = Effect.effect.IsPrefab();

            //A scene effect that is not moved keeps its own world matrix
            if (!isPrefab && (Effect.root == null || (!Effect.useRootPosition && !Effect.useRootRotation)))
                return effectT.localToWorldMatrix;

            //Prefabs are instantiated at the prefab root pose (no parent), with the scale multiplier applied
            var position = isPrefab ? effectT.localPosition : effectT.position;
            var rotation = isPrefab ? effectT.localRotation : effectT.rotation;
            var scale = isPrefab ? effectT.localScale * Effect.scale : effectT.lossyScale;

            if (Effect.root != null)
            {
                if (Effect.useRootPosition) position = Effect.root.position;
                if (Effect.useRootRotation) rotation = Effect.root.rotation;
            }

            return Matrix4x4.TRS(position, rotation, scale);
        }

        private void OnDrawGizmosSelected()
        {
            if (!UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)) return;
            if (Selection.gameObjects.Length > 0 && Selection.gameObjects[0] != this.gameObject) return;
            if (Effects == null || Effects.Count == 0 || SelectedEffect < 0 || SelectedEffect >= Effects.Count) return; //If no effects or the selected effect is out of range

            var Effect = Effects[SelectedEffect]; //Cache the selected Effect

            if (Effect == null || Effect.effect == null) return;

            if (gizmoMeshesOwner != Effect.effect) FindSelectedEffectMesh(); //The Effect changed, rebuild the mesh cache

            var rootMatrix = GetEffectRootMatrix(Effect);
            var effectWorldToLocal = Effect.effect.transform.worldToLocalMatrix;

            Gizmos.color = debugColor;

            bool drawn = false;

            foreach (var meshComp in gizmoMeshes)
            {
                if (meshComp == null) continue;

                var mesh = meshComp is MeshFilter filter ? filter.sharedMesh : ((SkinnedMeshRenderer)meshComp).sharedMesh;
                if (mesh == null) continue;

                //Mesh pose relative to the Effect root, placed where the Effect root will be
                Gizmos.matrix = rootMatrix * effectWorldToLocal * meshComp.transform.localToWorldMatrix;
                Gizmos.DrawMesh(mesh);
                drawn = true;
            }

            if (!drawn)
            {
                Gizmos.matrix = rootMatrix;
                Gizmos.DrawSphere(Vector3.zero, 0.2f);
                Gizmos.DrawWireSphere(Vector3.zero, 0.2f);
            }

            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }

    [System.Serializable]
    public class Effect
    {
        public string Name = "EffectName";
        public int ID;
        public bool active = true;
        [RequiredField] public Transform root;

        public bool isChild;
        public bool disableOnStop = true;
        public bool useRootPosition = true;
        public bool useRootRotation = true;
        /// <summary>
        /// Prefab or GameObject to Instantiate
        /// </summary>
        public GameObject effect;
        public TransformOffset Offset = new(1);
        public AudioSource audioSource;
        public AudioClipReference Clip;

        /// <summary>Life of the Effect</summary>
        [Min(0)] public float life = 10f;

        /// <summary>Delay Time to execute the effect after is called.</summary>
        [Min(0)] public float delay;
        public float scale = 1f;

        ///// <summary>Scriptable Object to Modify anything you want before, during or after the effect is invoked</summary>
        //public EffectModifier Modifier;


        [SerializeReference]
        public Reaction OnPlayReaction;
        [SerializeReference]
        public Reaction OnStopReaction;

        public UnityEvent OnPlay;
        public UnityEvent OnStop;

        /// <summary>Returns the Owner of the Effect </summary>
        public Transform Owner { get; set; }

        /// <summary>  The Effect is playing. Use this to skip double playing the same effect /summary>
        public bool IsPlaying { get; set; }

        /// <summary>Returns the Instance of the Effect Prefab </summary>
        public GameObject Instance { get => instance; set => instance = value; }

        public TrailRenderer IsTrailRenderer { get; set; }
        public ParticleSystem IsParticleSystem { get; set; }

        [System.NonSerialized]
        private GameObject instance;

        internal void Initialize()
        {
            if (effect != null && !effect.IsPrefab()) //Store if the effect its not a prefab
            {
                effect.gameObject.SetActive(false); //Deactivate at start
                IsTrailRenderer = effect.FindComponent<TrailRenderer>();
                IsParticleSystem = effect.FindComponent<ParticleSystem>();
            }
        }
    }

    /// ---------------------------------------------------

    #region INSPECTOR
#if UNITY_EDITOR

    [CustomEditor(typeof(EffectManager))]
    public class EffectManagerEditor : Editor
    {
        private ReorderableList list;
        private SerializedProperty EffectList, Owner, SelectedEffect, DebugColor, debug, editOffset, Editor_Tabs1;
        private EffectManager M;

        /// <summary>Tabs drawn for the selected Effect.</summary>
        private static readonly string[] EffectTabs = new string[] { "General", "Reactions", "Events" };

        private static GUIContent editIcon;
        public static GUIContent EditIcon
        {
            get
            {
                editIcon ??= new GUIContent(EditorGUIUtility.IconContent("TransformTool"))
                {
                    tooltip = "Edit Offset in ViewPort"
                };
                return editIcon;
            }
        }


        private void OnEnable()
        {
            M = ((EffectManager)target);
            Owner = serializedObject.FindProperty("Owner");
            debug = serializedObject.FindProperty("debug");
            editOffset = serializedObject.FindProperty("editOffset");
            EffectList = serializedObject.FindProperty("Effects");
            SelectedEffect = serializedObject.FindProperty("SelectedEffect");
            DebugColor = serializedObject.FindProperty("debugColor");
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");

            list = new ReorderableList(serializedObject, EffectList, true, true, true, true)
            {
                drawElementCallback = DrawElementCallback,
                drawHeaderCallback = HeaderCallbackDelegate,
                onAddCallback = OnAddCallBack,
                onSelectCallback = (list) =>
                {
                    SelectedEffect.intValue = list.index;
                    serializedObject.ApplyModifiedProperties();
                    M.FindSelectedEffectMesh();
                }
            };

            list.index = SelectedEffect.intValue;
            M.FindSelectedEffectMesh();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Manage all the Effects using the function (PlayEffect(int ID))");

            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(Owner);
                editOffset.boolValue = GUILayout.Toggle(editOffset.boolValue, EditIcon, EditorStyles.miniButtonMid, GUILayout.Width(28), GUILayout.Height(20));
                EditorGUILayout.PropertyField(DebugColor, GUIContent.none, GUILayout.Width(50));
                MalbersEditor.DrawDebugIcon(debug);
            }


            list.DoLayoutList();

            if (list.index != -1 && list.index < list.count)
            {
                Effect effect = M.Effects[list.index];

                if (effect != null)
                {
                    SerializedProperty Element = EffectList.GetArrayElementAtIndex(list.index);

                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField($"[{effect.Name}]", EditorStyles.boldLabel);

                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, EffectTabs);

                            if (cc.changed) GUI.FocusControl(null); //Remove the focus from the previous tab controls
                        }

                        switch (Editor_Tabs1.intValue)
                        {
                            case 0: ShowGeneral(Element, effect); break;
                            case 1: ShowReactions(Element); break;
                            case 2: ShowEvents(Element); break;
                        }
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Draws the General tab of the selected Effect (Effect, Audio, Location and Orientation).</summary>
        private void ShowGeneral(SerializedProperty Element, Effect effect)
        {


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var eff = Element.FindPropertyRelative("effect");

                var is_Prefab = eff.objectReferenceValue != null && (eff.objectReferenceValue as GameObject).IsPrefab();

                if (Application.isPlaying && is_Prefab)
                {
                    using (new EditorGUI.DisabledGroupScope(true))
                        EditorGUILayout.ObjectField("Effect Instance", effect.Instance, typeof(GameObject), false);
                }
                var root = Element.FindPropertyRelative("root");
                root.isExpanded = MalbersEditor.Foldout(root.isExpanded, "Location & Orientation");

                if (root.isExpanded)
                {

                    EditorGUILayout.PropertyField(Element.FindPropertyRelative("scale"), new GUIContent("Scale", "Scale the Prefab object"));

                    if (Element.FindPropertyRelative("life").floatValue <= 0)
                    {
                        EditorGUILayout.HelpBox("Life = 0  the effect will not be destroyed by this Script", MessageType.Info);
                    }




                    EditorGUILayout.PropertyField(root, new GUIContent("Root", "Uses this transform to position the Effect"));


                    if (root.objectReferenceValue != null)
                    {
                        var isChild = Element.FindPropertyRelative("isChild");
                        var useRootPosition = Element.FindPropertyRelative("useRootPosition");
                        var useRootRotation = Element.FindPropertyRelative("useRootRotation");

                        using (new EditorGUI.DisabledGroupScope(isChild.boolValue))
                        {
                            EditorGUILayout.PropertyField(useRootPosition, new GUIContent("Use Root Position", "Set the Effect location as the root position."));
                            EditorGUILayout.PropertyField(useRootRotation, new GUIContent("Use Root Rotation", "Orient the Effect using the root rotation."));
                        }

                        EditorGUILayout.PropertyField(isChild, new GUIContent("Is Child", "Set the Effect as a child of the Root transform"));

                        if (isChild.boolValue)
                        {
                            var Offset = Element.FindPropertyRelative("Offset");
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(Offset, true);
                            EditorGUI.indentLevel--;
                        }

                    }
                }

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var audio = Element.FindPropertyRelative("audioSource");
                    audio.isExpanded = MalbersEditor.Foldout(audio.isExpanded, "Audio");

                    if (audio.isExpanded)
                    {
                        EditorGUILayout.PropertyField(audio,
                            new GUIContent("Source", "Where the audio for the Effect will be player"));
                        EditorGUILayout.PropertyField(Element.FindPropertyRelative("Clip"),
                           new GUIContent("Clip", "What audio will be played"));
                    }
                }
            }
        }

        /// <summary>Draws the Reactions tab of the selected Effect.</summary>
        private void ShowReactions(SerializedProperty Element)
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("OnPlayReaction"));
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("OnStopReaction"));
                EditorGUI.indentLevel--;
            }
        }

        /// <summary>Draws the Events tab of the selected Effect.</summary>
        private void ShowEvents(SerializedProperty Element)
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("OnPlay"));
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("OnStop"));
            }
        }


        private void OnSceneGUI()
        {
            if (!editOffset.boolValue) return;

            if (M.Effects != null)
            {
                var Effect = M.Effects[SelectedEffect.intValue];

                if (Effect != null)
                {
                    if (Tools.current == Tool.Move)
                    {
                        if (Effect.isChild)
                        {
                            using (var cc = new EditorGUI.ChangeCheckScope())
                            {
                                Vector3 piv = Effect.root.TransformPoint(Effect.Offset.Position);
                                Vector3 NewPivPosition = Handles.PositionHandle(piv, Quaternion.identity);

                                if (cc.changed)
                                {
                                    Undo.RecordObject(target, "Change Pos Offset");
                                    Effect.Offset.Position = Effect.root.InverseTransformPoint(NewPivPosition);
                                    EditorUtility.SetDirty(target);
                                }
                            }
                        }
                        else
                        {
                            using (var cc = new EditorGUI.ChangeCheckScope())
                            {
                                if (!Effect.effect.IsPrefab())
                                {
                                    Vector3 piv = Effect.effect.transform.position;
                                    Vector3 NewPivPosition = Handles.PositionHandle(piv, Quaternion.identity);

                                    if (cc.changed)
                                    {
                                        Undo.RecordObject(Effect.effect.transform, "Change Pos Offset");
                                        Effect.effect.transform.position = NewPivPosition;
                                        EditorUtility.SetDirty(Effect.effect.transform);
                                    }
                                }
                            }
                        }
                    }
                    else if (Tools.current == Tool.Rotate)
                    {
                        if (Effect.isChild)
                        {
                            using (var cc = new EditorGUI.ChangeCheckScope())
                            {
                                Vector3 piv = Effect.root.TransformPoint(Effect.Offset.Position);

                                var startRotation = Effect.root.rotation;

                                Quaternion NewPivRotation = Handles.RotationHandle(startRotation * Quaternion.Euler(Effect.Offset.Rotation), piv);

                                if (cc.changed)
                                {
                                    Undo.RecordObject(target, "Change Rot Offset");
                                    Effect.Offset.Rotation = (Quaternion.Inverse(startRotation) * NewPivRotation).eulerAngles;
                                    EditorUtility.SetDirty(target);
                                }
                            }
                        }
                        else
                        {

                            using (var cc = new EditorGUI.ChangeCheckScope())
                            {
                                if (!Effect.effect.IsPrefab())
                                {
                                    Vector3 piv = Effect.effect.transform.position;
                                    Quaternion NewPivRotation = Handles.RotationHandle(Effect.effect.transform.rotation, piv);

                                    if (cc.changed)
                                    {
                                        Undo.RecordObject(Effect.effect.transform, "Change Rot Offset");
                                        Effect.effect.transform.rotation = NewPivRotation;
                                        EditorUtility.SetDirty(Effect.effect.transform);
                                    }
                                }
                            }
                        }
                    }
                    else if (Tools.current == Tool.Scale)
                    {
                        if (Effect.isChild)
                        {
                            using (var cc = new EditorGUI.ChangeCheckScope())
                            {
                                Vector3 piv = Effect.root.TransformPoint(Effect.Offset.Position);
                                Vector3 NewPivScale = Handles.ScaleHandle(Effect.Offset.Scale, piv, Effect.root.rotation, HandleUtility.GetHandleSize(piv));

                                if (cc.changed)
                                {
                                    Undo.RecordObject(target, "Change Scale Offset");
                                    Effect.Offset.Scale = NewPivScale;
                                    EditorUtility.SetDirty(target);
                                }
                            }
                        }
                        else
                        {
                            using (var cc = new EditorGUI.ChangeCheckScope())
                            {
                                if (!Effect.effect.IsPrefab())
                                {
                                    Vector3 piv = Effect.effect.transform.position;
                                    Vector3 NewPivScale = Handles.ScaleHandle(Effect.effect.transform.localScale, piv, Effect.effect.transform.rotation, HandleUtility.GetHandleSize(piv));

                                    if (cc.changed)
                                    {
                                        Undo.RecordObject(Effect.effect.transform, "Change Scale Offset");
                                        Effect.effect.transform.localScale = NewPivScale;
                                        EditorUtility.SetDirty(Effect.effect.transform);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private const float DragHandle = 20f;    //The List elements are indented by the drag handle, the Header is not
        private const float ID_Width = 32f;
        private const float Life_Width = 38f;
        private const float Delay_Width = 38f;
        private const float Disable_Width = 26f;
        private const float Gap = 2f;

        private static GUIContent C_Active, C_ID, C_Name, C_Effect, C_Life, C_Delay, C_Disable;

        /// <summary>Creates the Column labels and their tooltips.</summary>
        private static void FindColumnLabels()
        {
            C_Active ??= new GUIContent(string.Empty, "Enable/Disable the Effect. Disabled Effects are ignored by Play and Stop");
            C_ID ??= new GUIContent("ID", "ID of the Effect. PlayEffect(int ID) plays all the Effects sharing this ID");
            C_Name ??= new GUIContent("Name", "Name of the Effect. PlayEffect(string name) plays all the Effects sharing this Name");
            C_Effect ??= new GUIContent("Effect", "Prefab or GameObject which holds the Effect (Particles, Transforms). Prefabs are instantiated");
            C_Life ??= new GUIContent("Life", "Duration of the Effect. The Effect will be stopped after the Life time has passed. Life = 0 means it will not be stopped by this script");
            C_Delay ??= new GUIContent("Delay", "Time to wait before playing the Effect");
            C_Disable ??= new GUIContent("Dis", "Disable On Stop. When the Effect is not a Prefab, its GameObject will be disabled when the Effect stops");
        }

        /// <summary>Splits a list row into the [Active][ID][Name][Effect][Life][Delay][Disable On Stop] columns.</summary>
        private static void ColumnRects(Rect rect, out Rect active, out Rect ID, out Rect name,
            out Rect effect, out Rect life, out Rect delay, out Rect disable)
        {
            var height = EditorGUIUtility.singleLineHeight;

            active = new Rect(rect.x, rect.y, 15, height);

            var x = rect.x + 15 + Gap;
            var free = rect.width - 15 - ID_Width - Life_Width - Delay_Width - Disable_Width - (Gap * 6);

            var nameWidth = Mathf.Max(40, free * 0.45f);
            var effectWidth = Mathf.Max(40, free - nameWidth);

            ID = new Rect(x, rect.y, ID_Width, height);
            x += ID_Width + Gap;

            name = new Rect(x, rect.y, nameWidth, height);
            x += nameWidth + Gap;

            effect = new Rect(x, rect.y, effectWidth, height);
            x += effectWidth + Gap;

            life = new Rect(x, rect.y, Life_Width, height);
            x += Life_Width + Gap;

            delay = new Rect(x, rect.y, Delay_Width, height);
            x += Delay_Width + Gap;

            disable = new Rect(x, rect.y, Disable_Width, height);
        }

        void HeaderCallbackDelegate(Rect rect)
        {
            FindColumnLabels();

            rect.xMin += DragHandle;

            ColumnRects(rect, out Rect R_Active, out Rect R_ID, out Rect R_Name,
                out Rect R_Effect, out Rect R_Life, out Rect R_Delay, out Rect R_Disable);

            EditorGUI.LabelField(R_Active, C_Active, EditorStyles.centeredGreyMiniLabel);
            EditorGUI.LabelField(R_ID, C_ID, EditorStyles.centeredGreyMiniLabel);
            EditorGUI.LabelField(R_Name, C_Name, EditorStyles.miniLabel);
            EditorGUI.LabelField(R_Effect, C_Effect, EditorStyles.centeredGreyMiniLabel);
            EditorGUI.LabelField(R_Life, C_Life, EditorStyles.centeredGreyMiniLabel);
            EditorGUI.LabelField(R_Delay, C_Delay, EditorStyles.centeredGreyMiniLabel);
            EditorGUI.LabelField(R_Disable, C_Disable, EditorStyles.centeredGreyMiniLabel);
        }

        void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = EffectList.GetArrayElementAtIndex(index);

            var e_active = element.FindPropertyRelative("active");
            var e_Name = element.FindPropertyRelative("Name");
            var e_ID = element.FindPropertyRelative("ID");
            var e_effect = element.FindPropertyRelative("effect");
            var e_life = element.FindPropertyRelative("life");
            var e_delay = element.FindPropertyRelative("delay");
            var e_disableOnStop = element.FindPropertyRelative("disableOnStop");

            rect.y += 2;

            ColumnRects(rect, out Rect R_Active, out Rect R_ID, out Rect R_Name,
                out Rect R_Effect, out Rect R_Life, out Rect R_Delay, out Rect R_Disable);

            e_active.boolValue = EditorGUI.Toggle(R_Active, e_active.boolValue);
            e_ID.intValue = EditorGUI.IntField(R_ID, e_ID.intValue);
            e_Name.stringValue = EditorGUI.TextField(R_Name, e_Name.stringValue, EditorStyles.label);
            EditorGUI.PropertyField(R_Effect, e_effect, GUIContent.none);
            EditorGUI.PropertyField(R_Life, e_life, GUIContent.none);
            EditorGUI.PropertyField(R_Delay, e_delay, GUIContent.none);

            //Disable On Stop only applies to Effects that are not Prefabs
            var is_Prefab = e_effect.objectReferenceValue == null || (e_effect.objectReferenceValue as GameObject).IsPrefab();

            using (new EditorGUI.DisabledGroupScope(is_Prefab))
            {
                R_Disable.x += (R_Disable.width - 15) / 2; //Center the toggle on its column
                R_Disable.width = 15;
                e_disableOnStop.boolValue = EditorGUI.Toggle(R_Disable, e_disableOnStop.boolValue);
            }
        }

        void OnAddCallBack(ReorderableList list)
        {
            M.Effects ??= new System.Collections.Generic.List<Effect>();
            M.Effects.Add(new Effect());
        }
    }
#endif
    #endregion
}