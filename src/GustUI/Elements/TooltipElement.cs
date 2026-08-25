using System;
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
    private Vector2 anchor;
    private bool visible;
    private long shownAtMs;

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
        Chain(target.AddTrait<OnEnterTrait>(), args => Show(text, args.GlobalMousePosition.AsXna));
        Chain(target.AddTrait<OnExitTrait>(), _ => Hide());
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
        shared.anchor = nearScreenPosition + new Vector2(14, 20);
        shared.visible = true;
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
            base.Draw();
            return;
        }

        var theme = Resources.StaticResources.Theme;
        var sdfFont = Resources.StaticResources.FontManager.LoadSdfFont(theme.UiFontSmall.Family);
        Vector2 textSize = sdfFont.MeasureString(text, theme.UiFontSmall.Size);

        int w = (int)textSize.X + PadX * 2;
        int h = (int)textSize.Y + PadY * 2;

        // Clamp to the window so the label never renders off-screen.
        Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
        float x = MathHelper.Clamp(anchor.X, 0, Math.Max(0, windowSize.X - w));
        float y = MathHelper.Clamp(anchor.Y, 0, Math.Max(0, windowSize.Y - h));
        var rect = new Rectangle((int)x, (int)y, w, h);

        // design-guide.md §9: one standard tooltip style everywhere —
        // SurfaceHeader-family background, SurfaceBorder outline, BodyText.
        var manager = Resources.StaticResources.DrawManager;
        manager.DrawFilledRectangle(rect, theme.SurfaceHeader * 0.97f);
        manager.DrawRectangle(rect, theme.SurfaceBorder, 1);
        manager.DrawSdfString(sdfFont, text, new Vector2(x + PadX, y + PadY), theme.UiFontSmall.Size, theme.BodyText);

        base.Draw();
    }
}
