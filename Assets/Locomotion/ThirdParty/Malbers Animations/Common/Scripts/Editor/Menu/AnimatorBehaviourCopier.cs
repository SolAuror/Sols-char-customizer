// Last Change: 9/17/2026 6:05pm
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;

namespace MalbersAnimations
{
    // MWC — Copy/Paste State Machine Behaviours from one Animator State into others.
    //
    // Menus registered (same commands, 3 surfaces):
    //  • CONTEXT/AnimatorState          → right-click a State node in the Animator window
    //  • CONTEXT/StateMachineBehaviour  → the [⋮] menu on any Behaviour header in the Inspector
    //  • Tools/Malbers Animations/Animator → works on ALL the selected Animator States
    //
    // Copy:
    //  • Copy Behavior       → copies ONLY the behaviour the [⋮] menu was opened on
    //  • Copy All Behaviors  → copies every behaviour of the State
    //
    // Paste modes:
    //  • Paste Behaviors           → adds every copied behaviour (duplicates allowed)
    //  • Paste Behaviors New-Only  → adds only the behaviour types the target does not have
    //  • Paste Behaviors Override  → copies the values onto the behaviours of the same type (adds the rest)
    public static class AnimatorBehaviourCopier
    {
        //MWC — Clipboard survives domain reloads: everything is stored as GlobalObjectId strings
        private const string CK_Owner = "MalbersAnim.BehaviourClip.OwnerGID";
        private const string CK_Items = "MalbersAnim.BehaviourClip.ItemsGID";
        private const string CK_Label = "MalbersAnim.BehaviourClip.Label";
        private const char SEPARATOR = '|';

        //MWC — Fast path, valid until the next domain reload
        private static AnimatorState s_Owner;
        private static List<StateMachineBehaviour> s_Items;

        public enum PasteMode { Add, NewOnly, Override }

        #region Menu Priorities
        private const int P_CopyOne = 699;  //same group as Copy All (gap ≤ 10 = no separator)
        private const int P_Copy = 700;     //separator (gap > 10) between the Copy and the Paste groups
        private const int P_Paste = 711;
        private const int P_PasteNew = 712;
        private const int P_PasteOver = 713;
        #endregion

        #region CONTEXT/AnimatorState — right-click on the State node (Animator window)
        [MenuItem("CONTEXT/AnimatorState/Copy All Behaviors", false, P_Copy)]
        private static void CTX_Copy(MenuCommand cmd) => CopyAll(cmd.context as AnimatorState);

        [MenuItem("CONTEXT/AnimatorState/Copy All Behaviors", true)]
        private static bool CTX_Copy_Validate(MenuCommand cmd) => HasBehaviours(cmd.context as AnimatorState);

        [MenuItem("CONTEXT/AnimatorState/Paste Behaviors", false, P_Paste)]
        private static void CTX_Paste(MenuCommand cmd) => Paste(cmd.context as AnimatorState, PasteMode.Add);

        [MenuItem("CONTEXT/AnimatorState/Paste Behaviors", true)]
        private static bool CTX_Paste_Validate(MenuCommand cmd) => CanPaste(cmd.context as AnimatorState);

        [MenuItem("CONTEXT/AnimatorState/Paste Behaviors New-Only", false, P_PasteNew)]
        private static void CTX_PasteNew(MenuCommand cmd) => Paste(cmd.context as AnimatorState, PasteMode.NewOnly);

        [MenuItem("CONTEXT/AnimatorState/Paste Behaviors New-Only", true)]
        private static bool CTX_PasteNew_Validate(MenuCommand cmd) => CanPaste(cmd.context as AnimatorState);

        [MenuItem("CONTEXT/AnimatorState/Paste Behaviors Override", false, P_PasteOver)]
        private static void CTX_PasteOver(MenuCommand cmd) => Paste(cmd.context as AnimatorState, PasteMode.Override);

        [MenuItem("CONTEXT/AnimatorState/Paste Behaviors Override", true)]
        private static bool CTX_PasteOver_Validate(MenuCommand cmd) => CanPaste(cmd.context as AnimatorState);
        #endregion

