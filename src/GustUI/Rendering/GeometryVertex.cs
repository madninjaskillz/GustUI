using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.InteropServices;

namespace GustUI.Rendering
{
    /// <summary>
    /// Vertex format for the batched geometry renderer (<see cref="GeometryBatch"/>)
    /// that is replacing DrawManager's SpriteBatch backend
    /// (DrawManager.UseGeometryBackend). Position.Z is ALWAYS 0 and must
    /// never be run through RenderScale's Matrix.CreateScale — see
    /// DrawManager.Begin()'s own doc comment for the exact bug this would
    /// reintroduce (scaling Z pushed layerDepth-1 sprites outside the clip
    /// frustum and silently discarded every glyph).
    ///
    /// ClipRect carries PushScissor/PopScissor's current scissor rect
    /// per-vertex (device-pixel xMin, yMin, xMax, yMax) so clipping is a
    /// fragment-shader discard test (GeometryBatch.fx) instead of a
    /// GraphicsDevice.ScissorRectangle change — the latter can only change
    /// between SpriteBatch batches, which is the forced-flush cost this
    /// format exists to eliminate.
    /// </summary>
    // Sequential+Pack=1, matching every stock XNA/MonoGame vertex struct
    // (e.g. VertexPositionColor) — without this, the CLR is free to
    // reorder these fields for alignment, which would silently desync the
    // real in-memory byte layout from the offsets VertexDeclaration below
    // declares (found the hard way: an earlier version of this struct
    // omitted the attribute and every geometry-backend draw rendered
    // nothing, with no error — the GPU was reading garbage
    // positions/clip-rects off a shuffled layout, not failing to draw).
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GeometryVertex : IVertexType
    {
        public Vector3 Position;
        public Color Color;
        public Vector2 UV;
        public Vector4 ClipRect;

        public GeometryVertex(Vector2 position, Color color, Vector2 uv, Vector4 clipRect)
        {
            Position = new Vector3(position, 0f);
            Color = color;
            UV = uv;
            ClipRect = clipRect;
        }

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 0),
            new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(24, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1));

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}
