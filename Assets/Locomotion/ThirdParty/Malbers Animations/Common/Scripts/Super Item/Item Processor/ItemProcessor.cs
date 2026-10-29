// Last Change: 9/17/2026 7:40pm
using MalbersAnimations.Utilities;
using System.Collections; //MWC: IEnumerator handle for the delayed Execute
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor.IMGUI.Controls;
using UnityEditor;
#endif

namespace MalbersAnimations.Items
{
    /*
     TODO:
    - Add Debug Icon to the Processors. And Debug them separately. (Like the Conditions)
     
     */

    public enum ReleaseAction { None, OnRelease, OnEnd }
    public enum ExecuteProcessor
    {
        OnActionStart = 1 << 0,
        OnActionUpdated = 1 << 1,
        OnActionReleased = 1 << 2,
        OnTriggerEnter = 1 << 3,
        OnTriggerExit = 1 << 4,
        OnAnimation = 1 << 5,
        [InspectorName("On Prev Completed")]
        OnPrevProcessorCompleted = 1 << 6, //When certain processor gives a signal, it will execute another processor
        [InspectorName("On Prev Interrupted")]
        OnPrevProcessorInterrupted = 1 << 7, //Executes when a previous processor signals it was interrupted (e.g. a Charge released early)
        //MWC — the Action FINISHED playing (its Mode/animation ended, duration elapsed, taken over), whatever ended it.
        //Unlike OnActionReleased it does not fire on the input release, so a teardown waits for the animation to be over
        [InspectorName("On Action End")]
        OnActionEnd = 1 << 8
    }

    //MWC — Base URL for the Item Processor documentation. Every built-in processor carries a [HelpURL] built from it,
    //which the Help button on the processor row opens. Unity ignores [HelpURL] on non-Object types, so the drawer
    //reads the attribute itself — meaning a custom processor gets a working Help button just by adding the attribute.
    public static class ItemDocs { public const string Processors = "https://malbersanimations.gitbook.io/animal-controller/super-item/super-item-system/item-processors/"; }

    [System.Serializable]
    public abstract class ItemProcessor
    {
        [Tooltip("When to execute the Action")]
        [Flag, HideInInspector]
        public ExecuteProcessor execute = ExecuteProcessor.OnActionStart;

        public int Index { get; internal set; } //Index of the processor in the Action Processors List. This is set by the ItemAction when adding the processor to the list

        /// <summary>
        /// When creating a processor by code this will set the default execute value.
        /// </summary>
        public virtual ExecuteProcessor InitialExecute => ExecuteProcessor.OnActionStart;

        /// <summary> Display name in the Condition</summary>
        public abstract string DynamicName { get; }

        /// <summary> The actions is being used or not. E.g Fire a weapon is required while Aiming is Active</summary>
        [HideInInspector] public bool Active = true;

        [Tooltip("Delay in seconds before the processor executes")]
        //MWC: named executeDelay (not "delay") to avoid Unity's "same field serialized in base and subclass"
        //error, since some processors (Sound, Unity) already declare their own `delay` field.
        [HideInInspector, Min(0)] public float executeDelay = 0; //MWC: optional delay before Execute, mirrors Reaction.delay

        //MWC: handle of the delayed Execute coroutine, so it can be dropped when the action is interrupted. Runtime only, never serialized.
        [System.NonSerialized] private IEnumerator C_DelayedExecute;

        /// <summary> True while an <see cref="executeDelay"/> is counting down and its Execute has not run yet </summary>
        public bool HasPendingDelay => C_DelayedExecute != null;

        //MWC: run Execute now, or after `executeDelay` seconds using the item as the coroutine host (same pattern as Reaction.delay)
        private void ExecuteDelayed(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (executeDelay > 0)
            {
                CancelDelayedExecute(item); //MWC: only one pending delay per processor. Re-triggering restarts the countdown

                C_DelayedExecute = item.Delay_Action(executeDelay, () =>
                {
                    C_DelayedExecute = null; //MWC: clear the handle first, the delay is no longer pending once it fires
                    Execute(item, set, action);
                });
            }
            else Execute(item, set, action);
        }

