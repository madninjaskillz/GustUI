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

        private const int IconBox = 24;
        private const int LabelLeft = 38;

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
                iconElement.Set<PositionTrait>(new TVVector(8, 3));
                iconElement.Set<SizeTrait>(new TVVector(IconBox, IconBox));
                iconElement.Set<FontTrait>(Resources.StaticResources.Theme.SymbolFont);
                iconElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
                iconElement.Set<TextTrait>(new TVText(icon));
            }

            if (more)
            {
                if (!hideMore)
                {
                    moreElement = this.AddChildElement<TextElement>();
                    moreElement.Set<PositionTrait>(new TVVector(width - 30, 3));
                    moreElement.Set<SizeTrait>(new TVVector(IconBox, IconBox));
                    moreElement.Set<FontTrait>(Resources.StaticResources.Theme.SymbolFont);
                    moreElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
                    moreElement.Set<TextTrait>(new TVText(UIFont.Symbol.More.Icon()));


                    Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) => clickMore(x, menuItem.SubItems)));
                    Set<OnExitTrait>(new TVEvent<ClickEventArgs>((x) =>
                    {
                        if (popup != null)
                        {
                            if (!popup.IsMouseOver())
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
            textElement.Set<PositionTrait>(new TVVector(LabelLeft, 5));
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

        private void clickMore(ClickEventArgs x, List<MenuItemModel> subItems)
        {
            if (x is ClickEventArgs clickEventArgs)
            {
                if (popup == null)
                {
                    popup = new FruitPopupMenu(subItems, 300);
                    var ps = clickEventArgs.Element.GetActualPosition();
                    popup.Set<PositionTrait>(new TVVector(ps.X + clickEventArgs.Element.GetSize().X, ps.Y));
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
                clickMore(new ClickEventArgs
                {
                    Element = this,
                }, _menuItem.SubItems);

                var autoPops = Resources.StaticResources.RootWindow.Children.Items.Where(x => x is FruitPopupMenu fpu && fpu.WasAutoPopped).ToList();
                foreach (var ap in autoPops)
                {
                    ap.Kill();
                }

            }
            base.Update(parent);
        }
    }
}
