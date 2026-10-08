using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GustUI
{
    /// <summary>
    /// GustUI's built-in content: the default fonts (Segoe UI, Segoe icon
    /// fonts, Segoe UI Symbol, Comfortaa) and the nine-grid shadow and
    /// modifier-key images, in <c>_Embedded/Content</c>.
    ///
    /// They are manifest resources, read straight out of the loaded assembly
    /// when something asks. Until 2026-10-08 they were base64 string literals
    /// in a generated dictionary: about 6.8 MB of base64, held as UTF-16 for
    /// the life of the process (~13 MB) whether or not a font was ever baked
    /// again. Now the only managed copy is the transient byte array a font
    /// bake reads, which is garbage as soon as its atlas exists.
    /// </summary>
    public class UUContent : IContentManager
    {
        /// <summary>The prefix the csproj's LogicalName gives every file, so a
        /// name here is the file's own name and nothing else.</summary>
        internal const string ResourcePrefix = "GustUI.Embedded.";

        private static Assembly Assembly => typeof(UUContent).Assembly;

        /// <summary>Every file this content manager can open.</summary>
        public static IReadOnlyList<string> Names { get; } = Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, System.StringComparison.Ordinal))
            .Select(n => n.Substring(ResourcePrefix.Length))
            .ToArray();

        public Texture2D GetTexture(string name)
        {
            using Stream stream = OpenStream(name);
            return Texture2D.FromStream(Resources.StaticResources.GraphicsDevice, stream);
        }

        /// <summary>A read-only, seekable stream over the file as it sits in
        /// the assembly image — no copy is made until the caller reads.</summary>
        public Stream OpenStream(string name)
        {
            return Assembly.GetManifestResourceStream(ResourcePrefix + name)
                ?? throw new FileNotFoundException("GustUI has no embedded content called '" + name + "'.", name);
        }

        public byte[] ReadAllBytes(string name)
        {
            using Stream stream = OpenStream(name);
            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
    }
}
