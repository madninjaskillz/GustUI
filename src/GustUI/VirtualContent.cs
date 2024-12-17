using Microsoft.Xna.Framework.Graphics;
using nkast.Wasm.XHR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    public class VirtualContent : IContentManager
    {
        private string _rootDirectory;

        public VirtualContent(string rootDirectory)
        {
            _rootDirectory = rootDirectory.Replace("\\","/");

        }
        private string GetFullPath(string name)
        {
            return _rootDirectory + (_rootDirectory.EndsWith("/") ? "" : "/") + name;
        }
        public Stream OpenStream(string name)
        {
            XMLHttpRequest request = new XMLHttpRequest();

            string path = GetFullPath(name);
            request.Open("GET", path, false);
            request.OverrideMimeType("text/plain; charset=x-user-defined");
            request.Send();

            if (request.Status == 200)
            {
                string responseText = request.ResponseText;

                byte[] buffer = new byte[responseText.Length];
                for (int i = 0; i < responseText.Length; i++)
                    buffer[i] = (byte)(responseText[i] & 0xff);

                Stream ms = new MemoryStream(buffer);

                return ms;
            }
            else
            {
                throw new IOException("HTTP request failed. Status:" + request.Status);
            }
        }

        public byte[] ReadAllBytes(string name)
        {
            using (Stream stream = OpenStream(name))
            {
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                return buffer;
            }
        }

        public Texture2D GetTexture(string v)
        {
            return Texture2D.FromStream(Resources.StaticResources.GraphicsDevice, OpenStream(v));
        }
    }
}
