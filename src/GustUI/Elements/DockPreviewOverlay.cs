using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    /// <summary>
    /// The shared translucent screen-edge preview shown while a
    /// <see cref="ModalWindowElement"/> is held over a dock zone
    /// (2026-08-16 docked-modal feature, see <see cref="ModalWindowElement.DockTo"/>).
    /// One instance for the whole app — only one drag can be in progress at
    /// a time — lazily built on first use and reparented to
    /// <see cref="Resources.StaticResources"/>'s root window. Modeled on
    /// SequencerView's own drag-drop ghost/badge pair: a plain
    /// <see cref="FilledRectangleElement"/> at a high explicit
    /// <see cref="Depth"/>, shown/hidden by moving off-canvas rather than
    /// toggling a visibility trait.
    /// </summary>
    internal static class DockPreviewOverlay
    {
        /// <summary>New documented depth tier: above popups (500,000, so a
        /// dock preview stays visible over any popup left open elsewhere),
        /// below tooltips (1,000,000, the one thing allowed to cover it).</summary>
        public const int Depth = 700000;

        private static FilledRectangleElement overlay;

        private static void Ensure()
        {
            if (overlay != null)
            {
                return;
            }

            overlay = new FilledRectangleElement(-10000, 0, 0, 0,
                new TVFillSolidColor(() => Resources.StaticResources.Theme.AccentSelection * 0.25f),
                2, Resources.StaticResources.Theme.AccentSelection);
            overlay.Depth = Depth;
            Resources.StaticResources.RootWindow.AddChild(overlay, "dock-preview");
        }

        /// <summary><paramref name="size"/> is the panel's own width for a
        /// Left/Right preview, or its own height for a Top/Bottom preview
        /// (2026-08-17, Top/Bottom added) — same axis <see cref="Managers.DockLayout"/>
        /// reserves along for that side. <paramref name="instanceBottomInset"/>
        /// is the dragged modal's own <see cref="ModalWindowElement.BottomInset"/>
        /// (app-level chrome, e.g. a status bar) — matches the geometry
        /// <see cref="ModalWindowElement.LayoutDocked"/> actually lands the
        /// panel at, rather than the preview overshooting past it.</summary>
        public static void Show(DockSide side, float size, float instanceBottomInset = 0f)
        {
            Ensure();

            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            float top = Managers.DockLayout.TopInset;
            float bottom = Managers.DockLayout.BottomInset + instanceBottomInset;
            float left = Managers.DockLayout.LeftInset;
            float right = Managers.DockLayout.RightInset;

            TVVector position;
            TVVector rectSize;
            switch (side)
            {
                case DockSide.Left:
                    position = new TVVector(0, top);
                    rectSize = new TVVector(size, windowSize.Y - top - bottom);
                    break;
                case DockSide.Right:
                    position = new TVVector(windowSize.X - size, top);
                    rectSize = new TVVector(size, windowSize.Y - top - bottom);
                    break;
                case DockSide.Top:
                    position = new TVVector(left, top);
                    rectSize = new TVVector(windowSize.X - left - right, size);
                    break;
                default: // Bottom
                    position = new TVVector(left, windowSize.Y - size);
                    rectSize = new TVVector(windowSize.X - left - right, size);
                    break;
            }

            overlay.ElementTrait<PositionTrait>().Set(position);
            overlay.ElementTrait<SizeTrait>().Set(rectSize);
        }

        public static void Hide()
        {
            if (overlay == null)
            {
                return;
            }

            overlay.ElementTrait<PositionTrait>().Set(new TVVector(-10000, 0));
            overlay.ElementTrait<SizeTrait>().Set(new TVVector(0, 0));
        }
    }
}
