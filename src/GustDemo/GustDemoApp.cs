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

        public TVFont UiFont { get; private set; }

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

            UiFont = new TVFont() { Family = "C:\\Windows\\Fonts\\segoeuisl.ttf", Size = 72, Border = 0 };

            FilledRectangleElement rectangle = new(300, 300, 200, 80, new TVFillSimpleGradient(GraphicsDevice,
                                                                                               Color.Red,
                                                                                               Color.Blue,
                                                                                               Direction.Horizontally), 2, Color.Red);

            BasicButtonElement button = new BasicButtonElement(
                UiFont, 
                "hello",
                Color.White,
                new TVFillSimpleGradient(GraphicsDevice, Color.Blue,Color.Black, Direction.Vertically),
                new TVVector(40,40),
                new TVVector(200,80));

            button.Set<OnClickTrait>(new TVEvent((TVEventArgs sender) => 
            {
                Debug.WriteLine("Oh hai!");
                button.Set<PositionTrait>(button.ElementTrait<PositionTrait>().Value() + new TVVector(10, 10));
                Debug.WriteLine(sender);
            }));

            _window.AddChild(button);
            _window.AddChild(rectangle);

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