        #region CONTEXT/StateMachineBehaviour — the [⋮] menu of a Behaviour in the Inspector
        //MWC — Copies ONLY the behaviour whose [⋮] menu was used
        [MenuItem("CONTEXT/StateMachineBehaviour/Copy Behavior", false, P_CopyOne)]
        private static void SMB_CopyOne(MenuCommand cmd) => CopyOne(cmd.context as StateMachineBehaviour);

        [MenuItem("CONTEXT/StateMachineBehaviour/Copy Behavior", true)]
        private static bool SMB_CopyOne_Validate(MenuCommand cmd) => cmd.context is StateMachineBehaviour;

        [MenuItem("CONTEXT/StateMachineBehaviour/Copy All Behaviors", false, P_Copy)]
        private static void SMB_Copy(MenuCommand cmd) => CopyAll(OwnerState(cmd.context as StateMachineBehaviour));

        [MenuItem("CONTEXT/StateMachineBehaviour/Copy All Behaviors", true)]
        private static bool SMB_Copy_Validate(MenuCommand cmd) => cmd.context is StateMachineBehaviour;

        [MenuItem("CONTEXT/StateMachineBehaviour/Paste Behaviors", false, P_Paste)]
        private static void SMB_Paste(MenuCommand cmd) => Paste(OwnerState(cmd.context as StateMachineBehaviour), PasteMode.Add);

        [MenuItem("CONTEXT/StateMachineBehaviour/Paste Behaviors", true)]
        private static bool SMB_Paste_Validate(MenuCommand cmd) => cmd.context is StateMachineBehaviour && HasClipboard();

        [MenuItem("CONTEXT/StateMachineBehaviour/Paste Behaviors New-Only", false, P_PasteNew)]
        private static void SMB_PasteNew(MenuCommand cmd) => Paste(OwnerState(cmd.context as StateMachineBehaviour), PasteMode.NewOnly);

        [MenuItem("CONTEXT/StateMachineBehaviour/Paste Behaviors New-Only", true)]
        private static bool SMB_PasteNew_Validate(MenuCommand cmd) => cmd.context is StateMachineBehaviour && HasClipboard();

        [MenuItem("CONTEXT/StateMachineBehaviour/Paste Behaviors Override", false, P_PasteOver)]
        private static void SMB_PasteOver(MenuCommand cmd) => Paste(OwnerState(cmd.context as StateMachineBehaviour), PasteMode.Override);

        [MenuItem("CONTEXT/StateMachineBehaviour/Paste Behaviors Override", true)]
        private static bool SMB_PasteOver_Validate(MenuCommand cmd) => cmd.context is StateMachineBehaviour && HasClipboard();
        #endregion

        #region Tools Menu — works on every selected Animator State
        [MenuItem("Tools/Malbers Animations/Animator/Copy All Behaviors", false, P_Copy)]
        private static void MENU_Copy() => CopyAll(SelectedStates().FirstOrDefault());

        [MenuItem("Tools/Malbers Animations/Animator/Copy All Behaviors", true)]
        private static bool MENU_Copy_Validate() => HasBehaviours(SelectedStates().FirstOrDefault());

        [MenuItem("Tools/Malbers Animations/Animator/Paste Behaviors", false, P_Paste)]
        private static void MENU_Paste() => PasteToSelection(PasteMode.Add);

        [MenuItem("Tools/Malbers Animations/Animator/Paste Behaviors", true)]
        private static bool MENU_Paste_Validate() => HasClipboard() && SelectedStates().Count > 0;

        [MenuItem("Tools/Malbers Animations/Animator/Paste Behaviors New-Only", false, P_PasteNew)]
        private static void MENU_PasteNew() => PasteToSelection(PasteMode.NewOnly);

        [MenuItem("Tools/Malbers Animations/Animator/Paste Behaviors New-Only", true)]
        private static bool MENU_PasteNew_Validate() => HasClipboard() && SelectedStates().Count > 0;

        [MenuItem("Tools/Malbers Animations/Animator/Paste Behaviors Override", false, P_PasteOver)]
        private static void MENU_PasteOver() => PasteToSelection(PasteMode.Override);

        [MenuItem("Tools/Malbers Animations/Animator/Paste Behaviors Override", true)]
        private static bool MENU_PasteOver_Validate() => HasClipboard() && SelectedStates().Count > 0;
        #endregion

