// Last Change: 10/16/2026 4:20pm
#if UNITY_EDITOR
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement; //MWC — PrefabStageUtility (hide the Equip/Holster in Character buttons in Prefab Mode)
using UnityEditorInternal;
using UnityEngine;
#endif

namespace MalbersAnimations.Items
{
#if UNITY_EDITOR

    [CustomEditor(typeof(SuperItem))]
    public class SuperItemEditor : Editor
    {
        private SerializedProperty ShowWarnings, Editor_Tabs1, Editor_Tabs2,
            Editor_ActionTabs, //MWC: Action tabs index, stored on the Item instead of on each Action

            // Active,
            //Mode, DisableOnEquip,
            EquipMode, UnequipMode, EquipDuration,

            debug, DebugColor,

            autoPick, autoEquip, Stance, strafe,

            Holster, HolsterColor, EquipColor,
            EquipPointIndex, ItemType, CanBeEquippedWith,
            DestroyRbOnPick, DisableCollidersOnEquip, DisableCollidersOnHolster, //MWC — per-item collider policy
            SecondaryEquipPoint, //MWC: Dual Wielding (single point)
            SecondaryHolster, //MWC: Dual Wielding (fall-through holster)
                              //MWC — HolsterAnim/holsterSlot/HolsterOffset/EquipPointOffset/OverrideItemType/ItemTypeOverride removed:
                              //they live in the character's SuperItemOffsets asset now

            rb, m_colliders, m_Triggers,
            //interactionID,

            m_UI, description, actionSets,
            //DefaultActionSetIndex, CurrentActionSetIndex,
            //  Action,

            EquipReaction, UnequipReaction,

            // FocusedBy,
            DisplayMesh, UpdateTargetFromAimer, TargetFromInteractor, InteractableID, ClearTargetOnUnfocus, //MWC: Target from Interactor fields
            FocusTags, FocusedByReaction, UnFocusedByReaction,
            HolsteredReaction, UnHolsteredReaction,

            OnEquip, OnUnequip, OnFocused, OnUnfocused,
            // uniqueEquipment,
            mirror,

            //MWC — chamberSize/chamberUnits/ChargeTime/ChargeMaxValue/ChargeAnimParam removed: Chamber & Charge are Stats on the item's Stats component now

            PickMode, DropMode, PickDuration,

            OnUnHolstered, OnHolstered,
            OnAddedToManager, OnRemoveFromManager, OnPickedBy, OnDroppedBy,


            RuntimeIK,
            //IKProfile,

            ////DualWielding, SlaveItemID,
            //BlockedActions,
            SelectedActionSetIndex
           ;

        private SuperItem M;
        private readonly Dictionary<string, Editor> IK_Editor = new();

        //string COPYACTION = "";

        // ── Action Set clipboard ─────────────────────────────────────────────
        // A tiny ScriptableObject wrapper lets JsonUtility capture [SerializeReference] fields
        // (e.g. ItemProcessor polymorphic arrays) when serialising the whole object graph.
        private class ActionSetClipboardSO : ScriptableObject
        {
            public ItemActionSet actionSetData;
        }

        private static string s_ActionSetClipboard; // null means empty

        private void CopyActionSetToClipboard(int index)
        {
            var temp = ScriptableObject.CreateInstance<ActionSetClipboardSO>();
            temp.actionSetData = M.actionSets[index];
            s_ActionSetClipboard = JsonUtility.ToJson(temp);
            Object.DestroyImmediate(temp);
        }

        private void PasteActionSetFromClipboard()
        {
            var temp = ScriptableObject.CreateInstance<ActionSetClipboardSO>();
            JsonUtility.FromJsonOverwrite(s_ActionSetClipboard, temp);
            var pastedSet = temp.actionSetData;
            Object.DestroyImmediate(temp);

            //MWC — the copy carries the [Active]/[Default] flags of the Set it came from, so pasting used to create a
            //SECOND Active and a SECOND Default Set. A pasted Set never steals those roles; EnforceSingleActiveAndDefault
            //promotes it only when it ends up being the only Set of the list
            pastedSet.Active = false;
            pastedSet.Default = false;

            Undo.RecordObject(M, "Paste Action Set");
            M.actionSets ??= new();
            M.actionSets.Add(pastedSet);
            EditorUtility.SetDirty(M);
            serializedObject.Update();
        }
        // ─────────────────────────────────────────────────────────────────────

        // ── Item Action clipboard ────────────────────────────────────────────
        private class ItemActionClipboardSO : ScriptableObject
        {
            public ItemAction actionData;
        }

        private static string s_ItemActionClipboard;

        private void CopyItemActionToClipboard(int actionSetIndex, int actionIndex)
        {
            var temp = ScriptableObject.CreateInstance<ItemActionClipboardSO>();
            temp.actionData = M.actionSets[actionSetIndex].actions[actionIndex];
            s_ItemActionClipboard = JsonUtility.ToJson(temp);
            Object.DestroyImmediate(temp);
        }

        private void PasteItemActionFromClipboard(int actionSetIndex)
        {
            var temp = ScriptableObject.CreateInstance<ItemActionClipboardSO>();
            JsonUtility.FromJsonOverwrite(s_ItemActionClipboard, temp);
            var pastedAction = temp.actionData;
            Object.DestroyImmediate(temp);

            Undo.RecordObject(M, "Paste Item Action");
            M.actionSets[actionSetIndex].actions ??= new();
            M.actionSets[actionSetIndex].actions.Add(pastedAction);
            EditorUtility.SetDirty(M);
            serializedObject.Update();
        }
        // ─────────────────────────────────────────────────────────────────────

        private ReorderableList Reo_ActionSets;

        //MWC — [Ignore] column of the Action Sets list: a Set skipped by every Auto Activation
        private const float IGNORE_W = 30f;

        /// <summary> [Auto] column of the Action Sets list: the Set Auto Activation flags, editable inline </summary>
        private const float AUTO_W = 110f;
        private static readonly GUIContent s_AutoSet = new("Auto", "Set Auto Activation: when the Item re-checks whether this Set should become the Active one");
        private static readonly string[] s_AutoNames = System.Enum.GetNames(typeof(SetActivationType));

        private static readonly GUIContent s_IgnoreSet = new("✖",
            "IGNORE this Action Set.\n\nIt is skipped by EVERY form of Auto Activation, and if it is the Active Set the Item returns to the Default Set.\n" +
            "The Default Set can NOT be ignored: the flag has no effect there");

        //MWC — set by the [Ignore] toggle, consumed after ApplyModifiedProperties so the Set swap never runs mid-draw
        private bool checkIgnoredSets;
        private readonly Dictionary<string, ReorderableList> Reo_Actions = new();

        private GUIStyle bigButtonStyle;

        protected virtual void OnEnable()
        {
            M = target as SuperItem;

            // MWC: Guard against null target during domain reload/compile. Accessing
            // serializedObject below calls CreateSerializedObject() which throws
            // SerializedObjectNotCreatableException ("Object at index 0 is null") when
            // the inspected target was torn down by a recompile. Bail out cleanly.
            if (M == null || target == null) return;

            #region Find Property
            //Private Properties
            ShowWarnings = serializedObject.FindProperty("ShowWarnings");
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            Editor_Tabs2 = serializedObject.FindProperty("Editor_Tabs2");
            Editor_ActionTabs = serializedObject.FindProperty("Editor_ActionTabs");
            debug = serializedObject.FindProperty("debug");
            DebugColor = serializedObject.FindProperty("DebugColor");

            SelectedActionSetIndex = serializedObject.FindProperty("SelectedActionSetIndex");

            //General
            //Active = serializedObject.FindProperty(nameof(M.active));
            DisplayMesh = serializedObject.FindProperty(nameof(M.DisplayMesh));
            //MWC: Side field removed from SuperItem (Side comes from the Equip Point ID)

            actionSets = serializedObject.FindProperty(nameof(M.actionSets));

            UpdateTargetFromAimer = serializedObject.FindProperty(nameof(M.UpdateTargetFromAimer));
            TargetFromInteractor = serializedObject.FindProperty(nameof(M.TargetFromInteractor));
            InteractableID = serializedObject.FindProperty(nameof(M.InteractableID));
            ClearTargetOnUnfocus = serializedObject.FindProperty(nameof(M.ClearTargetOnUnfocus));

            // SlaveItemID = serializedObject.FindProperty(nameof(M.SlaveItemID));
            DestroyRbOnPick = serializedObject.FindProperty(nameof(M.DestroyRbOnPick));
            DisableCollidersOnEquip = serializedObject.FindProperty(nameof(M.DisableCollidersOnEquip));
            DisableCollidersOnHolster = serializedObject.FindProperty(nameof(M.DisableCollidersOnHolster));
            ItemType = serializedObject.FindProperty(nameof(M.ItemType));
            CanBeEquippedWith = serializedObject.FindProperty(nameof(M.CanBeEquippedWith));
            Stance = serializedObject.FindProperty(nameof(M.Stance));
            strafe = serializedObject.FindProperty(nameof(M.strafe));

            //Modes
            EquipMode = serializedObject.FindProperty(nameof(M.EquipMode));

            UnequipMode = serializedObject.FindProperty(nameof(M.UnequipMode));
            EquipDuration = serializedObject.FindProperty(nameof(M.equipDuration));

            //EquipAbility = serializedObject.FindProperty(nameof(M.EquipAbility));
            //UnequipAbility = serializedObject.FindProperty(nameof(M.UnequipAbility));

            PickMode = serializedObject.FindProperty(nameof(M.PickMode));
            DropMode = serializedObject.FindProperty(nameof(M.DropMode));
            PickDuration = serializedObject.FindProperty(nameof(M.PickDuration));

            //data
            m_UI = serializedObject.FindProperty(nameof(m_UI));
            description = serializedObject.FindProperty(nameof(description));

            //references
            rb = serializedObject.FindProperty(nameof(M.rb));
            m_colliders = serializedObject.FindProperty(nameof(M.m_colliders));
            m_Triggers = serializedObject.FindProperty(nameof(M.m_Triggers));

            //holsters
            autoPick = serializedObject.FindProperty(nameof(M.autoPick));
            autoEquip = serializedObject.FindProperty(nameof(M.m_autoEquip));

            Holster = serializedObject.FindProperty("holster");
            HolsterColor = serializedObject.FindProperty(nameof(M.HolsterColor));


            //MWC — HolsterAnim/holsterSlot/HolsterOffset lookups removed (moved to the SuperItemOffsets asset)

            //equip point
            EquipPointIndex = serializedObject.FindProperty(nameof(M.EquipPoint));
            EquipColor = serializedObject.FindProperty(nameof(M.EquipColor));
            RuntimeIK = serializedObject.FindProperty(nameof(M.RuntimeIK));

            //MWC — EquipPointOffset/OverrideItemType/ItemTypeOverride lookups removed (moved to the SuperItemOffsets asset)
            SecondaryEquipPoint = serializedObject.FindProperty(nameof(M.SecondaryEquipPoint)); //MWC: Dual Wielding
            SecondaryHolster = serializedObject.FindProperty(nameof(M.SecondaryHolster)); //MWC: Dual Wielding
            // IKProfile = serializedObject.FindProperty(nameof(M.IKProfile));

            //Reactions
            EquipReaction = serializedObject.FindProperty(nameof(M.EquipReaction));
            UnequipReaction = serializedObject.FindProperty(nameof(M.UnequipReaction));
            FocusTags = serializedObject.FindProperty(nameof(M.FocusTags));
            FocusedByReaction = serializedObject.FindProperty(nameof(M.FocusedByReaction));
            UnFocusedByReaction = serializedObject.FindProperty(nameof(M.UnFocusedByReaction));
            HolsteredReaction = serializedObject.FindProperty(nameof(M.HolsteredReaction));
            UnHolsteredReaction = serializedObject.FindProperty(nameof(M.UnHolsteredReaction));

            //MWC — Chamber & Charge property lookups removed (moved to the Stats component)

            //  FocusedBy = serializedObject.FindProperty("focusedBy");
            mirror = serializedObject.FindProperty(nameof(M.mirror));

            //events
            OnEquip = serializedObject.FindProperty(nameof(M.OnEquip));
            OnUnequip = serializedObject.FindProperty(nameof(M.OnUnequip));
            OnFocused = serializedObject.FindProperty(nameof(M.OnFocused));
            OnUnfocused = serializedObject.FindProperty(nameof(M.OnUnfocused));
            OnHolstered = serializedObject.FindProperty(nameof(M.OnHolstered));
            OnUnHolstered = serializedObject.FindProperty(nameof(M.OnUnHolstered));


            OnAddedToManager = serializedObject.FindProperty(nameof(M.OnAddedToManager));
            OnRemoveFromManager = serializedObject.FindProperty(nameof(M.OnRemoveFromManager));
            OnPickedBy = serializedObject.FindProperty(nameof(M.OnPickedBy));
            OnDroppedBy = serializedObject.FindProperty(nameof(M.OnDroppedBy));

            // interactionID = serializedObject.FindProperty(nameof(M.interactionID));
            #endregion
            DrawActionSets();

            DrawValidation();
            MasterPopupList = new string[] { "None" };

            GetMasterActionPopup();
        }

