using GustUI._Embedded;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    public class UUContent : IContentManager
    {
        private UUContentDictionary _dictionary;

        public UUContent(UUContentDictionary dictionary)
        {
            _dictionary = dictionary;

        }

        public Texture2D GetTexture(string name)
        {
            return Texture2D.FromStream(Resources.StaticResources.GraphicsDevice, OpenStream(name));
        }

        public Stream OpenStream(string name)
        {
            return new MemoryStream(Convert.FromBase64String(_dictionary.Files[name]));
        }

        public byte[] ReadAllBytes(string name)
        {
            return Convert.FromBase64String(_dictionary.Files[name]);
        }
    }
}
