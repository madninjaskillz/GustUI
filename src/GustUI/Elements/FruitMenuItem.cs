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
        public FruitMenuItem(MenuItemModel menuItem, Action<ClickEventArgs> actionOverride = null, int width = 300, bool hideMore = false)
        {
            _menuItem = menuItem;
            var icon = menuItem.Icon;
            var text = menuItem.Text;
            var action = actionOverride != null ? actionOverride : (x)=>{
                menuItem.Action(x);
                var autoPops = Resources.StaticResources.RootWindow.Children.Items.Where(x => x is FruitPopupMenu fpu);
                foreach (var ap in autoPops)
                {
                    ap.Kill();
                }

            };
            var more = menuItem.SubItems?.Count > 0;
            Set<SizeTrait>(new TVVector(width, 40));
            Set<BackgroundFillTrait>(new TVSmartFill { States = Resources.StaticResources.Theme.FruitMenuItemStates });
            Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) =>
            {
                Log.This("doing click");
                action(x);
            }));

            if (icon != null)
            {
                iconElement = this.AddChildElement<TextElement>();
                iconElement.Set<PositionTrait>(new TVVector(10, 5));
                iconElement.Set<SizeTrait>(new TVVector(32, 32));
                iconElement.Set<FontTrait>(Resources.StaticResources.Theme.SymbolFont);
                iconElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
                iconElement.Set<TextTrait>(new TVText(icon));
            }

            if (more)
            {
                if (!hideMore)
                {
                    moreElement = this.AddChildElement<TextElement>();
                    moreElement.Set<PositionTrait>(new TVVector(width - 40, 5));
                    moreElement.Set<SizeTrait>(new TVVector(32, 32));
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
                    float height = 8;
                    float iconSize = 42;
                    float iconHeight = 26;
                    float ps = width - (30 + (menuItem.Shortcut.Modifiers.Count * iconSize));
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
                    keyElement.Set<SizeTrait>(new TVVector(30, iconHeight));
                    keyElement.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
                    keyElement.Set<ForegroundColorTrait>(new TVColor(menuItem.Enabled ? Color.Black : Color.Black * 0.5f));
                    keyElement.Set<TextTrait>(new TVText(menuItem.Shortcut.Key.ToString()));
                }
            }


            textElement = this.AddChildElement<TextElement>();
            textElement.Set<PositionTrait>(new TVVector(50, 6));
            textElement.Set<SizeTrait>(new TVVector(width - 70, 50));
            textElement.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            textElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
            textElement.Set<TextTrait>(new TVText(text));

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
