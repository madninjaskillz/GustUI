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

        private FilledRectangleElement logoElement;
        public FruitMenuElement(List<MenuItemModel> items)
        {
            menuItems = items;  
            Set<SizeTrait>(new TVVector(Resources.StaticResources.RootWindow.GetSize().X, 40));
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

            
            var shadow = AddChildElement<FilledRectangleElement>();
            shadow.Set<SizeTrait>(new TVVector(Resources.StaticResources.RootWindow.GetSize().X, 10));
            shadow.Set<PositionTrait>(new TVVector(0, 40));
            shadow.Set<BackgroundFillTrait>(new TVFillSimpleGradient(new Microsoft.Xna.Framework.Color(0, 0, 0, 128), new Microsoft.Xna.Framework.Color(0, 0, 0, 0),  Direction.Vertically));

        }


    }
}
