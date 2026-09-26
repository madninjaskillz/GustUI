using GustUI.Elements.InputElements;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
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

        /// <summary>
        /// The ONE submenu currently open beneath this popup, and the item
        /// that opened it. A popup owns its open submenu rather than each
        /// item owning its own, because an item can only ever learn that the
        /// pointer left it ONCE (OnExitTrait is an edge, not a state), and
        /// it deliberately spends that one notification declining to close
        /// while the pointer is heading INTO the submenu. Nothing ever
        /// revisits it after that, so an item-owned submenu is stranded open
        /// the moment the pointer wanders through it and back — and the next
        /// item opens a second one alongside (2026-08-30, bug #24: "sub
        /// menus can stack and things become unreadable"). Ownership here
        /// makes "one submenu per level" an invariant of the level itself
        /// instead of a promise every item has to keep on its own.
        /// </summary>
        private FruitMenuItem submenuOwner;
        private FruitPopupMenu submenu;

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

        /// <summary>
        /// The search field, when this popup was asked for one, and the items
        /// it filters. A menu with a hundred entries behind three levels of
        /// submenu is a menu people stop reading; typing is how they ask it a
        /// question instead.
        /// </summary>
        private TextFieldElement search;
        private List<MenuItemModel> unfilteredItems;
        private string searchHint = "";
        private string lastQuery;

        /// <summary>Most results shown at once. Past this the list is longer
        /// than the window, and the query is the thing to narrow rather than
        /// the scroll.</summary>
        private const int MaxResults = 40;

        public FruitPopupMenu(List<MenuItemModel> items, int width, Element trigger = null)
            : this(items, width, trigger, searchable: false, searchHint: null)
        {
        }

        /// <summary>
        /// A popup with a SEARCH ROW at the top, focused the moment it opens.
        ///
        /// Typing replaces the list with every leaf item that matches --
        /// including the ones inside submenus, labelled with the trail that
        /// leads to them ("With Module > Bass > Reese"). Clearing the box puts
        /// the menu back exactly as it was. The point is that a nested menu
        /// stops being somewhere you navigate and becomes somewhere you can
        /// ask: a person who knows they want "Reese" should not have to
        /// remember first whether it is a module, a template or a pack group.
        /// </summary>
        public FruitPopupMenu(List<MenuItemModel> items, int width, Element trigger, bool searchable, string searchHint = null)
        {
            this.trigger = trigger;
            Depth = PopupDepth;
            Live.Add(this);
            menuItems = items;
            unfilteredItems = items;
            this.searchHint = searchHint ?? "Search...";
            Set<SizeTrait>(new TVVector(width, FruitMenuItem.RowHeight * items.Count));
            Set<PositionTrait>(new TVVector(0, 0));
            // Same translucent chrome family as the fruit menu bar it extends
            // from (design-guide.md §1.5) — was a flat, harsher near-white
            // independent of that bar's own (already-retinted) look.
            Set<BackgroundFillTrait>(new TVFillSimpleGradient(
                Resources.StaticResources.Theme.MenuBarFillTop,
                Resources.StaticResources.Theme.MenuBarFillBottom,
                Direction.Vertically));

            this.rowWidth = width;

            if (searchable)
            {
                BuildSearchRow();
            }

            LayOutItems();

            Set<OnScrollWheelChanged>(new TVEvent<ScrollEventArgs>(HandleWheel));
        }

        private int rowWidth;

        /// <summary>Y the items start at: under the search row when there is
        /// one.</summary>
        private float itemsTop;

        private void BuildSearchRow()
        {
            // A MENU ROW THAT HAPPENS TO TAKE TYPING. A TextFieldElement dresses
            // itself for a dialog — SurfaceRaised with BodyText on it — and a
            // popup is a LIGHT strip in both themes (see Theme.MenuBarFillTop),
            // so the default look put a dark box with pale text at the top of a
            // pale menu. Everything here is the palette the rows beside it use:
            // the menu's own fill, black ink, MenuFont, and the row height that
            // makes it line up with them.
            search = new TextFieldElement
            {
                MaxLength = 64,
                Text = "",
                Placeholder = searchHint,
                Font = Resources.StaticResources.Theme.MenuFont,
            };

            search.Set<PositionTrait>(new TVVector(SearchInset, SearchInset));
            search.Set<SizeTrait>(new TVVector(rowWidth - (SearchInset * 2), FruitMenuItem.RowHeight));
            Color menuFill = Resources.StaticResources.Theme.MenuBarFillBottom;
            search.Surface = Color.Lerp(menuFill, Color.White, 0.5f);
            search.SurfaceFocused = Color.White;
            search.BorderColour = Color.Lerp(menuFill, Color.Black, 0.25f);
            search.BorderFocusedColour = Resources.StaticResources.Theme.MenuHighlight;
            search.Ink = Color.Black;
            search.FitText();
            AddChild(search, "menu search");

            itemsTop = FruitMenuItem.RowHeight + (SearchInset * 2);
            lastQuery = "";

            // FOCUSED ON OPEN, which is the whole point: the menu appears and
            // you are already typing into it. Through the InputManager rather
            // than a flag on the field, because focus is one thing for the
            // whole UI and two ideas of who has it is how a keystroke ends up
            // somewhere nobody is looking.
            Resources.StaticResources.InputManager.SetFocus(search);
        }

        /// <summary>Places (or re-places) one row per item under the search
        /// row, and sizes the popup to fit.</summary>
        private void LayOutItems()
        {
            foreach ((FruitMenuItem row, float _) in itemRows)
            {
                row.Kill();
            }

            itemRows.Clear();
            keyboardRow = -1;

            float ps = itemsTop;
            foreach (MenuItemModel item in menuItems)
            {
                FruitMenuItem i = new FruitMenuItem(item, width: rowWidth);

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

            // THE BOX DOES NOT MOVE WHILE YOU TYPE. Every keystroke changes how
            // many results there are, and a popup that resized to fit them
            // jumped under the pointer on each one — worse near the bottom of
            // the window, where the on-screen clamp then slid it up as well.
            // So the first layout fixes the height and every later one lives
            // inside it: the list scrolls, the frame stays where it opened.
            naturalHeight = ps;
            if (search == null)
            {
                Set<SizeTrait>(new TVVector(rowWidth, ps));
                return;
            }

            if (lockedHeight <= 0)
            {
                lockedHeight = ps;
            }

            Set<SizeTrait>(new TVVector(rowWidth, lockedHeight));
        }

        /// <summary>The height a searchable popup keeps for its whole life —
        /// whatever its unfiltered list needed. Zero until the first layout.</summary>
        private float lockedHeight;

        /// <summary>Margin around the search row, and what the items clear.</summary>
        private const float SearchInset = 4f;

        /// <summary>
        /// Re-reads the search box and, when the query has changed, replaces
        /// the list with the matches.
        ///
        /// Polled rather than driven by a change event because a text field
        /// reports what it holds and nothing else. It runs once a frame and
        /// does nothing at all while the string is the same.
        /// </summary>
        private void ApplySearch()
        {
            if (search == null)
            {
                return;
            }

            string query = (search.Text ?? "").Trim();
            if (string.Equals(query, lastQuery, StringComparison.Ordinal))
            {
                return;
            }

            lastQuery = query;
            CloseSubmenu();

            if (query.Length == 0)
            {
                menuItems = unfilteredItems;
                LayOutItems();
                return;
            }

            var results = new List<MenuItemModel>();
            Collect(unfilteredItems, "", query, results);

            if (results.Count == 0)
            {
                results.Add(new MenuItemModel { Text = "No matches", Enabled = false });
            }

            menuItems = results;
            LayOutItems();
        }

        /// <summary>
        /// Every leaf under <paramref name="items"/> that matches, labelled
        /// with the trail that leads to it.
        ///
        /// A SUBMENU MATCHES THROUGH ITS CHILDREN. Only a leaf knows what it
        /// does, so a heading whose own name matches contributes its children
        /// rather than itself -- an entry that did nothing when picked would
        /// be worse than no entry. The trail is part of what is matched, so
        /// typing the heading finds everything under it.
        /// </summary>
        private static void Collect(List<MenuItemModel> items, string trail, string query, List<MenuItemModel> into)
        {
            if (items == null)
            {
                return;
            }

            foreach (MenuItemModel item in items)
            {
                if (into.Count >= MaxResults)
                {
                    return;
                }

                string text = item?.Text ?? "";
                if (text.Length == 0)
                {
                    continue;   // a separator has nothing to match
                }

                string here = trail.Length == 0 ? text : trail + " \u203a " + text;

                if (item.SubItems != null && item.SubItems.Count > 0)
                {
                    Collect(item.SubItems, here, query, into);
                    continue;
                }

                if (item.Action == null)
                {
                    continue;   // a label is not a destination
                }

                if (here.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0)
                {
                    continue;
                }

                into.Add(new MenuItemModel
                {
                    Text = here,
                    Icon = item.Icon,
                    Action = item.Action,
                    Enabled = item.Enabled,
                    Shortcut = item.Shortcut,
                });
            }
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

            ApplySearch();

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

            // A searchable popup keeps the height it opened at (see
            // LayOutItems), so what it shows is that, capped to the window.
            float wanted = search != null && lockedHeight > 0 ? lockedHeight : naturalHeight;
            bool scrolls = Math.Max(naturalHeight, wanted) > available || naturalHeight > wanted;
            float shownHeight = Math.Min(wanted, available);

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

        /// <summary>
        /// Records <paramref name="opening"/> as this level's open submenu,
        /// closing whatever was open here first. Called by the
        /// <see cref="FruitMenuItem"/> that just built it: the item
        /// registers its submenu with the level it lives on instead of
        /// keeping it to itself, which is what keeps two siblings from both
        /// having one open. Re-registering the SAME owner is a no-op, so an
        /// item re-asserting its already-open submenu doesn't flicker it
        /// shut and back.
        /// </summary>
        public void OpenSubmenu(FruitMenuItem owner, FruitPopupMenu opening)
        {
            if (ReferenceEquals(submenuOwner, owner) && ReferenceEquals(submenu, opening))
            {
                return;
            }

            CloseSubmenu();
            submenuOwner = owner;
            submenu = opening;

            // Told, rather than worked out from the element tree: a submenu is
            // a child of the WINDOW (it has to be, to draw over everything),
            // so there is nothing in its parentage that says which row it hangs
            // off. The left arrow needs both.
            if (opening != null)
            {
                opening.openedFrom = this;
                opening.openedBy = owner;
            }
        }

        /// <summary>
        /// Closes this level's submenu, and — through that submenu's own
        /// <see cref="Kill"/> — everything nested below it. Without the
        /// cascade a third-level submenu outlives the second-level popup it
        /// hangs off, which is the same defect one level down.
        /// </summary>
        public void CloseSubmenu()
        {
            FruitMenuItem owner = submenuOwner;
            FruitPopupMenu open = submenu;
            submenuOwner = null;
            submenu = null;

            // The owner is told first, and unconditionally: its own "do I
            // already have one open?" latch has to be cleared even if the
            // popup was already gone, or the item refuses to ever reopen.
            owner?.ForgetSubmenu();
            open?.Kill();
        }

        /// <summary>Takes this popup's own submenu down with it, so closing
        /// a level closes the whole tail below it however deep it goes.</summary>
        public override void Kill()
        {
            Live.Remove(this);
            CloseSubmenu();
            base.Kill();
        }

        /// <summary>The popup this one is a submenu OF, and the row it hangs
        /// off — set by <see cref="OpenSubmenu"/>. Null for a top-level
        /// popup.</summary>
        private FruitPopupMenu openedFrom;
        private FruitMenuItem openedBy;

        /// <summary>Which row the keyboard is on, or -1 for none. Nothing is
        /// highlighted until an arrow key is pressed: a menu that opened with
        /// its first row already lit looks like a menu that is about to do
        /// something.</summary>
        private int keyboardRow = -1;

        /// <summary>What a key does to an open menu. See
        /// <see cref="CommandFor"/>.</summary>
        public enum MenuCommand
        {
            None,
            Down,
            Up,
            Activate,
            Close,
            EnterSubmenu,
            Back,
        }

        /// <summary>
        /// Every popup built and not yet killed, oldest first. A submenu is
        /// built after the level it hangs off, so the deepest open level is
        /// the last one here that is still on screen.
        /// </summary>
        private static readonly List<FruitPopupMenu> Live = new List<FruitPopupMenu>();

        /// <summary>
        /// The key a menu takes, and what it does with it (ezmuze #330, #350).
        ///
        /// Up, Down, Enter and Escape are the menu's whatever has the focus:
        /// in a single-line search field Up and Down only jump the caret to
        /// the ends, which is worth less than moving down a list of results.
        /// LEFT AND RIGHT BELONG TO THE SEARCH FIELD while it has the focus,
        /// because that is where the caret lives and a caret that cannot be
        /// moved is a box you cannot correct a typo in. Everything else is not
        /// the menu's, and goes on to the window as usual.
        /// </summary>
        internal static MenuCommand CommandFor(Keys key, bool typingInSearch)
        {
            switch (key)
            {
                case Keys.Down: return MenuCommand.Down;
                case Keys.Up: return MenuCommand.Up;
                case Keys.Enter: return MenuCommand.Activate;
                case Keys.Escape: return MenuCommand.Close;
                case Keys.Right: return typingInSearch ? MenuCommand.None : MenuCommand.EnterSubmenu;
                case Keys.Left: return typingInSearch ? MenuCommand.None : MenuCommand.Back;
                default: return MenuCommand.None;
            }
        }

        /// <summary>
        /// Offers a newly pressed key to the open menu, if there is one, and
        /// says whether it took it. Called by InputManager AHEAD of dialogs,
        /// text fields and every shortcut: an open menu is what the person is
        /// looking at, so a key it takes goes nowhere else.
        ///
        /// It used to read the keyboard for itself in Update, which runs after
        /// InputManager has already dispatched the frame's keys — so Escape
        /// closed the menu AND ran the window's own Escape, and one press
        /// meant to dismiss a menu shut the module panel or the module editor
        /// behind it (ezmuze #350, #330). Enter did the same to the
        /// sequencer's "open the piano roll".
        ///
        /// Only the DEEPEST open level navigates: a submenu is what the person
        /// is looking at.
        /// </summary>
        internal static bool HandleOpenMenuKey(Keys key)
        {
            FruitPopupMenu menu = Deepest();
            if (menu == null)
            {
                return false;
            }

            switch (CommandFor(key, menu.TypingInSearch()))
            {
                case MenuCommand.Down:
                    menu.MoveHighlight(+1);
                    return true;
                case MenuCommand.Up:
                    menu.MoveHighlight(-1);
                    return true;
                case MenuCommand.Activate:
                    menu.Activate();
                    return true;
                case MenuCommand.Close:
                case MenuCommand.Back:
                    menu.CloseLevel();
                    return true;
                case MenuCommand.EnterSubmenu:
                    menu.EnterSubmenu();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether any menu is open on screen.</summary>
        public static bool AnyOpen => Deepest() != null;

        /// <summary>The innermost open level: the newest live popup that is
        /// on screen, followed down its open submenus.</summary>
        private static FruitPopupMenu Deepest()
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                FruitPopupMenu menu = Live[i];
                if (menu.Parent == null)
                {
                    continue;
                }

                while (menu.submenu != null && menu.submenu.Parent != null)
                {
                    menu = menu.submenu;
                }

                return menu;
            }

            return null;
        }

        private bool TypingInSearch()
            => search != null && Resources.StaticResources.InputManager.CurrentlyFocused == search;

        /// <summary>Moves to the next row that can be landed on, wrapping at
        /// the ends — a menu is a ring, and arrowing down past the last item to
        /// reach the first is faster than arrowing back up through nine.</summary>
        private void MoveHighlight(int step)
        {
            if (itemRows.Count == 0)
            {
                return;
            }

            int from = keyboardRow;
            for (int n = 0; n < itemRows.Count; n++)
            {
                from = ((from + step) % itemRows.Count + itemRows.Count) % itemRows.Count;
                if (itemRows[from].Item.Selectable)
                {
                    SetHighlight(from);
                    return;
                }
            }
        }

        private void SetHighlight(int index)
        {
            for (int i = 0; i < itemRows.Count; i++)
            {
                itemRows[i].Item.KeyboardHighlighted = i == index;
            }

            keyboardRow = index;

            // A highlight below the fold is one nobody can see.
            if (index >= 0 && index < itemRows.Count)
            {
                float rowTop = itemRows[index].NaturalY;
                float rowBottom = rowTop + FruitMenuItem.RowHeight;
                float height = ElementTrait<SizeTrait>().Value().Y;

                if (rowTop - scroll < itemsTop)
                {
                    ApplyScroll(rowTop - itemsTop);
                }
                else if (rowBottom - scroll > height)
                {
                    ApplyScroll(rowBottom - height);
                }
            }
        }

        private void Activate()
        {
            if (keyboardRow < 0 || keyboardRow >= itemRows.Count)
            {
                return;
            }

            FruitMenuItem row = itemRows[keyboardRow].Item;
            if (row.HasSubmenu)
            {
                EnterSubmenu();
                return;
            }

            row.Activate();
        }

        private void EnterSubmenu()
        {
            if (keyboardRow < 0 || keyboardRow >= itemRows.Count)
            {
                return;
            }

            FruitMenuItem row = itemRows[keyboardRow].Item;
            FruitPopupMenu opened = row.OpenSubmenuFromKeyboard();

            // Straight onto its first row: having asked for the submenu, the
            // next arrow press should move WITHIN it rather than open it a
            // second time.
            opened?.MoveHighlight(+1);
        }

        /// <summary>Escape and Left: closes this level. A submenu hands the
        /// highlight back to the row it came from; a top-level popup closes
        /// the menu.</summary>
        private void CloseLevel()
        {
            FruitPopupMenu parent = openedFrom;

            if (parent == null)
            {
                Kill();
                return;
            }

            parent.CloseSubmenu();
            parent.RestoreHighlight(openedBy);
        }

        /// <summary>Puts the keyboard back on the row whose submenu just
        /// closed.</summary>
        private void RestoreHighlight(FruitMenuItem row)
        {
            for (int i = 0; i < itemRows.Count; i++)
            {
                if (ReferenceEquals(itemRows[i].Item, row))
                {
                    SetHighlight(i);
                    return;
                }
            }
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
