// Last Change: 9/30/2026 10:58am
using MalbersAnimations.Events;
using MalbersAnimations.Items;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.UI
{
    //Shows the Actions the player can use with the Super Items in their hands and which input drives each one:
    //  Attack        [LMB]
    //  Attack Heavy  Hold [LMB]
    //  Block         [RMB]
    //The Items reach the UI through MEvents (SI Item Equipped / SI Item Unequipped, raised by the Equip Points of the
    //character). From then on the UI watches the Manager itself, so Action Set changes (Bow: Aim → Charge & Fire),
    //Blocked Actions and the playing state are reflected without any extra event.
    /// <summary> UI that lists the available Item Actions of the equipped Super Items and the input that starts each one </summary>
    [AddComponentMenu("Malbers/UI/Super Item Actions UI")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/super-item")]
    public class SuperItemActionsUI : MonoBehaviour
    {
        /// <summary> Interaction name (Press, LongPress, DoubleTap, Toggle...) → label shown on the row </summary>
        [System.Serializable]
        public class InteractionLabel
        {
            [Tooltip("Interaction of the input row as the Input Link names it: Press, Down, Up, LongPress, DoubleTap, Toggle")]
            public string interaction;
            [Tooltip("Text shown next to the key. Leave empty to show nothing")]
            public string label;

            public InteractionLabel(string interaction, string label)
            {
                this.interaction = interaction;
                this.label = label;
            }
        }

        [Tooltip("Row template. It is cloned for every Action shown.\nA child of this object is disabled and kept as the template; a prefab is instantiated")]
        [RequiredField] public SuperItemActionRowUI rowTemplate;

        [Tooltip("Parent of the rows. Empty = this transform")]
        public Transform container;

        [Tooltip("Optional icon set. When the control path of a binding has a Sprite here, the row shows it instead of the key text")]
        public InputPromptIcons icons;

        [Tooltip("Show the Actions that are Blocked (Block Actions processor) dimmed instead of hiding them")]
        public BoolReference showBlocked = new(true);

        [Tooltip("Show a Secondary Action (e.g. Charge & Fire) only while its Master Action (e.g. Aim) is playing")]
        public BoolReference secondaryWhileMaster = new(true);

        [Tooltip("Show the Actions of the Default Item (e.g. fists) while no Item is equipped")]
        public BoolReference showDefaultItem = new(true);

        [Tooltip("Value that changes when the input device changes (Keyboard/Gamepad). The rows are rebuilt when it changes so the keys follow the active device. E.g. the [Current Device] String Var")]
        public StringReference device = new();

        [Tooltip("Interaction of the input row → label shown next to the key. Interactions not listed show no label")]
        public List<InteractionLabel> interactionLabels = new()
        {
            new InteractionLabel("LongPress", "Hold"),
            new InteractionLabel("DoubleTap", "x2"),
            new InteractionLabel("Toggle", "Toggle"),
        };

        [Header("Events")]
        [Tooltip("True when at least one Action is shown, false when the list is empty (show/hide the panel)")]
        public BoolEvent OnHasActions = new();
        [Tooltip("Number of Actions shown")]
        public IntEvent OnActionCount = new();

        public bool debug;

        /// <summary> Super Item Manager (the character) whose equipped Items are listed </summary>
        public SuperItemManager Manager { get; private set; }

        /// <summary> Rows currently shown </summary>
        public int RowCount { get; private set; }

        private readonly List<SuperItemActionRowUI> rows = new();
        private readonly List<SuperItem> items = new();
        private readonly HashSet<string> shownKeys = new();
        private IInputPrompt prompter;
        private long lastSignature;
        private bool dirty;

        private void Awake()
        {
            if (container == null) container = transform;

            //A template that lives inside the panel is disabled so it never shows as a row of its own
            if (rowTemplate != null && rowTemplate.gameObject.scene.IsValid() && rowTemplate.transform.IsChildOf(container))
                rowTemplate.gameObject.SetActive(false);

            OnHasActions.Invoke(false);
            OnActionCount.Invoke(0);
        }

        private void OnEnable() => dirty = true;

        #region MEvent friendly entry points
        /// <summary> [SI Item Equipped] The Item (as Component) that was just equipped. Its Manager becomes the one listed </summary>
        public void SetItem(Component item) => SetItem(item as SuperItem ?? (item != null ? item.GetComponent<SuperItem>() : null));

        /// <summary> The Item that was just equipped. Its Manager becomes the one listed </summary>
        public void SetItem(SuperItem item)
        {
            if (item == null) return;

            if (item.User != null) SetManager(item.User);
            else dirty = true;
        }

        /// <summary> [SI Item Unequipped] The Item (as Component) that was just unequipped. The list is rebuilt from the Manager on the next frame </summary>
        public void ItemUnequipped(Component item) => dirty = true;

        /// <summary> Lists the Items of this Manager (as Component, e.g. the character GameObject or any of its components) </summary>
        public void SetManager(Component manager) =>
            SetManager(manager as SuperItemManager ?? (manager != null ? manager.GetComponentInParent<SuperItemManager>() : null));

        /// <summary> Lists the Items of this Manager (as GameObject) </summary>
        public void SetManager(GameObject manager) => SetManager(manager != null ? manager.GetComponentInParent<SuperItemManager>() : null);

        /// <summary> Lists the Items of this Manager </summary>
        public void SetManager(SuperItemManager manager)
        {
            if (Manager != manager)
            {
                Manager = manager;
                prompter = manager != null ? manager.InputSource as IInputPrompt : null;
                Debugging($"Manager set to [{(manager != null ? manager.name : "None")}]");
            }
            dirty = true;
        }

        /// <summary> Forgets the Manager and hides every row </summary>
        public void Clear()
        {
            Manager = null;
            prompter = null;
            HideRows(0);
            lastSignature = 0;
            dirty = false;
        }

        /// <summary> Rebuilds the rows on the next frame (e.g. after the input device changed) </summary>
        public void Refresh() => dirty = true;
        #endregion

        private void LateUpdate()
        {
            if (Manager == null || !Manager.Active)
            {
                if (RowCount > 0) HideRows(0);
                return;
            }

            var signature = Signature();

            if (dirty || signature != lastSignature)
            {
                Rebuild();
                lastSignature = signature;
                dirty = false;
            }
        }

        /// <summary> Items in the hands, in Equip Point order, plus the Default Item when it is the one active </summary>
        private void CollectItems()
        {
            items.Clear();

            var points = Manager.EquipPoints;
            for (int i = 0; i < points.Count; i++)
            {
                var item = points[i]?.Item;
                if (item != null && item.IsEquipped && !items.Contains(item)) items.Add(item);
            }

            if (showDefaultItem.Value && Manager.DefaultItemActive && !items.Contains(Manager.DefaultItem))
                items.Add(Manager.DefaultItem);
        }

        /// <summary> Cheap fingerprint of everything that changes what the rows show. A different value → the rows are rebuilt </summary>
        private long Signature()
        {
            CollectItems();

            long h = 17;
            h = h * 31 + (Manager.InputSource != null ? Manager.InputSource.GetHashCode() : 0);
            h = h * 31 + (device.Value != null ? device.Value.GetHashCode() : 0);

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var set = item.ActiveActionSet;

#if UNITY_6000_4_OR_NEWER
                h = h * 31 + item.GetEntityId().GetHashCode();
#else
                h = h * 31 + item.GetInstanceID();
#endif
                h = h * 31 + (set != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(set) : 0);
                if (set == null || set.actions == null) continue;

                h = h * 31 + (set.Active ? 1 : 0);

                for (int a = 0; a < set.actions.Count; a++)
                {
                    var action = set.actions[a];
                    if (action == null) continue;

                    int bits = (action.Active ? 1 : 0)
                             | (action.IsPlaying ? 2 : 0)
                             | (item.BlockedActions.IsBlocked(action.ActionID) ? 4 : 0)
                             | (action.MasterAction != null && action.MasterAction.IsPlaying ? 8 : 0);

                    h = h * 31 + bits;
                }
            }
            return h;
        }

        /// <summary> Fills the rows from the equipped Items (the Items were just collected by <see cref="Signature"/>) </summary>
        private void Rebuild()
        {
            int count = 0;
            shownKeys.Clear();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var set = item.ActiveActionSet;
                if (set == null || set.actions == null || !set.Active) continue;

                for (int a = 0; a < set.actions.Count; a++)
                {
                    var action = set.actions[a];
                    if (!Visible(item, action, out var blocked)) continue;

                    var inputName = InputNameOf(item, action.ActionID);
                    if (string.IsNullOrEmpty(inputName)) continue; //No input row on the Manager for this Action ID

                    var prompt = ResolvePrompt(inputName);
                    var key = $"{action.Name}|{prompt.Display}|{prompt.Interaction}";
                    if (!shownKeys.Add(key)) continue; //Dual wielding: the same Action on both hands is listed once

                    var row = GetRow(count);
                    Sprite icon = null;
                    if (icons != null) icons.TryGetSprite(prompt.ControlPath, out icon);

                    row.Set(item, action, action.Name, prompt.Display, LabelOf(prompt.Interaction), icon, action.IsPlaying, blocked);
                    row.transform.SetSiblingIndex(count);
                    count++;
                }
            }

            HideRows(count);

            Debugging($"Rebuilt: {count} Action(s) from {items.Count} Item(s)");
        }

        /// <summary> Should the Action get a row? Also reports whether the row must be dimmed </summary>
        private bool Visible(SuperItem item, ItemAction action, out bool blocked)
        {
            blocked = false;

            if (action == null || !action.Active || action.ActionID == null) return false;
            if (action.primaryHandOnly.Value && !item.IsOnPrimaryPoint) return false;             //Dual wielding follower copy: never subscribed
            if (secondaryWhileMaster.Value && action.IsSecondary && !action.MasterAction.IsPlaying) return false; //Charge & Fire only while Aiming

            blocked = item.BlockedActions.IsBlocked(action.ActionID);
            return !blocked || showBlocked.Value;
        }

        /// <summary> Input name mapped to the Item's Action ID on the Manager's Actions Input table (its own row, or an Action Equivalent's row) </summary>
        private string InputNameOf(SuperItem item, ItemActionID id)
        {
            var row = Manager.GetActionInput(item, id);
            return row != null && row.active ? row.value.Value : null;
        }

        /// <summary> Prompt of an input name on the active device. The input name itself when the source cannot resolve bindings </summary>
        private InputPrompt ResolvePrompt(string inputName)
        {
            if (prompter != null && prompter.TryGetInputPrompt(inputName, out var prompt)) return prompt;
            return new InputPrompt { Display = inputName };
        }

        /// <summary> Label for an interaction name, empty when the interaction is not on the list </summary>
        private string LabelOf(string interaction)
        {
            if (string.IsNullOrEmpty(interaction) || interactionLabels == null) return string.Empty;

            for (int i = 0; i < interactionLabels.Count; i++)
            {
                var l = interactionLabels[i];
                if (l != null && string.Equals(l.interaction, interaction, System.StringComparison.OrdinalIgnoreCase)) return l.label;
            }
            return string.Empty;
        }

        /// <summary> Row at an index, created from the template when the pool is short </summary>
        private SuperItemActionRowUI GetRow(int index)
        {
            while (rows.Count <= index)
            {
                var row = Instantiate(rowTemplate, container);
                row.name = $"{rowTemplate.name} ({rows.Count})";
                rows.Add(row);
            }

            var r = rows[index];
            if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
            return r;
        }

        /// <summary> Deactivates every row from an index on and reports the new count </summary>
        private void HideRows(int from)
        {
            for (int i = from; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r == null) continue;
                r.Clear();
                if (r.gameObject.activeSelf) r.gameObject.SetActive(false);
            }

            if (RowCount != from)
            {
                RowCount = from;
                OnHasActions.Invoke(from > 0);
                OnActionCount.Invoke(from);
            }
        }

        /// <summary>Editor only console log for this UI, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Debugging(string value)
        {
#if UNITY_EDITOR
            if (debug) MDebug.Log($"<B>[{name}] Super Item Actions UI</B> → {value}", this);
#endif
        }

        private void Reset()
        {
            container = transform;
            rowTemplate = GetComponentInChildren<SuperItemActionRowUI>(true);
        }
    }
}
