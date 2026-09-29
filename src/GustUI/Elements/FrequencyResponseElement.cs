using System;
using System.Collections.Generic;
using System.Globalization;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GustUI.Elements;

/// <summary>
/// One draggable point on a <see cref="FrequencyResponseElement"/>: an EQ
/// band, a filter's corner, a crossover. The element only draws and moves
/// these; what they MEAN (what a band's type does to the curve) is the host's,
/// through <see cref="FrequencyResponseElement.ResponseDb"/>.
/// </summary>
public sealed class ResponseNode
{
    public float Hz = 1000f;

    public float Db;

    public float Q = 1f;

    /// <summary>An off band is drawn as a hollow ring and adds nothing to
    /// the curve; it can still be grabbed and moved.</summary>
    public bool Enabled = true;

    /// <summary>Whether a horizontal drag moves the frequency. False for a
    /// point whose frequency is derived (a 3-band EQ's mid, between its
    /// splits).</summary>
    public bool MovesX = true;

    /// <summary>Whether a vertical drag moves the gain. A cut or a notch has
    /// no gain: it sits on the 0 dB line, and a vertical drag moves its Q
    /// instead, when it has one.</summary>
    public bool MovesY = true;

    /// <summary>Whether the wheel (or Alt-drag) moves the Q.</summary>
    public bool HasQ = true;

    public Color Color = new Color(95, 224, 160);

    /// <summary>Drawn inside the node when there is room ("3").</summary>
    public string Label = "";

    /// <summary>What the readout calls it ("Band 3 · Bell").</summary>
    public string Description = "";
}

/// <summary>
/// A frequency-response display and editor: a log frequency axis from
/// <see cref="MinHz"/> to <see cref="MaxHz"/>, a dB grid, the combined
/// response curve filled from 0 dB, the selected band's own curve faintly in
/// its colour, and a draggable node per band (ezmuze #497).
///
/// <list type="bullet">
/// <item>Drag a node: frequency across, gain up and down. Shift is fine.</item>
/// <item>Alt-drag, or the wheel over a node: its Q.</item>
/// <item>Double-click a node: <see cref="NodeReset"/>.</item>
/// <item>Right-click a node: <see cref="NodeRemoved"/> (when the owner allows adding).</item>
/// <item>Press on empty space: <see cref="AddNode"/>, and the new node is
/// then dragged by the same press.</item>
/// <item><see cref="ReadOnly"/>: a picture with hover readouts, for a filter.</item>
/// </list>
///
/// Transparent where nothing is drawn, so a host can lay it over a live
/// spectrum (the ezmuze module panels put Korben's analyser behind it).
/// The curve is evaluated through the host's <see cref="ResponseDb"/> only
/// when a node changed or <see cref="InvalidateResponse"/> was called — a
/// band's response is transcendental maths, and the picture is still most
/// of the time.
///
/// The dB scale is chosen, not fixed: the smallest of <see cref="DbRanges"/>
/// that holds every enabled node's gain, re-chosen when no drag is under way,
/// so a gentle EQ is drawn large and a +24 dB boost still fits.
/// </summary>
[ElementTraits(typeof(PositionTrait), typeof(SizeTrait), typeof(OnMousePress), typeof(OnMouseButtonHeldDown),
    typeof(OnMouseRelease), typeof(OnRightMousePress), typeof(OnHoverTrait), typeof(OnExitTrait), typeof(OnScrollWheelChanged))]
public class FrequencyResponseElement : Element
{
    public float MinHz { get; set; } = 20f;

    public float MaxHz { get; set; } = 20000f;

    /// <summary>The scales to choose from, ± dB, smallest first.</summary>
    public float[] DbRanges { get; set; } = { 12f, 18f, 24f, 30f };

    /// <summary>The ± dB a node's gain may be dragged to.</summary>
    public float MaxDb { get; set; } = 30f;

    public float MinQ { get; set; } = 1f / 32f;

    public float MaxQ { get; set; } = 32f;

    /// <summary>The scale in use, ± dB.</summary>
    public float DbRange { get; private set; } = 12f;

