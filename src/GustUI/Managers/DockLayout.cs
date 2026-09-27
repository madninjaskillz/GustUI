using System;
using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Extensions;
using Microsoft.Xna.Framework;

namespace GustUI.Managers
{
    /// <summary>
    /// Tracks which <see cref="ModalWindowElement"/>s are currently docked to
    /// each screen edge (2026-08-16, docked-modal feature; Top/Bottom added
    /// 2026-08-17 — the Stack uses Bottom) and the total space each edge
    /// currently reserves, so every full-screen/fill-available window can
    /// shrink around them without any docked panel needing to know about any
    /// specific full-screen view (or vice versa) — the same "poll a shared
    /// value every frame" idiom <c>FullScreenModalElement</c> already
    /// used for window-resize.
    ///
    /// Insets are computed LIVE on every read (2026-08-17 — previously cached
    /// and only refreshed on Register/Unregister, which meant a docked
    /// panel's own resize-drag splitter had no way to propagate to the
    /// windows sharing space with it without an extra explicit notify call;
    /// reading live removes that whole class of "forgot to invalidate"
    /// bug). Each docked panel reserves <c>min(its own current size along
    /// the dock axis, 50% of the game window's size along that axis)</c> —
    /// content-sized by default (a panel opens at whatever width/height its
    /// own content wants), capped so one docked panel can never eat more
    /// than half the window — see <see cref="EffectiveSize"/> for the fuller
    /// picture, including the filler-budget clamp added 2026-08-17.
    ///
    /// Multiple panels can dock to the same side; they stack outward from the
    /// screen edge in registration order (<see cref="StackOffset"/>) — e.g.
    /// opening the wave bank while the loop browser is already docked right
    /// lands it just inboard of the loop browser.
    /// </summary>
    public static class DockLayout
    {
        private static readonly List<ModalWindowElement> leftStack = new List<ModalWindowElement>();
        private static readonly List<ModalWindowElement> rightStack = new List<ModalWindowElement>();
        private static readonly List<ModalWindowElement> topStack = new List<ModalWindowElement>();
        private static readonly List<ModalWindowElement> bottomStack = new List<ModalWindowElement>();

        /// <summary>Every currently-open window the docks keep room for
        /// (<see cref="ModalWindowElement.KeepsDockRoom"/>): every
        /// <see cref="ModalWindowElement.FillsAvailableSpace"/> window, and one
        /// that filled and has since been picked up (ezmuze #395)
        /// (2026-08-17 — see <see cref="EffectiveSize"/>'s own doc
        /// comment for why this exists: a docked stack's reservation must
        /// leave room for whichever filler needs the most, or the filler's
        /// own MinSize floor and the dock's reservation can together exceed
        /// the window, producing genuine pixel overlap with nothing to
        /// resolve the squeeze).</summary>
        private static readonly List<ModalWindowElement> fillers = new List<ModalWindowElement>();

        /// <summary>
        /// How much each docked panel RESERVES along its dock axis, once
        /// something has said so explicitly.
        ///
        /// This is the boundary between a docked panel and whatever fills the
        /// rest, and it belongs here rather than being read back off the
        /// panel's own SizeTrait. That was the old model and it had no way to
        /// be right: the panel's size is written by half a dozen things —
        /// a title-bar drag, a tab merge, its own content reflow — and any of
        /// them silently moved a boundary the rest of the layout was deriving
        /// from. Dragging the split line, which is where the panel's title bar
        /// happens to sit, tore the panel out of the dock AND left the filler
        /// believing it had the whole window.
        ///
        /// Absent = "however big the panel wants to be", which is the right
        /// default for a panel that has just been docked and has never been
        /// resized.
        /// </summary>
        private static readonly Dictionary<ModalWindowElement, float> reservations = new();

