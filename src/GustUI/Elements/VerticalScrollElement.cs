using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;

namespace GustUI.Elements
{
    [ElementTraits(typeof(OnScrollTrait), typeof(OnScrollWheelChanged))]
    public class VerticalScrollElement : FilledRectangleElement
    {
        private RectangleElement container = new RectangleElement();
        private VerticalScrollbarElement scrollBar;

        public VerticalScrollElement()
        {
            scrollBar = new VerticalScrollbarElement(getContainerHeight);
            
            base.AddChild(container, "container");
            base.AddChild(scrollBar, "scrollBar");
            container.SizeFitsChildren = true;

            this.Set<OnScrollTrait>(new TVEvent<ScrollEventArgs>(x => handleScroll(x)));
            this.Set<OnScrollWheelChanged>(new TVEvent<ScrollEventArgs>(x => scrollBar.HandleScrollWheel(x)));
            setup();
        }

        private void setup()
        {
            scrollBar.Set<SizeTrait>(new TVVector(20, this.GetSize().Y));
            scrollBar.Set<PositionTrait>(new TVVector(this.GetSize().X - 20, 2));
        }

        private float getContainerHeight()
        {
            var s = container.GetSize();
            var h = s.Y;
            return h;
        }

        private void handleScroll(ScrollEventArgs x)
        {
            TVVector oldPosition = container.GetRelativePosition();
            var newPosition = new TVVector(this.GetRelativePosition().X, -x.ScrollPosition);

            var delta = newPosition - oldPosition;

            container.Set<PositionTrait>(newPosition);
        }

        private Vector2 previousSize = Vector2.Zero;
        public override void Update(Element parent = null)
        {
            var thisSize = this.GetSize().AsXna;
            if (thisSize.X != previousSize.X || thisSize.Y != previousSize.Y)
            {
                previousSize = thisSize;
                setup();
            }
            base.Update(parent);
        }

        public override void Draw()
        {
            var size = this.GetSize();
            
            var position = this.GetActualPosition();
            var rect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
            Resources.StaticResources.DrawManager.SetScissor(rect);
//            Resources.StaticResources.GraphicsDevice.ScissorRectangle = rect;
            base.Draw();
            Resources.StaticResources.DrawManager.SetScissor(null);
//            Resources.StaticResources.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, (int)Resources.StaticResources.RootWindow.GetSize().X, (int)Resources.StaticResources.RootWindow.GetSize().Y);
        }

        public override void AddChildElement(Element element, string overrideName = null)
        {
            if (overrideName == null)
            {
                overrideName = element.ElementName;
            }
            container.AddChild(element, overrideName);
        }

        public override void AddChild(Element child, string name)
        {
            container.AddChild(child, name);
        }
    }
}
