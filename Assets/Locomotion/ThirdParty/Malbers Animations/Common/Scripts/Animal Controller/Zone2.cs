using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Reactions;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Controller
{
    /// <summary>
    /// MWC: New reactions-only Zone. A lean sibling of <see cref="Zone"/> that does a single job:
    /// fire Reactions on Enter / Activate / Exit. Mode, State and Stance "zones" are achieved by
    /// composing the matching reactions (ModeReaction, StateReaction, StanceReaction, ZoneForceReaction…) on the
    /// Activation/Exit reaction lists, so this component carries no zoneType switch or per-type fields.
    /// The activation gates of the old Zone (Limit, Probability, Angle, Bone Only) are kept so every Zone can be migrated.
    /// </summary>
    [AddComponentMenu("Malbers/Animal Controller/Zone2 (Reaction Zone)")]
    [SelectionBase]
    public class Zone2 : MonoBehaviour, IZone
    {
        public bool debug;

        [Tooltip("As soon as the animal enters the zone it will activate the zone (fire the Activation Reaction). If False then call the method Zone2.Activate()")]
        public BoolReference automatic = new(true);
        [Tooltip("ID of the Zone. Use it to identify the Zone on the Animal")]
        public IntReference zoneID = new(-1);

        [Tooltip("How many characters can use this zone at the same time.\nNegative values: The zone has no character limit")]
        public IntReference Limit = new(-1);

        [Tooltip("Disable the Zone after it was used")]
        public BoolReference DisableAfterUsed = new();

        [Tooltip("Delay the Disable of the Zone after it was used")]
        public FloatReference DisableDelay = new(0);

        [Tooltip("Remove the Animal from the Zone as soon as the Zone is activated. The Animal will need to exit and enter again to use it")]
        public bool RemoveAnimalOnActive = false;

        [Range(0, 1), Tooltip("Probability to Activate the Zone")]
        public float Weight = 1;

        [Range(0, 360), Tooltip("Limit the Activation of the Zone of an angle from the Animal. Angle will be ignored if is 0 or 360")]
        public float Angle = 360;
        [Tooltip("The Zone can be used in both sides")]
        public bool DoubleSide = false;
        [Tooltip("Flip the Angle")]
        public bool Flip = false;

        [Tooltip("Check only colliders with this name")]
        public bool BoneOnly;
        public string BoneName = "Head";

        [Tooltip("Layer to detect the Animal")]
        public LayerReference Layer = new(1048576); //Animal Layer

        [SerializeField, HideInInspector] private List<Tag> tags; //Old tag list. Upgraded to m_Tags on Validate
        public MTags m_Tags;
        public bool tagUpdated = false;

        [Tooltip("Collider for the Zone. If is not set, it will find the first collider attached to this gameobject")]
        [RequiredField] public Collider ZoneCollider;
        public Collider ZCollider => ZoneCollider;

        [Tooltip("Mode that activates this Zone by Input. When the Animal is inside the zone and the Input of this Mode is pressed, the Zone will be activated instead of the Mode.\n" +
            "Leave it empty if the Zone does not depend on a Mode Input")]
        public ModeID InputMode;
        [Tooltip("Ability of the Input Mode used by this Zone. (-99 means any ability)")]
        public IntReference InputAbility = new(-99);
        [Tooltip("When entering a non automatic Zone, the 'Active Ability Index' of the Input Mode will be changed to the Input Ability of the Zone")]
        public bool PrepareModeZone = true;

        [Tooltip("Reaction invoked on the entering Animal as soon as it enters the trigger")]
        public Reaction2 EnterReaction;
        [Tooltip("Reaction invoked on the Animal when it fully exits the trigger. Use it to reverse what the Activation Reaction did (e.g. exit a State, reset a Stance)")]
        public Reaction2 ExitReaction;
        [Tooltip("Reaction invoked on the Animal when the Zone activates. This is where the Mode/State/Stance is applied.\n" +
            "The Zone is activated only if all the reactions succeed (the first failing reaction stops the rest)")]
        public Reaction2 ActivationReaction;
        [Tooltip("Reaction invoked on the Animal when the Zone fails to activate (Limit reached or the Activation Reaction failed).\n" +
            "Automatic zones invoke it once per attempt, not on every retry frame")]
        public Reaction2 FailedReaction;

        [Tooltip("Global conditions that must be true for the zone to activate. Dynamic Target = the entering Animal")]
        public Conditions.Conditions2 conditions; //MWC: gate activation with reusable Conditions2

        public AnimalEvent OnEnter = new();
        public AnimalEvent OnExit = new();
        public AnimalEvent OnZoneActivation = new();
        public AnimalEvent OnZoneFailed = new();

        /// <summary>Currents Animals inside the zone</summary>
        public HashSet<MAnimal> AnimalsInZone { get; internal set; }

        /// <summary>Currents Animals that have activated the zone</summary>
        public HashSet<MAnimal> AnimalsUsingZone { get; internal set; }

        public MAnimal JustExitAnimal;

        /// <summary>List of all colliders entering the Zone</summary>
        internal HashSet<Collider> m_Colliders = new();

        /// <summary>Keep a Track of all the Reaction Zones on the Scene</summary>
        public static List<Zone2> Zones;

        /// <summary>Tags the entering collider needs (MWC: reads m_Tags, the list shown in the Inspector, like Zone does)</summary>
        public List<Tag> Tags { get => m_Tags; set => m_Tags = new MTags(value); }

        /// <summary>A Zone2 is a Mode Zone when it has an Input Mode. Mode.cs will activate the zone when the Mode Input is pressed inside it</summary>
        public bool IsMode => InputMode != null;
        /// <summary>The Zone is a State Zone when its first Activation Reaction is a State Reaction</summary>
        public bool IsState => FirstActivationReaction is StateReaction;
        /// <summary>The Zone is a Stance Zone when its first Activation Reaction is a Stance Reaction</summary>
        public bool IsStance => FirstActivationReaction is StanceReaction;

        /// <summary>ID of the zone. On a Mode Zone it is the ID of the Input Mode (Required by Mode.cs)</summary>
        public int ZoneID => InputMode != null ? InputMode.ID : zoneID.Value;
        public int Index => zoneID;

        /// <summary>Ability Index of the Input Mode</summary>
        public int ModeAbilityIndex => InputAbility.Value;

        /// <summary>Zone Type deduced from the Input Mode and the first Activation Reaction (Used by the AI Look Decision)</summary>
        public ZoneType Type
        {
            get
            {
                if (IsMode) return ZoneType.Mode;
                return FirstActivationReaction switch
                {
                    ModeReaction => ZoneType.Mode,
                    StateReaction => ZoneType.State,
                    StanceReaction => ZoneType.Stance,
                    ZoneForceReaction => ZoneType.Force,
                    _ => ZoneType.ReactionsOnly,
                };
            }
        }

        private Reaction FirstActivationReaction => ActivationReaction.IsValid ? ActivationReaction.reactions[0] : null;

        private void Awake()
        {
            Zones ??= new List<Zone2>();
        }

        private void OnEnable()
        {
            if (ZoneCollider == null)
                ZoneCollider = GetComponent<Collider>();    //Get the reference for the collider

            if (ZoneCollider)
            {
                ZoneCollider.isTrigger = true;              //Force Trigger
                ZoneCollider.enabled = true;
            }

            Zones.Add(this);                                //Save the Reaction Zone on the global list

            AnimalsInZone = new();
            AnimalsUsingZone = new();
        }

        private void OnDisable()
        {
            var animals = new List<MAnimal>(AnimalsInZone);

            foreach (var animal in animals)
            {
                ResetStoredAnimal(animal);
                OnExit.Invoke(animal);
                ReactAll(ExitReaction, animal);
            }

            if (ZoneCollider)
                ZoneCollider.enabled = false;

            AnimalsInZone = new();      //Clear the Animals in Zone
            AnimalsUsingZone = new();   //Clear the Animals using the Zone
            m_Colliders = new();        //Clear the Colliders
            JustExitAnimal = null;

            Zones.Remove(this);         //Remove the Reaction Zone from the global list
        }

        private void OnValidate()
        {
            if (!tagUpdated && m_Tags != null)
            {
                m_Tags = new MTags(tags);   //Upgrade the old serialized tag list
                tagUpdated = true;
                MTools.SetDirty(this);
            }
        }

        public bool TrueConditions(Collider other)
        {
            if (!enabled) return false;

            if (Tags != null && Tags.Count > 0)
            {
                if (!other.gameObject.HasMalbersTagInParent(Tags.ToArray())) return false;
            }

            if (ZoneCollider == null) return false;
            if (other == null) return false;                                        //You are CALLING AN ELIMINATED ONE

            if (BoneOnly && !other.name.ToLower().Contains(BoneName.ToLower())) return false;  //If is Bone Only and no bone was found Skip
            if (!MTools.Layer_in_LayerMask(other.gameObject.layer, Layer)) return false;
            if (transform.IsChildOf(other.transform)) return false;                 //Do not Interact with yourself

            return true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (TrueConditions(other))
            {
                MAnimal animal = other.FindComponent<MAnimal>();             //Get the animal on the entering collider

                if (!animal || animal.Sleep || !animal.enabled) return;      //If there's no animal, or is Sleep or disabled do nothing
                if (animal.RB.isKinematic) return;                           //Do not Activate while the animal is kinematic
                if (automatic && animal == JustExitAnimal) return;           //Do not activate the animal that just exit

                if (!m_Colliders.Contains(other))
                    m_Colliders.Add(other);            //if the entering collider is not already on the list add it
                else return;                           //The Collider was already there

                //if the animal is already on the list do nothing
                if (AnimalsInZone.Contains(animal)) return;

                //If the Animal is on another Zone Remove it from the other Zone
                if (animal.InZone && animal.Zone != (IZone)this)
                    animal.Zone.RemoveAnimal(animal);

                animal.Zone = this;                    //Let the animal know it is on a zone
                AnimalsInZone.Add(animal);             //Set a new Animal
                OnEnter.Invoke(animal);
                ReactAll(EnterReaction, animal);

                Debugging($"[Enter Animal] -> [{animal.name}]", "yellow");

                if (automatic)
                {
                    //Keep trying while the animal is inside the zone (Conditions, Angle or the Reaction may be valid later)
                    if (!ActivateZone(animal) && CanEverActivate(animal))
                        StartCoroutine(KeepTryingAutomatic(animal));
                }
                else
                {
                    PrepareZone(animal);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            //Exit all colliders of the animal even if the true conditions are not met (e.g. the Layer changed while inside the zone)
            MAnimal animal = other.GetComponentInParent<MAnimal>();
            if (animal == null) return;

            if (m_Colliders != null && m_Colliders.Contains(other))
                m_Colliders.Remove(other);                               //Remove the collider that is exiting the zone

            CheckMissingColliders();

            if (AnimalsInZone.Contains(animal))         //Means the Entering animal still exists on the zone
            {
                bool NoAnimalColliders = true;

                foreach (var col in m_Colliders)
                {
                    if (col.transform.SameHierarchy(animal.transform)) //Check if a Collider is still on the Animal
                    {
                        NoAnimalColliders = false;
                        break;
                    }
                }
                if (NoAnimalColliders) RemoveAnimal(animal);
            }
        }

        private IEnumerator KeepTryingAutomatic(MAnimal animal)
        {
            yield return null;

            while (animal != null && enabled && AnimalsInZone.Contains(animal) && animal.Zone == (IZone)this)
            {
                silentFail = true;          //Retries do not repeat the Failed Reaction / OnZoneFailed every frame
                var activated = ActivateZone(animal);
                silentFail = false;

                if (activated) yield break;
                yield return null;
            }
        }

        /// <summary>True while the automatic retry loop is trying to activate the zone</summary>
        private bool silentFail;

        /// <summary>Invokes OnZoneFailed and the Failed Reaction (skipped on automatic retries)</summary>
        protected void ZoneFailed(MAnimal animal)
        {
            if (silentFail || animal == null) return;

            Debugging($"[Zone Failed] <b>[{animal.name}]</b>", "red");
            OnZoneFailed.Invoke(animal);
            ReactAll(FailedReaction, animal);
        }

        /// <summary>False when the Animal does not have the Input Mode of this Zone, so there is no reason to keep trying</summary>
        private bool CanEverActivate(MAnimal animal) => InputMode == null || animal.Mode_Get(InputMode) != null;

        /// <summary>Check the Zone gates (Probability, Limit, Conditions and Angle)</summary>
        public virtual bool CanActivateZone(MAnimal animal)
        {
            if (animal == null) return false;

            //Mode Zone: an Animal without the Input Mode can never use it (no retries, no "Mode Reaction Failed" logs)
            if (InputMode != null && animal.Mode_Get(InputMode) == null) return false;

            if (Weight < 1 && Random.Range(0f, 1f) >= Weight)
            {
                Debugging($"[Zone Failed to activate] -> Probability <b>[{Weight:F2}]</b>");
                return false;
            }

            if (Limit > 0 && !AnimalsUsingZone.Contains(animal) && AnimalsUsingZone.Count >= Limit)
            {
                Debugging($"[Zone Failed to activate] -> Limit <b>[{Limit.Value}]</b>");
                ZoneFailed(animal);
                return false;
            }

            //MWC: Global conditions gate the activation (ignored when no conditions are set)
            if (conditions.Valid && !conditions.Evaluate(animal)) return false;

            if (!CheckAngle(animal)) return false;

            return true;
        }

        /// <summary>Activate the Zone on a given Animal: fires the Activation Reaction once.</summary>
        public virtual bool ActivateZone(MAnimal animal)
        {
            if (!CanActivateZone(animal)) return false;

            animal.Zone = this;             //Let the animal know it is on a zone

            //MWC: fire the Activation Reaction exactly once (fixes the Zone.ReactionsOnly double-fire).
            //If there's no Activation Reaction the zone still counts as activated so events still run.
            var ok = !ActivationReaction.IsValid || ActivationReaction.React(animal);

            if (ok)
            {
                AnimalsUsingZone.Add(animal);   //Count the animal only when the zone truly activates (Limit)
                Debugging($"[Zone Activate] <b>[{animal.name}]</b>");
                OnZoneActive(animal);
                return true;
            }

            ZoneFailed(animal);
            return false;
        }

        /// <summary>Activate the Zone for every Animal currently inside it (UnityEvent friendly).</summary>
        public virtual void ActivateZone()
        {
            //Copy to avoid mutation while iterating (DisableAfterUsed / ResetStoredAnimal may modify the set)
            var animals = new List<MAnimal>(AnimalsInZone);
            foreach (var animal in animals)
                ActivateZone(animal);
        }

        /// <summary> Check if the Animal is in the right angle to activate the zone </summary>
        protected bool CheckAngle(MAnimal animal)
        {
            if (Angle == 360 || Angle == 0) return true;

            var flip = Flip ? 1 : -1;

            var EntrySideAngle = Vector3.Angle(transform.forward * flip, animal.Forward) * 2;
            var OtherSideAngle = EntrySideAngle;

            if (DoubleSide) OtherSideAngle = Vector3.Angle(-transform.forward * flip, animal.Forward) * 2;

            var side = Vector3.Dot((animal.transform.position - transform.position).normalized, transform.forward) * -1; //Calculate the correct side
            return (EntrySideAngle < Angle && side < 0) || (OtherSideAngle < Angle && side > 0);
        }

        /// <summary>Non automatic zones: set the Ability of the Input Mode so the Animal plays the correct one when the Input is pressed</summary>
        protected virtual void PrepareZone(MAnimal animal)
        {
            if (InputMode != null && PrepareModeZone)
                animal.Mode_Get(InputMode)?.SetAbilityIndex(ModeAbilityIndex);
        }

        /// <summary>Activation bookkeeping. MWC: does NOT re-fire the Activation Reaction (already fired in ActivateZone).</summary>
        internal void OnZoneActive(MAnimal animal)
        {
            OnZoneActivation.Invoke(animal);

            if (RemoveAnimalOnActive)
            {
                ResetStoredAnimal(animal);
                AnimalsInZone.Remove(animal);
                AnimalsUsingZone.Remove(animal);
            }

            if (DisableAfterUsed.Value)
            {
                if (DisableDelay.Value > 0)
                    this.Delay_Action(DisableDelay.Value, () => enabled = false);
                else
                    enabled = false;
            }
        }

        public virtual void RemoveAnimal(MAnimal animal)
        {
            OnExit.Invoke(animal);              //Invoke On Exit when all the animal's colliders have exited the Zone
            ReactAll(ExitReaction, animal);     //React Exit: this is where a Mode/State/Stance gets reversed

            ResetStoredAnimal(animal);

            AnimalsInZone.Remove(animal);
            AnimalsUsingZone.Remove(animal);

            Debugging($"[Exit Animal] -> [{animal.name}]", "yellow");

            if (automatic)
            {
                JustExitAnimal = animal;
                this.Delay_Action(() => JustExitAnimal = null);
            }
        }

        private void CheckMissingColliders()
        {
            m_Colliders.RemoveWhere(x => x == null || x.gameObject == null || !x.enabled || !x.gameObject.scene.IsValid());
        }

        /// <summary>Enter and Exit reactions are notifications: run every reaction even if one of them fails</summary>
        private static void ReactAll(Reaction2 reaction, MAnimal animal)
        {
            if (!reaction.IsValid || animal == null) return;

            foreach (var r in reaction.reactions)
            {
                if (r == null) continue;
                if (r.useLocal && r.localTarget == null) continue;
                r.TryReact(animal);
            }
        }

        /// <summary>AI / Waypoint arrival hook: activate the zone on the arriving Animal.</summary>
        public void TargetArrived(GameObject go)
        {
            var animal = go.FindComponent<MAnimal>();
            ActivateZone(animal);
        }

        public void ResetAllAnimals()
        {
            var animals = new List<MAnimal>(AnimalsInZone);
            foreach (var animal in animals)
                ResetStoredAnimal(animal);
        }

        public virtual void ResetStoredAnimal(Component animalC)
        {
            var animal = animalC.FindComponent<MAnimal>();
            ResetStoredAnimal(animal);
        }

        public virtual void ResetStoredAnimal(MAnimal animal)
        {
            if (animal == null) return;

            if (animal.Zone != null && animal.Zone == (IZone)this)
                animal.Zone = null;     //Tell the Animal it is no longer on a Zone

            if (InputMode != null)
            {
                var mode = animal.Mode_Get(InputMode);

                //Only reset when it has the same Index... works for zones near each other
                if (mode != null && mode.AbilityIndex == ModeAbilityIndex) mode.ResetAbilityIndex();
            }

            //Remove all the colliders from the animal in case some are still there
            foreach (var item in animal.colliders)
            {
                if (item != null && m_Colliders.Contains(item))
                    m_Colliders.Remove(item);
            }

            if (animal.MainCollider != null && m_Colliders.Contains(animal.MainCollider))
                m_Colliders.Remove(animal.MainCollider);

            AnimalsInZone.Remove(animal);
            AnimalsUsingZone.Remove(animal);
        }

        public void Debugging(string value, string color = "green")
        {
#if UNITY_EDITOR
            if (debug)
                MDebug.Log($"<B>[{name}]</B> → <color={color}><B>{value}</B></color>", this);
#endif
        }

        [HideInInspector] public int Editor_Tabs1 = 0;

#if UNITY_EDITOR
        [ContextMenu("Add On Focused Prefab")]
        private void AddFocusedPrefab()
        {
            MTools.AddOnFocusedPrefab(transform, OnEnter, OnExit);
        }

        [ContextMenu("Connect to Align")]
        private void TryAlign()
        {
            var method = this.GetUnityAction<MAnimal>("Aligner", "Align");
            if (method != null) UnityEditor.Events.UnityEventTools.AddPersistentListener(OnZoneActivation, method);
            MTools.SetDirty(this);
        }

        private void Reset()
        {
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast"); //MWC: keep the zone trigger out of raycasts

            if (ZoneCollider == null)
                ZoneCollider = GetComponent<Collider>();

            if (GetComponent<Collider>() == null)
            {
                Debug.LogWarning("There's no Collider on the Zone, Adding a BoxCollider", this);
                ZoneCollider = gameObject.AddComponent<BoxCollider>();
            }

            if (ZoneCollider)
            {
                ZoneCollider.isTrigger = true; //MWC: force the collider to be a trigger
                ZoneCollider.enabled = true;
            }
        }

        private void OnDrawGizmos()
        {
            if (!UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)) return;

            //MWC: draw the Global conditions gizmos (matches Zone)
            conditions.Gizmos(this);

            //Draw the Angle check for every animal inside the zone
            if (debug && Application.isPlaying && AnimalsInZone != null)
            {
                foreach (var animal in AnimalsInZone)
                {
                    if (animal == null) continue;
                    var DColor = CheckAngle(animal) ? Color.green : Color.red;
                    MDebug.Draw_Arrow(animal.transform.position + Vector3.up * 0.05f, animal.Forward, DColor);
                }
            }
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(Zone2)), CanEditMultipleObjects]
    public class Zone2Editor : Editor
    {
        private Zone2 m;
        protected string[] Tabs1 = new string[] { "General", "Events" };

        private SerializedProperty
            EnterReaction, ExitReaction, ActivationReaction, FailedReaction, conditions,
            automatic, DisableAfterUsed, DisableDelay, debug, Editor_Tabs1, zoneID,
            Limit, Weight, Angle, DoubleSide, Flip, BoneOnly, BoneName, RemoveAnimalOnActive,
            InputMode, InputAbility, PrepareModeZone,
            OnZoneActivation, OnExit, OnEnter, OnZoneFailed, layer, m_tag, ZoneCollider;

        protected virtual void OnEnable()
        {
            m = (Zone2)target;

            EnterReaction = serializedObject.FindProperty(nameof(Zone2.EnterReaction));
            zoneID = serializedObject.FindProperty(nameof(Zone2.zoneID));
            ExitReaction = serializedObject.FindProperty(nameof(Zone2.ExitReaction));
            FailedReaction = serializedObject.FindProperty(nameof(Zone2.FailedReaction));
            ActivationReaction = serializedObject.FindProperty(nameof(Zone2.ActivationReaction));
            conditions = serializedObject.FindProperty(nameof(Zone2.conditions));

            automatic = serializedObject.FindProperty(nameof(Zone2.automatic));
            DisableAfterUsed = serializedObject.FindProperty(nameof(Zone2.DisableAfterUsed));
            DisableDelay = serializedObject.FindProperty(nameof(Zone2.DisableDelay));
            layer = serializedObject.FindProperty(nameof(Zone2.Layer));
            m_tag = serializedObject.FindProperty(nameof(Zone2.m_Tags));
            debug = serializedObject.FindProperty(nameof(Zone2.debug));
            ZoneCollider = serializedObject.FindProperty(nameof(Zone2.ZoneCollider));
            Editor_Tabs1 = serializedObject.FindProperty(nameof(Zone2.Editor_Tabs1));

            Limit = serializedObject.FindProperty(nameof(Zone2.Limit));
            Weight = serializedObject.FindProperty(nameof(Zone2.Weight));
            Angle = serializedObject.FindProperty(nameof(Zone2.Angle));
            DoubleSide = serializedObject.FindProperty(nameof(Zone2.DoubleSide));
            Flip = serializedObject.FindProperty(nameof(Zone2.Flip));
            BoneOnly = serializedObject.FindProperty(nameof(Zone2.BoneOnly));
            BoneName = serializedObject.FindProperty(nameof(Zone2.BoneName));
            RemoveAnimalOnActive = serializedObject.FindProperty(nameof(Zone2.RemoveAnimalOnActive));

            InputMode = serializedObject.FindProperty(nameof(Zone2.InputMode));
            InputAbility = serializedObject.FindProperty(nameof(Zone2.InputAbility));
            PrepareModeZone = serializedObject.FindProperty(nameof(Zone2.PrepareModeZone));

            OnEnter = serializedObject.FindProperty(nameof(Zone2.OnEnter));
            OnExit = serializedObject.FindProperty(nameof(Zone2.OnExit));
            OnZoneActivation = serializedObject.FindProperty(nameof(Zone2.OnZoneActivation));
            OnZoneFailed = serializedObject.FindProperty(nameof(Zone2.OnZoneFailed));

            if (ZoneCollider.objectReferenceValue == null)
            {
                ZoneCollider.objectReferenceValue = m.GetComponent<Collider>();
                serializedObject.ApplyModifiedProperties();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Zone to invoke any Reactions");

            using (new GUILayout.HorizontalScope())
            {
                Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs1);
                MalbersEditor.DrawDebugIcon(debug);
            }

            EditorGUI.BeginChangeCheck();

            switch (Editor_Tabs1.intValue)
            {
                case 0: DrawGeneral(); break;
                case 1: DrawEvents(); break;
                default: break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Zone2 Changed");
                EditorUtility.SetDirty(target);
            }

            if (Application.isPlaying && debug.boolValue && m.AnimalsInZone != null)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);

                    EditorGUILayout.LabelField("Animals in Zone (" + m.AnimalsInZone.Count + ")", EditorStyles.boldLabel);
                    foreach (var item in m.AnimalsInZone)
                        EditorGUILayout.ObjectField(item.name, item, typeof(MAnimal), false);

                    EditorGUILayout.LabelField("Animals Using Zone (" + m.AnimalsUsingZone.Count + ")", EditorStyles.boldLabel);
                    foreach (var item in m.AnimalsUsingZone)
                        EditorGUILayout.ObjectField(item.name, item, typeof(MAnimal), false);

                    EditorGUILayout.LabelField("Colliders in Zone (" + m.m_Colliders.Count + ")", EditorStyles.boldLabel);
                    foreach (var item in m.m_Colliders)
                        EditorGUILayout.ObjectField(item.name, item, typeof(Collider), false);

                    Repaint();
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(automatic, new GUIContent("Automatic"));
                EditorGUILayout.PropertyField(ZoneCollider, new GUIContent("Trigger"));
                EditorGUILayout.PropertyField(zoneID);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(InputMode);
                if (InputMode.objectReferenceValue != null)
                {
                    EditorGUILayout.PropertyField(InputAbility);
                    EditorGUILayout.PropertyField(PrepareModeZone);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(m_tag,
                    new GUIContent("Tags", "Set this parameter if you want the zone to Interact only with gameObjects with that tag"));
                EditorGUILayout.PropertyField(layer);

                EditorGUILayout.PropertyField(BoneOnly);
                if (BoneOnly.boolValue)
                    EditorGUILayout.PropertyField(BoneName);

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(conditions, new GUIContent("Global Conditions"));
                EditorGUI.indentLevel--;
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(Limit);
                EditorGUILayout.PropertyField(Weight, new GUIContent("Probability"));
                EditorGUILayout.PropertyField(Angle);

                if (Angle.floatValue > 0 && Angle.floatValue < 360)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(DoubleSide);
                        EditorGUILayout.PropertyField(Flip);
                    }
                }

                EditorGUILayout.PropertyField(DisableAfterUsed);
                if (m.DisableAfterUsed.Value)
                    EditorGUILayout.PropertyField(DisableDelay);

                EditorGUILayout.PropertyField(RemoveAnimalOnActive);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(ActivationReaction);
                EditorGUILayout.PropertyField(EnterReaction);
                EditorGUILayout.PropertyField(ExitReaction);
                EditorGUILayout.PropertyField(FailedReaction);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(OnEnter, new GUIContent("On Animal Enter Zone"));
                EditorGUILayout.PropertyField(OnExit, new GUIContent("On Animal Exit Zone"));
                EditorGUILayout.PropertyField(OnZoneActivation, new GUIContent("On Zone Active"));
                EditorGUILayout.PropertyField(OnZoneFailed, new GUIContent("On Zone Failed"));
            }
        }

        private void OnSceneGUI()
        {
            if (m.Angle <= 0 || m.Angle >= 360) return;

            //Draw the facing directions that activate the zone (see CheckAngle: the angle between directions is doubled)
            var t = m.transform;
            var dir = t.forward * (m.Flip ? 1 : -1);
            var half = m.Angle / 2f;           //Valid when 2 * angle < Angle
            var pos = t.position;

            Handles.color = new Color(0, 1, 0, 0.15f);
            Handles.DrawSolidArc(pos, t.up, Quaternion.AngleAxis(-half, t.up) * dir, half * 2, 1f);

            if (m.DoubleSide)
            {
                Handles.color = new Color(0, 0.6f, 1, 0.15f);
                Handles.DrawSolidArc(pos, t.up, Quaternion.AngleAxis(-half, t.up) * -dir, half * 2, 1f);
            }
        }
    }
#endif
}
