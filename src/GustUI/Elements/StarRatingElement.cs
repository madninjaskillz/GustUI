using System;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements
{
    /// <summary>
    /// A row of five stars, in the two jobs a star row ever has: SHOWING a
    /// score (fractional — 4.3 lights four stars and a third of the fifth) and
    /// ASKING for one (whole stars, hover-previewed, click to commit).
    ///
    /// One element rather than two because the picture must be identical: a
    /// person who rates something four and then sees the average is comparing
    /// two rows of stars, and two implementations of a star drift in size,
    /// colour and gap the first time either is touched. <see cref="Interactive"/>
    /// is the only difference — a read-only row takes no input at all, so it
    /// can sit inside a card that is itself clickable without stealing the
    /// click.
    ///
    /// The stars are GEOMETRY, not glyphs: a font's ★ is a different shape and
    /// weight in every face, cannot be half-filled, and is not in every font
    /// the app ships. A polygon is the same at 9px on a store tile and at 22px
    /// in a rating picker, and the fractional fill is a scissor rectangle over
    /// the filled copy rather than a second, sadder shape.
    /// </summary>
    [ElementTraits(typeof(PositionTrait), typeof(SizeTrait))]
    public class StarRatingElement : Element
    {
        /// <summary>How many stars the row has. Five everywhere; a property
        /// because the geometry does not care.</summary>
        public int Stars { get; set; } = 5;

        /// <summary>The score, 0..<see cref="Stars"/>. Fractional when the row
        /// is showing an average; whole when it is asking for one.</summary>
        public float Value { get; set; }

        /// <summary>
        /// Takes clicks: hovering previews a whole-star score and clicking
        /// commits it through <see cref="OnValueChanged"/>. Off by default —
        /// most star rows in the app are read-only.
        ///
        /// Turning it on is what ATTACHES the mouse traits, and that is the
        /// point rather than an implementation detail: a read-only row has no
        /// mouse traits at all, so it cannot swallow a click meant for whatever
        /// it is sitting on. A store tile is one big button with a star row
        /// drawn on it, and stars that ate the click would make the score the
        /// one part of the card you cannot click through.
        /// </summary>
        public bool Interactive
        {
            get => interactive;
            set
            {
                interactive = value;
                if (value)
                {
                    AttachInput();
                }
            }
        }

        /// <summary>The width and height of ONE star, px. Null takes the
        /// element's own height, which is what a row inside a text line
        /// wants.</summary>
        public float? StarPixels { get; set; }

        /// <summary>Gap between stars, px. Scales with the star when it is
        /// left null, so a 9px row on a tile and a 22px picker look like the
        /// same object at two sizes.</summary>
        public float? Gap { get; set; }

        public Color FilledColor { get; set; } = new Color(240, 190, 90);

        public Color EmptyColor { get; set; } = new Color(88, 88, 102);

        /// <summary>What a hovered star looks like while an interactive row is
        /// being aimed at. Ignored when <see cref="Interactive"/> is off.</summary>
        public Color HoverColor { get; set; } = new Color(255, 214, 130);

        /// <summary>Raised with the committed whole-star score, 1..<see cref="Stars"/>.</summary>
        public Action<int> OnValueChanged;

        private bool interactive;
        private bool hovering;
        private float hoverX;
        private bool pressed;
        private bool inputAttached;

        private void AttachInput()
        {
            if (inputAttached)
            {
                return;
            }

            inputAttached = true;

            AddTrait<OnEnterTrait>().Set(new TVEvent<ClickEventArgs>(args =>
            {
                hovering = true;
                hoverX = args.MouseState.X;
            }));

            AddTrait<OnExitTrait>().Set(new TVEvent<ClickEventArgs>(_ =>
            {
                hovering = false;
                pressed = false;
            }));

            AddTrait<OnMousePress>().Set(new TVEvent<ClickEventArgs>(args =>
            {
                hoverX = args.MouseState.X;
                pressed = Interactive;
            }));

            // Press THEN release on the row, the rule CardGrid's tiles follow:
            // a bare release fires for a drag that merely finished here.
            AddTrait<OnMouseRelease>().Set(new TVEvent<ClickEventArgs>(args =>
            {
                if (!pressed || !Interactive)
                {
                    pressed = false;
                    return;
                }

                pressed = false;
                int picked = StarAt(args.MouseState.X);
                if (picked <= 0)
                {
                    return;
                }

                Value = picked;
                OnValueChanged?.Invoke(picked);
            }));
        }

        /// <summary>The width a row of <paramref name="stars"/> stars takes at
        /// this size — what a caller sizing a line of "stars then text" needs
        /// before either exists.</summary>
        public static float WidthFor(float starPixels, int stars = 5, float? gap = null)
            => stars <= 0 ? 0 : (stars * starPixels) + ((stars - 1) * (gap ?? DefaultGap(starPixels)));

        private static float DefaultGap(float starPixels) => Math.Max(1f, starPixels * 0.18f);

        public override void Draw()
        {
            var manager = Resources.StaticResources.DrawManager;
            Vector2 pos = this.GetActualXnaPosition();
            Vector2 size = this.GetSize().AsXna;

            float star = StarPixels ?? size.Y;
            if (star < 3f || Stars <= 0)
            {
                base.Draw();
                return;
            }

            float gap = Gap ?? DefaultGap(star);
            float top = pos.Y + ((size.Y - star) / 2f);

            // A hovered interactive row shows what a click would DO, not what
            // the value is: the preview is the whole point of hovering. The
            // pointer is read here rather than from a move event because there
            // is no move event — enter and press are the only two the element
            // gets, and a preview that only updated on those would stick to
            // whichever star the pointer entered by.
            if (interactive && hovering && Resources.StaticResources.InputManager != null)
            {
                hoverX = Resources.StaticResources.InputManager.CurrentMouseState.X;
            }

            int preview = Interactive && hovering ? StarAt(hoverX) : 0;
            float shown = preview > 0 ? preview : Value;
            Color lit = preview > 0 ? HoverColor : FilledColor;

            for (int i = 0; i < Stars; i++)
            {
                float left = pos.X + (i * (star + gap));
                var centre = new Vector2(left + (star / 2f), top + (star / 2f));
                float radius = star / 2f;

                FillStar(manager, centre, radius, EmptyColor);

                float fraction = MathHelper.Clamp(shown - i, 0f, 1f);
                if (fraction <= 0.02f)
                {
                    continue;
                }

                if (fraction >= 0.98f)
                {
                    FillStar(manager, centre, radius, lit);
                    continue;
                }

                // The part-lit star: the SAME shape, cut off at the fraction.
                // Scissoring the filled copy keeps the silhouette exact — a
                // "half star" drawn as its own polygon is a different shape
                // and reads as a rendering fault beside its whole neighbours.
                manager.PushScissor(new Rectangle(
                    (int)Math.Floor(left), (int)Math.Floor(top),
                    (int)Math.Ceiling(star * fraction), (int)Math.Ceiling(star)));
                FillStar(manager, centre, radius, lit);
                manager.PopScissor();
            }

            base.Draw();
        }

        /// <summary>Which star a screen X is over, 1..<see cref="Stars"/>, or 0
        /// for none. Whole stars: half a star is a thing an average can be and
        /// not a thing a person can mean.</summary>
        private int StarAt(float screenX)
        {
            Vector2 pos = this.GetActualXnaPosition();
            Vector2 size = this.GetSize().AsXna;
            float star = StarPixels ?? size.Y;
            float gap = Gap ?? DefaultGap(star);
            float pitch = star + gap;
            if (pitch <= 0)
            {
                return 0;
            }

            int index = (int)Math.Floor((screenX - pos.X) / pitch);
            return index < 0 ? 0 : Math.Min(Stars, index + 1);
        }

        /// <summary>
        /// One five-pointed star as a triangle fan around its centre: ten
        /// perimeter points alternating outer and inner radius, ten triangles.
        /// The inner radius is 0.382 of the outer — the pentagram's own ratio,
        /// which is what makes the points read as points rather than as a
        /// spiky decagon.
        /// </summary>
        private static void FillStar(Managers.DrawManager manager, Vector2 centre, float radius, Color color)
        {
            const int Points = 5;
            const float InnerRatio = 0.382f;

            var vertices = new VertexPositionColor[(Points * 2) + 2];
            vertices[0] = new VertexPositionColor(new Vector3(centre, 0f), color);

            for (int i = 0; i <= Points * 2; i++)
            {
                int step = i % (Points * 2);
                float r = step % 2 == 0 ? radius : radius * InnerRatio;

                // -90 degrees: a star's first point is straight up.
                float angle = (-MathHelper.PiOver2) + (step * MathHelper.Pi / Points);
                vertices[i + 1] = new VertexPositionColor(
                    new Vector3(
                        centre.X + ((float)Math.Cos(angle) * r),
                        centre.Y + ((float)Math.Sin(angle) * r),
                        0f),
                    color);
            }

            var indices = new short[Points * 2 * 3];
            for (int i = 0; i < Points * 2; i++)
            {
                indices[i * 3] = 0;
                indices[(i * 3) + 1] = (short)(i + 1);
                indices[(i * 3) + 2] = (short)(i + 2);
            }

            manager.DrawTriangles(vertices, indices, Points * 2);
        }
    }
}