        /// <summary>
        /// Sets the boundary for a docked panel, in pixels along its dock axis.
        ///
        /// One entry point for both directions of the same gesture: there is
        /// only ever ONE boundary between a dock and the space beside it, so
        /// dragging it from the panel's side and dragging it from the filler's
        /// side are the same act and must not be two mechanisms that can
        /// disagree.
        /// </summary>
        public static void SetReservation(ModalWindowElement modal, float size)
        {
            if (modal != null)
            {
                reservations[modal] = Math.Max(0f, size);
            }
        }

        /// <summary>Forgets an explicit boundary, returning the panel to
        /// content-sized.</summary>
        public static void ClearReservation(ModalWindowElement modal)
        {
            if (modal != null)
            {
                reservations.Remove(modal);
            }
        }

        public static float LeftInset => Reserved(leftStack, DockSide.Left);

        public static float RightInset => Reserved(rightStack, DockSide.Right);

        public static float TopInset => Reserved(topStack, DockSide.Top);

        public static float BottomInset => Reserved(bottomStack, DockSide.Bottom);

        internal static void Register(ModalWindowElement modal, DockSide side)
        {
            Unregister(modal); // idempotent: re-docking (e.g. left -> right) just moves it
            List<ModalWindowElement> stack = StackFor(side);
            if (stack != null)
            {
                stack.Add(modal);
                dockedAt[modal] = ++dockSequence;
            }
        }

        // ---- who owns a corner (ezmuze #384) ------------------------------------
        //
        // A side dock and a bottom (or top) dock meet in a corner, and each used
        // to leave it to the other: a side dock stopped at the bottom docks'
        // edge, and a bottom dock started at the side docks' edge. With the
        // explorer docked along the bottom and the sequencer then docked left,
        // the block below the one and beside the other belonged to nobody:
        // 1126 x 330 px of bare backdrop. Now the dock that was there FIRST keeps
        // the corner and the later one fits inside it, which is exactly what the
        // dock preview (DockPreviewOverlay) has always shown while you hold.
        // Docking something never moves or narrows a panel already docked.

        /// <summary>When each docked window docked, so the earlier of two
        /// perpendicular docks can keep the corner they share.</summary>
        private static readonly Dictionary<ModalWindowElement, long> dockedAt = new();

        private static long dockSequence;

        /// <summary>
        /// How much of <paramref name="side"/> is taken, as far as
        /// <paramref name="modal"/> is concerned: the panels docked to that side
        /// BEFORE it was. They keep the corners they share with it, so its span
        /// stops at them; panels docked later sit inside its span instead.
        /// </summary>
        internal static float InsetBefore(ModalWindowElement modal, DockSide side)
        {
            List<ModalWindowElement> stack = StackFor(side);
            if (stack == null)
            {
                return 0f;
            }

            if (!dockedAt.TryGetValue(modal, out long mine))
            {
                return Reserved(stack, side);
            }

            // Every docked window asks this every frame: one scratch list, not
            // one per call. The UI is single-threaded, and EffectiveSize (below)
            // never comes back in here.
            insetScratch.Clear();
            foreach (ModalWindowElement other in stack)
            {
                insetScratch.Add((dockedAt.TryGetValue(other, out long theirs) ? theirs : long.MaxValue, EffectiveSize(other, side)));
            }

            return SumDockedBefore(insetScratch, mine);
        }

        private static readonly List<(long Order, float Size)> insetScratch = new();

        /// <summary>The arithmetic of <see cref="InsetBefore"/>: the total depth
        /// of the docks that docked before <paramref name="mine"/>.</summary>
        internal static float SumDockedBefore(IReadOnlyList<(long Order, float Size)> docks, long mine)
        {
            float sum = 0f;
            foreach ((long order, float size) in docks)
            {
                if (order < mine)
                {
                    sum += size;
                }
            }

            return sum;
        }

