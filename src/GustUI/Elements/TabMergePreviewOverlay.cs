using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    /// <summary>
    /// The tab-merge counterpart to <see cref="DockPreviewOverlay"/> —
    /// highlights another <see cref="ModalWindowElement.Tabable"/> window's
    /// title-bar rect while a tabable window is being dragged over it, so
    /// releasing there reads as "these two are about to become tabs" rather
    /// than an ordinary drop. Same shape: one lazily-built instance,
    /// shown/hidden by moving off-canvas, at the same
    /// <see cref="DockPreviewOverlay.Depth"/> tier.
    /// </summary>
    internal static class TabMergePreviewOverlay
    {
        private static FilledRectangleElement overlay;

        private static void Ensure()
        {
            if (overlay != null)
            {
                return;
            }

            overlay = new FilledRectangleElement(0, 0, 0, 0,
                new TVFillSolidColor(() => Resources.StaticResources.Theme.AccentSelection * 0.35f),
                2, Resources.StaticResources.Theme.AccentSelection);
            overlay.Depth = DockPreviewOverlay.Depth;
            Resources.StaticResources.RootWindow.AddChild(overlay, "tab-merge-preview");
        }

        public static void Show(Vector2 position, Vector2 size)
        {
            Ensure();
            overlay.Visible = true;
            overlay.ElementTrait<PositionTrait>().Set(new TVVector(position));
            overlay.ElementTrait<SizeTrait>().Set(new TVVector(size));
        }

        public static void Hide()
        {
            if (overlay == null)
            {
                return;
            }

            // Both belt and braces at once until 2026-09-16: parked offscreen
            // AND zeroed, because neither on its own was trusted to hide it
            // (#232). One flag says it.
            overlay.Visible = false;
        }
    }
}
