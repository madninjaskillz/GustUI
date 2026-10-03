using System;
using System.Collections.Generic;
using GustUI.Elements;
using Microsoft.Xna.Framework;

namespace GustUI.TraitValues
{
    /// <summary>
    /// A file dropped on the app from outside it (ezmuze #683), as handed to
    /// the <see cref="TVFileDrop"/> that took it.
    /// </summary>
    public class DroppedFileEventArgs : TVEventArgs
    {
        private byte[] bytes;

        /// <summary>The file's name, extension included.</summary>
        public string Name { get; set; }

        /// <summary>Where it is on this machine, when it has a path at all (a
        /// browser drop has none). Bytes are read from here on demand.</summary>
        public string Path { get; set; }

        /// <summary>Where it was dropped, in GustUI's space, or null when the
        /// platform could not say and the drop went to the active window.</summary>
        public Vector2? Point { get; set; }

        /// <summary>The window the drop landed on.</summary>
        public Element Window { get; set; }

        /// <summary>The element that registered for it — the window itself, or
        /// something inside it.</summary>
        public Element Target { get; set; }

        /// <summary>Everything under <see cref="Point"/> inside
        /// <see cref="Window"/>, outermost first; empty without a point. A
        /// window that routes drops to parts of itself reads this rather than
        /// testing rectangles of its own.</summary>
        public IReadOnlyList<Element> Chain { get; set; } = Array.Empty<Element>();

        /// <summary>The file's lowercase extension with its dot (".wav"), or
        /// "" for none.</summary>
        public string Extension => TVFileDrop.ExtensionOf(Name);

        public DroppedFileEventArgs() { }

        public DroppedFileEventArgs(string name, string path, byte[] bytes, Vector2? point)
        {
            Name = name;
            Path = path;
            this.bytes = bytes;
            Point = point;
        }

        /// <summary>The file's contents: as handed over, or read from
        /// <see cref="Path"/> the first time something asks. Throws when the
        /// file cannot be read, so the taker can say why.</summary>
        public byte[] Bytes
        {
            get
            {
                if (bytes == null && !string.IsNullOrEmpty(Path))
                {
                    bytes = System.IO.File.ReadAllBytes(Path);
                }

                return bytes;
            }
        }

        /// <summary>Whether anything under the point is <paramref name="element"/>.</summary>
        public bool Over(Element element)
        {
            if (element == null)
            {
                return false;
            }

            foreach (Element e in Chain)
            {
                if (ReferenceEquals(e, element))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// What an element says about files dropped on it from outside the app
    /// (ezmuze #683): which ones it takes, and what to do with them. Carried by
    /// <see cref="Traits.FileDropTrait"/>; found and called by
    /// <see cref="Managers.FileDropRouter"/>.
    ///
    /// Registering is how a window gets a drop at all. A drop goes to the
    /// front-most window under the pointer and to the deepest element of it
    /// there that registered for the file's type; a window that registered
    /// nothing for it does not pass it on to whatever lies behind it.
    /// </summary>
    public class TVFileDrop : TraitValue
    {
        /// <summary>Whether this takes a file of this name. Asked by name rather
        /// than by a fixed list so a registrant whose list can change (importers
        /// that can be switched off) answers for itself.</summary>
        public Func<string, bool> Accepts { get; set; }

        /// <summary>The drop. Everything that reaches here was accepted.</summary>
        public Action<DroppedFileEventArgs> Drop { get; set; }

        /// <summary>A drag carrying an accepted file moved over this target
        /// (platforms that report the drag, not just the drop). Optional.</summary>
        public Action<DroppedFileEventArgs> DragOver { get; set; }

        /// <summary>That drag left this target, was dropped, or was abandoned.
        /// Optional.</summary>
        public Action DragLeave { get; set; }

        public TVFileDrop() { }

        public TVFileDrop(Func<string, bool> accepts, Action<DroppedFileEventArgs> drop)
        {
            Accepts = accepts;
            Drop = drop;
        }

        /// <summary>A registration for these extensions (".wav", with the dot,
        /// any case); "*" takes every file.</summary>
        public static TVFileDrop For(IEnumerable<string> extensions, Action<DroppedFileEventArgs> drop)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string extension in extensions)
            {
                set.Add(extension);
            }

            return new TVFileDrop(name => set.Contains("*") || set.Contains(ExtensionOf(name)), drop);
        }

        public bool Takes(string name) => Accepts?.Invoke(name ?? "") == true;

        /// <summary>The lowercase extension of <paramref name="name"/> with its
        /// dot, or "" when it has none.</summary>
        public static string ExtensionOf(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            int dot = name.LastIndexOf('.');
            int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            return dot > slash && dot < name.Length - 1 ? name.Substring(dot).ToLowerInvariant() : "";
        }
    }
}
