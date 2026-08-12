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
        /// <summary>Gap between stacked children (design-guide.md §3 — 12px
        /// baseline app-wide; override per stack if a denser/looser rhythm
        /// is deliberate for that surface). Zero by default so existing
        /// callers built around a zero-gap stack (each child pre-sized to
        /// include its own bottom margin) don't shift under them.</summary>
        public float Spacing { get; set; } = 0f;

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
            Subscribe(element);
            RecalculatePositions();
        }

        public override void AddChild(Element child, string name)
        {
            base.AddChild(child, name);
            Subscribe(child);
            RecalculatePositions();
        }

        /// <summary>Recalculates whenever a stacked child's OWN size
        /// changes (e.g. a TextElement settling its real wrapped height
        /// after its font/width is set, or any child resizing later) —
        /// event-driven via Trait's ValueChangedEventHandler, replacing a
        /// previous once-a-second poll that could show a stale, overlapping
        /// layout for up to a second after content changed.</summary>
        private void Subscribe(Element child)
        {
            child.ElementTrait<SizeTrait>().ValueChangedEventHandler += (s, e) => RecalculatePositions();
        }

        private void RecalculatePositions()
        {
            var currentY = 0f;
            var items = this.Children.Items;
            foreach (var child in items)
            {
                child.Set<PositionTrait>(new TVVector(0, currentY));
                currentY += child.GetSize().Y + Spacing;
            }

            if (items.Any())
            {
                currentY -= Spacing; // no trailing gap after the last child
            }

            this.Set<SizeTrait>(new TVVector(this.GetSize().X, currentY));
        }
    }
}
