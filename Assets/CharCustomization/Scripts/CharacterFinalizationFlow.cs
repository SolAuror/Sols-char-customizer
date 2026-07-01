using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterFinalizationFlow : MonoBehaviour
    {
        [Header("Character")]
        [SerializeField] private CharacterProfile profile;
        [SerializeField] private CharacterMorphDemoUI demoUI;

        [Header("Interface")]
        [SerializeField] private TMP_InputField characterNameInput;
        [SerializeField] private Button randomizeButton;
        [SerializeField] private Button finalizeButton;
        [SerializeField] private TMP_Text finalizeButtonLabel;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private GameObject customizationInterface;

        [Header("Optional Gameplay Handoff")]
        [SerializeField] private CharacterPreviewControls previewControls;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private Behaviour gameplayController;
        [SerializeField, Min(0f)] private float transitionDuration = 0.75f;

        [Header("Testing")]
        [SerializeField] private string savePathOverride;

        private ICharacterPlayerSaveRepository repository;
        private string pendingOverwriteName;
        private bool listenersRegistered;

        public event Action<PlayerCharacterRecord> Finalized;

        public TMP_InputField CharacterNameInput => characterNameInput;
        public Camera GameplayCamera => gameplayCamera;
        public Behaviour GameplayController => gameplayController;
        public ICharacterPlayerSaveRepository Repository => repository;

        private void Awake()
        {
            repository = new CharacterPlayerSaveRepository(savePathOverride);
            if (customizationInterface == null)
            {
                customizationInterface = gameObject;
            }

            if (gameplayCamera != null)
            {
                gameplayCamera.enabled = false;
                SetAudioListenerEnabled(gameplayCamera, false);
            }

            if (gameplayController != null)
            {
                if (gameplayController is DemoPlayerController demoPlayerController)
                {
                    demoPlayerController.SetGameplayInputEnabled(false);
                }

                gameplayController.enabled = false;
            }
        }

        private void OnEnable()
        {
            RegisterListeners();
        }

        private void OnDisable()
        {
            UnregisterListeners();
        }

        public void Randomize()
        {
            if (profile == null)
            {
                SetStatus("The character profile is not assigned.", true);
                return;
            }

            profile.RandomizeCurrent(0.65f);
            demoUI?.RefreshPanel();
            demoUI?.RefreshSkinPanel();
            ClearOverwriteConfirmation();
            SetStatus("Character randomized.", false);
        }

        public void SetPlayerSaveRepository(ICharacterPlayerSaveRepository saveRepository)
        {
            repository = saveRepository ?? new CharacterPlayerSaveRepository(savePathOverride);
        }

        public void FinalizeCharacter()
        {
            if (profile == null || characterNameInput == null)
            {
                SetStatus("The finalization flow is not fully configured.", true);
                return;
            }

            string playerName = characterNameInput.text.Trim();
            bool confirmOverwrite = string.Equals(
                pendingOverwriteName,
                playerName,
                StringComparison.OrdinalIgnoreCase);
            CharacterRecipe recipe = profile.CaptureRecipe();

            if (!repository.TrySavePlayer(
                    playerName,
                    recipe,
                    confirmOverwrite,
                    out PlayerCharacterRecord record,
                    out bool duplicateName,
                    out string error))
            {
                if (duplicateName)
                {
                    pendingOverwriteName = playerName;
                    SetFinalizeButtonLabel("Confirm Overwrite");
                    SetStatus($"A player named '{playerName}' already exists. Select Finalize again to overwrite it.", true);
                }
                else
                {
                    ClearOverwriteConfirmation();
                    SetStatus(error, true);
                }

                return;
            }

            ClearOverwriteConfirmation();
            Finalized?.Invoke(record);

            if (!HasCompleteHandoff(out string handoffError))
            {
                SetStatus(string.IsNullOrEmpty(handoffError)
                    ? $"Saved {record.PlayerName}."
                    : $"Saved {record.PlayerName}. {handoffError}", !string.IsNullOrEmpty(handoffError));
                return;
            }

            if (!TryPrepareGameplayController(out string controllerError))
            {
                SetStatus(
                    $"Saved {record.PlayerName}. {controllerError}",
                    true);
                return;
            }

            if (!previewControls.BlendTo(gameplayCamera, transitionDuration, CompleteGameplayHandoff))
            {
                SetStatus(
                    $"Saved {record.PlayerName}. The gameplay camera handoff could not start, so the customization UI stayed active.",
                    true);
                return;
            }

            SetCustomizationInteractable(false);
            if (customizationInterface != null)
            {
                customizationInterface.SetActive(false);
            }

            SetStatus($"Saved {record.PlayerName}.", false);
        }

        private void CompleteGameplayHandoff()
        {
            if (gameplayController != null)
            {
                if (gameplayController is DemoPlayerController demoPlayerController)
                {
                    demoPlayerController.SetGameplayInputEnabled(true);
                }

                gameplayController.enabled = true;
            }

            previewControls.enabled = false;
        }

        private bool HasCompleteHandoff(out string error)
        {
            error = null;
            bool hasAnyHandoff = gameplayCamera != null || gameplayController != null;
            if (!hasAnyHandoff)
            {
                return false;
            }

            if (gameplayCamera == null)
            {
                error = "Assign a gameplay camera before enabling the finalization handoff.";
                return false;
            }

            if (previewControls == null)
            {
                error = "Assign preview controls before enabling the finalization handoff.";
                return false;
            }

            if (previewControls.PreviewCamera == null)
            {
                error = "The preview controls do not have a preview camera assigned.";
                return false;
            }

            if (previewControls.PreviewCamera == gameplayCamera)
            {
                error = "The gameplay camera must be separate from the preview camera.";
                return false;
            }

            return true;
        }

        private bool TryPrepareGameplayController(out string error)
        {
            error = null;
            if (gameplayController is not DemoPlayerController demoPlayerController)
            {
                return true;
            }

            CharacterMorphController controller = profile != null ? profile.Controller : null;
            if (controller == null || !controller.TryGetActiveAnimator(out Animator activeAnimator))
            {
                error = "The active character body has no Animator, so gameplay control could not start.";
                return false;
            }

            demoPlayerController.BindAnimator(activeAnimator);
            demoPlayerController.SetGameplayInputEnabled(false);
            return true;
        }

        private static void SetAudioListenerEnabled(Camera targetCamera, bool enabled)
        {
            if (targetCamera != null && targetCamera.TryGetComponent(out AudioListener listener))
            {
                listener.enabled = enabled;
            }
        }

        private void SetCustomizationInteractable(bool interactable)
        {
            if (randomizeButton != null)
            {
                randomizeButton.interactable = interactable;
            }

            if (finalizeButton != null)
            {
                finalizeButton.interactable = interactable;
            }

            if (characterNameInput != null)
            {
                characterNameInput.interactable = interactable;
            }
        }

        private void RegisterListeners()
        {
            if (listenersRegistered || characterNameInput == null || randomizeButton == null || finalizeButton == null)
            {
                return;
            }

            characterNameInput.onValueChanged.AddListener(OnCharacterNameChanged);
            randomizeButton.onClick.AddListener(Randomize);
            finalizeButton.onClick.AddListener(FinalizeCharacter);
            listenersRegistered = true;
        }

        private void UnregisterListeners()
        {
            if (!listenersRegistered)
            {
                return;
            }

            characterNameInput.onValueChanged.RemoveListener(OnCharacterNameChanged);
            randomizeButton.onClick.RemoveListener(Randomize);
            finalizeButton.onClick.RemoveListener(FinalizeCharacter);
            listenersRegistered = false;
        }

        private void OnCharacterNameChanged(string _)
        {
            ClearOverwriteConfirmation();
        }

        private void ClearOverwriteConfirmation()
        {
            pendingOverwriteName = null;
            SetFinalizeButtonLabel("Finalize");
        }

        private void SetFinalizeButtonLabel(string label)
        {
            if (finalizeButtonLabel != null)
            {
                finalizeButtonLabel.text = label;
            }
        }

        private void SetStatus(string message, bool isError)
        {
            if (statusLabel == null)
            {
                if (isError && !string.IsNullOrWhiteSpace(message))
                {
                    Debug.LogWarning(message, this);
                }
                return;
            }

            statusLabel.text = message ?? string.Empty;
            statusLabel.color = isError
                ? new Color(1f, 0.55f, 0.45f, 1f)
                : new Color(0.65f, 0.9f, 0.7f, 1f);
        }
    }
}
