using System;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    /// <summary>
    /// The bar/beat strip above a <see cref="PianoRollElement"/> (bug board
    /// #211) — the same readout the sequencer's timeline has, and for the same
    /// reason: without it there is nothing on screen that says which bar you
    /// are looking at, and after a zoom or a scroll the grid alone cannot say.
    ///
    /// A SEPARATE element rather than a strip inside the roll, deliberately.
    /// The roll's y axis is pitch, and every hit test on it — a note, a bend
    /// vertex, a key in the gutter — maps y straight to a semitone row. A band
    /// carved out of its top would have shifted all of that by a constant that
    /// every one of those would have to remember to subtract.
    ///
    /// It is given the roll's OWN viewport fields (<see cref="FirstBeat"/>,
    /// <see cref="PixelsPerBeat"/>, <see cref="KeyboardWidth"/>) rather than a
    /// copy of the numbers, so "it follows zoom and scrolling" is not
    /// something anybody has to keep in sync.
    /// </summary>
    [ElementTraits(typeof(PositionTrait), typeof(SizeTrait))]
    public class PianoRollRulerElement : Element
    {
        /// <summary>Beat at the left edge of the grid.</summary>
        public double FirstBeat;

        public float PixelsPerBeat = 40f;

        public int BeatsPerBar = 4;

        /// <summary>Length of the pattern; past this the strip is dimmed, the
        /// same way the roll dims the void past the end.</summary>
        public double BeatsVisible = 16;

        /// <summary>Left inset matching the roll's keyboard gutter, so bar 1
        /// is over the same pixel in both.</summary>
        public float KeyboardWidth;

        /// <summary>Where the transport is, in beats; negative to hide.</summary>
        public double PlayheadBeats = -1;

        public Color BackColor = new Color(22, 22, 28);
        public Color TickColor = new Color(70, 70, 84);
        public Color BeatTickColor = new Color(48, 48, 58);
        public Color LabelColor = new Color(150, 150, 165);
        public Color BaseLineColor = new Color(70, 70, 84);
        public Color PastEndColor = new Color(0, 0, 0) * 0.45f;
        public Color PlayheadColor = new Color(255, 90, 90);

        private Managers.SdfFont font;

        public PianoRollRulerElement()
        {
            Set<PositionTrait>(new TVVector(0, 0));
            Set<SizeTrait>(new TVVector(10, 18));
        }

        public float XForBeat(double beats)
            => KeyboardWidth + (float)((beats - FirstBeat) * PixelsPerBeat);

        public override void Draw()
        {
            Managers.DrawManager manager = Resources.StaticResources.DrawManager;
            Vector2 pos = this.GetActualXnaPosition();
            Vector2 size = this.GetSize().AsXna;
            int width = (int)size.X;
            int height = (int)size.Y;
            if (width < 8 || height < 6)
            {
                base.Draw();
                return;
            }

            int x0 = (int)pos.X;
            int y0 = (int)pos.Y;
            int gridX = x0 + (int)KeyboardWidth;
            manager.DrawFilledRectangle(new Rectangle(x0, y0, width, height), BackColor);

            // No zoom yet (the view has not fitted the pattern), so a bar is
            // zero pixels wide and the loop below would never reach the right
            // edge. Draw the empty strip and wait for a real number.
            if (PixelsPerBeat <= 0.01f)
            {
                manager.DrawFilledRectangle(new Rectangle(x0, y0 + height - 1, width, 1), BaseLineColor);
                base.Draw();
                return;
            }

            font ??= Resources.StaticResources.FontManager.LoadSdfFont(Resources.StaticResources.Theme.UiFontSmall.Family);
            float labelSize = Math.Clamp(height - 6f, 8f, 11f);

            // Bars get a full tick and a number; beats get a short one, and
            // only while there is room for them to mean anything. A ruler that
            // draws a tick every three pixels is a grey bar.
            float pxPerBar = PixelsPerBeat * Math.Max(1, BeatsPerBar);
            bool drawBeats = PixelsPerBeat >= 14f;
            int labelEvery = pxPerBar >= 46f ? 1 : pxPerBar >= 23f ? 2 : pxPerBar >= 12f ? 4 : 8;

            long firstBar = (long)Math.Floor(FirstBeat / Math.Max(1, BeatsPerBar));

            // Bounded as well as terminated by the right edge: this runs
            // inside Draw, and a viewport that has gone strange must cost a
            // blank ruler, never a frame that does not end.
            long lastBar = firstBar + (long)(width / Math.Max(1f, pxPerBar)) + 2;
            for (long bar = Math.Max(0, firstBar); bar <= lastBar; bar++)
            {
                double beat = bar * (double)Math.Max(1, BeatsPerBar);
                int x = (int)XForBeat(beat) + x0;
                if (x >= x0 + width)
                {
                    break;
                }

                if (x >= gridX)
                {
                    manager.DrawFilledRectangle(new Rectangle(x, y0 + 2, 1, height - 3), TickColor);
                    if (bar % labelEvery == 0)
                    {
                        string label = (bar + 1).ToString();
                        manager.DrawSdfString(font, label, new Vector2(x + 3, y0 + 1), labelSize, LabelColor);
                    }
                }

                if (!drawBeats)
                {
                    continue;
                }

                for (int b = 1; b < Math.Max(1, BeatsPerBar); b++)
                {
                    int bx = (int)XForBeat(beat + b) + x0;
                    if (bx >= gridX && bx < x0 + width)
                    {
                        manager.DrawFilledRectangle(new Rectangle(bx, y0 + height - 5, 1, 4), BeatTickColor);
                    }
                }
            }

            // Past the pattern's end, dimmed to match the grid below.
            int endX = x0 + (int)XForBeat(BeatsVisible);
            if (endX < x0 + width)
            {
                int dimX = Math.Max(gridX, endX);
                manager.DrawFilledRectangle(new Rectangle(dimX, y0, x0 + width - dimX, height), PastEndColor);
            }

            if (PlayheadBeats >= 0)
            {
                int px = x0 + (int)XForBeat(PlayheadBeats);
                if (px >= gridX && px < x0 + width)
                {
                    manager.DrawFilledRectangle(new Rectangle(px, y0, 2, height), PlayheadColor);
                }
            }

            manager.DrawFilledRectangle(new Rectangle(x0, y0 + height - 1, width, 1), BaseLineColor);
            base.Draw();
        }
    }
}