        /// <summary>Where a docked window sits: its index in its side's stack
        /// and when it docked. Null when it is not docked.</summary>
        internal static (int Index, long Order)? PlacementOf(ModalWindowElement modal)
        {
            if (modal == null || !dockedAt.TryGetValue(modal, out long order))
            {
                return null;
            }

            foreach (DockSide side in new[] { DockSide.Left, DockSide.Right, DockSide.Top, DockSide.Bottom })
            {
                int index = StackFor(side).IndexOf(modal);
                if (index >= 0)
                {
                    return (index, order);
                }
            }

            return null;
        }

        /// <summary>
        /// Puts a window that has just re-docked back in the slot it held
        /// before: the same place in its side's stack and the same docking
        /// order, so it is beside the same panels and keeps the corners it
        /// had. For a drag that is cancelled (#374): docking afresh would put
        /// it at the inboard end, as the newest dock.
        /// </summary>
        internal static void RestorePlacement(ModalWindowElement modal, DockSide side, (int Index, long Order) placement)
        {
            List<ModalWindowElement> stack = StackFor(side);
            if (stack == null || !stack.Remove(modal))
            {
                return;
            }

            stack.Insert(Math.Clamp(placement.Index, 0, stack.Count), modal);
            dockedAt[modal] = placement.Order;
        }

        /// <summary>
        /// The rectangle a docked panel occupies. Along its own edge it runs
        /// from <paramref name="startInset"/> to <paramref name="endInset"/> (the
        /// perpendicular docks that were there first, see
        /// <see cref="InsetBefore"/>: the top and bottom ones for a side dock,
        /// the left and right ones for a top or bottom dock). Across it, it is
        /// <paramref name="thickness"/> deep and <paramref name="stackOffset"/>
        /// in from the window's edge. <paramref name="chromeBottom"/> is the
        /// app's own strip along the bottom (the status bar), which every dock
        /// stops above.
        /// </summary>
        internal static (Vector2 Position, Vector2 Size) DockRect(DockSide side, Vector2 window,
            float thickness, float stackOffset, float startInset, float endInset, float chromeBottom)
        {
            if (side == DockSide.Left || side == DockSide.Right)
            {
                float height = Math.Max(0f, window.Y - startInset - endInset - chromeBottom);
                float x = side == DockSide.Left ? stackOffset : window.X - thickness - stackOffset;
                return (new Vector2(x, startInset), new Vector2(thickness, height));
            }

            float width = Math.Max(0f, window.X - startInset - endInset);
            float y = side == DockSide.Top ? stackOffset : window.Y - chromeBottom - thickness - stackOffset;
            return (new Vector2(startInset, y), new Vector2(width, thickness));
        }

        internal static void Unregister(ModalWindowElement modal)
        {
            leftStack.Remove(modal);
            rightStack.Remove(modal);
            topStack.Remove(modal);
            bottomStack.Remove(modal);

            dockedAt.Remove(modal);

            // A panel that has left the dock keeps no claim on the boundary —
            // re-docking it later should start from its content size again.
            reservations.Remove(modal);

            // ...nor on the size it wanted while docked (#379): off the dock it
            // is a floating window again and its SizeTrait is simply its size.
            preferred.Remove(modal);
        }

        /// <summary>
        /// The panel docked to <paramref name="side"/> that is closest to the
        /// middle of the screen — the one whose inboard edge IS the boundary
        /// with whatever fills the rest, and therefore the one a filler's own
        /// edge-drag is really moving.
        /// </summary>
        public static ModalWindowElement Innermost(DockSide side)
        {
            List<ModalWindowElement> stack = StackFor(side);
            return stack == null || stack.Count == 0 ? null : stack[stack.Count - 1];
        }

        /// <summary>How much <paramref name="modal"/> reserves right now, after
        /// every cap — what a caller adjusting the boundary has to add to.</summary>
        public static float ReservedFor(ModalWindowElement modal, DockSide side) =>
            modal == null ? 0f : EffectiveSize(modal, side);

