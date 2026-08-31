using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;

namespace GustUI.Elements
{
    /// <summary>
    /// A button with a second, narrower segment on its right edge: the main
    /// area runs the DEFAULT action straight away, and the segment — a hard
    /// divider and a down chevron — opens the rest of them.
    ///
    /// The point is that the common case costs one click. "New project" is the
    /// motivating example: an empty project is what almost everyone wants, so
    /// pressing the button should just make one, while the templates stay one
    /// click away behind the chevron instead of standing between the user and
    /// the thing they came for.
    ///
    /// It IS a <see cref="BasicButtonElement"/>, not a lookalike, so it drops
    /// into anything that takes one (<see cref="ModalWindowElement"/>'s button
    /// row, for instance) and inherits hover/press fills, the Enabled wash and
    /// the disabled behaviour unchanged.
    ///
    /// The segment is a real child element rather than a hit-test on the click
    /// X: that gets it its own hover and press states for free (TVSmartFill
    /// resolves per element), and lets it set <see cref="Element.SwallowsPointer"/>,
    /// which is what stops a click on the chevron ALSO firing the default
    /// action — GustUI dispatches a click to every hovered element in the
    /// chain, parent included, unless something swallows it.
    ///
    /// The chevron is <see cref="UIFont.Symbol.ChevronDownBold"/>, which had to
    /// be ADDED to that enum (2026-08-23) before this could use it: the enum is
    /// Microsoft's WinRT Symbol list, which has an Up and no Down, and only
    /// members of it get baked into the SDF atlas.
    /// </summary>
    public class SplitButtonElement : BasicButtonElement
    {
        /// <summary>Roughly square on a standard-height button, which is the
        /// proportion the affordance is usually drawn at.</summary>
        public const int DefaultArrowWidth = 32;

        private readonly ArrowSegment segment;
        private readonly int arrowWidth;

        /// <summary>Live-colour form — see BasicButtonElement's own overload
        /// and ezmuze bug board #66.</summary>
        public SplitButtonElement(
            string text,
            Func<Color> foreground,
            TVFill background,
            TVVector position = null,
            TVVector size = null,
            TVEvent<ClickEventArgs> onClick = null,
            TVEvent<ClickEventArgs> onMore = null,
            TVFill segmentFill = null,
            int arrowWidth = DefaultArrowWidth)
            : this(text, Color.White, background, position, size, onClick, onMore, segmentFill, arrowWidth)
        {
            Set<ForegroundColorTrait>(new TVColor(foreground));
        }

        public SplitButtonElement(
            string text,
            Color foreground,
            TVFill background,
            TVVector position = null,
            TVVector size = null,
            TVEvent<ClickEventArgs> onClick = null,
            TVEvent<ClickEventArgs> onMore = null,
            TVFill segmentFill = null,
            int arrowWidth = DefaultArrowWidth)
            : base(text, foreground, background, position, size, onClick)
        {
            this.arrowWidth = Math.Max(12, arrowWidth);

            // No more-menu means no segment: a chevron that opens nothing is
            // a promise the control can't keep, and without it this is simply
            // a normal button.
            if (onMore == null)
            {
                return;
            }

            segment = new ArrowSegment(this);

            // A translucent wash over whatever the button's own fill resolved
            // to, NOT an opaque colour: the button underneath may be a
            // gradient or a smart fill mid-crossfade, and the segment has to
            // read as part of the same control in every one of those states.
            segment.Set<BackgroundFillTrait>(segmentFill ?? new TVFillSolidColor(new Color(255, 255, 255, 26)));

            // Shields the button from the click (see the class doc) — without
            // this, opening the menu would also run the default action.
            segment.SwallowsPointer = true;

            segment.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>(x =>
            {
                if (Enabled)
                {
                    onMore.TriggerAction?.Invoke(x);
                }
            }));

            AddChild(segment, "split-arrow");
        }

        /// <summary>Where the divider falls, measured from the button's left
        /// edge — what a caller needs to place a popup under the segment
        /// rather than under the whole button.</summary>
        public float SegmentLeft => Math.Max(0, this.GetSize().X - arrowWidth);

        public override void Draw()
        {
            LayoutSegment();
            base.Draw();
        }

