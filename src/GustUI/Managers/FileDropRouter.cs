using System;
using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Managers
{
    /// <summary>What became of a file dropped on the app.</summary>
    public enum FileDropOutcome
    {
        /// <summary>A registrant in the window it landed on took it.</summary>
        Taken,

        /// <summary>It landed on a window that registered nothing for its
        /// type. Nothing else got it either: the host says so.</summary>
        Refused,

        /// <summary>No window to land on.</summary>
        NoWindow,
    }

    /// <summary>
    /// Sends a file dropped on the app from outside it to the window it was
    /// dropped on (ezmuze #683).
    ///
    /// THE RULE. The drop belongs to the window DRAWN on top at the drop point
    /// (<see cref="ModalWindowElement.WindowAt"/> — the same answer a tab merge
    /// uses), or, while a waiting dialog holds the app, to that dialog. Inside
    /// it, the deepest element under the point that registered a
    /// <see cref="FileDropTrait"/> taking the file's type gets it. If nothing
    /// in that window takes it, nothing does: it is refused, and does not fall
    /// through to the window behind. Before this the sequencer received every
    /// drop and tested its own rectangles, so a sample dropped on a module
    /// panel landed on the timeline hidden underneath.
    ///
    /// A drop with no point (a platform that does not report one, or a pointer
    /// outside the window) goes to the ACTIVE window, and to whatever in it
    /// registered, front-most first.
    /// </summary>
    public static class FileDropRouter
    {
        private static Element dragTarget;

        /// <summary>The window a drop at <paramref name="point"/> belongs to:
        /// the waiting dialog when one is up, else the topmost window there,
        /// else the active window (also the answer for no point).</summary>
        public static ModalWindowElement TargetWindow(Element root, Vector2? point)
        {
            ModalWindowElement waiting = ModalWindowElement.WaitingDialog;
            if (waiting != null)
            {
                return waiting;
            }

            if (point.HasValue)
            {
                ModalWindowElement at = ModalWindowElement.WindowAt(root, point.Value);
                if (at != null)
                {
                    return at;
                }
            }

            return ModalWindowElement.ActiveModal(root);
        }

        /// <summary>
        /// The element inside <paramref name="window"/> that takes a file
        /// called <paramref name="name"/>: with a point, the deepest registrant
        /// under it that takes the type. Failing that — no point, a point on
        /// the window's chrome (its title bar, its menu row) rather than on the
        /// part that registered, or a point beside a waiting dialog that holds
        /// the app — the window's OUTERMOST registrant for the type, which is
        /// the window speaking for all of itself. Null when nothing in the
        /// window takes it. <paramref name="chain"/> is what lies under the
        /// point, outermost first.
        /// </summary>
        public static Element FindTarget(Element window, Vector2? point, string name, out IReadOnlyList<Element> chain)
        {
            chain = Array.Empty<Element>();
            if (window == null)
            {
                return null;
            }

            if (point.HasValue)
            {
                List<Element> under = InputManager.ElementsAt(window, point.Value);
                chain = under;
                for (int i = under.Count - 1; i >= 0; i--)
                {
                    if (TakesFile(under[i], name))
                    {
                        return under[i];
                    }
                }
            }

            return Outermost(window, name);
        }

        /// <summary>The shallowest visible, attached element of
        /// <paramref name="window"/> (itself included) that takes the file,
        /// breadth first.</summary>
        private static Element Outermost(Element window, string name)
        {
            var level = new Queue<Element>();
            level.Enqueue(window);
            while (level.Count > 0)
            {
                Element element = level.Dequeue();
                if (element == null || !element.Visible)
                {
                    continue;
                }

                if (TakesFile(element, name))
                {
                    return element;
                }

                if (element.Children != null)
                {
                    foreach (Element child in element.Children.Items)
                    {
                        level.Enqueue(child);
                    }
                }
            }

            return null;
        }

        private static bool TakesFile(Element element, string name)
            => element.HasTrait<FileDropTrait>() && element.ElementTrait<FileDropTrait>().Value()?.Takes(name) == true;

        private static TVFileDrop Registration(Element element)
            => element != null && element.HasTrait<FileDropTrait>() ? element.ElementTrait<FileDropTrait>().Value() : null;

        /// <summary>
        /// Delivers one dropped file. <paramref name="drop"/> carries the name,
        /// the path or bytes and the point; this fills in the window, the
        /// target and the chain. <paramref name="window"/> is the window it
        /// landed on, for the host's "this window does not take those" when it
        /// was refused.
        /// </summary>
        public static FileDropOutcome Drop(Element root, DroppedFileEventArgs drop, out ModalWindowElement window)
        {
            EndDrag();

            window = TargetWindow(root, drop.Point);
            if (window == null)
            {
                return FileDropOutcome.NoWindow;
            }

            Element target = FindTarget(window, drop.Point, drop.Name, out IReadOnlyList<Element> chain);
            if (target == null)
            {
                return FileDropOutcome.Refused;
            }

            drop.Window = window;
            drop.Target = target;
            drop.Chain = chain;
            Registration(target)?.Drop?.Invoke(drop);
            return FileDropOutcome.Taken;
        }

        /// <summary>
        /// A drag carrying a file is at <paramref name="drag"/>'s point: tells
        /// the registrant under it, and tells the one it just left that it
        /// left. Returns whether something there would take the file.
        /// </summary>
        public static bool DragOver(Element root, DroppedFileEventArgs drag)
        {
            ModalWindowElement window = TargetWindow(root, drag.Point);
            Element target = FindTarget(window, drag.Point, drag.Name, out IReadOnlyList<Element> chain);
            if (!ReferenceEquals(target, dragTarget))
            {
                EndDrag();
                dragTarget = target;
            }

            if (target == null)
            {
                return false;
            }

            drag.Window = window;
            drag.Target = target;
            drag.Chain = chain;
            Registration(target)?.DragOver?.Invoke(drag);
            return true;
        }

        /// <summary>The drag left the app, or was abandoned.</summary>
        public static void EndDrag()
        {
            Element left = dragTarget;
            dragTarget = null;
            Registration(left)?.DragLeave?.Invoke();
        }
    }
}
