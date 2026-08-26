using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;

namespace GustUI.Elements
{
    /// <summary>
    /// A per-modal toolbar strip — the standardized counterpart to
    /// <see cref="MenuBarElement"/> for arbitrary transport/action content
    /// (icon buttons, sliders, live-updating text) rather than a declarative
    /// menu-item list. Unlike MenuBarElement this isn't handed a model to
    /// render; a host just adds children to it directly (the same way views
    /// already add children to their modal), since toolbar content varies
    /// too widely per view to standardize as data. Sits directly below the
    /// per-modal menu bar's OWN row, right after its last item, when one
    /// exists — or starting at x=0 right under the title bar when it
    /// doesn't — see ModalWindowElement/FullScreenModalElement's own
    /// EnsureToolbar/ContentTop. Totally optional: a view that never calls
    /// EnsureToolbar gets no strip and no reserved space, exactly like
    /// SetMenu today.
    /// </summary>
    public class ToolbarElement : FilledRectangleElement
    {
        /// <summary>
        /// Bar height in pixels — content below the bar starts here.
        ///
        /// The SAME height as the menu bar it sits beside, and derived from it
        /// so the two cannot drift: a toolbar taller than the menu makes one
        /// row that is really two, with the menu labels floating in the middle
        /// of it. It was 40 against the menu's 28 when both were sized for a
        /// touch target.
        /// </summary>
        public const int BarHeight = MenuBarElement.BarHeight;

        private readonly Element host;

        public ToolbarElement(Element host)
        {
            this.host = host;
            Set<PositionTrait>(new TVVector(0, 0));
            Set<SizeTrait>(new TVVector(host.GetSize().X, BarHeight));
            // Transparent: the host modal's chrome-row strip paints the
            // shared SurfaceHeader background (see MenuBarElement's ctor
            // comment — one container background, no per-bar seams).
            Set<BackgroundFillTrait>(new TVFillSolidColor(Microsoft.Xna.Framework.Color.Transparent));
        }

        /// <summary>How much width this toolbar's own CONTENT actually
        /// needs — the rightmost edge of any child, in this element's own
        /// local coordinate space. Deliberately NOT the same thing as
        /// <see cref="SizeTrait"/> (which always stretches to fill whatever
        /// space is left to the host's right edge, by design — see
        /// Update()): a host arranging this toolbar in a wrapping row (e.g.
        /// ModalWindowElement/FullScreenModalElement's own EnsureToolbar)
        /// needs to know the SMALL, natural amount of room the content needs
        /// to decide whether it fits, not the artificially-stretched
        /// SizeTrait — using SizeTrait for that check is self-defeating,
        /// since "fill whatever's left" trivially always "fits" whatever's
        /// left by definition.</summary>
        public float ContentWidth
        {
            get
            {
                float rightmost = 0f;
                foreach (Element child in Children.Items)
                {
                    TVVector pos = child.ElementTrait<PositionTrait>().Value();
                    TVVector size = child.GetSize();
                    rightmost = System.Math.Max(rightmost, pos.X + size.X);
                }

                return rightmost;
            }
        }

        public override void Update(Element parent = null)
        {
            base.Update(parent);

            // Follows the host modal's current width every frame — same
            // idiom as MenuBarElement.Update()'s own width-tracking. Own X
            // is subtracted so this still ends flush with the host's right
            // edge when sharing a row with a menu bar (positioned starting
            // partway across, at the menu bar's ContentWidth, not at 0).
            float width = host.GetSize().X - ElementTrait<PositionTrait>().Value().X;
            if (ElementTrait<SizeTrait>().Value().X != width)
            {
                Set<SizeTrait>(new TVVector(width, BarHeight));
            }
        }
    }
}
