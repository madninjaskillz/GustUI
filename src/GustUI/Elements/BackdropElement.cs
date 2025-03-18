using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Elements
{
    [ElementTraits(typeof(OnHoverTrait), typeof(OnEnterTrait), typeof(OnExitTrait), typeof(OnMouseButtonHeldDown))]
    public class BackdropElement : FilledRectangleElement
    {
        int timeout = 0;
        public BackdropElement()
        {

            Set<SizeTrait>(new TVVector(Resources.StaticResources.RootWindow.GetSize().X, Resources.StaticResources.RootWindow.GetSize().Y - 40));
            Set<PositionTrait>(new TVVector(0, 40));
            Set<BackgroundFillTrait>(new TVFillSolidColor(Microsoft.Xna.Framework.Color.Black * 0.75f));
            Set<OnHoverTrait>(new TVEvent<ClickEventArgs>((x) =>
            {
                timeout = timeout + 2;
                if (timeout > 25)
                {
                    CloseMenus();
                }
            }));
            Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) => CloseMenus()));
        }

        private void CloseMenus()
        {

            foreach (var c in Resources.StaticResources.RootWindow.Children.Items.Where(c => c is FruitPopupMenu))
            {
                c.Kill();
            }
            timeout = 0;

        }
        public override void Update(Element parent = null)
        {
            base.Update(parent);
            Set<SizeTrait>(new TVVector(Resources.StaticResources.RootWindow.GetSize().X, Resources.StaticResources.RootWindow.GetSize().Y - 40));
            Set<PositionTrait>(new TVVector(0, 40));

            if (timeout > 0)
            {
                timeout--;
            }
        }
    }
}
