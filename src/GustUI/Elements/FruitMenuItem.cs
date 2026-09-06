using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Models;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Elements
{
    [ElementTraits(typeof(OnMouseButtonHeldDown), typeof(OnHoverTrait), typeof(OnExitTrait))]
    public class FruitMenuItem : FilledRectangleElement
    {
        TextElement iconElement;
        TextElement textElement;
        TextElement moreElement;
        MenuItemModel _menuItem;
        FruitPopupMenu popup = null;
        /// <summary>
        /// How long the pointer has dwelt on this row, counted in frames: a
        /// hovered frame adds 2 and every update takes 1 back, so sitting
        /// still climbs by 1 a frame and moving away drains it at the same
        /// rate. At <see cref="maxHover"/> the row opens its submenu without
        /// being clicked.
        ///
        /// CLAMPED near that threshold, and it has to be. Uncapped it just kept
        /// climbing for as long as the pointer sat there — a row logged at 241
        /// after four seconds — and the drain then took four seconds to fall
        /// back through the threshold, which it did while the pointer was on a
        /// DIFFERENT row by then. The test below fired on that way down and
        /// reopened the submenu you had just left, on top of the one you had
        /// moved to (2026-09-06, user report: "I hover one option then move to
        /// a second, it shows the second, then replaces it with the first
        /// despite me not being on it any more"). Capped, leaving a row puts
        /// the count under the threshold on the very next frame, so the drain
        /// can never cross it a second time.
        ///
        /// The clamp is maxHover + 1 rather than maxHover, and the extra 1 is
        /// not slack: <see cref="Update"/> drains BEFORE it tests, so a count
        /// clamped exactly at the threshold arrives at the test one short and
        /// the submenu never opens at all. One frame of headroom is what puts
        /// the steady state under the pointer exactly on the threshold.
        /// </summary>
        int hoverCounter = 0;

        /// <summary>The dwell that opens a submenu, in frames — a bit under a
        /// second at 60fps.</summary>
        int maxHover = 50;

        /// <summary>
        /// This row's own highlight fill, kept so the row's INK can be read off
        /// the same eased number the background is drawn with.
        ///
        /// Every label, glyph and shortcut letter here used to be a fixed
        /// <c>Color.Black</c>. That is right for a row at rest — a popup is a
        /// light strip in both themes — and it was wrong the instant the
        /// pointer arrived, because the highlight is a saturated blue: the one
        /// row you had picked out was the one row you could not read
        /// (2026-09-06, live user report; the menu BAR had the same bug found
        /// the same way in 2026-08 and fixed only for itself).
        /// </summary>
        private readonly TVSmartFill highlightFill;

        /// <summary>
        /// <paramref name="rest"/> for an unhovered row, sliding to the
        /// highlight's own ink as the blue comes in under it.
        ///
        /// A live lambda per element rather than a value set on enter and exit:
        /// the two OnEnter/OnExit edges cannot express a crossfade, and a
        /// snapshot taken at construction is a menu built before a theme switch
        /// still painted in the old palette — the exact bug ezmuze #66 was.
        /// </summary>
        private TVColor Ink(Color rest, float dim = 1f)
            => new TVColor(() => Color.Lerp(
                rest,
                Resources.StaticResources.Theme.MenuHighlightInk,
                highlightFill.HighlightWeight) * dim);

        /// <summary>Menu rows cross faster than the design guide's default
        /// ~150ms button transition (design-guide.md §5, 2026-09-06): a row is
        /// something you sweep past on the way to another one.</summary>
        internal const float HighlightFadeSeconds = 0.1f;
        /// <summary>
        /// One row's height, and the type scale that goes with it.
        ///
        /// Menus were sized for a touch target on a tablet: a 40px row with a
        /// 32px icon and body-sized text, which in a dense audio app reads as
        /// a menu shouting. The label is Theme.MenuFont — shared with the bar
        /// that opens it — and everything around it is scaled to match:
        /// shrinking the type alone would have left small text marooned in a
        /// tall row, which looks like a bug rather than like a smaller menu.
        /// </summary>
        public const int RowHeight = 30;

        /// <summary>The icon COLUMN's width — layout space, so every row's
        /// label starts at the same x whatever its glyph. Not the glyph's own
        /// size; see <see cref="IconFontSize"/>.</summary>
        private const int IconBox = 24;

        /// <summary>
        /// The glyph's type size, matched to the label's (Theme.MenuFont is
        /// 18) rather than to Theme.SymbolFont's own 24 (#40).
        ///
        /// A symbol font is type, and 24pt icons beside 18pt text read as a
        /// row of oversized pictures with the words as an afterthought — the
        /// two are meant to be one line, and one line has one size. The
        /// COLUMN stays 24 wide: that is alignment, and shrinking it would
        /// pull every label left for no reason.
        /// </summary>
        private const int IconFontSize = 18;

        private const int LabelLeft = 38;

        /// <summary>The label's own inset from the top of the row. The icon
        /// takes the same one, so glyph and text sit on one line rather than
        /// the icon riding 2px high as it did while it was taller.</summary>
        private const int RowTextTop = 5;

        /// <summary>Theme.SymbolFont's family at the label's size — the icon
        /// font this menu actually wants (#40).</summary>
        private static TVFont IconFont => new()
        {
            Family = Resources.StaticResources.Theme.SymbolFont.Family,
            Size = IconFontSize,
            Border = 0,
        };

        public FruitMenuItem(MenuItemModel menuItem, Action<ClickEventArgs> actionOverride = null, int width = 300, bool hideMore = false)
        {
            _menuItem = menuItem;
            var icon = menuItem.Icon;
            var text = menuItem.Text;
            var action = actionOverride != null ? actionOverride : (x)=>{
                // Disabled items ignore clicks entirely (the popup stays open,
                // matching native menus); enabled items without an Action are
                // placeholders and must not NRE — they just close the menu.
                if (!menuItem.Enabled)
                {
                    return;
                }

                // Close the menu BEFORE running the action (native menu
                // order) — an action that opens its own popup (e.g. an "add
                // component" picker) must not have it swept by the close.
                var autoPops = Resources.StaticResources.RootWindow.Children.Items.Where(x => x is FruitPopupMenu fpu).ToList();
                foreach (var ap in autoPops)
                {
                    ap.Kill();
                }

                menuItem.Action?.Invoke(x);

            };
            var more = menuItem.SubItems?.Count > 0;
            Set<SizeTrait>(new TVVector(width, RowHeight));
            highlightFill = new TVSmartFill
            {
                States = Resources.StaticResources.Theme.FruitMenuItemStates,
                FadeSeconds = HighlightFadeSeconds,
            };
            Set<BackgroundFillTrait>(highlightFill);
            Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) =>
            {
                Log.This("doing click");
                action(x);
            }));

            if (icon != null)
            {
                iconElement = this.AddChildElement<TextElement>();
                iconElement.Set<PositionTrait>(new TVVector(8, RowTextTop));
                iconElement.Set<SizeTrait>(new TVVector(IconBox, IconFontSize));
                iconElement.Set<FontTrait>(IconFont);
                iconElement.Set<ForegroundColorTrait>(Ink(Color.Black));
                iconElement.Set<TextTrait>(new TVText(icon));
            }

            if (more)
            {
                if (!hideMore)
                {
                    moreElement = this.AddChildElement<TextElement>();
                    moreElement.Set<PositionTrait>(new TVVector(width - 30, RowTextTop));
                    moreElement.Set<SizeTrait>(new TVVector(IconBox, IconFontSize));
                    moreElement.Set<FontTrait>(IconFont);
                    moreElement.Set<ForegroundColorTrait>(Ink(Color.Black));
                    moreElement.Set<TextTrait>(new TVText(UIFont.Symbol.More.Icon()));


                    Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) => clickMore(x, menuItem.SubItems)));
                    // Leaving the item closes its submenu — UNLESS the
                    // pointer is already over the submenu, which is what
                    // moving diagonally into it looks like. That decision is
                    // final: OnExitTrait is an edge, so nothing re-asks once
                    // the pointer leaves the submenu again. The level's own
                    // ownership (FruitPopupMenu.OpenSubmenu) is what closes
                    // it in that case, when a sibling opens its own.
                    Set<OnExitTrait>(new TVEvent<ClickEventArgs>((x) =>
                    {
                        if (popup != null && !popup.IsMouseOver())
                        {
                            if (OwningPopup != null)
                            {
                                // Goes through the level so its record of
                                // "what is open here" clears too; that call
                                // is what nulls `popup`, via ForgetSubmenu.
                                OwningPopup.CloseSubmenu();
                            }
                            else
                            {
                                popup.Kill();
                                popup = null;
                            }
                        }
                    }));

                    Set<OnHoverTrait>(new TVEvent<ClickEventArgs>((x) =>
                    {
                        hoverCounter = Math.Min(hoverCounter + 2, maxHover + 1);
                    }));
                }
            }
            else
            {
                if (menuItem.Shortcut != null)
                {
                    // Centred in the row, not a number left over from when the
                    // row was 40 tall.
                    float iconSize = 26;
                    float iconHeight = 16;
                    float height = (RowHeight - iconHeight) / 2f;
                    float ps = width - (22 + (menuItem.Shortcut.Modifiers.Count * iconSize));
                    foreach (var mod in menuItem.Shortcut.Modifiers)
                    {
                        var modElement = this.AddChildElement<FilledRectangleElement>();
                        modElement.Set<PositionTrait>(new TVVector(ps, height));
                        modElement.Set<SizeTrait>(new TVVector(iconSize, iconHeight));
                        modElement.Set<BackgroundFillTrait>(Resources.StaticResources.Theme.KBModifiers[mod].SetOpacity(menuItem.Enabled ? 1 : 0.5f));

                        ps += iconSize + 2;
                    }
                    var keyElement = this.AddChildElement<TextElement>();
                    keyElement.Set<PositionTrait>(new TVVector(ps, height));
                    keyElement.Set<SizeTrait>(new TVVector(22, iconHeight));
                    keyElement.Set<FontTrait>(Resources.StaticResources.Theme.MenuFont);
                    keyElement.Set<ForegroundColorTrait>(Ink(Color.Black, menuItem.Enabled ? 1f : 0.5f));
                    keyElement.Set<TextTrait>(new TVText(menuItem.Shortcut.Key.ToString()));
                }
            }


            // A menu item is ONE fixed-height row, so its label must be one
            // line: wrapping produces a second line the row has no space for,
            // which lands on top of the next item. Anything too long is
            // ellipsised to the label column instead — the full text is still
            // available to a caller that wants it in a tooltip.
            TVFont labelFont = Resources.StaticResources.Theme.MenuFont;
            float labelWidth = width - 54;

            textElement = this.AddChildElement<TextElement>();
            textElement.WordWrap = false;
            textElement.Set<PositionTrait>(new TVVector(LabelLeft, RowTextTop));
            textElement.Set<SizeTrait>(new TVVector(labelWidth, RowHeight));
            textElement.Set<FontTrait>(labelFont);
            textElement.Set<ForegroundColorTrait>(Ink(Color.Black));
            textElement.Set<TextTrait>(new TVText(TextElement.Ellipsise(text, labelWidth, labelFont)));

            if (!menuItem.Enabled)
            {
                textElement.Set<ForegroundColorTrait>(Ink(Color.Black, 0.5f));
                Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
            }

            if (text.Length == 0)
            {
                Set<SizeTrait>(new TVVector(width, 2));
                Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Black * 0.3f));
            }

        }

        /// <summary>Called by the popup this item lives on when it closes
        /// this item's submenu on someone else's behalf. Clears the latch
        /// below, without which <see cref="clickMore"/> would see a stale
        /// non-null reference and refuse to ever reopen the submenu.</summary>
        internal void ForgetSubmenu()
        {
            popup = null;
        }

        /// <summary>The popup this item is a row of, or null for an item
        /// that isn't inside one — the legacy <see cref="FruitMenuElement"/>
        /// bar's own top-level items, which are children of the BAR. Those
        /// are built with hideMore:true and so never reach this code, but
        /// the null path stays a quiet no-op rather than a throw, because
        /// callers outside GustUI construct FruitMenuItems too.</summary>
        private FruitPopupMenu OwningPopup => Parent as FruitPopupMenu;

        private void clickMore(ClickEventArgs x, List<MenuItemModel> subItems)
        {
            if (x is ClickEventArgs clickEventArgs)
            {
                if (popup == null)
                {
                    popup = new FruitPopupMenu(subItems, 300);
                    var ps = clickEventArgs.Element.GetActualPosition();
                    popup.Set<PositionTrait>(new TVVector(ps.X + clickEventArgs.Element.GetSize().X, ps.Y));

                    // Registered with the LEVEL, not kept private to this
                    // item — opening this submenu closes whichever sibling's
                    // submenu was open (bug #24). Done BEFORE the popup joins
                    // the window so the outgoing one is gone by the time the
                    // new one is drawable, rather than both existing for a
                    // frame.
                    OwningPopup?.OpenSubmenu(this, popup);

                    Resources.StaticResources.RootWindow.AddChild(popup, "popup");
                    popup.Set<BorderFillTrait>(new TVBorder9Grid());

                    if (popup.GetActualPosition().X + popup.GetSize().X > Resources.StaticResources.RootWindow.GetSize().X)
                    {
                        popup.Set<PositionTrait>(new TVVector(ps.X - popup.GetSize().X, ps.Y));
                    }
                }
            }
        }

        public override void Update(Element parent = null)
        {
            if (hoverCounter > 0)
            {
                hoverCounter--;
            }

            // >=, NOT ==. The count is stepped from two places — +2 per
            // hovered frame, -1 per update — and nothing makes those alternate:
            // InputManager replays each host-pushed pointer edge as its own
            // full dispatch pass before the polled one, so a single frame can
            // raise hover twice, move the count by 3, and step straight over
            // the threshold. An exact-equality test on a number that does not
            // have to land on the value is a dwell that usually works, which
            // is harder to trust than one that never does.
            //
            // Firing on every frame at the top is not a problem: clickMore
            // declines when a submenu is already open, so this is self-
            // latching, and the clamp above is what stops the drain from
            // coming back through and firing again after the pointer has left.
            if (hoverCounter >= maxHover)
            {
                // Auto-pop on dwell. There used to be a sweep here that
                // killed every root popup flagged WasAutoPopped, meaning to
                // clear a sibling's submenu — but that flag was only ever
                // set from the constructor and every call site passed false,
                // so it never killed anything and two siblings could both
                // stay open (bug #24). clickMore now registers with the
                // owning popup, which closes the sibling's for real.
                clickMore(new ClickEventArgs
                {
                    Element = this,
                }, _menuItem.SubItems);
            }
            base.Update(parent);
        }
    }
}