        /// <summary>
        /// Drop a pending delayed Execute so it never runs. Called when the Action is FORCEFULLY interrupted
        /// (another action takes over, the Set is swapped, the Item is unequipped/holstered or disabled).
        /// A normal Action exit (input released, mode ended by itself) does NOT cancel it.
        /// </summary>
        public void CancelDelayedExecute(SuperItem item)
        {
            if (C_DelayedExecute == null) return;

            if (item != null) item.StopCoroutine(C_DelayedExecute);
            C_DelayedExecute = null;
        }

        internal void _ActionStart(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (!item.enabled || !set.Active || !action.Active || !Active) return;


            if (execute.HasFlag(ExecuteProcessor.OnActionStart))
                ExecuteDelayed(item, set, action); //MWC: honor delay

            ActionStart(item, set, action); //Call the Action Input Started method
        }

        /// <summary>
        /// When the Action is being used, this method is called every frame. The delta time is passed as a parameter to allow the processor to use it if needed. 
        /// By default, the Execute method without delta is called, but you can override this method if you need to use the delta time on the Action Updated.
        /// </summary>
        internal void _ActionUpdated(SuperItem item, ItemActionSet set, ItemAction action, float delta)
        {
            if (!item.enabled || !set.Active || !action.Active || !Active) return;

            if (execute.HasFlag(ExecuteProcessor.OnActionUpdated))
            {
                Execute(item, set, action, delta);
            }

            ActionUpdatedInternal(item, set, action, delta);
        }
        internal void _ActionReleased(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (!item.enabled || !set.Active || !action.Active || !Active) return;

            if (execute.HasFlag(ExecuteProcessor.OnActionReleased))
                ExecuteDelayed(item, set, action); //MWC: honor delay

            ActionReleased(item, set, action); //Call the Action Input Released method
        }


        /// <summary>
        /// When the Action is being used, this method is called by an animation behavior. 
        /// This is useful for creating processors that need to be executed on a specific frame of the animation, like a hitbox or a sound effect.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="set"></param>
        /// <param name="action"></param>
        internal void _ActionOnAnimation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (!item.enabled || !set.Active || !action.Active || !Active) return;

            if (execute.HasFlag(ExecuteProcessor.OnAnimation))
                ExecuteDelayed(item, set, action); //MWC: honor delay

            ActionOnAnimation(item, set, action); //MWC — was ActionReleased (copy-paste bug): the animation event must not fire the release handler
        }



        /// <summary> When an OnTrigger Enter Interaction is detected, this method is called to verify if the Item Processor should execute the EnterTrigger method</summary>
        /// <param name="go">Gameobject detected on the OnTrigger enter</param>
        /// <param name="collider">Collider detected on the Ontrigger enter</param>
        /// <param name="item">Super Item owner of the processor</param>
        /// <param name="set">Action Set owner of the processor </param>
        /// <param name="action">Action owner of the processor </param>
        /// <param name="user">Who is holding the super item</param>
        internal void _EnterTrigger(GameObject go, Collider collider, TriggerProxy proxy, SuperItem item, ItemActionSet set, ItemAction action, SuperItemManager user)
        {
            if (!item.enabled || !set.Active || !action.Active || !Active) return;

            if (!execute.HasFlag(ExecuteProcessor.OnTriggerEnter)) return;

            EnterTrigger(go, collider, proxy, item, set, action, user);
        }


        /// <summary> When an OnTrigger Exit Interaction is detected, this method is called to verify if the Item Processor should execute the EnterTrigger method</summary>
        /// <param name="go">Gameobject detected on the OnTrigger exit</param>
        /// <param name="collider">Collider detected on the Ontrigger exit</param>
        /// <param name="item">Super Item owner of the processor</param>
        /// <param name="set">Action Set owner of the processor </param>
        /// <param name="action">Action owner of the processor </param>
        /// <param name="user">Who is holding the super item</param>
        internal void _ExitTrigger(GameObject go, Collider collider, TriggerProxy proxy, SuperItem item, ItemActionSet set, ItemAction action, SuperItemManager user)
        {
            if (!item.enabled || !set.Active || !action.Active || !Active) return;
            if (!execute.HasFlag(ExecuteProcessor.OnTriggerExit)) return;

            ExitTrigger(go, collider, proxy, item, set, action, user);
        }