        #region Copy
        /// <summary> Puts every behaviour of the State on the clipboard </summary>
        public static void CopyAll(AnimatorState source)
        {
            if (source == null) return;

            var behaviours = ValidBehaviours(source);
            if (behaviours.Count == 0) return;

            SetClipboard(source, behaviours);

            Debug.Log($"<color=green><b>[Copy All Behaviors]</b></color> [{source.name}] → {behaviours.Count} behaviour(s) " +
                      $"on the clipboard: {Names(behaviours)}");
        }

        /// <summary> Puts a SINGLE behaviour on the clipboard (the one the [⋮] menu was opened on) </summary>
        public static void CopyOne(StateMachineBehaviour behaviour)
        {
            if (behaviour == null) return;

            var owner = OwnerState(behaviour);
            SetClipboard(owner, new List<StateMachineBehaviour>() { behaviour });

            Debug.Log($"<color=green><b>[Copy Behavior]</b></color> [{(owner != null ? owner.name : "?")}] → " +
                      $"[{behaviour.GetType().Name}] on the clipboard");
        }

        //MWC — Stores the owner State and the behaviours as GlobalObjectIds, so the clipboard survives script recompiles
        private static void SetClipboard(AnimatorState owner, List<StateMachineBehaviour> behaviours)
        {
            s_Owner = owner;
            s_Items = behaviours;

            SessionState.SetString(CK_Owner, owner != null ? GlobalObjectId.GetGlobalObjectIdSlow(owner).ToString() : string.Empty);
            SessionState.SetString(CK_Items, string.Join(SEPARATOR.ToString(),
                behaviours.Select(x => GlobalObjectId.GetGlobalObjectIdSlow(x).ToString())));
            SessionState.SetString(CK_Label, Names(behaviours));
        }
        #endregion

        #region Paste
        /// <summary> Pastes the clipboard behaviours into all the selected Animator States </summary>
        public static void PasteToSelection(PasteMode mode)
        {
            foreach (var state in SelectedStates()) Paste(state, mode);
        }

        /// <summary> Pastes the clipboard behaviours into a single Animator State </summary>
        public static void Paste(AnimatorState target, PasteMode mode)
        {
            if (target == null) return;

            var clipboard = ClipboardBehaviours();

            if (clipboard.Count == 0)
            {
                Debug.LogWarning("[Paste Behaviors] The clipboard is empty or the copied behaviours no longer exist.");
                return;
            }

            if (OwnerState() == target) return; //never paste on the State the behaviours were copied from

            Undo.RegisterCompleteObjectUndo(target, "Paste Animator Behaviors");

            int added = 0, overriden = 0, skipped = 0;

            switch (mode)
            {
                case PasteMode.Add:
                    foreach (var b in clipboard)
                        if (AddCopy(target, b) != null) added++;
                    break;

                case PasteMode.NewOnly:
                    var existingTypes = new HashSet<Type>(ValidBehaviours(target).Select(x => x.GetType()));

                    foreach (var b in clipboard)
                    {
                        if (!existingTypes.Add(b.GetType())) { skipped++; continue; } //the target already has this type
                        if (AddCopy(target, b) != null) added++;
                    }
                    break;

                case PasteMode.Override:
                    var candidates = ValidBehaviours(target);
                    var used = new HashSet<StateMachineBehaviour>();

                    foreach (var b in clipboard)
                    {
                        //Pair them in order, so 2 behaviours of the same type override the 1st and the 2nd one
                        var match = candidates.FirstOrDefault(x => x.GetType() == b.GetType() && !used.Contains(x));

                        if (match != null)
                        {
                            used.Add(match);
                            Undo.RecordObject(match, "Paste Animator Behaviors");
                            EditorUtility.CopySerialized(b, match);
                            EditorUtility.SetDirty(match);
                            overriden++;
                        }
                        else if (AddCopy(target, b) != null) added++;
                    }
                    break;
            }

            EditorUtility.SetDirty(target);
            InternalEditorUtility.RepaintAllViews();

            Debug.Log($"<color=green><b>[Paste Behaviors ({mode})]</b></color> {SessionState.GetString(CK_Label, string.Empty)} " +
                      $"→ [{target.name}]. Added: {added}. Overriden: {overriden}. Skipped: {skipped}", target);
        }