        /// <summary>
        /// Keeps the segment pinned to the right edge and the label centred in
        /// what's LEFT of the button.
        ///
        /// This runs per frame rather than once in the constructor because the
        /// base class syncs the label's size from the button's own — so on
        /// every resize the label is silently widened back to the full width,
        /// and its centred text drifts under the chevron. Each Set is guarded,
        /// so a button that isn't resizing writes no traits and raises no
        /// change events.
        /// </summary>
        private void LayoutSegment()
        {
            if (segment == null)
            {
                return;
            }

            TVVector size = this.GetSize();
            if (size.X <= 0 || size.Y <= 0)
            {
                return;
            }

            int width = Math.Min(arrowWidth, (int)size.X);
            float left = size.X - width;

            TVVector segmentPosition = segment.ElementTrait<PositionTrait>().Value();
            if (Math.Abs(segmentPosition.X - left) > 0.5f || Math.Abs(segmentPosition.Y) > 0.5f)
            {
                segment.Set<PositionTrait>(new TVVector(left, 0));
            }

            TVVector segmentSize = segment.GetSize();
            if (Math.Abs(segmentSize.X - width) > 0.5f || Math.Abs(segmentSize.Y - size.Y) > 0.5f)
            {
                segment.Set<SizeTrait>(new TVVector(width, size.Y));
            }

            if (Label != null && Math.Abs(Label.GetSize().X - left) > 0.5f)
            {
                Label.Set<SizeTrait>(new TVVector(left, size.Y));
            }
        }

        /// <summary>
        /// The chevron half. Drawn as a CHILD so it lands in the normal child
        /// draw pass — which puts it under the parent's disabled wash, where a
        /// chevron painted after <c>base.Draw()</c> would sit on top of it and
        /// stay bright on a disabled button.
        /// </summary>
        private sealed class ArrowSegment : FilledRectangleElement
        {
            private readonly SplitButtonElement owner;
            private readonly TextElement chevron;

            internal ArrowSegment(SplitButtonElement owner)
            {
                this.owner = owner;

                // UIFont.Symbol.ChevronDownBold (E96E, added 2026-08-23 —
                // the enum had an Up and no Down, and no chevron at all).
                // The heavier of the two chevron weights: at button size the
                // light one (ChevronDown, E70D) reads as a hairline.
                chevron = this.AddChildElement<TextElement>();
                chevron.Set<TextTrait>(new TVText(UIFont.Symbol.ChevronDownBold.Icon()));
                chevron.Set<ForegroundColorTrait>(new TVColor(owner.ElementTrait<ForegroundColorTrait>().Value().AsXna));
                chevron.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment { Alignment = HorizontalAlignment.Center });
                chevron.Set<VerticalAlignmentTrait>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });
            }

            public override void Draw()
            {
                Vector2 size = this.GetSize().AsXna;
                if (size.X <= 0 || size.Y <= 0)
                {
                    base.Draw();
                    return;
                }

                LayoutChevron(size);
                base.Draw();

                // The divider, inset top and bottom so it reads as a seam
                // inside the button rather than as the button's own border.
                // Drawn AFTER the fill (which base.Draw paints) and before
                // nothing else cares — the chevron is a child element and has
                // already been composited by the time this returns.
                Vector2 position = this.GetActualXnaPosition();
                float inset = Math.Max(4f, size.Y * 0.22f);
                Resources.StaticResources.DrawManager.DrawFilledRectangle(
                    new Rectangle(
                        (int)position.X,
                        (int)(position.Y + inset),
                        1,
                        Math.Max(1, (int)(size.Y - inset * 2))),
                    owner.ElementTrait<ForegroundColorTrait>().Value().AsXna * 0.45f);
            }

            /// <summary>Sizes the glyph from the segment rather than pinning it
            /// to a constant, so a taller or narrower button still gets a
            /// chevron in proportion. Guarded, like the outer layout, so a
            /// static button writes no traits per frame.</summary>
            private void LayoutChevron(Vector2 size)
            {
                int glyphSize = Math.Max(8, (int)Math.Round(Math.Min(size.X, size.Y) * 0.5f));

                TVFont font = chevron.ElementTrait<FontTrait>().Value();
                if (font == null || font.Size != glyphSize)
                {
                    chevron.Set<FontTrait>(new TVFont
                    {
                        Family = Resources.StaticResources.Theme.SymbolFont.Family,
                        Size = glyphSize,
                        Border = 0,
                    });
                }

                TVVector current = chevron.GetSize();
                if (Math.Abs(current.X - size.X) > 0.5f || Math.Abs(current.Y - size.Y) > 0.5f)
                {
                    chevron.Set<PositionTrait>(new TVVector(0, 0));
                    chevron.Set<SizeTrait>(new TVVector(size.X, size.Y));
                }
            }
        }
    }
}