        public virtual void ItemEquipped(SuperItem item, ItemActionSet set, ItemAction action) { }
        public virtual void ItemUnequipped(SuperItem item, ItemActionSet set, ItemAction action) { }

        #region MWC — Interruption wrappers. They always kill a pending delayed Execute, then forward to the virtual hook.
        //The Active check lives here (it used to be in the ItemAction loops) so a DISABLED processor still gets its pending delay cancelled.

        internal void _ActionExit(SuperItem item, ItemActionSet set, ItemAction action)
        {
            CancelDelayedExecute(item);   //The Action was interrupted by another one: the pending delay must not fire
            ActionExit(item, set, action);
        }

        //MWC — the Action FINISHED playing, whatever ended it (input released, duration elapsed, Mode ended,
        //higher-priority Action took over, forced interruption). Called from ItemAction.Exit_Internal.
        //Unlike _ActionExit it does NOT cancel a pending delayed Execute: this is a normal end-of-life notification,
        //not an interruption, so a Processor that must survive the whole release pass (Charge holding its value for
        //the fire animation) can use it as its teardown point.
        internal void _ActionEnd(SuperItem item, ItemActionSet set, ItemAction action)
        {
            //MWC — [On Action End] Execute path, gated like every other Execute path so a disabled Processor never runs it
            if (Active && item.enabled && set.Active && action.Active && execute.HasFlag(ExecuteProcessor.OnActionEnd))
                ExecuteDelayed(item, set, action); //honor delay

            ActionEnd(item, set, action);
        }

        //MWC — the Action FAILED while it was playing: its Conditions (Action, Target or Set) or its PreRequisites
        //stopped being valid, so ItemAction.Update is ending it. Unlike ActionEnd/ActionExit this one identifies WHY,
        //which lets a Processor undo work a normal end would keep (the Charge clearing itself, the Projectile hiding
        //its trajectory prediction). It does NOT cancel a pending delayed Execute — the release pass owns that.
        internal void _ActionFailed(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (Active) ActionFailed(item, set, action);
        }

        internal void _SetExit(SuperItem item, ItemActionSet set, ItemAction action)
        {
            CancelDelayedExecute(item);   //The Action Set was swapped: the pending delay must not fire
            if (Active) SetExit(item, set, action);
        }

