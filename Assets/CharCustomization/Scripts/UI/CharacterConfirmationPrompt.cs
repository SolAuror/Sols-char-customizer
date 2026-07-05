using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterConfirmationPrompt : MonoBehaviour
    {
        private static readonly HashSet<string> SuppressedSessionKeys = new(StringComparer.Ordinal);

        [Header("Authored UI")]
        [SerializeField] private GameObject modalRoot;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text bodyLabel;
        [SerializeField] private Toggle dontShowAgainToggle;
        [SerializeField] private Button yesButton;
        [SerializeField] private Button noButton;

        private Action pendingConfirm;
        private Action pendingCancel;
        private string pendingSessionKey;
        private bool listenersRegistered;
        private bool isShowing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionState()
        {
            SuppressedSessionKeys.Clear();
        }

        private void Awake()
        {
            ResolveMissingReferences();
            if (!isShowing && modalRoot != null && modalRoot.activeSelf)
            {
                Hide();
            }
        }

        private void OnEnable()
        {
            RegisterListeners();
        }

        private void OnDisable()
        {
            UnregisterListeners();
            ClearPendingCallbacks();
        }

        public bool Show(
            string sessionKey,
            string title,
            string body,
            Action onConfirm,
            Action onCancel = null)
        {
            if (!string.IsNullOrWhiteSpace(sessionKey) && SuppressedSessionKeys.Contains(sessionKey))
            {
                onConfirm?.Invoke();
                return true;
            }

            ResolveMissingReferences();
            if (!HasRequiredReferences())
            {
                Debug.LogWarning("The confirmation prompt is missing authored UI references.", this);
                return false;
            }

            pendingSessionKey = sessionKey;
            pendingConfirm = onConfirm;
            pendingCancel = onCancel;

            titleLabel.text = title ?? "Confirm";
            bodyLabel.text = body ?? string.Empty;
            dontShowAgainToggle.SetIsOnWithoutNotify(false);
            isShowing = true;
            modalRoot.SetActive(true);
            RegisterListeners();
            return true;
        }

        public void Hide()
        {
            if (modalRoot != null)
            {
                modalRoot.SetActive(false);
            }

            isShowing = false;
            ClearPendingCallbacks();
        }

        private void Confirm()
        {
            Action confirm = pendingConfirm;
            if (dontShowAgainToggle != null &&
                dontShowAgainToggle.isOn &&
                !string.IsNullOrWhiteSpace(pendingSessionKey))
            {
                SuppressedSessionKeys.Add(pendingSessionKey);
            }

            Hide();
            confirm?.Invoke();
        }

        private void Cancel()
        {
            Action cancel = pendingCancel;
            Hide();
            cancel?.Invoke();
        }

        private void RegisterListeners()
        {
            if (listenersRegistered)
            {
                return;
            }

            yesButton?.onClick.AddListener(Confirm);
            noButton?.onClick.AddListener(Cancel);
            listenersRegistered = true;
        }

        private void UnregisterListeners()
        {
            if (!listenersRegistered)
            {
                return;
            }

            yesButton?.onClick.RemoveListener(Confirm);
            noButton?.onClick.RemoveListener(Cancel);
            listenersRegistered = false;
        }

        private void ClearPendingCallbacks()
        {
            pendingConfirm = null;
            pendingCancel = null;
            pendingSessionKey = null;
        }

        private bool HasRequiredReferences()
        {
            return modalRoot != null &&
                   titleLabel != null &&
                   bodyLabel != null &&
                   dontShowAgainToggle != null &&
                   yesButton != null &&
                   noButton != null;
        }

        private void ResolveMissingReferences()
        {
            modalRoot ??= gameObject;
            titleLabel ??= FindText("Title");
            bodyLabel ??= FindText("Body");
            dontShowAgainToggle ??= FindComponent<Toggle>("Dont Show Again", "Don't Show Again");
            yesButton ??= FindComponent<Button>("Yes Button");
            noButton ??= FindComponent<Button>("No Button");
        }

        private TMP_Text FindText(params string[] objectNames)
        {
            TMP_Text[] labels = GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text label in labels)
            {
                if (MatchesAnyName(label.gameObject.name, objectNames))
                {
                    return label;
                }
            }

            return null;
        }

        private T FindComponent<T>(params string[] objectNames)
            where T : Component
        {
            T[] components = GetComponentsInChildren<T>(true);
            foreach (T component in components)
            {
                if (MatchesAnyName(component.gameObject.name, objectNames))
                {
                    return component;
                }
            }

            return null;
        }

        private static bool MatchesAnyName(string candidateName, IReadOnlyList<string> objectNames)
        {
            for (int index = 0; index < objectNames.Count; index++)
            {
                if (string.Equals(candidateName, objectNames[index], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
