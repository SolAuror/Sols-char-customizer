using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    // The tattoo menu. Choose a design, then click the body to place it; keep holding to
    // drag it into place. Click a tattoo on the body, or its tile under Your Tattoos, to
    // select it: drag to move it, Shift+scroll to resize, Ctrl+scroll to turn, and the
    // size, rotation, and opacity sliders, ink swatches, Mirror, and Remove edit it.
    // Escape cancels a design that has not been placed yet. The selected tattoo pulses
    // outlined on the body and its tile is marked. Clicks and scrolls on the
    // character are claimed from the preview controls, so the character does not turn or
    // the camera zoom while a tattoo is placed or edited.
    [DefaultExecutionOrder(-50)]
    public sealed class CharacterTattooPicker : MonoBehaviour
    {
        // Per scroll notch.
        private const float ScrollResize = 1.08f;
        private const float ScrollTurnDegrees = 5f;
        // A drag smaller than this, in metres on the skin, does not rebuild the layer.
        private const float MinimumMove = 0.001f;
        private static readonly Vector3 SelectedScale = Vector3.one * 1.08f;
        // The menu's accent, thick round the selected tile, the chosen design, and the ink.
        private static readonly Color MarkedOutline = new(0.82f, 0.73f, 0.55f, 1f);
        private static readonly Vector2 MarkedOutlineDistance = new(3f, -3f);

        [SerializeField] private CharacterTattoos tattoos;
        [Tooltip("For its camera, and so placing tattoos does not also turn the character. Empty finds one in the scene.")]
        [SerializeField] private CharacterPreviewControls preview;

        [Header("Designs")]
        [SerializeField] private RectTransform designContent;
        [Tooltip("Inactive template cloned per design: a Button with a 'Preview' RawImage and a 'Name' label as children.")]
        [SerializeField] private Button designTileTemplate;

        [Header("Your Tattoos")]
        [SerializeField] private RectTransform placedContent;
        [Tooltip("Inactive template cloned per placed tattoo: a Button with a 'Preview' RawImage child.")]
        [SerializeField] private Button placedTileTemplate;

        [Header("Ink")]
        [SerializeField] private RectTransform inkContent;
        [Tooltip("Inactive template cloned once per ink colour.")]
        [SerializeField] private Button inkSwatchTemplate;
        [Tooltip("Ink colours (sRGB).")]
        [SerializeField] private List<Color> inkColors = new()
        {
            CharacterTattooPlacement.DefaultInk,
            new Color(0.03f, 0.03f, 0.03f, 1f),
            new Color(0.16f, 0.17f, 0.2f, 1f),
            new Color(0.07f, 0.1f, 0.25f, 1f),
            new Color(0.06f, 0.2f, 0.12f, 1f),
            new Color(0.45f, 0.06f, 0.06f, 1f),
            new Color(0.3f, 0.16f, 0.08f, 1f),
            new Color(0.25f, 0.09f, 0.3f, 1f),
            new Color(0.05f, 0.27f, 0.3f, 1f),
            new Color(0.55f, 0.42f, 0.12f, 1f),
            new Color(0.92f, 0.92f, 0.9f, 1f),
        };

        [Header("Selected Tattoo")]
        [SerializeField] private Slider sizeSlider;
        [SerializeField] private Slider rotationSlider;
        [SerializeField] private Slider opacitySlider;
        [SerializeField] private Button mirrorButton;
        [SerializeField] private Button removeButton;
        [SerializeField] private TMP_Text hint;

        private readonly List<Button> designTiles = new();
        private readonly List<string> designIds = new();
        private readonly List<Button> placedTiles = new();
        private readonly List<Button> inkSwatches = new();
        private readonly List<string> placedSignature = new();
        private CharacterTattooSurface surface;
        private string armedDesign;
        private int selected = -1;
        private bool dragging;
        private bool listening;
        private Color newInk = CharacterTattooPlacement.DefaultInk;
        private float newOpacity = CharacterTattooPlacement.DefaultOpacity;

        public CharacterTattoos Tattoos => tattoos;
        public int SelectedIndex => selected;
        public string ArmedDesign => armedDesign;

        private void OnEnable()
        {
            if (preview == null)
            {
                preview = FindFirstObjectByType<CharacterPreviewControls>();
            }

            if (tattoos != null)
            {
                Listen(true);
                Rebuild();
            }
        }

        private void OnDisable()
        {
            Listen(false);
            StopDragging();
            armedDesign = null;
            if (tattoos != null)
            {
                tattoos.Highlighted = -1;
            }
        }

        private void OnDestroy()
        {
            surface?.Dispose();
        }

        public void Bind(CharacterTattoos target)
        {
            Listen(false);
            if (tattoos != null)
            {
                tattoos.Highlighted = -1;
            }

            tattoos = target;
            selected = -1;
            armedDesign = null;
            if (isActiveAndEnabled && tattoos != null)
            {
                Listen(true);
                Rebuild();
            }
        }

        // Removes every tattoo.
        public void ResetToDefault()
        {
            armedDesign = null;
            selected = -1;
            tattoos?.ClearPlacements();
            Refresh();
        }

        // Places the next click's tattoo with this design.
        public void ArmDesign(string designId)
        {
            armedDesign = designId;
            Refresh();
        }

        public void Select(int index)
        {
            selected = tattoos != null && index >= 0 && index < tattoos.Placements.Count ? index : -1;
            armedDesign = null;
            Refresh();
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (tattoos == null || mouse == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && armedDesign != null)
            {
                armedDesign = null;
                Refresh();
            }

            Vector2 pointer = mouse.position.ReadValue();
            bool overInterface = preview != null && preview.IsPointerOverInterface(pointer);
            if (mouse.leftButton.wasPressedThisFrame && !overInterface && TryHitSkin(pointer, out Vector3 point, out Vector3 normal))
            {
                Claim();
                Press(point, normal);
            }
            else if (dragging)
            {
                if (mouse.leftButton.isPressed)
                {
                    Claim();
                    if (TryHitSkin(pointer, out point, out normal))
                    {
                        Drag(point, normal);
                    }
                }
                else
                {
                    StopDragging();
                }
            }

            float scroll = mouse.scroll.ReadValue().y;
            bool resize = keyboard != null && keyboard.shiftKey.isPressed;
            bool turn = keyboard != null && keyboard.ctrlKey.isPressed;
            if (!Mathf.Approximately(scroll, 0f) && !overInterface && (resize || turn) && HasSelection)
            {
                Claim();
                Describe(out float angle, out Vector2 size, out bool mirrored);
                if (resize)
                {
                    size *= scroll > 0f ? ScrollResize : 1f / ScrollResize;
                }
                else
                {
                    angle += Mathf.Sign(scroll) * ScrollTurnDegrees;
                }

                Reshape(angle, size, mirrored);
            }
        }

        private bool HasSelection => tattoos != null && selected >= 0 && selected < tattoos.Placements.Count;

        private Vector3 MeshUp
        {
            get
            {
                SkinnedMeshRenderer skin = tattoos.Skin;
                return skin != null ? skin.transform.InverseTransformDirection(Vector3.up) : Vector3.up;
            }
        }

        private void Claim()
        {
            if (preview != null)
            {
                preview.ClaimPointerThisFrame();
            }
        }

        private bool TryHitSkin(Vector2 pointer, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            Camera view = preview != null && preview.PreviewCamera != null ? preview.PreviewCamera : Camera.main;
            SkinnedMeshRenderer skin = tattoos.Skin;
            if (view == null || skin == null)
            {
                return false;
            }

            surface ??= new CharacterTattooSurface();
            return surface.TryHit(skin, tattoos.PlacementMesh, view.ScreenPointToRay(pointer), out point, out normal);
        }

        private void Press(Vector3 point, Vector3 normal)
        {
            if (armedDesign != null)
            {
                Vector2 size = new(0.11f, 0.11f);
                if (tattoos.Catalog != null && tattoos.Catalog.TryGet(armedDesign, out CharacterTattooDesign design))
                {
                    size = design.DefaultSize;
                }

                // Wraps around a limb; the torso projects flat.
                tattoos.TryGetCurve(point, out Vector3 axis, out float radius);
                selected = tattoos.AddPlacement(CharacterTattooPlacer.Place(
                    armedDesign, point, normal, MeshUp, 0f, size, false, newInk, newOpacity, axis, radius));
                armedDesign = null;
            }
            else
            {
                selected = FindTattooAt(point);
            }

            dragging = HasSelection;
            tattoos.Previewing = dragging;
            Refresh();
        }

        private void Drag(Vector3 point, Vector3 normal)
        {
            if (!HasSelection)
            {
                return;
            }

            CharacterTattooPlacement placement = tattoos.Placements[selected];
            CharacterTattooPlacer.Describe(placement, MeshUp, out Vector3 current, out _, out _, out _, out _);
            if ((current - point).sqrMagnitude >= MinimumMove * MinimumMove)
            {
                tattoos.TryGetCurve(point, out Vector3 axis, out float radius);
                tattoos.SetPlacement(selected, CharacterTattooPlacer.Move(placement, point, normal, MeshUp, axis, radius));
            }
        }

        private void StopDragging()
        {
            dragging = false;
            if (tattoos != null)
            {
                tattoos.Previewing = false;
            }
        }

        // The topmost tattoo whose design covers this point of skin.
        private int FindTattooAt(Vector3 point)
        {
            IReadOnlyList<CharacterTattooPlacement> placements = tattoos.Placements;
            for (int index = placements.Count - 1; index >= 0; index--)
            {
                CharacterTattooPlacer.Describe(placements[index], MeshUp, out Vector3 centre, out _, out _, out Vector2 size, out _);
                if ((centre - point).magnitude <= Mathf.Max(size.x, size.y) * 0.5f)
                {
                    return index;
                }
            }

            return -1;
        }

        private void Describe(out float angle, out Vector2 size, out bool mirrored)
        {
            CharacterTattooPlacer.Describe(tattoos.Placements[selected], MeshUp, out _, out _, out angle, out size, out mirrored);
        }

        private void Reshape(float angle, Vector2 size, bool mirrored)
        {
            if (HasSelection)
            {
                tattoos.SetPlacement(selected, CharacterTattooPlacer.Reshape(tattoos.Placements[selected], MeshUp, angle, size, mirrored));
            }
        }

        private void Listen(bool listen)
        {
            if (listen == listening || tattoos == null)
            {
                return;
            }

            listening = listen;
            if (listen)
            {
                tattoos.Changed += Refresh;
                sizeSlider?.onValueChanged.AddListener(OnSizeChanged);
                rotationSlider?.onValueChanged.AddListener(OnRotationChanged);
                opacitySlider?.onValueChanged.AddListener(OnOpacityChanged);
                mirrorButton?.onClick.AddListener(OnMirror);
                removeButton?.onClick.AddListener(OnRemove);
            }
            else
            {
                tattoos.Changed -= Refresh;
                sizeSlider?.onValueChanged.RemoveListener(OnSizeChanged);
                rotationSlider?.onValueChanged.RemoveListener(OnRotationChanged);
                opacitySlider?.onValueChanged.RemoveListener(OnOpacityChanged);
                mirrorButton?.onClick.RemoveListener(OnMirror);
                removeButton?.onClick.RemoveListener(OnRemove);
            }
        }

        private void Rebuild()
        {
            if (sizeSlider != null)
            {
                sizeSlider.minValue = CharacterTattooPlacer.MinimumSize;
                sizeSlider.maxValue = 0.4f;
            }

            if (rotationSlider != null)
            {
                rotationSlider.minValue = -180f;
                rotationSlider.maxValue = 180f;
            }

            if (opacitySlider != null)
            {
                opacitySlider.minValue = 0f;
                opacitySlider.maxValue = 1f;
            }

            RebuildDesignTiles();
            RebuildInkSwatches();
            placedSignature.Clear();
            Refresh();
        }

        private void RebuildDesignTiles()
        {
            Clear(designTiles);
            designIds.Clear();
            if (designContent == null || designTileTemplate == null || tattoos.Catalog == null)
            {
                return;
            }

            foreach (CharacterTattooDesign design in tattoos.Catalog.Designs)
            {
                if (design == null || design.Texture == null)
                {
                    continue;
                }

                Button tile = CreateTile(designTileTemplate, designContent, $"Tattoo Design {design.Label}", design.Texture);
                SetLabel(tile, design.Label);
                string id = design.Id;
                tile.onClick.AddListener(() => ArmDesign(id));
                designTiles.Add(tile);
                designIds.Add(id);
            }
        }

        private void RebuildInkSwatches()
        {
            Clear(inkSwatches);
            if (inkContent == null || inkSwatchTemplate == null)
            {
                return;
            }

            foreach (Color color in inkColors)
            {
                Button swatch = Instantiate(inkSwatchTemplate, inkContent);
                swatch.name = "Tattoo Ink Swatch";
                swatch.gameObject.SetActive(true);
                if (swatch.targetGraphic != null)
                {
                    swatch.targetGraphic.color = color;
                }

                Color ink = color;
                swatch.onClick.AddListener(() => OnInkChosen(ink));
                inkSwatches.Add(swatch);
            }
        }

        // One tile per placed tattoo, rebuilt only when the tattoos or their designs change.
        private void RebuildPlacedTiles()
        {
            var signature = new List<string>();
            foreach (CharacterTattooPlacement placement in tattoos.Placements)
            {
                signature.Add(placement.DesignId);
            }

            if (signature.Count == placedSignature.Count && SameOrder(signature))
            {
                return;
            }

            placedSignature.Clear();
            placedSignature.AddRange(signature);
            Clear(placedTiles);
            if (placedContent == null || placedTileTemplate == null)
            {
                return;
            }

            for (int index = 0; index < signature.Count; index++)
            {
                Texture texture = tattoos.Catalog != null && tattoos.Catalog.TryGet(signature[index], out CharacterTattooDesign design)
                    ? design.Texture
                    : null;
                Button tile = CreateTile(placedTileTemplate, placedContent, $"Placed Tattoo {index + 1}", texture);
                int chosen = index;
                tile.onClick.AddListener(() => Select(chosen));
                placedTiles.Add(tile);
            }
        }

        private bool SameOrder(List<string> signature)
        {
            for (int index = 0; index < signature.Count; index++)
            {
                if (!string.Equals(signature[index], placedSignature[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        // Shows the tattoos and the selected one's settings without feeding changes back.
        private void Refresh()
        {
            if (tattoos == null)
            {
                return;
            }

            if (!HasSelection)
            {
                selected = -1;
            }

            tattoos.Highlighted = selected;
            RebuildPlacedTiles();
            for (int index = 0; index < placedTiles.Count; index++)
            {
                Mark(placedTiles[index], placedTileTemplate, index == selected);
            }

            for (int index = 0; index < designTiles.Count; index++)
            {
                Mark(designTiles[index], designTileTemplate, designIds[index] == armedDesign);
            }

            bool editing = HasSelection;
            Color ink = editing ? tattoos.Placements[selected].Ink : newInk;
            for (int index = 0; index < inkSwatches.Count && index < inkColors.Count; index++)
            {
                Mark(inkSwatches[index], inkSwatchTemplate, SameColour(inkColors[index], ink));
            }

            SetInteractable(sizeSlider, editing);
            SetInteractable(rotationSlider, editing);
            SetInteractable(mirrorButton, editing);
            SetInteractable(removeButton, editing);
            if (editing)
            {
                CharacterTattooPlacement placement = tattoos.Placements[selected];
                Describe(out float angle, out Vector2 size, out _);
                sizeSlider?.SetValueWithoutNotify(Mathf.Max(size.x, size.y));
                rotationSlider?.SetValueWithoutNotify(Mathf.Repeat(angle + 180f, 360f) - 180f);
                opacitySlider?.SetValueWithoutNotify(placement.Opacity);
            }
            else
            {
                opacitySlider?.SetValueWithoutNotify(newOpacity);
            }

            if (hint != null)
            {
                hint.text = armedDesign != null
                    ? $"Click the body to place {DesignLabel(armedDesign)}. Esc cancels."
                    : editing
                        ? "Drag it on the body to move it. Shift+scroll resizes, Ctrl+scroll turns it."
                        : "Choose a design, then click the body to place it. Click a tattoo to select it.";
            }
        }

        private string DesignLabel(string designId)
        {
            return tattoos.Catalog != null && tattoos.Catalog.TryGet(designId, out CharacterTattooDesign design)
                ? design.Label
                : designId;
        }

        private void OnSizeChanged(float value)
        {
            if (!HasSelection)
            {
                return;
            }

            Describe(out float angle, out Vector2 size, out bool mirrored);
            float largest = Mathf.Max(size.x, size.y, 1e-4f);
            Reshape(angle, size * (value / largest), mirrored);
        }

        private void OnRotationChanged(float value)
        {
            if (HasSelection)
            {
                Describe(out _, out Vector2 size, out bool mirrored);
                Reshape(value, size, mirrored);
            }
        }

        // Without a selection, sets the opacity of tattoos placed next.
        private void OnOpacityChanged(float value)
        {
            newOpacity = value;
            if (HasSelection)
            {
                CharacterTattooPlacement placement = tattoos.Placements[selected];
                tattoos.SetPlacement(selected, placement.WithInk(placement.Ink, value));
            }
        }

        // Without a selection, sets the ink of tattoos placed next.
        private void OnInkChosen(Color color)
        {
            newInk = color;
            if (HasSelection)
            {
                CharacterTattooPlacement placement = tattoos.Placements[selected];
                tattoos.SetPlacement(selected, placement.WithInk(color, placement.Opacity));
            }
        }

        private void OnMirror()
        {
            if (HasSelection)
            {
                Describe(out float angle, out Vector2 size, out bool mirrored);
                Reshape(angle, size, !mirrored);
            }
        }

        private void OnRemove()
        {
            if (HasSelection)
            {
                int index = selected;
                selected = -1;
                tattoos.RemovePlacement(index);
            }
        }

        private static Button CreateTile(Button template, RectTransform content, string name, Texture texture)
        {
            Button tile = Instantiate(template, content);
            tile.name = name;
            tile.gameObject.SetActive(true);
            Transform previewChild = tile.transform.Find("Preview");
            if (previewChild != null && previewChild.TryGetComponent(out RawImage image))
            {
                image.texture = texture;
                image.enabled = texture != null;
            }

            return tile;
        }

        // A marked tile is enlarged and outlined in the accent colour; others keep the
        // template's outline.
        private static void Mark(Button tile, Button template, bool marked)
        {
            tile.transform.localScale = marked ? SelectedScale : Vector3.one;
            if (tile.TryGetComponent(out Outline outline) && template != null && template.TryGetComponent(out Outline resting))
            {
                outline.effectColor = marked ? MarkedOutline : resting.effectColor;
                outline.effectDistance = marked ? MarkedOutlineDistance : resting.effectDistance;
            }
        }

        private static bool SameColour(Color first, Color second)
        {
            return Mathf.Abs(first.r - second.r) + Mathf.Abs(first.g - second.g) + Mathf.Abs(first.b - second.b) < 0.01f;
        }

        private static void SetLabel(Button tile, string label)
        {
            Transform nameChild = tile.transform.Find("Name");
            if (nameChild != null && nameChild.TryGetComponent(out TMP_Text text))
            {
                text.text = label;
            }
        }

        private static void SetInteractable(Selectable selectable, bool interactable)
        {
            if (selectable != null)
            {
                selectable.interactable = interactable;
            }
        }

        private static void Clear(List<Button> buttons)
        {
            foreach (Button button in buttons)
            {
                if (button != null)
                {
                    Destroy(button.gameObject);
                }
            }

            buttons.Clear();
        }
    }
}