        /// <summary>Read-only snapshot of whatever's currently docked to
        /// <paramref name="side"/>, closest-to-the-edge first — used by the
        /// dock-onto-an-occupied-side-merges-instead-of-stacks feature
        /// (2026-08-17, user request): a tabable window dropped on an edge
        /// that already has a tabable occupant offers to merge with it
        /// rather than silently taking a second slot in the stack.</summary>
        internal static IReadOnlyList<ModalWindowElement> DockedTo(DockSide side)
        {
            return (IReadOnlyList<ModalWindowElement>)StackFor(side) ?? Array.Empty<ModalWindowElement>();
        }

        /// <summary>Registers <paramref name="modal"/> as a window that
        /// continuously fills whatever space docking leaves free — see
        /// <see cref="EffectiveSize"/>. Idempotent.</summary>
        internal static void RegisterFiller(ModalWindowElement modal)
        {
            if (!fillers.Contains(modal))
            {
                fillers.Add(modal);
            }
        }

        internal static void UnregisterFiller(ModalWindowElement modal)
        {
            fillers.Remove(modal);
        }

        /// <summary>Whether the docks keep room for <paramref name="modal"/>'s
        /// minimum (<see cref="ModalWindowElement.KeepsDockRoom"/>).</summary>
        internal static bool HoldsRoom(ModalWindowElement modal) => fillers.Contains(modal);

        private static List<ModalWindowElement> StackFor(DockSide side)
        {
            switch (side)
            {
                case DockSide.Left: return leftStack;
                case DockSide.Right: return rightStack;
                case DockSide.Top: return topStack;
                case DockSide.Bottom: return bottomStack;
                default: return null;
            }
        }

        /// <summary>Sum of the EFFECTIVE (capped + budget-clamped) size of
        /// every panel docked before this one on the given side — this
        /// panel's own distance from the true screen edge.</summary>
        internal static float StackOffset(ModalWindowElement modal, DockSide side)
        {
            List<ModalWindowElement> stack = StackFor(side);
            float offset = 0f;
            if (stack == null)
            {
                return offset;
            }

            foreach (ModalWindowElement other in stack)
            {
                if (other == modal)
                {
                    break;
                }

                offset += EffectiveSize(other, side);
            }

            return offset;
        }

        private static float Reserved(List<ModalWindowElement> stack, DockSide side)
        {
            float sum = 0f;
            foreach (ModalWindowElement modal in stack)
            {
                sum += EffectiveSize(modal, side);
            }

            return sum;
        }

        /// <summary>How much space this docked panel effectively occupies
        /// along its dock axis — content-sized, capped to 50% of the game
        /// window (see the class doc comment), and FURTHER capped so the
        /// running total reserved on this side never starves whichever open
        /// <see cref="ModalWindowElement.FillsAvailableSpace"/> window needs
        /// the most room below its own <see cref="ModalWindowElement.MinSize"/>.
        /// Found live, 2026-08-17: without this second clamp, a docked
        /// panel's natural/resized width plus a filler's MinSize floor could
        /// together exceed the window, and nothing shrank to resolve
        /// it — the filler's own Math.Max(MinSize, available) clamp always
        /// wins, so the docked panel visibly overlapped it instead.
        ///
        /// THE single source of truth for this panel's own on-screen width/
        /// height along the dock axis: <see cref="ModalWindowElement.LayoutDocked"/>
        /// renders at exactly this value (not the panel's raw, unclamped
        /// SizeTrait) every frame, so a resize-drag past either cap visibly
        /// stops right there rather than silently desyncing from what
        /// <see cref="LeftInset"/>/etc. report to the windows sharing space
        /// with it.</summary>
        internal static float EffectiveSize(ModalWindowElement modal, DockSide side)
        {
            bool horizontal = side == DockSide.Left || side == DockSide.Right;
            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            float axisSize = horizontal ? windowSize.X : windowSize.Y;

            // The explicit boundary wins; the panel's own size is only the
            // default for one that has never been dragged.
            float natural = reservations.TryGetValue(modal, out float reserved)
                ? reserved
                : NaturalSize(modal, horizontal);

            // Never thinner than the window's own minimum across the dock,
            // where the caps below allow it (ezmuze #400): a sequencer that
            // came off maximised at 182 docked top at 182 of its 260, with a
            // 78 px gap above the explorer. The splitter's reservation is
            // floored here too, so dragging the boundary stops at it.
            natural = OwnFloor(natural, horizontal ? modal.MinSize.X : modal.MinSize.Y);

            float floor = Math.Max(MaxFillerMinSize(horizontal), LaterCrossDockFloor(modal, horizontal));
            floor = Math.Max(floor, LaterOppositeDockFloor(modal, side));

            // A top or bottom dock never runs into the app's bottom chrome.
            // The filler floors already carry it; this only bites with no
            // filler, which is when two opposite docks were overlapping.
            if (!horizontal)
            {
                floor = Math.Max(floor, modal.BottomInset);
            }

            return Clamp(natural, axisSize, floor, StackOffset(modal, side) + OppositeDockedBefore(modal, side));
        }