    public List<ResponseNode> Nodes { get; } = new List<ResponseNode>();

    public int SelectedIndex { get; set; } = -1;

    public bool ReadOnly { get; set; }

    /// <summary>Whether a press on empty space may add a node and a
    /// right-click remove one (an EQ with spare bands; not a 3-band EQ).</summary>
    public bool CanAddNodes { get; set; }

    /// <summary>
    /// Whether the dB scale also holds the curve's own highest point, not
    /// only the nodes' gains. For a picture of a filter (ezmuze #508 on),
    /// where the resonant peak is the curve and its one point sits at the
    /// cutoff rather than on top of the peak.
    /// </summary>
    public bool FitCurve { get; set; }

    /// <summary>The whole response in dB at a frequency. Null draws no
    /// curve.</summary>
    public Func<float, float> ResponseDb;

    /// <summary>
    /// Optional: the lowest and highest response in dB between two
    /// frequencies. Given it, each column of the curve is drawn from the
    /// span it covers, alternating the span's top and bottom, so detail finer
    /// than a column (a comb's teeth, dense at the top of a log axis) draws
    /// as the band it fills rather than as whichever tooth a column's centre
    /// happened to land on. Where the detail is wider than a column the two
    /// are equal and the curve is the plain one.
    /// </summary>
    public Func<float, float, (float Min, float Max)> ResponseRange;

    /// <summary>One node's own response in dB at a frequency, for the
    /// faint per-band curve. Null draws none.</summary>
    public Func<int, float, float> NodeResponseDb;

    /// <summary>Raised on the press that picks a node.</summary>
    public Action<int> NodeSelected;

    /// <summary>A node moved, live, with its new frequency, gain and Q.</summary>
    public Action<int, float, float, float> NodeEdited;

    /// <summary>A drag, wheel turn or Q gesture ended: the undo boundary.</summary>
    public Action<int> NodeEditCompleted;

    public Action<int> NodeReset;

    public Action<int> NodeRemoved;

    /// <summary>A press on empty space at (Hz, dB): return the index of the
    /// node that is now there, or -1 for none.</summary>
    public Func<float, float, int> AddNode;

    public Func<float, string> FormatHz = DefaultHz;

    public Func<float, string> FormatDb = DefaultDb;

    public Func<float, string> FormatQ = q => "Q " + q.ToString(q < 10f ? "0.00" : "0.0", CultureInfo.InvariantCulture);

    // ---- colours: defaults on the dark palette; a host sets its theme's ----
    public Color GridColor = new Color(255, 255, 255, 18);
    public Color GridStrongColor = new Color(255, 255, 255, 34);
    public Color ZeroLineColor = new Color(255, 255, 255, 60);
    public Color LabelColor = new Color(150, 156, 172);
    public Color CurveColor = new Color(95, 224, 160);
    public float CurveFillAlpha = 0.2f;
    public Color ReadoutFill = new Color(16, 18, 24, 225);
    public Color ReadoutText = new Color(226, 230, 238);
    public Color NodeRingColor = new Color(12, 14, 18);
    public Color SelectedRingColor = new Color(245, 247, 250);

    /// <summary>Pixel inset of the ± scale lines from the top and bottom
    /// edges, so the outermost gridline and a node on it are not clipped.</summary>
    public float VerticalInset { get; set; } = 10f;

    public float NodeRadius { get; set; } = 6f;

    /// <summary>Pixel size of the axis labels; a host drawing the editor
    /// scaled up scales this with it.</summary>
    public float LabelSize { get; set; } = 10f;

    /// <summary>Pixel size of the hover readout.</summary>
    public float ReadoutSize { get; set; } = 11f;

    // ---- state ----
    private SdfFont font;
    private int dragIndex = -1;
    private bool dragQ;
    private bool dragged;
    private Vector2 dragStart;
    private float dragStartQ;
    private float dragStartDb;
    private float dragStartHz;
    private bool pressWasDoubleClick;
    private int hoverIndex = -1;
    private Vector2? hoverAt;
    private float[] curve = Array.Empty<float>();
    private float[] bandCurve = Array.Empty<float>();
    private int bandCurveFor = -2;
    private float[] snapshot = Array.Empty<float>();
    private int version;
    private int builtVersion = -1;
    private int builtWidth;

