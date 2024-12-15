using GustUI.Elements;
using GustUI.Managers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
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
        public VirtualContent VirtualContent;
        public ContentManager Content;
        public Resources(GraphicsDevice graphicsDevice, VirtualContent virtualContent, ContentManager content, WindowElement root)
        {
            RootWindow = root;
            this.GraphicsDevice = graphicsDevice;
            Pixel = new Texture2D(graphicsDevice, 1, 1);
            Pixel.SetData<Color>(new Color[1] { Color.White });
            VirtualContent = virtualContent;
            Content = content;

            FontManager = new FontManager(graphicsDevice, virtualContent);
            InputManager = new InputManager();
            DrawOOPManager = new DrawOOPManager();
        }

        public void Update()
        {
            InputManager.Update();
            RootWindow.Update();
        }

        public FontManager FontManager;
        public InputManager InputManager;
        public DrawOOPManager DrawOOPManager;
        public Theme Theme;

        public static Resources StaticResources;
    }
}
