// Last Change: 9/19/2026 3:44pm
#if UNITY_EDITOR
using MalbersAnimations.Conditions; // MWC — Conditions2Drawer for the per-processor Conditions row
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MalbersAnimations.IK
{
    // MWC — Shared inspector renderer for a single IKSet.
    // The tabbed UI (IK Processors / Weight Processors / Events), the processor ReorderableList
    // and the selected-processor detail panel used to be duplicated inside IKManagerEditor and
    // IKProcessorProfileEditor. Both of them — plus the new IKSetLibraryEditor — now share this class.
    //
    // The IKSet can live on a MonoBehaviour (IKManager) or inside a ScriptableObject asset
    // (IKSetProfile / IKSetLibrary). When there is no IKManager, everything that needs a scene
    // reference (Aimer, Targets array, resolved Bone, Animator float parameters, Verify) is hidden,
    // because those cannot be stored in an asset.
    public class IKSetDrawer
    {
        /// <summary> ReorderableLists cached per IKSet property path. </summary>
        private readonly Dictionary<string, ReorderableList> processorLists = new();

        /// <summary> All the IKProcessor concrete types, for the [+] dropdown. </summary>
        private static List<Type> derivedTypes;

        /// <summary> Object to record for Undo (the IKManager, the profile or the library). </summary>
        private readonly UnityEngine.Object owner;

        /// <summary> Null when the IKSet lives inside a ScriptableObject asset. </summary>
        private readonly IKManager manager;

        /// <summary> IKSet currently being drawn. Read by the ReorderableList callbacks so they always
        /// operate on fresh data even when the selected set changes or the list gets replaced. </summary>
        private IKSet current;

        private GUIStyle popupStyle;
        private List<string> floatAnimParam;

        private static readonly GUIContent s_AnimParamLabel = new("Anim Params [IKProcessor]",
            "Local Anim Parameters to apply to a specific IK Processor. E.g Use the Anim Curve for the Left Hand and another anim curve for the Right Hand.\n" +
            "Every valid parameter is multiplied together. Empty rows, and names the Animator does not have, are ignored"); //MWC — now a list

        //MWC — labels for the Anim Parameters list rows
        private static readonly GUIContent s_AnimParamEmptyLabel = new(" ");
        private static readonly GUIContent s_AnimParamNoneLabel = new("None");
        private static readonly GUIContent s_AnimParamAddLabel = new("+", "Add an Anim Parameter");
        private static readonly GUIContent s_AnimParamRemoveLabel = new("-", "Remove the last Anim Parameter");

        //MWC — Conditions2Drawer appends " (Disabled)" to the label it receives, so this one cannot be a cached
        //static GUIContent (it would accumulate the suffix on every repaint). A fresh instance is built per draw.
        private const string s_ConditionsText = "Conditions [IKProcessor]";
        private const string s_ConditionsTip = "Local Conditions to apply to a specific IK Processor. If they evaluate false the Processor weight is set to zero";

        //MWC — reused directly instead of going through PropertyField (see DrawProcessorConditions)
        private static Conditions2Drawer s_conditionsDrawer;

        private static readonly GUIContent s_TargetHeader = new(" Target [I]",
            "Target Index from the <Targets> array. Set it to -1 if the Processor does not need any Target");

        /// <summary> Gizmos toggle drawn next to Enable Time. Global switch for every Processor of the Set. </summary>
        private static GUIContent s_GizmosLabel;

        public IKSetDrawer(UnityEngine.Object owner, IKManager manager = null)
        {
            this.owner = owner;
            this.manager = manager;
            derivedTypes ??= MTools.GetAllTypes<IKProcessor>();
        }

        private bool HasManager => manager != null;

        /// <summary> Drops every cached ReorderableList. Call it after the sets list gets replaced,
        /// otherwise the cached lists keep pointing at stale SerializedProperties. </summary>
        public void ClearCache() => processorLists.Clear();

        // ─────────────────────────────────────────────────────────────────────────
        /// <summary> Draws the full tabbed UI of one IKSet. </summary>
        /// <param name="setProperty">SerializedProperty pointing at the IKSet.</param>
        /// <param name="set">The IKSet instance behind <paramref name="setProperty"/>.</param>
        /// <param name="editorTabs">Property storing the selected tab (lives on the owner asset/component).</param>
        public void DrawSet(SerializedProperty setProperty, IKSet set, SerializedProperty editorTabs)
        {
            if (setProperty == null || set == null || editorTabs == null) return;

            current = set;

            popupStyle ??= new GUIStyle(GUI.skin.GetStyle("PaneOptions"))
            {
                imagePosition = ImagePosition.ImageOnly,
                margin = new RectOffset(0, 0, 3, 0)
            };

            var processorsAmount = set.Processors != null ? set.Processors.Count : 0;
            var weightAmount = set.weightProcessors != null ? set.weightProcessors.Count : 0;

            var labels = new string[] { $"IK Set [{processorsAmount}]", $"Weight Processors [{weightAmount}]", "Events" };

            editorTabs.intValue = GUILayout.Toolbar(Mathf.Clamp(editorTabs.intValue, 0, 2), labels);

            switch (editorTabs.intValue)
            {
                case 0: DrawProcessorsTab(setProperty, set); break;
                case 1: DrawWeightsTab(setProperty, set); break;
                default: DrawEventsTab(setProperty); break;
            }
        }

        // ── Tab 0 : IK Processors ────────────────────────────────────────────────
        private void DrawProcessorsTab(SerializedProperty setProperty, IKSet set)
        {
            DrawFinalWeight(set);

            var EnableTime = setProperty.FindPropertyRelative("EnableTime");
            EnableTime.isExpanded = MalbersEditor.Foldout(EnableTime.isExpanded, "IK Set General Properties");

            if (EnableTime.isExpanded)
            {
                var DisableTime = setProperty.FindPropertyRelative("DisableTime");
                var EnterLerp = setProperty.FindPropertyRelative("EnterLerp");
                var ExitLerp = setProperty.FindPropertyRelative("ExitLerp");

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(EnableTime);

                    if (EnableTime.floatValue > 0)
                        EditorGUILayout.PropertyField(EnterLerp, GUIContent.none, GUILayout.MaxWidth(50), GUILayout.MinWidth(5));

                    DrawGizmosButton(setProperty.FindPropertyRelative("Gizmos"));
                }

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(DisableTime);

                    if (DisableTime.floatValue > 0)
                        EditorGUILayout.PropertyField(ExitLerp, GUIContent.none, GUILayout.MaxWidth(50), GUILayout.MinWidth(5));
                }

                //MWC — the Aimer is a scene component, it cannot be stored inside an asset
                if (HasManager)
                    EditorGUILayout.PropertyField(setProperty.FindPropertyRelative("aimer"));
            }

            //MWC — the Targets array holds scene Transforms, hide it when the Set lives in an asset (use IKTags instead)
            if (HasManager)
            {
                var Target = setProperty.FindPropertyRelative("Targets");

                if (Target.arraySize > 0)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(Target);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.PropertyField(setProperty.FindPropertyRelative("ClearTargetsOnDisable"));
                }
            }

            DrawProcessorList(setProperty, set);
        }

        /// <summary> Toggle button for the Set Gizmos, same look as the Debug buttons of the other Malbers inspectors. </summary>
        private static void DrawGizmosButton(SerializedProperty gizmos)
        {
            if (gizmos == null) return;

            s_GizmosLabel ??= new GUIContent(EditorGUIUtility.IconContent("d_GizmosToggle"))
            { tooltip = "Draw the Gizmos and Scene View handles of every IK and Weight Processor of this Set" };

            var currentGUIColor = GUI.color;
            GUI.color = gizmos.boolValue ? Color.green : currentGUIColor;
            gizmos.boolValue = GUILayout.Toggle(gizmos.boolValue, s_GizmosLabel, EditorStyles.miniButtonMid, GUILayout.Width(28), GUILayout.Height(20));
            GUI.color = currentGUIColor;
        }

        // ── Tab 1 : Weight Processors ────────────────────────────────────────────
        private void DrawWeightsTab(SerializedProperty setProperty, IKSet set)
        {
            DrawFinalWeight(set);

            EditorGUILayout.PropertyField(setProperty.FindPropertyRelative("LerpWeight"));

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(setProperty.FindPropertyRelative("weightProcessors"));
            EditorGUI.indentLevel--;
        }

        // ── Tab 2 : Events ───────────────────────────────────────────────────────
        private static void DrawEventsTab(SerializedProperty setProperty)
        {
            EditorGUILayout.PropertyField(setProperty.FindPropertyRelative("OnWeightChanged"));
            EditorGUILayout.PropertyField(setProperty.FindPropertyRelative("OnSetEnable"));
            EditorGUILayout.PropertyField(setProperty.FindPropertyRelative("OnSetDisable"));
        }

        private void DrawFinalWeight(IKSet set)
        {
            if (!Application.isPlaying || !HasManager) return;

            var guiColor = GUI.color;
            GUI.color = Color.yellow;
            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.FloatField("Final Weight", set.FinalWeight);
            GUI.color = guiColor;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Processor list + detail panel
        // ─────────────────────────────────────────────────────────────────────────
        private void DrawProcessorList(SerializedProperty setProperty, IKSet set)
        {
            var link = setProperty.FindPropertyRelative("IKProcesors");
            var SelectedIKProcessor = setProperty.FindPropertyRelative("SelectedIKProcessor");

            string listKey = setProperty.propertyPath;

            if (!processorLists.TryGetValue(listKey, out var reoLink))
            {
                reoLink = BuildProcessorList(setProperty, link, SelectedIKProcessor);
                processorLists.Add(listKey, reoLink);
            }

            reoLink.DoLayoutList();

            var index = reoLink.index = SelectedIKProcessor.intValue;

            if (index < 0 || index >= link.arraySize) return;
            if (set.Processors == null || index >= set.Processors.Count) return;

            SerializedProperty ikProcessor = link.GetArrayElementAtIndex(index);
            if (ikProcessor == null || ikProcessor.managedReferenceValue == null) return;

            var processor = set.Processors[index];
            if (processor == null) return;

            EditorGUILayout.Space(-16);

            var typeLabel = $"[{ikProcessor.managedReferenceValue.GetType().Name}]";

            if (HasManager)
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(typeLabel, EditorStyles.boldLabel, GUILayout.Width(150));

                    //MWC — Bone is resolved at runtime only, so it is meaningless on an asset
                    using (new EditorGUI.DisabledGroupScope(true))
                        EditorGUILayout.ObjectField(GUIContent.none, processor.Bone, typeof(Transform), false, GUILayout.Width(200));
                }
            }
            else
            {
                EditorGUILayout.LabelField(typeLabel, EditorStyles.boldLabel);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var TargetIndex = ikProcessor.FindPropertyRelative("TargetIndex");

                var targets = set.Targets ?? Array.Empty<Scriptables.TransformReference>(); //MWC — Targets can be null on a freshly created Set
                var TargLength = targets.Length;
                var TIndex = TargetIndex.intValue;
                var RequireTarget = processor.RequireTargets;

                string CurrentTarget = TIndex >= 0 && TIndex < TargLength && targets[TIndex].Value ? targets[TIndex].Value.name : "Empty";

                CurrentTarget = $" [Target: {CurrentTarget}]";

                if (TIndex == -1 || !RequireTarget) CurrentTarget = "  [No Target Needed]";

                if (RequireTarget)
                {
                    if (processor.Tag == null && TIndex >= 0)
                    {
                        //MWC — the index warnings only make sense against a real Targets array (IKManager)
                        if (HasManager)
                        {
                            if (TIndex >= TargLength)
                                EditorGUILayout.HelpBox($"The Target Index [{TIndex}] greater than the Set Targets Array [{TargLength}]", MessageType.Warning);
                            else if (targets[TIndex].Value == null)
                                EditorGUILayout.HelpBox($"The Target Index [{TIndex}] is Empty. Make sure to set the value in the Editor or at Runtime", MessageType.Warning);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox("This IK Processor uses a Target Index. Assign an IK Tag instead so the Set can be reused by any character.", MessageType.Warning);
                        }
                    }
                    else
                    {
                        CurrentTarget = $" [Target: {(processor.Tag ? processor.Tag.name : "Missing")}]";
                    }
                }

                EditorGUILayout.PropertyField(ikProcessor, new GUIContent(ikProcessor.displayName + CurrentTarget), true);

                if (ikProcessor.isExpanded)
                {
                    DrawAnimParameter(ikProcessor);
                    DrawProcessorConditions(ikProcessor); //MWC — Local Conditions2 for this single Processor
                }
            }
        }

        private ReorderableList BuildProcessorList(SerializedProperty setProperty, SerializedProperty link, SerializedProperty selected)
        {
            ReorderableList list = null;

            list = new ReorderableList(setProperty.serializedObject, link, true, true, true, true)
            {
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (index >= link.arraySize) return;

                    var element = link.GetArrayElementAtIndex(index);
                    if (element.managedReferenceValue == null) return;

                    if (current == null || current.Processors == null || index >= current.Processors.Count) return;

                    var active = element.FindPropertyRelative("Active");
                    var name = element.FindPropertyRelative("name");
                    var Weight = element.FindPropertyRelative("Weight");
                    var Tag = element.FindPropertyRelative("Tag");

                    var height = EditorGUIUtility.singleLineHeight;
                    float buttonWidth = 5;

                    var activeRect = new Rect(rect.x, rect.y, 20, height);

                    var processor = current.Processors[index];
                    var requireTarget = processor != null && processor.RequireTargets;

                    // Calculate the available width for Name and Tag fields
                    float nameTagStart = rect.x + 20;
                    float nameTagWidth = (rect.width * 0.7f - 20 - buttonWidth);

                    float halfNameTagWidth = requireTarget ? nameTagWidth * 0.5f : nameTagWidth;

                    var NameRect = new Rect(nameTagStart, rect.y, halfNameTagWidth, height);
                    var weightRect = new Rect(rect.width - rect.width * 0.3f + 25 - buttonWidth, rect.y, rect.width * 0.3f + 12f - buttonWidth, height);

                    var dC = GUI.contentColor;

                    if (selected.intValue == index) GUI.contentColor = Color.yellow;

                    EditorGUIUtility.labelWidth = 30;
                    active.boolValue = EditorGUI.Toggle(activeRect, GUIContent.none, active.boolValue);
                    EditorGUI.PropertyField(NameRect, name, GUIContent.none);

                    if (requireTarget)
                    {
                        var TagRect = new Rect(nameTagStart + halfNameTagWidth + 20, rect.y, halfNameTagWidth - 15, height);
                        EditorGUI.PropertyField(TagRect, Tag, GUIContent.none);
                    }

                    EditorGUI.PropertyField(weightRect, Weight, new GUIContent(" "));

                    GUI.contentColor = dC;

                    var DuplicateRect = new Rect(rect.width + 28, rect.y, 20, height);

                    if (GUI.Button(DuplicateRect, new GUIContent("D", "Duplicate/Clone the IK Processor")))
                    {
                        var refValue = element.managedReferenceValue;
                        var Copy = JsonUtility.ToJson(refValue);
                        var Duplicate = JsonUtility.FromJson(Copy, refValue.GetType());

                        AddNewItem(Duplicate, list, setProperty);
                    }

                    EditorGUIUtility.labelWidth = 0;
                },

                drawHeaderCallback = rect =>
                {
                    var IDRect = new Rect(rect) { height = EditorGUIUtility.singleLineHeight, width = 60 };

                    EditorGUI.LabelField(IDRect, s_TargetHeader);

                    var height = EditorGUIUtility.singleLineHeight;

                    var nameRect = new Rect(IDRect.x + 75, rect.y, 80, height);
                    var WeightRect = new Rect(rect) { x = rect.width - 30, width = 65 };

                    EditorGUI.LabelField(nameRect, "IK Processor");
                    EditorGUI.LabelField(WeightRect, "Weight");

                    //MWC — Verify needs a live Animator, so it only shows up on an IKManager
                    if (HasManager)
                    {
                        var button = new Rect(WeightRect.x - 55, WeightRect.y, 50, height);

                        var defaultGuiColor = GUI.color;
                        GUI.color = Color.green;

                        if (GUI.Button(button, "Verify")) current?.Verify(manager, manager.animator);

                        GUI.color = defaultGuiColor;
                    }
                },

                onAddDropdownCallback = (Rect buttonRect, ReorderableList l) =>
                {
                    var menu = new GenericMenu();

                    foreach (var type in derivedTypes)
                    {
                        var att = type.GetCustomAttribute<AddTypeMenuAttribute>(false); //Find the correct name
                        string LabelName = att != null ? att.MenuName : type.Name;

                        menu.AddItem(new GUIContent(LabelName), false, (x) => AddNewItem(x, l, setProperty), Activator.CreateInstance(type));
                    }
                    menu.ShowAsContext();
                },

                onSelectCallback = (l) => { selected.intValue = l.index; }
            };

            return list;
        }

        private static void AddNewItem(object target, ReorderableList list, SerializedProperty setProperty)
        {
            var index = list.serializedProperty.arraySize;
            list.serializedProperty.arraySize++;
            list.index = index;

            IKProcessor link = (IKProcessor)target;
            link.name = target.GetType().Name;

            var element = list.serializedProperty.GetArrayElementAtIndex(index);
            element.managedReferenceValue = target;
            element.isExpanded = true;

            setProperty.serializedObject.ApplyModifiedProperties();
        }

        // ── Anim Parameter rows ──────────────────────────────────────────────────
        //MWC — was a single string. The Processor multiplies every non empty parameter now, so this draws one row per
        //entry plus a [+][-] footer. The float parameter picker is per row and only exists when the Set is drawn from
        //an IKManager: an IKSet living inside an asset has no Animator to read the parameter names from.
        /// <summary> Draws the Anim Parameters list of one IK Processor </summary>
        private void DrawAnimParameter(SerializedProperty ikProcessor)
        {
            var AnimParameters = ikProcessor.FindPropertyRelative("AnimParameters");
            if (AnimParameters == null) return;

            MigrateLegacyAnimParameter(ikProcessor, AnimParameters);

            var usePicker = HasManager && manager.animator != null && manager.gameObject.activeInHierarchy;

            if (usePicker) FindAllFloatParameters();

            for (int i = 0; i < AnimParameters.arraySize; i++)
            {
                var element = AnimParameters.GetArrayElementAtIndex(i);

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUI.indentLevel += 2;
                    //Only the first row carries the label, the rest keep the label column empty so they stay aligned
                    EditorGUILayout.PropertyField(element, i == 0 ? s_AnimParamLabel : s_AnimParamEmptyLabel);
                    EditorGUI.indentLevel -= 2;

                    if (usePicker && floatAnimParam != null)
                    {
                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            var index = Mathf.Max(floatAnimParam.IndexOf(element.stringValue), 0);

                            index = EditorGUILayout.Popup(index, floatAnimParam.ToArray(), popupStyle, GUILayout.Width(12));

                            if (cc.changed)
                            {
                                //Update the Name using the Animator Float Parameters
                                Undo.RecordObject(owner, "Set Anim Parameter");
                                element.stringValue = index == 0 ? string.Empty : floatAnimParam[index];
                                ikProcessor.serializedObject.ApplyModifiedProperties();
                                ikProcessor.serializedObject.Update();
                            }
                        }
                    }
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                if (AnimParameters.arraySize == 0)
                {
                    EditorGUI.indentLevel += 2;
                    EditorGUILayout.LabelField(s_AnimParamLabel, s_AnimParamNoneLabel);
                    EditorGUI.indentLevel -= 2;
                }

                GUILayout.FlexibleSpace();

                //arraySize++ zero-inits the new element, so the added row starts empty (an empty row is ignored)
                if (GUILayout.Button(s_AnimParamAddLabel, EditorStyles.miniButtonLeft, GUILayout.Width(25)))
                    AnimParameters.arraySize++;

                using (new EditorGUI.DisabledGroupScope(AnimParameters.arraySize == 0))
                {
                    if (GUILayout.Button(s_AnimParamRemoveLabel, EditorStyles.miniButtonRight, GUILayout.Width(25)))
                        AnimParameters.arraySize--;
                }
            }

            SyncAnimParameterHashes(AnimParameters, ikProcessor.FindPropertyRelative("AnimParameterHashes"));
        }

        // MWC — one way migration of the legacy single [AnimParameter] so opening the Inspector is enough to convert it.
        /// <summary> Moves the legacy single Anim Parameter of a Processor into its Anim Parameters array </summary>
        private static void MigrateLegacyAnimParameter(SerializedProperty ikProcessor, SerializedProperty AnimParameters)
        {
            var legacy = ikProcessor.FindPropertyRelative("AnimParameter");

            if (legacy == null || string.IsNullOrEmpty(legacy.stringValue)) return;

            if (AnimParameters.arraySize == 0)
            {
                AnimParameters.arraySize = 1;
                AnimParameters.GetArrayElementAtIndex(0).stringValue = legacy.stringValue;
            }

            legacy.stringValue = string.Empty; //Drop it so it cannot come back
            ikProcessor.serializedObject.ApplyModifiedProperties();
        }

        // MWC — the IK Set resolves these against the real Animator on Initialize(); this only keeps the serialized
        // values aligned at edit time. Values are written only when they actually change, so it does not dirty on repaint.
        /// <summary> Keeps the serialized Anim Parameter hashes aligned with their names </summary>
        private static void SyncAnimParameterHashes(SerializedProperty names, SerializedProperty hashes)
        {
            if (hashes == null) return;

            if (hashes.arraySize != names.arraySize) hashes.arraySize = names.arraySize;

            for (int i = 0; i < names.arraySize; i++)
            {
                var name = names.GetArrayElementAtIndex(i).stringValue;
                var hash = string.IsNullOrEmpty(name) ? 0 : Animator.StringToHash(name);
                var hashProperty = hashes.GetArrayElementAtIndex(i);

                if (hashProperty.intValue != hash) hashProperty.intValue = hash;
            }
        }

        // ── Local Conditions row ─────────────────────────────────────────────────
        // MWC — Conditions2 that gate the weight of this single IK Processor.
        // CheckIf carries [Hide(nameof(drawConditions))] so the default PropertyField of the processor skips it.
        // That attribute drawer would also swallow this manual call, so Conditions2Drawer is invoked directly.
        private static void DrawProcessorConditions(SerializedProperty ikProcessor)
        {
            var CheckIf = ikProcessor.FindPropertyRelative("CheckIf");
            if (CheckIf == null) return;

            s_conditionsDrawer ??= new Conditions2Drawer();

            //new GUIContent every time: Conditions2Drawer appends " (Disabled)" to the label it receives
            var label = new GUIContent(s_ConditionsText, s_ConditionsTip);

            EditorGUI.indentLevel++;
            var rect = EditorGUILayout.GetControlRect(true, s_conditionsDrawer.GetPropertyHeight(CheckIf, label));
            s_conditionsDrawer.OnGUI(rect, CheckIf, label);
            EditorGUI.indentLevel--;
        }

        private void FindAllFloatParameters()
        {
            if (floatAnimParam != null) return;

            try
            {
                if (!HasManager || !manager.gameObject.activeInHierarchy) return;

                var anim = manager.animator;
                if (anim == null || anim.runtimeAnimatorController == null) return;

                floatAnimParam = new() { "None" };

                for (int i = 0; i < anim.parameterCount; i++)
                {
                    var parameter = anim.GetParameter(i);

                    if (parameter.type == UnityEngine.AnimatorControllerParameterType.Float) //Ambiguous with UnityEditorInternal's
                        floatAnimParam.Add(parameter.name);
                }
            }
            catch
            {
                //do nothing
            }
        }
    }
}
#endif