    public FrequencyResponseElement()
    {
        AddTrait<CursorTrait>().Set(new TVText(StandardCursors.Precision));

        ElementTrait<OnMousePress>().Set(new TVEvent<ClickEventArgs>(OnPress));
        ElementTrait<OnMouseButtonHeldDown>().Set(new TVEvent<ClickEventArgs>(OnHeld));
        ElementTrait<OnMouseRelease>().Set(new TVEvent<ClickEventArgs>(OnRelease));
        ElementTrait<OnRightMousePress>().Set(new TVEvent<ClickEventArgs>(OnRightPress));
        ElementTrait<OnHoverTrait>().Set(new TVEvent<ClickEventArgs>(args => HoverAt(Local(args))));
        ElementTrait<OnExitTrait>().Set(new TVEvent<ClickEventArgs>(_ =>
        {
            if (dragIndex < 0)
            {
                hoverAt = null;
                hoverIndex = -1;
            }
        }));
        Set<OnScrollWheelChanged>(new TVEvent<ScrollEventArgs>(OnWheel));
    }

    /// <summary>The pointer over the element, not pressed.</summary>
    internal void HoverAt(Vector2 local)
    {
        hoverAt = local;
        if (dragIndex < 0)
        {
            hoverIndex = HitTest(local);
        }
    }

    /// <summary>Makes the next draw re-evaluate the curve even though no
    /// node moved (the host changed something the nodes do not carry: a
    /// band's type, its slope).</summary>
    public void InvalidateResponse() => version++;

    // ================================================================ maths

    /// <summary>X within the element, 0..width, of a frequency.</summary>
    public float XOf(float hz, float width) => FrequencyResponseMath.PositionOf(hz, MinHz, MaxHz) * width;

    public float HzAt(float x, float width) => FrequencyResponseMath.FrequencyAt(x / Math.Max(1f, width), MinHz, MaxHz);

    public float YOf(float db, float height) => FrequencyResponseMath.YOf(db, DbRange, height, VerticalInset);

    public float DbAt(float y, float height) => FrequencyResponseMath.DbAt(y, DbRange, height, VerticalInset);

    /// <summary>Where a node is drawn: at its gain, or on the curve for one
    /// without a gain (a cut sits where it cuts).</summary>
    private Vector2 NodePoint(int index, Vector2 size)
    {
        ResponseNode node = Nodes[index];
        float x = XOf(node.Hz, size.X);
        float db = node.MovesY ? node.Db : 0f;
        return new Vector2(x, YOf(Math.Clamp(db, -DbRange, DbRange), size.Y));
    }

    private int HitTest(Vector2 local)
    {
        Vector2 size = this.GetSize().AsXna;
        float reach = NodeRadius + 5f;
        int best = -1;
        float bestDistance = reach * reach;

        // Last drawn is on top, and the selected node is drawn last.
        for (int i = Nodes.Count - 1; i >= 0; i--)
        {
            float d = Vector2.DistanceSquared(NodePoint(i, size), local);
            if (d < bestDistance || (i == SelectedIndex && d <= reach * reach))
            {
                bestDistance = d;
                best = i;
                if (i == SelectedIndex)
                {
                    break;
                }
            }
        }

        return best;
    }

    private Vector2 Local(ClickEventArgs args)
    {
        Vector2 pos = this.GetActualXnaPosition();
        return new Vector2(args.MouseState.X - pos.X, args.MouseState.Y - pos.Y);
    }

    private static bool Held(Keys left, Keys right)
    {
        InputManager input = Resources.StaticResources?.InputManager;
        if (input == null)
        {
            return false;
        }

        KeyboardState keys = input.CurrentKeyboardState;
        return keys.IsKeyDown(left) || keys.IsKeyDown(right);
    }

    // ================================================================ input

