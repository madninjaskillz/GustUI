using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;

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

            FilledRectangleElement rectangle = new();
            rectangle.Set<SizeTrait, TVVector>(new TVVector(200, 80));
            rectangle.Set<PositionTrait, TVVector>(new TVVector(300, 300));
            
            rectangle.Set<BackgroundColorTrait, TVColor>(new TVColor(Color.Pink));

            _window.ElementTrait<ChildrenTrait>().Value().Add(rectangle);

            BasicButtonElement button = new BasicButtonElement();
            button.Set<FontTrait>(new TVFont() { Family = "C:\\Windows\\Fonts\\arial.ttf", Size = 72, Border = 0 });
            button.Set<ForegroundColorTrait>(new TVColor(Color.Pink));
            button.Set<BackgroundColorTrait>(new TVColor(Color.DarkGreen));
            button.Set<TextTrait>(new TVText() { Text = "Hello!" });
            button.Set<PositionTrait>(new TVVector(40, 40));
            button.Set<SizeTrait>(new TVVector(200, 80));

            _window.ElementTrait<ChildrenTrait>().Value().Add(button);

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
