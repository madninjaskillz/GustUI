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
    public class VerticalStackElement : FilledRectangleElement
    {
        public VerticalStackElement()
        {
            SizeFitsChildren = false;
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

            this.Set<SizeTrait>(new TVVector(this.GetSize().X, currentY));
        }

        DateTime lastUpdate = DateTime.Now;
        public override void Update(Element parent = null)
        {
            base.Update();
            //todo - make this event based
            if (DateTime.Now - lastUpdate > TimeSpan.FromSeconds(1))
            {
                RecalculatePositions();
                lastUpdate = DateTime.Now;
            }
        }
    }
}
