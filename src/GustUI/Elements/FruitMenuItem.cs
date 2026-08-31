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
        int hoverCounter = 0;
        int maxHover = 50;
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
            Set<BackgroundFillTrait>(new TVSmartFill { States = Resources.StaticResources.Theme.FruitMenuItemStates });
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
                iconElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
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
                    moreElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
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
                        hoverCounter = hoverCounter + 2;
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
                    keyElement.Set<ForegroundColorTrait>(new TVColor(menuItem.Enabled ? Color.Black : Color.Black * 0.5f));
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
            textElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
            textElement.Set<TextTrait>(new TVText(TextElement.Ellipsise(text, labelWidth, labelFont)));

            if (!menuItem.Enabled)
            {
                textElement.Set<ForegroundColorTrait>(new TVColor(Color.Black * 0.5f));
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

            if (hoverCounter == maxHover)
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
