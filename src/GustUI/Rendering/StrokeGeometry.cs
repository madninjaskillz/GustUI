using Microsoft.Xna.Framework;
using System;

namespace GustUI.Rendering
{
    /// <summary>
    /// An ANTIALIASED stroke along a polyline, written as plain triangles: the
    /// one place the maths for every line GustUI draws lives (wires, curves,
    /// automation lanes, knob pointers, <c>DrawThickLine</c> itself), so the
    /// immediate path and anything that builds the same stroke ahead of time
    /// (<see cref="Elements.GlowCurveElement"/>'s cache) cannot drift.
    ///
    /// <para><b>Why it exists (2026-10-05).</b> Lines used to be one rotated
    /// quad per segment with its start truncated to whole pixels, square
    /// ends, and no soft edge at all — wires and curves came out as hard
    /// staircases with notches at every joint, and a translucent wire went
    /// darker wherever two segments overlapped. Circles, rings and rounded
    /// boxes had been antialiased since 2026-08-13; strokes never were.</para>
    ///
    /// <para><b>How.</b> One joined strip down the whole line, four vertex rows
    /// across it: transparent, opaque, opaque, transparent. The true edge sits
    /// on the alpha 1→0 crossing, so the soft band straddles it rather than
    /// eating into it — the same trick as <c>AppendFeatheredFill</c>, with the
    /// same width: one PHYSICAL pixel (1 / RenderScale in the logical units
    /// every draw uses), whatever the display scaling. Vertex alpha only, so no
    /// shader change and no extra draw call: a stroke shares the white texel
    /// and segment every other flat shape is in.</para>
    ///
    /// <list type="bullet">
    /// <item>Joints are mitred along the averaged normal, the mitre clamped so a
    /// spike in a scope trace cannot throw a vertex across the panel. One strip
    /// means no overlap, so a translucent line is one even colour.</item>
    /// <item>The two open ends get a transparent cap column one feather past the
    /// end, so the ends are soft too.</item>
    /// <item>Thinner than a physical pixel, the opaque core collapses to the
    /// centre line and the colour scales down by the coverage it actually has —
    /// a correctly faint hairline, not a fat grey one.</item>
    /// <item>A segment that runs exactly horizontal or vertical has its centre
    /// line moved onto the pixel grid — onto pixel CENTRES for an odd physical
    /// width, onto pixel EDGES for an even one — so a flat automation run or an
    /// orthogonal wire stays a crisp line instead of two half-strength rows.
    /// Diagonals are left exactly where they are and get the soft edge.</item>
    /// </list>
    /// </summary>
    public static class StrokeGeometry
    {
        /// <summary>Vertices a stroke through <paramref name="points"/> points
        /// can write, at most (two cap columns included).</summary>
        public static int MaxVertices(int points) => (points + 2) * 4;

        /// <summary>Indices a stroke through <paramref name="points"/> points
        /// can write, at most.</summary>
        public static int MaxIndices(int points) => (points + 1) * 18;

        /// <summary>Mitre length limit, in half-widths. Four keeps a sharp
        /// turn readable without letting a near-reversal spike out.</summary>
        private const float MaxMiter = 4f;

        [ThreadStatic] private static Vector2[] scratchPoints;
        [ThreadStatic] private static Color[] scratchColors;

        /// <summary>
        /// Writes the stroke into <paramref name="verts"/> from
        /// <paramref name="vAt"/> and <paramref name="indices"/> from
        /// <paramref name="iAt"/>; indices are relative to
        /// <paramref name="indexBase"/> (the index <paramref name="vAt"/> will
        /// have in whatever the caller hands them to). Both arrays must have
        /// room for <see cref="MaxVertices"/>/<see cref="MaxIndices"/>.
        /// </summary>
        /// <param name="colors">One colour per point, or a single colour for the
        /// whole line.</param>
        /// <param name="renderScale">Physical pixels per logical pixel — what
        /// sizes the soft edge and the pixel grid the flat runs snap to.</param>
        public static void Write(
            ReadOnlySpan<Vector2> points, ReadOnlySpan<Color> colors, float thickness, float renderScale,
            Vector2 uv, Vector4 clip,
            GeometryVertex[] verts, int vAt, short[] indices, int iAt, int indexBase,
            out int vertexCount, out int indexCount, bool capStart = true, bool capEnd = true)
        {
            vertexCount = 0;
            indexCount = 0;
            if (points.Length < 2 || colors.Length == 0 || thickness <= 0.0001f)
            {
                return;
            }

            float scale = Math.Max(0.01f, renderScale);
            float feather = 1f / scale;

            // Half-widths of the opaque core and of the whole soft footprint,
            // and how much of the colour survives. Both profiles integrate to
            // exactly `thickness`, so a line neither gains nor loses weight
            // from being antialiased.
            float inner, outer, coverage;
            if (thickness >= feather)
            {
                inner = (thickness - feather) * 0.5f;
                outer = (thickness + feather) * 0.5f;
                coverage = 1f;
            }
            else
            {
                inner = 0f;
                outer = feather;
                coverage = thickness / feather;
            }

            // Copy (snapping flat runs to the grid) and drop zero-length steps,
            // which have no direction to build a normal from.
            int n = points.Length;
            Vector2[] p = Scratch(ref scratchPoints, n);
            Color[] c = Scratch(ref scratchColors, n);
            int physicalWidth = (int)Math.Round(thickness * scale);
            bool oddWidth = physicalWidth <= 1 || physicalWidth % 2 == 1;
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 point = points[i];
                bool flatX = (i > 0 && Math.Abs(points[i].Y - points[i - 1].Y) < 0.0001f && Math.Abs(points[i].X - points[i - 1].X) > 0.0001f)
                    || (i < n - 1 && Math.Abs(points[i + 1].Y - points[i].Y) < 0.0001f && Math.Abs(points[i + 1].X - points[i].X) > 0.0001f);
                bool flatY = (i > 0 && Math.Abs(points[i].X - points[i - 1].X) < 0.0001f && Math.Abs(points[i].Y - points[i - 1].Y) > 0.0001f)
                    || (i < n - 1 && Math.Abs(points[i + 1].X - points[i].X) < 0.0001f && Math.Abs(points[i + 1].Y - points[i].Y) > 0.0001f);
                if (flatX)
                {
                    point.Y = Snap(point.Y, scale, oddWidth);
                }

                if (flatY)
                {
                    point.X = Snap(point.X, scale, oddWidth);
                }

                Color color = (colors.Length == n ? colors[i] : colors[0]) * coverage;
                if (count > 0 && Vector2.DistanceSquared(point, p[count - 1]) < 1e-8f)
                {
                    c[count - 1] = color;
                    continue;
                }

                p[count] = point;
                c[count] = color;
                count++;
            }

