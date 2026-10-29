// Last Change: 10/16/2026 4:28pm
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Collections;
using MalbersAnimations.Scriptables;


#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/secondary-components/ik-manager")]
    [AddComponentMenu("Malbers/IK/IK Manager"), DisallowMultipleComponent]
    [DefaultExecutionOrder(1500)]
    [Unity.Cinemachine.SaveDuringPlay]
    public class IKManager : MonoBehaviour, IIKSource
    {
        [RequiredField] public Animator animator;

        [Range(0f, 1f), Tooltip("Global weight for the All IK Profiles")]
        public float Weight = 1;

        /// <summary>Multiplier applied to every position offset of all the IK Processors, to match the character bone scale</summary>
        [Min(0.0001f), Tooltip("Multiplies every position offset of all the IK Processors (Goal, Hint, Body, Sole... offsets) to match the scale of the character bones.\nRotation offsets are not affected")]
        public float ScaleMultiplier = 1;

        public List<IKSet> sets = new();
        /// <summary>Animator parameter hashes available on this rig, used to validate IK Set parameter bindings</summary>
        public HashSet<int> animatorHashParams { get; set; }

        //MWC — IK Set Library: reusable IK Set collection stored as an asset (one per rig usually)
        [Tooltip("IK Set Library applied to this manager. Its Sets are always deep copied, the asset is never modified at runtime")]
        public IKSetLibrary library;

        [Tooltip("Override: the Library replaces all the Sets below.\nAdditive: the Library Sets are appended to the Sets below")]
        public IKSetLoadMode LibraryMode = IKSetLoadMode.Override;

        [Tooltip("Apply the IK Set Library on Awake. Disable it if you prefer to bake it in the Editor with the [Load] button, or to load it yourself at runtime")]
        public bool LoadLibraryOnAwake = true;

        [Tooltip("Fill the Global Targets from the Humanoid Avatar on Awake (Root, Hips, Spine, Chest, Neck, Head, Shoulders, Hands, Feet, Legs and any IK Goal transform on the rig). " +
            "Only empty rows are filled, Transforms assigned by hand are kept. Needs a Humanoid Animator; at runtime only the IK Tags referenced by this character are found")]
        public bool AutomapOnAwake = false;

        //MWC — IK Set Remap: rig specific overrides for the IK Set Profiles that OTHER systems hand to this character
        //(Super Items, mainly). The Library above swaps the Sets this manager owns; this swaps the ones it receives.
        [Tooltip("Rig specific IK Set Profiles. When an Item asks for a Profile listed here, this character uses the " +
            "override instead. Profiles with no row are used as they are")]
        public IKSetRemap Remap;


        [Tooltip("Assign Tags on Transforms so the IK Processors can use Tags instead of Indexes.")]
        public List<IDPair<IKTag, TransformReference>> GlobalTargets = new();
        public Dictionary<int, Transform> GlobalTargets_Dic { get; private set; } = new();

        public System.Action<Animator, float, float> UpdateIKAction { get; set; }

        public System.Action<Animator, float, float, int> AnimatorIKAction { get; set; }
        public Transform Owner => transform;

        /// <summary> Store the Selected Tab in the inspector</summary>
        [HideInInspector, SerializeField] private int EditorTabs;
        [HideInInspector, SerializeField] internal int SelectedSet;

        /// <summary> Gets or sets the mapping of transforms to their corresponding bind pose rotations.  </summary>
        public Dictionary<Transform, Quaternion> BindPose { get; set; } = new();

        private void Awake()
        {
            animator = animator == null ? this.FindComponent<Animator>() : animator;

            if (animator == null)
            {
                Debug.LogError($"No Animator found on the GameObject [{name}] or its children", this);
                enabled = false;
                return;
            }
            //Store all the FLOAT animParameters of the Animator
            animatorHashParams = new();
            foreach (var parameter in animator.parameters)
            {
                if (parameter.type == UnityEngine.AnimatorControllerParameterType.Float)
                    animatorHashParams.Add(parameter.nameHash);
            }

            // Cache the initial local rotations of all the bones in the animator to be able to restore them later if needed
            var skinnedMeshes = animator.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in skinnedMeshes)
            {
                foreach (var bone in smr.bones)
                {
                    if (bone != null)
                        BindPose.TryAdd(bone, bone.localRotation);
                }
            }

            //MWC — apply the IK Set Library before any Set gets initialized (OnEnable runs after Awake)
            if (LoadLibraryOnAwake) Library_Load();

            //Resolve the Global Targets from the Avatar before OnEnable builds the Tag → Transform dictionary
            if (AutomapOnAwake) Automap(true);
        }

        #region Automap (Humanoid)

        /// <summary> IK Tag asset name → Humanoid bone resolved by <see cref="Automap(bool)"/>. </summary>
        internal static readonly (string tag, HumanBodyBones bone)[] HumanoidTargets =
        {
            ("IK_Hip",              HumanBodyBones.Hips),
            ("IK_Spine0",           HumanBodyBones.Spine),
            ("IK_Chest",            HumanBodyBones.Chest),
            ("IK_UpperChest",       HumanBodyBones.UpperChest),
            ("IK_Neck 1",           HumanBodyBones.Neck),
            ("IK_Head",             HumanBodyBones.Head),
            ("IK_LeftShoulder",     HumanBodyBones.LeftShoulder),
            ("IK_RightShoulder",    HumanBodyBones.RightShoulder),
            ("IK_LeftHand",         HumanBodyBones.LeftHand),
            ("IK_RightHand",        HumanBodyBones.RightHand),
            ("IK_LeftFoot",         HumanBodyBones.LeftFoot),
            ("IK_RightFoot",        HumanBodyBones.RightFoot),
            ("IK_LeftThigh",        HumanBodyBones.LeftUpperLeg),
            ("IK_RightThigh",       HumanBodyBones.RightUpperLeg),
            ("IK_LeftLowerLeg",     HumanBodyBones.LeftLowerLeg),
            ("IK_RightLowerLeg",    HumanBodyBones.RightLowerLeg),
        };

        /// <summary> Fills the Global Targets from the Humanoid Avatar bones and the IK Goal transforms of the rig, using the IK Tags currently loaded. Returns how many Targets were assigned. </summary>
        public int Automap(bool overwrite = false)
        {
            //At runtime only the IK Tags something references are loaded (the rows of this list, the Sets' Processors,
            //the Library, the Remap…). Tags nothing uses are not needed anyway, so they are skipped without a warning.
            var tags = new Dictionary<string, IKTag>();

            foreach (var tag in Resources.FindObjectsOfTypeAll<IKTag>())
                if (tag != null) tags[tag.name] = tag;

            return Automap(overwrite, tags, false);
        }

        /// <summary> Fills the Global Targets from the Humanoid Avatar bones and the IK Goal transforms of the rig, resolving the IK Tags by name from [tags]. Returns how many Targets were assigned. </summary>
        public int Automap(bool overwrite, IReadOnlyDictionary<string, IKTag> tags, bool warnMissingTags)
        {
            var anim = animator != null ? animator : this.FindComponent<Animator>();

            if (anim == null || !anim.isHuman)
            {
                Debug.LogWarning($"[IKManager] Automap needs a Humanoid Animator on [{name}].", this);
                return 0;
            }

            GlobalTargets ??= new();

            var mapped = 0;
            var bonesFound = 0;

            mapped += Automap_SetTarget(tags, "IK_Root", transform, overwrite, warnMissingTags);

            //GetBoneTransform() returns null when the Animator has not bound the Avatar to this hierarchy
            //(e.g. Synty rigs: one shared Avatar whose skeleton root name does not match the renamed model child).
            //Fall back to the bone names stored on the Avatar Human Description and find them on the hierarchy.
            var boneNames = Automap_AvatarBoneNames(anim.avatar);
            var children = GetComponentsInChildren<Transform>(true);

            foreach (var (tag, bone) in HumanoidTargets)
            {
                var boneTransform = anim.GetBoneTransform(bone);

                if (boneTransform == null && boneNames.TryGetValue(HumanTrait.BoneName[(int)bone], out var boneName))
                    boneTransform = Array.Find(children, x => x.name == boneName);

                if (boneTransform != null) bonesFound++;

                mapped += Automap_SetTarget(tags, tag, boneTransform, overwrite, warnMissingTags);
            }

            if (bonesFound == 0)
                Debug.LogWarning($"[IKManager] Automap: no Humanoid bone could be resolved on [{name}]. Check that the Avatar [{(anim.avatar != null ? anim.avatar.name : "None")}] matches this hierarchy.", this);

            //IK Goals are not Humanoid bones: they are extra transforms on the rig, so find them by name
            foreach (var child in children)
            {
                var goalTag = Automap_GoalTagName(child.name);
                if (goalTag != null) mapped += Automap_SetTarget(tags, goalTag, child, overwrite, warnMissingTags);
            }

            return mapped;
        }

        /// <summary> Humanoid bone name → hierarchy bone name, read from the Avatar Human Description. </summary>
        private static Dictionary<string, string> Automap_AvatarBoneNames(Avatar avatar)
        {
            var result = new Dictionary<string, string>();

            if (avatar == null) return result;

            foreach (var bone in avatar.humanDescription.human)
                if (!string.IsNullOrEmpty(bone.humanName) && !string.IsNullOrEmpty(bone.boneName))
                    result[bone.humanName] = bone.boneName;

            return result;
        }

        /// <summary> Assigns a Transform to the Global Target with the given Tag name, adding the row when it is missing. Returns 1 when something changed. </summary>
        private int Automap_SetTarget(IReadOnlyDictionary<string, IKTag> tags, string tagName, Transform value, bool overwrite, bool warnMissingTag)
        {
            if (value == null) return 0;

            if (!tags.TryGetValue(tagName, out var tag) || tag == null)
            {
                if (warnMissingTag) Debug.LogWarning($"[IKManager] Automap: no IK Tag asset named [{tagName}] was found on the project.", this);
                return 0;
            }

            var entry = GlobalTargets.Find(x => x != null && x.ID == tag);

            if (entry == null)
            {
                GlobalTargets.Add(new IDPair<IKTag, TransformReference>(tag, new TransformReference(value)));
                return 1;
            }

            entry.Value ??= new TransformReference();

            if (entry.Value.Value == value) return 0;                    //Already mapped
            if (entry.Value.Value != null && !overwrite) return 0;       //Keep the manual assignment

            entry.Value = new TransformReference(value);
            return 1;
        }

        /// <summary> Returns the IK Goal Tag name matching a transform name, or null when it is not an IK Goal. </summary>
        private static string Automap_GoalTagName(string transformName)
        {
            var n = transformName.ToLower();

            if (!Automap_KeyWord(n, "goal")) return null;

            var side = Automap_KeyWord(n, "left", "_l", " l") ? "Left" : Automap_KeyWord(n, "right", "_r", " r") ? "Right" : null;

            if (side == null) return null;

            if (Automap_KeyWord(n, "hand")) return $"IK_{side}Hand_Goal";
            if (Automap_KeyWord(n, "foot")) return $"IK_{side}Foot_Goal";
            if (Automap_KeyWord(n, "elbow")) return $"IK_{side}Elbow_Goal";
            if (Automap_KeyWord(n, "knee")) return $"IK_{side}Knee_Goal";

            return null;
        }

        /// <summary> True when the lower-case [name] contains any of the keywords. </summary>
        private static bool Automap_KeyWord(string name, params string[] keyword)
        {
            foreach (var item in keyword)
                if (name.Contains(item.ToLower())) return true;

            return false;
        }

        #endregion

        #region IK Set Remap

        // MWC — single choke point for every system that hands an IK Set Profile to this character. Keep resolving
        // through here instead of reading the authored Profile directly, so a new consumer gets the rig swap for free.
        /// <summary> Returns the rig specific override of an IK Set Profile, or the Profile itself. </summary>
        public IKSetProfile ResolveProfile(IKSetProfile profile)
            => profile != null && Remap != null ? Remap.GetProfile(profile) : profile;

        /// <summary> True when this character has a Remap and it holds a row for that default Profile. </summary>
        public bool HasProfileOverride(IKSetProfile profile) => Remap != null && Remap.HasProfile(profile);

        #endregion

        #region IK Set Library

        /// <summary> Applies the assigned IK Set Library using the serialized <see cref="LibraryMode"/>. </summary>
        public void Library_Load() => Library_Load(library, LibraryMode);

        /// <summary> Applies a given IK Set Library using the serialized <see cref="LibraryMode"/>. </summary>
        public void Library_Load(IKSetLibrary source) => Library_Load(source, LibraryMode);

        /// <summary> Replaces all the Sets of this manager with the ones stored in the Library. </summary>
        public void Library_Override(IKSetLibrary source) => Library_Load(source, IKSetLoadMode.Override);

        /// <summary> Appends the Sets stored in the Library to the ones already on this manager. </summary>
        public void Library_Add(IKSetLibrary source) => Library_Load(source, IKSetLoadMode.Additive);

        /// <summary> Applies an IK Set Library using an explicit mode. </summary>
        public void Library_Load(IKSetLibrary source, IKSetLoadMode mode)
        {
            if (source == null) return;
            Sets_Apply(source.CloneSets(), mode);
        }

        /// <summary> Installs already deep copied Sets on this manager, disabling/initializing them
        /// when this happens while the game is already running. </summary>
        private void Sets_Apply(List<IKSet> newSets, IKSetLoadMode mode)
        {
            animatorHashParams ??= new(); //Needed by IKSet.Initialize when this is called before Awake

            //Only re-initialize when the manager is already live, otherwise OnEnable does it
            var live = Application.isPlaying && GlobalInitialized;

            if (mode == IKSetLoadMode.Override)
            {
                if (live && sets != null)
                {
                    foreach (var set in sets) set?.OnDisable(animator, animatorHashParams);
                }
                sets = new List<IKSet>();
            }
            else
            {
                sets ??= new List<IKSet>();
            }

            foreach (var set in newSets)
            {
                if (set == null) continue;

                if (sets.Exists(x => x != null && x.Name == set.Name))
                    Debug.LogWarning($"[IKManager] Duplicated IK Set name <B>[{set.Name}]</B>. FindSet() will only return the first one.", this);

                sets.Add(set);

                if (live)
                {
                    set.Initialize(this);
                    set.OnEnable(animator, animatorHashParams);
                }
            }
        }

        #endregion

        private bool GlobalInitialized = false;
        private void InitializeGlobalTargets()
        {
            if (GlobalInitialized) return;

            GlobalTargets ??= new(); //Make sure the Tag Target list is not Null

            //Convert the Global Targets List to a Dictionary for faster access on the IK Processors
            GlobalTargets_Dic = new();

            foreach (var t in GlobalTargets)
            {
                if (t.Value != null && t.ID != null)
                {
                    if (!GlobalTargets_Dic.ContainsKey(t.ID))
                    {
                        GlobalTargets_Dic.Add(t.ID, t.Value.Value);
                    }
                }
            }

            foreach (var set in sets) set.Initialize(this);
            GlobalInitialized = true;
        }

        private void OnEnable()
        {
            animatePhysics = animator.updateMode == AnimatorUpdateMode.Fixed;

            InitializeGlobalTargets();

            if (animatePhysics)
                StartCoroutine(SolveFixedUpdateIK());

            //initialize every IK Set
            foreach (var set in sets)
                set.OnEnable(animator, animatorHashParams);
        }

        private bool animatePhysics;

        /// <summary>The Animator solves on the physics step, so an IK tick is a FixedUpdate and not a render frame</summary>
        internal bool AnimatePhysics => animatePhysics;

        private void OnDisable()
        {
            foreach (var set in sets)
            {
                set.OnDisable(animator, animatorHashParams);
            }

            StopAllCoroutines();
        }
        private IEnumerator SolveFixedUpdateIK()
        {
            var wait = new WaitForFixedUpdate();

            while (true)
            {
                yield return wait;

                UpdateIKAction?.Invoke(animator, Weight, Time.fixedDeltaTime);

                foreach (var set in sets)
                {
                    set.CacheValues(animator);
                    set.LateUpdate(animator, Weight, Time.fixedDeltaTime);
                }

            }
        }
        private void LateUpdate()
        {
            if (animatePhysics) return;

            UpdateIKAction?.Invoke(animator, Weight, Time.deltaTime);

            foreach (var set in sets)
            {
                set.CacheValues(animator);
                set.LateUpdate(animator, Weight, Time.deltaTime);
            }
        }

        private void OnAnimatorIK(int LayerIndex)
        {
            var time = animator.updateMode == AnimatorUpdateMode.Normal ? Time.deltaTime : Time.fixedDeltaTime;

            AnimatorIKAction?.Invoke(animator, Weight, time, LayerIndex);


            foreach (var set in sets)
                set.OnAnimatorIK(animator, Weight, time, LayerIndex);
        }



        /// <summary>Activate or deactivate a Set.</summary>
        /// <param name="set"> name of the set</param>
        /// <param name="value">enable: true disable: false</param>
        public void Set_Enable(string set, bool value)
        {
            if (!enabled) return;

            var sets = set.Split(',');

            foreach (var s in sets)
            {
                var NewSet = FindSet(s);
                NewSet?.Enable(value);
            }
        }

        /// <summary>Finds a set by its name and Activates it</summary>
        public void Set_Enable(string set) => Set_Enable(set, true);

        /// <summary>Finds a set by its name and deactivates it</summary>
        public void Set_Disable(string set) => Set_Enable(set, false);

        /// <summary>Finds a set by its name and blends its weight to 1</summary>
        public void Set_Weight_1(string set) => Set_Weight(set, true);
        /// <summary>Finds a set by its name and blends its weight to 0</summary>
        public void Set_Weight_0(string set) => Set_Weight(set, false);

        /// <summary>Finds a set by its name and blends its weight to 1 (true) or 0 (false)</summary>
        public void Set_Weight(string set, bool value)
        {
            var sets = set.Split(',');
            foreach (var s in sets)
            {
                var NewSet = FindSet(s);
                NewSet?.SetWeight(value);
            }
        }

        /// <summary> Sets a new Target to a IK Set given the set name, and the new index and target transform value </summary>

        public void Target_Set(string set, Transform newTarget, int index)
        {
            var sets = set.Split(',');
            foreach (var s in sets)
            {
                var NewSet = FindSet(s);
                NewSet?.SetTarget(newTarget, index);
            }
        }

        /// <summary>  Finds a IK Set given a name </summary>
        public virtual IKSet FindSet(string set) => sets.Find(x => x.Name == set);

        public void Target_Clear(string set, int index)
        {
            var NewSet = FindSet(set);
            NewSet?.ClearTarget(index);
        }

        public void Target_Clear(string set)
        {
            var sets = set.Split(',');
            foreach (var s in sets)
            {
                var NewSet = FindSet(s);
                NewSet?.ClearAllTargets();
            }
        }

        public void Target_Add_Global(IDPair<IKTag, TransformReference> newTarget)
        {
            if (newTarget.Value.Value != null && newTarget.ID != null)
            {
                //Add or update the target in the dictionary
                if (GlobalTargets_Dic.ContainsKey(newTarget.ID))
                {
                    GlobalTargets_Dic[newTarget.ID] = newTarget.Value.Value;
                }
                else
                {
                    GlobalTargets_Dic.Add(newTarget.ID, newTarget.Value.Value);
                    //  Debug.Log("ADDED TO DIC");
                }


                //update also the list to keep it in sync with the dictionary for inspector visibility and so the IK Processors can find the index of the target in the list if they need to
                var foundTarget = GlobalTargets.Find(x => x.ID.ID == newTarget.ID.ID);

                if (foundTarget != null)
                {
                    foundTarget.Value = new(newTarget.Value.Value);
                }
                else
                {
                    GlobalTargets.Add(new(newTarget.ID, new(newTarget.Value)));
                }
            }
        }

        public void Target_Remove_Global(IDPair<IKTag, TransformReference> newTarget)
        {
            if (newTarget.ID != null)
            {
                if (GlobalTargets_Dic.ContainsKey(newTarget.ID))
                {
                    GlobalTargets_Dic[newTarget.ID] = null;
                }
                //update also the list to keep it in sync with the dictionary for inspector visibility and so the IK Processors can find the index of the target in the list if they need to
                if (GlobalTargets.Find(x => x.ID == newTarget.ID) is IDPair<IKTag, TransformReference> foundTarget)
                {
                    foundTarget.Value = new();
                }
            }
        }

        internal Transform GetTargetByTag(IKTag tag)
        {
            if (GlobalTargets_Dic.TryGetValue(tag, out var target))
            {
                return target;
            }
            return null;
        }

        public void Target_Set(string set, Transform[] targets)
        {
            var sets = set.Split(',');

            foreach (var s in sets)
            {
                var NewSet = FindSet(s);
                NewSet?.SetTargets(targets);
            }
        }


        public void Processor_SetEnable(string set, string processor, bool value)
        {
            var NewSet = FindSet(set);
            NewSet?.Processor_SetEnable(processor, value);
        }

        private void Reset()
        {
            animator = this.FindComponent<Animator>();
        }



        private void AutoFillTargetsOnValidate()
        {
            foreach (var idPair in GlobalTargets)
            {
                if (idPair.ID != null && idPair.Value.Value == null)
                {
                    if (idPair.ID.name.ToLower().Contains("head")) idPair.Value = new TransformReference(transform.FindGrandChild("head"));
                    else if (idPair.ID.name.ToLower().Contains("neck")) idPair.Value = new TransformReference(transform.FindGrandChild("neck"));
                    else if (idPair.ID.name.ToLower().Contains("upperchest")) idPair.Value = new TransformReference(transform.FindGrandChild("upperchest"));
                    else if (idPair.ID.name.ToLower().Contains("chest")) idPair.Value = new TransformReference(transform.FindGrandChild("chest"));
                    else if (idPair.ID.name.ToLower().Contains("root")) idPair.Value = new TransformReference(transform);
                }
            }
        }

        public bool List_GlobalTarget_HasTag(IKTag tag) => GlobalTargets.Exists(x => x.ID == tag);
        public bool List_GlobalTarget_HasTag(string tag) => GlobalTargets.Exists(x => x.ID != null && x.ID.name == tag);
        public Transform List_GlobalTarget_GetTagValue(IKTag tag)
        {
            var target = GlobalTargets.Find(x => x.ID == tag);
            return target?.Value.Value;
        }

        public bool List_GlobalTarget_HasTagValue(IKTag tag) => GlobalTargets.Exists(x => x.ID == tag && x.Value.Value != null);

