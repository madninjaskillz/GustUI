using GustUI.Elements;
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
        public GraphicsDevice GraphicsDevice;
        public Texture2D Pixel;
        public WindowElement RootWindow;
        public Resources(GraphicsDevice graphicsDevice, VirtualContent content, WindowElement root)
        {
            RootWindow = root;
            this.GraphicsDevice = graphicsDevice;
            Pixel = new Texture2D(graphicsDevice, 1, 1);
            Pixel.SetData<Color>(new Color[1] { Color.White });

            FontManager = new FontManager(graphicsDevice, content);  
            Theme = new Theme();
        }

        public FontManager FontManager;

        public Theme Theme;

        public static Resources StaticResources;
    }
}