        private void DrawValidation()
        {
            Validation = "";
            ValidationRefreshed = false; //MWC — cleared here so the "all clear" box only shows right after the Refresh button, not after an automatic re-validation

            foreach (var set in M.actionSets)
            {
                if (set == null) continue;
                foreach (var action in set.actions)
                {
                    if (action == null || action.processors == null) continue;
                    foreach (var processor in action.processors)
                    {
                        if (processor != null)
                        {
                            string validation = processor.Validation(M, set, action);

                            if (!string.IsNullOrEmpty(validation))
                            {
                                Validation += $"⚠[{set.Name} - {action.Name} - {processor.GetType().Name}] <<{validation}>>";
                            }
                        }
                        else
                        {
                            Validation += $"⚠[{set.Name} - {action.Name}] <<Processor Missing>>\n";
                        }
                    }

                    //MWC — the old "append action.Trigger into m_Triggers" step is gone: the list is derived from the
                    //item's children now, so registering it here was redundant AND it dirtied the asset from a draw
                    //pass. Instead, flag a Trigger that is not under this item, which is the only case that breaks.
                    if (action.Trigger != null && !action.Trigger.transform.IsChildOf(M.transform))
                        Validation += $"⚠[{set.Name} - {action.Name}] <<Trigger [{action.Trigger.name}] is not a child of this Item, so it will never be registered>>\n";
                }
            }
        }

        private string Validation;

        //MWC — set only by the Refresh button, so the "all clear" message appears on an explicit check, not on every repaint
        private bool ValidationRefreshed;

        //MWC — Equip/Unequip event labels: they are raised by the owner and send the Current User.
        //Cached statics, ShowEvents runs every repaint.
        private static readonly GUIContent GC_OnEquip = new("On Equip by Owner",
            "Raised when this Item is equipped by an user.\nSends the Current User (the Owner's GameObject)");
        private static readonly GUIContent GC_OnUnequip = new("On Unequip by Owner",
            "Raised when this Item is unequipped by an user.\nSends the Current User (the Owner's GameObject)");

        private static readonly GUIContent s_UpdateRuntimeOffset = new("Update Runtime Offset",
            "Saves this Item's current local Position/Rotation/Scale into its entry (Item + Equip Point / Item + Holster) " +
            "in the User's Item Offsets asset. Available while the Item is equipped or holstered");

        private static GUIContent s_RefreshContent; //MWC — cached to avoid per-repaint allocation

        //MWC — Refresh button content (icon + label) for re-running the Processors validation
        private static GUIContent RefreshContent
        {
            get
            {
                s_RefreshContent ??= new GUIContent(" Refresh", EditorGUIUtility.IconContent("d_Refresh").image,
                        "Re-run the Processors Validation to check if the warnings are gone");
                return s_RefreshContent;
            }
        }

        //MWC — "Equip & Holsters" and "References" are no longer tabs: their sections live inside General now
        private readonly string[] Tab1 = new string[] { "General", "Sets & Actions" };
        private readonly string[] Tab2 = new string[] { "Reactions", "Events", };

        private const int NO_TAB = 3; //MWC — out-of-range index: the toolbar draws with nothing selected

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            //MWC — repair duplicated [Active]/[Default] Sets before anything is drawn (Paste, Add, Remove, old data)
            if (EnforceSingleActiveAndDefault()) serializedObject.ApplyModifiedPropertiesWithoutUndo();


            bigButtonStyle ??= new GUIStyle(EditorStyles.toolbarButton)
            {
                fixedHeight = 25,
                alignment = TextAnchor.MiddleCenter,
            };

            var Missing = "";

            if (M.ItemType == null) Missing += " [⚠ Item Type]";
            if (M.HolsterID == null) Missing += " [⚠ Holster]";
            if (M.EquipPoint == null) Missing += " [⚠ Equip Point]";

            var itemID = M.ItemType != null ? ($"{M.ItemType.name} ID: {M.ItemType.ID}") : "None";

            //MWC — Description + Refresh button. Validation is only rebuilt on OnEnable/edits, so it goes stale after
            //fixing something elsewhere (e.g. assigning a Stat ID). Refresh re-runs it without reselecting the Item.
            using (new GUILayout.HorizontalScope())
            {
                MalbersEditor.DrawDescription($"Super Item  [{itemID}] {Missing} {(Application.isPlaying ? $"[Is Equipped: {M.IsEquipped}]" : "")}");


            }

            bool anyWarning = !string.IsNullOrEmpty(Missing) || !string.IsNullOrEmpty(Validation);

            if (anyWarning)
                ShowWarnings.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Show Warnings", "Show or hide the Warnings help boxes"), ShowWarnings.boolValue);

            if (ShowWarnings.boolValue && !string.IsNullOrEmpty(Missing))
            {
                EditorGUILayout.HelpBox("The Item is missing some references: " + Missing, MessageType.Warning);
            }

            if (ShowWarnings.boolValue && !string.IsNullOrEmpty(Validation))
            {
                var guicolor = GUI.color;
                GUI.color = Color.red + Color.yellow;
                EditorGUILayout.HelpBox(Validation, MessageType.None);
                GUI.color = guicolor;

                //MWC — "all clear" feedback only after an explicit Refresh; automatic re-validations clear the flag inside DrawValidation
                if (ValidationRefreshed && string.IsNullOrEmpty(Validation) && string.IsNullOrEmpty(Missing))
                    EditorGUILayout.HelpBox("No issues found. Processors and references are valid.", MessageType.Info);
            }



            //Refresh button for re-running the Processors validation. Only shows if there are warnings or missing references.
            if ((!string.IsNullOrEmpty(Validation) || !string.IsNullOrEmpty(Missing)) && GUILayout.Button(RefreshContent, EditorStyles.miniButton, GUILayout.Width(75)))
            {
                DrawValidation();
                ValidationRefreshed = true;
            }

            //MWC — the Equip & Holsters / References tab labels are gone with their tabs. The missing-reference
            //warnings they carried are already reported by the 'Missing' description and HelpBox above.

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                if (Editor_Tabs1 == null) return;

                //MWC — a stale index saved by the removed tabs would leave both toolbars unselected: fall back to General
                if (Editor_Tabs1.intValue >= Tab1.Length && Editor_Tabs2.intValue >= Tab2.Length) Editor_Tabs1.intValue = 0;

                Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tab1);

                if (Editor_Tabs1.intValue != NO_TAB) Editor_Tabs2.intValue = NO_TAB;

                Editor_Tabs2.intValue = GUILayout.Toolbar(Editor_Tabs2.intValue, Tab2);

                if (Editor_Tabs2.intValue != NO_TAB) Editor_Tabs1.intValue = NO_TAB;

                if (cc.changed)
                {
                    GetMasterActionPopup();
                    DrawValidation();
                }
            }

            if (Application.isPlaying)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUI.DisabledGroupScope(true))
                    {
                        using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
                        {
                            EditorGUIUtility.labelWidth = 50;
                            EditorGUILayout.ObjectField("User", M.User, typeof(SuperItemManager), true, GUILayout.MinWidth(50));
                            EditorGUILayout.ObjectField("Target", M.Target, typeof(GameObject), true, GUILayout.MinWidth(50));
                            EditorGUIUtility.labelWidth = 0;

                        }
                        using (new GUILayout.HorizontalScope())
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.ToggleLeft("In Holster ", M.IsInHolster, GUILayout.MinWidth(50));
                                EditorGUILayout.ToggleLeft("Equipped", M.IsEquipped, GUILayout.MinWidth(50));
                            }

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {

                                EditorGUILayout.ToggleLeft("Can Interact", M.Active, GUILayout.MinWidth(50));
                            }
                        }
                    }

                    //Saves this Item's pose (moved by hand while equipped/holstered) into the User's Item Offsets asset
                    using (new EditorGUI.DisabledGroupScope(M.User == null || M.User.ItemOffsets == null || (!M.IsEquipped && !M.IsInHolster)))
                    {
                        if (GUILayout.Button(s_UpdateRuntimeOffset))
                            SuperItemManagerEditor.SaveRuntimeOffsets(M.User, M);
                    }
                }
            }


            EditorGUILayout.Separator();

            //First Tabs



            int Selection1 = Editor_Tabs1.intValue;

            switch (Selection1)
            {
                case 0: ShowGeneral(); break;  //MWC — also draws the old Equip & Holsters and References sections
                case 1: ShowActionsSets(); break;
                default:
                    break;
            }

            //second Tabs
            int Selection2 = Editor_Tabs2.intValue;

            switch (Selection2)
            {
                case 0: ShowReactions(); break;
                case 1: ShowEvents(); break;
                default:
                    break;
            }
            serializedObject.ApplyModifiedProperties();

            //MWC — an [Ignore] toggle raised on the Active Set: run the real swap now that the flag is applied
            if (checkIgnoredSets)
            {
                checkIgnoredSets = false;
                if (Application.isPlaying) M.ActionSet_CheckIgnored();
            }
        }

        private void ShowGeneral()
        {
            DrawEquipInCharacter(); //MWC — Editor-only [Equip in Character] [Holster in Character] buttons

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ItemType.isExpanded = MalbersEditor.Foldout(ItemType.isExpanded, "General"))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        var itemID = M.ItemType != null ? M.ItemType.ID.ToString() : "None";

                        EditorGUILayout.PropertyField(ItemType, new GUIContent($"Item Type [{itemID}]"), GUILayout.MinWidth(100));

                        //MWC: Side field removed - the Side is now taken from the Equip Point ID the item is equipped on

                        EditorGUILayout.PropertyField(DebugColor, GUIContent.none, GUILayout.Width(40));
                        MalbersEditor.DrawDebugIcon(debug);
                    }


                    if (M.ItemType != null)
                    {
                        EditorGUILayout.PropertyField(CanBeEquippedWith);
                    }


                    EditorGUILayout.PropertyField(DestroyRbOnPick);
                    EditorGUILayout.PropertyField(DisableCollidersOnEquip, new GUIContent("Disable Colliders On Equip"));
                    EditorGUILayout.PropertyField(DisableCollidersOnHolster, new GUIContent("Disable Colliders On Holster"));
                    EditorGUILayout.PropertyField(mirror);
                    EditorGUILayout.PropertyField(Stance);
                    EditorGUILayout.PropertyField(strafe);

                }
            }

            ShowHolsters(); //MWC — was the "Equip & Holsters" tab

            //MWC: Group all the target-resolution options under a "Find Target" foldout (state stored on UpdateTargetFromAimer).
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (UpdateTargetFromAimer.isExpanded = MalbersEditor.Foldout(UpdateTargetFromAimer.isExpanded, "Find Target"))
                {
                    EditorGUILayout.PropertyField(UpdateTargetFromAimer);
                    EditorGUILayout.PropertyField(TargetFromInteractor); //MWC: Target from Interactor toggle
                    EditorGUILayout.PropertyField(InteractableID);       //MWC: hidden unless Target from Interactor is on (via [Hide])
                    EditorGUILayout.PropertyField(ClearTargetOnUnfocus); //MWC: hidden unless Target from Interactor is on (via [Hide])
                }
            }



            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (autoPick.isExpanded = MalbersEditor.Foldout(autoPick.isExpanded, "Pick and Drop"))
                {
                    EditorGUILayout.PropertyField(autoPick);
                    EditorGUILayout.PropertyField(autoEquip);

                    EditorGUILayout.PropertyField(EquipDuration);

                    EditorGUILayout.PropertyField(EquipMode);
                    EditorGUILayout.PropertyField(UnequipMode);


                    EditorGUILayout.PropertyField(PickDuration);
                    EditorGUILayout.PropertyField(PickMode);
                    EditorGUILayout.PropertyField(DropMode);
                    EditorGUILayout.PropertyField(FocusTags);
                }
            }


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (m_UI.isExpanded = MalbersEditor.Foldout(m_UI.isExpanded, "Data & Description"))
                {
                    EditorGUILayout.PropertyField(m_UI);
                    DrawUIIconPreview(); //MWC — show the UI Icon sprite as an image, right under its field
                    EditorGUILayout.PropertyField(description);

                    //MWC — [Find] OVERRIDES the Display Mesh (plus its Transform and Scale) with the one found in the
                    //Item's renderers. The automatic search (Reset/OnValidate) never replaces an authored Mesh
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(DisplayMesh);

                        if (GUILayout.Button(new GUIContent("Find",
                            "Find the Display Mesh in the Item's renderers and OVERRIDE the stored one (its Display Transform and Scale are recalculated too)"),
                            GUILayout.MaxWidth(60)))
                        {
                            Undo.RecordObject(M, $"[{M.name}] Find Display Mesh");

                            M.ForceFindDisplayMesh();

                            EditorUtility.SetDirty(M);
                            serializedObject.Update(); //The target was written directly: refresh the cached properties
                        }
                    }
                }
            }

            ShowReferences(); //MWC — was the "References" tab

            //MWC — "Chamber Data" and "Charge Data" sections removed: Chamber & Charge are Stats
            //on a Stats component next to the Super Item (Fire/Reload/Charge processors reference them by StatID)
        }

        #region Equip / Holster in Character (Editor Only) —— MWC

        //MWC — Remembered target character, so clicking the buttons again does not ask every time.
        //Instance ID (not the reference) so it survives a domain reload; validated against the scene on every use.
