using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Models;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    /// <summary>
    /// One top-level entry in a <see cref="MenuBarElement"/> — a compact,
    /// text-only button (no icon slot, no "more" chevron, no shortcut
    /// column: those only ever apply to dropdown ENTRIES, which still
    /// render via the existing <see cref="FruitMenuItem"/>/<see cref="FruitPopupMenu"/>
    /// pair unchanged). Deliberately its own class rather than reusing
    /// FruitMenuItem — that class hardcodes a 40px item height (baked into
    /// its icon/shortcut layout math), too tall for a native-Windows-sized
    /// menu strip (2026-08-17 per-modal menu bar rework).
    /// </summary>
    [ElementTraits(typeof(OnMouseButtonHeldDown), typeof(OnHoverTrait), typeof(OnExitTrait))]
    public class MenuBarItem : FilledRectangleElement
    {
        public MenuBarItem(MenuItemModel menuItem, System.Action<ClickEventArgs> action, int width, int height)
        {
            Set<SizeTrait>(new TVVector(width, height));
            Set<BackgroundFillTrait>(new TVSmartFill { States = Resources.StaticResources.Theme.FruitMenuItemStates });
            Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) =>
            {
                if (!menuItem.Enabled)
                {
                    return;
                }

                action(x);
            }));

            int textInset = System.Math.Max(2, (int)(height * 0.15f));
            TextElement textElement = this.AddChildElement<TextElement>();
            textElement.Set<PositionTrait>(new TVVector(MenuBarElement.ItemPaddingX, textInset));
            textElement.Set<SizeTrait>(new TVVector(width - MenuBarElement.ItemPaddingX * 2, height));
            textElement.Set<FontTrait>(Resources.StaticResources.Theme.MenuFont);
            // Theme.BodyText, NOT a fixed Color.Black (found 2026-08-17, live
            // user test: "black on grey" in dark mode) — FruitMenuItemStates'
            // fills (reused here for the hover/press highlight) were tuned
            // for FruitMenuElement's OWN bar, which is a deliberately light
            // strip in BOTH themes (design-guide.md §1.5, now superseded);
            // this bar's background is Theme.SurfaceHeader instead — a
            // normal, theme-matched (dark-in-dark-mode) fill — so the text
            // needs to flip with the theme too, same as every other label on
            // a theme-matched surface (e.g. ModalTitleBarElement's own title
            // text). A snapshot at construction, not a live re-read every
            // frame — matches that same title-text precedent (a menu bar
            // built before a theme switch keeps its old color until rebuilt).
            textElement.Set<ForegroundColorTrait>(new TVColor(menuItem.Enabled ? Resources.StaticResources.Theme.BodyText : Resources.StaticResources.Theme.BodyText * 0.5f));
            textElement.Set<TextTrait>(new TVText(menuItem.Text));
        }
    }
}
