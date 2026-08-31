using GustUI.Extensions;
using Microsoft.Xna.Framework;
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

        /// <summary>The Children.Items list instance RecalculatePositions
        /// last laid out against — TVElements hands out a freshly-rebuilt
        /// list (a new object) any time membership or Depth-sort changes
        /// (Add/Remove/InvalidateSort all null its own cache), so a
        /// reference-inequality check is a cheap, exact "did the child set
        /// change since I last looked" test with no extra bookkeeping.
        /// Needed because AddChild/AddChildElement only recalculate on the
        /// way IN — a child leaving via the universal Element.Kill() path
        /// (which removes straight from Parent.Children, bypassing this
        /// class entirely) previously left a permanent gap where it used to
        /// sit, and the stack never shrank back down.</summary>
        private List<Element> lastLayoutItems;

        /// <summary>The child sizes <see cref="RecalculatePositions"/> last
        /// laid out against, parallel to <see cref="lastLayoutItems"/>.
        ///
        /// Needed because the <see cref="Subscribe"/> event below only fires
        /// for a child that WRITES its SizeTrait, and a whole class of child
        /// never does: one using <see cref="Element.SizeFitsChildren"/>
        /// computes its size live inside <c>GetSize()</c> and leaves the trait
        /// untouched for ever (see ElementExtensions.GetSize, and
        /// InputManager's own note on the same asymmetry). Such a child can
        /// grow by hundreds of pixels without this stack hearing a thing.
        ///
        /// That was ezmuze bug #29: the welcome screen's whole body sits in a
        /// SizeFitsChildren container, its "What's new" list fills in when the
        /// change log finishes loading a moment after the modal is built, and
        /// the stack above it went on reporting the height it had before that
        /// arrived. The modal sized itself from the stale number, so the demo
        /// grid hung out of the bottom of the dialog, unclipped -- and the
        /// modal's own "have I overflowed?" check read the same stale number,
        /// so it never grew a scrollbar either.</summary>
        private readonly List<Vector2> lastChildSizes = new List<Vector2>();

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

        /// <summary>Catches a child leaving via Kill() (or any other removal
        /// path that doesn't route through this class's own AddChild/
        /// AddChildElement) — see <see cref="lastLayoutItems"/>'s own doc
        /// comment. A poll, not another event hook: nothing on the removal
        /// side (Element.Kill / TVElements.Remove) offers one to subscribe
        /// to.</summary>
        public override void Update(Element parent = null)
        {
            base.Update(parent);

            if (!ReferenceEquals(this.Children.Items, lastLayoutItems) || ChildSizesChanged())
            {
                RecalculatePositions();
            }
        }

        /// <summary>Whether any child is a different size than it was when
        /// this stack last laid out -- the poll that catches the children
        /// <see cref="Subscribe"/> cannot hear from (see
        /// <see cref="lastChildSizes"/>).
        ///
        /// Compares every child rather than just the total height: two
        /// children changing by equal and opposite amounts in one frame leaves
        /// the total identical while everything below the first one is in the
        /// wrong place.</summary>
        private bool ChildSizesChanged()
        {
            List<Element> items = this.Children.Items;
            if (items.Count != lastChildSizes.Count)
            {
                return true;
            }

            for (int i = 0; i < items.Count; i++)
            {
                TVVector size = items[i].GetSize();
                if (size.X != lastChildSizes[i].X || size.Y != lastChildSizes[i].Y)
                {
                    return true;
                }
            }

            return false;
        }

        private void RecalculatePositions()
        {
            var currentY = 0f;
            var maxWidth = 0f;
            var items = this.Children.Items;
            lastChildSizes.Clear();
            foreach (var child in items)
            {
                // Only WRITE a position that actually moved. This runs from
                // Update now, so it can run every frame while something in the
                // stack is still settling, and a trait write fires listeners
                // whether or not the value changed.
                TVVector at = child.GetRelativePosition();
                if (at.X != 0f || at.Y != currentY)
                {
                    child.Set<PositionTrait>(new TVVector(0, currentY));
                }

                TVVector size = child.GetSize();
                lastChildSizes.Add(new Vector2(size.X, size.Y));
                currentY += size.Y + Spacing;
                maxWidth = Math.Max(maxWidth, size.X);
            }

            if (items.Any())
            {
                currentY -= Spacing; // no trailing gap after the last child
            }

            // Width is normally caller-set (a stack of full-width rows), but
            // never SHRINKS below the widest child even if the caller forgot
            // to set one or set it too narrow (2026-08-18, found the hard
            // way building HorizontalStackElement: GustUI's hit-testing
            // requires a PARENT to itself geometrically contain the pointer
            // before it ever recurses into checking children — see
            // Managers/InputManager.cs's CollectHovered — so an under-sized
            // stack doesn't just clip visually, it makes every child
            // silently unclickable, not merely a cosmetic default).
            float width = Math.Max(maxWidth, this.GetSize().X);
            TVVector own = this.GetSize();
            if (own.X != width || own.Y != currentY)
            {
                // Same guard, and it matters more here: this write is what
                // notifies a PARENT stack, so an unguarded one every frame
                // would walk the whole ancestor chain for no change.
                this.Set<SizeTrait>(new TVVector(width, currentY));
            }

            lastLayoutItems = items;
        }
    }
}