        //MWC — Creates a new behaviour of the same type on the target and copies all the serialized values into it
        private static StateMachineBehaviour AddCopy(AnimatorState target, StateMachineBehaviour source)
        {
            var copy = target.AddStateMachineBehaviour(source.GetType());

            if (copy == null)
            {
                Debug.LogWarning($"[Paste Behaviors] Could not create a [{source.GetType().Name}] on [{target.name}]");
                return null;
            }

            EditorUtility.CopySerialized(source, copy);
            Undo.RegisterCreatedObjectUndo(copy, "Paste Animator Behaviors");
            EditorUtility.SetDirty(copy);
            return copy;
        }
        #endregion

        #region Clipboard
        //MWC — Resolves the copied behaviours: session references first, then the persistent GlobalObjectIds
        private static List<StateMachineBehaviour> ClipboardBehaviours()
        {
            if (s_Items != null && s_Items.Count > 0 && s_Items.TrueForAll(x => x != null)) return s_Items;

            s_Items = new List<StateMachineBehaviour>();

            var stored = SessionState.GetString(CK_Items, string.Empty);
            if (string.IsNullOrEmpty(stored)) return s_Items;

            foreach (var id in stored.Split(SEPARATOR))
            {
                if (Resolve(id) is StateMachineBehaviour b) s_Items.Add(b);
            }

            return s_Items;
        }

        //MWC — Resolves the State the behaviours were copied from (used to block pasting on itself)
        private static AnimatorState OwnerState()
        {
            if (s_Owner != null) return s_Owner;

            s_Owner = Resolve(SessionState.GetString(CK_Owner, string.Empty)) as AnimatorState;
            return s_Owner;
        }

        private static UnityEngine.Object Resolve(string globalID)
        {
            if (string.IsNullOrEmpty(globalID)) return null;
            if (!GlobalObjectId.TryParse(globalID, out var parsed)) return null;

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed);
        }

        private static bool HasClipboard() => ClipboardBehaviours().Count > 0;

        private static bool CanPaste(AnimatorState state)
        {
            if (state == null || !HasClipboard()) return false;
            return OwnerState() != state; //hide the Paste entries on the State the behaviours were copied from
        }
        #endregion

        #region Utils
        private static bool HasBehaviours(AnimatorState state) => state != null && ValidBehaviours(state).Count > 0;

        //MWC — Behaviours can be null when their script is missing
        private static List<StateMachineBehaviour> ValidBehaviours(AnimatorState state)
        {
            var result = new List<StateMachineBehaviour>();
            if (state == null || state.behaviours == null) return result;

            foreach (var b in state.behaviours)
                if (b != null) result.Add(b);

            return result;
        }

        private static string Names(List<StateMachineBehaviour> behaviours) =>
            string.Join(", ", behaviours.Select(x => x.GetType().Name));

        private static List<AnimatorState> SelectedStates() =>
            Selection.GetFiltered<AnimatorState>(SelectionMode.Editable).ToList();

        //MWC — Finds the Animator State that owns a behaviour (used by the [⋮] menu of the Inspector)
        private static AnimatorState OwnerState(StateMachineBehaviour behaviour)
        {
            if (behaviour == null) return null;

            //The selected State is almost always the owner - check it first
            var selected = SelectedStates().FirstOrDefault();
            if (selected != null && Array.IndexOf(selected.behaviours, behaviour) >= 0) return selected;

            var path = AssetDatabase.GetAssetPath(behaviour); //null on runtime instances (Play Mode)
            if (string.IsNullOrEmpty(path)) return null;

            //MWC — Fully qualified: UnityEditorInternal also declares an AnimatorController type
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
            if (controller == null) return null;

            foreach (var layer in controller.layers)
                foreach (var state in AllStates(layer.stateMachine))
                    if (Array.IndexOf(state.behaviours, behaviour) >= 0) return state;

            return null;
        }

        //MWC — All the States of a State Machine, including the ones inside Sub-State Machines
        private static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine sm)
        {
            if (sm == null) yield break;

            foreach (var child in sm.states) yield return child.state;

            foreach (var child in sm.stateMachines)
                foreach (var state in AllStates(child.stateMachine))
                    yield return state;
        }
        #endregion
    }
}
#endif
