using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.TraitValues
{
    public class TVFill : TraitValue
    {
        public Texture2D Texture { get; set; }
        public float Opacity { get; set; } = 1f;
    }

    public class TVSmartFill : TVFill
    {
        private ButtonStates buttonStates;
        public ButtonStates States { get; set; }
    }

    public class TVFillImage : TVFill
    {

        public Tiling Tiling { get; set; }

        /// <summary>Draw color the texture is multiplied by (default white =
        /// unchanged). Lets one grayscale/alpha texture serve many colored
        /// fills — e.g. waveform block faces tinted per channel.</summary>
        public Color Tint { get; set; } = Color.White;

        public TVFillImage SetOpacity(float opacity)
        {
            Opacity = opacity;
            return this;
        }
    }

    public class TVFillSolidColor : TVFill
    {
        public Color Color { get; set; }

        /// <summary>Optional live-computed alternative to <see cref="Color"/>
        /// — set via the <c>Func&lt;Color&gt;</c> constructor when a fill
        /// needs to track something that changes after construction (e.g.
        /// GustUI.Resources.StaticResources.Theme after a light/dark
        /// switch) without the owning view rebuilding or re-Setting the
        /// trait. Draw() reads <see cref="ResolvedColor"/>, not
        /// <see cref="Color"/>, so this is evaluated fresh every frame.</summary>
        private readonly Func<Color> colorFunc;

        public Color ResolvedColor => colorFunc != null ? colorFunc() : Color;

        public TVFillSolidColor() { }
        public TVFillSolidColor(Color color)
        {
            Color = color;
        }

        public TVFillSolidColor(Func<Color> colorFunc)
        {
            this.colorFunc = colorFunc;
        }
    }

    /// <summary>
    /// A solid fill with ROUNDED corners — the same one-line swap as
    /// <see cref="TVFillSolidColor"/>, drawn through
    /// <c>DrawManager.DrawRoundedRectangle</c> (radius-cached corner atlas,
    /// so a resizing element bakes nothing per frame). Optional
    /// <see cref="BorderColor"/> paints a rounded outline in the same pass,
    /// because a square <see cref="Traits.BorderSizeTrait"/> border around a
    /// rounded fill reads as a mistake.
    /// </summary>
    public class TVFillRoundedColor : TVFill
    {
        public Color Color { get; set; }

        public int Radius { get; set; } = 0;

        /// <summary>Null = no outline.</summary>
        public Color? BorderColor { get; set; }

        public int BorderSize { get; set; } = 1;

        /// <summary>Optional live-computed alternatives to <see cref="Color"/>/
        /// <see cref="BorderColor"/> — see TVFillSolidColor's matching
        /// field for why. Draw() reads <see cref="ResolvedColor"/>/
        /// <see cref="ResolvedBorderColor"/>, evaluated fresh every frame.</summary>
        private readonly Func<Color> colorFunc;
        private readonly Func<Color> borderColorFunc;

        public Color ResolvedColor => colorFunc != null ? colorFunc() : Color;
        public Color? ResolvedBorderColor => borderColorFunc != null ? borderColorFunc() : BorderColor;

        public TVFillRoundedColor() { }

        public TVFillRoundedColor(Color color, int radius = 0)
        {
            Color = color;
            Radius = radius;
        }

        public TVFillRoundedColor(Color color, int radius, Color borderColor, int borderSize = 1)
        {
            Color = color;
            Radius = radius;
            BorderColor = borderColor;
            BorderSize = borderSize;
        }

        public TVFillRoundedColor(Func<Color> colorFunc, int radius, Func<Color> borderColorFunc, int borderSize = 1)
        {
            this.colorFunc = colorFunc;
            Radius = radius;
            this.borderColorFunc = borderColorFunc;
            BorderSize = borderSize;
        }
    }

    public class TVVideoFill : TVFill
    {
        private Video video;
        private VideoPlayer player;
        public TVVideoFill(Video video)
        {
            this.video = video;
            player = new VideoPlayer();

        }

        private bool stopped;

        /// <summary>Stops playback for good; GetTexture will no longer restart the video.</summary>
        public void Stop()
        {
            stopped = true;
            try
            {
                player.Stop();
            }
            catch
            {
            }
        }

        public Texture2D GetTexture()
        {
            try
            {
                if (stopped)
                {
                    return null;
                }

                player.Volume = 0.0f;
                player.IsMuted = true;
                if (player.State == MediaState.Stopped)
                {
                    player.Play(video);
                }
                return player.GetTexture();
            }
            catch (Exception e)
            {

            }

            return null;
        }
    }

    public class TVBlurFill : TVFill
    {
        public float Ratio { get; set; }
        public TVBlurFill(float ratio, TVFill overlay)
        {
            Ratio = ratio;
            OverlayFill = overlay;
        }

        public TVFill OverlayFill { get; set; }
    }

    public class TVFillSimpleGradient : TVFill
    {
        public Color PrimaryColor { get; }
        public Color SecondaryColor { get; }
        public Direction Direction { get; }

        public TVFillSimpleGradient(Color primary, Color secondary, Direction direction)
        {
            int w = 1;
            int h = 1;
            if (direction == Direction.Horizontally)
            {
                w = 256;
            }
            else
            {
                h = 256;
            }

            Texture2D result = new Texture2D(Resources.StaticResources.GraphicsDevice, w, h);
            Color[] c = new Color[256];

            Color col = primary;
            for (int i = 0; i < 256; i++)
            {
                c[i] = col;
                col = Color.Lerp(primary, secondary, i / 255f);
            }

            result.SetData(c);
            this.Texture = result;
            PrimaryColor = primary;
            SecondaryColor = secondary;
            Direction = direction;
        }
    }

    public enum Tiling
    {
        None,
        Repeat,
        Stretch,
        Scale
    }

    public enum Direction
    {
        Horizontally,
        Vertically,
    }

}
