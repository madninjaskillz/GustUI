using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Microsoft.Xna.Framework.Media;
using System;
using System.Collections.Generic;
using GustUI;
using GustUI.Elements;
using GustUI.TraitValues;
using GustUI.Traits;
using System.Diagnostics;

namespace GustMultiplatformDemo
{
    /// <summary>
    /// This is the main type for your game.
    /// </summary>
    public class GustMultiplatformDemoGame : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private WindowElement _window;
        private VirtualContent virtualContent;
        
        public GustMultiplatformDemoGame()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            this.Window.AllowUserResizing = true;
            _graphics.SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;
#if (ANDROID || iOS)
            _graphics.IsFullScreen = true;
#endif
            this.Window.AllowUserResizing = true;

        }

        /// <summary>
        /// Allows the game to perform any initialization it needs to before starting to run.
        /// This is where it can query for any required services and load any non-graphic
        /// related content.  Calling base.Initialize will enumerate through any components
        /// and initialize them as well.
        /// </summary>
        protected override void Initialize()
        {
            // TODO: Add your initialization logic here

            base.Initialize();

        }

        /// <summary>
        /// LoadContent will be called once per game and is the place to load
        /// all of your content.
        /// </summary>
        protected override void LoadContent()
        {
            virtualContent = new VirtualContent("Content");
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _window = new WindowElement(this.Window, GraphicsDevice, virtualContent);

            var UiFont = new TVFont() { Family = "C:\\Windows\\Fonts\\segoeuisl.ttf", Size = 72, Border = 0 };

            FilledRectangleElement rectangle = new(300, 300, 200, 80, new TVFillSimpleGradient(
                                                                                               Color.Red,
                                                                                               Color.Blue,
                                                                                               Direction.Horizontally), 2, Color.Red);

            BasicButtonElement button = new BasicButtonElement(
                UiFont,
                "hello",
                Color.White,
                new TVFillSimpleGradient(Color.Blue, Color.Black, Direction.Vertically),
                new TVVector(40, 40),
                new TVVector(200, 80));

            button.Set<OnClickTrait>(new TVEvent((TVEventArgs sender) =>
            {
                Debug.WriteLine("Oh hai!");
                button.Set<PositionTrait>(button.ElementTrait<PositionTrait>().Value() + new TVVector(10, 10));
                Debug.WriteLine(sender);
            }));

            _window.AddChild(button, "button");
            _window.AddChild(rectangle, "rectangle");

        }

        /// <summary>
        /// UnloadContent will be called once per game and is the place to unload
        /// game-specific content.
        /// </summary>
        protected override void UnloadContent()
        {
            // TODO: Unload any non ContentManager content here
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Update(GameTime gameTime)
        {
            MouseState mouseState = Mouse.GetState();
            KeyboardState keyboardState = Keyboard.GetState();
            GamePadState gamePadState = GamePad.GetState(PlayerIndex.One);

            if (keyboardState.IsKeyDown(Keys.Escape) ||
                keyboardState.IsKeyDown(Keys.Back) ||
                gamePadState.Buttons.Back == ButtonState.Pressed)
            {
                try { Exit(); }
                catch (PlatformNotSupportedException) { /* ignore */ }
            }

            // TODO: Add your update logic here

            base.Update(gameTime);
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            // TODO: Add your drawing code here

            base.Draw(gameTime);
        }
    }
}
