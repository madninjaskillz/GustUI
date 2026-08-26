using GustUI.Extensions;
using GustUI.Models;
using GustUI.Traits;
using GustUI.TraitValues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Elements
{
    [Attributes.ElementTraits(typeof(OnScrollWheelChanged))]
    public class FruitPopupMenu : FilledRectangleElement
    {
        /// <summary>
        /// Popups draw above ordinary panels by default (panels near the root
        /// accumulate depth as they gain children — the AddChild root-sibling
        /// bump — so a depth-0 popup added later can still end up BEHIND a
        /// busy panel; the tooltip/status-bar convention of an explicit high
        /// depth fixes the class once for every consumer). Below
        /// TooltipElement's 1000000 so hints still win.
        /// </summary>
        public const int PopupDepth = 500000;

        private List<MenuItemModel> menuItems;
        public bool WasAutoPopped { get; set; }

        /// <summary>Every item with the Y it would sit at if the popup were
        /// tall enough to show them all. See <see cref="ApplyScroll"/>.</summary>
        private readonly List<(FruitMenuItem Item, float NaturalY)> itemRows = new();

        /// <summary>Total height of the items, whatever the popup ends up
        /// being clamped to.</summary>
        private float naturalHeight;

        /// <summary>Pixels scrolled, 0 when everything fits.</summary>
        private float scroll;

        /// <summary>Pixels per wheel notch. Roughly two menu rows, which is
        /// the scale a menu wants — a wheel notch that moves half an item is
        /// worse than not scrolling at all.</summary>
        private const float WheelStep = 64f;

        /// <summary>The element that opened this popup (e.g. a plain
        /// BasicButtonElement dropdown trigger, unlike the fruit-menu's own
        /// items). Optional — null for popups opened from within the fruit
        /// menu itself, which AnyMenuUiHovered already covers. Hovering it
        /// counts the same as hovering menu UI (see AnyMenuUiHovered) so the
        /// popup doesn't auto-close while the mouse is still sitting over
        /// the button that just opened it.</summary>
        private readonly Element trigger;

        public FruitPopupMenu(List<MenuItemModel> items, int width, bool autoPopped = false, Element trigger = null)
        {
            WasAutoPopped = autoPopped;
            this.trigger = trigger;
            Depth = PopupDepth;
            menuItems = items;
            Set<SizeTrait>(new TVVector(width, FruitMenuItem.RowHeight * items.Count));
            Set<PositionTrait>(new TVVector(0, 0));
            // Same translucent chrome family as the fruit menu bar it extends
            // from (design-guide.md §1.5) — was a flat, harsher near-white
            // independent of that bar's own (already-retinted) look.
            Set<BackgroundFillTrait>(new TVFillSimpleGradient(
                Resources.StaticResources.Theme.MenuBarFillTop,
                Resources.StaticResources.Theme.MenuBarFillBottom,
                Direction.Vertically));

            float ps = 0;
            foreach (MenuItemModel item in menuItems)
            {
                FruitMenuItem i = new FruitMenuItem(item,width: width);

                this.AddChild(i, "fruit item");

                if (item.Text == "")
                {
                    ps = ps + 5;
                }

                i.Set<PositionTrait>(new TVVector(0, ps));

                // Remembered so Update() can re-place every item at
                // (natural - scroll) without having to re-derive the layout.
                itemRows.Add((i, ps));

                ps = ps + Math.Max(i.ElementTrait<SizeTrait>().Value().Y,7);

            }

            naturalHeight = ps;
            Set<SizeTrait>(new TVVector(width, ps));

            Set<OnScrollWheelChanged>(new TVEvent<ScrollEventArgs>(HandleWheel));
        }

        // Guards against the popup closing on the SAME click that opened it.
        // A trigger button that isn't itself a FruitMenuElement/FruitPopupMenu
        // (e.g. a plain BasicButtonElement dropdown, unlike the fruit-menu's
        // own items) gets no help from AnyMenuUiHovered() below, and
        // InputManager's sub-frame edge-replay (a fast press+release can both
        // land in ONE Update() pass — see InputManager.ProcessMouseState) can
        // still have LeftJustPressed=true on the very frame this popup is
        // constructed, in reaction to that same click's release. Without this
        // guard the popup would immediately see "a press happened, nothing
        // menu-ish is hovered" and Kill() itself before ever being visible —
        // reproduced as "the dropdown popup flickers or doesn't appear,
        // seemingly at random" depending on exact press/release frame timing.
        private bool eligibleToAutoClose;

        /// <summary>
        /// A press anywhere outside the menu system closes the popup. This
        /// used to be the (fullscreen, dimming) BackdropElement's job; screens
        /// that clear the stage don't keep a backdrop, so the popup now owns
        /// its dismissal.
        /// </summary>
        public override void Update(Element parent = null)
        {
            base.Update(parent);

            // Self-clamp to the window (mirrors ModalWindowElement's own
            // screen-clamp) — a popup taller/wider than the space below/
            // right of its anchor used to run off-window with its lower
            // items unreachable. This was invisible at DPI scale 1
            // (logical pixels == physical, so most windows were "tall
            // enough" purely by coincidence) but became reachable the
            // moment SyncDevicePixelRatio's Automatic mode started
            // dividing by the REAL system scale (UserPreferences.
            // SystemDisplayScale) instead of always 1 — the same window
            // has proportionally less LOGICAL room at 150%+ scale, and
            // e.g. File's 13-item dropdown (found 2026-08-12: its last
            // item, Preferences, sat past the bottom of a modest window
            // and was simply unclickable).
            TVVector pos = ElementTrait<PositionTrait>().Value();
            TVVector size = ElementTrait<SizeTrait>().Value();
            TVVector windowSize = Resources.StaticResources.RootWindow.GetSize();

            float topLimit = 0;
            if (Resources.StaticResources.RootWindow.Children.Items.Any(x => x is FruitMenuElement))
            {
                topLimit = Resources.StaticResources.RootWindow.Children.Items.First(x => x is FruitMenuElement).GetSize().Y;
            }

            // Clamping alone cannot save a menu TALLER than the space it has
            // to live in — Min/Max just pins it to the top and the surplus
            // still hangs off the bottom, unreachable. So when the items do
            // not fit, the popup is capped to the available height, clips its
            // children, and scrolls (2026-08-23; File's dropdown crossed this
            // line at a modest window size and took Preferences with it).
            float available = Math.Max(80, windowSize.Y - topLimit - 8);
            bool scrolls = naturalHeight > available;
            float shownHeight = scrolls ? available : naturalHeight;

            if (Math.Abs(size.Y - shownHeight) > 0.5f)
            {
                Set<SizeTrait>(new TVVector(size.X, shownHeight));
                size = ElementTrait<SizeTrait>().Value();
            }

            ClipChildren = scrolls;
            ApplyScroll(scrolls ? Math.Min(scroll, naturalHeight - available) : 0f);

            float clampedX = Math.Max(0, Math.Min(pos.X, windowSize.X - size.X));
            float clampedY = Math.Max(topLimit, Math.Min(pos.Y, windowSize.Y - size.Y));
            if (clampedX != pos.X || clampedY != pos.Y)
            {
                Set<PositionTrait>(new TVVector(clampedX, clampedY));
            }

            if (eligibleToAutoClose && Resources.StaticResources.InputManager.LeftJustPressed && !AnyMenuUiHovered() && !(trigger != null && trigger.IsMouseOver()))
            {
                Kill();
                return;
            }

            eligibleToAutoClose = true;
        }

        private void HandleWheel(ScrollEventArgs args)
        {
            // ScrollWheelDelta = previous - current, so wheel-up is negative —
            // same convention VerticalScrollElement uses.
            scroll += args.ScrollWheelDelta / 120f * WheelStep;
        }

        /// <summary>Re-places every item at its natural Y less the scroll
        /// offset, clamping the offset to the real range first so a wheel
        /// flick can't push the menu off its own top or bottom.</summary>
        private void ApplyScroll(float requested)
        {
            float max = Math.Max(0, naturalHeight - ElementTrait<SizeTrait>().Value().Y);
            float applied = Math.Max(0, Math.Min(requested, max));

            if (Math.Abs(applied - scroll) > 0.01f)
            {
                scroll = applied;
            }

            foreach ((FruitMenuItem item, float naturalY) in itemRows)
            {
                TVVector at = item.ElementTrait<PositionTrait>().Value();
                float wanted = naturalY - applied;
                if (Math.Abs(at.Y - wanted) > 0.01f)
                {
                    item.Set<PositionTrait>(new TVVector(at.X, wanted));
                }
            }
        }

        private static bool AnyMenuUiHovered()
        {
            foreach (Element child in Resources.StaticResources.RootWindow.Children.Items)
            {
                if ((child is FruitPopupMenu || child is FruitMenuElement) && child.IsMouseOver())
                {
                    return true;
                }
            }

            return false;
        }
    }
}
