using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Elements
{
    public class VerticalScrollbarElement : FilledRectangleElement
    {
        private float scrollPosition = 0;

        private float scrollPercentage = 0;
        private float sizePercentage = 0.5f;
        private float _scrollPercentage = 0;
        private float _scrollPosition = 0;
        private Func<float> getContainerHeight;
        private RectangleElement scrollBarInner = new FilledRectangleElement();
        public VerticalScrollbarElement(Func<float> getContainerHeight)
        {
            this.Set<OnMouseButtonHeldDown>(new TVEvent<ClickEventArgs>(handleInnerDrag));
            this.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>(stopInnerDrag));
            this.Set<OnExitTrait>(new TVEvent<ClickEventArgs>(stopInnerDrag));

            Set<BorderSizeTrait>(new TVInt(0));
            Set<BorderFillTrait>(new TVBorderColorFill(Color.Transparent));

            scrollBarInner.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.DarkGray, Color.DarkGray * 0.8f, Direction.Vertically));
            AddChild(scrollBarInner, "scrollBarInner");

            scrollBarInner.Set<OnMouseButtonHeldDown>(new TVEvent<ClickEventArgs>(handleInnerDrag));
            scrollBarInner.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>(stopInnerDrag));
          
            this.getContainerHeight = getContainerHeight;
        }


        private void stopInnerDrag(ClickEventArgs args)
        {
            innerDragOffset = null;
        }

        private void handleInnerDrag(ClickEventArgs args)
        {
            if (innerDragOffset != null)
            {
                var offset = args.GlobalMousePosition.AsXna - innerDragOffset.Value;
                var containerSizeY = getContainerHeight();
                var thisHeight = this.GetSize().Y;

                var innerHeight = thisHeight * sizePercentage;
                var innerY = (thisHeight - innerHeight) * scrollPercentage;
                innerY += offset.Y;


                float maxScroll = containerSizeY - thisHeight;

                scrollPercentage = (innerY) / (thisHeight - innerHeight);
                scrollPosition = scrollPercentage * maxScroll;

                PrivateUpdate();
            }

            innerDragOffset = args.GlobalMousePosition.AsXna;
        }

        internal void HandleScrollWheel(ScrollEventArgs x)
        {
            float scrollAmount = x.ScrollWheelDelta / 10f;
            scrollPosition = scrollPosition + scrollAmount;

            PrivateUpdate();
        }

        private Vector2? innerDragOffset = null;
        public void PrivateUpdate(Element parent = null)
        {
            var containerSizeY = getContainerHeight();
            var size = this.GetSize();
            float maxScroll = containerSizeY - size.Y;

            if (scrollPosition > maxScroll)
            {
                scrollPosition = (int)maxScroll;
            }

            if (scrollPosition < 0)
            {
                scrollPosition = 0;
            }
            scrollPercentage = scrollPosition / maxScroll;
            sizePercentage = size.Y / containerSizeY;

            if (_scrollPercentage != scrollPercentage || _scrollPosition != scrollPosition)
            {
                Parent.ElementTrait<OnScrollTrait>().Value().TriggerAction?.Invoke(new ScrollEventArgs() { ScrollPercentage = scrollPercentage, ScrollPosition = scrollPosition });
                _scrollPosition = scrollPosition;
                _scrollPercentage = scrollPercentage;
            }

            var parentSize = Parent.GetSize();
            Set<PositionTrait>(new TVVector(parentSize.X - 20, 2));
            Set<SizeTrait>(new TVVector(18, parentSize.Y - 4));

            var innerHeight = this.GetSize().Y * sizePercentage;
            var innerY = (this.GetSize().Y - innerHeight) * scrollPercentage;
            scrollBarInner.Set<SizeTrait>(new TVVector(14, innerHeight));
            scrollBarInner.Set<PositionTrait>(new TVVector(2, innerY));

            base.Update(parent);
        }
        bool initial = false;
        public override void Draw()
        {
            if (!initial)
            {
                PrivateUpdate();
                initial = true;
            }
            var size = this.GetSize();
            var position = this.GetActualPosition();
            var rect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
            Resources.StaticResources.GraphicsDevice.ScissorRectangle = rect;
            base.Draw();
            Resources.StaticResources.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, (int)Resources.StaticResources.RootWindow.GetSize().X, (int)Resources.StaticResources.RootWindow.GetSize().Y);
        }
    }
}
