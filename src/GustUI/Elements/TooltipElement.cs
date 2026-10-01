using System;
using System.Collections.Generic;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements;

/// <summary>
/// A lightweight hover label. <see cref="Attach"/> wires an element's
/// enter/exit events to show/hide a single shared tooltip near the cursor
/// after <see cref="HoverDelayMs"/>. The tooltip deliberately has no
/// Position/Size traits, so it is invisible to hit-testing and can never
/// steal hover from the element it describes; it draws itself immediate-mode
/// (KnobElement-style) at a very high depth.
/// </summary>
public class TooltipElement : Element
{
    /// <summary>Delay before the label appears, in milliseconds — design-guide.md
    /// §9 standardizes this at ~450ms everywhere a tooltip appears.</summary>
    public static int HoverDelayMs { get; set; } = 450;

    private const int PadX = 8;
    private const int PadY = 5;
    private const int TooltipDepth = 1000000;

    private static TooltipElement shared;

    private string text = "";
    private Vector2 cursor;
    private Element owner;

    // The wrapped lines for the current text at the current window width and
    // font size. Wrapping measures word by word, so it is redone only when
    // one of those changes, not every frame the label is up.
    private List<string> layoutLines;
    private string layoutText;
    private Vector2 layoutWindow;
    private float layoutFontSize;
    private float layoutWidest;
    private bool visible;
    private long shownAtMs;
    private Rectangle drawnBounds;

    /// <summary>
    /// Where the label was last DRAWN, in element space, or
    /// <see cref="Rectangle.Empty"/> when it is not on screen (hidden, or
    /// still inside the hover delay).
    ///
    /// The tooltip has no Position/Size traits on purpose (see the class
    /// summary), so an inspector reading element bounds - the ezmuze
    /// remote-control API's /tree - saw it as 0x0 even while it was plainly
    /// on screen. This is read-only and plays no part in hit-testing.
    /// </summary>
    public Rectangle DrawnBounds => drawnBounds;

    /// <summary>Whether the label is on screen right now (the last draw
    /// painted it).</summary>
    public bool IsShowing => drawnBounds != Rectangle.Empty;

    /// <summary>The text the label is showing, or would show once the hover
    /// delay passes.</summary>
    public string Text => text;

    /// <summary>
    /// Shows <paramref name="text"/> while the pointer hovers
    /// <paramref name="target"/>. Uses the target's OnEnter/OnExit events
    /// (attached at runtime if absent) — any existing enter/exit handlers on
    /// the target are replaced.
    /// </summary>
    /// <summary>
    /// Gives an element a hover tooltip, CHAINING onto whatever enter/exit
    /// handlers it already has rather than replacing them.
    ///
    /// Trait.Set replaces, which made a tooltip and a hover EFFECT mutually
    /// exclusive on the same element — and silently, with whichever call came
    /// last winning. That is a trap rather than a policy: attaching a tooltip
    /// is not a statement about what else should happen on hover, and every
    /// caller that hit it had to hand-roll Show/Hide to get both.
    /// </summary>
    public static void Attach(Element target, string text)
    {
        Chain(target.AddTrait<OnEnterTrait>(), args => ShowFor(target, text, args.GlobalMousePosition.AsXna));
        Chain(target.AddTrait<OnExitTrait>(), _ => HideFor(target));
    }

    /// <summary>
    /// As <see cref="Attach(Element, string)"/>, but the text is asked for each
    /// time the pointer arrives — for a toggle whose tooltip names what the
    /// NEXT click will do ("Show routings" / "Hide routings"), which a string
    /// fixed at attach time would get wrong after the first click.
    /// </summary>
    public static void Attach(Element target, Func<string> text)
    {
        Chain(target.AddTrait<OnEnterTrait>(), args => ShowFor(target, text(), args.GlobalMousePosition.AsXna));
        Chain(target.AddTrait<OnExitTrait>(), _ => HideFor(target));
    }

    // GustUI fires a frame's ENTERS before its EXITS (InputManager.
    // UpdateHoverTransitions), so moving straight from one attached element
    // to another in a single frame - adjacent toolbar buttons, store tiles,
    // or any jump of the pointer - showed the new label and then let the old
    // element's exit hide it again: the second tooltip never appeared. An
    // exit now hides only the label its own element put up.
    private static void ShowFor(Element target, string text, Vector2 nearScreenPosition)
    {
        Show(text, nearScreenPosition);
        shared.owner = target;
    }

