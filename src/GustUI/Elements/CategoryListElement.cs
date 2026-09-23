using System;
using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GustUI.Elements;

/// <summary>
/// A vertical list of categories, one row each with an icon and a label, and
/// exactly one selected — the left-hand column of a settings window.
///
/// The job a tab strip does, turned on its side. A horizontal strip gives
/// every tab an equal share of the width, so it runs out of room at five or
/// six labels and starts wrapping them; a column gives every label the whole
/// column and grows downward instead, and the icon beside each label lets the
/// eye find a category without reading the list.
///
/// Rows follow the list-row language (design-guide.md §9.5 and §6): selected
/// is an <c>AccentSelection</c> tint plus a 3px accent bar on the leading
/// edge — the tab underline turned on its side — hover a fainter tint,
/// disabled greyed and inert. Every colour is read from the theme each
/// frame, so a light/dark switch needs no rebuild.
///
/// Keyboard: a click on the list gives it focus, and Up/Down/Home/End then
/// move the selection. The 2px accent focus outline (§6 Focus) appears once
/// the keyboard is actually being used on it, not on every click: a ring that
/// lit up whenever a category was clicked would be telling a mouse user about
/// a keyboard they are not using. While it
/// has focus the list takes keys the way a text field does, which is what
/// keeps Up and Down from also reaching the shortcut hooks of whatever sits
/// behind the dialog.
/// </summary>
[ElementTraits(typeof(OnFocused), typeof(OnUnfocused))]
public class CategoryListElement : RectangleElement
{
    /// <summary>One category. Give it a symbol-font glyph in
    /// <see cref="Icon"/>, or — where the font has no glyph for the idea — a
    /// factory for a drawn icon in <see cref="DrawnIcon"/>. The drawn element
    /// is positioned and sized to the icon column by the list, and gets the
    /// row's icon colour through its <see cref="ForegroundColorTrait"/> if it
    /// declares one.</summary>
    public sealed class Item
    {
        public string Label { get; set; }

        public string Icon { get; set; }

        public Func<Element> DrawnIcon { get; set; }

        public bool Enabled { get; set; } = true;

        public Item(string label, string icon = null, Func<Element> drawnIcon = null)
        {
            Label = label;
            Icon = icon;
            DrawnIcon = drawnIcon;
        }
    }

    public const float DefaultRowHeight = 36f;

    /// <summary>The icon column is layout, not type (design-guide.md
    /// 2026-08-31): every label starts at the same x whatever its icon.</summary>
    private const float IconBox = 24f;

    private const float LeftPad = 12f;
    private const float LabelGap = 10f;
    private const float AccentBarWidth = 3f;

    private readonly List<Item> items;
    private readonly float rowHeight;
    private readonly List<FilledRectangleElement> rows = new();
    private int selectedIndex;

    /// <summary>Whether a key has moved the selection since focus last
    /// arrived or the last click — what draws the focus outline.</summary>
    private bool keyboardInUse;

    /// <summary>Raised when a click or a key changes the selection — never
    /// when <see cref="SelectedIndex"/> is assigned from code, and never for
    /// a click on the row that is already selected.</summary>
    public Action<int> OnSelectionChanged;

    public IReadOnlyList<Item> Items => items;

    public int SelectedIndex
    {
        get => selectedIndex;
        set => selectedIndex = Math.Clamp(value, 0, Math.Max(0, items.Count - 1));
    }

    public CategoryListElement(IEnumerable<Item> items, float width, TVFont labelFont, float rowHeight = DefaultRowHeight)
    {
        this.items = new List<Item>(items);
        this.rowHeight = rowHeight;

        Set<PositionTrait>(new TVVector(0, 0));
        Set<SizeTrait>(new TVVector(width, rowHeight * this.items.Count));

        // Only a click ON the list takes focus (InputManager focuses the
        // topmost element declaring OnFocused under the pointer); these are
        // here so that is true, and so a redraw notices it.
        Set<OnFocused>(new TVEvent<TVEventArgs>(_ => keyboardInUse = false));
        Set<OnUnfocused>(new TVEvent<TVEventArgs>(_ => keyboardInUse = false));

        // The symbol family at the LABEL's size: an icon beside a label is
        // the same size as the label (design-guide.md 2026-08-31).
        var iconFont = new TVFont
        {
            Family = Resources.StaticResources.Theme.SymbolFont.Family,
            Size = labelFont.Size,
            Border = 0,
        };

        float labelHeight = TextElement.Measure("Ag", labelFont).Y;
        float labelTop = Math.Max(0f, (rowHeight - labelHeight) / 2f);
        float iconTop = Math.Max(0f, (rowHeight - IconBox) / 2f);
        float labelLeft = LeftPad + IconBox + LabelGap;

        for (int i = 0; i < this.items.Count; i++)
        {
            int index = i;
            Item item = this.items[i];

            var row = new FilledRectangleElement(0, (int)(i * rowHeight), (int)width, (int)rowHeight,
                new TVFillSolidColor(() => RowFill(index)));
            row.AddTrait<CursorTrait>().Set(new TVText(
                item.Enabled ? Managers.StandardCursors.PointingHand : Managers.StandardCursors.Forbidden));
            row.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>(_ =>
            {
                keyboardInUse = false;
                Choose(index);
            }));
            AddChild(row, "category" + i);
            rows.Add(row);

            var bar = new FilledRectangleElement(0, 0, (int)AccentBarWidth, (int)rowHeight,
                new TVFillSolidColor(() => index == selectedIndex
                    ? Resources.StaticResources.Theme.AccentSelection
                    : Color.Transparent));
            row.AddChild(bar, "accent");

            if (item.DrawnIcon != null)
            {
                Element drawn = item.DrawnIcon();
                if (drawn != null)
                {
                    drawn.Set<PositionTrait>(new TVVector(LeftPad, iconTop));
                    drawn.Set<SizeTrait>(new TVVector(IconBox, IconBox));
                    if (drawn.HasTrait<ForegroundColorTrait>())
                    {
                        drawn.Set<ForegroundColorTrait>(new TVColor(() => IconColour(index)));
                    }

                    row.AddChild(drawn, "icon");
                }
            }
            else if (!string.IsNullOrEmpty(item.Icon))
            {
                // Same top inset as the label, so glyph and text share a line.
                var glyph = new TextElement { WordWrap = false };
                glyph.Set<PositionTrait>(new TVVector(LeftPad, labelTop));
                glyph.Set<SizeTrait>(new TVVector(IconBox, labelHeight));
                glyph.Set<FontTrait>(iconFont);
                glyph.Set<ForegroundColorTrait>(new TVColor(() => IconColour(index)));
                glyph.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment { Alignment = HorizontalAlignment.Center });
                glyph.Set<TextTrait>(new TVText(item.Icon));
                row.AddChild(glyph, "icon");
            }

