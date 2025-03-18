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
        public bool WasAutoPopped { get; set; }
        public FruitPopupMenu(List<MenuItemModel> items, int width, bool autoPopped = false)
        {
            WasAutoPopped = autoPopped;
            menuItems = items;
            Set<SizeTrait>(new TVVector(width, 40*items.Count));
            Set<PositionTrait>(new TVVector(0, 0));
            Set<BackgroundFillTrait>(new TVFillSolidColor(Microsoft.Xna.Framework.Color.White*0.8f));

            float ps = 0;
            foreach (MenuItemModel item in menuItems)
            {
                FruitMenuItem i = new FruitMenuItem(item,width: width);

                this.AddChild(i, "fruit item");

                if (item.Text == "")
                {
                    ps = ps + 5;
                }

                i.Set<PositionTrait>(new TVVector(0, ps));

                ps = ps + Math.Max(i.ElementTrait<SizeTrait>().Value().Y,7);

            }

            Set<SizeTrait>(new TVVector(width, ps));
        }
    }
}