        internal void _ItemUnequipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            CancelDelayedExecute(item);   //The Item was put away/dropped: the pending delay must not fire
            if (Active) ItemUnequipped(item, set, action);
        }

        internal void _OnDisable(SuperItem item, ItemActionSet set, ItemAction action)
        {
            CancelDelayedExecute(item);   //The Item was disabled: the pending delay must not fire
            OnDisable(item, set, action);
        }
        #endregion

        /// <summary> Execute the action used in the item</summary>
        public virtual void Execute(SuperItem item, ItemActionSet set, ItemAction action) { }

        /// <summary>
        /// By Default, the Execute method with delta is called on the Action Updated, but you can override this method if you need to use the delta time on the Action Start or Action Released. 
        /// </summary>
        public virtual void Execute(SuperItem item, ItemActionSet set, ItemAction action, float delta) => Execute(item, set, action);

        /// <summary> Initialize the Item Processor and set once all references and variables. This is called on Awake</summary>
        public virtual void Awake(SuperItem item, ItemActionSet set, ItemAction action) { }

        public virtual void ActionReleased(SuperItem item, ItemActionSet set, ItemAction action) { }





        //MWC — signal ALL FOLLOWING processors that have the given execute flag (was: next processor only).
        //Skips disabled processors, consistent with _ActionStart/_ActionUpdated.
        private void SignalFollowing(ExecuteProcessor flag, SuperItem item, ItemActionSet set, ItemAction action)
        {
            for (int i = Index + 1; i < action.processors.Length; i++)
            {
                var next = action.processors[i];
                if (next != null && next.Active && next.execute.HasFlag(flag))
                    next.ExecuteDelayed(item, set, action); //Honor each processor's own executeDelay
            }
        }

        /// <summary>
        /// Call when the processor is completed and wants to give the signal to execute all the following processors in the list with the flag OnPrevProcessorCompleted.
        /// This is useful for creating processors that need to wait for certain conditions before executing the next processor, like waiting for an animation to finish or waiting for a certain time to pass.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="set"></param>
        /// <param name="action"></param>
        public virtual void ProcessorCompleted(SuperItem item, ItemActionSet set, ItemAction action)
            => SignalFollowing(ExecuteProcessor.OnPrevProcessorCompleted, item, set, action); //MWC — now signals ALL following processors with the flag

        /// <summary>
        /// Call when the processor was interrupted (e.g. a Charge released early) and wants to give the signal to
        /// execute all the following processors in the list with the flag OnPrevProcessorInterrupted.
        /// </summary>
        public virtual void ProcessorInterrupted(SuperItem item, ItemActionSet set, ItemAction action)
            => SignalFollowing(ExecuteProcessor.OnPrevProcessorInterrupted, item, set, action);

        public virtual void ActionStart(SuperItem item, ItemActionSet set, ItemAction action) { }

        /// <summary>
        /// By Default, the Execute method with delta is called on the Action Updated, but you can override this method if you need to use the delta time on the Action Start or Action Released.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="set"></param>
        /// <param name="action"></param>
        /// <param name="delta"></param>
        public virtual void ActionUpdatedInternal(SuperItem item, ItemActionSet set, ItemAction action, float delta) { }

        //MWC — hook for the OnAnimation execute path (was incorrectly routed to ActionReleased). Override to react to animation events.
        public virtual void ActionOnAnimation(SuperItem item, ItemActionSet set, ItemAction action) { }

        //These methods are called Automatically when connected to the animal controller
        public virtual void ModeStart(SuperItem item, ItemActionSet set, ItemAction action, int Mode, int Ability) { }
        public virtual void ModeEnd(SuperItem item, ItemActionSet set, ItemAction action, int Mode, int Ability) { }
        public virtual void StateChanged(SuperItem item, ItemActionSet set, ItemAction action, int State) { }
        public virtual void StanceChanged(SuperItem item, ItemActionSet set, ItemAction action, int stance) { }

        /// <summary> When an OnTrigger Enter Interaction is detected, this method is called to verify if the Item Processor should execute the EnterTrigger method</summary>
        /// <param name="action">Action owner of the processor </param>
        /// <param name="user">Who is holding the super item</param>
        public virtual void EnterTrigger(GameObject go, Collider collider, TriggerProxy proxy, SuperItem item, ItemActionSet set, ItemAction action, SuperItemManager user) { }

        /// <summary> When an OnTrigger exit Interaction is detected, this method is called to verify if the Item Processor should execute the EnterTrigger method</summary>
        public virtual void ExitTrigger(GameObject go, Collider collider, TriggerProxy proxy, SuperItem item, ItemActionSet set, ItemAction action, SuperItemManager user) { }

        /// <summary>  Executed when the set exits or is swapped by another </summary>
        public virtual void SetExit(SuperItem item, ItemActionSet set, ItemAction itemAction) { }

        /// <summary>  Executed when the action exits. By default, the Action will exit when the input is released, but this can be override to create different behaviours like toggle or hold by secondary action</summary>
        public virtual void ActionExit(SuperItem item, ItemActionSet set, ItemAction itemAction) { }

        /// <summary>
        /// Executed when the Action FINISHED playing, on every end path: input released (Stop On Release),
        /// duration elapsed, the Mode/animation ended, another Action took over, or the Action was interrupted.
        /// Fires AFTER the Action's OnActionExit Reaction, so those Reactions still read the Action's runtime data.
        /// Use it instead of <see cref="ActionExit"/> when the teardown must also happen on a normal, non-forced exit.
        /// </summary>
        public virtual void ActionEnd(SuperItem item, ItemActionSet set, ItemAction itemAction) { }

        /// <summary>
        /// Executed when the Action was ended by a FAILED Condition or PreRequisite while it was playing
        /// (E.g. an [Aim Angle] limit leaving its range). Fires right after the release pass and before the Action
        /// really exits, so a Processor can undo state a normal end would keep.
        /// </summary>
        public virtual void ActionFailed(SuperItem item, ItemActionSet set, ItemAction itemAction) { }

        /// <summary>  Executed when the set is entered or swapped from another </summary>
        public virtual void SetEnter(SuperItem item, ItemActionSet set, ItemAction itemAction) { }

        public virtual void OnEnable(SuperItem item, ItemActionSet set, ItemAction itemAction) { }

        public virtual void OnDisable(SuperItem item, ItemActionSet set, ItemAction itemAction) { }
        public virtual void OnDrawGizmos(SuperItem item, ItemActionSet set, ItemAction itemAction) { }
        public virtual void OnDrawGizmosSelected(SuperItem item, ItemActionSet set, ItemAction itemAction) { }

        /// <summary>
        /// Make sure the processor is correctly configured. This is called when the user clicks the "Validate" button in the Item Editor Window. Return a string with the error or warning, or return null or empty if everything is correct.
        /// </summary>
        public abstract string Validation(SuperItem item, ItemActionSet set, ItemAction action);

        /// <summary> This method is called by the Super Item OnValidate. Editor Only </summary>
        public virtual void OnValidate(SuperItem item, ItemActionSet set, ItemAction action) { }

        /// <summary>
        /// Reset the processor to its default values. This is called when the user clicks the "Reset" button in the Item Editor Window.
        /// This is useful for resetting the processor to its default values after created a new processor or when the user wants to reset the processor to its default values.
        /// </summary>
        public virtual void Reset() { }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only. Called ONCE right after this processor is created and its type is chosen in the Item inspector.
        /// Unlike <see cref="Reset"/> (which only knows the processor itself), this receives the owning Item, Set and Action,
        /// so a processor can auto-fill its own fields, add a Condition to the owning Action, or add/configure a component
        /// on the Item's GameObject. Mark anything you change dirty (EditorUtility.SetDirty) inside the override.
        /// </summary>
        //MWC — context-aware creation hook (see ItemProcessorDrawer.RunOnCreatedHook)
        public virtual void OnCreated(SuperItem item, ItemActionSet set, ItemAction action) { }