        // ---- opposite docks share the axis (ezmuze #395) ------------------------
        //
        // A top dock and a bottom dock (or left and right) share one axis, and
        // nothing kept them apart: each was clamped only against its own side's
        // stack and the fillers. With the explorer docked along the bottom at
        // its 50% cap (281.33 of 562.67) and the sequencer dragged to the top
        // at its 260 minimum, the two overlapped by 2.67 px, the explorer's
        // title bar under the sequencer. The #384/#390 rules now hold across the
        // axis too: the dock that was there first keeps its size and the later
        // one fits in what is left, and the later one's minimum holds the
        // earlier one back.

        private static DockSide Opposite(DockSide side) => side switch
        {
            DockSide.Left => DockSide.Right,
            DockSide.Right => DockSide.Left,
            DockSide.Top => DockSide.Bottom,
            DockSide.Bottom => DockSide.Top,
            _ => DockSide.None,
        };

        /// <summary>How deep the docks on the opposite side that docked BEFORE
        /// <paramref name="modal"/> are: the later dock fits inside what they
        /// leave. Recurses only into earlier docks, so it ends.</summary>
        private static float OppositeDockedBefore(ModalWindowElement modal, DockSide side)
        {
            List<ModalWindowElement> stack = StackFor(Opposite(side));
            if (stack == null || stack.Count == 0 || !dockedAt.TryGetValue(modal, out long mine))
            {
                return 0f;
            }

            float sum = 0f;
            foreach (ModalWindowElement other in stack.ToArray())
            {
                if (dockedAt.TryGetValue(other, out long theirs) && theirs < mine)
                {
                    sum += EffectiveSize(other, Opposite(side));
                }
            }

            return sum;
        }

        /// <summary>The most room any dock on the opposite side that docked
        /// AFTER <paramref name="modal"/> needs along this axis: its MinSize,
        /// plus the bottom chrome for a top or bottom dock.</summary>
        private static float LaterOppositeDockFloor(ModalWindowElement modal, DockSide side)
        {
            List<ModalWindowElement> stack = StackFor(Opposite(side));
            if (stack == null || stack.Count == 0 || !dockedAt.TryGetValue(modal, out long mine))
            {
                return 0f;
            }

            bool horizontal = side == DockSide.Left || side == DockSide.Right;
            floorScratch.Clear();
            foreach (ModalWindowElement other in stack)
            {
                if (dockedAt.TryGetValue(other, out long theirs))
                {
                    floorScratch.Add((theirs, horizontal ? other.MinSize.X : other.MinSize.Y + other.BottomInset));
                }
            }

            return MaxFloorDockedAfter(floorScratch, mine);
        }

