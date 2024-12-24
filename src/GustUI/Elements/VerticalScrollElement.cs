using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    [ElementTraits(typeof(OnScrollTrait))]
    public class VerticalScrollElement : FilledRectangleElement
    {
        private RectangleElement container = new RectangleElement();
        private RectangleElement scrollBar = new FilledRectangleElement();
        private RectangleElement scrollBarInner = new FilledRectangleElement();
        private float scrollPosition = 0;

        private float scrollPercentage = 0;
        private float sizePercentage = 0.5f;
        public VerticalScrollElement()
        {
            base.AddChild(container, "container");
            base.AddChild(scrollBar, "scrollBar");
            container.SizeFitsChildren = true;
            scrollBar.Set<BorderSizeTrait>(new TVInt(0));
            scrollBar.Set<BorderFillTrait>(new TVBorderColorFill(Color.Transparent));
            //scrollBar.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.Gray, Color.DarkGray, Direction.Vertically));

            scrollBarInner.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.DarkGray, Color.DarkGray * 0.8f, Direction.Vertically));
            scrollBar.AddChild(scrollBarInner, "scrollBarInner");

            scrollBarInner.Set<OnMouseButtonHeldDown>(new TVEvent<ClickEventArgs>(handleInnerDrag));
            scrollBarInner.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>(stopInnerDrag));
            scrollBarInner.Set<OnExitTrait>(new TVEvent<ClickEventArgs>(stopInnerDrag));

        }

        private void stopInnerDrag(ClickEventArgs args)
        {
            innerDragOffset = null;
        }

        private void handleInnerDrag(ClickEventArgs args)
        {
            if (innerDragOffset != null)
            {
                var offset = args.GlobalMousePosition.AsXna-innerDragOffset.Value;
                var containerSize = container.GetSize();
                
                var innerHeight = scrollBar.GetSize().Y * sizePercentage;
                var innerY = (scrollBar.GetSize().Y - innerHeight) * scrollPercentage;
                innerY += offset.Y;

                var size = this.GetSize();
                float maxScroll = containerSize.Y - size.Y;

                scrollPercentage = (innerY) / (scrollBar.GetSize().Y - innerHeight);
                scrollPosition = scrollPercentage * maxScroll;

            }

            innerDragOffset = args.GlobalMousePosition.AsXna;
        }

        private Vector2? innerDragOffset = null;
        public override void Update(Element parent = null)
        {
            var containerSize = container.GetSize();
            var size = this.GetSize();
            float maxScroll = containerSize.Y - size.Y;

            if (scrollPosition > maxScroll)
            {
                scrollPosition = (int)maxScroll;
            }

            if (scrollPosition < 0)
            {
                scrollPosition = 0;
            }
            scrollPercentage = scrollPosition / maxScroll;
            sizePercentage = size.Y / containerSize.Y;
            container.Set<PositionTrait>(new TVVector(0, -scrollPosition));


            scrollBar.Set<PositionTrait>(new TVVector(this.GetSize().X - 20, 2));
            scrollBar.Set<SizeTrait>(new TVVector(18, this.GetSize().Y - 4));

            var innerHeight = scrollBar.GetSize().Y * sizePercentage;
            var innerY = (scrollBar.GetSize().Y - innerHeight) * scrollPercentage;
            scrollBarInner.Set<SizeTrait>(new TVVector(14, innerHeight));
            scrollBarInner.Set<PositionTrait>(new TVVector(2, innerY));

            base.Update(parent);
        }
        public override void Draw()
        {
            var size = this.GetSize();
            var position = this.GetActualPosition();
            var rect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
            Resources.StaticResources.GraphicsDevice.ScissorRectangle = rect;
            base.Draw();
            Resources.StaticResources.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, (int)Resources.StaticResources.RootWindow.GetSize().X, (int)Resources.StaticResources.RootWindow.GetSize().Y);
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