    private void OnPress(ClickEventArgs args)
    {
        if (PressAt(Local(args), args.ClickCount, Held(Keys.LeftAlt, Keys.RightAlt)))
        {
            CapturePointer();
        }
    }

    /// <summary>A press at an element-relative point; true when a drag
    /// began. The pointer handlers' body, callable without a pointer (the
    /// tests drive it).</summary>
    internal bool PressAt(Vector2 local, int clickCount, bool alt)
    {
        if (ReadOnly)
        {
            return false;
        }

        int hit = HitTest(local);
        pressWasDoubleClick = clickCount == 2 && hit >= 0;
        if (pressWasDoubleClick)
        {
            SelectedIndex = hit;
            NodeReset?.Invoke(hit);
            InvalidateResponse();
            return false;
        }

        if (hit < 0 && CanAddNodes && AddNode != null)
        {
            Vector2 size = this.GetSize().AsXna;
            hit = AddNode(HzAt(local.X, size.X), Math.Clamp(DbAt(local.Y, size.Y), -DbRange, DbRange));
            InvalidateResponse();
        }

        if (hit < 0)
        {
            return false;
        }

        SelectedIndex = hit;
        NodeSelected?.Invoke(hit);
        dragIndex = hit;
        hoverIndex = hit;
        dragQ = alt && Nodes[hit].HasQ;
        dragged = false;
        dragStart = local;
        dragStartQ = Nodes[hit].Q;
        dragStartDb = Nodes[hit].Db;
        dragStartHz = Nodes[hit].Hz;
        return true;
    }

    private void OnHeld(ClickEventArgs args) => DragTo(Local(args), Held(Keys.LeftShift, Keys.RightShift));

    /// <summary>The pointer, held, at an element-relative point.</summary>
    internal void DragTo(Vector2 local, bool fineMode)
    {
        if (pressWasDoubleClick || dragIndex < 0 || dragIndex >= Nodes.Count)
        {
            return;
        }

        hoverAt = local;
        ResponseNode node = Nodes[dragIndex];
        Vector2 size = this.GetSize().AsXna;
        float fine = fineMode ? 0.25f : 1f;
        Vector2 delta = (local - dragStart) * fine;
        if (delta == Vector2.Zero && !dragged)
        {
            return;
        }

        dragged = true;
        float hz = node.Hz;
        float db = node.Db;
        float q = node.Q;

        if (dragQ || (!node.MovesY && node.HasQ))
        {
            // Up is narrower: 60 px doubles the Q.
            q = Math.Clamp(dragStartQ * MathF.Pow(2f, -delta.Y / 60f), MinQ, MaxQ);
        }
        else if (node.MovesY)
        {
            float pxPerDb = Math.Max(1f, (size.Y / 2f) - VerticalInset) / DbRange;
            db = Math.Clamp(dragStartDb - (delta.Y / pxPerDb), -MaxDb, MaxDb);
        }

        if (!dragQ && node.MovesX)
        {
            float startX = XOf(dragStartHz, size.X);
            hz = Math.Clamp(HzAt(startX + delta.X, size.X), MinHz, MaxHz);
        }

        node.Hz = hz;
        node.Db = db;
        node.Q = q;
        NodeEdited?.Invoke(dragIndex, hz, db, q);
    }

    private void OnRelease(ClickEventArgs args)
    {
        if (ReleaseDrag())
        {
            ReleasePointer();
        }
    }

    /// <summary>The press ended; true when it ended a drag.</summary>
    internal bool ReleaseDrag()
    {
        if (pressWasDoubleClick)
        {
            pressWasDoubleClick = false;
            NodeEditCompleted?.Invoke(SelectedIndex);
            return false;
        }

        if (dragIndex < 0)
        {
            return false;
        }

        int index = dragIndex;
        dragIndex = -1;
        dragQ = false;
        NodeEditCompleted?.Invoke(index);
        return true;
    }