        // ---- a later side dock keeps its minimum (ezmuze #390) ------------------
        //
        // A dock that docked AFTER a perpendicular one fits inside it (#384), so
        // its length along its edge is whatever the earlier one leaves. The
        // floor above only counted FILLERS, and a sequencer docked to the side
        // is not one any more: with the explorer docked along the bottom first,
        // the explorer grew to its 50% cap the moment the sequencer docked left,
        // leaving it 257 px of its 260 minimum. The later dock's minimum (plus
        // the app's bottom chrome, which a side dock also stops above) is now a
        // floor on the earlier one, exactly as a filler's is. A dock that was
        // there FIRST runs past the corner and is not squeezed, so it sets none.

        /// <summary>The most room any dock on a perpendicular side that docked
        /// AFTER <paramref name="modal"/> needs along this axis: its MinSize, plus
        /// the bottom chrome for a side dock measured vertically.</summary>
        private static float LaterCrossDockFloor(ModalWindowElement modal, bool horizontal)
        {
            if (!dockedAt.TryGetValue(modal, out long mine))
            {
                return 0f;
            }

            floorScratch.Clear();
            foreach (DockSide crossSide in horizontal ? new[] { DockSide.Top, DockSide.Bottom } : new[] { DockSide.Left, DockSide.Right })
            {
                foreach (ModalWindowElement other in StackFor(crossSide))
                {
                    if (dockedAt.TryGetValue(other, out long theirs))
                    {
                        floorScratch.Add((theirs, horizontal ? other.MinSize.X : other.MinSize.Y + other.BottomInset));
                    }
                }
            }

            return MaxFloorDockedAfter(floorScratch, mine);
        }

        private static readonly List<(long Order, float Floor)> floorScratch = new();

        /// <summary>The arithmetic of <see cref="LaterCrossDockFloor"/>: the
        /// largest floor among the docks that docked after <paramref name="mine"/>.</summary>
        internal static float MaxFloorDockedAfter(IReadOnlyList<(long Order, float Floor)> docks, long mine)
        {
            float max = 0f;
            foreach ((long order, float floor) in docks)
            {
                if (order > mine && floor > max)
                {
                    max = floor;
                }
            }

            return max;
        }

        // ---- the dock preview (ezmuze #391) -------------------------------------

        /// <summary>
        /// Where a window that wants <paramref name="natural"/> along its dock
        /// axis would land if it docked to <paramref name="side"/> now: the same
        /// rectangle <see cref="ModalWindowElement.LayoutDocked"/> will give it.
        /// It docks last, so it sits inboard of every panel already on that
        /// side, inside the span of every perpendicular dock (#384), above the
        /// app's bottom chrome, and at its clamped size (50% cap, filler
        /// floors). The preview used to place a bottom dock flush with the
        /// window's bottom edge, over the status bar and any bottom docks, and
        /// drew a side dock at its unclamped width. <paramref name="minAlong"/>
        /// is the window's own minimum along the edge: once it docks, the
        /// perpendicular docks already there give way to it (#390), so the
        /// preview shows that too.
        /// </summary>
        internal static (Vector2 Position, Vector2 Size) PreviewRect(DockSide side, float natural, float chromeBottom, float minAlong,
            float minAcross = 0f, ModalWindowElement docking = null)
        {
            Vector2 window = Resources.StaticResources.RootWindow.GetSize().AsXna;
            bool horizontal = side == DockSide.Left || side == DockSide.Right;
            return PreviewRect(side, window, natural,
                Reserved(StackFor(side), side),
                MaxFillerMinSize(horizontal, docking),
                horizontal ? TopInset : LeftInset,
                horizontal ? BottomInset : RightInset,
                chromeBottom,
                minAlong,
                Reserved(StackFor(Opposite(side)), Opposite(side)),
                minAcross);
        }