            float labelWidth = Math.Max(1f, width - labelLeft - LeftPad);
            var label = new TextElement { WordWrap = false };
            label.Set<PositionTrait>(new TVVector(labelLeft, labelTop));
            label.Set<SizeTrait>(new TVVector(labelWidth, labelHeight));
            label.Set<FontTrait>(labelFont);
            label.Set<ForegroundColorTrait>(new TVColor(() => LabelColour(index)));
            label.Set<TextTrait>(new TVText(TextElement.Ellipsise(item.Label, labelWidth, labelFont)));
            row.AddChild(label, "label");
        }
    }

    /// <summary>Height the list needs for all its rows.</summary>
    public float ContentHeight => rowHeight * items.Count;

    private void Choose(int index)
    {
        if (index < 0 || index >= items.Count || !items[index].Enabled || index == selectedIndex)
        {
            return;
        }

        selectedIndex = index;
        OnSelectionChanged?.Invoke(index);
    }

    /// <summary>The next enabled row from <paramref name="from"/> in
    /// <paramref name="direction"/>, or <paramref name="from"/> itself when
    /// there is none (the list does not wrap: holding Down stops at the
    /// bottom rather than cycling back to the top).</summary>
    private int NextEnabled(int from, int direction)
    {
        for (int i = from + direction; i >= 0 && i < items.Count; i += direction)
        {
            if (items[i].Enabled)
            {
                return i;
            }
        }

        return from;
    }

    private Color RowFill(int index)
    {
        Theme theme = Resources.StaticResources.Theme;
        if (index == selectedIndex)
        {
            return theme.AccentSelection * 0.35f;
        }

        if (items[index].Enabled
            && Resources.StaticResources.InputManager.GetElementState(rows[index]) != Managers.InputManager.ElementState.Normal)
        {
            return theme.AccentSelection * 0.14f;
        }

        return Color.Transparent;
    }

    private Color LabelColour(int index)
    {
        Color text = Resources.StaticResources.Theme.BodyText;
        return items[index].Enabled ? text : Grey(text) * 0.5f;
    }

    /// <summary>Semantic tint (design-guide.md §8): the selected category's
    /// icon wears the accent, the rest the text colour.</summary>
    private Color IconColour(int index)
    {
        Theme theme = Resources.StaticResources.Theme;
        if (!items[index].Enabled)
        {
            return Grey(theme.BodyText) * 0.5f;
        }

        return index == selectedIndex ? theme.AccentSelection : theme.BodyText * 0.85f;
    }

    private static Color Grey(Color c)
    {
        int l = (int)((c.R * 0.3f) + (c.G * 0.59f) + (c.B * 0.11f));
        return new Color(l, l, l, (int)c.A);
    }

    private bool HasFocus => Resources.StaticResources.InputManager.CurrentlyFocused == this;

    /// <summary>Takes keys only while it is actually on screen. Focus is not
    /// cleared when a dialog closes, and a detached list that still counted as
    /// focused would go on swallowing every shortcut in the app.</summary>
    internal override bool CanBeInputFocused => IsAttached();

    private bool IsAttached()
    {
        Element root = Resources.StaticResources.RootWindow;
        for (Element e = this; e != null; e = e.Parent)
        {
            if (e == root)
            {
                return true;
            }
        }

        return false;
    }

    internal override void HandleKeyInput(Keys key, bool shift, bool control)
    {
        if (key is Keys.Up or Keys.Down or Keys.Home or Keys.End)
        {
            keyboardInUse = true;
        }

        switch (key)
        {
            case Keys.Up:
                Choose(NextEnabled(selectedIndex, -1));
                break;
            case Keys.Down:
                Choose(NextEnabled(selectedIndex, 1));
                break;
            case Keys.Home:
                Choose(NextEnabled(-1, 1) is int first && first >= 0 ? first : selectedIndex);
                break;
            case Keys.End:
                Choose(NextEnabled(items.Count, -1) is int last && last < items.Count ? last : selectedIndex);
                break;
        }
    }

    public override void Draw()
    {
        base.Draw();

        if (keyboardInUse && HasFocus && CanBeInputFocused)
        {
            Vector2 at = this.GetActualXnaPosition();
            Vector2 size = CachedSizeTrait.Value().AsXna;
            var outline = new Rectangle((int)at.X - 2, (int)at.Y - 2, (int)size.X + 4, (int)ContentHeight + 4);
            Resources.StaticResources.DrawManager.DrawRectangle(outline, Resources.StaticResources.Theme.AccentSelection, 2);
        }
    }
}
