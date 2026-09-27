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

            overlay = new FilledRectangleElement(0, 0, 0, 0,
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
        /// panel at, rather than the preview overshooting past it. The
        /// rectangle itself is <see cref="Managers.DockLayout.PreviewRect(DockSide, float, float, float)"/>
        /// (#391): clamped as the dock will be, and inboard of every dock
        /// already on screen.</summary>
        public static void Show(DockSide side, float size, float instanceBottomInset = 0f, float minAlong = 0f)
        {
            Ensure();
            overlay.Visible = true;

            // Exactly where the dock will land (#391): one rule, shared with
            // LayoutDocked, rather than a second copy of the geometry. The
            // copy that lived here put a bottom dock flush with the window's
            // bottom edge, over the status bar and any existing bottom docks.
            (Vector2 position, Vector2 rectSize) = Managers.DockLayout.PreviewRect(side, size, instanceBottomInset, minAlong);

            overlay.ElementTrait<PositionTrait>().Set(new TVVector(position));
            overlay.ElementTrait<SizeTrait>().Set(new TVVector(rectSize));
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