    private void OnRightPress(ClickEventArgs args)
    {
        if (ReadOnly || !CanAddNodes)
        {
            return;
        }

        int hit = HitTest(Local(args));
        if (hit >= 0)
        {
            NodeRemoved?.Invoke(hit);
            NodeEditCompleted?.Invoke(hit);
            InvalidateResponse();
        }
    }

    private void OnWheel(ScrollEventArgs args) => Wheel(-args.ScrollWheelDelta / 120f);

    /// <summary>Wheel notches over the hovered node, up positive.</summary>
    internal void Wheel(float notches)
    {
        if (ReadOnly || hoverIndex < 0 || hoverIndex >= Nodes.Count || !Nodes[hoverIndex].HasQ || dragIndex >= 0)
        {
            return;
        }

        // ScrollWheelDelta = previous - current: wheel-up is negative, and
        // up is narrower. An eighth of an octave of Q per notch.
        ResponseNode node = Nodes[hoverIndex];
        node.Q = Math.Clamp(node.Q * MathF.Pow(2f, notches / 8f), MinQ, MaxQ);
        SelectedIndex = hoverIndex;
        NodeEdited?.Invoke(hoverIndex, node.Hz, node.Db, node.Q);
        NodeEditCompleted?.Invoke(hoverIndex);
    }

    // ================================================================ draw

    public override void Draw()
    {
        DrawManager manager = Resources.StaticResources.DrawManager;
        Vector2 origin = this.GetActualXnaPosition();
        Vector2 size = this.GetSize().AsXna;
        if (size.X < 16 || size.Y < 16)
        {
            base.Draw();
            return;
        }

        font ??= Resources.StaticResources.FontManager.LoadSdfFont(Resources.StaticResources.Theme.UiFontSmall.Family);

        EnsureCurve((int)size.X);
        if (dragIndex < 0)
        {
            DbRange = FrequencyResponseMath.ChooseRange(DbRanges, Nodes, FitCurve ? curve : null);
        }

        DrawGrid(manager, origin, size);
        DrawCurves(manager, origin, size);
        DrawNodes(manager, origin, size);
        DrawReadout(manager, origin, size);
        base.Draw();
    }

    /// <summary>Re-evaluates the curve when a node, the width or the scale
    /// changed. The comparison is against a flat snapshot of every node's
    /// fields, so a host that edits nodes in place needs to do nothing.</summary>
    private void EnsureCurve(int width)
    {
        int columns = Math.Max(2, width / 2);
        int needed = (Nodes.Count * 6) + 2;
        if (snapshot.Length != needed)
        {
            snapshot = new float[needed];
            builtVersion = -1;
        }

        bool changed = builtVersion != version || builtWidth != width;
        int at = 0;
        void Take(float value)
        {
            if (snapshot[at] != value)
            {
                snapshot[at] = value;
                changed = true;
            }

            at++;
        }

        foreach (ResponseNode node in Nodes)
        {
            Take(node.Hz);
            Take(node.Db);
            Take(node.Q);
            Take(node.Enabled ? 1f : 0f);
            Take(node.MovesY ? 1f : 0f);
            Take(node.Color.PackedValue);
        }

        Take(SelectedIndex);
        Take(hoverIndex);

        if (!changed)
        {
            return;
        }

        builtVersion = version;
        builtWidth = width;
        if (curve.Length != columns)
        {
            curve = new float[columns];
            bandCurve = new float[columns];
        }

        for (int c = 0; c < columns; c++)
        {
            float x = c * (width - 1f) / (columns - 1);
            if (ResponseRange != null)
            {
                float half = (width - 1f) / (columns - 1) / 2f;
                (float lo, float hi) = ResponseRange(HzAt(Math.Max(0f, x - half), width), HzAt(Math.Min(width, x + half), width));
                curve[c] = (c & 1) == 0 ? hi : lo;
            }
            else
            {
                curve[c] = ResponseDb?.Invoke(HzAt(x, width)) ?? 0f;
            }
        }

        // The selected node's own shape, or the hovered one's while nothing
        // is selected.
        bandCurveFor = SelectedIndex >= 0 ? SelectedIndex : hoverIndex;
        if (bandCurveFor >= 0 && bandCurveFor < Nodes.Count && NodeResponseDb != null && Nodes[bandCurveFor].Enabled)
        {
            for (int c = 0; c < columns; c++)
            {
                bandCurve[c] = NodeResponseDb(bandCurveFor, HzAt(c * (width - 1f) / (columns - 1), width));
            }
        }
        else
        {
            bandCurveFor = -2;
        }
    }