        /// <summary>The arithmetic of <see cref="PreviewRect(DockSide, float, float, float)"/>,
        /// with the layout read out: <paramref name="sameSide"/> is what that
        /// side already reserves, <paramref name="startInset"/> and
        /// <paramref name="endInset"/> what the two perpendicular sides do.</summary>
        internal static (Vector2 Position, Vector2 Size) PreviewRect(DockSide side, Vector2 window, float natural,
            float sameSide, float fillerFloor, float startInset, float endInset, float chromeBottom, float minAlong = 0f,
            float opposite = 0f, float minAcross = 0f)
        {
            bool horizontal = side == DockSide.Left || side == DockSide.Right;
            float axis = horizontal ? window.X : window.Y;
            float chrome = horizontal ? 0f : chromeBottom;

            // The opposite side's docks were there first, so this one fits in
            // what they leave, after they give way to its minimum (#395), as
            // LaterOppositeDockFloor makes them do once it has docked.
            if (opposite > 0f && minAcross > 0f)
            {
                opposite = Math.Min(opposite, Math.Max(0f, axis - minAcross - chrome));
            }

            // Docked, it is never thinner than its own minimum (#400).
            natural = OwnFloor(natural, minAcross);

            float thickness = Clamp(natural, axis, Math.Max(fillerFloor, chrome), sameSide + opposite);

            // The end dock (bottom, or right) gives way to the new dock's
            // minimum, as LaterCrossDockFloor makes it do once it has docked.
            float along = horizontal
                ? window.Y - startInset - endInset - chromeBottom
                : window.X - startInset - endInset;
            if (along < minAlong)
            {
                endInset = Math.Max(0f, endInset - (minAlong - along));
            }

            return DockRect(side, window, thickness, sameSide, startInset, endInset, chromeBottom);
        }

        /// <summary>What a docked window wants across its dock once its own
        /// minimum is counted: <paramref name="natural"/>, raised to
        /// <paramref name="minimum"/>. The caps in <see cref="Clamp"/> still
        /// apply on top (half the axis, and the room other windows need), so
        /// the minimum holds wherever the free space allows (ezmuze #400).</summary>
        internal static float OwnFloor(float natural, float minimum) => Math.Max(natural, minimum);

        /// <summary>The arithmetic of <see cref="EffectiveSize"/>, with the
        /// window read out: <paramref name="natural"/> capped to half the axis
        /// and to what is left once the biggest filler's floor and the panels
        /// stacked outboard of this one have been paid for.</summary>
        internal static float Clamp(float natural, float axisSize, float fillerFloor, float before)
        {
            float own = Math.Min(natural, 0.5f * axisSize);
            float budget = Math.Max(0f, axisSize - fillerFloor);
            float remaining = Math.Max(0f, budget - before);
            return Math.Min(own, remaining);
        }

        // ---- the size a docked panel WANTS (ezmuze #379) ----------------------
        //
        // LayoutDocked renders a docked panel at its EFFECTIVE size, and has to
        // write that into the panel's SizeTrait (hit-testing and the panel's own
        // reflow read it). That same SizeTrait was also where EffectiveSize read
        // the panel's natural size from, so every clamp became the new natural
        // size: a small window squeezed the explorer to 118 px, and it stayed
        // at 118 when the window grew again -- and went lower each time the
        // window shrank. The fix keeps the two apart: what LayoutDocked wrote is
        // remembered, and while the SizeTrait still holds exactly that, the
        // panel still wants what it wanted before. Anything ELSE writing the
        // size (the panel's own content reflow) is a new preference, taken as is.

        private static readonly Dictionary<ModalWindowElement, (float Written, float Preferred)> preferred = new();

        /// <summary>What a docked panel wants along its dock axis, before any
        /// clamp: its preferred size while its SizeTrait still holds the value
        /// <see cref="NoteDockedSize"/> last wrote, and its SizeTrait otherwise.</summary>
        internal static float NaturalSize(ModalWindowElement modal, bool horizontal)
        {
            float current = horizontal ? modal.GetSize().X : modal.GetSize().Y;
            return PreferredSize(current, preferred.TryGetValue(modal, out var note) ? note : null);
        }

