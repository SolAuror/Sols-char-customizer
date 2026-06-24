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
        private float zoomOffset;
        private float panOffset;
        private bool initialized;
        private readonly List<RaycastResult> interfaceHits = new();

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
            initialized = true;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (!initialized || mouse == null || IsPointerOverInterface(mouse.position.ReadValue()))
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
            ApplyCameraPosition();
        }

        public void PanVertical(float amount)
        {
            panOffset = Mathf.Clamp(panOffset + amount, -maximumPanOffset, maximumPanOffset);
            ApplyCameraPosition();
        }

        public void ResetCamera()
        {
            zoomOffset = 0f;
            panOffset = 0f;
            ApplyCameraPosition();
        }

        private void ApplyCameraPosition()
        {
            Transform cameraTransform = previewCamera.transform;
            cameraTransform.position = initialCameraPosition
                + cameraTransform.forward * zoomOffset
                + cameraTransform.up * panOffset;
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