    private static readonly float[] GridHz = { 30, 40, 50, 60, 70, 80, 90, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000 };

    private static readonly float[] LabelHz = { 50, 100, 200, 500, 1000, 2000, 5000, 10000 };

    private void DrawGrid(DrawManager manager, Vector2 origin, Vector2 size)
    {
        int top = (int)origin.Y;
        int height = (int)size.Y;

        foreach (float hz in GridHz)
        {
            if (hz <= MinHz || hz >= MaxHz)
            {
                continue;
            }

            bool decade = hz is 100 or 1000 or 10000;
            int x = (int)(origin.X + XOf(hz, size.X));
            manager.DrawFilledRectangle(new Rectangle(x, top, 1, height), decade ? GridStrongColor : GridColor);
        }

        float labelSize = LabelSize;
        foreach (float hz in LabelHz)
        {
            string text = hz >= 1000 ? (hz / 1000f).ToString("0", CultureInfo.InvariantCulture) + "k" : hz.ToString("0", CultureInfo.InvariantCulture);
            float x = origin.X + XOf(hz, size.X) + 3f;
            manager.DrawSdfString(font, text, new Vector2(x, origin.Y + size.Y - labelSize - 3f), labelSize, LabelColor);
        }

        float step = FrequencyResponseMath.GridStep(DbRange);
        for (float db = -DbRange; db <= DbRange + 0.01f; db += step)
        {
            int y = (int)(origin.Y + YOf(db, size.Y));
            bool zero = Math.Abs(db) < 0.01f;
            manager.DrawFilledRectangle(new Rectangle((int)origin.X, y, (int)size.X, 1), zero ? ZeroLineColor : GridColor);
            if (!zero)
            {
                string text = (db > 0 ? "+" : "") + db.ToString("0", CultureInfo.InvariantCulture);
                Vector2 measured = font.MeasureString(text, labelSize);
                manager.DrawSdfString(font, text, new Vector2(origin.X + size.X - measured.X - 4f, y - labelSize - 1f), labelSize, LabelColor);
            }
        }
    }

    private void DrawCurves(DrawManager manager, Vector2 origin, Vector2 size)
    {
        if (curve.Length < 2 || ResponseDb == null)
        {
            return;
        }

        float zeroY = origin.Y + YOf(0f, size.Y);

        if (bandCurveFor >= 0 && bandCurveFor < Nodes.Count)
        {
            Color band = Nodes[bandCurveFor].Color;
            DrawFilled(manager, origin, size, bandCurve, zeroY, band * 0.16f);
            DrawLine(manager, origin, size, bandCurve, band * 0.6f, 1.25f);
        }

        DrawFilled(manager, origin, size, curve, zeroY, CurveColor * CurveFillAlpha);
        DrawLine(manager, origin, size, curve, CurveColor, 2f);
    }

    // Reused every frame: a curve is redrawn at the frame rate, and four
    // arrays per fill per frame is garbage for no reason.
    private Vector2[] upperScratch = Array.Empty<Vector2>();
    private Vector2[] lowerScratch = Array.Empty<Vector2>();
    private Vector2[] upNormals = Array.Empty<Vector2>();
    private Vector2[] downNormals = Array.Empty<Vector2>();

