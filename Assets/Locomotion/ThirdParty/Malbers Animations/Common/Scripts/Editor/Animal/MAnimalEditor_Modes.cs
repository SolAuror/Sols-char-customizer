#if UNITY_EDITOR
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    // MWC - partial: Modes tab — ShowModes, DrawAbilities, and reorderable list callbacks
    public partial class MAnimalEditor
    {
        private void ShowModes()
        {
            using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(StartWithMode, G_StartWithMode);
                MalbersEditor.DrawDebugIcon(DebugModes, MTools.MGreen);


                if (Application.isPlaying)
                {
                    var color = GUI.color;
                    GUI.color = MTools.MGreen * 1.5f;
                    if (GUILayout.Button(new GUIContent("Interrupt", "Interrupt Mode"), GUILayout.Width(60)))
                    {
                        m.Mode_Interrupt();
                    }
                    GUI.color = color;
                }
            }

            //MWC: Warn when two Modes share the same ID (causes a duplicate-key crash on Awake/CacheAllModes).
            DrawDuplicateModeIDWarning();

            Reo_List_Modes.index = SelectedMode.intValue;
            var index = SelectedMode.intValue;

            //On Empty List
            if (S_Mode_List.arraySize == 0 || SelectedMode.intValue == -1)
            {
                Reo_List_Modes.DoLayoutList();        //Paint the Reordable List
            }

            if (index != -1 && m.modes.Count > 0 && index < m.modes.Count && S_Mode_List != null && S_Mode_List.arraySize > 0)
            {
                var CurrentMode = S_Mode_List.GetArrayElementAtIndex(index);

                var mode = m.modes[index];

                var ID = CurrentMode.FindPropertyRelative("ID").objectReferenceValue;
                var SelectedAbilityIndexEditor = CurrentMode.FindPropertyRelative("SelectedAbilityIndexEditor");
                var ModeName = ID != null ? ID.name : "";
                var ModeID = ID != null ? (ID as ModeID).ID : -1;

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (targets != null && targets.Length > 1)
                    {
                        EditorGUILayout.EndVertical();
                        return; //Do not show Multiple Animals
                    }

                    using (new GUILayout.HorizontalScope())
                    {
                        showModeList.boolValue = MalbersEditor.Foldout(showModeList.boolValue, $"Modes List [{S_Mode_List.arraySize}].    Selected:[{ModeName}]     ID:[{ModeID}]");


                        //if (!showModeList.boolValue)
                        //{
                        //    SelectedMode.intValue = EditorGUILayout.Popup( SelectedMode.intValue, ModePopupList, popupStyle, GUILayout.Width(150));
                        //}
                    }

                    if (showModeList.boolValue)
                    {
                        Reo_List_Modes.DoLayoutList();        //Paint the Reordable List
                    }
                }

                if (CurrentMode != null)
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        //Mode Title
                        using (new GUILayout.HorizontalScope())

                        {
                            if (!showModeList.boolValue)
                            {
                                SelectedMode.intValue = EditorGUILayout.Popup(SelectedMode.intValue, ModePopupList, popupStyle, GUILayout.Width(15));
                            }

                            EditorGUI.indentLevel++;
                            CurrentMode.isExpanded = GUILayout.Toggle(CurrentMode.isExpanded, new GUIContent("Mode"), EditorStyles.foldoutHeader);
                        }

                        GUILayout.Space(-20);
                        var IDD = CurrentMode.FindPropertyRelative("ID");

                        EditorGUIUtility.labelWidth = 80;
                        using (new EditorGUI.DisabledGroupScope(true))
                            EditorGUILayout.ObjectField(IDD, new GUIContent("  "), GUILayout.MinWidth(50));
                        EditorGUIUtility.labelWidth = 0;
                        EditorGUI.indentLevel--;


                        GUILayout.Space(5);

                        if (CurrentMode.isExpanded)
                        {
                            Mode_Tabs1.intValue = GUILayout.Toolbar(Mode_Tabs1.intValue, new string[4] { "General", "Abilities", "Events", "Reactions" });

                            switch (Mode_Tabs1.intValue)
                            {
                                case 0:
                                    var Input = CurrentMode.FindPropertyRelative("Input");
                                    var active = CurrentMode.FindPropertyRelative("active");
                                    var hasCoolDown = CurrentMode.FindPropertyRelative("hasCoolDown");
                                    var CoolDown = CurrentMode.FindPropertyRelative("CoolDown");
                                    var allowRotation = CurrentMode.FindPropertyRelative("allowRotation");
                                    var m_Source = CurrentMode.FindPropertyRelative("m_Source");
                                    var allowMovement = CurrentMode.FindPropertyRelative("allowMovement");
                                    var modifier = CurrentMode.FindPropertyRelative("modifier");
                                    var ignoreLowerModes = CurrentMode.FindPropertyRelative("ignoreLowerModes");
                                    var forceIgnorePriority = CurrentMode.FindPropertyRelative("forceIgnorePriority");
                                    var EnterConditions = CurrentMode.FindPropertyRelative("EnterConditions");
                                    var InterruptConditions = CurrentMode.FindPropertyRelative("InterruptConditions");
                                    var ExitConditions = CurrentMode.FindPropertyRelative("ExitConditions");

                                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                    {
                                        EditorGUILayout.PropertyField(Input);

                                        EditorGUILayout.PropertyField(active);
                                        EditorGUILayout.PropertyField(ignoreLowerModes,
                                            new GUIContent("Ignore Lower", "It will play this mode even if another Lower Priority Mode is playing"));
                                        EditorGUILayout.PropertyField(forceIgnorePriority);

                                        EditorGUILayout.PropertyField(hasCoolDown);
                                        if (hasCoolDown.boolValue)
                                            EditorGUILayout.PropertyField(CoolDown);
                                        EditorGUILayout.PropertyField(allowRotation, new GUIContent("Allow Rotation", "Allows rotate while is on the Mode"));
                                        EditorGUILayout.PropertyField(allowMovement, new GUIContent("Allow Movement", "Allows movement while is on the Mode"));

                                        EditorGUILayout.PropertyField(modifier, G_Modifier);
                                        EditorGUILayout.PropertyField(m_Source);
                                        EditorGUI.indentLevel++;
                                        EditorGUILayout.PropertyField(EnterConditions);
                                        EditorGUILayout.PropertyField(InterruptConditions);
                                        EditorGUILayout.PropertyField(ExitConditions);
                                        EditorGUI.indentLevel--;
                                    }
                                    if (Application.isPlaying)
                                    {
                                        using (new EditorGUI.DisabledGroupScope(true))
                                        {
                                            using (new GUILayout.HorizontalScope())
                                            {
                                                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                                {
                                                    EditorGUILayout.ToggleLeft("Playing Mode", mode.PlayingMode);
                                                    EditorGUILayout.ToggleLeft($"Mode [{mode.Name}] Input", mode.InputValue);
                                                }

                                                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                                {
                                                    EditorGUILayout.ToggleLeft("In CoolDown", mode.InCoolDown);
                                                    EditorGUILayout.IntField("Temporal Activation", mode.TemporalActivation);
                                                }
                                            }

                                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                            {
                                                for (int i = 0; i < mode.Abilities.Count; i++)
                                                {
                                                    EditorGUILayout.ToggleLeft($"Ability [{mode.Abilities[i].Name}] Input", mode.Abilities[i].InputValue);
                                                }
                                            }
                                        }
                                    }


                                    break;
                                case 1:
                                    var AbilityIndex = CurrentMode.FindPropertyRelative("m_AbilityIndex");
                                    var DefaultIndex = CurrentMode.FindPropertyRelative("DefaultIndex");
                                    var ResetToDefault = CurrentMode.FindPropertyRelative("ResetToDefault");
                                    var Abilities = CurrentMode.FindPropertyRelative("Abilities");

                                    using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
                                    {
                                        EditorGUIUtility.labelWidth = 70;
                                        EditorGUILayout.PropertyField(AbilityIndex, G_AbilityIndex, GUILayout.MinWidth(50));
                                        EditorGUILayout.PropertyField(DefaultIndex, G_DefaultIndex, GUILayout.MinWidth(50));
                                        EditorGUIUtility.labelWidth = 0;
                                        ResetToDefault.boolValue = GUILayout.Toggle(ResetToDefault.boolValue, G_ResetToDefault, EditorStyles.miniButton, GUILayout.Width(20));
                                    }

                                    using (new GUILayout.HorizontalScope())
                                    {
                                        EditorGUILayout.LabelField("[If Active Ability Index is -99, the mode will play a random ability]", AbilityStyleDesc);
                                        if (Application.isPlaying)
                                        {
                                            var color = GUI.color;
                                            GUI.color = MTools.MGreen * 1.5f;
                                            if (GUILayout.Button(new GUIContent("Interrupt", "Interrupt Mode"), GUILayout.Width(60)))
                                            {
                                                m.Mode_Interrupt();
                                            }

                                            if (GUILayout.Button(new GUIContent("Stop", "Stop Mode"), GUILayout.Width(60)))
                                            {
                                                m.Mode_Stop();
                                            }
                                            GUI.color = color;
                                        }
                                    }


                                    var ActiveAbilityConditions = CurrentMode.FindPropertyRelative("ActiveAbilityConditions");

                                    EditorGUILayout.PropertyField(ActiveAbilityConditions, true);

                                    DrawAbilities(index, CurrentMode, Abilities);
                                    break;
                                case 2:
                                    var OnEnterMode = CurrentMode.FindPropertyRelative("OnEnterMode");
                                    var OnAbilityIndex = CurrentMode.FindPropertyRelative("OnAbilityIndex");
                                    var OnExitMode = CurrentMode.FindPropertyRelative("OnExitMode");

                                    var OnModeEnabled = CurrentMode.FindPropertyRelative("OnModeEnabled");
                                    var OnModeDisabled = CurrentMode.FindPropertyRelative("OnModeDisabled");

                                    EditorGUILayout.PropertyField(OnEnterMode, new GUIContent($"On [{ModeName}] Enter "));
                                    EditorGUILayout.PropertyField(OnExitMode, new GUIContent($"On [{ModeName}] Exit"));

                                    EditorGUILayout.PropertyField(OnModeEnabled, new GUIContent($"On [{ModeName}] Enabled"));
                                    EditorGUILayout.PropertyField(OnModeDisabled, new GUIContent($"On [{ModeName}] Disabled"));

                                    EditorGUILayout.PropertyField(OnAbilityIndex, new GUIContent($"On [{ModeName}] Active Ability Index changed "));
                                    break;

                                case 3:
                                    var OnEnterReaction = CurrentMode.FindPropertyRelative("OnEnterReaction");
                                    var OnExitReaction = CurrentMode.FindPropertyRelative("OnExitReaction");

                                    var OnEnableReaction = CurrentMode.FindPropertyRelative("OnEnabledReaction");
                                    var OnDisableReaction = CurrentMode.FindPropertyRelative("OnDisabledReaction");

                                    EditorGUI.indentLevel++;
                                    EditorGUILayout.PropertyField(OnEnterReaction, new GUIContent($"On [{ModeName}] Enter Reaction"));
                                    EditorGUILayout.PropertyField(OnExitReaction, new GUIContent($"On [{ModeName}] Exit Reaction "));

                                    EditorGUILayout.PropertyField(OnEnableReaction, new GUIContent($"On [{ModeName}] Enabled Reaction "));
                                    EditorGUILayout.PropertyField(OnDisableReaction, new GUIContent($"On [{ModeName}] Disabled Reaction "));
                                    EditorGUI.indentLevel--;
                                    break;
                            }
                        }
                    }
                }
            }
        }

        //MWC: Detect Modes that share the same ID and show a HelpBox listing them.
        private void DrawDuplicateModeIDWarning()
        {
            if (m.modes == null || m.modes.Count < 2) return;

            var seen = new HashSet<int>();
            var duplicates = new List<string>();

            foreach (var mode in m.modes)
            {
                if (mode == null || mode.ID == null) continue;
                if (!seen.Add(mode.ID.ID))
                    duplicates.Add($"{mode.ID.name} (ID {mode.ID.ID})");
            }

            if (duplicates.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"Duplicate Mode ID(s) found: {string.Join(", ", duplicates)}.\nEach Mode ID must be unique. Only the first entry will be used at runtime.",
                    MessageType.Warning);
            }
        }

        private void DrawAbilities(int ModeIndex, SerializedProperty SelectedMode, SerializedProperty Abilities)
        {
            ReorderableList Reo_AbilityList;

            string listKey = SelectedMode.propertyPath;

            var SelectedAbility = SelectedMode.FindPropertyRelative("SelectedAbilityIndexEditor");

            if (Reo_Abilities.ContainsKey(listKey))
            {
                // fetch the reorderable list in dict
                Reo_AbilityList = Reo_Abilities[listKey];
            }
            else
            {
                Reo_AbilityList = new ReorderableList(SelectedMode.serializedObject, Abilities, true, true, true, true)
                {
                    drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        rect.y += 2;

                        var element = Abilities.GetArrayElementAtIndex(index);

                        var IndexValue = element.FindPropertyRelative("Index");
                        var name = element.FindPropertyRelative("Name");

                        var Active = element.FindPropertyRelative("active");

                        var ConstValue = Active.FindPropertyRelative("ConstantValue");
                        var VarValue = Active.FindPropertyRelative("Variable");
                        var useConstant = Active.FindPropertyRelative("UseConstant").boolValue;
                        BoolVar variable = VarValue.objectReferenceValue as BoolVar;

                        var IDRect = new Rect(rect) { height = EditorGUIUtility.singleLineHeight };

                        var ActiveRect = new Rect(IDRect);
                        var NameRect = new Rect(IDRect);

                        ActiveRect.width = 20;

                        IDRect.x = rect.width / 4 * 3 + 50;
                        IDRect.width = rect.width / 4 - 12;

                        NameRect.x += 24;
                        NameRect.width = rect.width / 4 * 3 - 50;

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

                        var dC = GUI.color;

                        if (SelectedAbility.intValue == index) GUI.color = MTools.MGreen * 2;

                        if (Application.isPlaying)
                        {
                            IDRect.width -= 30;
                        }


                        EditorGUI.PropertyField(NameRect, name, GUIContent.none);

                        EditorGUIUtility.labelWidth = 56;
                        EditorGUI.PropertyField(IDRect, IndexValue, GUIContent.none);
                        EditorGUIUtility.labelWidth = 0;
                        GUI.color = dC;


                        if (Application.isPlaying)
                        {
                            var TestBRect = new Rect(rect.width + 10, rect.y, 38, EditorGUIUtility.singleLineHeight);

                            var color = GUI.color;
                            GUI.color = MTools.MGreen * 1.5f;
                            if (GUI.Button(TestBRect, "Test"))
                            {
                                var ModeID = m.modes[ModeIndex].ID;
                                var ability = m.modes[ModeIndex].Abilities[index].Index.Value;
                                m.Mode_Activate(ModeID, ability);
                            }
                            GUI.color = color;
                        }
                    },

                    drawHeaderCallback = rect =>
                    {
                        var IDRect = new Rect(rect)
                        {
                            height = EditorGUIUtility.singleLineHeight
                        };

                        var NameRect = new Rect(IDRect);

                        IDRect.x = rect.width / 4 * 3 + 40;
                        IDRect.width = 40;

                        NameRect.x += 24;
                        NameRect.width = rect.width / 4 * 3 - 50;

                        string Selected = "None";

                        if (SelectedAbility.intValue != -1 && SelectedAbility.intValue < m.modes[ModeIndex].Abilities.Count)
                        {
                            var value = m.modes[ModeIndex].Abilities[SelectedAbility.intValue].Index.Value;
                            var neg = value > 0 ? 1 : -1;

                            if (m.modes[ModeIndex].ID)
                                Selected = $"{(m.modes[ModeIndex].ID.ID * 1000 + Mathf.Abs(value)) * neg}";
                        }
                        else
                        {
                            SelectedAbility.intValue = m.modes[ModeIndex].Abilities.Count - 1;
                        }


                        EditorGUI.LabelField(NameRect, $"   Abilities    Selected → [{Selected}]");
                        EditorGUI.LabelField(IDRect, "Index");
                    },

                    onAddCallback = (list) =>
                    {
                        var index = list.count == 0 ? 0 : list.count - 1;
                        Abilities.InsertArrayElementAtIndex(list.count == 0 ? 0 : list.count - 1);
                        list.index = -1;
                    },
                    onSelectCallback = (list) =>
                    {
                        SelectedAbility.intValue = list.index;
                    }
                };

                Reo_Abilities.Add(listKey, Reo_AbilityList);  //Store it on the Editor
            }

            Reo_AbilityList.DoLayoutList();

            Reo_AbilityList.index = SelectedAbility.intValue;


            if (SelectedAbility.intValue != -1 && SelectedAbility.intValue < Abilities.arraySize)
            {
                // Debug.Log("SelectedAbility = " + SelectedAbility);
                SerializedProperty ability = Abilities.GetArrayElementAtIndex(SelectedAbility.intValue);

                if (ability != null)
                {

                    var Name = ability.FindPropertyRelative("Name");
                    var Status = ability.FindPropertyRelative("Status");

                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        var M = m.modes[ModeIndex];
                        if (M.ID != null && M.Abilities[SelectedAbility.intValue] != null)
                        {
                            var valu = m.modes[ModeIndex].Abilities[SelectedAbility.intValue].Index.Value;
                            var neg = valu > 0 ? 1 : -1;

                            EditorGUILayout.LabelField(new GUIContent($"[{Name.stringValue}] Combined Index → " +
                            $"[{(m.modes[ModeIndex].ID.ID * 1000 + Mathf.Abs(valu)) * neg}]",
                             "The combined index is set using this formula: (Mode_ID * 1000 + Ability_Index)\nThis is used in the Animator Controller Transition values for activating Mode Abilities"), AbilityStyleDesc);
                        }
                        Ability_Tabs.intValue = GUILayout.Toolbar(Ability_Tabs.intValue, new string[5] { "General", "Status", "Limits", "Audio", "Events" });

                        switch (Ability_Tabs.intValue)
                        {
                            //General
                            case 0:
                                {
                                    var active = ability.FindPropertyRelative("active");
                                    var Input = ability.FindPropertyRelative("Input");


                                    var MultiplierPosition = ability.FindPropertyRelative("MultiplierPosition");
                                    var MultiplierRotation = ability.FindPropertyRelative("MultiplierRotation");

                                    var AdditivePosition = ability.FindPropertyRelative("AdditivePosition");
                                    var AdditiveRotation = ability.FindPropertyRelative("AdditiveRotation");

                                    var CoolDown = ability.FindPropertyRelative("CoolDown");
                                    var modifier = ability.FindPropertyRelative("modifier");

                                    var IgnoreGrounded = ability.FindPropertyRelative("IgnoreGrounded");
                                    var IgnoreGravity = ability.FindPropertyRelative("IgnoreGravity");
                                    var NoYMovement = ability.FindPropertyRelative("NoYMovement");
                                    var Persistent = ability.FindPropertyRelative("Persistent");
                                    var IncludeInRandom = ability.FindPropertyRelative("IncludeInRandom");
                                    var OverrideCapsule = ability.FindPropertyRelative("OverrideCapsule");

                                    EditorGUILayout.PropertyField(active);
                                    EditorGUILayout.PropertyField(Input);

                                    EditorGUILayout.PropertyField(modifier);
                                    EditorGUILayout.PropertyField(CoolDown);

                                    EditorGUILayout.PropertyField(IgnoreGrounded);
                                    EditorGUILayout.PropertyField(IgnoreGravity);
                                    EditorGUILayout.PropertyField(NoYMovement);
                                    EditorGUILayout.PropertyField(Persistent);
                                    EditorGUILayout.PropertyField(IncludeInRandom);


                                    EditorGUILayout.PropertyField(MultiplierPosition);
                                    EditorGUILayout.PropertyField(MultiplierRotation);

                                    EditorGUILayout.PropertyField(AdditivePosition);
                                    EditorGUILayout.PropertyField(AdditiveRotation);

                                    using (new EditorGUI.DisabledGroupScope(!MainCollider.objectReferenceValue))
                                    {
                                        //Reserve the row by hand: the ExposeScriptableAsset drawer lays out the inline inspector with GUILayout,
                                        //so it must not be inside a HorizontalScope or the inspector is drawn beside the field instead of below it
                                        var preset = OverrideCapsule.objectReferenceValue as CapsuleColliderPreset;
                                        var row = EditorGUILayout.GetControlRect();
                                        var fieldRect = row;
                                        if (preset != null) fieldRect.width -= 28;

                                        EditorGUI.PropertyField(fieldRect, OverrideCapsule);

                                        if (preset != null)
                                        {
                                            var buttonRect = new Rect(row) { x = row.xMax - 25, width = 25 };

                                            if (GUI.Button(buttonRect, new GUIContent("C", "Copy Main Capsule values into the Preset")))
                                            {
                                                preset.modifier = new(m.MainCollider)
                                                {
                                                    modify = CapsuleModifier.height | CapsuleModifier.radius | CapsuleModifier.center
                                                };
                                                EditorUtility.SetDirty(preset);
                                            }
                                        }
                                    }

                                    break;
                                }
                            //Status
                            case 1:
                                {

                                    var Release = ability.FindPropertyRelative("Release");
                                    var abilityTime = ability.FindPropertyRelative("abilityTime");
                                    var ChargeValue = ability.FindPropertyRelative("ChargeValue");
                                    var ChargeCurve = ability.FindPropertyRelative("ChargeCurve");

                                    var help = "";

                                    EditorGUILayout.PropertyField(Status);

                                    switch ((AbilityStatus)Status.intValue)
                                    {
                                        case AbilityStatus.Charged:
                                            EditorGUILayout.PropertyField(abilityTime, new GUIContent("Charge Time"));
                                            var ConstValue = abilityTime.FindPropertyRelative("ConstantValue");

                                            if (ConstValue.floatValue != 0)
                                            {
                                                EditorGUILayout.PropertyField(ChargeValue);
                                                EditorGUILayout.PropertyField(ChargeCurve);
                                                EditorGUILayout.PropertyField(Release);
                                                help = "The Ability can be charged, it will be active while the Input Value is [True]. It will be stopped when the Input is released.\n" +
                                               "The ModePower animator Parameter will store the charge value ";
                                            }
                                            else
                                            {
                                                help = "The Ability will be active while the input is press down";
                                            }

                                            break;
                                        case AbilityStatus.ActiveByTime:
                                            EditorGUILayout.PropertyField(abilityTime);
                                            help = "The Ability is active during the ability time, then it will stop";
                                            break;
                                        case AbilityStatus.PlayOnce:
                                            help = "The Ability will play once";
                                            break;
                                        case AbilityStatus.Toggle:
                                            help = "The Ability will active when the Input Value is [True].\nIt will be stopped the next time the Input Value is [True]";
                                            break;
                                        case AbilityStatus.Forever:
                                            help = "The Ability will active forever. To stop it, call:\nMAnimal.Mode_Stop()";
                                            break;
                                        default: break;
                                    }

                                    EditorGUILayout.LabelField(help, AbilityStyleDesc);
                                    break;
                                }
                            //Limits
                            case 2:
                                {
                                    var S_properties = ability.FindPropertyRelative("Limits");
                                    var states = S_properties.FindPropertyRelative("states");
                                    var stances = S_properties.FindPropertyRelative("stances");
                                    var TransitionFrom = S_properties.FindPropertyRelative("TransitionFrom");
                                    var AbilityCondition = S_properties.FindPropertyRelative("AbilityCondition");
                                    var InterruptCondition = S_properties.FindPropertyRelative("InterruptCondition");

                                    //EditorGUILayout.PropertyField(S_properties, true);
                                    EditorGUILayout.PropertyField(states, true);
                                    EditorGUILayout.PropertyField(stances, true);

                                    EditorGUI.indentLevel++;
                                    EditorGUILayout.PropertyField(AbilityCondition);
                                    EditorGUILayout.PropertyField(InterruptCondition);


                                    EditorGUILayout.PropertyField(TransitionFrom, true);
                                    EditorGUI.indentLevel--;



                                    if (GUILayout.Button("Copy these limits to all other Abilities"))
                                    {
                                        var ModeAbilities = m.modes[ModeIndex].Abilities;
                                        var properties = ModeAbilities[SelectedAbility.intValue].Limits;

                                        foreach (var ab in ModeAbilities)
                                            ab.Limits = new ModeProperties(properties);

                                        Debug.Log("All Limits copied to all the Abilities in Mode: " + m.modes[ModeIndex].Name);
                                        EditorUtility.SetDirty(target);
                                    }
                                    break;
                                }
                            //Audio
                            case 3:
                                {
                                    var audioClip = ability.FindPropertyRelative("audioClip");
                                    var audioSource = ability.FindPropertyRelative("audioSource");
                                    var m_stopAudio = ability.FindPropertyRelative("m_stopAudio");
                                    var ClipDelay = ability.FindPropertyRelative("ClipDelay");


                                    EditorGUILayout.PropertyField(audioClip);
                                    EditorGUILayout.PropertyField(audioSource);
                                    EditorGUILayout.PropertyField(ClipDelay);
                                    EditorGUILayout.PropertyField(m_stopAudio);


                                    if (GUILayout.Button("Copy these audio properties to all other Abilities"))
                                    {
                                        var ModeAbilities = m.modes[ModeIndex].Abilities;
                                        var audioclip = ModeAbilities[SelectedAbility.intValue].audioClip;
                                        var audioSourcec = ModeAbilities[SelectedAbility.intValue].audioSource;
                                        var delay = ModeAbilities[SelectedAbility.intValue].ClipDelay.Value;
                                        var stopaudio = ModeAbilities[SelectedAbility.intValue].m_stopAudio;

                                        foreach (var ab in ModeAbilities)
                                        {
                                            ab.audioClip = new(audioclip);
                                            ab.audioSource = (audioSourcec);
                                            ab.ClipDelay = new(delay);
                                            ab.m_stopAudio = (stopaudio);
                                        }
                                        Debug.Log("All Limits copied to all the Abilities in Mode: " + m.modes[ModeIndex].Name);
                                        EditorUtility.SetDirty(target);
                                    }
                                    break;
                                }
                            //Events
                            case 4:
                                {

                                    var OnEnter = ability.FindPropertyRelative("OnEnter");
                                    var OnExit = ability.FindPropertyRelative("OnExit");
                                    var ReactEnter = ability.FindPropertyRelative("ReactEnter");
                                    var ReactExit = ability.FindPropertyRelative("ReactExit");
                                    var OnCharged = ability.FindPropertyRelative("OnCharged");


                                    EditorGUILayout.PropertyField(ReactEnter);
                                    EditorGUILayout.PropertyField(ReactExit);
                                    var ab_name = Name.stringValue;
                                    EditorGUILayout.PropertyField(OnEnter, new GUIContent($"On [{ab_name}] Enter"));
                                    EditorGUILayout.PropertyField(OnExit, new GUIContent($"On [{ab_name}] Exit"));

                                    if ((AbilityStatus)Status.intValue == AbilityStatus.Charged)
                                        EditorGUILayout.PropertyField(OnCharged, new GUIContent($"On [{ab_name}] Charged"));
                                    break;
                                }
                        }
                    }
                }
            }
        }

        #region DrawModes

        //-------------------------MODES-----------------------------------------------------------
        private void Draw_Header_Modes(Rect rect)
        {
            var r = new Rect(rect);
            var a = new Rect(rect) { width = 65 };
            EditorGUI.LabelField(a, new GUIContent("  Active", "Is the Mode Enable or Disable"));
            r.x += 60;
            r.width = 60;
            EditorGUI.LabelField(r, new GUIContent("Mode", "Modes are the Animations that can be played on top of the States"));

            var activeRect = rect;
            activeRect.width -= 20;
            activeRect.x += 20;
            var prioRect = new Rect(activeRect.width + 30, activeRect.y, 45, activeRect.height);
            var IDRect = new Rect(activeRect.width + 5, activeRect.y, 35, activeRect.height);

            EditorGUI.LabelField(IDRect, new GUIContent("ID", "Mode ID:\n Numerical ID value for the Mode"));
            EditorGUI.LabelField(prioRect, new GUIContent("Pri", "Priority:\n If A mode has 'Ignore Lower Modes' enabled, it will play even if a Lower Mode is Playing"));
        }

        private void Draw_Element_Modes(Rect rect, int index, bool isActive, bool isFocused)
        {
            rect.y += 2;
            if (S_Mode_List.arraySize <= index) return;

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                var ModeProperty = S_Mode_List.GetArrayElementAtIndex(index);
                var active = ModeProperty.FindPropertyRelative("active");
                var ID = ModeProperty.FindPropertyRelative("ID");

                var rectan = new Rect(rect);
                rectan.width -= 20;
                rectan.x += 20;
                rectan.y -= 2;

                var activeRect1 = new Rect(rect.x, rect.y - 2, 20, rect.height);
                var IDRect = new Rect(rect.x + 40, rect.y, rect.width - 90, EditorGUIUtility.singleLineHeight);


                if (Application.isPlaying)
                {
                    IDRect.width -= 40;
                }

                var IDVal = new Rect(rectan.width + 9, rectan.y + 3, 35, rectan.height);

                var dC = GUI.backgroundColor;
                if (isActive) GUI.backgroundColor = MTools.MGreen;
                active.boolValue = EditorGUI.Toggle(activeRect1, GUIContent.none, active.boolValue);


                // Highlight the Mode in red if it doesn't have an ID assigned, since it won't be able to be activated through code or the animator without an ID.
                if (ID.objectReferenceValue == null)
                {
                    GUI.backgroundColor = MTools.MRed * 2;
                }


                EditorGUI.PropertyField(IDRect, ID, GUIContent.none);
                GUI.backgroundColor = dC;


                if (Application.isPlaying)
                {
                    var TestBRect = new Rect(rect.width - 40 - 2, rect.y, 38, EditorGUIUtility.singleLineHeight);

                    var color = GUI.color;
                    GUI.color = MTools.MGreen * 1.5f;
                    if (GUI.Button(TestBRect, "Test"))
                    {
                        m.Mode_Activate(ID.objectReferenceValue as ModeID);
                    }
                    GUI.color = color;
                }



                var style = new GUIStyle(EditorStyles.boldLabel)
                { alignment = TextAnchor.UpperRight };

                if (m.modes[index].ID != null)
                {
                    EditorGUI.LabelField(IDVal, m.modes[index].ID.ID.ToString(), style);
                }

                var priorityRect = new Rect(rectan.width + 42, rectan.y, 40, rectan.height); // MWC - increased width to fit 2-digit priority values

                EditorGUI.LabelField(priorityRect, "│" + (S_Mode_List.arraySize - index - 1));

                if (cc.changed)
                {
                    Undo.RecordObject(target, "Move Handles");
                }
            }
        }

        private void OnAdd_Modes(ReorderableList list)
        {
            if (m.modes == null) m.modes = new();

            Ability newAbility = new()
            {
                active = new BoolReference(true),
                Index = new IntReference(1),
                Name = "AbilityName"
            };

            var newMode = new Mode()
            {
                Abilities = new List<Ability>(1) { newAbility },
            };

            m.modes.Add(newMode);

            EditorUtility.SetDirty(m);
            ModeArray_Popup();
        }

        private void OnRemoveCallback_Mode(ReorderableList list)
        {
            // The reference value must be null in order for the element to be removed from the SerializedProperty array.
            S_Mode_List.DeleteArrayElementAtIndex(list.index);
            list.index -= 1;

            if (list.index == -1 && S_Mode_List.arraySize > 0)  //In Case you remove the first one
            {
                list.index = 0;
            }
            SelectedMode.intValue--;
            list.index = Mathf.Clamp(list.index, 0, list.index - 1);
            S_Mode_List.serializedObject.ApplyModifiedProperties();
            ModeArray_Popup();
            EditorUtility.SetDirty(m);
            GUIUtility.ExitGUI();
        }
        #endregion
    }
}
#endif
