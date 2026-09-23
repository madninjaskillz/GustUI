using System;
using System.Collections.Generic;
using System.Diagnostics;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements;

/// <summary>
/// A row of underlined TEXT tabs with exactly one selected: the sub-tabs of a
/// page, not the tabs of a window.
///
/// A window's tab strip is its title bar. It gives every tab an equal share
/// of the width and ellipsises a long title (design-guide.md "Tab strips").
/// This is the other kind: a few short labels choosing what a page below them
/// shows. Each tab is sized to its own text, so nothing is ever cut short or
/// wrapped. If the tabs are wider than <see cref="MaxWidth"/>, whole tabs move
/// to a second line rather than squeezing a label. The first label on a line
/// starts at the element's left edge, so the row lines up with the content
/// under it; the padding that makes a tab easy to hit is on its right, and
/// between tabs.
///
/// Selected is the body text colour with a 2px <c>AccentSelection</c>
/// underline sitting on a 1px hairline that runs the full width. The others
/// are dimmed, and hover brightens them. The underline slides between tabs
/// with the §5 exponential ease (<see cref="Ease.Toward"/>), so a click
/// mid-slide turns back from where it visibly is. Every colour is read from
/// the theme each frame, so a light/dark switch needs no rebuild.
/// </summary>
public class TextTabsElement : RectangleElement
{
    /// <summary>Space either side of a label inside its tab.</summary>
    public const float TabPad = 8f;

    /// <summary>Space between two tabs.</summary>
    public const float TabGap = 4f;

    private const float VerticalPad = 6f;
    private const float UnderlineHeight = 2f;

    private readonly List<string> labels;
    private readonly List<bool> visible;
    private readonly TVFont font;
    private readonly List<FilledRectangleElement> tabs = new();
    private readonly List<Rectangle> tabRects = new();
    private readonly List<float> textWidths = new();
    private readonly List<float> leads = new();
    private readonly FilledRectangleElement underline;
    private readonly List<FilledRectangleElement> baselines = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();

    private float maxWidth;
    private int selectedIndex;
    private float lineHeight;
    private Vector4 shownUnderline = new Vector4(-1f);
    private double lastUpdate = -1;

    /// <summary>Raised when a click changes the selection. Never raised when
    /// <see cref="SelectedIndex"/> is set from code, and never for a click on
    /// the tab that is already selected.</summary>
    public Action<int> OnSelectionChanged;

    public TextTabsElement(IEnumerable<string> labels, TVFont font, float maxWidth)
    {
        this.labels = new List<string>(labels);
        visible = new List<bool>(new bool[this.labels.Count]);
        for (int i = 0; i < visible.Count; i++)
        {
            visible[i] = true;
        }

        this.font = font;
        this.maxWidth = maxWidth;
        lineHeight = TextElement.Measure("Ag", font).Y + (VerticalPad * 2f) + UnderlineHeight;

        Set<PositionTrait>(new TVVector(0, 0));

        for (int i = 0; i < this.labels.Count; i++)
        {
            int index = i;
            float textWidth = (float)Math.Ceiling(TextElement.Measure(this.labels[i], font).X);
            textWidths.Add(textWidth);
            leads.Add(TabPad);
            var tab = new FilledRectangleElement(0, 0, (int)(textWidth + (TabPad * 2f)), (int)lineHeight,
                new TVFillSolidColor(Color.Transparent));
            tab.AddTrait<CursorTrait>().Set(new TVText(Managers.StandardCursors.PointingHand));
            tab.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>(_ => Choose(index)));

            var label = new TextElement { WordWrap = false };
            label.Set<PositionTrait>(new TVVector(TabPad, VerticalPad));
            label.Set<SizeTrait>(new TVVector(textWidth + 2f, lineHeight - (VerticalPad * 2f)));
            label.Set<FontTrait>(font);
            label.Set<ForegroundColorTrait>(new TVColor(() => LabelColour(index)));
            label.Set<TextTrait>(new TVText(this.labels[i]));
            tab.AddChild(label, "label");

            AddChild(tab, "tab" + i);
            tabs.Add(tab);
            tabRects.Add(Rectangle.Empty);
        }

        underline = new FilledRectangleElement(0, 0, 0, (int)UnderlineHeight,
            new TVFillSolidColor(() => Resources.StaticResources.Theme.AccentSelection));
        AddChild(underline, "underline");