#if UNITY_6000_4_OR_NEWER
        private static EntityId s_TargetManagerID; //MWC — Unity 6.4 replaced the Instance ID with the Entity ID
#else
        private static int s_TargetManagerID;
#endif

        private static GUIContent s_EquipInCharacter, s_HolsterInCharacter, s_PickCharacter, s_UnequipFromCharacter;

        //MWC — Character currently holding this Item on a row, cached: the scan runs on Layout events only (throttled),
        //so the button count never changes between Layout and Repaint
        //MWC — UI Icon preview ────────────────────────────────────────────────────────
        private const float k_IconPreviewSize = 100f; //MWC — height of the UI Icon thumbnail

        /// <summary>Draws the resolved UI Icon (constant or Sprite Var) as an image under its field</summary>
        private void DrawUIIconPreview()
        {
            if (targets.Length > 1) return; //Multi-editing: there is no single icon to show

            var sprite = M != null ? M.UIIcon : null;

            if (sprite == null) return;

            //Keep the sprite's aspect ratio: wide icons stay wide instead of being squashed into a square
            var aspect = sprite.rect.height > 0 ? sprite.rect.width / sprite.rect.height : 1f;
            var width = Mathf.Max(16f, k_IconPreviewSize * aspect);

            //MWC — centered: take the full line, then place the thumbnail in the middle of it
            var line = GUILayoutUtility.GetRect(0, k_IconPreviewSize, GUILayout.ExpandWidth(true));
            var rect = new Rect(line.x + (line.width - width) * 0.5f, line.y, width, k_IconPreviewSize);

            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.25f)); //Backdrop, so transparent icons stay readable

            var texture = sprite.texture;

            //Draw only the sprite's own region of the texture (sliced sheets and atlases share one texture)
            if (texture != null && !sprite.packed)
            {
                var tr = sprite.textureRect;
                var coords = new Rect(tr.x / texture.width, tr.y / texture.height,
                                      tr.width / texture.width, tr.height / texture.height);

                GUI.DrawTextureWithTexCoords(rect, texture, coords, true);
            }
            else
            {
                //Packed at runtime (or no texture): the asset preview is the only safe source. It loads async → null on the first repaints
                var preview = AssetPreview.GetAssetPreview(sprite);

                if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
                else Repaint(); //Ask for another repaint until the preview is ready
            }
        }

        private SuperItemManager assignedManager;
        private string assignedSlot;
        private double nextAssignmentCheck;

        /// <summary> Editor-only helpers: drop this Item into a scene character's Equip Point / Holster so it can be
        /// tested on Play without wiring the Super Item Manager by hand. Hidden in Play Mode and in Prefab Mode;
        /// disabled for prefab assets (the manager needs a scene instance). </summary>
        private void DrawEquipInCharacter()
        {
            if (Application.isPlaying) return;                                   //Play Mode uses the real equip flow
            if (targets.Length > 1) return;                                      //One Item at a time
            if (PrefabStageUtility.GetPrefabStage(M.gameObject) != null) return;  //Hidden in Prefab Mode

            s_EquipInCharacter ??= new GUIContent("Equip in Char",
                "Sets this Item on the Equip Point of a character with a Super Item Manager in the Scene.\nAny item already there is replaced");
            s_HolsterInCharacter ??= new GUIContent("Holster in Char",
                "Sets this Item on the Holster of a character with a Super Item Manager in the Scene.\nAny item already there is replaced");
            s_PickCharacter ??= new GUIContent("▾", "Choose which character the buttons will use");
            s_UnequipFromCharacter ??= new GUIContent("Remove",
                "Removes this Item from the Equip Point / Holster of the character it is assigned to");

            RefreshAssignment();

            var isPrefabAsset = M.gameObject.IsPrefab();

            using (new GUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledGroupScope(isPrefabAsset))
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(s_EquipInCharacter, EditorStyles.miniButtonMid, GUILayout.Width(100)))
                        PlaceItemInCharacter(false);

                    if (GUILayout.Button(s_HolsterInCharacter, EditorStyles.miniButtonMid, GUILayout.Width(100)))
                        PlaceItemInCharacter(true);
                    if (GUILayout.Button(s_PickCharacter, EditorStyles.miniButtonRight, GUILayout.Width(22), GUILayout.Height(22)))
                    {
                        var managers = FindSceneManagers();

                        if (managers.Count == 0) NoManagerFound();
#if UNITY_6000_4_OR_NEWER
                        else ShowCharacterMenu(managers, m => s_TargetManagerID = m.GetEntityId());
#else
                        else ShowCharacterMenu(managers, m => s_TargetManagerID = m.GetInstanceID());
#endif
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            //MWC — Only when the Item is already assigned: shows where it is and lets it be removed from there.
            //Kept enabled for prefab assets too: they can be assigned to a Holster row by hand.
            if (assignedManager != null)
            {
                using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    //MWC — a real Object field instead of a label, so it can be double clicked to jump to the
                    //character. Left enabled on purpose (a disabled field swallows the click) and the returned
                    //value is ignored: the row is rebuilt from the scan on every pass.

                    EditorGUILayout.LabelField(new GUIContent($"In [{assignedSlot}]", "Character holding this Item. Double click it to select it in the Hierarchy"), GUILayout.MaxWidth(120));

                    EditorGUILayout.ObjectField(GUIContent.none, assignedManager, typeof(SuperItemManager), true, GUILayout.MinWidth(70));

                    if (GUILayout.Button(s_UnequipFromCharacter, EditorStyles.miniButton, GUILayout.Width(70)))
                        RemoveItemFromCharacters();
                }
            }


            if (isPrefabAsset)
                EditorGUILayout.HelpBox("Drag the Item into the Scene to use [Equip/Holster in Character]", MessageType.None);
        }

        //MWC — Finds the character holding this Item on an Equip Point / Holster row. Runs on Layout events only
        //(and throttled), so the drawn controls stay identical between the Layout and the Repaint pass.
        private void RefreshAssignment(bool force = false)
        {
            if (!force)
            {
                if (Event.current == null || Event.current.type != EventType.Layout) return;
                if (EditorApplication.timeSinceStartup < nextAssignmentCheck) return;
            }

            nextAssignmentCheck = EditorApplication.timeSinceStartup + 0.5;

            assignedManager = null;
            assignedSlot = null;

            foreach (var manager in FindSceneManagers())
            {
                foreach (var point in manager.EquipPoints)
                {
                    if (point != null && point.Item == M)
                    {
                        assignedManager = manager;
                        assignedSlot = point.ID != null ? point.ID.name : "Equip Point";
                        return;
                    }
                }

                foreach (var holster in manager.holsters)
                {
                    if (holster != null && holster.Item == M)
                    {
                        assignedManager = manager;
                        assignedSlot = holster.ID != null ? holster.ID.name : "Holster";
                        return;
                    }
                }
            }
        }

        //MWC — Clears this Item from every Equip Point / Holster row of every character in the opened Scenes
        private void RemoveItemFromCharacters()
        {
            foreach (var manager in FindSceneManagers())
            {
                var so = new SerializedObject(manager);

                ClearItemFromRows(so.FindProperty(nameof(manager.EquipPoints)));
                ClearItemFromRows(so.FindProperty(nameof(manager.holsters)));

                if (so.ApplyModifiedProperties()) //Undo + Prefab overrides are handled here
                    Debug.Log($"<b>[{M.name}]</b> removed from <b>[{manager.name}]</b>", manager);
            }

            RefreshAssignment(true);
            Repaint(); //MWC — see PlaceItemIn
        }

        //MWC — Resolves the character, then sets this Item on its Equip Point (holster: false) or Holster (holster: true)
        private void PlaceItemInCharacter(bool holster)
        {
            var managers = FindSceneManagers();

            if (managers.Count == 0) { NoManagerFound(); return; }

            var target = ResolveTargetManager(managers);

            if (target != null) { PlaceItemIn(target, holster); return; }

            //More than one character and no valid remembered one: let the user choose
            ShowCharacterMenu(managers, m =>
            {
#if UNITY_6000_4_OR_NEWER
                s_TargetManagerID = m.GetEntityId();
#else
                s_TargetManagerID = m.GetInstanceID();
#endif
                PlaceItemIn(m, holster);
            });
        }

        //MWC — All the Super Item Managers of the opened Scenes (inactive included). Prefab Stage objects are skipped.
        private static List<SuperItemManager> FindSceneManagers()
        {
            var found = Object.FindObjectsByType<SuperItemManager>(FindObjectsInactive.Include
#if !UNITY_6000_4_OR_NEWER
             , FindObjectsSortMode.None
#endif
             );

            var result = new List<SuperItemManager>();

            foreach (var m in found)
            {
                if (m == null || !m.gameObject.scene.IsValid()) continue;
                if (PrefabStageUtility.GetPrefabStage(m.gameObject) != null) continue;
                result.Add(m);
            }
            return result;
        }

        //MWC — Single manager wins; otherwise the remembered one when it is still in the Scene. Null means "ask".
        private static SuperItemManager ResolveTargetManager(List<SuperItemManager> managers)
        {
            if (managers.Count == 1) return managers[0];

            foreach (var m in managers)
#if UNITY_6000_4_OR_NEWER
                if (m.GetEntityId() == s_TargetManagerID) return m;
#else
                if (m.GetInstanceID() == s_TargetManagerID) return m;
#endif

            return null;
        }

        private static void ShowCharacterMenu(List<SuperItemManager> managers, System.Action<SuperItemManager> onPick)
        {
            var menu = new GenericMenu();

            foreach (var m in managers)
            {
                var manager = m; //Capture
#if UNITY_6000_4_OR_NEWER
                menu.AddItem(new GUIContent(manager.name), manager.GetEntityId() == s_TargetManagerID, () => onPick(manager));
#else
                menu.AddItem(new GUIContent(manager.name), manager.GetInstanceID() == s_TargetManagerID, () => onPick(manager));
#endif
            }
            menu.ShowAsContext();
        }

        private static void NoManagerFound() =>
            EditorUtility.DisplayDialog("No Super Item Manager",
                "There is no character with a Super Item Manager component in the opened Scenes", "OK");

        //MWC — Writes the Item on the resolved row, replacing whatever was there, and parents it to the
        //Equip Point / Holster Slot using the character's Super Item Offsets asset (same rules the runtime uses).
        private void PlaceItemIn(SuperItemManager manager, bool holster)
        {
            int index = holster ? FindHolsterIndex(manager) : FindEquipPointIndex(manager);

            if (index < 0)
            {
                var missing = holster
                    ? $"Holster [{(M.HolsterID != null ? M.HolsterID.name : "None")}]"
                    : $"Equip Point [{(M.EquipPoint != null ? M.EquipPoint.name : "None")}]";

                EditorUtility.DisplayDialog("Missing Slot", $"[{manager.name}] has no {missing} for this Item", "OK");
                return;
            }

            var so = new SerializedObject(manager);
            var equipPointList = so.FindProperty(nameof(manager.EquipPoints));
            var holsterList = so.FindProperty(nameof(manager.holsters));

            //MWC — The Item can only live on ONE character: clear it from every OTHER Manager in the opened Scenes
            //first. Only the target's own rows used to be cleared, so the previous character kept referencing the
            //Item: it ended up assigned to two characters at once and the [In ...] row below reported whichever
            //Manager the scan found first — usually the old one, so the reference looked like it never changed.
            foreach (var other in FindSceneManagers())
            {
                if (other == manager) continue;

                var otherSO = new SerializedObject(other);

                ClearItemFromRows(otherSO.FindProperty(nameof(other.EquipPoints)));
                ClearItemFromRows(otherSO.FindProperty(nameof(other.holsters)));

                if (otherSO.ApplyModifiedProperties()) //Undo + Prefab overrides are handled here
                    Debug.Log($"<b>[{M.name}]</b> removed from <b>[{other.name}]</b>", other);
            }

            //The Item can only be in one place: clear it from every other row first
            ClearItemFromRows(equipPointList);
            ClearItemFromRows(holsterList);

            var itemProp = (holster ? holsterList : equipPointList).GetArrayElementAtIndex(index).FindPropertyRelative("Item");
            var replaced = itemProp.objectReferenceValue as SuperItem;

            itemProp.objectReferenceValue = M;
            so.ApplyModifiedProperties(); //Undo + Prefab overrides are handled here

            //MWC — Transforms are never touched: the row reference is the only thing set. The Gizmo Mesh preview
            //on the Manager already shows where the Item will land, and the runtime parents it on Play
            EnsureOffsetEntry(manager, holster, index);

            var slotName = holster ? manager.holsters[index].ID.name : manager.EquipPoints[index].ID.name;

            Debug.Log($"<b>[{M.name}]</b> {(holster ? "holstered" : "equipped")} in <b>[{manager.name}]</b> → [{slotName}]" +
                $"{(replaced != null && replaced != M ? $" (replaced [{replaced.name}])" : "")}", manager);

            RefreshAssignment(true); //MWC — the [In ...] row must show the new assignment right away
            Repaint();               //MWC — the picker menu runs outside the inspector pass, which would not repaint on its own
        }

        //MWC — Primary Equip Point first, then the Secondary (Dual Wielding) one. -1 when the character has neither.
        private int FindEquipPointIndex(SuperItemManager manager)
        {
            var points = manager.EquipPoints;

            for (int i = 0; i < points.Count; i++)
                if (points[i] != null && points[i].ID != null && points[i].ID == M.EquipPoint) return i;

            for (int i = 0; i < points.Count; i++)
                if (points[i] != null && points[i].ID != null && M.IsAllowedEquipPoint(points[i].ID)) return i;

            return -1;
        }

        //MWC — Primary Holster first, then the Secondary (Dual Wielding) one. -1 when the character has neither.
        private int FindHolsterIndex(SuperItemManager manager)
        {
            var list = manager.holsters;

            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].ID != null && list[i].ID == M.HolsterID) return i;

            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].ID != null && M.IsAllowedHolster(list[i].ID)) return i;

            return -1;
        }

        //MWC — Removes this Item from every row of the list, so it is never referenced twice on the same character
        private void ClearItemFromRows(SerializedProperty list)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                var item = list.GetArrayElementAtIndex(i).FindPropertyRelative("Item");
                if (item.objectReferenceValue == M) item.objectReferenceValue = null;
            }
        }

        //MWC — Mirrors SuperItemManagerEditor.SyncRow: only creates the missing (Item + Point/Holster) entry
        //in the Offsets asset. Authored offsets are never overwritten here.
        private void EnsureOffsetEntry(SuperItemManager manager, bool holster, int index)
        {
            var offsets = manager.ItemOffsets;

            if (offsets == null || M.ItemType == null) return;

            Undo.RecordObject(offsets, "Update Super Item Offsets");

            var added = holster
                ? offsets.EnsureHolsterOffset(M.ItemType, manager.holsters[index].ID)
                : offsets.EnsureOffset(M.ItemType, manager.EquipPoints[index].ID);

            if (added) EditorUtility.SetDirty(offsets);
        }

        #endregion

        //MWC — now a collapsible section inside General (was its own tab). The collider/trigger counts and the
        //missing-reference warning that used to live in the tab label are kept in the foldout title.
        private void ShowReferences()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var title = (M.m_colliders == null || M.m_Triggers == null)
                    ? "⚠ References ⚠"
                    : $"References [C:{M.m_colliders.Length}] [T:{M.m_Triggers.Length}]";

                if (!(rb.isExpanded = MalbersEditor.Foldout(rb.isExpanded, title))) return;

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(rb, new GUIContent("RigidBody"));

                    //MWC — [Find] OVERRIDES the Colliders and Triggers lists with what is really under the Item. It used
                    //to be a no-op on an Item that already had colliders stored (see SuperItem.FindColliders), and the
                    //new lists were written straight on the target while the cached properties stayed stale.
                    if (GUILayout.Button(new GUIContent("Find",
                        "Find the Rigidbody and OVERRIDE the Colliders and Triggers lists with the ones found in the Item"),
                        GUILayout.MaxWidth(60)))
                    {
                        Undo.RecordObject(M, $"[{M.name}] Find Colliders");

                        var rigidbody = M.GetComponentInChildren<Rigidbody>();
                        if (rigidbody != null) M.rb = rigidbody;

                        M.FindColliders(); //Rebuilds [m_colliders] and [m_Triggers] from the Item's children

                        EditorUtility.SetDirty(M);
                        serializedObject.Update(); //The target was written directly: refresh the cached properties
                    }

                }
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_colliders);

                //MWC — Triggers are derived from the item's children (OnEnable/OnValidate), so they are shown read-only.
                //Hand-editing this array is what left the empty slot that threw inside ActivateDamager.
                using (new EditorGUI.DisabledGroupScope(true))
                    EditorGUILayout.PropertyField(m_Triggers);

                EditorGUI.indentLevel--;
            }
        }

        private void ShowReactions()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(EquipReaction);
                EditorGUILayout.PropertyField(UnequipReaction);
                EditorGUILayout.PropertyField(FocusedByReaction);
                EditorGUILayout.PropertyField(UnFocusedByReaction);
                EditorGUILayout.PropertyField(HolsteredReaction);
                EditorGUILayout.PropertyField(UnHolsteredReaction);
                EditorGUI.indentLevel--;
            }
        }

        private void ShowEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // if (OnEquip.isExpanded = MalbersEditor.Foldout(OnEquip.isExpanded, "Equip Events"))
                {
                    //MWC — named for who the event is about and what it sends (the Current User)
                    EditorGUILayout.PropertyField(OnEquip, GC_OnEquip);
                    EditorGUILayout.PropertyField(OnUnequip, GC_OnUnequip);
                    EditorGUILayout.PropertyField(OnFocused);
                    EditorGUILayout.PropertyField(OnUnfocused);
                    EditorGUILayout.PropertyField(OnHolstered);
                    EditorGUILayout.PropertyField(OnUnHolstered);
                    EditorGUILayout.PropertyField(OnAddedToManager);
                    EditorGUILayout.PropertyField(OnRemoveFromManager);
                    EditorGUILayout.PropertyField(OnPickedBy);
                    EditorGUILayout.PropertyField(OnDroppedBy);
                }
            }
        }

        private void ShowHolsters()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (Holster.isExpanded = MalbersEditor.Foldout(Holster.isExpanded, "Holsters & Equip"))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(Holster);
                        EditorGUILayout.PropertyField(HolsterColor, GUIContent.none, GUILayout.Width(50));
                    }
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(EquipPointIndex);
                        EditorGUILayout.PropertyField(EquipColor, GUIContent.none, GUILayout.Width(50));
                    }

                    EditorGUI.indentLevel++;

                    //MWC: Dual Wielding - single alternative holster (it gets its own row in the SuperItemOffsets asset,
                    //so the second copy can be stored with a different Offset, Slots and Anim)
                    EditorGUILayout.PropertyField(SecondaryHolster, new GUIContent("Secondary Holster (Dual Wield)",
                        "Alternative Holster used when the Primary Holster has no free Slot left for this item. Its Offset, Slots and Anim are stored in the Super Item Offsets asset"));

                    //MWC: Dual Wielding - single alternative equip point (its offset lives in the SuperItemOffsets asset too)
                    EditorGUILayout.PropertyField(SecondaryEquipPoint, new GUIContent("Secondary Equip Point (Dual Wield)",
                        "Alternative Equip Point used when the Primary Equip Point is occupied by a compatible item. Its offset is stored in the Super Item Offsets asset"));
                    EditorGUI.indentLevel--;
                }
            }
        }

        private void DrawActionSets()
        {
            Reo_ActionSets = new(serializedObject, actionSets, true, true, true, true)
            {
                drawHeaderCallback = (Rect rect) =>
                {
                    var nameRect = new Rect(rect) { width = 100, x = rect.x + 65 };
                    var Default = new Rect(rect) { width = 50, x = rect.width };

                    EditorGUI.LabelField(nameRect, "Action Sets", EditorStyles.boldLabel);
                    EditorGUI.LabelField(Default, new GUIContent("Def", "Default Action Set"), EditorStyles.boldLabel);

                    //MWC — names the [Ignore] column drawn just before Default on every row
                    var Ignore = new Rect(rect) { width = IGNORE_W, x = Default.x - IGNORE_W };
                    EditorGUI.LabelField(Ignore, new GUIContent("Ign", "Ignore the Action Set"), EditorStyles.boldLabel);

                    var Auto = new Rect(rect) { width = AUTO_W, x = Ignore.x - AUTO_W - 2 };
                    EditorGUI.LabelField(Auto, s_AutoSet, EditorStyles.boldLabel);

                    var IDRect = new Rect(rect) { width = 48, x = rect.x };
                    EditorGUI.LabelField(IDRect, "Active", EditorStyles.boldLabel);

                    // ── Paste / Clear clipboard buttons ──────────────────────
                    if (!string.IsNullOrEmpty(s_ActionSetClipboard))
                    {
                        var clearRect = new Rect(rect) { width = 45, x = rect.x + rect.width - 120 - AUTO_W }; //shifted left of the [Auto] column
                        var pasteRect = new Rect(rect) { width = 45, x = rect.x + rect.width - 170 - AUTO_W };

                        if (GUI.Button(pasteRect, new GUIContent("Paste", "Paste copied Action Set as new entry")))
                            PasteActionSetFromClipboard();

                        if (GUI.Button(clearRect, new GUIContent("Clear", "Clear the Action Set clipboard")))
                            s_ActionSetClipboard = null;
                    }
                    // ─────────────────────────────────────────────────────────
                },
                drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
                {
                    var dC = GUI.backgroundColor;

                    var activeRect = new Rect(rect.x + 15, rect.y, 20, rect.height);

                    var element = actionSets.GetArrayElementAtIndex(index);
                    var name = element.FindPropertyRelative("name");
                    var active = element.FindPropertyRelative("active");

                    var nameRect = new Rect(rect);
                    nameRect.y += 2;
                    nameRect.height = EditorGUIUtility.singleLineHeight;
                    nameRect.x += 35 + 15;
                    nameRect.width -= 75 + IGNORE_W + AUTO_W + 2; //gutter for the [Auto] mask and the [Ignore] button drawn before Default

                    var indexRect = new Rect(rect) { width = 20, x = rect.x };
                    EditorGUI.LabelField(indexRect, (index).ToString(), EditorStyles.boldLabel);

                    var Active = element.FindPropertyRelative("active");
                    var m_Default = element.FindPropertyRelative("m_Default");


                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        EditorGUI.PropertyField(activeRect, Active, GUIContent.none);

                        if (cc.changed)
                        {
                            if (!Active.boolValue) active.boolValue = true; //If the Action Set is not active, set it to true

                            if (Application.isPlaying)
                            {
                                M.ActionSet_ActiveByIndex(index); //Set the Active Action Set in case we are playing
                            }
                            for (int i = 0; i < actionSets.arraySize; i++)
                            {
                                var action = actionSets.GetArrayElementAtIndex(i);

                                if (i == index) continue; //Skip the current Action Set

                                var ElementActive = action.FindPropertyRelative("active");
                                ElementActive.boolValue = false; //Set the active value to the same as the current Action Set
                            }
                        }

                        { Undo.RecordObject(target, $"[{target.name}] Set Active Action Set"); }
                    }


                    var IDRect = new Rect(rect) { height = EditorGUIUtility.singleLineHeight };

                    var DefaultRect = new Rect(IDRect) { x = rect.width + 20, width = 30 };

                    //MWC — [Ignore] toggle, drawn in the same row just BEFORE the Default value
                    var m_Ignore = element.FindPropertyRelative("m_Ignore");
                    var IgnoreRect = new Rect(IDRect) { x = DefaultRect.x - IGNORE_W - 2, width = IGNORE_W, y = IDRect.y + 1 };

                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        var ignoreBG = GUI.backgroundColor;
                        if (m_Ignore.boolValue) GUI.backgroundColor = Color.red * 1.5f;

                        var newIgnore = GUI.Toggle(IgnoreRect, m_Ignore.boolValue, s_IgnoreSet, EditorStyles.miniButton);

                        GUI.backgroundColor = ignoreBG;

                        if (cc.changed)
                        {
                            Undo.RecordObject(target, $"[{target.name}] Ignore Action Set");
                            m_Ignore.boolValue = newIgnore;

                            //An Ignored Set cannot stay the Active one: hand the activation back to the Default Set.
                            //The Set flagged as Default is never ignored, so it is left alone (see ItemActionSet.Ignored)
                            if (newIgnore && !m_Default.boolValue && Active.boolValue)
                            {
                                for (int i = 0; i < actionSets.arraySize; i++)
                                {
                                    var other = actionSets.GetArrayElementAtIndex(i);
                                    other.FindPropertyRelative("active").boolValue = other.FindPropertyRelative("m_Default").boolValue;
                                }

                                checkIgnoredSets = true; //Play Mode needs the real Set swap, run after ApplyModifiedProperties
                            }
                        }
                    }

                    //[Auto] Set Auto Activation flags, drawn just BEFORE the [Ignore] button
                    var AutoActivation = element.FindPropertyRelative("AutoActivation");
                    var AutoRect = new Rect(IDRect) { x = IgnoreRect.x - AUTO_W - 2, width = AUTO_W, y = IDRect.y + 1 };

                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        var autoBG = GUI.contentColor;
                        if (AutoActivation.intValue != 0) GUI.contentColor = MTools.MBlue * 3f;
                        var newAuto = EditorGUI.MaskField(AutoRect, GUIContent.none, AutoActivation.intValue, s_AutoNames);
                        GUI.contentColor = autoBG;


                        if (cc.changed)
                        {
                            Undo.RecordObject(target, $"[{target.name}] Set Auto Activation");
                            AutoActivation.intValue = newAuto;
                        }
                    }

                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        if (isActive) GUI.backgroundColor = MTools.MBlue * 3; //The [Def] checkbox shares the selection tint
                        EditorGUI.PropertyField(DefaultRect, m_Default, GUIContent.none);
                        GUI.backgroundColor = dC;
                        if (cc.changed)
                        {
                            if (!m_Default.boolValue) m_Default.boolValue = true; //If the Action Set is not active, set it to true

                            for (int i = 0; i < actionSets.arraySize; i++)
                            {
                                var action = actionSets.GetArrayElementAtIndex(i);

                                if (i == index) continue; //Skip the current Action Set

                                var ElementActive = action.FindPropertyRelative("m_Default");
                                ElementActive.boolValue = false; //Set the active value to the same as the current Action Set
                            }
                        }
                        { Undo.RecordObject(target, $"[{target.name}] Set Default Action Set"); }
                    }

                    //Selection tint only on the name field: the [Auto], [Ignore] and [Def] controls keep their own colors
                    if (isActive) GUI.backgroundColor = MTools.MBlue * 3;
                    if (Application.isPlaying && Active.boolValue) GUI.backgroundColor = MTools.MGreen * 3;

                    EditorGUI.PropertyField(nameRect, name, GUIContent.none);

                    GUI.backgroundColor = dC;
                },
                onAddCallback = (ReorderableList list) =>
                {
                    Undo.RecordObject(target, $"[{target.name}] Add Action Set");

                    M.actionSets ??= new();

                    ItemActionSet newSet = new("New Action Set");

                    //MWC — a brand new Set is never Active nor Default: those roles stay on the Sets already authored
                    newSet.Active = false;
                    newSet.Default = false;

                    M.actionSets.Add(newSet);
                    EditorUtility.SetDirty(M);
                    serializedObject.Update(); //MWC — the List was changed on the target: refresh the cached properties
                },
                //MWC — Reordering MOVES the serialized DATA between the Sets, the instances themselves never move.
                //The [Active]/[Default] flags therefore land on a different row than the one that was flagged, and the
                //Selection (an index stored on the Item) stayed pointing at the old row, so the panel below showed a
                //different Set than the highlighted one. Re-check the invariant and re-anchor the selection on the
                //Set the user just dragged.
                onReorderCallbackWithDetails = (ReorderableList list, int oldIndex, int newIndex) =>
                {
                    SelectedActionSetIndex.intValue = newIndex;
                    list.index = newIndex;

                    if (EnforceSingleActiveAndDefault()) serializedObject.ApplyModifiedPropertiesWithoutUndo();

                    //The data moved but the running Item still holds the instance it entered: re-point it at the
                    //flagged Sets so the Inspector and the runtime agree again (the Set is NOT re-entered: reordering
                    //while playing is an authoring convenience, not a supported runtime operation)
                    if (Application.isPlaying && M.actionSets.Count > 0)
                    {
                        M.DefaultActionSet = M.actionSets.Find(x => x.Default) ?? M.actionSets[0];
                        M.ActiveActionSet = M.actionSets.Find(x => x.Active) ?? M.DefaultActionSet;
                    }

                    DrawValidation();
                    GetMasterActionPopup();
                },
                onSelectCallback = (ReorderableList list) =>
                {
                    SelectedActionSetIndex.intValue = list.index; //Store the current Action Set Index
                    DrawValidation();
                    GetMasterActionPopup();
                },
                index = SelectedActionSetIndex.intValue //Set the index of the Reorderable List to the Selected Action Set Index
            };
        }


        //MWC — Only ONE Set can be [Active] and only ONE can be [Default]. Both flags used to be enforced ONLY by their
        //own toggle, so every other path that grew or shrank the list (Paste Action Set, Add, Remove, hand edited data,
        //a prefab merged by hand) could leave two Actives and two Defaults behind. This re-checks the whole List and
        //keeps the FIRST flagged Set — the very one SuperItem.Awake() picks (FirstOrDefault) — so what the Inspector
        //shows is what will actually run, and it repairs Items already saved broken the first time they are inspected.

        /// <summary>Forces a single [Active] and a single [Default] Action Set. True when something was fixed.</summary>
        private bool EnforceSingleActiveAndDefault()
        {
            if (Application.isPlaying) return false;                 //Play Mode owns these flags (see SuperItem.ActionSet)
            if (actionSets == null || actionSets.arraySize == 0) return false;

            int firstDefault = -1, firstActive = -1;
            bool changed = false;

            for (int i = 0; i < actionSets.arraySize; i++)
            {
                var set = actionSets.GetArrayElementAtIndex(i);
                var isDefault = set.FindPropertyRelative("m_Default");
                var isActive = set.FindPropertyRelative("active");

                if (isDefault.boolValue)
                {
                    if (firstDefault == -1) firstDefault = i;
                    else { isDefault.boolValue = false; changed = true; } //Second Default found: clear it
                }

                if (isActive.boolValue)
                {
                    if (firstActive == -1) firstActive = i;
                    else { isActive.boolValue = false; changed = true; } //Second Active found: clear it
                }
            }

            //No Set flagged at all (first Set ever created, or the flagged one was deleted): the first Set takes the role
            if (firstDefault == -1)
            {
                firstDefault = 0;
                actionSets.GetArrayElementAtIndex(0).FindPropertyRelative("m_Default").boolValue = true;
                changed = true;
            }

            //The Item always needs a Set to start on, and the Default one is the natural fallback
            if (firstActive == -1)
            {
                actionSets.GetArrayElementAtIndex(firstDefault).FindPropertyRelative("active").boolValue = true;
                changed = true;
            }

            return changed;
        }


        //MWC — The Default Set is the fallback every Ignored Set returns to, so it can never be ignored itself:
        //ItemActionSet.Ignored reports false for it and the flag is simply inert. Warn instead of silently dropping it.
        private void DrawIgnoredSetsWarning()
        {
            for (int i = 0; i < M.actionSets.Count; i++)
            {
                var set = M.actionSets[i];

                if (set != null && set.Ignore && set.Default)
                {
                    EditorGUILayout.HelpBox($"The Default Action Set [{set.Name}] cannot be ignored. " +
                        "The [Ignore] flag has no effect on it: it stays the Set every other Ignored Set falls back to.\n" +
                        "Mark a different Set as Default, or clear the Ignore flag", MessageType.Warning);
                    return;
                }
            }
        }
        private void ShowActionsSets()
        {
            Reo_ActionSets.DoLayoutList(); //Draw the Reorderable List for Action Sets 

            DrawIgnoredSetsWarning(); //MWC — the Default Set cannot be ignored

            if (Reo_ActionSets.index != -1 && actionSets.arraySize > 0 && Reo_ActionSets.index < actionSets.arraySize)
            {
                var SelectedSet = actionSets.GetArrayElementAtIndex(Reo_ActionSets.index);

                //EditorGUILayout.Space(-18);

                var name = M.actionSets[Reo_ActionSets.index].Name;

                // EditorGUILayout.PropertyField(SelectedActionSet, new GUIContent($"———— Set [{name}] ————"), false);

                using (new GUILayout.HorizontalScope())
                {

                    //Title of the Action Set with a foldout to show the actions of the set
                    SelectedSet.isExpanded = MalbersEditor.Foldout_Bold(SelectedSet.isExpanded, ($"Set [{name}]"), MTools.MBlue * 1.5f, true);

                    // ── Copy button overlaid on the right edge of the foldout ────
                    //  var foldoutRect = GUILayoutUtility.GetLastRect();
                    // var copyBtnRect = new Rect(foldoutRect.xMax - 55, foldoutRect.y + 20, 50, foldoutRect.height - 4);


                    var color = GUI.backgroundColor;
                    GUI.backgroundColor = MTools.MBlue * 1.5f;
                    if (GUILayout.Button(new GUIContent("Copy Set", "Copy this Action Set to clipboard"), bigButtonStyle, GUILayout.Width(85)))
                        CopyActionSetToClipboard(Reo_ActionSets.index);
                    GUI.backgroundColor = color;
                }

                // ─────────────────────────────────────────────────────────────

                if (SelectedSet.isExpanded)
                {
                    if (Application.isPlaying)
                    {

                        using (new EditorGUI.DisabledGroupScope(true))
                        {
                            using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.LabelField($"Master Action: [{(M.MainAction != null ? (M.MainAction.name.Value) : "None")}]", GUILayout.Width(150));
                                EditorGUILayout.LabelField($"Slave Action:  [{(M.SlaveAction != null ? (M.SlaveAction.name.Value) : "None")}]", GUILayout.Width(150));
                                EditorGUILayout.LabelField($"Blocked Actions [{M.BlockedActions.Count}]", EditorStyles.boldLabel, GUILayout.Width(130));
                                EditorGUILayout.LabelField(M.BlockedActions.ToString(), EditorStyles.miniLabel);
                            }
                        }
                    }

                    var Actions = SelectedSet.FindPropertyRelative("actions");
                    var EditorTab1 = SelectedSet.FindPropertyRelative("EditorTab1");


                    var ASet = M.actionSets[Reo_ActionSets.index].Name;

                    DrawItemActions(Reo_ActionSets.index, SelectedSet, Actions);
                }
            }



            void DrawItemActions(int ActionSetIndex, SerializedProperty selectedSet, SerializedProperty Actions)
            {
                ReorderableList Reo_ActionList;

                string listKey = selectedSet.propertyPath;

                var SelectedActionIndex = selectedSet.FindPropertyRelative("SelectedAction"); //Index

                if (Reo_Actions.ContainsKey(listKey))
                {
                    // fetch the reorderable list in dict
                    Reo_ActionList = Reo_Actions[listKey];
                }
                else
                {
                    Reo_ActionList = new ReorderableList(selectedSet.serializedObject, Actions, true, true, true, true)
                    {
                        drawHeaderCallback = (Rect rect) =>
                        {
                            var activeRect = new Rect(rect) { width = 48, x = rect.x + 15 };
                            var nameRect = new Rect(rect) { x = rect.x + 50, width = rect.width / 3 * 1.8f - 65 - 15 - 48 };
                            var priorityRect = new Rect(rect) { x = rect.x + 50 + rect.width / 3 * 1.8f - 65 - 15 - 48 + 4, width = 50 };
                            var idRect = new Rect(rect) { x = rect.width / 3 * 1.8f + 35, width = rect.width / 3 * 0.8f + 12 };
                            var activationRect = new Rect(rect) { x = rect.width / 3 * 2.6f + 50, width = rect.width / 3 * 0.4f };

                            EditorGUI.LabelField(activeRect, "Active", EditorStyles.boldLabel);
                            EditorGUI.LabelField(nameRect, "   Name", EditorStyles.boldLabel);
                            EditorGUI.LabelField(priorityRect, "Priority", EditorStyles.boldLabel);
                            EditorGUI.LabelField(idRect, "Action", EditorStyles.boldLabel);
                            EditorGUI.LabelField(activationRect, "Input", EditorStyles.boldLabel);

                            // ── Paste / Clear clipboard buttons ──────────────
                            if (!string.IsNullOrEmpty(s_ItemActionClipboard))
                            {
                                var clearRect = new Rect(rect) { width = 45, x = rect.x + rect.width - 120 - AUTO_W }; //shifted left of the [Auto] column
                                var pasteRect = new Rect(rect) { width = 45, x = rect.x + rect.width - 170 - AUTO_W };

                                if (GUI.Button(pasteRect, new GUIContent("Paste", "Paste copied Item Action as new entry")))
                                    PasteItemActionFromClipboard(ActionSetIndex);

                                if (GUI.Button(clearRect, new GUIContent("Clear", "Clear the Item Action clipboard")))
                                    s_ItemActionClipboard = null;
                            }
                            // ─────────────────────────────────────────────────
                        },

                        drawElementCallback = (rect, index, isActive, isFocused) =>
                        {
                            rect.y += 2;

                            var element = Actions.GetArrayElementAtIndex(index);

                            var ActionID = element.FindPropertyRelative("ActionID");
                            var name = element.FindPropertyRelative("name");
                            var priority = element.FindPropertyRelative("priority");

                            //Active Bool
                            var Active = element.FindPropertyRelative("active");
                            var activation = element.FindPropertyRelative("activation");
                            var ConstValue = Active.FindPropertyRelative("ConstantValue");
                            var VarValue = Active.FindPropertyRelative("Variable");
                            var useConstant = Active.FindPropertyRelative("UseConstant").boolValue;
                            BoolVar variable = VarValue.objectReferenceValue as BoolVar;


                            if (M.actionSets.Count <= ActionSetIndex || ActionSetIndex < 0)
                                return; //If the Action Set is not valid, return

                            if (M.actionSets[ActionSetIndex].actions.Count <= index || index < 0)
                            { return; }

                            var itemAction = M.actionSets[ActionSetIndex].actions[index];

                            var IDRect = new Rect(rect) { height = EditorGUIUtility.singleLineHeight };

                            var NameRect = new Rect(IDRect);

                            var ActivationRect = new Rect(IDRect);

                            var ActiveRect = new Rect(rect.x + 15, rect.y, 20, rect.height);



                            NameRect.x += 15 + 35;
                            NameRect.width = rect.width / 3 * 1.8f - 65 - 15 - 48;

                            var PriorityRect = new Rect(NameRect) { x = NameRect.x + NameRect.width + 4, width = 44 };

                            IDRect.x = rect.width / 3 * 1.8f + 35;
                            IDRect.width = rect.width / 3 * 0.8f + 12;


                            ActivationRect.x = rect.width / 3 * 2.6f + 50;
                            ActivationRect.width = rect.width / 3 * 0.4f - 5;

                            var indexRect = new Rect(rect) { width = 20, x = rect.x };
                            EditorGUI.LabelField(indexRect, (index).ToString(), EditorStyles.boldLabel);


                            if (useConstant)
                            {
                                ConstValue.boolValue = EditorGUI.Toggle(ActiveRect, GUIContent.none, ConstValue.boolValue);

                                if (variable != null)
                                {
                                    variable.Value = ConstValue.boolValue;
                                    EditorUtility.SetDirty(variable);
                                }
                            }
                            else
                            {
                                if (variable != null)
                                {
                                    variable.Value = EditorGUI.Toggle(ActiveRect, GUIContent.none, variable.Value);
                                    ConstValue.boolValue = variable.Value;
                                }
                                else
                                {
                                    ConstValue.boolValue = EditorGUI.Toggle(ActiveRect, GUIContent.none, ConstValue.boolValue);
                                }
                            }

                            var dC = GUI.backgroundColor;

                            if (itemAction.IsPlaying)
                            {
                                if (itemAction.MasterAction != null)
                                    GUI.backgroundColor = Color.yellow * 4;
                                else
                                    GUI.backgroundColor = MTools.MGreen * 4;
                            }
                            else if (SelectedActionIndex.intValue == index)
                            {
                                GUI.backgroundColor = MTools.MBlue * 4;
                            }


                            EditorGUI.PropertyField(NameRect, name, GUIContent.none);
                            EditorGUI.PropertyField(PriorityRect, priority, GUIContent.none);

                            if (Application.isPlaying)
                            {
                                var w = 50;

                                IDRect.width -= w - 25; //If we are playing, remove the Default Action Set from the name Rect
                                var inputValueRect = new Rect(IDRect) { width = 20, x = IDRect.x + IDRect.width + 5 };

                                EditorGUI.LabelField(inputValueRect, $"[{(itemAction.InputValue ? "•" : "  ")}]", EditorStyles.boldLabel);
                            }


                            EditorGUIUtility.labelWidth = 56;
                            EditorGUI.PropertyField(IDRect, ActionID, GUIContent.none);
                            EditorGUIUtility.labelWidth = 0;


                            GUI.backgroundColor = dC;
                            EditorGUI.PropertyField(ActivationRect, activation, GUIContent.none);
                        },

                        onAddCallback = (list) =>
                        {
                            var actionList = M.actionSets[ActionSetIndex].actions;

                            actionList ??= new List<ItemAction>();

                            // Create a new ItemAction and add it to the action list
                            ItemAction newAction = new()
                            {
                                name = new StringReference($"New Action [{actionList.Count + 1}]"),
                                active = new BoolReference(true),
                                ActionID = MTools.GetInstance<ItemActionID>("Action1"),
                                ConditionsOnOwner = new() { active = true }
                            };

                            actionList.Add(newAction);
                            EditorUtility.SetDirty(M);
                        },
                        onSelectCallback = (list) =>
                        {
                            SelectedActionIndex.intValue = list.index;
                            GetMasterActionPopup();
                            DrawValidation();
                        },

                        index = SelectedActionIndex.intValue
                    };

                    Reo_Actions.Add(listKey, Reo_ActionList);  //Store it on the Editor
                }

                Reo_ActionList.DoLayoutList();

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    DrawSelectedAction(selectedSet, ActionSetIndex, Actions, SelectedActionIndex);
                }
            }

        }

        private static void Draw_Extras(SerializedProperty set)
        {
            var AutoActivation = set.FindPropertyRelative("AutoActivation");

            if (AutoActivation.intValue != 0)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var AutoActivationOn = set.FindPropertyRelative("AutoActivationOn");
                    var AutoActivationOff = set.FindPropertyRelative("AutoActivationOff");
                    //var ReturnToDefault = set.FindPropertyRelative("ReturnToDefault");
                    EditorGUI.indentLevel++;
                    //EditorGUILayout.PropertyField(ReturnToDefault);
                    EditorGUILayout.PropertyField(AutoActivationOn, new GUIContent("Auto Activation On [Dynamic Target: Current User]"));
                    EditorGUILayout.PropertyField(AutoActivationOff, new GUIContent("Auto Activations Off [Dynamic Target: Current User]"));
                    EditorGUI.indentLevel--;
                }
            }

            //MWC — Interruption sources: entering any of these States/Modes/Stances force-ends the Set's playing Actions
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Set Interruptions", EditorStyles.centeredGreyMiniLabel);

                EditorGUILayout.PropertyField(set.FindPropertyRelative("InterruptStates"));
                EditorGUILayout.PropertyField(set.FindPropertyRelative("InterruptModes"));
                EditorGUILayout.PropertyField(set.FindPropertyRelative("InterruptStances"));

                EditorGUILayout.HelpBox(
                    "The Character entering any of these interrupts the playing Actions of this Set (pending Processor delays are dropped).\n" +
                    "[✓] Include: only the listed IDs interrupt. [✕] Exclude: everything EXCEPT them interrupts. Empty: never.",
                    MessageType.None);
            }
        }
        private void Draw_IK(SerializedProperty SelectedSet, int ActionSetIndex)
        {
            var IKProfile = SelectedSet.FindPropertyRelative("IKProfile");

            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(IKProfile);
                RuntimeIK.boolValue = GUILayout.Toggle(RuntimeIK.boolValue, new GUIContent("Runtime"), EditorStyles.miniButton, GUILayout.Width(100));
            }

            //MWC — [IKProfile] is the AUTHORED asset now, it is no longer swapped in place at runtime. While playing,
            //edit the Profile the IK pass is actually running (the rig override and/or the Runtime copy), otherwise
            //tweaking values here would change an asset nothing is reading.
            var set = Application.isPlaying && M.actionSets != null && ActionSetIndex >= 0 && ActionSetIndex < M.actionSets.Count
                ? M.actionSets[ActionSetIndex] : null;

            var liveProfile = set != null ? set.CurrentIKProfile : null;

            if (liveProfile != null && liveProfile != IKProfile.objectReferenceValue)
            {
                //The IK Set actually running on the character: the Runtime copy, the rig Remap, or both. Shown as a
                //read-only field so it can be pinged and inspected; the inline editor below edits THIS one, not the asset.
                var remap = set.IKProfileRemapped && M.User != null && M.User.IKManager != null && M.User.IKManager.Remap != null ? M.User.IKManager.Remap.name : null;

                var label = set.IKProfileIsRuntimeCopy
                    ? new GUIContent("Runtime IK Set", $"Runtime copy of [{IKProfile.objectReferenceValue.name}]" +
                        (remap != null ? $" remapped through [{remap}]" : "") + ". Destroyed when the Item leaves the Set. Read only.")
                    : new GUIContent("Remapped IK Set", $"Rig asset [{liveProfile.name}] resolved from [{IKProfile.objectReferenceValue.name}]" +
                        (remap != null ? $" through [{remap}]" : "") + ". Used directly ([Runtime] is off). Read only.");

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(label, liveProfile, typeof(MalbersAnimations.IK.IKSetProfile), false); //Read only: the result is discarded

                    GUILayout.Label(set.IKProfileIsRuntimeCopy ? "Playing" : "Asset", EditorStyles.miniButton, GUILayout.Width(100));
                }

                EditorGUILayout.HelpBox($"Playing: editing [{liveProfile.name}], resolved from [{IKProfile.objectReferenceValue.name}]" +
                    $"{(remap != null ? $" through [{remap}]" : "")}.", MessageType.Info);
            }

            //MWC — [Runtime] off is the tuning pass: the Item writes into the asset so Play Mode edits survive. Spell out
            //WHICH asset is being written (the remapped one when there is a Remap) and that a build always copies.
            if (set != null && !RuntimeIK.boolValue && liveProfile != null)
            {
                EditorGUILayout.HelpBox($"[Runtime] is off: you are editing the asset [{liveProfile.name}] directly, so these " +
                    "values persist after Play Mode. Only one character may hold this Item while tuning — the IK Processors " +
                    "cache per character state. In a build a remapped Profile is always copied.", MessageType.Warning);
            }
            else if (!RuntimeIK.boolValue && M.User == null)
            {
                EditorGUILayout.HelpBox("[Runtime] is off: the Profile asset is used directly, so Play Mode edits are stored in it. " +
                    "Turn it back on when you are done tuning — IK Processors cache per character state.", MessageType.None);
            }

            if (IKProfile.objectReferenceValue != null)
            {
                // MMDrawnPropertiesEditor.MMDrawnProperties(element.objectReferenceValue);
                var key = IKProfile.propertyPath;

                //The live Profile changes when the Item is equipped by another character, so it cannot share the cache key
                var drawn = liveProfile != null ? (Object)liveProfile : IKProfile.objectReferenceValue;

                if (IK_Editor.TryGetValue(key, out Editor editor) && editor != null && editor.target == drawn)
                {
                    editor = IK_Editor[key];
                }
                else
                {
                    Editor.CreateCachedEditor(drawn, null, ref editor);
                    IK_Editor[key] = editor;
                }

                var IKTargets = SelectedSet.FindPropertyRelative("IKTargets");
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(IKTargets, new GUIContent("Dynamic IK Targets"));
                    EditorGUI.indentLevel--;
                }

                editor.OnInspectorGUI();
                editor.serializedObject.ApplyModifiedProperties();
            }
        }
        private void GetMasterActionPopup()
        {
            var SetIndex = SelectedActionSetIndex.intValue;

            if (SetIndex >= M.actionSets.Count) return; //If the Action Set Index is not valid, return

            MasterPopupList = new string[M.actionSets[SetIndex].actions.Count + 1];
            MasterPopupList[0] = "None";

            for (int i = 0; i < M.actionSets[SetIndex].actions.Count; i++)
            {
                MasterPopupList[i + 1] = M.actionSets[SetIndex].actions[i].name.Value;
            }
            // Debug.Log("Changed");
        }

        private string[] MasterPopupList;

        private void DrawSelectedAction(SerializedProperty selectedSet, int ActionSetIndex, SerializedProperty Actions, SerializedProperty SelectedActionIndex)
        {
            if (SelectedActionIndex.intValue != -1 && SelectedActionIndex.intValue < Actions.arraySize)
            {
                // Debug.Log("SelectedAbility = " + SelectedAbility);
                SerializedProperty actionSP = Actions.GetArrayElementAtIndex(SelectedActionIndex.intValue);

                if (M.actionSets.Count <= ActionSetIndex || ActionSetIndex < 0)
                    return; //If the Action Set is not valid, return

                if (M.actionSets[ActionSetIndex].actions.Count <= SelectedActionIndex.intValue || SelectedActionIndex.intValue < 0)
                    return; //If the Action is not valid, return

                var action = M.actionSets[ActionSetIndex].actions[SelectedActionIndex.intValue];

                var actionName = action.Name;
                var SetName = M.actionSets[ActionSetIndex].Name;

                var dependency = action.masterIndex > 0 ? $"→ [Requires: {M.actionSets[ActionSetIndex].actions[action.masterIndex - 1].Name}]" : "";

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUI.indentLevel++;
                    actionSP.isExpanded = MalbersEditor.Foldout_Bold(actionSP.isExpanded, $"Action [{actionName}]{dependency}", actionSP.isExpanded ? MTools.MBlue * 1.5f : MTools.MDarkGray, true);
                    EditorGUI.indentLevel--;


                    var color = GUI.backgroundColor;
                    GUI.backgroundColor = MTools.MBlue * 1.5f;
                    // ── Copy button overlaid on the right edge of the action foldout ──
                    if (GUILayout.Button(new GUIContent("Copy Action", "Copy this Item Action to clipboard"), bigButtonStyle, GUILayout.Width(85)))
                        CopyItemActionToClipboard(ActionSetIndex, SelectedActionIndex.intValue);
                    GUI.backgroundColor = color;
                }

                // ─────────────────────────────────────────────────────────────────

                if (actionSP != null && actionSP.isExpanded)
                {
                    if (Application.isPlaying)
                    {
                        using (new EditorGUI.DisabledGroupScope(true))
                        {
                            EditorGUIUtility.labelWidth = 180;
                            using (new GUILayout.HorizontalScope())
                            {
                                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                {
                                    EditorGUILayout.Toggle("Is Playing", action.IsPlaying);
                                    EditorGUILayout.Toggle("Stop Action On Release", action.StopActionOnRelease);
                                    EditorGUILayout.Toggle("Input", action.InputValue);
                                    EditorGUILayout.TextField("Master Action", action.MasterAction != null ? action.MasterAction.Name : "None");
                                }
                                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                {
                                    EditorGUILayout.Toggle("Can Exit", action.CanExit);
                                    EditorGUILayout.Toggle("On Hold By 2nd", action.OnHoldBySecondary);
                                    EditorGUILayout.Toggle("Is Slave", action.IsSecondary);
                                }
                                EditorGUIUtility.labelWidth = 0;
                                Repaint();
                            }
                        }
                    }


                    //MWC — item-level tab index: selecting another Action no longer jumps to whatever tab that
                    //Action had open last, so you stay on Processors (or Conditions…) while browsing the Actions.
                    var EditorTab1 = Editor_ActionTabs;

                    using (new GUILayout.HorizontalScope())
                    {
                        var prevBG = GUI.backgroundColor;
                        //GUI.backgroundColor = MTools.MBlue;
                        var actionTabStyle = new GUIStyle(EditorStyles.objectFieldThumb) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, fontSize = 13, fixedHeight = 20, };

                        int TotalProcessors = action.processors != null ? action.processors.Length : 0; //MWC: guard null processors (Unity can null SerializeReference arrays after a recompile)
                        var TotalReactions = action.OnActionExit.Count + action.OnActionPressed.Count + action.OnActionReleased.Count + action.OnActionFailedOnOwner.Count + action.OnActionFailedOnTarget.Count + action.OnActionStart.Count;
                        var TotalConditions = action.ConditionsOnOwner.Count + action.ConditionsOnTarget.Count + M.actionSets[ActionSetIndex].SetConditions.Count;

                        EditorTab1.intValue = GUILayout.Toolbar(EditorTab1.intValue,
                            new string[] { $"Processors {(TotalProcessors>0 ? $" [{TotalProcessors}]" : "")}",
                                $"Conditions {(TotalConditions>0 ? $" [{TotalConditions}]" : "")} ",
                                $"Reactions{(TotalReactions>0 ? $" [{TotalReactions}]" : "")}",
                                $"IK {(M.actionSets[ActionSetIndex].IKProfile != null ? "◉" : "")}",
                                $"Auto {(M.actionSets[ActionSetIndex].AutoActivation != 0 ? "◉" : "")}" }
                            , actionTabStyle);
                        GUI.backgroundColor = prevBG;
                    }
                    var debug = actionSP.FindPropertyRelative("debug");
                    var processors = actionSP.FindPropertyRelative("processors");

                    if (EditorTab1.intValue == 0)
                    {
                        DrawProcessors(SelectedActionIndex, actionSP, action, debug, processors, selectedSet);
                    }

                    else if (EditorTab1.intValue == 1) //Reactions
                    {
                        DrawActionConditions(actionSP, selectedSet, SetName, actionName);
                    }
                    else if (EditorTab1.intValue == 2) //Reactions
                    {
                        DrawReactions(actionSP, action.Name);
                        Draw_SetReactions(selectedSet);
                    }
                    else if (EditorTab1.intValue == 3) //IK
                    {
                        Draw_IK(selectedSet, ActionSetIndex); //MWC — index needed to reach the resolved runtime Profile
                    }
                    else if (EditorTab1.intValue == 4) //Extras
                    {
                        Draw_Extras(selectedSet);
                    }
                }
            }
        }

        private void DrawReactions(SerializedProperty actionSP, string AAction)
        {
            EditorGUILayout.LabelField($"Action [{AAction}] Reactions", EditorStyles.centeredGreyMiniLabel);
            var OnActionPressed = actionSP.FindPropertyRelative("OnActionPressed");
            var OnActionReleased = actionSP.FindPropertyRelative("OnActionReleased");
            var OnActionFailedOnTarget = actionSP.FindPropertyRelative("OnActionFailedOnTarget");
            var OnActionFailedOnOwner = actionSP.FindPropertyRelative("OnActionFailedOnOwner");
            var OnActionExit = actionSP.FindPropertyRelative("OnActionExit");
            var OnActionStart = actionSP.FindPropertyRelative("OnActionStart");
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(OnActionStart, new GUIContent($"[{AAction}] On Start"));
            EditorGUILayout.PropertyField(OnActionExit, new GUIContent($"[{AAction}] On Exit"));
            EditorGUILayout.PropertyField(OnActionPressed, new GUIContent($"[{AAction}] On Pressed"));
            EditorGUILayout.PropertyField(OnActionReleased, new GUIContent($"[{AAction}] On Released"));
            EditorGUILayout.PropertyField(OnActionFailedOnOwner, new GUIContent($"[{AAction}] On Failed on Owner"));
            EditorGUILayout.PropertyField(OnActionFailedOnTarget, new GUIContent($"[{AAction}] On Failed on Target"));
            EditorGUI.indentLevel--;
        }

        private void Draw_SetReactions(SerializedProperty SelectedActionSet)
        {
            var ASet = M.actionSets[Reo_ActionSets.index].Name;

            var OnEnterReaction = SelectedActionSet.FindPropertyRelative("OnEnterSetReaction");
            var OnExitReaction = SelectedActionSet.FindPropertyRelative("OnExitSetReaction");
            var OnUnequipReaction = SelectedActionSet.FindPropertyRelative("OnUnequipReaction");
            var OnEquipReaction = SelectedActionSet.FindPropertyRelative("OnEquipReaction");

            EditorGUILayout.LabelField($"Set [{ASet}] Reactions", EditorStyles.centeredGreyMiniLabel);

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(OnEnterReaction, new GUIContent($"[{ASet}] On Enter"));
            EditorGUILayout.PropertyField(OnExitReaction, new GUIContent($"[{ASet}] On Exit"));
            EditorGUILayout.PropertyField(OnEquipReaction, new GUIContent($"[{ASet}] On Equip Item "));
            EditorGUILayout.PropertyField(OnUnequipReaction, new GUIContent($"[{ASet}] On Unequip Item "));

            //MWC — fired when an Interrupt State/Mode/Stance kills the Set's playing Actions
            var OnInterruptReaction = SelectedActionSet.FindPropertyRelative("OnInterruptReaction");
            EditorGUILayout.PropertyField(OnInterruptReaction, new GUIContent($"[{ASet}] On Interrupted "));

            EditorGUI.indentLevel--;
        }

        private void DrawActionConditions(SerializedProperty actionSP, SerializedProperty sset, string selectedSet, string actionName)
        {
            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                var ConditionsOnOwner = actionSP.FindPropertyRelative("ConditionsOnOwner");
                var ConditionsOnTarget = actionSP.FindPropertyRelative("ConditionsOnTarget");
                var SetConditions = sset.FindPropertyRelative("SetConditions");
                EditorGUILayout.LabelField($"Action [{actionName}] Conditions", EditorStyles.centeredGreyMiniLabel);
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(ConditionsOnOwner, new GUIContent($"Action [{actionName}] Conditions on [Owner]"));
                EditorGUILayout.PropertyField(ConditionsOnTarget, new GUIContent($"Action [{actionName}] Conditions on [Target]"));
                EditorGUILayout.LabelField($"Set [{selectedSet}] Conditions", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.PropertyField(SetConditions, new GUIContent($"Set [{selectedSet}] Conditions"));
                EditorGUI.indentLevel--;

                DrawTriggerProxyConditions(actionSP);

                if (cc.changed)
                {
                    DrawValidation();
                }
            }
        }

        private SerializedObject triggerProxySO; //Cached per Trigger Proxy so the Conditions tab does not allocate one every repaint

        /// <summary>Draws the Conditions of the Action's Trigger Proxy (when assigned) inside the Action Conditions tab, so they can be edited without leaving the Item</summary>
        private void DrawTriggerProxyConditions(SerializedProperty actionSP)
        {
            var proxy = actionSP.FindPropertyRelative("Trigger").objectReferenceValue as TriggerProxy;
            if (proxy == null) return;

            if (triggerProxySO == null || triggerProxySO.targetObject != proxy)
                triggerProxySO = new SerializedObject(proxy);

            triggerProxySO.Update();

            EditorGUILayout.LabelField($"Trigger Proxy [{proxy.name}] Conditions", EditorStyles.centeredGreyMiniLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(triggerProxySO.FindProperty(nameof(TriggerProxy.Conditions)), new GUIContent("OnTrigger Enter Conditions", "Extra conditions the Trigger Proxy checks to filter the colliders entering it. Edited on the Trigger Proxy component"));
            EditorGUI.indentLevel--;

            triggerProxySO.ApplyModifiedProperties();
        }

        //MWC: Creates a child GameObject under the Super Item with a Trigger Proxy + trigger Box Collider and assigns it to the Action's Trigger reference
        private void CreateTriggerProxyForAction(SerializedProperty triggerProp, ItemAction action)
        {
            var actionName = action.Name;
            var go = new GameObject(string.IsNullOrEmpty(actionName) ? "Attack Trigger" : $"{actionName} Trigger");

            Undo.RegisterCreatedObjectUndo(go, "Create Trigger Proxy");
            Undo.SetTransformParent(go.transform, M.transform, "Create Trigger Proxy");

            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            go.layer = M.gameObject.layer; //Match the item's layer

            //Trigger Box Collider (small blade-like starting size - the user positions/resizes it)
            var box = Undo.AddComponent<BoxCollider>(go);
            box.isTrigger = true;
            box.size = new Vector3(0.1f, 0.1f, 0.5f);

            var proxy = Undo.AddComponent<MalbersAnimations.Utilities.TriggerProxy>(go);

            //Assign the new Trigger Proxy to the Action's Trigger reference
            triggerProp.objectReferenceValue = proxy;
            serializedObject.ApplyModifiedProperties();

            EditorGUIUtility.PingObject(go); //Point the user to the new GameObject in the hierarchy
        }

        private static GUIContent ActionDebugCont;

        /// <summary> True when [evt] is a right click (or a Ctrl+Click on macOS). </summary>
        private static bool IsRightClick(Event evt) =>
            evt.type == EventType.MouseDown && (evt.button == 1 || (evt.button == 0 && evt.control && Application.platform == RuntimePlatform.OSXEditor));

        /// <summary> Debug icon for an Action. Right Click opens a menu to enable/disable the Debug of all the Actions of the Set. </summary>
        private void DrawActionDebugIcon(SerializedProperty debug, SerializedProperty selectedSet)
        {
            ActionDebugCont ??= new GUIContent(MalbersEditor.DebugCont)
            { tooltip = "Debug this Action.\nRight Click: Enable/Disable the Debug on all the Actions of this Set" };

            //Reserve the rect first: the Toggle eats the right click, so a ContextClick event never arrives here
            var iconRect = GUILayoutUtility.GetRect(ActionDebugCont, EditorStyles.miniButtonMid, GUILayout.Width(28), GUILayout.Height(20));

            var e = Event.current;

            if (IsRightClick(e) && iconRect.Contains(e.mousePosition))
            {
                var setPath = selectedSet.propertyPath; //The menu callback runs later, so keep the path, not the property

                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Enable All Debugs (Set)"), false, () => SetAllActionsDebug(setPath, true));
                menu.AddItem(new GUIContent("Disable All Debugs (Set)"), false, () => SetAllActionsDebug(setPath, false));
                menu.ShowAsContext();

                e.Use();
            }

            var currentGUIColor = GUI.color;
            GUI.color = debug.boolValue ? Color.red : currentGUIColor;
            debug.boolValue = GUI.Toggle(iconRect, debug.boolValue, ActionDebugCont, EditorStyles.miniButtonMid);
            GUI.color = currentGUIColor;
        }

        /// <summary> Enables or disables the Debug on every Action of the Action Set stored at [setPath]. </summary>
        private void SetAllActionsDebug(string setPath, bool value)
        {
            if (serializedObject == null || serializedObject.targetObject == null) return;

            serializedObject.Update();

            var actions = serializedObject.FindProperty(setPath)?.FindPropertyRelative("actions");
            if (actions == null) return;

            for (int i = 0; i < actions.arraySize; i++)
                actions.GetArrayElementAtIndex(i).FindPropertyRelative("debug").boolValue = value;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
            Repaint();
        }


        private void DrawProcessors(SerializedProperty SelectedActionIndex, SerializedProperty actionSP, ItemAction action, SerializedProperty debug, SerializedProperty processors, SerializedProperty selectedSet)
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var masterIndex = actionSP.FindPropertyRelative("masterIndex");

                using (new GUILayout.HorizontalScope())
                {
                    if (MasterPopupList.Length > SelectedActionIndex.intValue + 1)
                        MasterPopupList[SelectedActionIndex.intValue + 1] = " "; //Do not show the current action as a Master Action, An action cannot have a Master Action if itself is the Master Action

                    masterIndex.intValue = EditorGUILayout.Popup(
                  new GUIContent("Master Action", "Action required to activate this. E.g. Firing arrow requires Aim to be active. Or Parry need Block to be active"), masterIndex.intValue, MasterPopupList);


                    DrawActionDebugIcon(debug, selectedSet);

                }

                //var stopActionOnRelease = actionSP.FindPropertyRelative("stopActionOnRelease");
                var TriggerProxy = actionSP.FindPropertyRelative("Trigger");
                var rate = actionSP.FindPropertyRelative("rate");
                var updateRate = actionSP.FindPropertyRelative("updateRate"); //MWC — repeat rate for the [On Action Updated] processors
                var duration = actionSP.FindPropertyRelative("duration");
                var interrupSelf = actionSP.FindPropertyRelative("interrupSelf");
                var primaryHandOnly = actionSP.FindPropertyRelative("primaryHandOnly"); //MWC: Dual Wielding
                var restartOnConditions = actionSP.FindPropertyRelative("restartOnConditions"); //MWC — auto restart when the Conditions pass again


                var ID = action.Trigger != null ? $"[ID:{action.Trigger.ID.Value}]" : "";

                //MWC: Trigger reference + quick-create [+] button (only shown when no Trigger is assigned)
                using (new GUILayout.HorizontalScope())
                {
                    var Proxy = (TriggerProxy)TriggerProxy.objectReferenceValue;

                    var color = GUI.color;

                    if (Proxy != null && !Proxy.transform.SameHierarchy(M.transform))
                        GUI.color = Color.red + Color.yellow * 0.5f;


                    EditorGUILayout.PropertyField(TriggerProxy, new GUIContent("Trigger  " + ID));

                    GUI.color = color;

                    if (TriggerProxy.objectReferenceValue == null)
                    {
                        if (GUILayout.Button(new GUIContent("+", "Create a child GameObject with a Trigger Proxy and a Box Collider, and assign it here"),
                            GUILayout.Width(24)))
                        {
                            CreateTriggerProxyForAction(TriggerProxy, action);
                            GUIUtility.ExitGUI(); //The hierarchy changed mid-GUI - abort this repaint cleanly
                        }
                    }
                }

                EditorGUILayout.PropertyField(rate, new GUIContent("Rate (CoolDown)"));
                EditorGUILayout.PropertyField(updateRate, new GUIContent("Update Rate (Repeat)"));
                EditorGUILayout.PropertyField(duration);
                EditorGUILayout.PropertyField(interrupSelf);
                EditorGUILayout.PropertyField(primaryHandOnly, new GUIContent("Primary Hand Only")); //MWC: Dual Wielding
                EditorGUILayout.PropertyField(restartOnConditions, new GUIContent("Restart On Conditions"));

                //MWC — [Fast Weapons] (Assassin's Creed style)
                EditorGUILayout.PropertyField(actionSP.FindPropertyRelative("fastEquip"), new GUIContent("Fast Equip",
                    "[Fast Weapons]: Pressing this Action's input while the Item is HOLSTERED draws it instantly " +
                    "(skipping the Equip animation) and starts this Action.\n\n" +
                    "• 0 = this Action is never fast equipped.\n" +
                    "• Higher WINS when several holstered Items answer the same input.\n" +
                    "E.g. Sword [Charge Attack] = 1 and Two Hand Axe [Charge Attack] = 2 → carrying both draws the Axe.\n\n" +
                    "• It also ranks against the Item ALREADY IN HAND: an equipped Action keeps its input only while " +
                    "its own value is not below the best holstered candidate's (a tie keeps it in hand).\n" +
                    "E.g. an equipped Dagger [Attack] = 0 gives the press to a holstered Sword [Attack] = 2.\n\n" +
                    "Requires [Use Fast Weapons] on the Super Item Manager"));
                EditorGUILayout.PropertyField(actionSP.FindPropertyRelative("validOnFastWeapons"), new GUIContent("Valid On Fast Weapons",
                    "[Fast Weapons]: When disabled, this Action ALWAYS yields its input to another item's [Fast Equip] " +
                    "Action while such a holstered candidate exists, whatever the two [Fast Equip] values are.\n" +
                    "E.g. the Bow's [Quick Fire] is skipped so the holstered Sword is fast-drawn and attacks instead.\n\n" +
                    "When ENABLED the Action still yields to a candidate that OUTRANKS its own [Fast Equip] value"));

                //MWC — Hands the Action takes only while it plays (Aiming a Bow, bracing a Rifle, a 2H swing)
                EditorGUILayout.PropertyField(actionSP.FindPropertyRelative("FreeHands"), new GUIContent("Free Hands [Action]",
                    "Hands this Action FREES from the Rein while it is PLAYING.\n" +
                    "Adds to the hand of the Equip Point and to the ones the Set declares. Released when the Action ends"));
            }

            //using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(processors, new GUIContent($"Processors [{processors.arraySize}]"));
                //EditorGUI.indentLevel--;
            }
        }

        /// <summary>True while this editor hides Unity's transform tool so only the IK Processor handles show.</summary>
        private bool hidToolsForIK;

        /// <summary>Restores Unity's transform tool if the IK handles hid it.</summary>
        protected virtual void OnDisable() => HideToolsForIK(false);

        /// <summary>Hides or restores Unity's transform tool on the item, only touching it when this editor hid it.</summary>
        private void HideToolsForIK(bool hide)
        {
            if (hide == hidToolsForIK) return;
            Tools.hidden = hide;
            hidToolsForIK = hide;
        }

        private void OnSceneGUI()
        {
            //The item's own Move/Rotate gizmo sits on top of the IK handles; hide it while they are drawn
            HideToolsForIK(DrawIKProcessorHandles());
        }

        /// <summary>Draws the selected IK Processor handles of the active Profile; returns true when any were drawn.</summary>
        private bool DrawIKProcessorHandles()
        {
            if (!Application.isPlaying) return false;

            //MWC — the resolved Profile: in Play Mode this is the rig override / Runtime copy, not the authored asset
            if (M.ActiveActionSet != null && M.User != null && M.ActiveActionSet.CurrentIKProfile != null)
            {
                var IKProfile = M.ActiveActionSet.CurrentIKProfile;
                var set = IKProfile.set;

                // The IK Set Drawer stores the selected Processor on the SET (IKSet.SelectedIKProcessor), the same slot
                // the IK Manager reads. A stale int on the Profile asset used to be read here instead, so the handles
                // always belonged to Processor [0] no matter which one was selected in the Inspector.
                if (set == null || !set.active || !set.Gizmos || set.Processors == null) return false;

                var index = set.SelectedIKProcessor;

                if (index < 0 || index >= set.Processors.Count) return false;

                var link = set.Processors[index];

                if (link != null && link.Active)
                {
                    link.OnSceneGUI(set, M.User.Anim, IKProfile, index);
                    return link.HasSceneHandles; //Processors without handles keep the item's own gizmo
                }
            }
            return false;
        }
    }
#endif
}