    private static void HideFor(Element target)
    {
        if (shared != null && (shared.owner == null || ReferenceEquals(shared.owner, target)))
        {
            Hide();
        }
    }

    /// <summary>Appends <paramref name="handler"/> to a trait's existing
    /// action, preserving it. Order is existing-then-ours: whatever the
    /// element already did on hover is still the primary behaviour.</summary>
    private static void Chain(Trait<TVEvent<ClickEventArgs>> trait, Action<ClickEventArgs> handler)
    {
        Action<ClickEventArgs> existing = trait.Value()?.TriggerAction;
        trait.Set(new TVEvent<ClickEventArgs>(args =>
        {
            existing?.Invoke(args);
            handler(args);
        }));
    }

    /// <summary>Shows the shared tooltip near a screen position (after the hover delay).</summary>
    public static void Show(string text, Vector2 nearScreenPosition)
    {
        if (shared == null)
        {
            shared = new TooltipElement { Depth = TooltipDepth, ElementName = "tooltip" };
        }

        // Kill() nulls Parent, so this self-heals if a screen cleared the stage.
        if (shared.Parent == null)
        {
            Resources.StaticResources.RootWindow.AddChild(shared, "tooltip");
            shared.Depth = TooltipDepth;
        }

        shared.text = text ?? "";
        shared.cursor = nearScreenPosition;
        shared.visible = true;
        shared.owner = null;
        shared.shownAtMs = Environment.TickCount64;
    }

    /// <summary>Hides the shared tooltip.</summary>
    public static void Hide()
    {
        if (shared != null)
        {
            shared.visible = false;
        }
    }

    public override void Draw()
    {
        if (!visible || text.Length == 0 || Environment.TickCount64 - shownAtMs < HoverDelayMs)
        {
            drawnBounds = Rectangle.Empty;
            base.Draw();
            return;
        }

        var theme = Resources.StaticResources.Theme;
        var sdfFont = Resources.StaticResources.FontManager.LoadSdfFont(theme.UiFontSmall.Family);
        float fontSize = theme.UiFontSmall.Size;
        float lineHeight = sdfFont.MeasureString("Ag", fontSize).Y;
        Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;

        // Wrapped to a reading measure and cut at half the window's height
        // (TooltipLayout). The SDF string drawer knows nothing of '\n', so
        // each line is measured and drawn on its own, a line height apart.
        if (layoutLines == null || layoutText != text || layoutWindow != windowSize || layoutFontSize != fontSize)
        {
            Func<string, float> measure = s => sdfFont.MeasureString(s, fontSize).X;
            float wrapWidth = TooltipLayout.MaxBoxWidthFor(windowSize.X) - (PadX * 2);
            layoutLines = TooltipLayout.Lines(text, measure, wrapWidth,
                TooltipLayout.MaxLinesFor(windowSize.Y, lineHeight, PadY));
            layoutWidest = 0f;
            foreach (string line in layoutLines)
            {
                layoutWidest = Math.Max(layoutWidest, measure(line));
            }

            layoutText = text;
            layoutWindow = windowSize;
            layoutFontSize = fontSize;
        }

        List<string> lines = layoutLines;
        int w = (int)layoutWidest + PadX * 2;
        int h = (int)(lineHeight * lines.Count) + PadY * 2;

        // Below-right of the pointer, flipped left/up at the window's edges,
        // and always wholly on screen.
        Vector2 at = TooltipLayout.Place(cursor, new Vector2(w, h), windowSize);
        float x = at.X;
        float y = at.Y;
        var rect = new Rectangle((int)x, (int)y, w, h);
        drawnBounds = rect;

        // design-guide.md §9: one standard tooltip style everywhere —
        // SurfaceHeader-family background, SurfaceBorder outline, BodyText.
        var manager = Resources.StaticResources.DrawManager;
        manager.DrawFilledRectangle(rect, theme.SurfaceHeader * 0.97f);
        manager.DrawRectangle(rect, theme.SurfaceBorder, 1);
        for (int i = 0; i < lines.Count; i++)
        {
            manager.DrawSdfString(sdfFont, lines[i], new Vector2(x + PadX, y + PadY + (i * lineHeight)),
                theme.UiFontSmall.Size, theme.BodyText);
        }

        base.Draw();
    }
}
