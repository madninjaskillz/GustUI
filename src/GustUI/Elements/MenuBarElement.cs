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

        /// <summary>The sections currently on the bar.</summary>
        internal IReadOnlyList<MenuItemModel> Sections => menuSections;

        /// <summary>
        /// The first enabled item, depth first, whose Shortcut is
        /// <paramref name="key"/> held with exactly its modifiers in
        /// <paramref name="state"/> and which has an Action to run; null when
        /// none is. Items with only SubItems are searched, never run. A
        /// disabled item hides its children too, as it does on screen.
        /// </summary>
        public static MenuItemModel FindShortcut(IEnumerable<MenuItemModel> items, Microsoft.Xna.Framework.Input.Keys key, Microsoft.Xna.Framework.Input.KeyboardState state)
        {
            if (items == null)
            {
                return null;
            }

            foreach (MenuItemModel item in items)
            {
                if (item == null || !item.Enabled)
                {
                    continue;
                }

                if (item.Action != null && item.Shortcut != null
                    && item.Shortcut.Key == key && item.Shortcut.IsHeldIn(state))
                {
                    return item;
                }

                MenuItemModel inner = FindShortcut(item.SubItems, key, state);
                if (inner != null)
                {
                    return inner;
                }
            }

            return null;
        }

        private static int MeasureItemWidth(string text)
        {
            float textWidth = Resources.StaticResources.FontManager.MeasureSdfText(Resources.StaticResources.Theme.MenuFont, text).X;
            return Math.Max(MinItemWidth, ItemPaddingX * 2 + (int)Math.Ceiling(textWidth));
        }

        /// <summary>
        /// The section that lists every open window (ezmuze #301). "View",
        /// because the sequencer's View menu already held the window toggles
        /// (Toggle stack, explorer, clip editor): the list joins them there as
        /// a section rather than being a second menu about windows. A bar with
        /// no View of its own (a module panel's File / Presets) gets a View
        /// holding just the list, so it is in the same place on every window.
        /// </summary>
        public const string WindowListSectionName = "View";

        /// <summary>Only a window's own bar lists windows.</summary>
        private bool HostsWindowList => host is ModalWindowElement;

        private MenuItemModel syntheticWindowSection;

        private static bool IsWindowListSection(MenuItemModel section)
            => string.Equals(section?.Text, WindowListSectionName, StringComparison.Ordinal);

        /// <summary>The bar's sections, plus a View for the window list when
        /// the view did not supply one -- placed before a trailing Help, where
        /// View sits on the sequencer's bar.</summary>
        private List<MenuItemModel> SectionsWithWindowList()
        {
            var sections = new List<MenuItemModel>(menuSections ?? new List<MenuItemModel>());
            if (!HostsWindowList || sections.Count == 0 || sections.Any(IsWindowListSection))
            {
                return sections;
            }

            syntheticWindowSection ??= new MenuItemModel { Text = WindowListSectionName, SubItems = new List<MenuItemModel>() };
            int at = sections.Count > 0 && string.Equals(sections[^1].Text, "Help", StringComparison.Ordinal)
                ? sections.Count - 1
                : sections.Count;
            sections.Insert(at, syntheticWindowSection);
            return sections;
        }

        /// <summary><paramref name="own"/> followed by one separator and the
        /// open windows, built now so the list is never stale.</summary>
        private static List<MenuItemModel> WithWindowList(List<MenuItemModel> own)
        {
            var items = new List<MenuItemModel>(own ?? new List<MenuItemModel>());
            List<MenuItemModel> windows = ModalWindowElement.OpenWindowItems();
            if (windows.Count == 0)
            {
                return items;
            }

            if (items.Count > 0 && !string.IsNullOrEmpty(items[^1].Text))
            {
                items.Add(new MenuItemModel { Text = "", Icon = null });
            }

            items.AddRange(windows);
            return items;
        }

        private void BuildItems()
        {
            foreach (MenuBarItem stale in itemElements)
            {
                stale.Kill();
            }

            itemElements.Clear();

            float x = 0;
            foreach (MenuItemModel section in SectionsWithWindowList())
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
            List<MenuItemModel> items = section.SubItems;
            if (HostsWindowList && IsWindowListSection(section))
            {
                items = WithWindowList(section.SubItems);
            }

            if (items == null || items.Count == 0)
            {
                return;
            }

            foreach (var open in Resources.StaticResources.RootWindow.Children.Items.Where(c => c is FruitPopupMenu).ToList())
            {
                open.Kill();
            }

            FruitPopupMenu popup = new FruitPopupMenu(items, 300, this);
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