#if UNITY_EDITOR

        private void OnDrawGizmosSelected()
        {
            if (!enabled) return;
            if (!UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)) return;


            if (sets != null && sets.Count > 0 && animator != null)
            {
                for (int k = 0; k < sets.Count; k++)
                {
                    var set = sets[k];

                    if (!set.active) continue;
                    if (!set.Gizmos) continue; //Global Gizmos switch of the Set

                    set.Owner = this; //MWC — edit-time Gizmos need the Owner for its Scale Multiplier (Initialize only runs in Play Mode)

                    //Paint the Weight Processors
                    if (set.weightProcessors != null)
                    {
                        foreach (var weightP in set.weightProcessors)
                        {
                            weightP?.OnDrawGizmos(set, animator);
                        }
                    }

                    if (set != null && SelectedSet == k && set.active && set.Processors != null)
                    {
                        for (int i = 0; i < set.Processors.Count; i++)
                        {
                            var link = set.Processors[i];
                            if (link != null && link.Active && set.SelectedIKProcessor == i)
                                link.OnDrawGizmos(this, set, animator, Weight);
                        }
                    }
                }
            }
        }
#endif

        private void OnValidate()
        {
            foreach (var set in sets)
            {
                if (set.aimer == null) set.aimer = this.FindComponent<Aim>();

                set.OnValidate(this);
            }

            if (Application.isPlaying)
            {
                OnValidateAction?.Invoke(this);
            }

            //Autofill some common tags like head, hand, foot, etc... if they are not already in the global targets list for easier use on the IK Processors
            AutoFillTargetsOnValidate();
        }
        public System.Action<IKManager> OnValidateAction { get; set; }


    }