            if (count < 2)
            {
                return;
            }

            int column = 0;
            Vector2 firstDir = Vector2.Normalize(p[1] - p[0]);
            if (capStart)
            {
                WriteColumn(verts, vAt, column++, p[0] - (firstDir * feather), Perp(firstDir), inner, outer, Color.Transparent, uv, clip);
            }

            for (int i = 0; i < count; i++)
            {
                Vector2 offset;
                if (i == 0)
                {
                    offset = Perp(firstDir);
                }
                else if (i == count - 1)
                {
                    offset = Perp(Vector2.Normalize(p[i] - p[i - 1]));
                }
                else
                {
                    Vector2 n0 = Perp(Vector2.Normalize(p[i] - p[i - 1]));
                    Vector2 n1 = Perp(Vector2.Normalize(p[i + 1] - p[i]));
                    Vector2 sum = n0 + n1;
                    if (sum.LengthSquared() < 1e-6f)
                    {
                        // Folds straight back on itself: no mitre exists.
                        offset = n0;
                    }
                    else
                    {
                        Vector2 miter = Vector2.Normalize(sum);
                        float along = Vector2.Dot(miter, n0);
                        offset = miter * Math.Min(MaxMiter, 1f / Math.Max(0.0001f, along));
                    }
                }

                WriteColumn(verts, vAt, column++, p[i], offset, inner, outer, c[i], uv, clip);
            }

            if (capEnd)
            {
                Vector2 lastDir = Vector2.Normalize(p[count - 1] - p[count - 2]);
                WriteColumn(verts, vAt, column++, p[count - 1] + (lastDir * feather), Perp(lastDir), inner, outer, Color.Transparent, uv, clip);
            }

            int ii = iAt;
            for (int col = 0; col < column - 1; col++)
            {
                int a = indexBase + (col * 4);
                int b = a + 4;
                for (int row = 0; row < 3; row++)
                {
                    indices[ii++] = (short)(a + row);
                    indices[ii++] = (short)(b + row);
                    indices[ii++] = (short)(b + row + 1);
                    indices[ii++] = (short)(a + row);
                    indices[ii++] = (short)(b + row + 1);
                    indices[ii++] = (short)(a + row + 1);
                }
            }

            vertexCount = column * 4;
            indexCount = ii - iAt;
        }

        private static void WriteColumn(GeometryVertex[] verts, int vAt, int column, Vector2 at, Vector2 normal,
            float inner, float outer, Color color, Vector2 uv, Vector4 clip)
        {
            int v = vAt + (column * 4);
            Color clear = color * 0f;
            verts[v] = new GeometryVertex(at + (normal * outer), clear, uv, clip);
            verts[v + 1] = new GeometryVertex(at + (normal * inner), color, uv, clip);
            verts[v + 2] = new GeometryVertex(at - (normal * inner), color, uv, clip);
            verts[v + 3] = new GeometryVertex(at - (normal * outer), clear, uv, clip);
        }

        private static Vector2 Perp(Vector2 direction) => new Vector2(-direction.Y, direction.X);

        /// <summary>Moves a coordinate onto the physical pixel grid: the nearest
        /// pixel centre for an odd width, the nearest pixel edge for an even
        /// one, which is where a line of that width covers whole pixels.</summary>
        public static float Snap(float value, float renderScale, bool oddWidth)
        {
            float physical = value * renderScale;
            float snapped = oddWidth ? (float)Math.Floor(physical) + 0.5f : (float)Math.Round(physical);
            return snapped / renderScale;
        }

        private static T[] Scratch<T>(ref T[] buffer, int length)
        {
            if (buffer == null || buffer.Length < length)
            {
                buffer = new T[Math.Max(length, Math.Max(64, (buffer?.Length ?? 0) * 2))];
            }

            return buffer;
        }
    }
}
