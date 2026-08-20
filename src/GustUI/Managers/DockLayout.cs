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
    /// value every frame" idiom <see cref="FullScreenModalElement"/> already
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

        /// <summary>Every currently-open <see cref="ModalWindowElement.FillsAvailableSpace"/>
        /// window (2026-08-17 — see <see cref="EffectiveSize"/>'s own doc
        /// comment for why this exists: a docked stack's reservation must
        /// leave room for whichever filler needs the most, or the filler's
        /// own MinSize floor and the dock's reservation can together exceed
        /// the window, producing genuine pixel overlap with nothing to
        /// resolve the squeeze).</summary>
        private static readonly List<ModalWindowElement> fillers = new List<ModalWindowElement>();

        public static float LeftInset => Reserved(leftStack, DockSide.Left);

        public static float RightInset => Reserved(rightStack, DockSide.Right);

        public static float TopInset => Reserved(topStack, DockSide.Top);

        public static float BottomInset => Reserved(bottomStack, DockSide.Bottom);

        internal static void Register(ModalWindowElement modal, DockSide side)
        {
            Unregister(modal); // idempotent: re-docking (e.g. left -> right) just moves it
            StackFor(side)?.Add(modal);
        }

        internal static void Unregister(ModalWindowElement modal)
        {
            leftStack.Remove(modal);
            rightStack.Remove(modal);
            topStack.Remove(modal);
            bottomStack.Remove(modal);
        }

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

            float cap = 0.5f * axisSize;
            float natural = horizontal ? modal.GetSize().X : modal.GetSize().Y;
            float own = Math.Min(natural, cap);

            float fillerFloor = MaxFillerMinSize(horizontal);
            float budget = Math.Max(0f, axisSize - fillerFloor);
            float before = StackOffset(modal, side);
            float remaining = Math.Max(0f, budget - before);

            return Math.Min(own, remaining);
        }

        private static float MaxFillerMinSize(bool horizontal)
        {
            float max = 0f;
            foreach (ModalWindowElement filler in fillers)
            {
                float v = horizontal ? filler.MinSize.X : filler.MinSize.Y;
                if (v > max)
                {
                    max = v;
                }
            }

            return max;
        }

        /// <summary>The rect a "fills available space" window — or anything
        /// else that wants to occupy exactly whatever docking leaves free,
        /// e.g. a maximized <see cref="ModalWindowElement"/> or
        /// <see cref="FullScreenModalElement"/> — should occupy: window size
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
