using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    public static class CharacterUiRaycastUtility
    {
        public static void ConfigureSafeRaycastTargets(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            ScrollRect[] scrollRects = root.GetComponentsInChildren<ScrollRect>(true);
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = ShouldReceiveRaycasts(graphic, scrollRects);
            }
        }

        public static bool ShouldReceiveRaycasts(Graphic graphic, IReadOnlyList<ScrollRect> scrollRects)
        {
            if (graphic == null)
            {
                return false;
            }

            if (graphic.GetComponent<Mask>() != null || graphic.GetComponent<RectMask2D>() != null)
            {
                return true;
            }

            foreach (ScrollRect scrollRect in scrollRects)
            {
                if (IsScrollSurface(graphic, scrollRect))
                {
                    return true;
                }
            }

            Selectable selectable = graphic.GetComponentInParent<Selectable>(true);
            if (selectable == null)
            {
                return false;
            }

            if (selectable.targetGraphic == graphic)
            {
                return true;
            }

            return selectable switch
            {
                Slider slider => IsSliderGraphic(graphic, slider),
                Scrollbar scrollbar => IsScrollbarGraphic(graphic, scrollbar),
                Toggle toggle => IsToggleGraphic(graphic, toggle),
                TMP_Dropdown dropdown => IsDropdownGraphic(graphic, dropdown),
                TMP_InputField input => IsInputFieldGraphic(graphic, input),
                _ => false
            };
        }

        private static bool IsScrollSurface(Graphic graphic, ScrollRect scrollRect)
        {
            if (scrollRect == null)
            {
                return false;
            }

            if (scrollRect.GetComponent<Graphic>() == graphic)
            {
                return true;
            }

            if (scrollRect.viewport != null &&
                scrollRect.viewport.TryGetComponent(out Graphic viewportGraphic) &&
                viewportGraphic == graphic)
            {
                return true;
            }

            return false;
        }

        private static bool IsSliderGraphic(Graphic graphic, Slider slider)
        {
            return IsRectGraphic(graphic, slider.fillRect) ||
                   IsRectGraphic(graphic, slider.handleRect);
        }

        private static bool IsScrollbarGraphic(Graphic graphic, Scrollbar scrollbar)
        {
            return IsRectGraphic(graphic, scrollbar.handleRect);
        }

        private static bool IsToggleGraphic(Graphic graphic, Toggle toggle)
        {
            return toggle.graphic == graphic;
        }

        private static bool IsDropdownGraphic(Graphic graphic, TMP_Dropdown dropdown)
        {
            if (IsRectGraphic(graphic, dropdown.template))
            {
                return true;
            }

            return dropdown.targetGraphic == null &&
                   graphic is Image &&
                   graphic.transform.parent == dropdown.transform;
        }

        private static bool IsInputFieldGraphic(Graphic graphic, TMP_InputField input)
        {
            return IsRectGraphic(graphic, input.textViewport);
        }

        private static bool IsRectGraphic(Graphic graphic, RectTransform rectTransform)
        {
            return rectTransform != null &&
                   rectTransform.TryGetComponent(out Graphic rectGraphic) &&
                   rectGraphic == graphic;
        }
    }
}
