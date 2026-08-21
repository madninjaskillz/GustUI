using GustUI.Extensions;
using GustUI.Models;
using GustUI.Traits;
using GustUI.TraitValues;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GustUI.Elements
{
    /// <summary>
    /// A per-modal menu strip — the per-view replacement for the old global
    /// <see cref="FruitMenuElement"/> (2026-08-17 rework: no global menu bar
    /// at all; each modal that implements a menu contract shows its own
    /// strip directly under its own title bar, and content below is pushed
    /// down accordingly — see FullScreenModalElement/ModalWindowElement's
    /// own SetMenu/ContentTop). Plain flat fill, no app icon/gradient/
    /// full-window-tracking chrome — those are FruitMenuElement specifics
    /// this bar doesn't need, since it's owned and positioned by its host
    /// modal rather than living at the window root. Height targets a
    /// native-Windows classic menu-bar size (GetSystemMetrics(SM_CYMENU)
    /// ~20px @ 96 DPI/100% scale) rather than the 40px title-bar height —
    /// tuned via a live screenshot against that reasoning, not guessed
    /// blind; nudge <see cref="BarHeight"/> if it doesn't read as
    /// native-sized once built. Dropdowns reuse <see cref="FruitPopupMenu"/>
    /// unchanged (same PopupDepth tier, same FruitMenuItem-rendered
    /// entries) — only the top-level strip itself is bespoke (see
    /// <see cref="MenuBarItem"/>'s own doc comment for why).
    /// </summary>
    public class MenuBarElement : FilledRectangleElement
    {
        /// <summary>Bar height in pixels — content below the bar starts here.</summary>
        public const int BarHeight = 28;

        /// <summary>Left/right padding around each item's label — no icon
        /// slot reserved (top-level entries never carry one).</summary>
        public const int ItemPaddingX = 14;

        private const int MinItemWidth = 60;

        private List<MenuItemModel> menuSections;
        private readonly List<MenuBarItem> itemElements = new List<MenuBarItem>();
        private readonly Element host;

        /// <summary>Total width of the built item strip — where the last
        /// item ends. Lets a host sharing this bar's row with something else
        /// (ModalWindowElement/FullScreenModalElement's own EnsureToolbar,
        /// 2026-08-18: the toolbar sits on the SAME row as the menu bar
        /// instead of below it) know where its own content should start.</summary>
        public float ContentWidth { get; private set; }

        public MenuBarElement(Element host, List<MenuItemModel> sections)
        {
            this.host = host;
            menuSections = sections;
            Set<PositionTrait>(new TVVector(0, 0));
            Set<SizeTrait>(new TVVector(host.GetSize().X, BarHeight));
            // Transparent: the chrome-row STRIP behind this bar paints the
            // shared SurfaceHeader background (FullScreenModalElement/
            // ModalWindowElement chromeRowBg). The bar painting its own
            // 28px background next to the 40px toolbar left a dark gap
            // under whichever bar was shorter.
            Set<BackgroundFillTrait>(new TVFillSolidColor(Microsoft.Xna.Framework.Color.Transparent));
            BuildItems();
        }

        /// <summary>Replaces the bar's sections and rebuilds the item strip
        /// in place — same seam as FruitMenuElement.SetItems. No current
        /// ezmuze-studio caller needs this yet (each view builds its
        /// MenuSections() once at modal construction); kept for parity so a
        /// future context-sensitive-within-a-modal case has somewhere to
        /// hook in.</summary>
        public void SetItems(List<MenuItemModel> sections)
        {
            menuSections = sections;
            foreach (var open in Resources.StaticResources.RootWindow.Children.Items.Where(c => c is FruitPopupMenu).ToList())
            {
                open.Kill();
            }

            BuildItems();
        }

        private static int MeasureItemWidth(string text)
        {
            float textWidth = Resources.StaticResources.FontManager.MeasureSdfText(Resources.StaticResources.Theme.UiFont, text).X;
            return Math.Max(MinItemWidth, ItemPaddingX * 2 + (int)Math.Ceiling(textWidth));
        }

        private void BuildItems()
        {
            foreach (MenuBarItem stale in itemElements)
            {
                stale.Kill();
            }

            itemElements.Clear();

            float x = 0;
            foreach (MenuItemModel section in menuSections)
            {
                int itemWidth = MeasureItemWidth(section.Text);
                MenuItemModel captured = section;
                MenuBarItem item = new MenuBarItem(captured, (args) => OpenDropdown(captured, args), itemWidth, BarHeight);
                this.AddChild(item, "menu-bar-item");
                itemElements.Add(item);

                item.Set<PositionTrait>(new TVVector(x, 0));
                x += itemWidth;
            }

            ContentWidth = x;
        }

        /// <summary>Opens (or, if already open, replaces) this section's
        /// dropdown — same FruitPopupMenu the old global bar used, anchored
        /// below the clicked item in absolute screen coordinates (this bar
        /// is nested inside its host modal, unlike FruitMenuElement which
        /// always sat at the window root, so a local PositionTrait read
        /// won't do — GetActualPosition() walks the parent chain). Passing
        /// `this` as the popup's trigger (not just the clicked item) means
        /// hovering ANY item on this bar counts as "still over menu UI",
        /// matching FruitMenuElement's own whole-bar-hover behavior.</summary>
        private void OpenDropdown(MenuItemModel section, ClickEventArgs args)
        {
            if (section.SubItems == null || section.SubItems.Count == 0)
            {
                return;
            }

            foreach (var open in Resources.StaticResources.RootWindow.Children.Items.Where(c => c is FruitPopupMenu).ToList())
            {
                open.Kill();
            }

            FruitPopupMenu popup = new FruitPopupMenu(section.SubItems, 300, false, this);
            TVVector ps = args.Element.GetActualPosition();
            TVVector sz = args.Element.GetSize();
            popup.Set<PositionTrait>(new TVVector(ps.X, ps.Y + sz.Y));
            Resources.StaticResources.RootWindow.AddChild(popup, "popup " + Guid.NewGuid().ToString());
            popup.Set<BorderFillTrait>(new TVBorder9Grid
            {
                TopCenter = false,
                TopLeft = false,
                TopRight = false,
            });
        }

        public override void Update(Element parent = null)
        {
            base.Update(parent);

            // Follows the host modal's current width every frame — a modal
            // built once and resized/maximized later (the sequencer's own
            // resizable ModalWindowElement) would otherwise leave the bar
            // at its construction-time width. Same idiom as
            // ModalTitleBarElement.Update()'s own size-tracking.
            float width = host.GetSize().X;
            if (ElementTrait<SizeTrait>().Value().X != width)
            {
                Set<SizeTrait>(new TVVector(width, BarHeight));
            }
        }
    }
}