        Layout();
    }

    public IReadOnlyList<string> Labels => labels;

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            selectedIndex = Math.Clamp(value, 0, Math.Max(0, labels.Count - 1));
        }
    }

    /// <summary>The widest a line of tabs may run before the next tab moves
    /// to a new line.</summary>
    public float MaxWidth
    {
        get => maxWidth;
        set
        {
            if (Math.Abs(value - maxWidth) >= 0.5f)
            {
                maxWidth = value;
                Layout();
            }
        }
    }

    /// <summary>Height of one line of tabs, hairline included.</summary>
    public float LineHeight => lineHeight;

    /// <summary>Hides or shows a tab. A hidden tab takes no room. Hiding the
    /// selected tab does not move the selection; the caller decides where it
    /// goes.</summary>
    public void SetTabVisible(int index, bool isVisible)
    {
        if (index < 0 || index >= labels.Count || visible[index] == isVisible)
        {
            return;
        }

        visible[index] = isVisible;
        Layout();
    }

    public bool IsTabVisible(int index) => index >= 0 && index < visible.Count && visible[index];

    private void Choose(int index)
    {
        if (index < 0 || index >= labels.Count || !visible[index] || index == selectedIndex)
        {
            return;
        }

        selectedIndex = index;
        OnSelectionChanged?.Invoke(index);
    }

    private void Layout()
    {
        float x = 0f;
        int line = 0;
        for (int i = 0; i < tabs.Count; i++)
        {
            FilledRectangleElement tab = tabs[i];
            tab.Visible = visible[i];
            if (!visible[i])
            {
                tabRects[i] = Rectangle.Empty;
                continue;
            }

            float full = textWidths[i] + (TabPad * 2f);
            if (x > 0f && x + full > maxWidth)
            {
                line++;
                x = 0f;
            }

            // The first tab on a line gives up its left padding.
            float lead = x > 0f ? TabPad : 0f;
            float w = textWidths[i] + lead + TabPad;
            leads[i] = lead;

            float y = line * lineHeight;
            tab.Set<PositionTrait>(new TVVector(x, y));
            tab.Set<SizeTrait>(new TVVector(w, lineHeight));
            tab.Children.Get("label")?.Set<PositionTrait>(new TVVector(lead, VerticalPad));
            tabRects[i] = new Rectangle((int)x, (int)y, (int)w, (int)lineHeight);
            x += w + TabGap;
        }

        int lines = line + 1;

        // One hairline under each line of tabs, the full width: it is what
        // makes the row read as the top edge of what it chooses, not as a
        // loose run of links.
        while (baselines.Count < lines)
        {
            var hairline = new FilledRectangleElement(0, 0, 1, 1,
                new TVFillSolidColor(() => Resources.StaticResources.Theme.SurfaceBorder));
            AddChild(hairline, "baseline" + baselines.Count);
            baselines.Add(hairline);
        }

        for (int i = 0; i < baselines.Count; i++)
        {
            baselines[i].Visible = i < lines;
            baselines[i].Set<PositionTrait>(new TVVector(0, ((i + 1) * lineHeight) - 1f));
            baselines[i].Set<SizeTrait>(new TVVector(Math.Max(1f, maxWidth), 1f));
        }

        // The underline draws over the hairline, so it goes last.
        Children.Remove(underline);
        AddChild(underline, "underline");

        Set<SizeTrait>(new TVVector(maxWidth, lines * lineHeight));
        shownUnderline = new Vector4(-1f);
    }

    public override void Update(Element parent = null)
    {
        double now = clock.Elapsed.TotalSeconds;
        float dt = lastUpdate < 0 ? 0f : (float)Math.Min(now - lastUpdate, 0.25);
        lastUpdate = now;

        Rectangle target = selectedIndex < tabRects.Count ? tabRects[selectedIndex] : Rectangle.Empty;
        bool known = !target.IsEmpty && selectedIndex < textWidths.Count;
        var goal = new Vector4(
            known ? target.X + leads[selectedIndex] : 0f,
            target.Bottom - UnderlineHeight,
            known ? textWidths[selectedIndex] : 0f,
            known ? 1f : 0f);

        // First placement (and after a relayout) snaps: an underline that
        // slid in from the corner every time the page opened would be motion
        // nobody asked for.
        Vector4 next = shownUnderline.X < 0f
            ? goal
            : new Vector4(
                Ease.Toward(shownUnderline.X, goal.X, dt),
                Ease.Toward(shownUnderline.Y, goal.Y, dt),
                Ease.Toward(shownUnderline.Z, goal.Z, dt),
                goal.W);

        if (next != shownUnderline)
        {
            shownUnderline = next;
            underline.Visible = next.W > 0f;
            underline.Set<PositionTrait>(new TVVector(next.X, next.Y));
            underline.Set<SizeTrait>(new TVVector(next.Z, UnderlineHeight));
        }

        base.Update(parent);
    }

    private Color LabelColour(int index)
    {
        Theme theme = Resources.StaticResources.Theme;
        if (index == selectedIndex)
        {
            return theme.BodyText;
        }

        bool hovered = Resources.StaticResources.InputManager.GetElementState(tabs[index])
            != Managers.InputManager.ElementState.Normal;
        return theme.BodyText * (hovered ? 0.9f : 0.6f);
    }
}
