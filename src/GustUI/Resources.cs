using GustUI.Managers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    public class Resources
    {
        public Texture2D Pixel;
        public Resources(GraphicsDevice graphicsDevice)
        {
            Pixel = new Texture2D(graphicsDevice, 1, 1);
            Pixel.SetData<Color>(new Color[1] { Color.White });

            FontManager = new FontManager(graphicsDevice);  
        }

        public FontManager FontManager;

        public static Resources StaticResources;
    }
}
