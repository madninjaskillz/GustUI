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

        /// <summary>Current scroll offset in content pixels, clamped to
        /// [0, max]. Setting it moves the viewport programmatically (e.g.
        /// "scroll to top" when repopulating) without going through the
        /// wheel/thumb-drag gesture path — same clamp <see cref="VerticalScrollbarElement.ScrollPosition"/>
        /// already enforces.</summary>
        public float ScrollPosition
        {
            get => scrollBar.ScrollPosition;
            set { scrollBar.ScrollPosition = value; ApplyScroll(scrollBar.ScrollPosition); }
        }

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

            // PushScissor/PopScissor, not the raw SetScissor this used to
            // call directly: since the geometry-renderer migration, geometry
            // draws (the vast majority of the UI now) clip against
            // DrawManager's scissorStack (GetClipRectForGeometry()), which
            // only PushScissor writes to — GraphicsDevice.ScissorRectangle
            // (what a bare SetScissor sets) is left unused by that backend.
            // A raw SetScissor here meant scrolled-out content never
            // actually got clipped for anything geometry-rendered — only
            // whatever was still SpriteBatch'd (chiefly text) respected it.
            // PushScissor also intersects with any enclosing clip and
            // applies RenderScale, matching how Element.ClipChildren does
            // this everywhere else in GustUI (Element.cs's DrawChildren).
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

        /// <summary>
        /// Removes every child and scrolls back to the top — what a rebuilt
        /// list (a palette, a property inspector, any panel repopulated on
        /// selection) needs.
        ///
        /// It exists because <see cref="AddChild"/> redirects into the inner
        /// container while <see cref="Element.Children"/> still reports THIS
        /// element's own children (the container and the scrollbar). A caller
        /// clearing what looks like "the list" therefore empties the wrong
        /// collection and blanks the whole control — so the safe operation has
        /// to live here, next to the redirect that causes the asymmetry.
        /// </summary>
        public void ClearChildren()
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
    }
}
