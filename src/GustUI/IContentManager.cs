using Microsoft.Xna.Framework.Graphics;
using System.IO;

namespace GustUI
{
    public interface IContentManager
    {
        Stream OpenStream(string name);
        byte[] ReadAllBytes(string name);
        Texture2D GetTexture(string name);
    }
}
