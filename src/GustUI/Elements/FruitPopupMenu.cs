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
    public class FruitPopupMenu : FilledRectangleElement
    {
        private List<MenuItemModel> menuItems;
        public FruitPopupMenu(List<MenuItemModel> items, int width)
        {
            menuItems = items;
            Set<SizeTrait>(new TVVector(width, 40*items.Count));
            Set<PositionTrait>(new TVVector(0, 0));
            Set<BackgroundFillTrait>(new TVFillSolidColor(Microsoft.Xna.Framework.Color.White));

            float ps = 0;
            foreach (MenuItemModel item in menuItems)
            {
                FruitMenuItem i = new FruitMenuItem(item.Text, item.Icon, item.Action, item.SubItems != null && item.SubItems.Any(), width);

                this.AddChild(i, "fruit item");


                i.Set<PositionTrait>(new TVVector(0, ps));

                ps = ps + 40;

            }
        }
    }
}
