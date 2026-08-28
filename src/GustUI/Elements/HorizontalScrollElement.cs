using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;

namespace GustUI.Elements
{
    /// <summary>
    /// A scissored viewport over a WIDER content container, scrolled by
    /// <see cref="HorizontalScrollbarElement"/> (thumb drag / track paging) and
    /// the mouse wheel. Children added to this element land in the inner
    /// container, whose width follows its children
    /// (<see cref="Element.SizeFitsChildren"/>).
    ///
    /// The missing half of a pair. <see cref="VerticalScrollElement"/> has
    /// existed for as long as anything needed a scrolling list, and both
    /// scrollBARS have existed as long as that — but a horizontal CONTAINER
    /// never did, so every rail that ran out of room grew its own: an offset
    /// field, a reposition loop and a hand-wired bar, once per rail, each with
    /// its own answer for clipping and the wheel. This is that, once.
    /// </summary>
    [ElementTraits(typeof(OnScrollTrait), typeof(OnScrollWheelChanged))]
    public class HorizontalScrollElement : FilledRectangleElement
    {
        /// <summary>Content pixels scrolled per wheel notch (120 raw delta).</summary>
        public float WheelStep { get; set; } = 64f;

        /// <summary>
        /// How tall the scrollbar is, and therefore how much of the bottom edge
        /// content must leave clear.
        ///
        /// The bar is drawn OVER the content rather than beneath it, so a child
        /// laid out to the full height loses its last twelve pixels — which on
        /// a bordered box reads as the bottom edge simply missing. Named here
        /// for the reason its vertical twin is: the answer belongs to the
        /// scroll element, and a caller guessing 12 is a caller that gets it
        /// wrong the day this changes.
        /// </summary>
        public const float ScrollbarHeight = 12f;

        /// <inheritdoc cref="VerticalScrollElement.ScrollPosition"/>
        public float ScrollPosition
        {
            get => scrollBar.ScrollPosition;
            set { scrollBar.ScrollPosition = value; ApplyScroll(scrollBar.ScrollPosition); }
        }

        /// <summary>Whether the content is wider than the viewport — what a
        /// caller needs to decide whether to leave room for the bar at all.</summary>
        public bool CanScroll => scrollBar.MaxScroll > 0f;

        private readonly RectangleElement container = new RectangleElement();
        private readonly HorizontalScrollbarElement scrollBar;

        public HorizontalScrollElement()
        {
            scrollBar = new HorizontalScrollbarElement();
            scrollBar.OnUserScroll = ApplyScroll;

            base.AddChild(container, "container");
            base.AddChild(scrollBar, "scrollBar");
            container.SizeFitsChildren = true;

            this.Set<OnScrollWheelChanged>(new TVEvent<ScrollEventArgs>(HandleWheel));
        }

        /// <summary>
        /// The wheel scrolls SIDEWAYS here, with no modifier.
        ///
        /// A horizontal rail is the one place where that is what somebody
        /// means: there is nothing to scroll vertically, so requiring Shift
        /// would make the plain gesture do nothing at all.
        /// </summary>
        private void HandleWheel(ScrollEventArgs args)
        {
            // ScrollWheelDelta = previous - current, so wheel-up is negative.
            scrollBar.ScrollPosition += args.ScrollWheelDelta / 120f * WheelStep;
            ApplyScroll(scrollBar.ScrollPosition);
        }

        private void ApplyScroll(float position)
        {
            container.Set<PositionTrait>(new TVVector(-position, 0));

            ElementTrait<OnScrollTrait>().Value()?.TriggerAction?.Invoke(new ScrollEventArgs
            {
                ScrollPosition = position,
                ScrollPercentage = scrollBar.MaxScroll > 0f ? position / scrollBar.MaxScroll : 0f,
            });
        }

        public override void Update(Element parent = null)
        {
            var thisSize = this.GetSize().AsXna;
            scrollBar.Set<PositionTrait>(new TVVector(0, thisSize.Y - ScrollbarHeight));
            scrollBar.Set<SizeTrait>(new TVVector(thisSize.X, ScrollbarHeight));
            scrollBar.ContentSize = container.GetSize().X;
            scrollBar.ViewportSize = thisSize.X;

            // Nothing to scroll, nothing to draw: a bar pinned at full width
            // across a rail that fits is chrome saying "you have seen it all",
            // which is what the absence of a bar already says.
            scrollBar.Opacity = scrollBar.MaxScroll > 0f ? 1f : 0f;
            base.Update(parent);
        }

        public override void Draw()
        {
            var size = this.GetSize();
            var position = this.GetActualPosition();
            var rect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);

            // PushScissor/PopScissor rather than a raw SetScissor, for the
            // reason VerticalScrollElement.Draw documents at length: geometry
            // draws clip against DrawManager's scissor stack, and only
            // PushScissor writes to it.
            Resources.StaticResources.DrawManager.PushScissor(rect);
            base.Draw();
            Resources.StaticResources.DrawManager.PopScissor();
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

        /// <inheritdoc cref="VerticalScrollElement.ClearChildren"/>
        public override void ClearChildren()
        {
            var items = new System.Collections.Generic.List<Element>(container.Children.Items);
            foreach (Element child in items)
            {
                child.Kill();
            }

            container.Children.InvalidateSort();
            scrollBar.ScrollPosition = 0f;
            ApplyScroll(0f);
        }

        /// <summary>The content children (the inner container's), as opposed
        /// to <see cref="Element.Children"/>'s container + scrollbar.</summary>
        public TVElements ContentChildren => container.Children;

        /// <inheritdoc cref="VerticalScrollElement.ScrollbarOpacity"/>
        public float ScrollbarOpacity
        {
            get => scrollBar.Opacity;
            set => scrollBar.Opacity = value;
        }
    }
}
