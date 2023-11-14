using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;
using System.Diagnostics;

namespace GustDemo
{
    public class GustDemoApp : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private WindowElement _window;

        public GustDemoApp()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            this.Window.AllowUserResizing = true;

        }

        protected override void Initialize()
        {
            // TODO: Add your initialization logic here

            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _window = new WindowElement(this.Window, GraphicsDevice);

            FilledRectangleElement rectangle = new(300, 300, 200, 80, Color.Pink ,2, Color.Red);
            
            BasicButtonElement button = new BasicButtonElement(
                new TVFont() { Family = "C:\\Windows\\Fonts\\arial.ttf", Size = 72, Border = 0 }, 
                "hello",
                Color.White,
                Color.Blue,
                new TVVector(40,40),
                new TVVector(200,80));

            button.Set<OnClickTrait>(new TVEvent((object sender) => 
            {
                Debug.WriteLine("Oh hai!");
                Debug.WriteLine(sender);
            }));

            _window.Children.Add(button);
            _window.Children.Add(rectangle);

            // TODO: use this.Content to load your game content here
        }

        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            // TODO: Add your update logic here
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            _spriteBatch.Begin();
            _window.Draw(_spriteBatch, null);
            _spriteBatch.End();
            // TODO: Add your drawing code here

            base.Draw(gameTime);
        }
    }
}