    private void DrawFilled(DrawManager manager, Vector2 origin, Vector2 size, float[] db, float zeroY, Color color)
    {
        int n = db.Length;
        if (upperScratch.Length != n)
        {
            upperScratch = new Vector2[n];
            lowerScratch = new Vector2[n];
            upNormals = new Vector2[n];
            downNormals = new Vector2[n];
            Array.Fill(upNormals, new Vector2(0, -1));
            Array.Fill(downNormals, new Vector2(0, 1));
        }

        for (int c = 0; c < n; c++)
        {
            float x = origin.X + (c * (size.X - 1f) / (n - 1));
            float y = origin.Y + YOf(Math.Clamp(db[c], -DbRange * 1.2f, DbRange * 1.2f), size.Y);
            upperScratch[c] = new Vector2(x, Math.Min(y, zeroY));
            lowerScratch[c] = new Vector2(x, Math.Max(y, zeroY));
        }

        manager.DrawMonotoneRegion(upperScratch, lowerScratch, upNormals, downNormals, color);
    }

    private void DrawLine(DrawManager manager, Vector2 origin, Vector2 size, float[] db, Color color, float thickness)
    {
        // Round-capped float segments rather than a polyline of whole-pixel
        // quads: the curve is smooth and swept, and DrawThickLine's truncated
        // starts turn a gentle bell into a staircase.
        Vector2 previous = default;
        for (int c = 0; c < db.Length; c++)
        {
            float x = origin.X + (c * (size.X - 1f) / (db.Length - 1));
            float y = origin.Y + YOf(Math.Clamp(db[c], -DbRange * 1.2f, DbRange * 1.2f), size.Y);
            var point = new Vector2(x, y);
            if (c > 0)
            {
                manager.DrawRoundCapLine(previous, point, color, thickness);
            }

            previous = point;
        }
    }

    private void DrawNodes(DrawManager manager, Vector2 origin, Vector2 size)
    {
        // Selected last, so it is on top where two nodes meet.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                if ((i == SelectedIndex) != (pass == 1))
                {
                    continue;
                }

                ResponseNode node = Nodes[i];
                Vector2 at = origin + NodePoint(i, size);
                bool hot = i == hoverIndex || i == dragIndex;
                float r = NodeRadius + (hot ? 1f : 0f);

                if (i == SelectedIndex)
                {
                    manager.DrawFilledCircle(at, r + 2.5f, SelectedRingColor);
                }

                if (node.Enabled)
                {
                    manager.DrawFilledCircle(at, r + 1f, NodeRingColor);
                    manager.DrawFilledCircle(at, r, node.Color);
                }
                else
                {
                    manager.DrawFilledCircle(at, r + 1f, node.Color * 0.55f);
                    manager.DrawFilledCircle(at, r - 0.5f, NodeRingColor);
                }

                if (!string.IsNullOrEmpty(node.Label) && r >= 5f)
                {
                    float px = r * 1.5f;
                    Vector2 measured = font.MeasureString(node.Label, px);
                    Color ink = node.Enabled ? NodeRingColor : node.Color;
                    manager.DrawSdfString(font, node.Label, new Vector2(at.X - (measured.X / 2f), at.Y - (px / 2f) - 1f), px, ink);
                }
            }
        }
    }

    /// <summary>The hover readout: the node under the pointer (or being
    /// dragged) in full, or the frequency and response under it.</summary>
    private void DrawReadout(DrawManager manager, Vector2 origin, Vector2 size)
    {
        if (hoverAt == null)
        {
            return;
        }

        int index = dragIndex >= 0 ? dragIndex : hoverIndex;
        string text;
        if (index >= 0 && index < Nodes.Count)
        {
            ResponseNode node = Nodes[index];
            text = (string.IsNullOrEmpty(node.Description) ? "" : node.Description + "  ")
                   + FormatHz(node.Hz)
                   + (node.MovesY ? "  " + FormatDb(node.Db) : "")
                   + (node.HasQ ? "  " + FormatQ(node.Q) : "")
                   + (node.Enabled ? "" : "  (off)");
        }
        else
        {
            float hz = HzAt(hoverAt.Value.X, size.X);
            text = FormatHz(hz) + (ResponseDb != null ? "  " + FormatDb(ResponseDb(hz)) : "");
        }

        float px = ReadoutSize;
        Vector2 measured = font.MeasureString(text, px);
        float w = measured.X + (px * 1.1f);
        float h = px * 1.7f;
        Vector2 anchor = index >= 0 && index < Nodes.Count ? origin + NodePoint(index, size) : origin + hoverAt.Value;
        float x = Math.Clamp(anchor.X - (w / 2f), origin.X + 2f, origin.X + size.X - w - 2f);
        float y = anchor.Y - h - NodeRadius - 8f;
        if (y < origin.Y + 2f)
        {
            y = anchor.Y + NodeRadius + 8f;
        }

        manager.DrawRoundedRectangle(new Rectangle((int)x, (int)y, (int)w, (int)h), ReadoutFill, (int)(px * 0.35f));
        manager.DrawSdfString(font, text, new Vector2(x + (px * 0.55f), y + (px * 0.3f)), px, ReadoutText);
    }

    private static string DefaultHz(float hz) => hz >= 1000f
        ? (hz / 1000f).ToString(hz >= 10000f ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " kHz"
        : hz.ToString("0", CultureInfo.InvariantCulture) + " Hz";

    private static string DefaultDb(float db) => (db > 0.05f ? "+" : "") + db.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
}

