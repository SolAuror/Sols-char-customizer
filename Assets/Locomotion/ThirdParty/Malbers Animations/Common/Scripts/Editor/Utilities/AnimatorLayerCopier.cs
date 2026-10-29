// Last Change: 9/17/2026 6:05pm
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;


namespace MalbersAnimations
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  Animator Layer Copier
    //  Right-click any Animator Controller property field (e.g. the Animator
    //  component's "Controller" slot) to copy a layer, then right-click another
    //  controller field to paste it in as a new layer.
    //
    //  • Copy   – any layer, listed in a "Copy Layer" sub-menu
    //  • Paste  – always appended as a NEW layer; name gets a numeric suffix
    //             when the destination already uses it
    //  • Clipboard persists across domain reloads via SessionState
    //  • One console entry per paste — summary on the first line, every copied
    //    transition (source → destination + conditions) in the detail body
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary> SessionState-backed clipboard holding a reference to one Animator Controller layer. </summary>
    internal static class AnimatorLayerClipboard
    {
        private const string CK_Path = "MalbersAnim.LayerClip.CtrlPath";
        private const string CK_Idx = "MalbersAnim.LayerClip.LayerIdx";

        /// <summary> Stores the controller path + layer index of the copied layer. </summary>
        internal static void Copy(AnimatorController ctrl, int layerIndex)
        {
            var path = AssetDatabase.GetAssetPath(ctrl);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("Animator Layer Copier: the controller is not saved as an asset – nothing copied.");
                return;
            }

            SessionState.SetString(CK_Path, path);
            SessionState.SetInt(CK_Idx, layerIndex);
            Debug.Log($"Animator Layer Copier: copied layer '{ctrl.layers[layerIndex].name}' from '{ctrl.name}'.", ctrl);
        }

        /// <summary> Loads the stored controller + layer index, returning false when the clipboard is empty or stale. </summary>
        internal static bool Resolve(out AnimatorController ctrl, out int layerIndex)
        {
            layerIndex = SessionState.GetInt(CK_Idx, -1);
            ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(SessionState.GetString(CK_Path, ""));
            return ctrl != null && (uint)layerIndex < (uint)ctrl.layers.Length;
        }

        /// <summary> Empties the clipboard. </summary>
        internal static void Clear()
        {
            SessionState.EraseString(CK_Path);
            SessionState.EraseInt(CK_Idx);
        }
    }

    /// <summary> Injects Copy / Paste Layer entries into the context menu of any AnimatorController property field. </summary>
    internal static class AnimatorLayerContextMenu
    {
        /// <summary> Subscribes to Unity's property context menu callback on editor load. </summary>
        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.contextualPropertyMenu -= OnPropertyContextMenu;
            EditorApplication.contextualPropertyMenu += OnPropertyContextMenu;
        }

        /// <summary> Builds the Copy Layer sub-menu + Paste Layer entry for the right-clicked controller. </summary>
        private static void OnPropertyContextMenu(GenericMenu menu, SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference) return;
            if (property.objectReferenceValue is not AnimatorController ctrl) return;

            // The property is disposed after this callback — only the controller is captured below
            var layers = ctrl.layers;
            if (layers.Length == 0) return;

            menu.AddSeparator("");

            for (int i = 0; i < layers.Length; i++)
            {
                int index = i;
                menu.AddItem(new GUIContent($"Copy Layer/{i} - {Safe(layers[i].name)}"), false,
                    () => AnimatorLayerClipboard.Copy(ctrl, index));
            }

            if (AnimatorLayerClipboard.Resolve(out var srcCtrl, out int srcIdx))
            {
                var srcName = srcCtrl.layers[srcIdx].name;
                menu.AddItem(new GUIContent($"Paste Layer [{Safe(srcName)}]"), false,
                    () => AnimatorLayerCopy.PasteAsNewLayer(srcCtrl, srcIdx, ctrl));
                menu.AddItem(new GUIContent("Clear Layer Clipboard"), false, AnimatorLayerClipboard.Clear);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste Layer  ─  clipboard empty"));
            }
        }

        /// <summary> Strips '/' so a layer name never splits the menu item into a sub-menu. </summary>
        private static string Safe(string name) => string.IsNullOrEmpty(name) ? "(unnamed)" : name.Replace('/', '∕');

        // ─── Project window (right-click a .controller asset) ──────────────────────
        private const string AssetMenu = "Assets/Malbers Animations/";

        /// <summary> Drops down the selected controller's layer list so one can be copied. </summary>
        [MenuItem(AssetMenu + "Copy Animator Layer", false, 1100)]
        private static void CopyLayerFromAsset()
        {
            if (Selection.activeObject is not AnimatorController ctrl) return;

            var menu = new GenericMenu();
            var layers = ctrl.layers;

            for (int i = 0; i < layers.Length; i++)
            {
                int index = i;
                menu.AddItem(new GUIContent($"{i} - {Safe(layers[i].name)}"), false,
                    () => AnimatorLayerClipboard.Copy(ctrl, index));
            }
            menu.ShowAsContext();
        }

        /// <summary> Enables Copy Animator Layer only for a selected controller that actually has layers. </summary>
        [MenuItem(AssetMenu + "Copy Animator Layer", true)]
        private static bool CopyLayerFromAssetValidate()
            => Selection.activeObject is AnimatorController c && c.layers.Length > 0;

        /// <summary> Pastes the clipboard layer into the selected controller asset as a new layer. </summary>
        [MenuItem(AssetMenu + "Paste Animator Layer", false, 1101)]
        private static void PasteLayerToAsset()
        {
            if (Selection.activeObject is AnimatorController dest
             && AnimatorLayerClipboard.Resolve(out var src, out int srcIdx))
                AnimatorLayerCopy.PasteAsNewLayer(src, srcIdx, dest);
        }

        /// <summary> Enables Paste Animator Layer only when a controller is selected and the clipboard is valid. </summary>
        [MenuItem(AssetMenu + "Paste Animator Layer", true)]
        private static bool PasteLayerToAssetValidate()
            => Selection.activeObject is AnimatorController && AnimatorLayerClipboard.Resolve(out _, out _);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    //  Copy core
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary> Deep-copies one layer — states, sub-state machines, blend trees, behaviours and every transition. </summary>
    internal class AnimatorLayerCopy
    {
        private readonly Dictionary<AnimatorState, AnimatorState> _stateMap = new();
        private readonly Dictionary<AnimatorStateMachine, AnimatorStateMachine> _smMap = new();
        private readonly List<string> _log = new();
        private AnimatorController _dest;
        private int _tState, _tAny, _tEntry, _tSM;

        // ─── Entry point ──────────────────────────────────────────────────────────
        /// <summary> Appends the source layer to dest as a new layer, suffixing the name when it is already taken. </summary>
        internal static void PasteAsNewLayer(AnimatorController src, int srcIndex, AnimatorController dest)
        {
            if (src == null || dest == null) return;
            if ((uint)srcIndex >= (uint)src.layers.Length) return;

            Undo.RegisterCompleteObjectUndo(dest, "Paste Animator Layer");

            var newName = UniqueLayerName(dest, src.layers[srcIndex].name);
            dest.AddLayer(newName);                          // AddLayer always appends at the end

            new AnimatorLayerCopy().Run(src, srcIndex, dest, dest.layers.Length - 1, newName);
        }

        /// <summary> Returns baseName, suffixed with a number when the controller already uses it. </summary>
        private static string UniqueLayerName(AnimatorController ctrl, string baseName)
        {
            var used = new HashSet<string>(ctrl.layers.Select(l => l.name));
            if (!used.Contains(baseName)) return baseName;

            for (int i = 1; ; i++)
            {
                var candidate = $"{baseName} {i}";
                if (!used.Contains(candidate)) return candidate;
            }
        }

        // ─── Operation ────────────────────────────────────────────────────────────
        /// <summary> Fills the freshly created destination layer from the source layer. </summary>
        private void Run(AnimatorController src, int srcIndex, AnimatorController dest, int destIndex, string destName)
        {
            _dest = dest;
            var srcLayer = src.layers[srcIndex];

            // Copy missing parameters if cross-controller
            if (src != dest) CopyParameters(src, dest);

            // Build the destination state machine (the new layer starts empty)
            var destSM = dest.layers[destIndex].stateMachine;
            CopyStateMachineStructure(srcLayer.stateMachine, destSM);

            // Wire all transitions
            CopyTransitionsRecursive(srcLayer.stateMachine, destSM);

            // Default state
            if (srcLayer.stateMachine.defaultState != null
             && _stateMap.TryGetValue(srcLayer.stateMachine.defaultState, out var defState))
                destSM.defaultState = defState;

            // Layer-level settings (keep the unique name assigned at creation)
            var arr = dest.layers;
            var destLayer = arr[destIndex];
            CopyLayerSettings(srcLayer, ref destLayer);
            destLayer.name = destName;

            // A synced index from the source rarely maps here — drop it instead of writing a bogus link
            if (destLayer.syncedLayerIndex >= 0
             && (destLayer.syncedLayerIndex >= arr.Length || destLayer.syncedLayerIndex == destIndex))
            {
                _log.Add($"⚠  Synced Layer index {destLayer.syncedLayerIndex} is invalid here – set to None.");
                destLayer.syncedLayerIndex = -1;
            }

            arr[destIndex] = destLayer;
            dest.layers = arr;

            EditorUtility.SetDirty(dest);
            AssetDatabase.SaveAssets();

            // One console entry: summary first, details in the body
            var summary =
                $"Animator Layer Copier: pasted '{srcLayer.name}' ({src.name})  →  '{destName}' ({dest.name})\n" +
                $"   States: {_stateMap.Count}   Sub-SMs: {_smMap.Count - 1}\n" +
                $"   Transitions — State: {_tState}   AnyState: {_tAny}   Entry: {_tEntry}   Sub-SM: {_tSM}";

            Debug.Log(_log.Count > 0 ? $"{summary}\n{string.Join("\n", _log)}" : summary, dest);
        }

        // ─── Parameters ───────────────────────────────────────────────────────────
        /// <summary> Adds every source parameter missing from the destination, logging type mismatches. </summary>
        private void CopyParameters(AnimatorController src, AnimatorController dst)
        {
            var dstMap = dst.parameters.ToDictionary(p => p.name);
            foreach (var p in src.parameters)
            {
                if (dstMap.TryGetValue(p.name, out var ex))
                {
                    if (ex.type != p.type)
                        _log.Add($"⚠  Parameter type mismatch: '{p.name}'  ({ex.type} in dest vs {p.type} in source) – skipped.");
                    continue;
                }
                dst.AddParameter(p.name, p.type);
                _log.Add($"   + Parameter  '{p.name}'  ({p.type})");
            }
        }

        // ─── Layer settings ───────────────────────────────────────────────────────
        /// <summary> Copies mask, blending, weight, IK and sync settings from one layer to another. </summary>
        private static void CopyLayerSettings(AnimatorControllerLayer src, ref AnimatorControllerLayer dst)
        {
            dst.avatarMask = src.avatarMask;
            dst.blendingMode = src.blendingMode;
            dst.defaultWeight = src.defaultWeight;
            dst.iKPass = src.iKPass;
            dst.syncedLayerAffectsTiming = src.syncedLayerAffectsTiming;
            dst.syncedLayerIndex = src.syncedLayerIndex;
        }

        // ─── State machine structure ──────────────────────────────────────────────
        /// <summary> Recreates states and sub-state machines depth-first, filling the source → destination maps. </summary>
        private void CopyStateMachineStructure(AnimatorStateMachine src, AnimatorStateMachine dst)
        {
            _smMap[src] = dst;

            dst.anyStatePosition = src.anyStatePosition;
            dst.entryPosition = src.entryPosition;
            dst.exitPosition = src.exitPosition;

            // Sub-state machines (depth-first)
            foreach (var child in src.stateMachines)
            {
                var dstChild = dst.AddStateMachine(child.stateMachine.name, child.position);
                CopyStateMachineStructure(child.stateMachine, dstChild);
            }

            // States
            foreach (var child in src.states)
            {
                var s = child.state;
                var dstS = dst.AddState(s.name, child.position);

                dstS.speed = s.speed;
                dstS.speedParameter = s.speedParameter;
                dstS.speedParameterActive = s.speedParameterActive;
                dstS.cycleOffset = s.cycleOffset;
                dstS.cycleOffsetParameter = s.cycleOffsetParameter;
                dstS.cycleOffsetParameterActive = s.cycleOffsetParameterActive;
                dstS.mirror = s.mirror;
                dstS.mirrorParameter = s.mirrorParameter;
                dstS.mirrorParameterActive = s.mirrorParameterActive;
                dstS.iKOnFeet = s.iKOnFeet;
                dstS.writeDefaultValues = s.writeDefaultValues;
                dstS.tag = s.tag;
                dstS.motion = CopyMotion(s.motion);

                foreach (var b in s.behaviours)
                {
                    var copy = dstS.AddStateMachineBehaviour(b.GetType());
                    if (copy != null) EditorUtility.CopySerialized(b, copy);
                }

                _stateMap[s] = dstS;
            }
        }

        // ─── Motion / Blend Tree ──────────────────────────────────────────────────
        /// <summary> Returns the clip as-is; blend trees are deep-cloned into the destination controller. </summary>
        private Motion CopyMotion(Motion src)
        {
            if (src == null) return null;
            if (src is BlendTree tree) return CopyBlendTree(tree);
            return src;
        }

        /// <summary> Clones a blend tree (and its children) as a sub-asset of the destination controller. </summary>
        private BlendTree CopyBlendTree(BlendTree src)
        {
            var dst = new BlendTree
            {
                name = src.name,
                blendType = src.blendType,
                blendParameter = src.blendParameter,
                blendParameterY = src.blendParameterY,
                minThreshold = src.minThreshold,
                maxThreshold = src.maxThreshold,
                useAutomaticThresholds = src.useAutomaticThresholds
            };

            AssetDatabase.AddObjectToAsset(dst, _dest);

            var srcChildren = src.children;
            var dstChildren = new ChildMotion[srcChildren.Length];
            for (int i = 0; i < srcChildren.Length; i++)
            {
                dstChildren[i] = srcChildren[i];
                dstChildren[i].motion = CopyMotion(srcChildren[i].motion);
            }
            dst.children = dstChildren;
            return dst;
        }

        // ─── Transitions (recursive) ──────────────────────────────────────────────
        /// <summary> Rebuilds State, AnyState, Entry and Sub-SM transitions, then recurses into sub-state machines. </summary>
        private void CopyTransitionsRecursive(AnimatorStateMachine srcSM, AnimatorStateMachine dstSM)
        {
            // 1. State → state / SM / exit
            foreach (var child in srcSM.states)
            {
                if (!_stateMap.TryGetValue(child.state, out var dstState)) continue;
                foreach (var t in child.state.transitions)
                {
                    var newT = ResolveStateTrans(t, dstState);
                    if (newT != null)
                    {
                        ApplyStateTrans(t, newT);
                        _tState++;
                        _log.Add($"   [State]     '{child.state.name}'  →  '{DestLabel(t)}'  {FmtConds(t.conditions)}");
                    }
                    else _log.Add($"⚠  Could not resolve transition from '{child.state.name}'.");
                }
            }

            // 2. AnyState → state / SM
            foreach (var t in srcSM.anyStateTransitions)
            {
                var newT = ResolveAnyStateTrans(t, dstSM);
                if (newT != null)
                {
                    ApplyStateTrans(t, newT);
                    _tAny++;
                    _log.Add($"   [AnyState]  '{srcSM.name}'  →  '{DestLabel(t)}'  {FmtConds(t.conditions)}");
                }
                else _log.Add($"⚠  AnyState transition unresolvable in '{srcSM.name}'.");
            }

            // 3. Entry → state / SM
            foreach (var t in srcSM.entryTransitions)
            {
                var newT = ResolveEntryTrans(t, dstSM);
                if (newT != null)
                {
                    ApplyBaseTrans(t, newT);
                    _tEntry++;
                    _log.Add($"   [Entry]     '{srcSM.name}'  →  '{DestLabel(t)}'  {FmtConds(t.conditions)}");
                }
                else _log.Add($"⚠  Entry transition unresolvable in '{srcSM.name}'.");
            }

            // 4. Sub-SM → state / SM / exit
            foreach (var child in srcSM.stateMachines)
            {
                if (!_smMap.TryGetValue(child.stateMachine, out var dstChild)) continue;
                foreach (var t in srcSM.GetStateMachineTransitions(child.stateMachine))
                {
                    var newT = ResolveSMTrans(t, dstSM, dstChild);
                    if (newT != null)
                    {
                        ApplyBaseTrans(t, newT);
                        _tSM++;
                        _log.Add($"   [Sub-SM]    '{child.stateMachine.name}'  →  '{DestLabel(t)}'  {FmtConds(t.conditions)}");
                    }
                    else _log.Add($"⚠  Sub-SM transition unresolvable from '{child.stateMachine.name}'.");
                }
            }

            // 5. Recurse into sub-SMs
            foreach (var child in srcSM.stateMachines)
            {
                if (_smMap.TryGetValue(child.stateMachine, out var dstChild))
                    CopyTransitionsRecursive(child.stateMachine, dstChild);
            }
        }

        // ─── Transition resolvers ─────────────────────────────────────────────────
        /// <summary> Creates the mapped outgoing transition for a state, or null when the destination is unmapped. </summary>
        private AnimatorStateTransition ResolveStateTrans(AnimatorStateTransition t, AnimatorState dst)
        {
            if (t.isExit) return dst.AddExitTransition();
            if (t.destinationState != null && _stateMap.TryGetValue(t.destinationState, out var ds)) return dst.AddTransition(ds);
            if (t.destinationStateMachine != null && _smMap.TryGetValue(t.destinationStateMachine, out var dsm)) return dst.AddTransition(dsm);
            return null;
        }

        /// <summary> Creates the mapped AnyState transition, or null when the destination is unmapped. </summary>
        private AnimatorStateTransition ResolveAnyStateTrans(AnimatorStateTransition t, AnimatorStateMachine dstSM)
        {
            if (t.destinationState != null && _stateMap.TryGetValue(t.destinationState, out var ds)) return dstSM.AddAnyStateTransition(ds);
            if (t.destinationStateMachine != null && _smMap.TryGetValue(t.destinationStateMachine, out var dsm)) return dstSM.AddAnyStateTransition(dsm);
            return null;
        }

        /// <summary> Creates the mapped Entry transition, or null when the destination is unmapped. </summary>
        private AnimatorTransition ResolveEntryTrans(AnimatorTransition t, AnimatorStateMachine dstSM)
        {
            if (t.destinationState != null && _stateMap.TryGetValue(t.destinationState, out var ds)) return dstSM.AddEntryTransition(ds);
            if (t.destinationStateMachine != null && _smMap.TryGetValue(t.destinationStateMachine, out var dsm)) return dstSM.AddEntryTransition(dsm);
            return null;
        }

        /// <summary> Creates the mapped sub-state-machine transition, or null when the destination is unmapped. </summary>
        private AnimatorTransition ResolveSMTrans(AnimatorTransition t, AnimatorStateMachine dstParent, AnimatorStateMachine dstSrc)
        {
            //MWC: must be AddStateMachineExitTransition – AddStateMachineTransition(src) makes a destination-less
            //     transition that Unity drops on save, which silently lost every Sub-SM → Exit link
            if (t.isExit) return dstParent.AddStateMachineExitTransition(dstSrc);
            if (t.destinationState != null && _stateMap.TryGetValue(t.destinationState, out var ds)) return dstParent.AddStateMachineTransition(dstSrc, ds);
            if (t.destinationStateMachine != null && _smMap.TryGetValue(t.destinationStateMachine, out var dsm)) return dstParent.AddStateMachineTransition(dstSrc, dsm);
            return null;
        }

        // ─── Transition settings ──────────────────────────────────────────────────
        /// <summary> Copies exit time, duration and interruption settings, then the shared transition data. </summary>
        private static void ApplyStateTrans(AnimatorStateTransition src, AnimatorStateTransition dst)
        {
            dst.hasExitTime = src.hasExitTime;
            dst.exitTime = src.exitTime;
            dst.hasFixedDuration = src.hasFixedDuration;
            dst.duration = src.duration;
            dst.offset = src.offset;
            dst.interruptionSource = src.interruptionSource;
            dst.orderedInterruption = src.orderedInterruption;
            dst.canTransitionToSelf = src.canTransitionToSelf;
            ApplyBaseTrans(src, dst);
        }

        /// <summary> Copies name, solo, mute and conditions shared by every transition type. </summary>
        private static void ApplyBaseTrans(AnimatorTransitionBase src, AnimatorTransitionBase dst)
        {
            dst.name = src.name;
            dst.solo = src.solo;
            dst.mute = src.mute;
            var conds = new AnimatorCondition[src.conditions.Length];
            Array.Copy(src.conditions, conds, conds.Length);
            dst.conditions = conds;
        }

        // ─── Log helpers ──────────────────────────────────────────────────────────
        /// <summary> Returns a readable destination name for a transition (state, sub-SM or Exit). </summary>
        private static string DestLabel(AnimatorTransitionBase t)
        {
            if (t is AnimatorStateTransition st && st.isExit) return "[Exit]";
            if (t.destinationState != null) return t.destinationState.name;
            if (t.destinationStateMachine != null) return $"[SM] {t.destinationStateMachine.name}";
            return "[Exit]";
        }

        /// <summary> Formats transition conditions into a single readable bracketed string. </summary>
        private static string FmtConds(AnimatorCondition[] conds)
        {
            if (conds == null || conds.Length == 0) return "(no conditions)";
            var parts = conds.Select(c => c.mode switch
            {
                AnimatorConditionMode.If => c.parameter,
                AnimatorConditionMode.IfNot => $"!{c.parameter}",
                AnimatorConditionMode.Greater => $"{c.parameter} > {c.threshold}",
                AnimatorConditionMode.Less => $"{c.parameter} < {c.threshold}",
                AnimatorConditionMode.Equals => $"{c.parameter} == {c.threshold}",
                AnimatorConditionMode.NotEqual => $"{c.parameter} != {c.threshold}",
                _ => c.parameter
            });
            return $"[{string.Join("  &&  ", parts)}]";
        }
    }
}
#endif
