using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using System;

namespace GustUI.Elements
{
    /// <summary>
    /// A small floating panel hosting ARBITRARY content — unlike
    /// <see cref="FruitPopupMenu"/> (which renders a declarative
    /// List&lt;MenuItemModel&gt; as FruitMenuItems), a caller just adds
    /// whatever children it wants via the inherited AddChild (a slider, a
    /// label, a mini form). Same dismiss-on-outside-click and
    /// self-clamp-to-window behavior as FruitPopupMenu, generalized: a
    /// popover isn't chained/nested the way menus are, so it only needs to
    /// know about itself and its own trigger, not "any menu UI" app-wide.
    /// Position it after construction (e.g. below/near its trigger's
    /// GetActualPosition(), the same idiom MenuBarElement.OpenDropdown
    /// already uses for FruitPopupMenu).
    /// </summary>
    public class PopoverElement : FilledRectangleElement
    {
        /// <summary>Same tier as FruitPopupMenu's own PopupDepth — above
        /// content and side panels, below tooltips.</summary>
        public const int PopoverDepth = FruitPopupMenu.PopupDepth;

        /// <summary>Fires when this popover is Kill()'d, whichever way that
        /// happens — an explicit caller-driven close, or its own outside-
        /// click dismissal below. A caller holding a reference to reposition/
        /// toggle this popover needs this to know when that reference has
        /// gone stale, since the outside-click path doesn't otherwise tell
        /// anyone.</summary>
        public Action OnClosed;

        private readonly Element trigger;
        private bool eligibleToAutoClose;

        public PopoverElement(TVVector size, Element trigger = null)
        {
            this.trigger = trigger;
            Depth = PopoverDepth;
            Set<SizeTrait>(size);
            Set<PositionTrait>(new TVVector(0, 0));
            // Same translucent chrome family FruitPopupMenu uses.
            Set<BackgroundFillTrait>(new TVFillSimpleGradient(
                Resources.StaticResources.Theme.MenuBarFillTop,
                Resources.StaticResources.Theme.MenuBarFillBottom,
                Direction.Vertically));
            Set<BorderFillTrait>(new TVBorder9Grid());
        }

        public override void Kill()
        {
            OnClosed?.Invoke();
            OnClosed = null;
            base.Kill();
        }

        public override void Update(Element parent = null)
        {
            base.Update(parent);

            // Self-clamp to the window — same reasoning as FruitPopupMenu's
            // own identical clamp: a popover anchored near a window edge
            // shouldn't run off it.
            TVVector pos = ElementTrait<PositionTrait>().Value();
            TVVector size = ElementTrait<SizeTrait>().Value();
            TVVector windowSize = Resources.StaticResources.RootWindow.GetSize();
            float clampedX = Math.Max(0, Math.Min(pos.X, windowSize.X - size.X));
            float clampedY = Math.Max(0, Math.Min(pos.Y, windowSize.Y - size.Y));
            if (clampedX != pos.X || clampedY != pos.Y)
            {
                Set<PositionTrait>(new TVVector(clampedX, clampedY));
            }

            // A press anywhere outside this popover AND its trigger (so the
            // same click that opened it doesn't also immediately close it)
            // dismisses it — mirrors FruitPopupMenu's own eligibleToAutoClose
            // guard (needed for the same reason: InputManager's sub-frame
            // edge-replay can land a press+release in ONE Update() pass, so
            // without this a popover can see "a press happened" on the very
            // frame it's constructed and Kill() itself before ever becoming
            // visible).
            if (eligibleToAutoClose && Resources.StaticResources.InputManager.LeftJustPressed
                && !this.IsMouseOver() && !(trigger != null && trigger.IsMouseOver()))
            {
                Kill();
                return;
            }

            eligibleToAutoClose = true;
        }
    }
}
