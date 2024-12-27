using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Elements
{
    public class VerticalStackElement : RectangleElement
    {
        public VerticalStackElement()
        {
            SizeFitsChildren = true;
        }

        public override void AddChildElement(Element element, string overrideName = null)
        {
            if (overrideName == null)
            {
                overrideName = element.ElementName;
            }
            base.AddChild(element, overrideName);
            RecalculatePositions();
        }

        public override void AddChild(Element child, string name)
        {
            base.AddChild(child, name);
            RecalculatePositions();
        }

        private void RecalculatePositions()
        {
            var currentY = 0;
            foreach (var child in this.Children.Items)
            {
                child.Set<PositionTrait>(new TVVector(0, currentY));
                currentY += (int)child.GetSize().Y;
            }
        }
    }
}
