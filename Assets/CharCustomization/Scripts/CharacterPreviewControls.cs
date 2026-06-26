using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterPreviewControls : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CharacterMorphController controller;
        [SerializeField] private Camera previewCamera;

        [Header("Focus")]
        [SerializeField] private Vector3 focusOffset;
        [SerializeField] private Vector2 viewportFocus = new(0.66f, 0.5f);
        [SerializeField, Min(0.01f)] private float positionSmoothTime = 0.12f;
        [SerializeField, Min(0f)] private float rotationSharpness = 14f;

        [Header("Character Rotation")]
        [SerializeField, Min(0f)] private float rotationSensitivity = 0.25f;

        [Header("Camera Zoom")]
        [SerializeField, Min(0f)] private float zoomSensitivity = 0.003f;
        [SerializeField] private float minimumZoomOffset = -1.25f;
        [SerializeField] private float maximumZoomOffset = 1.1f;

        [Header("Camera Vertical Pan")]
        [SerializeField, Min(0f)] private float panSensitivity = 0.003f;
        [SerializeField, Min(0f)] private float maximumPanOffset = 0.9f;

        private Vector3 initialCameraPosition;
        private Vector3 viewDirection;
        private Vector3 smoothVelocity;
        private float baseDistance;
        private float zoomOffset;
        private float panOffset;
        private bool initialized;
        private bool isBlending;
        private readonly List<RaycastResult> interfaceHits = new();

        public Camera PreviewCamera => previewCamera;
        public bool IsBlending => isBlending;

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<CharacterMorphController>();
            }

            if (previewCamera == null)
            {
                previewCamera = Camera.main;
            }

            if (controller == null || previewCamera == null)
            {
                Debug.LogError("Character preview controls require a morph controller and preview camera.", this);
                enabled = false;
                return;
            }

            initialCameraPosition = previewCamera.transform.position;
            Vector3 focusPoint = CalculateFocusPoint();
            Vector3 focusToCamera = initialCameraPosition - focusPoint;
            baseDistance = Mathf.Max(0.1f, focusToCamera.magnitude);
            viewDirection = focusToCamera.sqrMagnitude > 0.0001f
                ? focusToCamera.normalized
                : -previewCamera.transform.forward;
            initialized = true;
            SnapToFocus();
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (!initialized || isBlending || mouse == null ||
                IsPointerOverInterface(mouse.position.ReadValue()))
            {
                return;
            }

            Vector2 pointerDelta = mouse.delta.ReadValue();
            if (mouse.leftButton.isPressed)
            {
                RotateCharacter(-pointerDelta.x * rotationSensitivity);
            }

            if (mouse.rightButton.isPressed)
            {
                PanVertical(pointerDelta.y * panSensitivity);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (!Mathf.Approximately(scroll, 0f))
            {
                Zoom(scroll * zoomSensitivity);
            }
        }

        private void LateUpdate()
        {
            if (!initialized || isBlending)
            {
                return;
            }

            Vector3 targetFocusPoint = CalculateFocusPoint() + Vector3.up * panOffset;
            float distance = Mathf.Max(0.1f, baseDistance - zoomOffset);
            Vector3 focusPoint = CalculateFramedFocusPoint(targetFocusPoint, distance);
            Vector3 desiredPosition = focusPoint + viewDirection * distance;
            Transform cameraTransform = previewCamera.transform;
            cameraTransform.position = Vector3.SmoothDamp(
                cameraTransform.position,
                desiredPosition,
                ref smoothVelocity,
                positionSmoothTime);

            Vector3 lookDirection = focusPoint - cameraTransform.position;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                float interpolation = 1f - Mathf.Exp(-rotationSharpness * Time.unscaledDeltaTime);
                cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, desiredRotation, interpolation);
            }
        }

        public void RotateCharacter(float degrees)
        {
            Transform character = controller.ActiveCharacterRoot;
            if (character != null)
            {
                character.Rotate(Vector3.up, degrees, Space.World);
            }
        }

        public void Zoom(float amount)
        {
            zoomOffset = Mathf.Clamp(zoomOffset + amount, minimumZoomOffset, maximumZoomOffset);
        }

        public void PanVertical(float amount)
        {
            panOffset = Mathf.Clamp(panOffset + amount, -maximumPanOffset, maximumPanOffset);
        }

        public void ResetCamera()
        {
            zoomOffset = 0f;
            panOffset = 0f;
            smoothVelocity = Vector3.zero;
        }

        public void SnapToFocus()
        {
            if (!initialized)
            {
                return;
            }

            Vector3 targetFocusPoint = CalculateFocusPoint() + Vector3.up * panOffset;
            float distance = Mathf.Max(0.1f, baseDistance - zoomOffset);
            Vector3 focusPoint = CalculateFramedFocusPoint(targetFocusPoint, distance);
            Transform cameraTransform = previewCamera.transform;
            cameraTransform.position = focusPoint + viewDirection * distance;
            cameraTransform.rotation = Quaternion.LookRotation(focusPoint - cameraTransform.position, Vector3.up);
            smoothVelocity = Vector3.zero;
        }

        public bool BlendTo(Camera gameplayCamera, float duration, Action onComplete = null)
        {
            if (!initialized || isBlending || gameplayCamera == null || gameplayCamera == previewCamera)
            {
                return false;
            }

            StartCoroutine(BlendRoutine(gameplayCamera, Mathf.Max(0f, duration), onComplete));
            return true;
        }

        private IEnumerator BlendRoutine(Camera gameplayCamera, float duration, Action onComplete)
        {
            isBlending = true;
            gameplayCamera.enabled = false;

            Transform sourceTransform = previewCamera.transform;
            Transform targetTransform = gameplayCamera.transform;
            Vector3 startPosition = sourceTransform.position;
            Quaternion startRotation = sourceTransform.rotation;
            float startFieldOfView = previewCamera.fieldOfView;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                float smoothT = t * t * (3f - 2f * t);
                sourceTransform.position = Vector3.Lerp(startPosition, targetTransform.position, smoothT);
                sourceTransform.rotation = Quaternion.Slerp(startRotation, targetTransform.rotation, smoothT);
                previewCamera.fieldOfView = Mathf.Lerp(startFieldOfView, gameplayCamera.fieldOfView, smoothT);
                yield return null;
            }

            sourceTransform.SetPositionAndRotation(targetTransform.position, targetTransform.rotation);
            previewCamera.fieldOfView = gameplayCamera.fieldOfView;
            gameplayCamera.enabled = true;
            previewCamera.enabled = false;
            isBlending = false;
            onComplete?.Invoke();
        }

        private Vector3 CalculateFocusPoint()
        {
            Transform activeRoot = controller != null ? controller.ActiveCharacterRoot : null;
            if (activeRoot == null)
            {
                return focusOffset;
            }

            Renderer[] renderers = activeRoot.GetComponentsInChildren<Renderer>(false);
            bool hasBounds = false;
            Bounds combinedBounds = default;
            foreach (Renderer targetRenderer in renderers)
            {
                if (!hasBounds)
                {
                    combinedBounds = targetRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(targetRenderer.bounds);
                }
            }

            return (hasBounds ? combinedBounds.center : activeRoot.position) + focusOffset;
        }

        private Vector3 CalculateFramedFocusPoint(Vector3 targetFocusPoint, float distance)
        {
            if (previewCamera == null)
            {
                return targetFocusPoint;
            }

            Vector2 clampedViewportFocus = new(
                Mathf.Clamp01(viewportFocus.x),
                Mathf.Clamp01(viewportFocus.y));
            Quaternion baseRotation = Quaternion.LookRotation(-viewDirection, Vector3.up);
            float verticalExtent = Mathf.Tan(previewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f) * distance;
            float horizontalExtent = verticalExtent * previewCamera.aspect;
            Vector3 horizontalOffset = (baseRotation * Vector3.right) *
                                       ((clampedViewportFocus.x - 0.5f) * 2f * horizontalExtent);
            Vector3 verticalOffset = (baseRotation * Vector3.up) *
                                     ((clampedViewportFocus.y - 0.5f) * 2f * verticalExtent);
            return targetFocusPoint - horizontalOffset - verticalOffset;
        }

        private bool IsPointerOverInterface(Vector2 pointerPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            var pointerData = new PointerEventData(eventSystem) { position = pointerPosition };
            interfaceHits.Clear();
            eventSystem.RaycastAll(pointerData, interfaceHits);

            foreach (RaycastResult hit in interfaceHits)
            {
                if (hit.gameObject.GetComponentInParent<Selectable>() != null ||
                    hit.gameObject.GetComponentInParent<ScrollRect>() != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
