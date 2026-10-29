// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Conditions;
using MalbersAnimations.IK;
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Items
{
    [System.Serializable]
    public class ItemActionSet
    {
        [Tooltip("Name of the Action Set. This value is used for the changing the Current Action Set")]
        public StringReference name = new("New Action Set");
        [Tooltip("Set the Current Action set to be the active set")]
        [SerializeField] private bool active;     //If the Action Set is active or not

        [Tooltip("Set the Current Action set to be the Default set to return to")]
        [SerializeField] private bool m_Default; //If the Action Set is active or not
        public bool Active { get => active; set => active = value; }
        public bool Default { get => m_Default; set => m_Default = value; }

        //MWC — Ignored Sets are invisible to every form of Auto Activation, and the Item leaves them for the Default
        //Set if one is active when the flag is raised. Use it to park a Set that is not finished or not wanted yet
        //without deleting it or clearing its Auto Activation flags.
        [Tooltip("Ignore this Action Set.\n\n" +
            "It is skipped by EVERY form of Auto Activation, and if it is the Active Set the Item returns to the Default Set.\n" +
            "The Default Set can NOT be ignored: the flag has no effect there")]
        [SerializeField] private bool m_Ignore;

        /// <summary>Raw [Ignore] flag as authored. Use <see cref="Ignored"/> to test it.</summary>
        public bool Ignore { get => m_Ignore; set => m_Ignore = value; }

        /// <summary>True when this Set must be skipped by every Auto Activation. The Default Set is never ignored.</summary>
        public bool Ignored => m_Ignore && !m_Default;

        [Tooltip("Type to Auto Activate this Action Set")]
        [Flag] public SetActivationType AutoActivation;

        [Tooltip("Conditions to Auto Activate this Action Set")]
        public Conditions2 AutoActivationOn;

        [Tooltip("Conditions to Return to the default Action Set ")]
        public Conditions2 AutoActivationOff;

        public List<ItemAction> actions = new();

        public Reaction2 OnEnterSetReaction;
        public Reaction2 OnExitSetReaction;

        public Reaction2 OnUnequipReaction;
        public Reaction2 OnEquipReaction;

        //MWC — Reaction fired once when the Set is interrupted by a State/Mode/Stance (see the Interrupt lists below)
        [Tooltip("Called when the Set is interrupted by an Interrupt State, Mode or Stance")]
        public Reaction2 OnInterruptReaction;

        #region MWC — Interruption Sources
        //The Character entering one of these States/Modes/Stances force-ends every playing Action of this Set.
        //Include [✓] = the listed IDs interrupt. Exclude [✕] = everything EXCEPT the listed IDs interrupts
        //(e.g. exclude the Set's own Reload mode so only foreign modes break the reload). An empty list never interrupts.

        [Tooltip("States that interrupt this Set when the Character enters them.\n" +
            "[✓ Include]: only the listed States interrupt.\n" +
            "[✕ Exclude]: every State EXCEPT the listed ones interrupts.\nEmpty = never interrupt")]
        public IDListCheck<StateID> InterruptStates = new();

        [Tooltip("Modes that interrupt this Set when the Character starts them.\n" +
            "[✓ Include]: only the listed Modes interrupt.\n" +
            "[✕ Exclude]: every Mode EXCEPT the listed ones interrupts (use it to skip the Set's own Modes).\nEmpty = never interrupt")]
        public IDListCheck<ModeID> InterruptModes = new();

        [Tooltip("Stances that interrupt this Set when the Character enters them.\n" +
            "[✓ Include]: only the listed Stances interrupt.\n" +
            "[✕ Exclude]: every Stance EXCEPT the listed ones interrupts.\nEmpty = never interrupt")]
        public IDListCheck<StanceID> InterruptStances = new();

        /// <summary>True when this Set has at least one interruption source configured</summary>
        public bool HasInterruptions => InterruptStates.Valid || InterruptModes.Valid || InterruptStances.Valid;

        /// <summary>Does entering this State ID interrupt the Set?</summary>
        public bool InterruptedByState(int stateID) => InterruptStates.Check(stateID);

        /// <summary>Does starting this Mode ID interrupt the Set?</summary>
        public bool InterruptedByMode(int modeID) => InterruptModes.Check(modeID);

        /// <summary>Does entering this Stance ID interrupt the Set?</summary>
        public bool InterruptedByStance(int stanceID) => InterruptStances.Check(stanceID);
        #endregion

        [Tooltip("Conditions to Use validate all actions in the set")]
        public Conditions2 SetConditions;


        //public GameObjectEvent OnEnterSet = new();
        //public GameObjectEvent OnExitSet = new();

        [SerializeField, HideInInspector]
        private int SelectedAction, EditorTab1;

        [Tooltip("Default IK Set Profile of this Action Set. It is authored data and is NEVER modified at runtime: " +
            "the character's IK Manager can remap it to a rig specific Profile (see IKSetRemap)")]
        public IKSetProfile IKProfile;

        [Tooltip("Dynamic IK Targets to be added  to the IK manager when the Set is Active")]
        public List<IDPair<IKTag, TransformReference>> IKTargets = new();

        #region IK Profile Resolution

        // MWC — [IKProfile] used to be overwritten in SuperItem.Awake() by its Runtime copy, which destroyed the
        // identity of the authored asset: once it pointed at a clone there was no key left to look up on the owner's
        // Remap, and an Item changing hands could never re-resolve. The authored field stays untouched now and the
        // value the IK pass uses lives here. Same shape as MWeaponManager.CurrentIKProfile.

        /// <summary>Profile resolved for the current owner (rig override + Runtime copy). Null until the Set is entered.</summary>
        public IKSetProfile ActiveIKProfile { get; private set; }

        /// <summary>Profile the IK pass must use: the resolved one, falling back to the authored one.</summary>
        public IKSetProfile CurrentIKProfile => ActiveIKProfile != null ? ActiveIKProfile : IKProfile;

        /// <summary>IK Manager the current [ActiveIKProfile] was resolved for, so it is resolved once per owner.</summary>
        private IKManager resolvedFor;

        /// <summary>True when [ActiveIKProfile] is a Runtime copy this Set created and therefore owns.</summary>
        private bool ownsRuntimeCopy;

        /// <summary>True when the resolved Profile came from the owner's rig Remap instead of the authored one.</summary>
        public bool IKProfileRemapped { get; private set; }

        /// <summary>True when [ActiveIKProfile] is a Runtime copy owned by this Set (Inspector info while playing).</summary>
        public bool IKProfileIsRuntimeCopy => ownsRuntimeCopy && ActiveIKProfile != null;

        /// <summary>True when the Remap forced the Runtime copy on an Item that has [Runtime IK] disabled.</summary>
        public bool RuntimeCopyForced { get; private set; }

        /// <summary>Resolves the authored Profile against the owner's rig Remap. Cached per IK Manager.</summary>
        /// <param name="manager">IK Manager of the character holding the Item.</param>
        /// <param name="runtimeCopy">Instantiate the resolved Profile so its Set is not shared between Items.</param>
        public IKSetProfile ResolveIKProfile(IKManager manager, bool runtimeCopy)
        {
            if (IKProfile == null) { ReleaseIKProfile(); return null; }

            if (manager == null) return CurrentIKProfile; //No owner yet: keep using the authored Profile

            if (resolvedFor == manager && ActiveIKProfile != null) return ActiveIKProfile; //Already resolved for this character

            ReleaseIKProfile(); //A previous owner's copy belongs to a different rig, it cannot be reused

            var resolved = manager.ResolveProfile(IKProfile);

            IKProfileRemapped = resolved != null && resolved != IKProfile;

            // MWC — a remapped Profile is a rig asset shared by EVERY character of that rig, and the IK/Weight
            // Processors inside it cache per character state (Bone, bone lengths, IKSet.Owner, FinalWeight, and the
            // Mode/Stance/State Weight Processors even subscribe to the character's events), so a shared one cross
            // wires them. BUT [Runtime IK] off is also the authoring switch: it is how offsets tuned in Play Mode
            // land back in the asset. The Editor therefore honors it (the tuning pass is single character by hand,
            // and WarnIfShared below catches it when it is not); a build never tunes anything, so there the copy is
            // forced and a forgotten toggle cannot ship as broken IK.
            var forceCopy = IKProfileRemapped && !Application.isEditor;

            RuntimeCopyForced = forceCopy && !runtimeCopy;

            if ((runtimeCopy || forceCopy) && resolved != null)
            {
                resolved = Object.Instantiate(resolved);
                resolved.name = resolved.name.Replace("(Clone)", "(Runtime)");
                ownsRuntimeCopy = true;
            }
#if UNITY_EDITOR
            else WarnIfShared(resolved, manager); //The asset itself is in use: warn if a second character gets it too
#endif

            ActiveIKProfile = resolved;
            resolvedFor = manager;

            return ActiveIKProfile;
        }

        /// <summary>Drops the resolved Profile, destroying the Runtime copy when this Set created it.</summary>
        public void ReleaseIKProfile()
        {
            if (ownsRuntimeCopy && ActiveIKProfile != null && Application.isPlaying)
                Object.Destroy(ActiveIKProfile);

            ownsRuntimeCopy = false;
            IKProfileRemapped = false;
            RuntimeCopyForced = false;
            ActiveIKProfile = null;
            resolvedFor = null;
        }

#if UNITY_EDITOR
        // MWC — the Editor lets an Item use the Profile asset itself (that is the tuning pass, see ResolveIKProfile).
        // That is only safe while ONE character holds it, and the failure is silent otherwise, so it is reported here.

        /// <summary>Editor only: first character that received each un-copied Profile.</summary>
        private static readonly Dictionary<IKSetProfile, IKManager> s_SharedProfiles = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearSharedProfiles() => s_SharedProfiles.Clear();

        /// <summary>Editor only: warns when a second character receives the same un-copied Profile asset.</summary>
        private static void WarnIfShared(IKSetProfile profile, IKManager manager)
        {
            if (profile == null || manager == null) return;

            if (s_SharedProfiles.TryGetValue(profile, out var first))
            {
                if (first != null && first != manager)
                {
                    Debug.LogWarning($"The IK Set Profile <B>[{profile.name}]</B> is used <B>un-copied</B> by " +
                        $"[{first.name}] and [{manager.name}] at the same time. The IK Processors cache per character state " +
                        $"(bones, Owner, weights, events), so both characters will fight over it. " +
                        $"Turn <B>[Runtime]</B> back on for this Item when you are done tuning the offsets.", profile);

                    s_SharedProfiles[profile] = null; //Reported once, do not spam every re-resolve
                }
            }
            else s_SharedProfiles[profile] = manager;
        }
#endif

        #endregion

        public string Name => name.Value;

        public ItemActionSet(string name) => this.name.Value = name;

        internal ItemAction GetActionByName(string requiredActionName)
        {
            foreach (var action in actions)
            {
                if (action.name == requiredActionName)
                    return action;
            }
            return null;
        }

        /// <summary>  Get the first processor of the specified type. This is used to access the processors from the Action without having to know the index of the processor in the array.  </summary>
        /// <typeparam name="T"> Type of the processor to get</typeparam>
        /// <returns> The first processor of the specified type, or null if no processor of that type is found</returns>
        internal T GetProcessor<T>(string actionName) where T : ItemProcessor
        {
            var Action = GetActionByName(actionName);
            return Action?.GetProcessor<T>();
        }
    }

    public enum SetActivationType
    {
        CheckState = 1 << 0,
        CheckStance = 1 << 1,
        CheckMode = 1 << 2,
        Interactor = 1 << 3,
        TargetChanged = 1 << 4,
        OnEquip = 1 << 5,
        OnUnequip = 1 << 6,
        OnAction = 1 << 7,
        External = 1 << 8,
        Riding = 1 << 9, // Any Rider notification (Mount/Dismount, Mount Trigger, Call Mount) re-checks the Set
    }

    //MWC — Hands an Item needs beyond the one physically holding it. Two-handed weapons (Bow, Rifle, 2H Axe, Spear)
    //keep the off hand on the shaft/string, so it is not available for anything else (today: the Riding rein).
    //Declared per Action Set (held for as long as the Set is active) and per Action (only while the Action plays).
    //It only ever ADDS to the hand the Equip Point already gives the Item — it can never give one back.
    public enum ItemHands
    {
        Left = 1 << 0,
        Right = 1 << 1,
    }
}