/// <summary>
/// The arithmetic of <see cref="FrequencyResponseElement"/>, apart from any
/// drawing, so it can be tested without a graphics device.
/// </summary>
public static class FrequencyResponseMath
{
    /// <summary>Where a frequency sits across a log axis, 0..1.</summary>
    public static float PositionOf(float hz, float minHz, float maxHz)
    {
        hz = Math.Clamp(hz, minHz, maxHz);
        return MathF.Log(hz / minHz) / MathF.Log(maxHz / minHz);
    }

    public static float FrequencyAt(float position, float minHz, float maxHz)
        => minHz * MathF.Pow(maxHz / minHz, Math.Clamp(position, 0f, 1f));

    /// <summary>Y of a gain, with ±<paramref name="range"/> an
    /// <paramref name="inset"/> in from the edges and 0 dB in the middle.</summary>
    public static float YOf(float db, float range, float height, float inset)
    {
        float half = (height / 2f) - inset;
        return (height / 2f) - (db / Math.Max(0.01f, range) * half);
    }

    public static float DbAt(float y, float range, float height, float inset)
    {
        float half = Math.Max(1f, (height / 2f) - inset);
        return ((height / 2f) - y) / half * range;
    }

    /// <summary>The smallest scale that holds every enabled gain node (and,
    /// given one, the curve's highest point: a cut going down to nothing
    /// does not stretch the scale, a resonant peak does). A
    /// node on the outermost line is still clear of the frame: the lines sit
    /// an inset in from the edges.</summary>
    public static float ChooseRange(float[] ranges, IReadOnlyList<ResponseNode> nodes, float[] curve = null)
    {
        float needed = 0f;
        if (curve != null)
        {
            // The highest point, and the bottom of every DIP the curve climbs
            // back out of (a comb's valleys), while it is above the largest
            // scale's floor. A cut's slope is not a dip: it never comes back,
            // so a low pass still draws on the scale its passband needs.
            float floor = ranges.Length > 0 ? -ranges[^1] : -30f;
            for (int c = 0; c < curve.Length; c++)
            {
                float db = curve[c];
                needed = Math.Max(needed, db);
                bool dip = c > 0 && c < curve.Length - 1 && db < curve[c - 1] && db < curve[c + 1];
                if (dip && db > floor)
                {
                    needed = Math.Max(needed, -db);
                }
            }
        }

        foreach (ResponseNode node in nodes)
        {
            if (node.Enabled && node.MovesY)
            {
                needed = Math.Max(needed, Math.Abs(node.Db));
            }
        }

        foreach (float range in ranges)
        {
            if (needed <= range + 0.001f)
            {
                return range;
            }
        }

        return ranges.Length > 0 ? ranges[^1] : 12f;
    }

    /// <summary>The gridline spacing for a scale: every 6 dB up to ±18,
    /// then every 12, and every 10 on a ±30 scale.</summary>
    public static float GridStep(float range) => range switch
    {
        <= 18f => 6f,
        >= 30f => 10f,
        _ => 12f,
    };
}