#endif
    }


#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ItemProcessor))]
    public class ItemProcessorDrawer : PropertyDrawer
    {
        private const int k_MaxTypePopupLineCount = 8;
        private static readonly Type k_UnityObjectType = typeof(UnityEngine.Object);
        // static readonly GUIContent k_NullDisplayName = new(TypeMenuUtility.k_NullDisplayName);

        private readonly Dictionary<string, TypePopupCache> m_TypePopups = new();
        private readonly Dictionary<string, GUIContent> m_TypeNameCaches = new();

        private SerializedProperty m_TargetProperty;

        private static GUIContent Icon_Delete;
        private static GUIContent Icon_Edit;
        private static GUIContent Icon_Delay;
        private static GUIContent Icon_Help;

        //MWC — Documentation URL per processor type, read from its [HelpURL]. Cached because resolving it is
        //reflection and this runs on every repaint of every processor row. A null result is cached too, so a
        //processor without the attribute is never probed twice.
        private static readonly Dictionary<Type, string> s_HelpURLs = new();

        private static string GetHelpURL(ItemProcessor processor)
        {
            if (processor == null) return null;

            var type = processor.GetType();

            if (!s_HelpURLs.TryGetValue(type, out var url))
            {
                var attributes = type.GetCustomAttributes(typeof(HelpURLAttribute), true);
                url = attributes.Length > 0 ? ((HelpURLAttribute)attributes[0]).URL : null;
                s_HelpURLs[type] = url;
            }
            return url;
        }

        //MWC — hover help for the Execute flags field. Unity's InspectorName has no tooltip overload, so the
        //explanation of every flag lives here and is shown by a transparent overlay label (see OnGUI).
        private static readonly GUIContent s_ExecuteTooltip = new(string.Empty,
            "WHEN this Processor executes. Multiple flags can be combined.\n\n" +
            "• On Action End — the Action FINISHED playing (its Mode/animation ended), whatever ended it. Use it instead of On Action Released to undo things once the animation is over.\n" +
            "• On Prev Completed — a Processor ABOVE finished successfully (Charge reaching full, Wait elapsing).\n" +
            "• On Prev Interrupted — a Processor ABOVE was interrupted (Charge released before it filled).\n\n" +
            "Every following Processor with the matching flag runs.");

        //MWC — hover help for the Active toggle and the delay field
        private static readonly GUIContent s_ActiveTooltip = new(string.Empty,
            "Enable/Disable this Processor.\nA disabled Processor never executes and is skipped by the Prev Completed / Prev Interrupted chain.");

        //MWC — tooltip for the delay value (the clock icon next to it has its own)
        private static readonly GUIContent Icon_Delay_Field = new(string.Empty,
            "Delay in seconds before this Processor executes.\nApplies to every execution path, including the Prev Completed / Prev Interrupted chain.");

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            FindIcons();

            var Height = EditorGUIUtility.singleLineHeight - 2;

            var Rc = new Rect(position) { height = EditorGUIUtility.singleLineHeight + 1, };

            GUI.Box(Rc, GUIContent.none, EditorStyles.toolbar); // Draw a box around the property

            //MWC — resolved once per row: managedReferenceValue deserializes the managed reference on every access,
            //and this runs for every processor on every repaint. It was read twice, and the Help button made it three times.
            var processor = property.managedReferenceValue as ItemProcessor;

            var isNull = processor == null;
            var Active = property.FindPropertyRelative("Active");
            var execute = property.FindPropertyRelative("execute");
            var delay = property.FindPropertyRelative("executeDelay");

            string targetName = label.text;

            if (!isNull)
            {
                targetName = $"{processor.DynamicName}"; //MWC — was a second managedReferenceValue read
                label.text = "       " + targetName; //Add the space for the active option
            }
            else
            {
                label.text = targetName.Replace("Element", "Processor");
            }

            EditorGUIUtility.labelWidth = 0;

            var text = label.text;
            // Rc.y += 1; // Adjust the position to fit the label inside the box
            EditorGUI.LabelField(Rc, text, EditorStyles.boldLabel);

            label.text = string.Empty; // Clear the label text to avoid duplication

            label = EditorGUI.BeginProperty(position, label, property);
            {
                var width = 25;
                var popupPosition = new Rect(position);
                popupPosition.width -= EditorGUIUtility.labelWidth;
                popupPosition.x += EditorGUIUtility.labelWidth;
                popupPosition.height = EditorGUIUtility.singleLineHeight;

                if (!isNull)
                {
                    var first = position.x + popupPosition.width + EditorGUIUtility.labelWidth - width;

                    #region Rects

                    var RemoveRect = new Rect(position)
                    {
                        x = first,
                        y = position.y,
                        height = Height,
                        width = width,
                    };

                    var actRect = new Rect(position)
                    {
                        x = position.x + 4,
                        y = position.y + 2,
                        height = Height,
                        width = width - 4,
                    };

                    //MWC — Help button, tucked between the flags field and the delete button
                    var HelpRect = new Rect(position)
                    {
                        x = RemoveRect.x - 20,
                        y = position.y + 2,
                        height = Height,
                        width = 20,
                    };

                    //MWC — anchor the right edge to the delete button. It was x = position.width - 100 with width 140,
                    //which ran 40px past the row under the delete button and stole its clicks (opened the flags dropdown instead)
                    //MWC — now anchored to the Help button, which sits between the two
                    var executeOnRect = new Rect(position)
                    {
                        x = HelpRect.x - 140 - 4,
                        y = position.y + 2,
                        height = Height,
                        width = 140,
                    };

                    //MWC: delay float field + icon, placed just left of the execute-flags field (mirrors ReactionCoreDrawer)
                    var DelayFieldWidth = 34f;
                    var DelayRect = new Rect(position)
                    {
                        x = executeOnRect.x - DelayFieldWidth - 4,
                        y = position.y + 2,
                        height = Height,
                        width = DelayFieldWidth,
                    };

                    var DelayIconRect = new Rect(position)
                    {
                        x = DelayRect.x - 18,
                        y = position.y + 2,
                        height = Height,
                        width = 18,
                    };

                    #endregion

                    // EditorGUI.PropertyField(executeOnRect, execute, GUIContent.none);
                    var res = EditorGUI.EnumFlagsField(executeOnRect, GUIContent.none, (ExecuteProcessor)execute.enumValueFlag);
                    execute.enumValueFlag = (int)(ExecuteProcessor)res;

                    //MWC — transparent overlay label carrying the flags help. GUI.Label never consumes mouse
                    //events, so the dropdown underneath keeps working while hovering shows the explanation.
                    GUI.Label(executeOnRect, s_ExecuteTooltip);

                    //MWC: draw the delay icon + value
                    GUI.Label(DelayIconRect, Icon_Delay, EditorStyles.iconButton);
                    delay.floatValue = EditorGUI.FloatField(DelayRect, GUIContent.none, delay.floatValue);
                    GUI.Label(DelayRect, Icon_Delay_Field); //MWC — tooltip for the delay value itself

                    Active.boolValue = GUI.Toggle(actRect, Active.boolValue, GUIContent.none);
                    GUI.Label(actRect, s_ActiveTooltip); //MWC — tooltip for the Active toggle

                    //MWC — Help button. Greyed out (rather than hidden) when the processor has no [HelpURL],
                    //so every row keeps the same layout and a missing page is visible instead of silent.
                    var helpURL = GetHelpURL(processor);

                    using (new EditorGUI.DisabledScope(helpURL == null))
                    {
                        Icon_Help.tooltip = helpURL != null
                            ? $"Open the documentation for this Processor\n{helpURL}"
                            : "This Processor has no documentation page.\nAdd a [HelpURL] attribute to its class to link one";

                        if (GUI.Button(HelpRect, Icon_Help, EditorStyles.iconButton))
                            Application.OpenURL(helpURL);
                    }

                    if (GUI.Button(RemoveRect, Icon_Delete, EditorStyles.toolbarButton))
                    {
                        property.managedReferenceValue = null;
                        property.serializedObject.ApplyModifiedProperties();
                    }
                }
                else
                {
                    var propertyName = TypePopupCache.GetTypeName(property, m_TypeNameCaches);

                    var guiColor = GUI.color;
                    GUI.color = Color.red + Color.white * 0.5f;

                    if (EditorGUI.DropdownButton(popupPosition, propertyName, FocusType.Keyboard))
                    {
                        TypePopupCache popup = GetTypePopup(property);
                        m_TargetProperty = property;
                        popup.TypePopup.Show(popupPosition);
                    }
                    GUI.color = guiColor;
                }

                position.y += 2;

                // Draw the managed reference property.
                EditorGUI.PropertyField(position, property, label, true);
            }
            EditorGUI.EndProperty();
            EditorGUI.indentLevel = indent;
        }

        private static void FindIcons()
        {
            if (Icon_Delete == null)
            {
                Icon_Delete = EditorGUIUtility.IconContent("d_TreeEditor.Trash");
                Icon_Delete.tooltip = "Remove this Processor"; //MWC — was "Clear the Condition" (copy-paste from the Condition drawer)
            }

            if (Icon_Edit == null)
            {
                Icon_Edit = MalbersEditor.Icon_Edit;
                Icon_Edit.tooltip = "Edit the reaction Description";
            }

            if (Icon_Delay == null) //MWC: same delay icon used by the Reaction drawer
            {
                Icon_Delay = EditorGUIUtility.IconContent("d_UnityEditor.AnimationWindow@2x");
                Icon_Delay.tooltip = "Delay the Processor for x Seconds";
            }

            //MWC — cloned, not shared: the tooltip is rewritten per row (documented vs. undocumented processor),
            //and IconContent returns a cached instance that other drawers also use.
            Icon_Help ??= new GUIContent(EditorGUIUtility.IconContent("d_Help@2x").image);
        }

        private TypePopupCache GetTypePopup(SerializedProperty property)
        {
            // Cache this string. This property internally call Assembly.GetName, which result in a large allocation.
            string managedReferenceFieldTypeName = property.managedReferenceFieldTypename;

            if (!m_TypePopups.TryGetValue(managedReferenceFieldTypeName, out TypePopupCache result))
            {
                var state = new AdvancedDropdownState();

                Type baseType = MSerializedTools.GetType(managedReferenceFieldTypeName);
                var popup = new AdvancedTypePopup(
                    TypeCache.GetTypesDerivedFrom(baseType).Append(baseType).Where(p =>
                        (p.IsPublic || p.IsNestedPublic) &&
                        !p.IsAbstract &&
                        !p.IsGenericType &&
                        !k_UnityObjectType.IsAssignableFrom(p) &&
                        Attribute.IsDefined(p, typeof(SerializableAttribute))
                    ),
                    k_MaxTypePopupLineCount, state);

                popup.OnItemSelected += item =>
                {
                    Type type = item.Type;
                    object obj = m_TargetProperty.SetManagedReference(type);
                    m_TargetProperty.isExpanded = (obj != null);
                    m_TargetProperty.serializedObject.ApplyModifiedProperties();

                    //Set the default execute value when creating a new processor
                    ItemProcessor processor = obj as ItemProcessor;
                    processor.execute = processor.InitialExecute;
                    processor.Reset(); //MWC: Reset the processor to its default values right after it's created
                    m_TargetProperty.managedReferenceValue = processor;


                    m_TargetProperty.serializedObject.ApplyModifiedProperties();

                    m_TargetProperty.serializedObject.Update();

                    RunOnCreatedHook(m_TargetProperty); //MWC — let the processor self-configure using its owning Item/Set/Action
                };

                result = new TypePopupCache(popup, state);
                m_TypePopups.Add(managedReferenceFieldTypeName, result);
            }
            return result;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, true) + 2;
        }

        //MWC — After a processor is created, resolve its owning Item/Set/Action and invoke OnCreated on the LIVE instance.
        //(The managed reference can be re-deserialized by ApplyModifiedProperties/Update, so we re-fetch it from the array.)
        private static void RunOnCreatedHook(SerializedProperty processorProp)
        {
            var so = processorProp.serializedObject;
            if (so.targetObject is not SuperItem item) return;

            //Path looks like: actionSets.Array.data[setIndex].actions.Array.data[actionIndex].processors.Array.data[procIndex]
            var idx = ParseDataIndices(processorProp.propertyPath);
            if (idx.Count < 3) return;

            int si = idx[0], ai = idx[1], pi = idx[2];

            if (item.actionSets == null || si < 0 || si >= item.actionSets.Count) return;
            var set = item.actionSets[si];
            if (set?.actions == null || ai < 0 || ai >= set.actions.Count) return;
            var action = set.actions[ai];
            if (action?.processors == null || pi < 0 || pi >= action.processors.Length) return;

            var live = action.processors[pi];
            if (live == null) return;

            Undo.RegisterCompleteObjectUndo(item, "Create Item Processor");
            live.OnCreated(item, set, action);

            EditorUtility.SetDirty(item);
            so.Update();
        }

        //MWC — extract the numeric indices from a SerializedProperty path (…data[N]…)
        private static List<int> ParseDataIndices(string path)
        {
            var indices = new List<int>();
            const string token = "data[";
            int i = 0;
            while ((i = path.IndexOf(token, i, StringComparison.Ordinal)) >= 0)
            {
                i += token.Length;
                int end = path.IndexOf(']', i);
                if (end < 0) break;
                if (int.TryParse(path.Substring(i, end - i), out var n)) indices.Add(n);
                i = end + 1;
            }
            return indices;
        }
    }
#endif
}