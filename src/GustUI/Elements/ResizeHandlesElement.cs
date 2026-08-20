using System;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GustUI.Elements
{
    /// <summary>
    /// Free-form drag-to-resize for a <see cref="ModalWindowElement"/>
    /// (2026-08-16) — 8 invisible strips (4 edges + 4 corners, corners
    /// drawn "on top" of the edges within their own zone) added as a child
    /// of the host modal, only when it opts in (<c>resizable: true</c>).
    ///
    /// GustUI has no OS cursor-shape API at all (checked; nothing exists),
    /// so a hover highlight is the affordance instead of a resize cursor —
    /// not a fallback from something better, the only option available.
    ///
    /// Continuation is driven by unconditional per-frame polling in
    /// <see cref="Update"/> — exactly <see cref="Element.BeingDragged"/>'s
    /// own shape (a bool flag checked every frame against
    /// <c>InputManager.CurrentMouseState</c>, regardless of hover) — NOT
    /// <c>CapturePointer</c>/<c>OnMouseButtonHeldDown</c>, the first version
    /// of this file used. That looked like the right idiom (it's what
    /// Slider/XYPad/Knob use) but broke the moment the user actually tried
    /// it: those elements never reposition themselves mid-drag, so their
    /// own hitbox staying under a captured pointer is a given; a resize
    /// handle's hitbox constantly MOVES as the host resizes (this class's
    /// own <see cref="Update"/> repositions all 8 every frame from the
    /// host's live size), and dragging fast enough for the cursor to
    /// outrun that moving hitbox visibly broke tracking — title-bar drag
    /// has no such problem since it never depends on where the dragged
    /// element itself currently is. Found 2026-08-17 from the user's own
    /// live test, not caught by any structural check beforehand.
    ///
    /// The anchor-based edge math itself (record the host's Position+Size
    /// once at press, recompute fresh from that anchor every frame rather
    /// than accumulating per-frame deltas) is unrelated to the above and
    /// unchanged — still the right way to avoid drift, independent of
    /// how continuation gets driven.
    /// </summary>
    internal class ResizeHandlesElement : FilledRectangleElement
    {
        private const float EdgeThickness = 6f;
        private const float CornerSize = 14f;

        private static Color HoverHighlight => Resources.StaticResources.Theme.AccentSelection * 0.55f;

        /// <summary>The classic Windows "size grip" dot-staircase, drawn
        /// permanently (not just on hover) in the SE corner only — matches
        /// the one corner real desktop apps mark this way. 2026-08-17,
        /// added after the user pointed out the resize corners are
        /// invisible until you happen to hover them, with no hint
        /// beforehand that anything is there — there's no resize-grip
        /// glyph in either icon font (checked), so this is drawn directly
        /// rather than as a text-trait icon like the title bar's buttons.</summary>
        // BodyText (near-white in dark theme), not SurfaceBorder — found
        // from the user's own screenshot: SurfaceBorder (60,60,74 in dark
        // theme) was barely perceptible at 2px dot size against a near-
        // black background, essentially invisible in practice even though
        // it WAS technically drawing. Muted via alpha rather than using a
        // flat mid-grey so it still reads as "quiet chrome," not a stray
        // bright artifact.
        private static Color GripDotColor => Resources.StaticResources.Theme.BodyText * 0.55f;

        private const float GripDotSize = 3f;
        private const float GripDotSpacing = 4f;
        private const float GripInset = 3f;

        private enum Handle
        {
            N,
            S,
            E,
            W,
            NE,
            NW,
            SE,
            SW,
        }

        private readonly ModalWindowElement host;
        private readonly FilledRectangleElement[] handles = new FilledRectangleElement[8];

        /// <summary>True while an edge/corner is actively being dragged —
        /// exposed (via <see cref="ModalWindowElement.IsResizing"/>) so a
        /// host application can defer anything that would tear down and
        /// reconstruct this element mid-gesture (a fresh instance has no
        /// <see cref="activeHandle"/>, silently ending the drag).</summary>
        public bool IsActive => activeHandle != null;

        private Handle? activeHandle;
        private Vector2 anchorMouse;
        private Vector2 anchorPosition;
        private Vector2 anchorSize;

        public ResizeHandlesElement(ModalWindowElement host)
        {
            this.host = host;
            Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));

            foreach (Handle h in Enum.GetValues(typeof(Handle)))
            {
                Handle captured = h;
                var handle = new FilledRectangleElement(0, 0, 0, 0, new TVFillSolidColor(Color.Transparent));
                // Only starts the drag (still legitimately hover-gated —
                // same as the title bar requiring an initial click ON the
                // drag bar). Continuing/ending the drag once started is
                // Update()'s job now, not this element's — see the class
                // doc comment for why.
                handle.ElementTrait<OnMousePress>().Set(new TVEvent<ClickEventArgs>(args => BeginResize(captured, args)));
                // OnEnterTrait isn't part of RectangleElement's own base
                // trait set (unlike OnExitTrait/OnMousePress above, which
                // are — confirmed the hard way, a KeyNotFoundException on
                // first launch) — AddTrait is the runtime opt-in for a
                // trait not declared via a class-level
                // [ElementTraits(...)] attribute, same idiom
                // LoopBrowserPanel's own scroll lists already use for
                // OnScrollWheelChanged.
                handle.AddTrait<OnEnterTrait>().Set(new TVEvent<ClickEventArgs>(_ =>
                    handle.Set<BackgroundFillTrait>(new TVFillSolidColor(HoverHighlight))));
                handle.ElementTrait<OnExitTrait>().Set(new TVEvent<ClickEventArgs>(_ =>
                {
                    if (activeHandle != captured)
                    {
                        handle.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
                    }
                }));
                // Corners sit above the edges (added after) within this
                // element's own local children — the corner zone geometry
                // below never actually overlaps an edge strip, but this
                // keeps draw/hit-test order matching intent regardless.
                AddChild(handle, h.ToString() + "-resize-handle");
                handles[(int)h] = handle;
            }
        }

        public override void Draw()
        {
            base.Draw();

            // Docked geometry is owned entirely by ModalWindowElement's own
            // DockTo/LayoutDocked (GustUI/Managers/DockLayout.cs) — Update()
            // below collapses every handle to zero size while docked (so
            // none of them hit-test at all), which also means the SE
            // handle's own position goes stale; skip the grip dots too
            // rather than draw a discoverability hint for a gesture that
            // can't do anything right now.
            if (host.DockedSide != DockSide.None)
            {
                return;
            }

            Element se = handles[(int)Handle.SE];
            Vector2 pos = se.GetActualXnaPosition();
            TVVector seSize = se.ElementTrait<SizeTrait>().Value();
            float rightEdge = pos.X + seSize.X - GripInset;
            float bottomEdge = pos.Y + seSize.Y - GripInset;
            var manager = Resources.StaticResources.DrawManager;

            // Classic Windows size-grip: a 1-2-3 diagonal staircase of dots
            // anchored to the corner, always drawn (not hover-gated) — the
            // discoverability hint itself, not a decoration on top of one.
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col <= row; col++)
                {
                    float dotX = rightEdge - col * GripDotSpacing;
                    float dotY = bottomEdge - row * GripDotSpacing;
                    manager.DrawFilledRectangle(
                        new Rectangle((int)(dotX - GripDotSize / 2f), (int)(dotY - GripDotSize / 2f), (int)GripDotSize, (int)GripDotSize),
                        GripDotColor);
                }
            }
        }

        public override void Update(Element parent = null)
        {
            base.Update(parent);

            // Docked POSITION is owned entirely by DockTo/LayoutDocked
            // (ModalWindowElement.cs, GustUI/Managers/DockLayout.cs) — the
            // other 7 handles would just get overwritten again the very
            // next frame (2026-08-17, user report against the docked-by-
            // default loop browser/wave bank panels: "looks like the
            // handles do nothing"), so those collapse to zero size while
            // docked. The ONE edge facing the content area (the boundary
            // between this panel and whatever's sharing space with it) is
            // still live, though — the docking-reinforcement follow-up
            // (2026-08-17): dragging it resizes ONLY this panel's own
            // width/height along the dock axis (never its position — that's
            // still LayoutDocked's job every frame), and DockLayout reads
            // that size live, so the window(s) sharing space with it
            // shrink/grow in the same frame with no extra plumbing.
            if (host.DockedSide != DockSide.None)
            {
                UpdateDockedSplitter();
                return;
            }

            TVVector size = host.GetSize();
            float w = size.X;
            float h = size.Y;
            Set<SizeTrait>(new TVVector(w, h));

            PositionHandle(Handle.NW, 0, 0, CornerSize, CornerSize);
            PositionHandle(Handle.NE, w - CornerSize, 0, CornerSize, CornerSize);
            PositionHandle(Handle.SW, 0, h - CornerSize, CornerSize, CornerSize);
            PositionHandle(Handle.SE, w - CornerSize, h - CornerSize, CornerSize, CornerSize);

            PositionHandle(Handle.N, CornerSize, 0, w - CornerSize * 2, EdgeThickness);
            PositionHandle(Handle.S, CornerSize, h - EdgeThickness, w - CornerSize * 2, EdgeThickness);
            PositionHandle(Handle.W, 0, CornerSize, EdgeThickness, h - CornerSize * 2);
            PositionHandle(Handle.E, w - EdgeThickness, CornerSize, EdgeThickness, h - CornerSize * 2);

            // Unconditional per-frame poll — Element.cs's own BeingDragged
            // shape (see the class doc comment) — so the drag keeps
            // tracking the mouse no matter how far outside any handle's
            // own (constantly moving) hitbox the cursor ends up.
            if (activeHandle != null)
            {
                MouseState mouse = Resources.StaticResources.InputManager.CurrentMouseState;
                if (mouse.LeftButton == ButtonState.Released)
                {
                    EndResize();
                }
                else
                {
                    ContinueResize(new Vector2(mouse.X, mouse.Y));
                }
            }
        }

        private void PositionHandle(Handle handle, float x, float y, float w, float h)
        {
            Element element = handles[(int)handle];
            element.Set<PositionTrait>(new TVVector(x, y));
            element.Set<SizeTrait>(new TVVector(Math.Max(0f, w), Math.Max(0f, h)));
        }

        /// <summary>Which of the 8 handles sits on a docked panel's own
        /// content-facing edge — e.g. a Left-docked panel's resize splitter
        /// is its RIGHT edge (that's the boundary with whatever's sharing
        /// space with it); a Bottom-docked panel's is its TOP edge.</summary>
        private static Handle SplitterHandleFor(DockSide side)
        {
            switch (side)
            {
                case DockSide.Left: return Handle.E;
                case DockSide.Right: return Handle.W;
                case DockSide.Top: return Handle.S;
                default: return Handle.N; // Bottom
            }
        }

        /// <summary>Docked counterpart to the floating branch below —
        /// collapses every handle except the one on the content-facing
        /// edge, spans that one the full length of the OTHER axis (a thin
        /// strip the full height for Left/Right, full width for Top/
        /// Bottom), and drives the SAME activeHandle/anchor drag machinery
        /// the floating handles use, just routed to <see cref="ContinueDockedResize"/>
        /// instead of the free-form edge math (this panel's position is
        /// never touched — LayoutDocked owns that, every frame,
        /// unconditionally).</summary>
        private void UpdateDockedSplitter()
        {
            TVVector size = host.GetSize();
            float w = size.X;
            float h = size.Y;
            Handle splitter = SplitterHandleFor(host.DockedSide);

            for (int i = 0; i < handles.Length; i++)
            {
                if ((Handle)i != splitter)
                {
                    handles[i].Set<SizeTrait>(new TVVector(0, 0));
                }
            }

            switch (splitter)
            {
                case Handle.E:
                    PositionHandle(Handle.E, w - EdgeThickness, 0, EdgeThickness, h);
                    break;
                case Handle.W:
                    PositionHandle(Handle.W, 0, 0, EdgeThickness, h);
                    break;
                case Handle.S:
                    PositionHandle(Handle.S, 0, h - EdgeThickness, w, EdgeThickness);
                    break;
                default: // N
                    PositionHandle(Handle.N, 0, 0, w, EdgeThickness);
                    break;
            }

            // Drop a drag left mid-flight if this panel stopped being
            // docked (or changed which side) out from under it — defensive,
            // the only path into/out of docked state is the title bar's own
            // drag/hold gesture, a different interaction from this one, so
            // the two shouldn't normally overlap.
            if (activeHandle != null && activeHandle != splitter)
            {
                EndResize();
            }

            if (activeHandle != null)
            {
                MouseState mouse = Resources.StaticResources.InputManager.CurrentMouseState;
                if (mouse.LeftButton == ButtonState.Released)
                {
                    EndResize();
                }
                else
                {
                    ContinueDockedResize(new Vector2(mouse.X, mouse.Y));
                }
            }
        }

        /// <summary>Resizes ONLY this panel's own width (Left/Right dock) or
        /// height (Top/Bottom dock) from the drag delta along that one
        /// axis — never its position. Clamped between <see cref="ModalWindowElement.MinSize"/>
        /// and DockLayout's own 50%-of-window reservation cap (see its
        /// class doc comment) so the splitter can't drag a panel past the
        /// same ceiling <see cref="Managers.DockLayout"/> would clip it to
        /// anyway — without this, dragging past the cap would look like it
        /// worked (the panel itself grows) while the window(s) sharing
        /// space with it stop shrinking to match, an inch-worm mismatch.</summary>
        private void ContinueDockedResize(Vector2 currentMouse)
        {
            bool horizontal = host.DockedSide == DockSide.Left || host.DockedSide == DockSide.Right;
            float delta = horizontal ? currentMouse.X - anchorMouse.X : currentMouse.Y - anchorMouse.Y;
            if (host.DockedSide == DockSide.Right || host.DockedSide == DockSide.Bottom)
            {
                delta = -delta;
            }

            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            float minDim = horizontal ? host.MinSize.X : host.MinSize.Y;
            // Math.Max, not a bare 50%-of-window cap: a window narrower
            // than 2x this panel's own MinSize would otherwise make
            // max < min and Math.Clamp throw — MinSize wins in that squeeze
            // (same "never below usable min" call FillsAvailableSpace's own
            // clamp already makes), same as the free-floating resize handles'
            // own min-size clamp already allows briefly exceeding a nominal
            // ceiling rather than ever going unusable.
            float cap = Math.Max(minDim, 0.5f * (horizontal ? windowSize.X : windowSize.Y));
            float anchorDim = horizontal ? anchorSize.X : anchorSize.Y;
            float newDim = Math.Clamp(anchorDim + delta, minDim, cap);

            host.Set<SizeTrait>(horizontal
                ? new TVVector(newDim, anchorSize.Y)
                : new TVVector(anchorSize.X, newDim));
        }

        private void BeginResize(Handle handle, ClickEventArgs args)
        {
            // CapturePointer here is NOT how continuation is driven (that's
            // still Update()'s own unconditional poll below, unaffected by
            // this) — it's what stops OTHER elements from also reacting.
            // InputManager.ProcessMouseState (GustUI/Managers/InputManager.cs
            // ~414) early-returns whenever anything holds capture, skipping
            // the whole hover-based dispatch block entirely; WITHOUT capture,
            // that block runs every frame and fires OnMouseButtonHeldDown on
            // whatever's currently under the cursor — a slider/knob/scrollbar
            // swept over mid-resize would react to it (their handlers don't
            // check whether THEY started the drag, they just always respond
            // to a held-down call), exactly the "touch another control and
            // it breaks" the user hit 2026-08-17 after CapturePointer was
            // removed for the PRIOR fix (cursor-outrunning-the-moving-handle).
            // Capture's own hover-independent held-dispatch (same method,
            // ~414-425) would have solved that first problem too without
            // ever needing to remove it — this keeps both fixes at once.
            args.Element.CapturePointer();
            activeHandle = handle;
            anchorMouse = args.GlobalMousePosition.AsXna;
            anchorPosition = host.ElementTrait<PositionTrait>().Value().AsXna;
            anchorSize = host.ElementTrait<SizeTrait>().Value().AsXna;
        }

        private void ContinueResize(Vector2 currentMouse)
        {
            Handle handle = activeHandle.Value;
            Vector2 delta = currentMouse - anchorMouse;

            bool west = handle is Handle.W or Handle.NW or Handle.SW;
            bool east = handle is Handle.E or Handle.NE or Handle.SE;
            bool north = handle is Handle.N or Handle.NW or Handle.NE;
            bool south = handle is Handle.S or Handle.SW or Handle.SE;

            // Absolute edges, three of which stay pinned at their anchor
            // value and one or two of which move with the drag — computing
            // edges (not size+position deltas) means the min-size clamp
            // below can just pull the MOVING edge back toward whichever
            // one is fixed, instead of separately reconciling a position
            // delta against a size delta.
            float left = anchorPosition.X;
            float top = anchorPosition.Y;
            float right = anchorPosition.X + anchorSize.X;
            float bottom = anchorPosition.Y + anchorSize.Y;

            if (west)
            {
                left = anchorPosition.X + delta.X;
            }

            if (east)
            {
                right = anchorPosition.X + anchorSize.X + delta.X;
            }

            if (north)
            {
                top = anchorPosition.Y + delta.Y;
            }

            if (south)
            {
                bottom = anchorPosition.Y + anchorSize.Y + delta.Y;
            }

            // Clamp the moving edge(s) to the screen before the min-size
            // clamp below — 2026-08-17, found from the user's own test:
            // dragging fast enough (a large per-frame mouse delta) let the
            // computed edge fly arbitrarily far past the window's actual
            // bounds with nothing to stop it, which both looked like an
            // unwanted "maximize" once the modal ballooned past the screen
            // and, for the top edge specifically, pushed Y so far above
            // ModalWindowElement.TopLimit() that Update()'s own screen-edge
            // clamp had to fight it every single frame. Bottom/right aren't
            // clamped to anything narrower than the raw window — matches
            // the same raw-window bound Update()'s own existing clamp uses.
            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            float topLimit = ModalWindowElement.TopLimit();
            left = Math.Max(0f, left);
            top = Math.Max(topLimit, top);
            right = Math.Min(windowSize.X, right);
            bottom = Math.Min(windowSize.Y, bottom);

            Vector2 minSize = host.MinSize;
            if (right - left < minSize.X)
            {
                if (west)
                {
                    left = right - minSize.X;
                }
                else
                {
                    right = left + minSize.X;
                }
            }

            if (bottom - top < minSize.Y)
            {
                if (north)
                {
                    top = bottom - minSize.Y;
                }
                else
                {
                    bottom = top + minSize.Y;
                }
            }

            host.Set<PositionTrait>(new TVVector(left, top));
            host.Set<SizeTrait>(new TVVector(right - left, bottom - top));
        }

        private void EndResize()
        {
            if (activeHandle != null)
            {
                handles[(int)activeHandle.Value].Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
            }

            activeHandle = null;
        }
    }
}
