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

            // A menu title is a thing you press (ezmuze #224); a disabled one
            // is not, and says so rather than inviting the click it will drop.
            AddTrait<CursorTrait>().Set(new TVText(menuItem.Enabled
                ? Managers.StandardCursors.PointingHand
                : Managers.StandardCursors.Forbidden));
            // The blue highlight, same states the dropdown rows use, so the
            // bar and the menu it opens light up as one control (2026-09-06:
            // this was a barely-there grey lift, which on the dark theme's
            // SurfaceHeader bar was very nearly invisible — "the bg should be
            // the standard blue on hover, similar to titlebars").
            TVSmartFill highlightFill = new TVSmartFill
            {
                States = Resources.StaticResources.Theme.FruitMenuItemStates,
                FadeSeconds = FruitMenuItem.HighlightFadeSeconds,
            };
            // A disabled entry does not light up at all. It never did
            // visibly, because the old highlight was a grey barely off the
            // bar's own colour; a blue one would have made "greyed out but
            // glowing" a new bug rather than an inherited one.
            if (menuItem.Enabled)
            {
                Set<BackgroundFillTrait>(highlightFill);
            }
            else
            {
                Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
            }
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
            // LIVE: the menu bar is built once and lives for the whole session, so a
            // captured colour left File/Edit/View/Help painted in the palette that
            // was current at startup — invisible after a switch to Light (#66).
            // HIGHLIGHT: and once the blue is under it, BodyText is the wrong
            // answer again in the other direction — it is a near-white in the
            // dark theme (fine) and a near-black in the light one (unreadable
            // on a saturated blue). The label crosses to the highlight's own
            // ink as the fill comes in, off the fill's own eased weight so the
            // two move together rather than each keeping its own clock.
            bool enabled = menuItem.Enabled;
            textElement.Set<ForegroundColorTrait>(new TVColor(() => Color.Lerp(
                enabled
                    ? Resources.StaticResources.Theme.BodyText
                    : Color.Lerp(Resources.StaticResources.Theme.SurfacePanel, Resources.StaticResources.Theme.BodyText, 0.5f),
                Resources.StaticResources.Theme.MenuHighlightInk,
                highlightFill.HighlightWeight)));
            textElement.Set<TextTrait>(new TVText(menuItem.Text));
        }
    }
}
