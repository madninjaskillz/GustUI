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
    /// <summary>Horizontal counterpart to <see cref="VerticalStackElement"/>
    /// — same shape, axes swapped: stacks children left-to-right, auto-sizes
    /// its own WIDTH from them (leaving height externally managed) rather
    /// than auto-sizing height. See VerticalStackElement's own doc comments
    /// for the reasoning behind each mechanism; kept as an independent class
    /// rather than a shared base with a Direction flag, matching this
    /// codebase's existing MenuBarElement/ToolbarElement precedent of
    /// separate-but-parallel classes over one parameterized one.</summary>
    public class HorizontalStackElement : FilledRectangleElement
    {
        /// <summary>Gap between stacked children (design-guide.md §3 — 12px
        /// baseline app-wide; override per stack if a denser/looser rhythm
        /// is deliberate for that surface). Zero by default so a stack built
        /// around pre-sized children (each already including its own
        /// trailing margin) doesn't shift under them.</summary>
        public float Spacing { get; set; } = 0f;

        /// <summary>Vertical gap between wrapped rows, when
        /// <see cref="WrapWidth"/> is set — independent of the horizontal
        /// <see cref="Spacing"/> between items on the same row.</summary>
        public float RowSpacing { get; set; } = 0f;

        /// <summary>When set, a child that would extend past this X wraps
        /// to a new row below instead — the classic CSS flex-wrap idiom
        /// (2026-08-18, user request: "if the toolbar would go outside of
        /// the window area, it appears below the fruit menu, not beside
        /// it"). Null (default) keeps the original single-row, auto-width
        /// behavior — every child packs onto one row regardless of how wide
        /// that makes the stack. Set this to the actual available width
        /// (e.g. a host's current width minus wherever this stack starts)
        /// and update it every frame if that can change — a changed value
        /// is detected the same way a child add/remove is (see Update())
        /// and re-triggers layout. Never wraps the FIRST item on a row: a
        /// single child wider than WrapWidth is left to overflow its own
        /// row rather than wrap against nothing and produce an infinite
        /// blank row.</summary>
        public float? WrapWidth { get; set; }

        /// <summary>The Children.Items list instance RecalculatePositions
        /// last laid out against — TVElements hands out a freshly-rebuilt
        /// list (a new object) any time membership or Depth-sort changes
        /// (Add/Remove/InvalidateSort all null its own cache), so a
        /// reference-inequality check is a cheap, exact "did the child set
        /// change since I last looked" test with no extra bookkeeping.
        /// Needed because AddChild/AddChildElement only recalculate on the
        /// way IN — a child leaving via the universal Element.Kill() path
        /// (which removes straight from Parent.Children, bypassing this
        /// class entirely) would otherwise leave a permanent gap where it
        /// used to sit, and the stack would never shrink back down.</summary>
        private List<Element> lastLayoutItems;

        /// <summary>WrapWidth's own value last laid out against — same
        /// change-detection idiom as <see cref="lastLayoutItems"/>, since a
        /// live-resizing host (e.g. ToolbarElement recomputing its own
        /// available width every frame) needs this stack to re-wrap without
        /// an add/remove ever happening.</summary>
        private float? lastWrapWidth;

        public HorizontalStackElement()
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
        /// changes (e.g. a TextElement settling its real measured width
        /// after its font/text is set, or any child resizing later) —
        /// event-driven via Trait's ValueChangedEventHandler, so a size
        /// change is reflected the same frame it happens rather than lagging
        /// behind a poll.</summary>
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

            if (!ReferenceEquals(this.Children.Items, lastLayoutItems) || WrapWidth != lastWrapWidth)
            {
                RecalculatePositions();
            }
        }

        private void RecalculatePositions()
        {
            var items = this.Children.Items;
            float currentX = 0f;
            float currentY = 0f;
            float rowHeight = 0f;
            float widestRow = 0f;

            foreach (var child in items)
            {
                TVVector size = child.GetSize();

                if (WrapWidth.HasValue && currentX > 0f && currentX + size.X > WrapWidth.Value)
                {
                    widestRow = Math.Max(widestRow, currentX - Spacing);
                    currentX = 0f;
                    currentY += rowHeight + RowSpacing;
                    rowHeight = 0f;
                }

                child.Set<PositionTrait>(new TVVector(currentX, currentY));
                currentX += size.X + Spacing;
                rowHeight = Math.Max(rowHeight, size.Y);
            }

            if (items.Any())
            {
                currentX -= Spacing; // no trailing gap after the last child on a row
            }

            widestRow = Math.Max(widestRow, currentX);

            // Width/height are normally caller-set (or, when wrapping,
            // WrapWidth IS the caller-set width), but never SHRINK below
            // what content actually needs even if the caller forgot to set
            // one, set it too small, or a single over-wide child overflowed
            // its own row above (found the hard way live-testing this
            // class's first real callers: an under-sized stack doesn't just
            // clip visually — GustUI's hit-testing requires a PARENT to
            // itself geometrically contain the pointer before it ever
            // recurses into checking children, so part or all of a child
            // outside the stack's own reported bounds is silently
            // unclickable. See VerticalStackElement's identical fix/comment
            // for its own axis.
            float width = WrapWidth.HasValue
                ? Math.Max(WrapWidth.Value, widestRow)
                : Math.Max(widestRow, this.GetSize().X);
            float height = Math.Max(currentY + rowHeight, this.GetSize().Y);
            this.Set<SizeTrait>(new TVVector(width, height));
            lastLayoutItems = items;
            lastWrapWidth = WrapWidth;
        }
    }
}