#if UNITY_EDITOR
    [CustomEditor(typeof(IKManager))]
    public class IKManagerEditor : Editor
    {
        ReorderableList Reo_Sets;
        ReorderableList Reo_GlobalTargets;

        private IKSetDrawer setDrawer; //MWC — shared IKSet renderer (also used by IKSetProfile & IKSetLibrary)

        IKManager m;

        SerializedProperty GlobalWeight, ScaleMultiplier, IKSets, animator, EditorTabs, SelectedSet, GlobalTargets,
            library, LibraryMode, LoadLibraryOnAwake, Remap, AutomapOnAwake
            ;

        private void OnEnable()
        {
            m = (IKManager)target;

            animator = serializedObject.FindProperty("animator");
            IKSets = serializedObject.FindProperty("sets");
            EditorTabs = serializedObject.FindProperty("EditorTabs");
            GlobalWeight = serializedObject.FindProperty("Weight");
            ScaleMultiplier = serializedObject.FindProperty(nameof(IKManager.ScaleMultiplier));
            SelectedSet = serializedObject.FindProperty("SelectedSet");
            GlobalTargets = serializedObject.FindProperty("GlobalTargets");

            library = serializedObject.FindProperty("library");
            LibraryMode = serializedObject.FindProperty("LibraryMode");
            LoadLibraryOnAwake = serializedObject.FindProperty("LoadLibraryOnAwake");
            AutomapOnAwake = serializedObject.FindProperty("AutomapOnAwake");
            Remap = serializedObject.FindProperty("Remap");

            setDrawer = new IKSetDrawer(m, m);

            ReordableLists();

            FindBonesFromTags();
        }

        private void ReordableLists()
        {
            Reo_Sets = new ReorderableList(serializedObject, IKSets, true, true, true, true)
            {
                drawHeaderCallback = Draw_Header_Set,
                drawElementCallback = Draw_Element,
                onAddCallback = OnAddCallBack,
                onSelectCallback = (list) => { SelectedSet.intValue = list.index; }
            };

            SelectedSet.intValue = Reo_Sets.index;

            Reo_GlobalTargets = new ReorderableList(serializedObject, GlobalTargets, true, true, true, true)
            {
                drawHeaderCallback = rect =>
                {
                    const float k_ButtonWidth = 90f;
                    const float k_AutomapWidth = 70f; //MWC — [Automap] button (humanoid rigs only)
                    const float k_OnAwakeWidth = 78f; //[On Awake] toggle next to it

                    var isHumanoid = m.animator != null && m.animator.isHuman;
                    var automapSpace = isHumanoid ? k_AutomapWidth + k_OnAwakeWidth + 8f : 0f;

                    var labelRect = new Rect(rect.x, rect.y, rect.width - k_ButtonWidth - automapSpace - 4f, rect.height);

                    EditorGUI.LabelField(labelRect, new GUIContent("Global Targets", "Tag + Transform pairs shared across all IK Sets"));

                    //MWC — Automap: fill the Global Targets using the Humanoid Avatar bones
                    if (isHumanoid)
                    {
                        var prevColor = GUI.color;
                        var automapRect = new Rect(rect.xMax - k_ButtonWidth - 4f - k_AutomapWidth, rect.y, k_AutomapWidth, rect.height);
                        GUI.color = new Color(0.5f, 0.8f, 1f, 1f);
                        if (GUI.Button(automapRect, new GUIContent("Automap",
                            "Map the Global Targets automatically using the Humanoid bones of the Avatar (plus any IK Goal transform found on the hierarchy)")))
                            pendingAutomap = true; //Deferred: mutating the list mid-layout desyncs the GUI
                        GUI.color = prevColor;

                        //Same mapping, run on Awake: for prefabs shared between rigs, or characters assembled at runtime
                        var onAwakeRect = new Rect(automapRect.x - 4f - k_OnAwakeWidth, rect.y, k_OnAwakeWidth, rect.height);
                        AutomapOnAwake.boolValue = EditorGUI.ToggleLeft(onAwakeRect, new GUIContent("On Awake",
                            "Run Automap on Awake. Only empty rows are filled, Transforms assigned by hand are kept. " +
                            "At runtime only the IK Tags this character references (rows, Processors, Library, Remap) can be found"),
                            AutomapOnAwake.boolValue);
                    }

                    //only show the button if the global targets is empty
                    if (GlobalTargets.arraySize == 0)
                    {
                        var prevColor = GUI.color;
                        var buttonRect = new Rect(rect.xMax - k_ButtonWidth, rect.y, k_ButtonWidth, rect.height);
                        GUI.color = new Color(0.4f, 1f, 0.4f, 1f);
                        if (GUI.Button(buttonRect, new GUIContent("Get Targets", "Collect all unique Transforms from every IKSet and append them to this list")))
                            GatherTargetsFromSets();
                        GUI.color = prevColor;
                    }

                    //add a button to clear all the global targets
                    if (GlobalTargets.arraySize > 0)
                    {
                        var prevColor = GUI.color;
                        var buttonRect = new Rect(rect.xMax - k_ButtonWidth, rect.y, k_ButtonWidth, rect.height);
                        GUI.color = new Color(1f, 0.4f, 0.4f, 1f);
                        if (GUI.Button(buttonRect, new GUIContent("Clear Targets", "Clear all the global targets")))
                        {
                            if (EditorUtility.DisplayDialog("Clear Global Targets", "Are you sure you want to clear all global targets? This action cannot be undone.", "Yes", "No"))
                            {
                                GlobalTargets.arraySize = 0;
                                serializedObject.ApplyModifiedProperties();
                            }

                        }
                        GUI.color = prevColor;
                    }
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.y += 2;
                    if (GlobalTargets.arraySize <= index) return;

                    var element = GlobalTargets.GetArrayElementAtIndex(index);
                    var id = element.FindPropertyRelative("ID");
                    var value = element.FindPropertyRelative("Value");

                    var height = EditorGUIUtility.singleLineHeight;
                    var half = (rect.width - 4f) * 0.50f;
                    var space = 15;

                    var idRect = new Rect(rect.x + space, rect.y, half - space, rect.height - 4f);
                    var valueRect = new Rect(rect.x + half + 4f + space, rect.y, rect.width - half - 4f - space, rect.height - 4f);

                    EditorGUIUtility.labelWidth = 1;
                    EditorGUI.PropertyField(idRect, id, GUIContent.none);
                    EditorGUI.PropertyField(valueRect, value, GUIContent.none);
                    EditorGUIUtility.labelWidth = 0;
                },

                elementHeightCallback = _ => EditorGUIUtility.singleLineHeight + 4f,

                onAddCallback = list =>
                {
                    list.serializedProperty.arraySize++;
                    serializedObject.ApplyModifiedProperties();
                },
            };
        }

        private void OnAddCallBack(ReorderableList list)
        {
            m.sets ??= new();

            m.sets.Add(
                new IKSet()
                {
                    // m_name = $"",
                    name = new StringReference($"newIK Set {list.count}") { UseConstant = true },

                    //Targets = new Scriptables.TransformReference[1],
                    EnableTime = 0.25f,
                    DisableTime = 0.25f
                }
                );

            IKSets.serializedObject.ApplyModifiedProperties();

            EditorUtility.SetDirty(m);
        }
        private void Draw_Header_Set(Rect rect)
        {
            var r = new Rect(rect);
            var WeightRect = new Rect(rect) { x = r.width - 30, width = 65 };
            var a = new Rect(rect) { width = 65 };
            EditorGUI.LabelField(a, new GUIContent("Active", "Enable Disable the IK Set"));
            r.x += 60;
            r.width = 60;
            EditorGUI.LabelField(r, new GUIContent("IK Set", "Name of the IK Profile "));
            EditorGUI.LabelField(WeightRect, new GUIContent("Weight", "Modes are the Animations that can be played on top of the States"));
        }

        private void Draw_Element(Rect rect, int index, bool isActive, bool isFocused)
        {
            rect.y += 2;
            if (IKSets.arraySize <= index) return;

            var ikSet = IKSets.GetArrayElementAtIndex(index);
            var active = ikSet.FindPropertyRelative("active");
            //var name = ikSet.FindPropertyRelative("m_name");
            var Name = ikSet.FindPropertyRelative("name");
            var weight = ikSet.FindPropertyRelative("weight");

            var height = EditorGUIUtility.singleLineHeight;

            var activeRect = new Rect(rect.x, rect.y, 20, height);
            var NameRect = new Rect(rect.x + 35, rect.y, rect.width * 0.7f - 35, height);
            var weightRect = new Rect(rect.width - rect.width * 0.3f + 25, rect.y, rect.width * 0.3f + 12f, height);

            EditorGUIUtility.labelWidth = 30;

            var dC = GUI.color;
            if (index == SelectedSet.intValue)
                GUI.color = Color.yellow;


            active.boolValue = EditorGUI.Toggle(activeRect, GUIContent.none, active.boolValue);
            EditorGUI.PropertyField(NameRect, Name, GUIContent.none);
            EditorGUI.PropertyField(weightRect, weight, new GUIContent(" "));
            EditorGUIUtility.labelWidth = 0;

            GUI.color = dC;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Manage all IK logic for all components");


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(animator);
                EditorGUILayout.PropertyField(GlobalWeight);
                EditorGUILayout.PropertyField(ScaleMultiplier);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GlobalWeight.isExpanded = MalbersEditor.Foldout(GlobalWeight.isExpanded, $"Global IK Targets [{Reo_GlobalTargets.count}]");
                if (GlobalWeight.isExpanded)
                    Reo_GlobalTargets.DoLayoutList();
            }

            DrawRemap();
            DrawLibraries();

            Reo_Sets.DoLayoutList();

            var index = SelectedSet.intValue;

            if (Reo_Sets.count > 0 && index > -1 && index < Reo_Sets.count)
            {
                var selectedSet = IKSets.GetArrayElementAtIndex(index);
                var Name = m.sets[index].name.Value;

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.indentLevel++;
                    {
                        Rect rowRect = EditorGUILayout.GetControlRect();
                        Rect indented = EditorGUI.IndentedRect(rowRect);
                        bool hasPaste = IKSetCopyPaste.HasClipboard;

                        const float BtnW = 58f, Gap = 4f;
                        //MWC — Save + Copy [+ Paste]
                        int btnCount = hasPaste ? 3 : 2;
                        float buttonsTotal = btnCount * (BtnW + Gap);

                        Rect foldoutRect = new(indented.x, indented.y, indented.width - buttonsTotal, indented.height);

                        float x = indented.xMax - buttonsTotal + Gap;
                        Rect saveRect = new(x, indented.y, BtnW, indented.height); x += BtnW + Gap;
                        Rect copyRect = new(x, indented.y, BtnW, indented.height); x += BtnW + Gap;

                        // Zero indent so Foldout doesn't double-offset the already-indented rect
                        int savedIndent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 0;
                        selectedSet.isExpanded = EditorGUI.Foldout(foldoutRect, selectedSet.isExpanded, $"[{Name} - IK Set]", true);
                        EditorGUI.indentLevel = savedIndent;

                        //MWC — save this single IK Set into an IK Set Library asset
                        if (GUI.Button(saveRect, new GUIContent("Save", "Save this IK Set into an IK Set Library asset")))
                            ShowSaveSetMenu(index);

                        if (GUI.Button(copyRect, new GUIContent("Copy", "Copy IK Processors and Weight Processors from this IK Set")))
                            IKSetCopyPaste.CopyProcessorsFrom(m.sets[index]);

                        if (hasPaste)
                        {
                            Rect pasteRect = new(x, indented.y, BtnW, indented.height);
                            if (GUI.Button(pasteRect, new GUIContent("Paste", $"Paste IK Processors and Weight Processors from \"{IKSetCopyPaste.ClipboardSetName}\"")))
                            {
                                Undo.RecordObject(m, "Paste IK Processors");
                                IKSetCopyPaste.PasteProcessorsInto(m.sets[index]);
                                EditorUtility.SetDirty(m);
                            }
                        }
                    }
                    EditorGUI.indentLevel--;

                    //MWC — the whole Set UI now comes from the shared IKSetDrawer
                    if (selectedSet.isExpanded)
                        setDrawer.DrawSet(selectedSet, m.sets[index], EditorTabs);
                }
            }
            serializedObject.ApplyModifiedProperties();

            //MWC — run the deferred Library load here: no GUILayout control is drawn after this point
            if (pendingLibraryLoad)
            {
                pendingLibraryLoad = false;
                LoadLibrariesInEditor();
            }

            //MWC — run the deferred Automap here for the same reason
            if (pendingAutomap)
            {
                pendingAutomap = false;
                AutomapHumanoidTargets();
            }

            //Run the deferred Save here too: SaveFilePanelInProject and DisplayDialog are modal and break any open layout scope
            if (pendingLibrarySave)
            {
                pendingLibrarySave = false;
                SaveSetsToLibrary();
            }
        }

        /// <summary> Set by the [Load] button, consumed at the end of OnInspectorGUI. </summary>
        private bool pendingLibraryLoad;

        /// <summary> Set by the [Automap] button, consumed at the end of OnInspectorGUI. </summary>
        private bool pendingAutomap;

        /// <summary> Set by the [Save] button, consumed at the end of OnInspectorGUI. </summary>
        private bool pendingLibrarySave;

        // ── IK Set Library ───────────────────────────────────────────────── MWC
        private void DrawLibraries()
        {


            var hasLibrary = m.library != null;

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                //Library + [Load] + [Save] all on the same row
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUIUtility.labelWidth = 100;
                    EditorGUILayout.PropertyField(library, new GUIContent("IK Sets Library"));
                    EditorGUIUtility.labelWidth = 0;

                    //Load only makes sense once a Library asset is assigned
                    using (new EditorGUI.DisabledGroupScope(!hasLibrary))
                    {
                        var prevColor = GUI.color;
                        GUI.color = new Color(0.4f, 1f, 0.4f, 1f);

                        if (GUILayout.Button(new GUIContent("Load",
                            "Copy the Sets from the Library into this IK Manager now, so you can see and tweak them before playing"),
                            EditorStyles.miniButton, GUILayout.Width(50)))
                            pendingLibraryLoad = true; //Deferred: mutating the sets list mid-layout desyncs the GUI

                        GUI.color = prevColor;
                    }

                    using (new EditorGUI.DisabledGroupScope(m.sets == null || m.sets.Count == 0))
                    {
                        if (GUILayout.Button(new GUIContent("Save", hasLibrary
                            ? $"Overwrite the Library \"{m.library.name}\" with the {m.sets.Count} IK Set(s) of this manager"
                            : "Save every IK Set of this manager into a new IK Set Library asset"),
                            EditorStyles.miniButton, GUILayout.Width(50)))
                            pendingLibrarySave = true; //Deferred: the modal Save dialog swallows the layout event and breaks the open scopes
                    }
                }

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(LibraryMode, GUIContent.none);
                    EditorGUIUtility.labelWidth = 120;
                    EditorGUILayout.PropertyField(LoadLibraryOnAwake, new GUIContent("Load on Awake"), GUILayout.Width(140));
                    EditorGUIUtility.labelWidth = 0;
                }

                if (m.LoadLibraryOnAwake && hasLibrary)
                {
                    var msg = m.LibraryMode == IKSetLoadMode.Override
                        ? $"On Awake [{m.library.name}] will REPLACE every IK Set below."
                        : $"On Awake the Sets of [{m.library.name}] will be APPENDED to the IK Sets below.";
                    EditorGUILayout.HelpBox(msg, MessageType.Info);
                }
            }


        }

        // ── IK Set Remap ─────────────────────────────────────────────────── MWC
        /// <summary> Draws the rig specific IK Set Profile overrides used by Items. </summary>
        private void DrawRemap()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUIUtility.labelWidth = 100;
                    EditorGUILayout.PropertyField(Remap, new GUIContent("IK Set Remap", Remap.tooltip));
                    EditorGUIUtility.labelWidth = 0;
                }

                var remap = m.Remap;

                if (remap != null && remap.remap != null)
                {
                    var rows = remap.remap.Count;

                    EditorGUILayout.HelpBox(rows == 0
                        ? $"[{remap.name}] is empty. Add a row per Profile this rig needs to swap."
                        : $"Items on this character resolve their IK Set Profile through [{remap.name}] ({rows} row(s)).",
                        rows == 0 ? MessageType.Warning : MessageType.Info);
                }
            }
        }

        private void LoadLibrariesInEditor()
        {
            if (m.library == null) return;

            if (m.LibraryMode == IKSetLoadMode.Override && m.sets != null && m.sets.Count > 0 &&
                !EditorUtility.DisplayDialog("Override IK Sets",
                    $"This will replace the {m.sets.Count} IK Set(s) currently on [{m.name}] with the Sets stored in [{m.library.name}].",
                    "Replace", "Cancel"))
                return;

            Undo.RecordObject(m, "Load IK Set Library");

            m.Library_Load();

            EditorUtility.SetDirty(m);
            serializedObject.Update();

            setDrawer.ClearCache(); //The sets list changed: cached ReorderableLists point at stale paths
            SelectedSet.intValue = m.sets.Count > 0 ? 0 : -1;
            serializedObject.ApplyModifiedProperties();

            Debug.Log($"[IKManager] Loaded {m.sets.Count} IK Set(s) from [{m.library.name}] into [{m.name}].", m);

            Repaint();
        }

        /// <summary> Saves every Set of this manager: overwrites the assigned Library, or creates a new one. </summary>
        private void SaveSetsToLibrary()
        {
            if (m.sets == null || m.sets.Count == 0) return;

            //No Library assigned: fall back to creating a brand new asset and hook it up
            if (m.library == null)
            {
                var created = SaveSetsToNewLibrary(m.sets, m.name);

                if (created != null)
                {
                    Undo.RecordObject(m, "Assign IK Set Library");
                    library.objectReferenceValue = created;
                    serializedObject.ApplyModifiedProperties();
                }
                return;
            }

            if (!EditorUtility.DisplayDialog("Override IK Set Library",
                $"Are you sure you want to override the current library?\n\n[{m.library.name}] will be replaced by the {m.sets.Count} IK Set(s) of [{m.name}].",
                "Yes", "No"))
                return;

            Undo.RecordObject(m.library, "Override IK Set Library");

            m.library.StoreSets(m.sets);

            EditorUtility.SetDirty(m.library);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(m.library);

            Debug.Log($"[IKManager] Overrode the Library \"{m.library.name}\" with {m.library.sets.Count} IK Set(s) from [{m.name}].", m.library);
        }

        /// <summary> Menu offering to save the selected Set into a new Library, or into the assigned one. </summary>
        private void ShowSaveSetMenu(int setIndex)
        {
            var set = m.sets[setIndex];
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("New IK Set Library…"), false,
                () => SaveSetsToNewLibrary(new List<IKSet>() { set }, set.Name));

            if (m.library != null)
            {
                var captured = m.library;
                var exists = captured.sets != null && captured.sets.Exists(x => x != null && x.Name == set.Name);

                // Tell the user up front whether this will land on an existing Set or add a new one
                menu.AddItem(new GUIContent(exists
                    ? $"Save to [{captured.name}]   (already has \"{set.Name}\")"
                    : $"Save to [{captured.name}]"), false, () => SaveSetToLibrary(captured, set));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Save to [no Library assigned]"));
            }

            menu.ShowAsContext();
        }

        /// <summary> Stores a deep copy of one Set in a Library. A Set with the same name can be replaced or kept alongside. </summary>
        private void SaveSetToLibrary(IKSetLibrary target, IKSet set)
        {
            target.sets ??= new List<IKSet>();

            // Sets are matched by name everywhere else (FindSet, Set_Add), so the same rule applies here
            var existing = target.sets.FindIndex(x => x != null && x.Name == set.Name);
            var action = "Appended";

            if (existing >= 0)
            {
                var option = EditorUtility.DisplayDialogComplex("Save IK Set to Library",
                    $"[{target.name}] already has an IK Set named \"{set.Name}\".\n\n" +
                    $"Replace it with the one from [{m.name}], or append a second copy next to it?",
                    "Replace", "Cancel", "Append");

                if (option == 1) return;

                Undo.RecordObject(target, "Save IK Set");
                var copy = IKSetLibrary.DeepCopy(new List<IKSet>() { set })[0];

                if (option == 0) { target.sets[existing] = copy; action = "Replaced"; }
                else target.sets.Add(copy);
            }
            else
            {
                Undo.RecordObject(target, "Save IK Set");
                target.sets.AddRange(IKSetLibrary.DeepCopy(new List<IKSet>() { set }));
            }

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(target);

            Debug.Log($"[IKManager] {action} IK Set \"{set.Name}\" in the Library \"{target.name}\".", target);
        }

        private IKSetLibrary SaveSetsToNewLibrary(List<IKSet> source, string defaultName)
        {
            if (source == null || source.Count == 0) return null;

            var path = EditorUtility.SaveFilePanelInProject("Save IK Set Library", $"{defaultName} IK Library", "asset",
                "Choose where to store the new IK Set Library asset");

            if (string.IsNullOrEmpty(path)) return null;

            var newLibrary = CreateInstance<IKSetLibrary>();
            newLibrary.Description = $"Saved from [{m.name}]";
            newLibrary.StoreSets(source);

            AssetDatabase.CreateAsset(newLibrary, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(newLibrary);

            Debug.Log($"[IKManager] Saved {newLibrary.sets.Count} IK Set(s) into \"{path}\".", newLibrary);

            return newLibrary;
        }

        private void GatherTargetsFromSets()
        {
            Undo.RecordObject(m, "Gather IKSet Targets");

            m.GlobalTargets ??= new();

            // Build a set of transforms already present for O(1) duplicate check
            var existing = new HashSet<Transform>();
            foreach (var entry in m.GlobalTargets)
            {
                if (entry?.Value?.Value != null)
                    existing.Add(entry.Value.Value);
            }

            int added = 0;
            foreach (var set in m.sets)
            {
                if (set?.Targets == null) continue;
                foreach (var tRef in set.Targets)
                {
                    var t = tRef?.Value;
                    if (t == null || !existing.Add(t)) continue; // Add returns false if already present

                    var newGlobal = new IDPair<IKTag, TransformReference>(FindTagByName(t), new(t));

                    m.GlobalTargets.Add(newGlobal);
                    added++;
                }
            }

            EditorUtility.SetDirty(m);
            serializedObject.Update();
            Debug.Log($"[IKManager] Get Targets: added {added} unique Transform(s) to Global Targets.", m);
        }

        // ── Automap (Humanoid) ───────────────────────────────────────────────── MWC
        // The mapping itself lives on IKManager.Automap (it also runs on Awake); this is the button's Editor wrapper.

        /// <summary> Fills the Global Targets with the Humanoid bones of the Avatar and any IK Goal transform on the hierarchy. </summary>
        internal void AutomapHumanoidTargets()
        {
            var anim = m.animator != null ? m.animator : m.FindComponent<Animator>();

            if (anim == null || !anim.isHuman)
            {
                Debug.LogWarning($"[IKManager] Automap needs a Humanoid Animator on [{m.name}].", m);
                return;
            }

            m.GlobalTargets ??= new();

            //Ask what to do with the Targets that are already assigned
            var overwrite = false;

            if (m.GlobalTargets.Exists(x => x != null && x.Value != null && x.Value.Value != null))
            {
                var option = EditorUtility.DisplayDialogComplex("Automap Global IK Targets",
                    "Some Global Targets already have a Transform assigned.\n\nFill only the empty ones, or overwrite them all?",
                    "Fill Empty Only", "Cancel", "Overwrite All");

                if (option == 1) return;
                overwrite = option == 2;
            }

            //Every IK Tag asset of the project (the runtime path only sees the loaded ones): cache them once,
            //MTools.GetInstance() scans the whole AssetDatabase on each call
            var tags = new Dictionary<string, IKTag>();
            foreach (var tag in MTools.GetAllInstances<IKTag>())
                if (tag != null) tags[tag.name] = tag;

            Undo.RecordObject(m, "Automap Global IK Targets");

            var mapped = m.Automap(overwrite, tags, true);

            EditorUtility.SetDirty(m);
            serializedObject.Update();

            Debug.Log($"[IKManager] Automap: assigned {mapped} Global Target(s) from the Humanoid Avatar of [{m.name}].", m);

            Repaint();
        }

        private IKTag FindTagByName(Transform t)
        {
            if (t == m.transform) return MTools.GetInstance<IKTag>("IK_Root");

            var n = t.name.ToLower();

            if (KeyWord(n, "head")) return MTools.GetInstance<IKTag>("IK_Head");
            if (KeyWord(n, "neck")) return MTools.GetInstance<IKTag>("IK_Neck 1");
            if (KeyWord(n, "hip")) return MTools.GetInstance<IKTag>("IK_Hip");
            if (KeyWord(n, "spine0")) return MTools.GetInstance<IKTag>("IK_Spine0");
            if (KeyWord(n, "spine1")) return MTools.GetInstance<IKTag>("IK_Chest");
            if (KeyWord(n, "chest")) return MTools.GetInstance<IKTag>("IK_Chest");
            if (KeyWord(n, "spine2")) return MTools.GetInstance<IKTag>("IK_UpperChest");
            if (KeyWord(n, "uppperchest")) return MTools.GetInstance<IKTag>("IK_UpperChest");


            if (KeyWord(n, "hand") && KeyWord(n, "goal") && KeyWord(n, "left", "_l", " l")) return MTools.GetInstance<IKTag>("IK_LeftHand_Goal");
            if (KeyWord(n, "hand") && KeyWord(n, "goal") && KeyWord(n, "right", "_r", " r")) return MTools.GetInstance<IKTag>("IK_RightHand_Goal");
            if (KeyWord(n, "foot") && KeyWord(n, "goal") && KeyWord(n, "left", "_l", " l")) return MTools.GetInstance<IKTag>("IK_LeftFoot_Goal");
            if (KeyWord(n, "foot") && KeyWord(n, "goal") && KeyWord(n, "right", "_r", " r")) return MTools.GetInstance<IKTag>("IK_RightFoot_Goal");

            if (KeyWord(n, "hand") && KeyWord(n, "left", "_l", " l")) return MTools.GetInstance<IKTag>("IK_LeftHand");
            if (KeyWord(n, "hand") && KeyWord(n, "right", "_r", " r")) return MTools.GetInstance<IKTag>("IK_RightHand");
            if (KeyWord(n, "foot") && KeyWord(n, "left", "_l", " l")) return MTools.GetInstance<IKTag>("IK_LeftFoot");
            if (KeyWord(n, "foot") && KeyWord(n, "right", "_r", " r")) return MTools.GetInstance<IKTag>("IK_RightFoot");

            return null;
        }

        private bool KeyWord(string name, params string[] keyword)
        {
            foreach (var item in keyword)
            {
                if (name.Contains(item.ToLower())) return true;
            }
            return false;
        }


        public void FindBonesFromTags()
        {
            foreach (var item in m.GlobalTargets)
            {
                if (item.Value == null)
                {
                    switch (item.ID.name)
                    {
                        case "head": item.Value = new TransformReference(m.transform.FindGrandChildinSkin("head")); break;
                        default: break;
                    }
                }
            }
        }


        /// <summary>True while this editor hides Unity's transform tool so only the IK Processor handles show.</summary>
        private bool hidToolsForIK;

        /// <summary>Restores Unity's transform tool if the IK handles hid it.</summary>
        private void OnDisable() => HideToolsForIK(false);

        /// <summary>Hides or restores Unity's transform tool on the character, only touching it when this editor hid it.</summary>
        private void HideToolsForIK(bool hide)
        {
            if (hide == hidToolsForIK) return;
            Tools.hidden = hide;
            hidToolsForIK = hide;
        }

        private void OnSceneGUI()
        {
            //The character's own Move/Rotate gizmo sits on top of the IK handles; hide it while they are drawn
            var drawn = false;

            if (m.sets != null && m.sets.Count > 0 && m.animator != null)
            {
                for (int k = 0; k < m.sets.Count; k++)
                {
                    var set = m.sets[k];

                    if (set != null && m.SelectedSet == k && set.active && set.Gizmos && set.Processors != null)
                    {
                        set.Owner = m; //MWC — edit-time Handles need the Owner for its Scale Multiplier (Initialize only runs in Play Mode)

                        for (int i = 0; i < set.Processors.Count; i++)
                        {
                            var link = set.Processors[i];

                            if (link != null && link.Active && set.SelectedIKProcessor == i)
                            {
                                link.OnSceneGUI(set, m.animator, target, i);
                                drawn |= link.HasSceneHandles;
                            }
                        }
                    }
                }
            }

            HideToolsForIK(drawn);
        }
    }
#endif
}