        /// <summary>The rule <see cref="NaturalSize"/> applies, testable
        /// without a window.</summary>
        internal static float PreferredSize(float current, (float Written, float Preferred)? note)
            => note is { } n && Math.Abs(current - n.Written) < 0.5f ? n.Preferred : current;

        /// <summary>Records that <see cref="ModalWindowElement.LayoutDocked"/>
        /// wrote <paramref name="written"/> into a panel that wanted
        /// <paramref name="natural"/>.</summary>
        internal static void NoteDockedSize(ModalWindowElement modal, float natural, float written)
        {
            if (modal != null)
            {
                preferred[modal] = (written, natural);
            }
        }

        /// <summary>
        /// The most room any open filler needs along this axis — its own
        /// MinSize PLUS whatever app chrome it leaves uncovered.
        ///
        /// The chrome has to be in here. A filler asks for
        /// <c>AvailableRect(extraBottomInset)</c> and then floors the result at
        /// its MinSize, so what it actually occupies is MinSize + that inset. A
        /// budget computed from MinSize alone left a docked panel free to
        /// reserve the chrome's worth of pixels as well, and the two then
        /// overlapped by exactly the height of the status bar — visible only
        /// once the window was small enough for the floor to bite.
        /// </summary>
        private static float MaxFillerMinSize(bool horizontal, ModalWindowElement except = null)
        {
            roomScratch.Clear();
            foreach (ModalWindowElement filler in fillers)
            {
                // The window being docked holds no room against itself: once
                // docked it sets no floor, so neither does its preview.
                roomScratch.Add((
                    filler.DockedSide != DockSide.None || filler == except,
                    horizontal ? filler.MinSize.X : filler.MinSize.Y + filler.BottomInset));
            }

            return MaxRoomFloor(roomScratch);
        }

        private static readonly List<(bool Docked, float Floor)> roomScratch = new();

        /// <summary>The arithmetic of <see cref="MaxFillerMinSize"/>: the largest
        /// floor among the windows the docks keep room for. A window that
        /// filled the free space keeps its floor after it is picked up and
        /// floats (ezmuze #395), so the docks do not grow under it; one that is
        /// docked itself sets none here (a later dock's floor is
        /// <see cref="LaterCrossDockFloor"/>'s business, and a dock's own
        /// minimum must not cap its own side).</summary>
        internal static float MaxRoomFloor(IReadOnlyList<(bool Docked, float Floor)> windows)
        {
            float max = 0f;
            foreach ((bool docked, float floor) in windows)
            {
                if (!docked && floor > max)
                {
                    max = floor;
                }
            }

            return max;
        }

        /// <summary>The rect a "fills available space" window — or anything
        /// else that wants to occupy exactly whatever docking leaves free,
        /// e.g. a maximized <see cref="ModalWindowElement"/> or
        /// <c>FullScreenModalElement</c> — should occupy: window size
        /// minus every side's live inset, with an optional extra bottom
        /// inset for the caller's own app-level chrome (a status bar) and an
        /// optional floor so the result never goes below some minimum usable
        /// size. Single source of truth for a formula that used to be
        /// hand-copied at three separate call sites (2026-08-17).</summary>
        public static (Vector2 Position, Vector2 Size) AvailableRect(float extraBottomInset = 0f, Vector2? minSize = null)
        {
            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            float left = LeftInset;
            float top = TopInset;
            float right = RightInset;
            float bottom = BottomInset + extraBottomInset;

            Vector2 size = new Vector2(
                Math.Max(0f, windowSize.X - left - right),
                Math.Max(0f, windowSize.Y - top - bottom));

            if (minSize.HasValue)
            {
                size = new Vector2(Math.Max(minSize.Value.X, size.X), Math.Max(minSize.Value.Y, size.Y));
            }

            return (new Vector2(left, top), size);
        }
    }
}
