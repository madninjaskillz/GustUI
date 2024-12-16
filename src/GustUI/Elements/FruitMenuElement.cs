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
    public class FruitMenuElement : FilledRectangleElement
    {
        private List<MenuItemModel> menuItems;
        public FruitMenuElement(List<MenuItemModel> items)
        {
            menuItems = items;  
            Set<SizeTrait>(new TVVector(Resources.StaticResources.RootWindow.GetSize().X, 40));
            Set<PositionTrait>(new TVVector(0, 0));
            Set<BackgroundFillTrait>(new TVFillSolidColor(Microsoft.Xna.Framework.Color.White));

            float ps = 32;
            foreach (MenuItemModel item in menuItems)
            {
                FruitMenuItem i = new FruitMenuItem(item.Text, item.Icon, (ClickEventArgs x) =>
                {
                    FruitPopupMenu popup = new FruitPopupMenu(item.SubItems, 200);
                    popup.Set<PositionTrait>(new TVVector(x.Element.ElementTrait<PositionTrait>().Value().X, x.Element.ElementTrait<PositionTrait>().Value().Y + x.Element.GetSize().Y));
                    Resources.StaticResources.RootWindow.AddChild(popup, "popup");
                    popup.Set<BorderFillTrait>(new TVBorder9Grid 
                    { 
                        TopCenter=false,
                        TopLeft = false,
                        TopRight = false,
                    });

                }, item.SubItems!=null && item.SubItems.Any(), 100);

                this.AddChild(i, "fruit item");

         
                i.Set<PositionTrait>(new TVVector(ps, 0));

                ps = ps + 20 + i.GetSize().X;

            }

            var shadow = AddChildElement<FilledRectangleElement>();
            shadow.Set<SizeTrait>(new TVVector(Resources.StaticResources.RootWindow.GetSize().X, 10));
            shadow.Set<PositionTrait>(new TVVector(0, 40));
            shadow.Set<BackgroundFillTrait>(new TVFillSimpleGradient(new Microsoft.Xna.Framework.Color(0, 0, 0, 128), new Microsoft.Xna.Framework.Color(0, 0, 0, 0),  Direction.Vertically));

        }
    }
}
