// Last Change: 9/19/2026 4:05pm
using MalbersAnimations.Events;
using MalbersAnimations.Items;
using UnityEngine;

namespace MalbersAnimations.UI
{
    /// <summary> One row of the Super Item Actions UI: [Action Name] [Hold] [Key]. Everything is pushed out through events so the visuals are free to change </summary>
    [AddComponentMenu("Malbers/UI/Super Item Action Row UI")]
    public class SuperItemActionRowUI : MonoBehaviour
    {
        [Tooltip("Canvas Group of the row. Its alpha is lowered while the Action is Blocked")]
        public CanvasGroup group;

        [Tooltip("Alpha of the row while the Action is Blocked (Block Actions processor, inactive Action or Set)")]
        [Range(0f, 1f)] public float blockedAlpha = 0.35f;

        [Header("Events")]
        [Tooltip("Name of the Action. E.g. Attack, Aim, Charge & Fire")]
        public StringEvent OnActionName = new();
        [Tooltip("Display name of the control on the active device. E.g. LMB, E, RT")]
        public StringEvent OnInputName = new();
        [Tooltip("Label of the interaction. E.g. Hold, x2. Empty for a plain press")]
        public StringEvent OnInteraction = new();
        [Tooltip("True when the interaction label is not empty (show/hide the label)")]
        public BoolEvent OnHasInteraction = new();
        [Tooltip("Icon of the control when the Input Prompt Icons asset has one for it")]
        public SpriteEvent OnIcon = new();
        [Tooltip("True when the control has an icon (show the icon)")]
        public BoolEvent OnHasIcon = new();
        [Tooltip("True when the control has NO icon (show the key text)")]
        public BoolEvent OnHasText = new();
        [Tooltip("True while the Action is playing")]
        public BoolEvent OnPlaying = new();
        [Tooltip("True while the Action is Blocked")]
        public BoolEvent OnBlocked = new();

        /// <summary> Item that owns the Action shown on this row </summary>
        public SuperItem Item { get; private set; }
        /// <summary> Action shown on this row </summary>
        public ItemAction Action { get; private set; }
        /// <summary> Last playing state pushed to the row </summary>
        public bool Playing { get; private set; }
        /// <summary> Last blocked state pushed to the row </summary>
        public bool Blocked { get; private set; }

        /// <summary> Fills the whole row </summary>
        public void Set(SuperItem item, ItemAction action, string actionName, string inputName, string interaction, Sprite icon, bool playing, bool blocked)
        {
            Item = item;
            Action = action;

            OnActionName.Invoke(actionName ?? string.Empty);
            OnInputName.Invoke(inputName ?? string.Empty);

            var hasInteraction = !string.IsNullOrEmpty(interaction);
            OnInteraction.Invoke(hasInteraction ? interaction : string.Empty);
            OnHasInteraction.Invoke(hasInteraction);

            var hasIcon = icon != null;
            OnIcon.Invoke(icon);
            OnHasIcon.Invoke(hasIcon);
            OnHasText.Invoke(!hasIcon);

            SetPlaying(playing, true);
            SetBlocked(blocked, true);
        }

        /// <summary> Updates the playing highlight </summary>
        public void SetPlaying(bool value, bool force = false)
        {
            if (!force && Playing == value) return;
            Playing = value;
            OnPlaying.Invoke(value);
        }

        /// <summary> Updates the blocked state (dims the row) </summary>
        public void SetBlocked(bool value, bool force = false)
        {
            if (!force && Blocked == value) return;
            Blocked = value;
            if (group) group.alpha = value ? blockedAlpha : 1f;
            OnBlocked.Invoke(value);
        }

        /// <summary> Forgets the Item and Action (the row is going back to the pool) </summary>
        public void Clear()
        {
            Item = null;
            Action = null;
        }

        private void Reset() => group = GetComponent<CanvasGroup>();
    }
}
