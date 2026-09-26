using System;
using System.Collections.Generic;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    /// <summary>
    /// The fade under a waiting dialog (ezmuze #368): a full-window layer that
    /// darkens and LIVE-blurs everything beneath the dialog the app is waiting
    /// on, and takes every click, hover and scroll that is not on the dialog.
    ///
    /// One instance for the app, a child of the root window, restacked every
    /// frame to sit one depth below the topmost waiting dialog: with two
    /// stacked, it is between them, so the older one is faded with the rest of
    /// the app. It is the front-most thing under the pointer anywhere off the
    /// dialog, and GustUI's hit test goes to the front-most root branch only,
    /// so nothing beneath it is ever hovered or pressed. A press on it flashes
    /// the dialog's title bar instead.
    ///
    /// It fades in and out with the dialog's own open/close animation
    /// (design-guide.md §5): its strength is the highest open-progress of the
    /// waiting dialogs still on screen. The blur itself is drawn by
    /// <see cref="Managers.DrawManager.ResolveBackdrop"/>, which is why this
    /// element draws nothing of its own.
    /// </summary>
    [GustUI.Attributes.ElementTraits(typeof(OnRightClickTrait))]
    public sealed class ModalScrimElement : FilledRectangleElement
    {
        internal const string ScrimName = "modal-scrim";

        private static ModalScrimElement instance;

        /// <summary>How far the scrim has faded in, 0..1.</summary>
        internal float Amount { get; private set; }

        /// <summary>Whether it is taking input: a waiting dialog is open and
        /// not on its way out.</summary>
        internal bool Blocking { get; private set; }

        private ModalScrimElement()
            : base(0, 0, 0, 0, new TVFillSolidColor(Color.Transparent))
        {
            ElementName = ScrimName;

            // A view rebuild must never sweep it away with the view's own
            // children (same reason windows are chrome).
            IsChrome = true;
            Visible = false;

            Set<OnMousePress>(new TVEvent<ClickEventArgs>(_ => ModalWindowElement.WaitingDialog?.FlashTitle()));
            Set<OnRightClickTrait>(new TVEvent<ClickEventArgs>(_ => ModalWindowElement.WaitingDialog?.FlashTitle()));
        }

        /// <summary>The scrim, if one has been made.</summary>
        internal static ModalScrimElement Current => instance;

        /// <summary>Makes sure the scrim exists and is in the root window.</summary>
        internal static void Ensure()
        {
            WindowElement root = Resources.StaticResources?.RootWindow;
            if (root == null)
            {
                return;
            }

            if (instance == null)
            {
                instance = new ModalScrimElement();
            }

            if (!ReferenceEquals(instance.Parent, root))
            {
                root.AddChild(instance, ScrimName);
            }
        }

        /// <summary>
        /// Restacks and resizes the scrim for the waiting dialogs as they are
        /// now. Called by every waiting dialog each frame, when one closes, and
        /// by the scrim itself, so it is never a frame behind a dialog going.
        /// </summary>
        internal static void Refresh()
        {
            if (instance == null)
            {
                Ensure();
                if (instance == null)
                {
                    return;
                }
            }

            WindowElement root = Resources.StaticResources?.RootWindow;
            List<ModalWindowElement> live = ModalWindowElement.LiveQuestions();

            ModalWindowElement top = null;
            float amount = 0f;
            bool blocking = false;
            foreach (ModalWindowElement question in live)
            {
                if (!ReferenceEquals(question.Parent, root) || !question.Visible)
                {
                    continue;
                }

                if (top == null || ModalWindowElement.StacksAbove(question, top))
                {
                    top = question;
                }

                amount = Math.Max(amount, question.ShownProgress);
                blocking |= question.BlocksInput;
            }

            // The first frame a dialog is up it has not animated yet, but it
            // already holds the app: the fade starts from nothing, the block
            // does not wait for it.
            instance.Amount = top == null ? 0f : Math.Clamp(amount, 0f, 1f);
            instance.Blocking = blocking;

            if (top != null && instance.Depth != top.Depth - 1)
            {
                instance.Depth = top.Depth - 1;
                instance.Parent?.Children?.InvalidateSort();
            }

            bool shown = top != null && (blocking || instance.Amount > 0f);
            instance.Visible = shown;

            // Hit area: the whole window while it blocks, nothing while it is
            // only fading out, so an answered dialog lets go of the app at once.
            TVVector rootSize = root?.GetSize() ?? new TVVector(0, 0);
            instance.Set<PositionTrait>(new TVVector(0, 0));
            instance.Set<SizeTrait>(blocking ? rootSize : new TVVector(0, 0));

            Managers.DrawManager draw = Resources.StaticResources?.DrawManager;
            if (draw != null && shown && instance.Amount > 0f)
            {
                draw.BackdropRequested = true;
            }
        }

        public override void Update(Element parent = null)
        {
            base.Update(parent);
            Refresh();
        }

        public override void Draw()
        {
            if (Amount <= 0f)
            {
                return;
            }

            Resources.StaticResources.DrawManager.ResolveBackdrop(Amount, Resources.StaticResources.Theme.Scrim);
        }
    }
}
