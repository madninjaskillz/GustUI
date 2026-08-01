using GustUI.Extensions;
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
    /// <summary>
    /// The top menu bar ("fruit menu"). Marked <see cref="Element.IsChrome"/>
    /// by default: it is persistent navigation, so stage-clearing screens
    /// should leave it alive and lay out below <see cref="MenuHeight"/> (the
    /// same top limit fullscreen modals already respect). Tracks the root
    /// window's width on resize.
    /// </summary>
    public class FruitMenuElement : FilledRectangleElement
    {
        /// <summary>Bar height in pixels — content below the menu starts here.</summary>
        public const int MenuHeight = 40;

        private List<MenuItemModel> menuItems;

        private FilledRectangleElement logoElement;
        private FilledRectangleElement shadowElement;
        public FruitMenuElement(List<MenuItemModel> items)
        {
            menuItems = items;
            IsChrome = true;
            Set<SizeTrait>(new TVVector(Resources.StaticResources.RootWindow.GetSize().X, MenuHeight));
            Set<PositionTrait>(new TVVector(0, 0));
            Set<BackgroundFillTrait>(new TVFillSolidColor(Microsoft.Xna.Framework.Color.White*0.8f));


            if (Resources.StaticResources.Theme.MenuLogo != null)
            {
                logoElement = this.AddChildElement<FilledRectangleElement>();
                logoElement.Set<PositionTrait>(new TVVector(5, 5));
                logoElement.Set<SizeTrait>(new TVVector(24, 24));
                logoElement.Set<BackgroundFillTrait>(Resources.StaticResources.Theme.MenuLogo);
            }

            float ps = 32;
            foreach (MenuItemModel item in menuItems)
            {
                FruitMenuItem i = new FruitMenuItem(item, (item.SubItems == null || item.SubItems.Count==0) ? null : (ClickEventArgs x) =>
                {
                    // One dropdown at a time: opening a menu closes any other
                    // open menu popups (the backdrop used to sweep these, but
                    // screens without a backdrop need the bar self-contained).
                    foreach (var open in Resources.StaticResources.RootWindow.Children.Items.Where(c => c is FruitPopupMenu).ToList())
                    {
                        open.Kill();
                    }

                    FruitPopupMenu popup = new FruitPopupMenu(item.SubItems, 300);
                    var ps = x.Element.ElementTrait<PositionTrait>().Value();
                    var sz = x.Element.GetSize();
                    popup.Set<PositionTrait>(new TVVector(ps.X, ps.Y + sz.Y));
                    Resources.StaticResources.RootWindow.AddChild(popup, "popup "+Guid.NewGuid().ToString());
                    popup.Set<BorderFillTrait>(new TVBorder9Grid
                    {
                        TopCenter=false,
                        TopLeft = false,
                        TopRight = false,
                    });

                }, 100, true);

                this.AddChild(i, "fruit item");


                i.Set<PositionTrait>(new TVVector(ps, 0));

                ps = ps + 20 + i.GetSize().X;

            }


            shadowElement = AddChildElement<FilledRectangleElement>();
            shadowElement.Set<SizeTrait>(new TVVector(Resources.StaticResources.RootWindow.GetSize().X, 10));
            shadowElement.Set<PositionTrait>(new TVVector(0, MenuHeight));
            shadowElement.Set<BackgroundFillTrait>(new TVFillSimpleGradient(new Microsoft.Xna.Framework.Color(0, 0, 0, 128), new Microsoft.Xna.Framework.Color(0, 0, 0, 0),  Direction.Vertically));

        }

        public override void Update(Element parent = null)
        {
            base.Update(parent);

            // Follow the window width (the bar is built once and lives for the
            // whole session, so a static construction-time width goes stale on
            // the first resize).
            float width = Resources.StaticResources.RootWindow.GetSize().X;
            if (ElementTrait<SizeTrait>().Value().X != width)
            {
                Set<SizeTrait>(new TVVector(width, MenuHeight));
                shadowElement.Set<SizeTrait>(new TVVector(width, 10));
            }
        }


    }
}
