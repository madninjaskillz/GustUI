using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;

namespace GustUI.Elements
{
    /// <summary>
    /// A scissored viewport over a taller content container, scrolled by the
    /// rewritten <see cref="VerticalScrollbarElement"/> (thumb drag / track
    /// paging) and the mouse wheel. Children added to this element land in the
    /// inner container, whose height follows its children
    /// (<see cref="Element.SizeFitsChildren"/>).
    /// </summary>
    [ElementTraits(typeof(OnScrollTrait), typeof(OnScrollWheelChanged))]
    public class VerticalScrollElement : FilledRectangleElement
    {
        /// <summary>Content pixels scrolled per wheel notch (120 raw delta).</summary>
        public float WheelStep { get; set; } = 48f;

        private readonly RectangleElement container = new RectangleElement();
        private readonly VerticalScrollbarElement scrollBar;

        public VerticalScrollElement()
        {
            scrollBar = new VerticalScrollbarElement();
            scrollBar.OnUserScroll = ApplyScroll;

            base.AddChild(container, "container");
            base.AddChild(scrollBar, "scrollBar");
            container.SizeFitsChildren = true;

            this.Set<OnScrollWheelChanged>(new TVEvent<ScrollEventArgs>(HandleWheel));
        }

        private void HandleWheel(ScrollEventArgs args)
        {
            // ScrollWheelDelta = previous - current, so wheel-up is negative.
            scrollBar.ScrollPosition += args.ScrollWheelDelta / 120f * WheelStep;
            ApplyScroll(scrollBar.ScrollPosition);
        }

        private void ApplyScroll(float position)
        {
            container.Set<PositionTrait>(new TVVector(0, -position));

            // Compat: consumers listening on OnScrollTrait keep receiving
            // scroll notifications like they did with the old scrollbar.
            ElementTrait<OnScrollTrait>().Value()?.TriggerAction?.Invoke(new ScrollEventArgs
            {
                ScrollPosition = position,
                ScrollPercentage = scrollBar.MaxScroll > 0f ? position / scrollBar.MaxScroll : 0f,
            });
        }

        public override void Update(Element parent = null)
        {
            var thisSize = this.GetSize().AsXna;
            scrollBar.Set<PositionTrait>(new TVVector(thisSize.X - 12, 0));
            scrollBar.Set<SizeTrait>(new TVVector(12, thisSize.Y));
            scrollBar.ContentSize = container.GetSize().Y;
            scrollBar.ViewportSize = thisSize.Y;
            base.Update(parent);
        }

        public override void Draw()
        {
            var size = this.GetSize();
            var position = this.GetActualPosition();
            var rect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
            Resources.StaticResources.DrawManager.SetScissor(rect);
            base.Draw();
            Resources.StaticResources.DrawManager.SetScissor(null);
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
