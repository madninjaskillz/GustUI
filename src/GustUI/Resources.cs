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
        public IContentManager VirtualContent;
        public IContentManager UUContent;
        public ContentManager Content;
        public DrawManager DrawManager;
        public RenderTarget2D RenderTarget;
        public DebugMode DebugMode = DebugMode.None;
        public Resources(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, VirtualContent virtualContent, ContentManager content, WindowElement root)
        {
            RootWindow = root;
            this.GraphicsDevice = graphicsDevice;
            Pixel = new Texture2D(graphicsDevice, 1, 1);
            Pixel.SetData<Color>(new Color[1] { Color.White });
            VirtualContent = virtualContent;
            Content = content;
            UUContent = new UUContent(new _Embedded.UUContentDictionary());

            FontManager = new FontManager(graphicsDevice, UUContent);
            InputManager = new InputManager();
            DrawOOPManager = new DrawOOPManager();
            DrawManager = new DrawManager(spriteBatch);
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
