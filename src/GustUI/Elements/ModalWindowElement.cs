using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Models;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;

namespace GustUI.Elements
{
    /// <summary>Which screen edge a <see cref="ModalWindowElement"/> is
    /// currently docked to (2026-08-16 docked-modal feature; Top/Bottom
    /// added 2026-08-17 — the Stack uses Bottom), or <see cref="None"/> for
    /// a normal floating modal.</summary>
    public enum DockSide
    {
        None,
        Left,
        Right,
        Top,
        Bottom
    }

    [ElementTraits(typeof(BodyTextTrait), typeof(FontTrait), typeof(ForegroundColorTrait), typeof(OnMouseButtonHeldDown))]
    public class ModalWindowElement : FilledRectangleElement
    {
        private ModalTitleBarElement titleBarElement;
        private List<BasicButtonElement> buttons = new List<BasicButtonElement>();
        private FilledRectangleElement buttonBackgroundElement;
        private MenuBarElement menuBar;
        private ToolbarElement toolbar;

        /// <summary>Hosts the menu bar's reserved space (see
        /// <see cref="menuSpacer"/>) and <see cref="toolbar"/> as wrapping
        /// row items — see <see cref="EnsureToolbar"/>. Null until
        /// EnsureToolbar has been called.</summary>
        private HorizontalStackElement chromeRow;

        /// <summary>Invisible placeholder occupying <see cref="menuBar"/>'s
        /// ContentWidth at the start of <see cref="chromeRow"/> — MenuBarElement
        /// itself always tracks the modal's FULL width (it's the actual menu
        /// chrome, always spanning edge to edge), so it can't be a stack item
        /// directly; this stands in for "the space its items already occupy"
        /// purely so the stack's wrap arithmetic reserves room for it before
        /// deciding whether <see cref="toolbar"/> fits on the same row.</summary>
        private FilledRectangleElement menuSpacer;

        /// <summary>Invisible placeholder standing in for <see cref="toolbar"/>
        /// in <see cref="chromeRow"/>'s wrap arithmetic, sized to
        /// <see cref="ToolbarElement.ContentWidth"/> (its NATURAL content
        /// width) rather than toolbar's own SizeTrait — which always
        /// stretches to fill whatever space is left to this modal's right
        /// edge, and so would trivially "always fit," permanently wrapping
        /// on the very first layout pass (found the hard way: toolbar's
        /// constructor already sets its own width to the FULL host width
        /// before it's ever positioned). Update() reads this sentinel's
        /// post-layout position every frame and applies it to the REAL
        /// toolbar (kept a direct child of `this`, not of chromeRow, so its
        /// existing fill-to-the-right-edge behavior is untouched).</summary>
        private FilledRectangleElement toolbarSlot;

        /// <summary>See FullScreenModalElement.chromeRowBg — the ONE
        /// full-width SurfaceHeader strip behind the (transparent) menu bar
        /// and toolbar, so their differing heights can't leave a dark gap
        /// under the shorter bar.</summary>
        private FilledRectangleElement chromeRowBg;

        private void EnsureChromeRowBackground()
        {
            if (chromeRowBg == null)
            {
                chromeRowBg = new FilledRectangleElement(0, 0, (int)this.GetSize().X, MenuBarElement.BarHeight,
                    new TVFillSolidColor(() => Resources.StaticResources.Theme.SurfaceHeader))
                {
                    Depth = -1,
                };
                AddChildElement(chromeRowBg);
                chromeRowBg.Set<PositionTrait>(new TVVector(0, ModalTitleBarElement.BarHeight));
            }
        }

        // ---- height cap + scroll (2026-08-13): a modal whose natural
        // content is taller than the window used to just run off the
        // bottom of the screen (footer buttons included — genuinely
        // unreachable, not just visually cramped). When content.GetSize().Y
        // exceeds MaxModalHeight() — re-checked every frame, see
        // RefreshScrollMode — content is wrapped in a
        // VerticalScrollElement instead of added to this modal directly, and
        // the modal's own height is capped to that same max — title bar and
        // footer buttons always stay on-screen with BottomScreenMargin of
        // breathing room below, and the body scrolls (wheel or its own
        // scrollbar) for the rest. A modal whose content already fits keeps
        // the exact old behavior (no wrapper, no scrollbar rail at all) —
        // this only ever engages for the overflow case.
        private VerticalScrollElement scrollViewport;
        private bool contentScrolls;

        /// <summary>
        /// How much of this modal's own CHROME is showing, 0..1 — the body
        /// fill, title bar, footer and drop shadow, plus the open-slide. 1 is
        /// the normal state and costs nothing.
        ///
        /// Separate from the open animation so a caller can hold a modal
        /// invisible for reasons of its own and let it appear later: the
        /// welcome screen holds it at 0 while the logo animates, so the logo
        /// plays alone over the background, then ramps it to 1. It deliberately
        /// does NOT touch the body content — a caller that wants part of its
        /// body visible during the hold (that logo) needs to be the one saying
        /// which part, which it does with Element.Opacity.
        /// </summary>
        public float ChromeReveal
        {
            get => chromeReveal;
            set => chromeReveal = Math.Clamp(value, 0f, 1f);
        }

        private float chromeReveal = 1f;

        /// <summary>Matches VerticalScrollElement's own hardcoded scrollbar
        /// width — reserved as EXTRA width beyond the content's own natural
        /// width when scrolling, so the rail doesn't overlap the rightmost
        /// slice of the actual content.</summary>
        private const float ScrollbarAllowance = 12f;

        /// <summary>Bottom breathing room below a height-capped modal, so
        /// its footer buttons sit clear of the window edge rather than
        /// flush against it.</summary>
        private const float BottomScreenMargin = 32f;

        /// <summary>Scroll offset (content pixels) of the body, when it's
        /// overflowing and therefore wrapped in a scroll viewport; a no-op
        /// read/write of 0 when it isn't (nothing to scroll).</summary>
        public float ContentScrollPosition
        {
            get => scrollViewport?.ScrollPosition ?? 0f;
            set { if (scrollViewport != null) { scrollViewport.ScrollPosition = value; } }
        }


        public bool FitModalToContent { get; private set; } = true;

        /// <summary>Whether this modal keeps itself centered in the window
        /// (design-guide.md §9: non-full-screen modals default to centered,
        /// and re-center if the window resizes). Defaults to true UNLESS an
        /// explicit position was passed at construction (treated as an
        /// intentional override). Turns off permanently the moment the user
        /// drags the modal by its title bar OR pulls one of its own resize
        /// handles — their placement wins from then on, including across a
        /// later window resize.</summary>
        public bool AutoCenter { get; set; } = true;

        /// <summary>Whether this modal continuously fills whatever space
        /// <see cref="Managers.DockLayout"/> leaves available (2026-08-17 —
        /// the sequencer's own default: opening/closing a docked panel like
        /// the loop browser now shrinks/grows it live, same as any
        /// <see cref="FullScreenModalElement"/> already did). False by
        /// default — opt-in, since most ModalWindowElement hosts
        /// (Preferences, About, a docked panel itself) are sized from their
        /// own content or from DockLayout instead. Turns off permanently
        /// the moment the user manually moves or resizes this instance
        /// (same "user interaction wins, once and for all" rule
        /// <see cref="AutoCenter"/> already follows) — matching "once
        /// docking has finished, windows can be moved and resized to
        /// overlap" (2026-08-17 docking-reinforcement spec): from then on
        /// this behaves like any other floating modal.</summary>
        public bool FillsAvailableSpace
        {
            get => fillsAvailableSpace;
            set
            {
                if (fillsAvailableSpace == value)
                {
                    return;
                }

                fillsAvailableSpace = value;
                if (value)
                {
                    Managers.DockLayout.RegisterFiller(this);
                }
                else
                {
                    Managers.DockLayout.UnregisterFiller(this);
                }
            }
        }

        private bool fillsAvailableSpace;

        /// <summary>Opt-in (2026-08-17, tab-container feature): whether
        /// dragging this modal's title bar onto ANOTHER tabable modal's own
        /// title bar (or an existing <see cref="TabContainerElement"/>)
        /// offers to merge them into a shared tabbed window — see
        /// <see cref="UpdateTabMergeGesture"/>. False by default; the loop
        /// browser and wave bank are the first two opted in.</summary>
        public bool Tabable { get; set; }

        /// <summary>Opt-in (2026-08-21): lets <see cref="Element.MoveToFront"/>
        /// place this floating window ABOVE the full-screen modal tier
        /// (<see cref="FullScreenModalElement.ModalDepth"/>), instead of the
        /// default clamp just below it. For auxiliary floats a full-screen
        /// editor OWNS and shows over itself (the wave picker's loop
        /// browser) — the 2026-08-17 blanket clamp silently buried those
        /// behind their owner, including on every later title-bar drag
        /// (drag-press calls MoveToFront too, so a one-shot depth override
        /// at spawn wouldn't survive). Stays below the loading tier
        /// (90,000) and everything above it.</summary>
        public bool FloatAboveModalTier { get; set; }

        private protected override int MoveToFrontCeiling
            => FloatAboveModalTier ? FullScreenModalElement.ModalDepth + 9999 : base.MoveToFrontCeiling;

        /// <summary>Opt-in hook (2026-08-17, tear-off/dissolve fix): an app-
        /// level owner that constructs and reuses ONE long-lived
        /// ModalWindowElement across every open/close cycle (LoopBrowserPanel,
        /// WaveBankPanel) has no way to find out its content got rehosted in
        /// a DIFFERENT modal instance otherwise. Merging this modal into a
        /// TabContainerElement, then the tab later dissolving back to a
        /// single window OR tearing back off, creates a BRAND NEW
        /// ModalWindowElement for the same content — an owner's own cached
        /// reference to the ORIGINAL modal silently goes stale the moment
        /// that happens, breaking both future Open() calls (nothing left to
        /// re-add to the tree) and the replacement modal's own close (the
        /// owner's Close() would Kill() the wrong, already-dead instance).
        /// Invoked with the NEW hosting modal when content is rehosted
        /// (dissolve, tear-off) — the owner should update its own reference
        /// AND re-wire OnCloseRequested/OnContentRehosted onto it, so the
        /// chain keeps working no matter how many more times this happens.
        /// Invoked with null when the content is actually being closed for
        /// good (a tab's own close-X, or "close all"). Never set (null) is
        /// a no-op — only Tabable hosts that reuse a shell need this at
        /// all.</summary>
        public System.Action<ModalWindowElement> OnContentRehosted { get; set; }

        /// <summary>The title this modal was constructed/last retitled
        /// with. Settable (matching FullScreenModalElement.Title's own
        /// get/set shape) so a host can retitle live the same idiomatic way
        /// ModuleEditorView already does for its FullScreenModalElement
        /// (`modal.Title = "Module editor - " + ...`); the getter is also
        /// what GustUI itself reads back (the tab-container merge reads it
        /// for the new tab's own label) without every caller having to
        /// track its own copy.</summary>
        public string Title
        {
            get => title;
            set
            {
                title = value;
                titleBarElement.SetTitle(value);
            }
        }

        private string title;

        /// <summary>The live content element passed to the constructor —
        /// read-only external access (2026-08-17, tab-container feature):
        /// merging this modal into a <see cref="TabContainerElement"/>
        /// detaches this same element and re-hosts it as a tab's content,
        /// rather than tearing down and rebuilding the hosted view from
        /// scratch.</summary>
        public Element Content => content;

        // =============================================================== tabs

        /// <summary>
        /// Takes <paramref name="source"/>'s view and hosts it as one more tab
        /// here, then kills the now-empty source shell.
        ///
        /// THIS window survives — it is not replaced. That is the whole point
        /// of a window owning its tabs rather than being swapped for a
        /// container: whoever holds a reference to it still holds a live one
        /// afterwards.
        /// </summary>
        public void AddTab(ModalWindowElement source)
        {
            if (source == null || ReferenceEquals(source, this))
            {
                return;
            }

            // A tabbed source folds ALL of its tabs in, not just its active
            // one — the alternative silently destroys the rest.
            if (source.tabs.Count > 1)
            {
                foreach (Tab moving in source.DetachAllTabs())
                {
                    Adopt(moving);
                }

                source.Undock();
                source.Kill();
                return;
            }

            Element body = source.Content;
            var entry = new Tab
            {
                Title = source.Title,
                Content = body,
                RehostCallback = source.OnContentRehosted,
            };

            body?.Parent?.Children?.Remove(body);
            source.tabs.Clear();
            source.Undock();
            source.Kill();

            Adopt(entry);
        }

        /// <summary>
        /// Hosts <paramref name="body"/> as a new tab directly, without a
        /// window being built for it first.
        ///
        /// The build-then-merge route works, but it is visible: a window
        /// appears at its own position and vanishes again on the next frame as
        /// its content is taken. A view that already knows where it belongs
        /// should be able to say so and never flash.
        ///
        /// <paramref name="onRehost"/> is told where the content ends up if it
        /// is later torn off, and told null when the tab is closed.
        /// </summary>
        public void AddTab(string tabTitle, Element body, Action<ModalWindowElement> onRehost = null)
        {
            if (body == null)
            {
                return;
            }

            body.Parent?.Children?.Remove(body);
            Adopt(new Tab { Title = tabTitle, Content = body, RehostCallback = onRehost });
        }

        private void Adopt(Tab entry)
        {
            tabs.Add(entry);
            BuildTabButton(entry);
            ActivateTab(tabs.Count - 1);
            entry.RehostCallback?.Invoke(this);
        }

        private List<Tab> DetachAllTabs()
        {
            var moving = new List<Tab>(tabs);
            foreach (Tab entry in moving)
            {
                entry.Button?.Kill();
                entry.Button = null;
                entry.Content?.Parent?.Children?.Remove(entry.Content);
            }

            tabs.Clear();
            return moving;
        }

        /// <summary>Shows the tab at <paramref name="index"/> — its content
        /// becomes the window's body, which is what makes every sizing and
        /// scrolling rule below apply to whichever tab is on screen.</summary>
        private void ActivateTab(int index)
        {
            if (index < 0 || index >= tabs.Count)
            {
                return;
            }

            Element outgoing = content;
            activeIndex = index;
            content = tabs[index].Content;

            if (ReferenceEquals(outgoing, content))
            {
                RefreshTabStrip();
                return;
            }

            // Detach rather than kill: an inactive tab's view stays alive with
            // all its state, it simply is not in the tree.
            outgoing?.Parent?.Children?.Remove(outgoing);

            if (contentScrolls && scrollViewport != null)
            {
                scrollViewport.AddChild(content, "content");
            }
            else
            {
                AddChild(content, "content");
            }

            content.Set<PositionTrait>(new TVVector(0, ContentTop));
            RefreshTabStrip();
        }

        /// <summary>Brings the tab showing <paramref name="hosted"/> to the
        /// front. False when this window does not host it.</summary>
        public bool ShowTab(Element hosted)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (!ReferenceEquals(tabs[i].Content, hosted))
                {
                    continue;
                }

                ActivateTab(i);
                MoveToFront();
                return true;
            }

            return false;
        }

        private void RemoveTab(int index, bool killContent)
        {
            if (index < 0 || index >= tabs.Count)
            {
                return;
            }

            Tab entry = tabs[index];
            entry.Button?.Kill();
            tabs.RemoveAt(index);

            if (killContent)
            {
                entry.RehostCallback?.Invoke(null);
                if (ReferenceEquals(content, entry.Content))
                {
                    content = null;
                }

                entry.Content?.Kill();
            }
            else
            {
                entry.Content?.Parent?.Children?.Remove(entry.Content);
                if (ReferenceEquals(content, entry.Content))
                {
                    content = null;
                }
            }

            if (tabs.Count == 0)
            {
                Close();
                return;
            }

            // Down to one is not a teardown any more, just a window without a
            // strip. The old container had to dissolve itself into a
            // replacement modal here, carrying its dock side, its inset and its
            // owner callback across by hand.
            int next = Math.Clamp(index >= tabs.Count ? tabs.Count - 1 : index, 0, tabs.Count - 1);
            content = null;
            ActivateTab(next);
        }

        /// <summary>
        /// The trough tabs sit in — DARKER than any of them.
        ///
        /// It used to be the same colour as an inactive tab, which left an
        /// inactive tab invisible against its own strip, and the same colour as
        /// the title bar, which let the last tab bleed into the window chrome
        /// beside it. Three tones now, so the strip reads as a recess: trough,
        /// inactive tab, active tab.
        /// </summary>
        private static Color TabStripFill => Resources.StaticResources.Theme.SurfaceBackdrop;

        private static Color TabInactiveFill => Resources.StaticResources.Theme.SurfaceHeader;

        private static Color TabActiveTop
            => Color.Lerp(Resources.StaticResources.Theme.SurfaceRaised, Resources.StaticResources.Theme.AccentSelection, 0.18f);

        private static Color TabActiveBottom
            => Color.Lerp(Resources.StaticResources.Theme.SurfaceHeader, Resources.StaticResources.Theme.AccentSelection, 0.05f);

        private static Color TabAccent => Resources.StaticResources.Theme.AccentSelection;

        /// <summary>
        /// Builds, shows or hides the strip, and lays the tabs across it.
        ///
        /// One tab means no strip at all: the ordinary title bar underneath is
        /// already a full-width bar carrying that view name, which is what a
        /// lone tab would draw anyway. The strip appears at two.
        /// </summary>
        private void RefreshTabStrip()
        {
            if (tabs.Count < 2)
            {
                // The buttons are siblings of the strip, not children, so
                // killing the strip alone would leave them on the title bar.
                foreach (Tab stale in tabs)
                {
                    stale.Button?.Kill();
                    stale.Button = null;
                }

                tabStrip?.Kill();
                tabStrip = null;
                if (titleBarElement != null)
                {
                    titleBarElement.LeftReserved = 0f;
                    titleBarElement.ChromeHidden = false;
                }

                if (tabs.Count == 1)
                {
                    Title = tabs[0].Title;
                }

                return;
            }

            if (tabStrip == null)
            {
                tabStrip = new FilledRectangleElement(0, 0, 10, ModalTitleBarElement.BarHeight,
                    new TVFillSolidColor(TabStripFill));

                // Above the title bar it covers, and with its own press guard:
                // GustUI gives the event to EVERY element whose rect contains
                // the point, so without this a tab click ALSO started a window
                // drag, and that drag swallowed the tab release.
                tabStrip.Depth = 10;
                tabStrip.Set<OnMousePress>(new TVEvent<ClickEventArgs>(args =>
                {
                    if (!IsOverTab(args.GlobalMousePosition.AsXna))
                    {
                        HandleTitleBarPress(args);
                    }
                }));

                AddChildElement(tabStrip);

                foreach (Tab pending in tabs)
                {
                    BuildTabButton(pending);
                }
            }

            TVVector size = ElementTrait<SizeTrait>().Value();

            // The strip stops short of this window own close/maximise. Those
            // are chrome, not a tab, and the gap beside them is what a tabbed
            // window gets dragged by once the tabs have eaten the rest.
            // The FULL width. A tabbed window has no separate title bar and no
            // chrome of its own: every tab carries its own close and pop-out,
            // the active one carries maximise, and dragging any of them moves
            // the window. So there is nothing left for a bar to hold.
            float stripWidth = Math.Max(MinTabWidth, size.X);
            if (titleBarElement != null)
            {
                // Reserved across the WHOLE bar: the strip covers it, and its
                // close/maximise would otherwise sit under the tabs, still
                // taking clicks.
                titleBarElement.LeftReserved = stripWidth;
                titleBarElement.ChromeHidden = true;
            }

            Title = string.Empty;
            tabStrip.Set<PositionTrait>(new TVVector(0, 0));
            tabStrip.Set<SizeTrait>(new TVVector(stripWidth, ModalTitleBarElement.BarHeight));

            bool active = IsFrontmostWindow(this);
            tabStrip.Set<BackgroundFillTrait>(new TVFillSolidColor(active ? TabStripFill : Dim(TabStripFill)));

            float shared = Math.Max(MinTabWidth, (stripWidth - (TabGap * (tabs.Count - 1))) / tabs.Count);

            float x = 0f;
            for (int i = 0; i < tabs.Count; i++)
            {
                Tab entry = tabs[i];
                if (entry.Button == null)
                {
                    BuildTabButton(entry);
                }

                entry.Width = shared;

                // Always ABOVE the strip, which is an opaque fill: this line
                // used to reset the depth set at build time, dropping every
                // idle tab underneath its own background.
                entry.Button.Depth = 11;
                entry.Button.Set<PositionTrait>(new TVVector(x, 0));
                entry.Button.Set<SizeTrait>(new TVVector(shared, ModalTitleBarElement.BarHeight));

                bool isActive = i == activeIndex;
                entry.Button.Set<BackgroundFillTrait>(isActive
                    ? new TVFillSimpleGradient(
                        active ? TabActiveTop : Dim(TabActiveTop),
                        active ? TabActiveBottom : Dim(TabActiveBottom),
                        Direction.Vertically)
                    : new TVFillSolidColor(active ? TabInactiveFill : Dim(TabInactiveFill)));

                entry.Underline.Set<PositionTrait>(new TVVector(0, ModalTitleBarElement.BarHeight - 2));
                entry.Underline.Set<SizeTrait>(new TVVector(isActive ? shared : 0f, 2));
                entry.Underline.Set<BackgroundFillTrait>(new TVFillSolidColor(active ? TabAccent : Dim(TabAccent)));

                float labelWidth = Math.Max(10f, shared - TabPaddingX - ((TabCloseSize + 4) * 3) - 6);
                entry.Label.Set<SizeTrait>(new TVVector(labelWidth, ModalTitleBarElement.BarHeight));
                entry.Label.Set<TextTrait>(new TVText(TextElement.Ellipsise(
                    entry.Title, labelWidth, Resources.StaticResources.Theme.UiFontSmall)));

                float slotY = (ModalTitleBarElement.BarHeight - TabCloseSize) / 2f;
                float slot = shared - TabCloseSize - 6;
                entry.CloseX.Set<PositionTrait>(new TVVector(slot, slotY));

                slot -= TabCloseSize + 4;
                entry.PopOut.Set<PositionTrait>(new TVVector(slot, slotY));
                entry.PopOut.Set<SizeTrait>(new TVVector(TabCloseSize, TabCloseSize));

                // Blanked rather than resized to nothing: a zero-sized text
                // element still draws its glyph, so sizing was never going to
                // hide it.
                entry.PopOut.Set<TextTrait>(new TVText(tabs.Count > 1 ? UIFont.Symbol.NewWindow.Icon() : string.Empty));

                // Maximise belongs to the WINDOW, so it rides on whichever tab
                // is showing rather than being repeated on every one.
                slot -= TabCloseSize + 4;
                entry.Maximise.Set<PositionTrait>(new TVVector(slot, slotY));
                entry.Maximise.Set<SizeTrait>(new TVVector(TabCloseSize, TabCloseSize));
                entry.Maximise.Set<TextTrait>(new TVText(!isActive
                    ? string.Empty
                    : isFullScreen
                        ? Resources.StaticResources.Theme.Icons.MinimizeIcon
                        : Resources.StaticResources.Theme.Icons.MaximizeIcon));

                x += shared + TabGap;
            }
        }

        private static Color Dim(Color c)
        {
            float grey = (c.R * 0.3f) + (c.G * 0.59f) + (c.B * 0.11f);
            return new Color(
                (int)MathHelper.Lerp(c.R, grey, 0.55f),
                (int)MathHelper.Lerp(c.G, grey, 0.55f),
                (int)MathHelper.Lerp(c.B, grey, 0.55f));
        }

        private void BuildTabButton(Tab entry)
        {
            if (tabStrip == null || entry.Button != null)
            {
                return;
            }

            // A drawn edge, not just a fill. Fills alone left an inactive tab
            // looking like the window behind it, and the last ACTIVE tab
            // running straight into the title bar beside it — the border is
            // what says "this is a tab" in both cases.
            var button = new FilledRectangleElement(0, 0, 100, ModalTitleBarElement.BarHeight,
                new TVFillSolidColor(TabInactiveFill), 1,
                Resources.StaticResources.Theme.SurfaceBorder);

            var label = new TextElement { WordWrap = false };
            label.Set<PositionTrait>(new TVVector(TabPaddingX, 0));
            label.Set<SizeTrait>(new TVVector(140, ModalTitleBarElement.BarHeight));
            label.Set<FontTrait>(Resources.StaticResources.Theme.UiFontSmall);
            label.Set<ForegroundColorTrait>(new TVColor(Resources.StaticResources.Theme.BodyText));
            label.Set<VerticalAlignmentTrait>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });
            label.Set<TextTrait>(new TVText(entry.Title));
            button.AddChild(label, "label");
            entry.Label = label;

            entry.Underline = new FilledRectangleElement(0, ModalTitleBarElement.BarHeight - 2, 0, 2,
                new TVFillSolidColor(TabAccent));

            button.AddChild(entry.Underline, "accent-underline");

            var close = new FilledRectangleElement(0, 0, TabCloseSize, TabCloseSize,
                new TVFillSolidColor(Color.Transparent));

            TextElement closeGlyph = close.AddChildElement<TextElement>();
            closeGlyph.Set<SizeTrait>(new TVVector(TabCloseSize, TabCloseSize));
            closeGlyph.Set<FontTrait>(new TVFont
            {
                Family = Resources.StaticResources.Theme.AltSymbolFont.Family,
                Size = TabCloseSize * 0.75f,
                Border = 0,
            });

            closeGlyph.Set<ForegroundColorTrait>(new TVColor(Resources.StaticResources.Theme.BodyText));
            closeGlyph.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment { Alignment = HorizontalAlignment.Center });
            closeGlyph.Set<VerticalAlignmentTrait>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });
            closeGlyph.Set<TextTrait>(Resources.StaticResources.Theme.Icons.CloseIcon.ToTextTrait());
            close.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>(_ => closeTabRequested = entry));
            close.AddTrait<OnEnterTrait>().Set(new TVEvent<ClickEventArgs>(_ =>
                close.Set<BackgroundFillTrait>(new TVFillSolidColor(Resources.StaticResources.Theme.AccentMuteOn))));
            close.Set<OnExitTrait>(new TVEvent<ClickEventArgs>(_ =>
                close.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent))));

            button.AddChild(close, "close");
            entry.CloseX = close;

            entry.PopOut = TabGlyph(button, "popout",
                new TVText(UIFont.Symbol.NewWindow.Icon()),
                "Move this tab into its own window",
                () => popOutRequested = entry);

            entry.Maximise = TabGlyph(button, "maximise",
                new TVText(Resources.StaticResources.Theme.Icons.MaximizeIcon),
                "Maximise this window",
                () =>
                {
                    if (DockedSide == DockSide.None)
                    {
                        ToggleFullScreen();
                    }
                });

            button.Set<OnMousePress>(new TVEvent<ClickEventArgs>(args => BeginTabDrag(entry, args)));

            entry.Button = button;

            // A sibling of the strip, not a child of it. As a child, the strip
            // sat between the button and the pointer and the close-X never got
            // its release — the container this replaced had them as siblings
            // for the same reason.
            button.Depth = 11;
            AddChild(button, "tab");
        }

        /// <summary>One of a tab's own little glyph buttons.</summary>
        private TextElement TabGlyph(Element parent, string name, TVText glyph, string tooltip, Action onClick)
        {
            var element = new TextElement { WordWrap = false };
            element.Set<SizeTrait>(new TVVector(TabCloseSize, TabCloseSize));
            element.Set<FontTrait>(new TVFont
            {
                Family = Resources.StaticResources.Theme.SymbolFont.Family,
                Size = TabCloseSize * 0.7f,
                Border = 0,
            });

            element.Set<ForegroundColorTrait>(new TVColor(Resources.StaticResources.Theme.BodyText));
            element.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment { Alignment = HorizontalAlignment.Center });
            element.Set<VerticalAlignmentTrait>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });
            element.Set<TextTrait>(glyph);

            // AddTrait, not Set: a TextElement does not DECLARE this trait, and
            // Set on a trait an element never declared throws.
            element.AddTrait<OnMouseRelease>().Set(new TVEvent<ClickEventArgs>(_ => onClick()));
            TooltipElement.Attach(element, tooltip);
            parent.AddChild(element, name);
            return element;
        }

        private bool IsOverTab(Vector2 mouse)
        {
            foreach (Tab entry in tabs)
            {
                if (entry.Button == null)
                {
                    continue;
                }

                Vector2 pos = entry.Button.GetActualXnaPosition();
                TVVector size = entry.Button.GetSize();
                if (mouse.X >= pos.X && mouse.X <= pos.X + size.X
                    && mouse.Y >= pos.Y && mouse.Y <= pos.Y + size.Y)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A press on a tab: activate it, then hand the gesture to the window
        /// drag, because a tab strip IS this window's title bar and dragging a
        /// title bar moves the window.
        ///
        /// There is no drag-to-tear-off and no drag-to-reorder. Both were
        /// gestures layered onto the same drag, which meant every tab press had
        /// to guess which of three things the user meant, and a tab could
        /// silently leave its window on a slightly clumsy click. Taking a tab
        /// out is a button on the tab instead: it says what it does.
        /// </summary>
        private void BeginTabDrag(Tab entry, ClickEventArgs args)
        {
            int index = tabs.IndexOf(entry);
            if (index < 0)
            {
                return;
            }

            Vector2 mouse = args.GlobalMousePosition.AsXna;
            if (OverElement(entry.CloseX, mouse) || OverElement(entry.PopOut, mouse)
                || OverElement(entry.Maximise, mouse))
            {
                return;
            }

            if (index != activeIndex)
            {
                ActivateTab(index);
            }

            HandleTitleBarPress(args);
        }

        private static bool OverElement(Element element, Vector2 point)
        {
            if (element == null)
            {
                return false;
            }

            Vector2 pos = element.GetActualXnaPosition();
            TVVector size = element.GetSize();
            return point.X >= pos.X && point.X <= pos.X + size.X
                && point.Y >= pos.Y && point.Y <= pos.Y + size.Y;
        }

        /// <summary>
        /// Takes a tab out into its own window — the explicit version of what
        /// dragging one off the strip used to do by accident.
        /// </summary>
        private void PopOutTab(Tab entry)
        {
            int index = tabs.IndexOf(entry);
            if (index < 0 || tabs.Count < 2)
            {
                return;
            }

            MouseState mouse = Resources.StaticResources.InputManager.CurrentMouseState;
            TVVector size = ElementTrait<SizeTrait>().Value();
            int bottomInset = BottomInset;
            Tab moved = DetachTab(index);

            var modal = new ModalWindowElement(moved.Title, moved.Content,
                position: new TVVector(mouse.X - (size.X / 2f), mouse.Y - (ModalTitleBarElement.BarHeight / 2f)),
                size: size, fitToContent: false, resizable: true, closable: true,
                minSize: new Vector2(MinSize.X, MinSize.Y))
            {
                Tabable = true,
                BottomInset = bottomInset,
            };

            Action<ModalWindowElement> rehost = moved.RehostCallback;
            modal.OnCloseRequested = () =>
            {
                rehost?.Invoke(null);
                modal.Kill();
            };

            modal.OnContentRehosted = rehost;
            rehost?.Invoke(modal);
            Resources.StaticResources.RootWindow.AddChild(modal, "tab-popped-" + Guid.NewGuid());
        }

        /// <summary>Per-frame tab upkeep, called from Update.</summary>
        private void UpdateTabs()
        {
            if (closeTabRequested != null)
            {
                Tab entry = closeTabRequested;
                closeTabRequested = null;
                RemoveTab(tabs.IndexOf(entry), killContent: true);
                return;
            }

            if (popOutRequested != null)
            {
                Tab entry = popOutRequested;
                popOutRequested = null;
                PopOutTab(entry);
                return;
            }

            if (tabs.Count > 1)
            {
                RefreshTabStrip();
                SizeAllTabs();
            }
        }

        /// <summary>
        /// Gives every tab the client area, not just the one on screen.
        ///
        /// An inactive tab is detached from the tree, so the per-frame layout
        /// never reaches it: resize a window and only the tab you were looking
        /// at knew about it — the others came back the size they were when you
        /// last left them. A view lays itself out from its own SizeTrait, so
        /// setting that is enough even while it is detached.
        /// </summary>
        private void SizeAllTabs()
        {
            if (FitModalToContent || contentScrolls)
            {
                return;
            }

            TVVector size = ElementTrait<SizeTrait>().Value();
            var box = new TVVector(size.X, Math.Max(0f, size.Y - ContentTop));

            foreach (Tab entry in tabs)
            {
                if (entry.Content == null || ReferenceEquals(entry.Content, content))
                {
                    continue;
                }

                // SIZE only. Position is how a hidden tab is kept off screen,
                // so writing it here drags the inactive tab back into view and
                // draws two tabs at once.
                entry.Content.Set<SizeTrait>(box);
            }
        }

        private Tab DetachTab(int index)
        {
            Tab entry = tabs[index];
            RemoveTab(index, killContent: false);
            return entry;
        }


        /// <summary>
        /// The window showing <paramref name="element"/> right now — its own
        /// shell, or the tab container that adopted it.
        ///
        /// Walks UP from the element, which is the only reliable direction: a
        /// view cannot remember the shell it was built with, because merging
        /// into a container reparents the content and kills that shell. Where
        /// the content currently sits is the question with a stable answer.
        /// </summary>
        public static ModalWindowElement HostWindowOf(Element element)
        {
            for (Element walk = element; walk != null; walk = walk.Parent)
            {
                if (walk is ModalWindowElement window)
                {
                    return window;
                }
            }

            return null;
        }

        /// <summary>
        /// Brings <paramref name="element"/> into view: if it is a tab's
        /// content its tab is made active, otherwise its window comes to the
        /// front. False when it is not in a window at all.
        ///
        /// This is what "open something already open" should do. An app asking
        /// to show a view it has already built should never have to know
        /// whether that view ended up as a floating window or as somebody's
        /// third tab — and tearing a tab out to add a fresh one in the same
        /// gesture scrambles the strip it came from.
        /// </summary>
        public static bool Reveal(Element element)
        {
            for (Element walk = element; walk != null; walk = walk.Parent)
            {
                if (walk.Parent is ModalWindowElement holder && holder.ShowTab(walk))
                {
                    return true;
                }

                if (walk is ModalWindowElement window)
                {
                    window.MoveToFront();
                    return true;
                }
            }

            return false;
        }

        /// <summary>What the title bar's X does. Null (default) = the base
        /// Kill() path (starts the close animation) via
        /// ModalTitleBarElement's own default. Set this when a host needs
        /// extra teardown beyond Kill() (e.g. stopping an audition preview,
        /// clearing an IsOpen flag) — matching FullScreenModalElement's
        /// identically-named/-shaped property. titleBarElement always
        /// exists by construction (unlike FullScreenModalElement's lazily-
        /// built one), so no null-guard needed here.</summary>
        public System.Action OnCloseRequested
        {
            get => titleBarElement.OnCloseRequested;
            set => titleBarElement.OnCloseRequested = value;
        }

        /// <summary>Height of the chrome row(s) directly below the title
        /// bar. With a toolbar, <see cref="chromeRow"/> owns this entirely —
        /// its own auto-computed height already reflects whether the
        /// toolbar fit beside the menu bar's reserved space (one row, the
        /// MAX of the two) or wrapped below it (two rows, menu bar height +
        /// toolbar height — see EnsureToolbar). Menu-only (no toolbar) keeps
        /// the simple one-row case.</summary>
        private int ChromeRowHeight => chromeRow != null
            ? (int)chromeRow.GetSize().Y
            : (menuBar != null ? MenuBarElement.BarHeight : 0);

        /// <summary>Y (modal-relative) where hosted content starts: below
        /// the title bar (always present — see titleBarElement's own doc
        /// comment) plus the shared menu/toolbar row (see
        /// <see cref="ChromeRowHeight"/>). Mirrors FullScreenModalElement's
        /// identically-named property (2026-08-17 per-modal menu rework).</summary>
        public int ContentTop => ModalTitleBarElement.BarHeight + ChromeRowHeight;

        /// <summary>
        /// The area a body element is actually VISIBLE in, in element space —
        /// the scroll viewport's size when the body scrolls, the modal's own
        /// content box when it doesn't.
        ///
        /// A body that lays itself out (rather than just growing) needs this:
        /// its own SizeTrait is what it was BUILT at, and when that exceeds the
        /// viewport the modal simply scrolls it, so anything the body anchored
        /// to its own bottom edge silently ends up below the fold. Without this
        /// the only way to find the real height was to reverse-engineer the
        /// chrome, margin and button-row arithmetic from outside.
        /// </summary>
        public Vector2 ContentViewportSize
        {
            get
            {
                if (scrollViewport != null)
                {
                    return scrollViewport.GetSize().AsXna;
                }

                // A modal that does NOT fit itself to its content (a resizable
                // or docked panel) has its size decided from outside, so the
                // content box is that size minus the chrome. Asking
                // EffectiveContent*() here would answer with the BODY's own
                // SizeTrait — which is what the body was built at, and for a
                // body that lays itself out from this property is simply the
                // answer it gave last time. That circularity meant a resizable
                // modal's contents never learned they had more room.
                if (!FitModalToContent)
                {
                    float buttonHeight = buttons.Count > 0 ? 80 : ContentMargin;
                    TVVector size = this.GetSize();
                    return new Vector2(
                        Math.Max(1f, size.X - (ContentMargin * 2)),
                        Math.Max(1f, size.Y - ContentTop - ContentMargin - buttonHeight));
                }

                return new Vector2(EffectiveContentWidth(), EffectiveContentHeight());
            }
        }

        /// <summary>
        /// Shows (or replaces) this modal's own menu strip, directly below
        /// its title bar — the per-modal replacement for the old global
        /// FruitMenuElement. No-op if <paramref name="sections"/> is
        /// null/empty. See FullScreenModalElement.SetMenu's identical doc
        /// comment; this is the ModalWindowElement counterpart (the
        /// sequencer's own host type).
        /// </summary>
        public void SetMenu(List<MenuItemModel> sections)
        {
            if (sections == null || sections.Count == 0)
            {
                return;
            }

            if (menuBar == null)
            {
                EnsureChromeRowBackground();
                menuBar = new MenuBarElement(this, sections);
                menuBar.Set<PositionTrait>(new TVVector(0, ModalTitleBarElement.BarHeight));
                AddChildElement(menuBar);
            }
            else
            {
                menuBar.SetItems(sections);
            }
        }

        /// <summary>
        /// Lazily builds (or returns the existing) toolbar strip, hosted in
        /// <see cref="chromeRow"/> (a wrapping <see cref="HorizontalStackElement"/>)
        /// alongside <see cref="menuSpacer"/> — a placeholder reserving the
        /// menu bar's own ContentWidth — when <see cref="SetMenu"/> has been
        /// called. The toolbar sits right after that reserved space when it
        /// fits, or wraps to its own row directly below the menu bar when it
        /// doesn't (2026-08-18, user request — "if the toolbar would go
        /// outside of the window area, it appears below the fruit menu, not
        /// beside it"); Update() keeps chromeRow.WrapWidth synced to this
        /// modal's live width every frame, so a resize re-wraps live. With
        /// no menu, the toolbar is chromeRow's only item and starts at x=0,
        /// same as before wrapping existed (a lone item never wraps against
        /// nothing). Totally optional: a view that never calls this gets no
        /// strip and no reserved <see cref="ContentTop"/> space, same as
        /// SetMenu. Call AFTER SetMenu if this modal also has a menu, so the
        /// reserved space matches what's actually there. Populate the
        /// returned element by adding children to it directly (arbitrary
        /// widgets — icon buttons, sliders, text — not a declarative list
        /// like SetMenu's sections).
        /// </summary>
        public ToolbarElement EnsureToolbar()
        {
            if (toolbar == null)
            {
                EnsureChromeRowBackground();

                // toolbar itself stays a direct child of `this`, not of
                // chromeRow — see toolbarSlot's own doc comment for why.
                toolbar = new ToolbarElement(this);
                AddChildElement(toolbar);

                chromeRow = new HorizontalStackElement { WrapWidth = this.GetSize().X };
                chromeRow.Set<PositionTrait>(new TVVector(0, ModalTitleBarElement.BarHeight));
                AddChildElement(chromeRow);

                if (menuBar != null)
                {
                    menuSpacer = new FilledRectangleElement(0, 0, (int)menuBar.ContentWidth, MenuBarElement.BarHeight,
                        new TVFillSolidColor(Color.Transparent));
                    chromeRow.AddChild(menuSpacer, "menu-spacer");
                }

                toolbarSlot = new FilledRectangleElement(0, 0, (int)toolbar.ContentWidth, ToolbarElement.BarHeight,
                    new TVFillSolidColor(Color.Transparent));
                chromeRow.AddChild(toolbarSlot, "toolbar-slot");
            }

            return toolbar;
        }

        /// <summary>Whether this modal is currently "maximized" (fills the
        /// window, auto-tracking it every frame — base Element.Update()'s
        /// own isFullScreen branch, unconditional and NOT limited to
        /// ModalWindowElement). <c>isFullScreen</c> itself is `internal`
        /// (Element.cs), invisible outside GustUI's own assembly; exposed
        /// here as get/set (not just via the internal, animated-transition
        /// ToggleFullScreen()) so a host in another assembly that
        /// reconstructs this modal in response to derived state (2026-08-17
        /// — the sequencer's own row-height-crossed-a-threshold rebuild)
        /// can carry this flag over to the new instance directly. No
        /// transition needed on the set: the very next Update() already
        /// snaps Size/Position to the correct fill-window rect
        /// unconditionally once isFullScreen is true, the same way the
        /// constructor's own explicit initial Position/Size already bypass
        /// animation. Found missing the hard way — a maximized sequencer,
        /// resized via the OS window, lost this flag on its first mid-
        /// resize rebuild, silently stopped auto-tracking the window from
        /// then on, and the resulting oversized-modal-vs-shrinking-window
        /// mismatch fed the screen-edge clamp into visibly flashing.</summary>
        public bool IsFullScreen
        {
            get => isFullScreen;
            set => isFullScreen = value;
        }

        // ---- keyboard-hook scope (2026-08-16) ----
        // Opt-in (constructor's `pushHookScope`) — most ModalWindowElement
        // hosts (Preferences, About, the docked Loop Browser/Wave Bank)
        // deliberately do NOT want this: it would suppress whatever view's
        // shortcuts are active underneath for as long as they're open,
        // which is wrong for a non-exclusive docked panel. Added for the
        // sequencer specifically, which — now that it's a ModalWindowElement
        // instead of a FullScreenModalElement — would otherwise silently
        // lose the scope suppression it always relied on: its own Space/
        // Ctrl+Z/L/Enter/Escape hooks need to go quiet while a secondary
        // FullScreenModalElement (Piano Roll etc.) is open on top, the same
        // way FullScreenModalElement's own construction already pushes a
        // scope for exactly this reason.
        private readonly bool hasHookScope;
        private readonly int hookScopeToken;
        private bool hookScopeClosed;

        /// <summary>Pops this modal's keyboard-hook scope (if constructed
        /// with <c>pushHookScope: true</c> — a no-op otherwise) and removes
        /// the element — the FullScreenModalElement-style close path. A
        /// bare <see cref="Kill"/> (e.g. the title bar's own close X, when
        /// this modal is also closable) does NOT pop the scope on its own;
        /// wired as this instance's default <see cref="OnCloseRequested"/>
        /// below so that path still goes through here rather than leaking
        /// the scope — same asymmetry FullScreenModalElement already has,
        /// same fix.</summary>
        public void Close()
        {
            if (hasHookScope && !hookScopeClosed)
            {
                hookScopeClosed = true;
                Resources.StaticResources.InputManager.PopHookScope(hookScopeToken);
            }

            Kill();
        }

        /// <summary>Closes the window HOSTING <paramref name="content"/>: for
        /// a plain modal that is this whole window (<see cref="Close"/>);
        /// <see cref="TabContainerElement"/> overrides it to close only that
        /// content's TAB, leaving sibling tabs alive. Found 2026-08-22: a
        /// docked panel's Close() killed its shell — which, once merged, was
        /// the shared tab container — taking every other panel tabbed with
        /// it down too. Panels call this on their CURRENT shell (whatever
        /// OnContentRehosted last handed them).</summary>
        public virtual void CloseContent(Element content)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (ReferenceEquals(tabs[i].Content, content))
                {
                    RemoveTab(i, killContent: true);
                    return;
                }
            }

            Close();
        }

        /// <summary>Closes whatever window CURRENTLY hosts <paramref name="content"/>
        /// — found by walking its parent chain to the nearest ModalWindowElement
        /// (a plain modal, a scroll-wrapped one, or a TabContainerElement) and
        /// calling <see cref="CloseContent"/> on it. A panel's own `modal`
        /// field can be stale: merging into a tab container does not notify
        /// the owner (only tear-off/dissolve/close do), so a close routed
        /// through the field hit the dead pre-merge shell and left the tab
        /// behind (found 2026-08-22: every Stack swap ADDED tabs). Returns
        /// false when the content isn't hosted by any window.</summary>
        public static bool CloseHostOf(Element content)
        {
            Element cursor = content?.Parent;
            while (cursor != null)
            {
                if (cursor is ModalWindowElement host)
                {
                    host.CloseContent(content);
                    return true;
                }

                cursor = cursor.Parent;
            }

            return false;
        }

        // ---- free-form resize (2026-08-16) ----

        private ResizeHandlesElement resizeHandles;

        /// <summary>The draggable boundary with whatever shares the screen
        /// with this panel — see DockSplitterElement. Only while docked.</summary>
        private DockSplitterElement dockSplitter;

        /// <summary>True while the user is actively dragging one of this
        /// modal's own resize handles (only ever true when constructed with
        /// <c>resizable: true</c>). A host that tears this modal down and
        /// reconstructs it in response to some derived state (the
        /// sequencer's own row-height-crossed-a-threshold rebuild, 2026-08-17)
        /// should check this (and <see cref="BeingDragged"/>, the title-bar-
        /// move equivalent) and defer until both are false — a fresh
        /// ModalWindowElement means a fresh ResizeHandlesElement with no
        /// in-progress drag state, which silently ends the gesture the
        /// instant a mid-drag rebuild fires.</summary>
        /// <summary>True while this modal is being resized by any of its own
        /// handles — including the dock splitter, so anything that backs off
        /// during a resize backs off for that too rather than treating a
        /// boundary drag as an ordinary click.</summary>
        public bool IsResizing =>
            (resizeHandles != null && resizeHandles.IsActive)
            || (dockSplitter != null && dockSplitter.IsDragging);

        /// <summary>Smallest this modal may be dragged down to via its own
        /// resize handles (see <see cref="ResizeHandlesElement"/>) — the
        /// moving edge stops instead of crossing the fixed opposite edge.
        /// Only meaningful when this instance was constructed with
        /// <c>resizable: true</c>; ignored otherwise. Default is a generic
        /// small floor — a host with real minimum-usable-content needs
        /// (the sequencer's toolbar, say) should set its own larger value.</summary>
        public Vector2 MinSize { get; set; } = new Vector2(240, 160);

        // ---- docked modal (2026-08-16) ----
        // A third layout mode alongside floating/full-screen: pinned full-
        // height to the left or right screen edge, shrinking every open
        // FullScreenModalElement around it (GustUI/Managers/DockLayout.cs).
        // "Any normal non-full-screen modal can be docked" (the feature's
        // own requirement) is why this lives on the shared base rather than
        // being special-cased to any one panel.

        /// <summary>Which edge this modal is currently docked to, or
        /// <see cref="DockSide.None"/> while floating/full-screen.</summary>
        public DockSide DockedSide { get; private set; } = DockSide.None;

        /// <summary>Window-bottom pixels a DOCKED modal leaves uncovered
        /// (e.g. the app's status bar) — same purpose as
        /// <see cref="FullScreenModalElement.BottomInset"/>, caller-supplied
        /// since GustUI itself doesn't know about app-level chrome. No
        /// effect while floating.</summary>
        public int BottomInset { get; set; }

        private const float DockEdgeZonePixels = 40f;
        private const double DockHoldThresholdSeconds = 1.0;
        private DockSide dockHoldZone = DockSide.None;
        private double dockHoldStartSeconds;
        private DockSide pendingDockSide = DockSide.None;

        /// <summary>How far the mouse must move from where a docked title
        /// bar was pressed before that press escalates into an actual
        /// Undock() (see <see cref="HandleTitleBarPress"/>/the docked branch
        /// of <see cref="Update"/>) — 2026-08-17, found from the user's own
        /// question about this code: undocking on PRESS rather than on real
        /// movement meant a plain click anywhere on a docked panel's title
        /// bar (not a drag at all) permanently popped it to floating, with
        /// no spec anywhere calling for click-to-undock.</summary>
        private const float UndockDragThresholdPixels = 6f;

        /// <summary>Mouse position at the moment a still-docked title bar was
        /// pressed, or null when not mid-press — compared against the
        /// current mouse position each frame while <see cref="Element.BeingDragged"/>
        /// to decide whether this press has become a real drag yet.</summary>
        private Vector2? dockPressMouse;

        /// <summary>
        /// One hosted view. Every window has at least one — its own body — so
        /// "a window" and "a tabbed window" are the same object in two states
        /// rather than two classes.
        ///
        /// They used to be two: a TabContainerElement that EXTENDED the thing
        /// it should have BEEN, passing a 10x10 transparent placeholder as its
        /// own content and managing tabs beside the machinery it inherited.
        /// Merging then had to build a container and destroy both shells, which
        /// is why every view needed telling that its content had been rehosted.
        /// A window now keeps its identity for its whole life.
        /// </summary>
        public sealed class Tab
        {
            public string Title;
            public Element Content;

            /// <summary>Told when this tab's content moves to another window or
            /// is closed — see <see cref="OnContentRehosted"/>.</summary>
            internal Action<ModalWindowElement> RehostCallback;

            internal FilledRectangleElement Button;
            internal TextElement Label;
            internal FilledRectangleElement Underline;
            internal FilledRectangleElement CloseX;
            internal TextElement PopOut;
            internal TextElement Maximise;
            internal float Width;
        }

        private readonly List<Tab> tabs = new List<Tab>();
        private int activeIndex;
        private FilledRectangleElement tabStrip;
        private Tab closeTabRequested;
        private Tab popOutRequested;


        private const int TabPaddingX = 12;
        private const int TabCloseSize = 16;
        private const int TabGap = 4;
        private const int MinTabWidth = 80;

        /// <summary>Grab area kept clear beside the window buttons, so a tabbed
        /// window can still be moved and docked.</summary>
        private const int TabDragGap = 44;

        /// <summary>Trough left between the last tab and the window chrome.</summary>
        private const int TabTrailingGap = 10;

        /// <summary>How many views this window hosts. One is an ordinary
        /// window; the strip only appears at two or more.</summary>
        public int TabCount => tabs.Count;

        private Element content;
        public ModalWindowElement()
        {

            titleBarElement = this.AddChildElement<ModalTitleBarElement>();
            buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();

            Setup();
        }
        public ModalWindowElement(string title, string body, List<BasicButtonElement> buttons = null, TVVector position = null, TVVector size = null)
        {
            // Store directly, not via the Title property setter — that
            // delegates to titleBarElement.SetTitle, and titleBarElement
            // isn't constructed yet at this point in either constructor.
            // ModalTitleBarElement's own constructor already receives
            // `title` and sets the initial text itself.
            this.title = title;
            AutoCenter = position == null;
            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<ForegroundColorTrait>(new TVColor(BodyForeground));

            Set<BodyTextTrait>(new TVText(body));
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));
            Set<BorderFillTrait>(new TVBorder9Grid());


            if (buttons != null)
            {
                this.buttons.AddRange(buttons);
            }
            else
            {
                this.buttons = new List<BasicButtonElement>();
            }

            titleBarElement = new ModalTitleBarElement(
                title,
                this,
                new TVVector(0, 0),
                new TVVector(size != null ? size.X : 400, 40));

            AddChildElement(titleBarElement);
            content = this.AddChildElement<TextElement>();

            content.Set<PositionTrait>(new TVVector(10, 50));
            content.Set<TextTrait>(new TVText(body));
            content.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            content.Set<ForegroundColorTrait>(new TVColor(BodyForeground));
            content.Set<SizeTrait>(new TVVector(size.X - 20, 0));
            content.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment() { Alignment = HorizontalAlignment.Left });

            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();
            }

            Setup();
        }

        public ModalWindowElement(string title, Element body, List<BasicButtonElement> buttons = null, TVVector position = null, TVVector size = null, bool fitToContent = true, bool resizable = false, bool closable = true, Vector2? minSize = null, bool pushHookScope = false, bool interactiveTitleBar = true)
        {
            // Store directly, not via the Title property setter — that
            // delegates to titleBarElement.SetTitle, and titleBarElement
            // isn't constructed yet at this point in either constructor.
            // ModalTitleBarElement's own constructor already receives
            // `title` and sets the initial text itself.
            this.title = title;
            if (minSize.HasValue)
            {
                MinSize = minSize.Value;
            }

            hasHookScope = pushHookScope;
            if (hasHookScope)
            {
                hookScopeToken = Resources.StaticResources.InputManager.PushHookScope();
            }

            AutoCenter = position == null;
            // Must be set before the title bar is built below (it reads
            // FitModalToContent to decide whether to show a maximize
            // button) and before Setup()/Update() ever size this modal off
            // its content. A dockable panel (fitToContent: false) instead
            // keeps whatever `size` the caller passed, unchanged by content
            // — DockTo/LayoutDocked below then only ever touch position and
            // height, never the authored width.
            FitModalToContent = fitToContent;

            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<ForegroundColorTrait>(new TVColor(BodyForeground));

            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(new TVVector(content != null ? content.GetSize().X + 20 : size.X, size.Y) ?? new TVVector(0, 0));

            Set<BorderFillTrait>(new TVBorder9Grid());


            if (buttons != null)
            {
                this.buttons.AddRange(buttons);
            }
            else
            {
                this.buttons = new List<BasicButtonElement>();
            }

            titleBarElement = new ModalTitleBarElement(
                title,
                this,
                new TVVector(0, 0),
                new TVVector(size != null ? size.X : 400, 40),
                closable,
                interactiveTitleBar);

            AddChildElement(titleBarElement);

            if (hasHookScope)
            {
                // Default so the title bar's own close X (when this
                // instance is ALSO closable) still pops the scope instead
                // of leaking it via a bare Kill() — see Close()'s own doc
                // comment. A caller setting OnCloseRequested afterward
                // (same override-friendly shape as FullScreenModalElement)
                // simply replaces this.
                OnCloseRequested = Close;
            }

            if (resizable)
            {
                resizeHandles = new ResizeHandlesElement(this);
                // Local sibling depth (Depth ordering is per-parent, not
                // global — TVElements.cs:40 sorts each element's OWN
                // children list) — must draw/hit-test above `content` below
                // regardless of add order, or a content element reaching
                // the modal's edge would cover the handles there.
                resizeHandles.Depth = 20;
                AddChildElement(resizeHandles);
            }

            this.content = body;
            tabs.Add(new Tab { Title = title, Content = body });

            this.AddChild(this.content, "content");
            content.Set<PositionTrait>(new TVVector(0, 0));

            // Whether this scrolls is re-decided every frame (see
            // RefreshScrollMode) — this first call just covers the common case
            // of content that is already too tall when it arrives.
            RefreshScrollMode();


            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();
            }

            Setup();
        }

        // Themed body/footer fill (was a near-white gradient — a bright
        // dialog box floating in an otherwise all-dark app, like an
        // unstyled browser confirm() box). Matches the app's own panel
        // language rather than the framework's old light-mode default.
        // Reads Theme LIVE (not cached) so a modal built after a theme
        // switch picks up the current palette — design-guide.md §1.
        private static Color BodyFillTop => Resources.StaticResources.Theme.SurfaceRaised;
        private static Color BodyFillBottom => Resources.StaticResources.Theme.SurfacePanel;
        private static Color FooterFillTop => Resources.StaticResources.Theme.SurfaceHeader;
        private static Color FooterFillBottom => Resources.StaticResources.Theme.SurfaceBackdrop;
        private static Color BodyForeground => Resources.StaticResources.Theme.BodyText;

        // ---- open/close transition (design-guide.md §5: subtle chrome
        // easing, ~100-150ms) — fade + a few px slide-up on the modal's own
        // chrome (body/title/footer backgrounds, border/shadow), reusing the
        // nine-grid border as the shadow: it already IS one (baked
        // low-opacity corner/edge textures, Theme.cs's NineGridOpacity),
        // previously only grown on drag. Content (buttons, text) doesn't
        // fade — no subtree-opacity primitive exists in GustUI to do that
        // cheaply, and a chrome-only fade already reads as a real transition.
        private readonly Stopwatch animClock = Stopwatch.StartNew();
        private double closeStartSeconds = -1;
        private bool closing;
        private TVVector restPosition;
        private bool restPositionInitialized;
        private float dragGrowSize = 16f;
        private const float TransitionSeconds = 0.15f;
        private const float SlideDistance = 10f;

        private static float EaseOutCubic(float t) => 1f - (float)Math.Pow(1f - t, 3);
        private static float EaseInCubic(float t) => t * t * t;

        /// <summary>Y below which a modal (or its content) may never sit —
        /// currently just whatever <see cref="Managers.DockLayout.TopInset"/>
        /// reserves (a top-docked panel, e.g. once the Stack uses it), 0 if
        /// nothing is docked there. Was a FruitMenuElement-height lookup
        /// before the 2026-08-17 per-modal menu rework removed the global
        /// fruit menu entirely — no window-level top chrome exists anymore,
        /// only per-dock reservations. internal, not private: ResizeHandlesElement
        /// needs this same value to clamp a resize against the same top
        /// boundary Update()'s own screen-edge clamp already uses.</summary>
        internal static float TopLimit()
        {
            return Managers.DockLayout.TopInset;
        }

        /// <summary>Whether <paramref name="host"/> is the frontmost window-
        /// like element currently open — the one with the highest
        /// <see cref="Element.FrontSequence"/> among every
        /// <see cref="ModalWindowElement"/> and <see cref="FullScreenModalElement"/>
        /// directly under the root window (2026-08-17, inactive-title-bar-
        /// desaturation feature). NOT keyed off <see cref="Element.Depth"/> —
        /// found live via the control API that Depth ties once two or more
        /// windows are both clamped to MoveToFront's own ceiling (see its
        /// doc comment), which happens almost immediately in normal use;
        /// FrontSequence has no ceiling to saturate against.
        /// <see cref="ModalTitleBarElement"/> calls this on its own parent
        /// every frame; <see cref="TabContainerElement"/> reuses it for its
        /// tab strip, since that IS a container's visible "title bar" once
        /// its own inert one is fully covered.</summary>
        internal static bool IsFrontmostWindow(Element host)
        {
            if (host?.Parent == null)
            {
                // Not parented under the root window (yet, or never) — e.g.
                // the very first frame after construction, before
                // AddChild/MoveToFront has run. Default to "active" rather
                // than flash desaturated for one frame.
                return true;
            }

            long maxSequence = long.MinValue;
            foreach (Element sibling in host.Parent.Children.Items)
            {
                if ((sibling is ModalWindowElement || sibling is FullScreenModalElement) && sibling.FrontSequence > maxSequence)
                {
                    maxSequence = sibling.FrontSequence;
                }
            }

            return host.FrontSequence >= maxSequence;
        }

        /// <summary>Same target rect FillsAvailableSpace already computes
        /// every frame — reused here so maximizing a window while something
        /// else is docked fills exactly the space docking leaves free,
        /// instead of the raw window (see Element.FullScreenTargetPosition's
        /// doc comment for the animation-fix context this was added
        /// alongside, 2026-08-17).</summary>
        protected override Vector2 FullScreenTargetPosition()
        {
            return Managers.DockLayout.AvailableRect(BottomInset, MinSize).Position;
        }

        protected override Vector2 FullScreenTargetSize()
        {
            return Managers.DockLayout.AvailableRect(BottomInset, MinSize).Size;
        }

        /// <summary>Tallest this modal may grow to and still leave
        /// BottomScreenMargin of clearance above the window's bottom edge —
        /// the cap Setup()/Update() enforce once content overflows it.
        /// Floored so a tiny/short window still gets a usable modal instead
        /// of a negative or near-zero one.</summary>
        private static float MaxModalHeight() =>
            Math.Max(200f, Resources.StaticResources.RootWindow.GetSize().Y - TopLimit() - BottomScreenMargin);

        /// <summary>Centered-in-window position (design-guide.md §9), Y
        /// floored at the fruit-menu bar like the pre-existing screen-clamp
        /// logic below — a modal taller than the window shouldn't compute a
        /// negative centered Y and fight that clamp every frame.</summary>
        private TVVector CenteredPosition()
        {
            TVVector windowSize = Resources.StaticResources.RootWindow.GetSize();
            TVVector modalSize = ElementTrait<SizeTrait>().Value();

            float y = Math.Max(TopLimit(), (windowSize.Y - modalSize.Y) / 2f);
            return new TVVector((windowSize.X - modalSize.X) / 2f, y);
        }

        /// <summary>Natural (uncapped) content height/width — CalculatedSize
        /// for a plain TextElement body (word-wrap aware), else the body's
        /// own live GetSize(). The shared read Setup()/Update() both used
        /// inline before the scroll-cap pass; factored out so both call
        /// sites and EffectiveContentHeight/ContentWidth agree.</summary>
        /// <summary>
        /// Promotes the body into a scroll viewport once it outgrows the
        /// screen. Called every frame, not once at construction.
        ///
        /// It used to be a one-time decision, on the reasoning that a modal is
        /// built fresh per open and its content does not change afterward. That
        /// is not true of content which ARRIVES: the welcome screen's pack
        /// templates appear when the pack sync finishes indexing, seconds after
        /// the modal was built, and cloud demos land whenever the service
        /// answers. Such a modal grew past the bottom of the window and could
        /// never gain a scrollbar, because the only moment that could have
        /// given it one had already passed.
        ///
        /// One direction only. Content that shrinks back keeps its viewport,
        /// which is harmless — a scroll element whose content fits has nothing
        /// to scroll and draws no rail — whereas unwrapping means reparenting
        /// the body a second time for no visible gain.
        /// </summary>
        private void RefreshScrollMode()
        {
            if (contentScrolls || content == null)
            {
                return;
            }

            float buttonHeight = this.buttons.Count > 0 ? 80 : ContentMargin;
            float naturalModalHeight = 40 + ContentMargin + NaturalContentHeight() + ContentMargin + buttonHeight;
            if (naturalModalHeight <= MaxModalHeight())
            {
                return;
            }

            contentScrolls = true;

            // Detach rather than Kill: this is the caller's body element, with
            // its own live subscriptions and state, being re-parented — not
            // discarded.
            Children.Remove(content);

            scrollViewport = new VerticalScrollElement();
            scrollViewport.Set<SizeTrait>(new TVVector(EffectiveContentWidth(), EffectiveContentHeight()));
            AddChildElement(scrollViewport);
            scrollViewport.AddChild(content, "content");
            content.Set<PositionTrait>(new TVVector(0, 0));
        }

        private float NaturalContentHeight() =>
            content is TextElement textElement ? textElement.CalculatedSize().Y : content.GetSize().Y;

        private float NaturalContentWidth() =>
            content is TextElement tx ? tx.CalculatedSize().X : content.GetSize().X;

        /// <summary>What Setup()/Update() should size the modal's content
        /// area to: the natural height when it already fits, or the capped
        /// height (MaxModalHeight() minus chrome) once <see cref="contentScrolls"/>
        /// is set — re-derived from the CURRENT window size every call, so
        /// a scrolling modal's visible cap still tracks a live window
        /// resize even though whether it scrolls at all was decided once,
        /// at construction.</summary>
        private float EffectiveContentHeight()
        {
            float natural = NaturalContentHeight();
            if (!contentScrolls)
            {
                return natural;
            }

            float buttonHeight = this.buttons.Count > 0 ? 80 : ContentMargin;
            float cappedContent = MaxModalHeight() - 40 - ContentMargin - ContentMargin - buttonHeight;

            // The cap is a CEILING, not a height. Returning it flat was
            // harmless while scroll mode was decided once (it only ever
            // engaged for content already taller than the cap, where the two
            // are the same number) and is not now that the decision is live:
            // a modal whose content grew past the cap and then shrank back
            // would stand at full screen height around a short body.
            return Math.Max(80f, Math.Min(natural, cappedContent));
        }

        /// <summary>Content width the MODAL should size itself to — the
        /// content's own natural width, plus room for the scrollbar rail
        /// when scrolling (so the rail sits clear of the content instead of
        /// overlapping its rightmost slice).</summary>
        private float EffectiveContentWidth()
        {
            float natural = NaturalContentWidth();
            return contentScrolls ? natural + ScrollbarAllowance : natural;
        }

        /// <summary>Intercepts EVERY removal path (the X button routes here
        /// via ModalTitleBarElement's default Parent.Kill(); a footer button
        /// calling Parent.Kill() directly through a plain Element reference
        /// reaches this too, since Element.Kill() is now virtual) — starts
        /// the close animation instead of removing immediately; Update()
        /// calls base.Kill() once it finishes.</summary>
        public override void Kill()
        {
            if (closing)
            {
                return;
            }

            // No-op if never registered — a plain, non-filling modal. Kept
            // unconditional (rather than gated on FillsAvailableSpace's own
            // current value) so a modal killed WHILE still filling doesn't
            // leave a stale entry in DockLayout's filler list forever,
            // permanently over-shrinking every future docked panel's own
            // budget for a window that no longer exists.
            Managers.DockLayout.UnregisterFiller(this);

            if (DockedSide != DockSide.None)
            {
                // The floating open/close slide+fade transition below is
                // driven from Update() — which, while docked, takes the
                // early-return LayoutDocked() path and never reaches that
                // logic at all (docked mode skips the ENTRANCE animation
                // for the same reason — see Update()'s own comment). Setting
                // closing=true here without a docked equivalent would just
                // leave the modal on screen forever with a permanently
                // inert close button: found 2026-08-16 the hard way,
                // clicking a docked Loop Browser's own X did nothing.
                // Docked panels close instantly instead.
                closing = true;
                Undock();
                base.Kill();
                return;
            }

            closing = true;
            closeStartSeconds = animClock.Elapsed.TotalSeconds;
            restPosition = ElementTrait<PositionTrait>().Value();
        }

        /// <summary>Minimum inset between a modal's chrome (title bar,
        /// edges, footer) and its content (design-guide.md §3 — modals
        /// carry at least this much margin; nested containers within that
        /// content are exempt, since the outer inset already separates them
        /// from the modal edge).</summary>
        private const float ContentMargin = 12f;

        private void Setup()
        {
            // Same fix as FullScreenModalElement/FruitMenuElement: a view
            // rebuild (Stage.Clear, ezmuze-studio) kills every non-chrome
            // window child, which used to include an open Preferences/About
            // dialog as collateral damage of an unrelated sequencer rebuild
            // (e.g. adding a channel while the dialog was open) — silently
            // closing it and abandoning any dropdown it had open. Explicit
            // closes (the Close button, Esc) still work: they call Kill()
            // directly, which IsChrome does not intercept — it only exempts
            // this element from the automated Stage.Clear sweep.
            IsChrome = true;

            Set<BackgroundFillTrait>(new TVFillSimpleGradient(BodyFillTop, BodyFillBottom, Direction.Vertically));
            var size = this.GetSize();
            Set<BorderFillTrait>(new TVBorder9Grid());
            restPosition = ElementTrait<PositionTrait>().Value();

            // Unconditional before this fix (2026-08-16) — every prior
            // caller happened to want content-fit sizing on first Setup()
            // even when they'd later override it (docked panels: DockTo()
            // runs immediately after construction and overwrites both
            // Position and Size before any of this is ever visible). The
            // sequencer's own resizable ModalWindowElement doesn't have an
            // immediate override like that — it passes its real fill-
            // available size straight to the constructor and expects Setup()
            // to leave it alone — so this now only recomputes size from
            // content when FitModalToContent is actually true; otherwise
            // `size` stays exactly what the constructor already set.
            if (FitModalToContent)
            {
                float contentHeight = EffectiveContentHeight();
                float contentWidth = EffectiveContentWidth();
                float buttonHeight = (this.buttons.Count > 0 ? 80 : ContentMargin);

                if (contentScrolls)
                {
                    scrollViewport.Set<SizeTrait>(new TVVector(contentWidth, contentHeight));
                }

                float calcHeight = 40 + ContentMargin + contentHeight + ContentMargin + buttonHeight;
                float calcWidth = contentWidth + ContentMargin * 2;
                size = new TVVector(calcWidth, calcHeight);
                Set<SizeTrait>(size);
            }

            titleBarElement.Set<SizeTrait>(new TVVector(size.X, 40));

            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement.Set<PositionTrait>(new TVVector(0, size.Y - 80));
                buttonBackgroundElement.Set<SizeTrait>(new TVVector(size.X, 80));
                // The footer strip had no Depth of its own, so it sat at 0
                // while its BUTTONS drew raised. Alone on screen that is
                // invisible; over ANOTHER modal it means the strip sinks below
                // the modal underneath while its own buttons stay above it, so
                // the lower modal's footer reads straight through this one's and
                // the two sets of buttons interleave. Matches the depth the
                // rest of this element's chrome already uses.
                buttonBackgroundElement.Depth = 30;
                buttonBackgroundElement.Set<BackgroundFillTrait>(new TVFillSimpleGradient(FooterFillTop, FooterFillBottom, Direction.Vertically));
                float xPos = size.X - 20;
                int i = 0;
                foreach (var button in this.buttons)
                {
                    i++;
                    button.Parent = buttonBackgroundElement;
                    buttonBackgroundElement.AddChild(button, $"Button {i}");
                    var buttonSize = button.GetSize().X;
                    button.Set<BorderSizeTrait>(new TVInt(0));
                    button.Set<PositionTrait>(new TVVector(xPos - button.GetSize().X, 40 - (button.GetSize().Y / 2f)));
                    Log.This($"Button {i} position: {button.GetActualPosition()} - {xPos}");
                    xPos -= buttonSize + 20;

                }
            }
            Vector2 calculatedModalSize = size.AsXna;
            Vector2 actualPosition = this.GetActualPosition().AsXna;
            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;

            justSpawned = true;
        }
        private bool justSpawned = false;
        private int maximizeAttemptCount = 0;

        /// <summary>Edge-detection state for the click-anywhere-brings-to-
        /// front check below — a plain field, not routed through
        /// Element.BeingDragged/previousMouseState (both private to the
        /// base class, and shaped around drag continuation specifically,
        /// not a general press edge).</summary>
        private ButtonState previousLeftButtonForFocus = ButtonState.Released;

        public override void Update(Element parent = null)
        {
            UpdateTabs();
            base.Update(parent);

            // Keeps the toolbar/menu wrap decision live across a resize —
            // chromeRow re-lays-out itself (and ContentTop/ChromeRowHeight
            // follow) once it sees a changed WrapWidth, same change-
            // detection HorizontalStackElement already uses for added/
            // removed children. menuSpacer/toolbarSlot's own widths also
            // need to stay in sync in case the menu/toolbar's real content
            // changes after EnsureToolbar already ran. Finally, the REAL
            // toolbar (not itself a chromeRow child — see toolbarSlot's own
            // doc comment) gets repositioned to wherever its sentinel
            // landed this pass — one frame behind chromeRow's own layout,
            // same tolerance every other width-tracking bar in this file
            // already has, imperceptible at any real frame rate.
            if (chromeRow != null)
            {
                chromeRow.WrapWidth = this.GetSize().X;
                if (menuSpacer != null)
                {
                    menuSpacer.Set<SizeTrait>(new TVVector(menuBar.ContentWidth, MenuBarElement.BarHeight));
                }

                toolbarSlot.Set<SizeTrait>(new TVVector(toolbar.ContentWidth, ToolbarElement.BarHeight));
                TVVector slotPosition = toolbarSlot.ElementTrait<PositionTrait>().Value();
                toolbar.Set<PositionTrait>(new TVVector(slotPosition.X, ModalTitleBarElement.BarHeight + slotPosition.Y));
            }

            if (chromeRowBg != null)
            {
                chromeRowBg.Set<SizeTrait>(new TVVector(this.GetSize().X, ChromeRowHeight));
            }

            // Click-anywhere-brings-to-front (2026-08-17, user report: only
            // the title bar's own drag-press called MoveToFront — clicking
            // the BODY of a background window, e.g. a loop entry in the
            // loop browser, never raised it, unlike every real desktop
            // convention). Unconditional per-frame press-edge poll (the
            // same "poll CurrentMouseState directly" idiom UpdateDockHoldGesture/
            // ResizeHandlesElement already use), not OnMousePress — that
            // trait only fires on whichever SPECIFIC child is topmost at
            // the click point, and this needs to fire for a press ANYWHERE
            // within this modal's own bounds regardless of which child (or
            // no child at all) actually consumes it. Runs unconditionally,
            // before the docked/floating branches below, so a click
            // anywhere in a DOCKED panel also counts.
            MouseState focusMouseState = Resources.StaticResources.InputManager.CurrentMouseState;
            if (focusMouseState.LeftButton == ButtonState.Pressed
                && previousLeftButtonForFocus == ButtonState.Released
                && IsMouseOver())
            {
                MoveToFront();
            }

            previousLeftButtonForFocus = focusMouseState.LeftButton;

            if (pendingMergeTarget != null)
            {
                ModalWindowElement target = pendingMergeTarget;
                pendingMergeTarget = null;
                target.AddTab(this);
                return;
            }

            // Docked is its own simple layout mode (mirrors
            // FullScreenModalElement.Layout()'s own separateness) rather
            // than another branch woven through the floating-modal logic
            // below (content-fit sizing, screen clamp, auto-center, the
            // open/close slide/fade animation) — none of that applies to a
            // panel pinned full-height to a screen edge. base.Update(parent)
            // above already walked the title bar/content children, so they
            // still get their own per-frame Update() either way.
            if (DockedSide != DockSide.None)
            {
                // A press on a still-docked title bar (HandleTitleBarPress)
                // no longer undocks immediately — it only records where the
                // press happened and starts the drag machinery (BeingDragged/
                // pointer capture) as normal. Stay visually pinned to the
                // dock rect, exactly as before, until the mouse has actually
                // moved past UndockDragThresholdPixels; only THEN escalate to
                // a real Undock() and fall through to the floating-modal
                // logic below THIS SAME frame, so the drag picks the modal up
                // with no extra stale frame at the (now wrong) dock rect.
                if (BeingDragged && dockPressMouse.HasValue)
                {
                    MouseState mouse = Resources.StaticResources.InputManager.CurrentMouseState;
                    Vector2 mousePos = new Vector2(mouse.X, mouse.Y);
                    if (Vector2.Distance(mousePos, dockPressMouse.Value) >= UndockDragThresholdPixels)
                    {
                        dockPressMouse = null;
                        Undock();
                    }
                    else
                    {
                        LayoutDocked();
                        return;
                    }
                }
                else
                {
                    LayoutDocked();
                    return;
                }
            }

            if (FillsAvailableSpace)
            {
                if (BeingDragged)
                {
                    // Dragged by the title bar — the user has picked this
                    // window up, so it is genuinely floating now. Same rule
                    // AutoCenter already follows.
                    FillsAvailableSpace = false;
                }
                else
                {
                    // A RESIZE is not the same as being dragged away. On a side
                    // with a dock, this window's edge and that dock's edge are
                    // the same line, so dragging it means "move the boundary" —
                    // absorb it into the dock's reservation and keep filling.
                    // Opting out of filling instead left the window shrunk with
                    // a hole between it and the dock, which stayed put.
                    if (IsResizing)
                    {
                        AbsorbResizeIntoDocks();
                    }

                    var fill = Managers.DockLayout.AvailableRect(BottomInset, MinSize);
                    Set<PositionTrait>(new TVVector(fill.Position));
                    Set<SizeTrait>(new TVVector(fill.Size));
                }
            }

            // Before the sizing below, so a body that grew this frame is
            // already wrapped when its height is turned into the modal's.
            RefreshScrollMode();

            if (FitModalToContent)
            {
                float contentHeight = EffectiveContentHeight();
                float contentWidth = EffectiveContentWidth();
                float buttonHeight = (this.buttons.Count > 0 ? 80 : ContentMargin);

                this.Set<SizeTrait>(new TVVector(
                    contentWidth + ContentMargin * 2,
                    40 + ContentMargin + contentHeight + ContentMargin + buttonHeight));
            }

            if (contentScrolls)
            {
                // A non-fit-to-content modal (Loop Browser, Wave Bank — both
                // resizable/dockable/rehostable) keeps whatever size the
                // caller/DockLayout/TabContainer gives it; the viewport must
                // track THAT live size every frame, not the screen-wide cap
                // EffectiveContentHeight()/Width() compute below for the
                // FitModalToContent case above. Found 2026-08-17: the
                // viewport was only ever sized once, in the constructor,
                // against MaxModalHeight() — so a docked/torn-off window far
                // smaller than the screen still reported a ~1261px-tall
                // visible area, desyncing the scrollbar thumb/travel from
                // what was actually on screen.
                float ownButtonHeight = this.buttons.Count > 0 ? 80 : ContentMargin;
                float viewportWidth = FitModalToContent ? EffectiveContentWidth() : this.GetSize().X - ContentMargin * 2;
                float viewportHeight = FitModalToContent
                    ? EffectiveContentHeight()
                    : Math.Max(80f, this.GetSize().Y - ContentTop - ContentMargin - ownButtonHeight);
                scrollViewport.Set<SizeTrait>(new TVVector(viewportWidth, viewportHeight));
            }

            var size = this.GetSize();

            Vector2 calculatedModalSize = size.AsXna;
            Vector2 actualPosition = this.GetActualPosition().AsXna;
            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            float opacity = BeingDragged ? 0.15f : 0.35f;

            // Keep a dragged window fully on-screen (2026-08-16 — reactivated
            // from a previously dead limitToScreenSpace-gated block of the
            // same code; that flag was hardcoded false and this never ran).
            // This is what makes "the mouse goes outside the screen" a
            // non-issue for the new dock-hold gesture below: the modal's own
            // rendered position never flies off-canvas, and OS mouse
            // coordinates that go negative/beyond the window while a button
            // is held (desktop's implicit capture) naturally saturate the
            // edge-zone comparisons in UpdateDockHoldGesture the same way.
            if (actualPosition.X + calculatedModalSize.X > windowSize.X)
            {
                Set<PositionTrait>(new TVVector(windowSize.X - calculatedModalSize.X, ElementTrait<PositionTrait>().Value().Y));
            }

            if (actualPosition.Y + calculatedModalSize.Y > windowSize.Y)
            {
                Set<PositionTrait>(new TVVector(ElementTrait<PositionTrait>().Value().X, windowSize.Y - calculatedModalSize.Y));
            }

            if (ElementTrait<PositionTrait>().Value().X < 0)
            {
                Set<PositionTrait>(new TVVector(0, ElementTrait<PositionTrait>().Value().Y));
            }

            // Drag-to-top-edge, hold, auto-maximize — unchanged from before
            // the clamp above was merged in; that clamp only touches X/the
            // bottom edge, this still owns Y/top exclusively. The Y-clamp
            // itself stays unconditional (also keeps a north-edge/corner
            // RESIZE from pushing the top past the menu bar), but the
            // escalation to an actual ToggleFullScreen() is now gated to
            // BeingDragged (title-bar move) specifically — 2026-08-17,
            // found from the user's own test: resizing fast enough to push
            // the top above TopLimit for several consecutive frames was
            // incrementing the exact same counter a title-bar drag does,
            // auto-maximizing the window mid-resize. Resize and title-bar
            // drag are mutually exclusive gestures (ResizeHandlesElement
            // never touches BeingDragged), so this cleanly separates them
            // without needing resize to know anything about this heuristic.
            if (!isFullScreen)
            {
                if (actualPosition.Y < TopLimit())
                {
                    Set<PositionTrait>(new TVVector(ElementTrait<PositionTrait>().Value().X, TopLimit()));
                    if (BeingDragged)
                    {
                        maximizeAttemptCount++;
                        if (maximizeAttemptCount > 10)
                        {
                            ToggleFullScreen();
                            maximizeAttemptCount = 0;
                        }
                    }
                }
                else
                {
                    maximizeAttemptCount = 0;
                }
            }

            if (justSpawned)
            {
                this.MoveToFront();
                justSpawned = false;
            }

            // So is a RESIZE, and for the same reason it matters more.
            // Auto-centering recomputes position FROM size every frame, so
            // pulling an edge moved the modal out from under the cursor: the
            // window appeared to grow from its middle rather than from the edge
            // being dragged, and the edge never went where it was put. Moving
            // the modal first made resizing behave, which is the tell —
            // dragging the title bar already ended auto-centering, and pulling
            // an edge is the same act of taking manual control.
            //
            // The modal's OWN handles only. IsResizing also covers the dock
            // splitter, and a docked panel is placed by the dock rather than
            // centred, so a boundary drag there is not the user overriding
            // anything.
            if (resizeHandles != null && resizeHandles.IsActive)
            {
                AutoCenter = false;
            }

            // A drag is the user taking manual control — auto-centering
            // never re-engages for this instance after that (design-guide.md
            // §9), even once they let go.
            if (BeingDragged)
            {
                AutoCenter = false;
                UpdateDockHoldGesture(windowSize);
                if (Tabable)
                {
                    UpdateTabMergeGesture();
                }
            }
            else
            {
                ResetDockHoldGesture();
                ResetTabMergeGesture();
            }

            bool autoCenterActive = AutoCenter && !isFullScreen && !BeingDragged;

            // ---- open/close transition (design-guide.md §5) ----
            // First real frame: capture restPosition from whatever the
            // clamp-to-screen-space logic ABOVE just settled on (already ran
            // this same Update() call), not the raw constructor position —
            // a modal whose intended Y sits above the fruit-menu limit (e.g.
            // PreferencesModal's centered-but-tall math) gets clamped there
            // every frame; sliding from the pre-clamp value would fight that
            // clamp for the whole transition and trip the maximizeAttemptCount
            // heuristic above into wrongly full-screening the modal. When
            // AutoCenter is on, skip straight to the real centered position
            // instead, so the very first frame opens already centered.
            if (!restPositionInitialized)
            {
                restPosition = autoCenterActive ? CenteredPosition() : ElementTrait<PositionTrait>().Value();
                restPositionInitialized = true;
            }

            double animElapsed = animClock.Elapsed.TotalSeconds;
            float animProgress;
            if (closing)
            {
                float t = (float)Math.Min(1.0, (animElapsed - closeStartSeconds) / TransitionSeconds);
                animProgress = 1f - EaseInCubic(t);
                if (t >= 1f)
                {
                    base.Kill();
                    return;
                }
            }
            else
            {
                float t = (float)Math.Min(1.0, animElapsed / TransitionSeconds);
                animProgress = EaseOutCubic(t);
                if (t >= 1f)
                {
                    if (autoCenterActive)
                    {
                        // Steady-state, still auto-centered: recompute EVERY
                        // frame (design-guide.md §9 — "if the screen resizes,
                        // that should move the modal to correct it") and
                        // apply directly; the slide-offset block below only
                        // fires mid-transition, so a resize correction needs
                        // its own explicit Set here.
                        restPosition = CenteredPosition();
                        Set<PositionTrait>(restPosition);
                    }
                    else
                    {
                        // Steady-state, not auto-centering (dragged, explicit
                        // position, or full-screen): keep tracking the real
                        // position so a later close-slide starts from
                        // wherever the modal actually is, not spawn.
                        restPosition = ElementTrait<PositionTrait>().Value();
                    }
                }
            }

            if (closing || animProgress < 1f)
            {
                Set<PositionTrait>(new TVVector(restPosition.X, restPosition.Y + (1f - animProgress) * SlideDistance));
            }

            // The open animation and the caller's hold are independent reasons
            // to be invisible, so they multiply rather than one overriding the
            // other.
            float shown = animProgress * chromeReveal;

            // Title bar and footer are fully faded, not just their fills: their
            // TEXT and BUTTONS are children, and a title reading "ezmuze studio"
            // over an invisible bar is exactly the artefact this is here to
            // avoid.
            titleBarElement.Opacity = shown;
            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement.Opacity = shown;
            }

            if (contentScrolls)
            {
                // The rail is chrome; what it scrolls is not.
                scrollViewport.ScrollbarOpacity = shown;
            }

            if (ElementTrait<BackgroundFillTrait>().Value() is TVFill bodyFill)
            {
                bodyFill.Opacity = shown;
            }

            if (titleBarElement.ElementTrait<BackgroundFillTrait>().Value() is TVFill titleFill)
            {
                titleFill.Opacity = animProgress;
            }

            if (this.buttons.Count > 0 && buttonBackgroundElement.ElementTrait<BackgroundFillTrait>().Value() is TVFill footerFill)
            {
                footerFill.Opacity = animProgress;
            }

            if (ElementTrait<BorderFillTrait>().Value() is TVBorder9Grid nineGrid)
            {
                // The nine-grid border already IS the modal's drop shadow
                // (baked low-opacity corner/edge textures) — grown on drag
                // as before, now ALSO scaled by open/close progress so the
                // shadow visibly deepens as the modal slides into place.
                float dragTarget = BeingDragged ? 32f : 16f;
                dragGrowSize = MathHelper.Lerp(dragGrowSize, dragTarget, 0.2f);
                nineGrid.NineGridSize = dragGrowSize * shown;
                nineGrid.Opacity = 0.5f * shown;
            }

            // buttonBackgroundElement only exists when this.buttons.Count >
            // 0 (Setup()'s own construction is already gated the same way;
            // this block wasn't — a latent bug that never surfaced before
            // because every prior ModalWindowElement caller in this codebase
            // happened to always pass at least one button, until the
            // sequencer's own resizable-but-button-less modal hit it,
            // 2026-08-16, NullReferenceException on the very first frame).
            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement.Set<PositionTrait>(new TVVector(0, size.Y - 80));
                buttonBackgroundElement.Set<SizeTrait>(new TVVector(size.X, 80));
                float xPos = size.X - 20;
                foreach (var button in buttonBackgroundElement.Children.Items)
                {
                    button.Set<PositionTrait>(new TVVector(xPos - button.GetSize().X, 40 - (button.GetSize().Y / 2f)));
                    xPos -= button.GetSize().X + 20;
                }
            }

            //content.Set<PositionTrait>(new TVVector((size.X / 2f) - (content.GetSize().X / 2f), (size.Y / 2f) - (content.GetSize().Y / 2f)));
            // X centering falls out of the size calc above already including
            // ContentMargin on both sides (design-guide.md §3): size.X is
            // contentWidth + 2*margin, so (size.X/2) - (contentWidth/2)
            // resolves to exactly margin either side. Positions scrollViewport
            // instead of content directly once contentScrolls — content
            // itself then just sits at (0,0) inside it (set once, at wrap
            // time in the constructor) while the viewport is what's actually
            // placed/clipped/sized within the modal's chrome.
            Element positioned = contentScrolls ? (Element)scrollViewport : content;
            if (!FitModalToContent && !contentScrolls)
            {
                // A fill-available modal's content host is a nominal-size
                // (often 10px) transparent rect the panel reflows internally
                // (LoopBrowserPanel/WaveBankPanel/StackPanelView) — its
                // authored SizeTrait never grows, and hit-testing culls a
                // subtree against its parent's rect (InputManager.
                // CollectHovered), so everything below the nominal height
                // was silently unclickable (found 2026-08-21: no loop-
                // browser row responded to clicks, docked or floating).
                // Stretch the host to the real client area, same geometry
                // LayoutDocked assigns, so docked and floating agree.
                positioned.Set<PositionTrait>(new TVVector(0, ContentTop));
                positioned.Set<SizeTrait>(new TVVector(size.X, Math.Max(0f, size.Y - ContentTop)));
            }
            else
            {
                positioned.Set<PositionTrait>(new TVVector((size.X / 2f) - (positioned.GetSize().X / 2f), ContentTop + ContentMargin));
            }
        }

        // ---- docked modal (2026-08-16) ----

        /// <summary>Docks this modal full-height to <paramref name="side"/>
        /// (no-op if already docked there). Only position/height change —
        /// width stays whatever it already was, whether authored at
        /// construction or last set while floating.</summary>
        public void DockTo(DockSide side)
        {
            if (side == DockSide.None)
            {
                Undock();
                return;
            }

            if (DockedSide == side)
            {
                return;
            }

            // Docking (not just dragging) onto an already-occupied side
            // merges with the existing tabable occupant instead of silently
            // stacking beside it (2026-08-17, user request: "if you open a
            // window like loop browser and it STARTS docked, and there's
            // already a docked window in that location... it should behave
            // as if it was just dragged on there"). Safe to merge (and
            // therefore Kill this instance) synchronously right here,
            // unlike the drag gesture's own merge commit — DockTo is never
            // reached for an occupied+tabable side from inside input
            // dispatch (UpdateDockHoldGesture already routes that case to
            // the deferred tabMergeTarget/pendingMergeTarget path instead;
            // every other DockTo call site is construction-time, not a
            // click handler).
            if (Tabable)
            {
                ModalWindowElement occupant = TabableOccupant(side);
                if (occupant != null)
                {
                    occupant.AddTab(this);
                    return;
                }
            }

            DockedSide = side;
            // A panel that docks immediately on open (DockTo called the
            // same frame it's constructed, before Update() ever runs) would
            // otherwise never reach Update()'s own justSpawned->MoveToFront()
            // line below — that line sits AFTER the docked-mode early
            // return, so it's simply unreachable once DockedSide is already
            // set by the time Update() first runs. Found 2026-08-16 via a
            // live screenshot: the panel existed at the right tree bounds
            // but rendered fully behind the sequencer's own modal (both
            // default to Depth 0 at construction; the sequencer's modal is
            // already at 60,000 by the time this one spawns).
            MoveToFront();
            Managers.DockLayout.Register(this, side);

            if (dockSplitter == null)
            {
                dockSplitter = new DockSplitterElement(this)
                {
                    // Above the title bar, which sits on exactly the same line
                    // for a Top/Bottom dock. Whoever draws and hit-tests last
                    // wins the overlap, and for this one line it must be the
                    // splitter — the rest of the title bar still tears the
                    // window out of the dock.
                    Depth = 30,
                };

                AddChildElement(dockSplitter);
            }

            LayoutDocked();
        }

        /// <summary>Detaches from the dock, leaving this modal exactly where
        /// it's currently rendered (its last docked rect) so the following
        /// floating-drag Update() picks it up with zero jump — the "pulled
        /// off the dock" feel. Idempotent.</summary>
        public void Undock()
        {
            if (DockedSide == DockSide.None)
            {
                return;
            }

            DockedSide = DockSide.None;
            Managers.DockLayout.Unregister(this);

            dockSplitter?.Kill();
            dockSplitter = null;
        }

        /// <summary>Full-height layout for the docked state — the docked
        /// counterpart to FullScreenModalElement.Layout(), same source
        /// values (TopLimit() for the fruit menu, BottomInset for the app's
        /// own bottom chrome) but X comes from DockLayout's stacking order
        /// instead of always 0.</summary>
        /// <summary>Full-height layout for a Left/Right dock: spans between
        /// TopLimit()/BottomInset vertically, stacks outward from its own
        /// screen edge (other same-side panels first) horizontally. A
        /// Top/Bottom dock (2026-08-17, the Stack's own) is the transposed
        /// mirror: spans between DockLayout's own Left/RightInset
        /// horizontally, stacks outward from its edge vertically. Either
        /// way the docked window's own width/height (Left/Right: width,
        /// Top/Bottom: height) is exactly what <see cref="Managers.DockLayout"/>
        /// reserves from windows sharing space with it — see its own doc
        /// comment (content-sized, capped at 50%, live).</summary>
        /// <summary>
        /// Turns this filler's edge-drag into the adjacent dock's reservation,
        /// on whichever sides actually have a dock.
        ///
        /// The two are the same line, so there is one quantity to change and
        /// this is how the filler's handle reaches it. Sides with no dock are
        /// left alone — there the window really is just being resized, and the
        /// snap-back to AvailableRect immediately below undoes it, which is the
        /// pre-existing behaviour for a fills-available window.
        /// </summary>
        private void AbsorbResizeIntoDocks()
        {
            (Vector2 position, Vector2 size) = Managers.DockLayout.AvailableRect(BottomInset, MinSize);
            Vector2 mine = this.GetActualPosition().AsXna;
            Vector2 mySize = this.GetSize().AsXna;

            Nudge(DockSide.Bottom, (position.Y + size.Y) - (mine.Y + mySize.Y));
            Nudge(DockSide.Right, (position.X + size.X) - (mine.X + mySize.X));
            Nudge(DockSide.Top, mine.Y - position.Y);
            Nudge(DockSide.Left, mine.X - position.X);
        }

        /// <summary>Grows the innermost dock on <paramref name="side"/> by
        /// <paramref name="delta"/> pixels — the space this window just gave
        /// up on that edge.</summary>
        private static void Nudge(DockSide side, float delta)
        {
            if (Math.Abs(delta) < 0.5f)
            {
                return;
            }

            ModalWindowElement dock = Managers.DockLayout.Innermost(side);
            if (dock == null)
            {
                return;
            }

            Managers.DockLayout.SetReservation(
                dock, Math.Max(48f, Managers.DockLayout.ReservedFor(dock, side) + delta));
        }

        private void LayoutDocked()
        {
            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            float stackOffset = Managers.DockLayout.StackOffset(this, DockedSide);

            if (DockedSide == DockSide.Left || DockedSide == DockSide.Right)
            {
                float top = TopLimit();
                float height = Math.Max(0f, windowSize.Y - top - Managers.DockLayout.BottomInset - BottomInset);
                float width = Managers.DockLayout.EffectiveSize(this, DockedSide);
                float x = DockedSide == DockSide.Left ? stackOffset : windowSize.X - width - stackOffset;

                Set<PositionTrait>(new TVVector(x, top));
                Set<SizeTrait>(new TVVector(width, height));
            }
            else
            {
                float leftInset = Managers.DockLayout.LeftInset;
                float width = Math.Max(0f, windowSize.X - leftInset - Managers.DockLayout.RightInset);
                float height = Managers.DockLayout.EffectiveSize(this, DockedSide);
                // A TOP dock measures from the top of the window plus whatever
                // is stacked above it — NOT from TopLimit(), which is
                // DockLayout.TopInset and therefore includes this very panel's
                // own reservation. That circularity put a top-docked panel
                // below the space it was itself reserving, landing it on top of
                // the window it was supposed to be making room beside.
                // StackOffset is the right quantity because it excludes self by
                // construction.
                float y = DockedSide == DockSide.Top
                    ? stackOffset
                    : windowSize.Y - BottomInset - height - stackOffset;

                Set<PositionTrait>(new TVVector(leftInset, y));
                Set<SizeTrait>(new TVVector(width, height));
            }

            // Docked content always spans the full authored width/height
            // flush under the title bar (and menu strip, if this modal has
            // one via SetMenu) — no natural-size centering, that's a
            // floating-modal concept that doesn't apply once
            // FitModalToContent is false. The content host's own per-frame
            // layout (each caller's own Update-driven reflow) is
            // responsible for sizing its internal children to this modal's
            // current GetSize(). The host's own SizeTrait must still track
            // the client area, though — hit-testing culls a subtree against
            // its parent's rect, so a nominal-size host makes its children
            // unclickable (2026-08-21, see the matching floating-path fix).
            TVVector dockedSize = this.GetSize();
            content.Set<PositionTrait>(new TVVector(0, ContentTop));
            content.Set<SizeTrait>(new TVVector(dockedSize.X, Math.Max(0f, dockedSize.Y - ContentTop)));

            dockSplitter?.LayoutFor(dockedSize.AsXna);
        }

        /// <summary>Hold-to-dock gesture, modeled directly on the top-edge
        /// auto-maximize heuristic above but time- rather than frame-count-
        /// based (Stopwatch, same idiom as SequencerView's own double-click
        /// timing) since the app's frame rate varies wildly (seen anywhere
        /// from ~24fps to 140+fps this session) and a frame count would fire
        /// at very different real-world speeds. Only runs while BeingDragged
        /// (called from Update()'s existing drag branch).</summary>
        private void UpdateDockHoldGesture(Vector2 windowSize)
        {
            MouseState mouse = Resources.StaticResources.InputManager.CurrentMouseState;
            DockSide zone = DockZoneFor(mouse, windowSize);

            // A tabable window held near an edge that already has a tabable
            // occupant merges with it instead of taking a second slot in
            // the stack (2026-08-17, user request — "behave like you
            // dropped it onto the window title") — UpdateTabMergeGesture,
            // below, is what actually offers that merge; suppress the
            // plain dock-to-edge hold/preview for this zone so the two
            // previews never fight over the same drop.
            if (zone != DockSide.None && Tabable && TabableOccupant(zone) != null)
            {
                zone = DockSide.None;
            }

            if (zone != dockHoldZone)
            {
                // Entering a new zone (including leaving one, zone == None)
                // resets the hold — no partial credit for hopping edges, and
                // leaving the zone before the threshold cancels the preview
                // immediately (no hold delay needed to cancel, matches the
                // feature's own "drag back toward the middle to cancel"
                // requirement).
                dockHoldZone = zone;
                dockHoldStartSeconds = animClock.Elapsed.TotalSeconds;
                if (pendingDockSide != DockSide.None)
                {
                    pendingDockSide = DockSide.None;
                    DockPreviewOverlay.Hide();
                }
            }
            else if (zone != DockSide.None && pendingDockSide == DockSide.None)
            {
                double held = animClock.Elapsed.TotalSeconds - dockHoldStartSeconds;
                if (held >= DockHoldThresholdSeconds)
                {
                    pendingDockSide = zone;
                    TVVector size = ElementTrait<SizeTrait>().Value();
                    bool horizontal = zone == DockSide.Left || zone == DockSide.Right;
                    DockPreviewOverlay.Show(zone, horizontal ? size.X : size.Y, BottomInset);
                }
            }
        }

        /// <summary>Which screen-edge zone (if any) <paramref name="mouse"/>
        /// currently sits within — shared by UpdateDockHoldGesture and
        /// UpdateTabMergeGesture's own occupied-edge check so both agree on
        /// exactly the same boundary.</summary>
        private static DockSide DockZoneFor(MouseState mouse, Vector2 windowSize)
        {
            if (mouse.X <= DockEdgeZonePixels)
            {
                return DockSide.Left;
            }

            if (mouse.X >= windowSize.X - DockEdgeZonePixels)
            {
                return DockSide.Right;
            }

            if (mouse.Y <= DockEdgeZonePixels)
            {
                return DockSide.Top;
            }

            if (mouse.Y >= windowSize.Y - DockEdgeZonePixels)
            {
                return DockSide.Bottom;
            }

            return DockSide.None;
        }

        /// <summary>The first OTHER tabable window currently docked to
        /// <paramref name="side"/>, or null — see UpdateDockHoldGesture's
        /// own doc comment for why this matters.</summary>
        private ModalWindowElement TabableOccupant(DockSide side)
        {
            foreach (ModalWindowElement occupant in Managers.DockLayout.DockedTo(side))
            {
                if (occupant != this && occupant.Tabable)
                {
                    return occupant;
                }
            }

            return null;
        }

        /// <summary>Called every non-dragging frame — clears any in-progress
        /// hold/preview state left over from a drag that just ended (via
        /// HandleTitleBarRelease, which reads pendingDockSide BEFORE this
        /// runs, so a completed dock/undock decision is never lost here).</summary>
        private void ResetDockHoldGesture()
        {
            if (dockHoldZone != DockSide.None || pendingDockSide != DockSide.None)
            {
                dockHoldZone = DockSide.None;
                pendingDockSide = DockSide.None;
                DockPreviewOverlay.Hide();
            }
        }

        // ---- tabable-modal merge (2026-08-17) ----

        private ModalWindowElement tabMergeTarget;

        /// <summary>Set by HandleTitleBarRelease, consumed at the very top
        /// of the next Update() — see its own doc comment for why the
        /// actual merge (which ends with this instance's Kill()) can't run
        /// synchronously from inside the release handler.</summary>
        private ModalWindowElement pendingMergeTarget;

        /// <summary>Tabable-drag counterpart to <see cref="UpdateDockHoldGesture"/>
        /// — no hold delay (unlike the screen-edge dock zone, hovering a
        /// SPECIFIC other tabable window's title bar is already a
        /// deliberate, narrow target, not something to trigger by
        /// accidentally passing through): shows the merge preview the
        /// instant the cursor is over one, drops it the instant it
        /// isn't. Only ever called while <see cref="Tabable"/>.
        ///
        /// Two ways to find a target (2026-08-17, second one added per user
        /// request): hovering directly over another tabable window's own
        /// title bar (the original gesture), OR holding near a screen edge
        /// that already has a tabable occupant docked there — "acts like
        /// you dropped it onto the window title" even though the cursor is
        /// out at the screen edge, not literally over that window's bar.</summary>
        private void UpdateTabMergeGesture()
        {
            MouseState mouse = Resources.StaticResources.InputManager.CurrentMouseState;
            Vector2 mousePos = new Vector2(mouse.X, mouse.Y);
            ModalWindowElement found = null;

            foreach (Element sibling in Resources.StaticResources.RootWindow.Children.Items)
            {
                if (sibling is ModalWindowElement other && other != this && other.Tabable)
                {
                    Vector2 pos = other.GetActualXnaPosition();
                    TVVector otherSize = other.GetSize();
                    if (mousePos.X >= pos.X && mousePos.X <= pos.X + otherSize.X
                        && mousePos.Y >= pos.Y && mousePos.Y <= pos.Y + ModalTitleBarElement.BarHeight)
                    {
                        found = other;
                        break;
                    }
                }
            }

            if (found == null)
            {
                Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
                DockSide edgeZone = DockZoneFor(mouse, windowSize);
                if (edgeZone != DockSide.None)
                {
                    found = TabableOccupant(edgeZone);
                }
            }

            if (found == tabMergeTarget)
            {
                return;
            }

            tabMergeTarget = found;
            if (found != null)
            {
                Vector2 pos = found.GetActualXnaPosition();
                TVVector otherSize = found.GetSize();
                TabMergePreviewOverlay.Show(pos, new Vector2(otherSize.X, ModalTitleBarElement.BarHeight));
            }
            else
            {
                TabMergePreviewOverlay.Hide();
            }
        }

        private void ResetTabMergeGesture()
        {
            if (tabMergeTarget != null)
            {
                tabMergeTarget = null;
                TabMergePreviewOverlay.Hide();
            }
        }

        /// <summary>Routed here (not straight to handleStartDrag) from
        /// ModalTitleBarElement's drag bar. For a DOCKED panel this no
        /// longer detaches immediately (see <see cref="UndockDragThresholdPixels"/>'s
        /// doc comment) — it just remembers where the press happened so
        /// Update()'s docked branch can tell a real drag from a plain
        /// click. Once that threshold is crossed, Undock() leaves position/
        /// size exactly where they were, so the floating-drag delta-move
        /// picks the modal up with no visible jump.</summary>
        internal void HandleTitleBarPress(TVEventArgs x)
        {
            if (DockedSide != DockSide.None)
            {
                MouseState mouse = Resources.StaticResources.InputManager.CurrentMouseState;
                dockPressMouse = new Vector2(mouse.X, mouse.Y);
            }

            handleStartDrag(x);
        }

        /// <summary>Routed here (not straight to handleStopDrag) so a
        /// release while the dock-hold preview is showing commits the dock
        /// instead of leaving the modal floating at the raw drop point.</summary>
        internal void HandleTitleBarRelease(TVEventArgs x)
        {
            handleStopDrag(x);
            dockPressMouse = null;

            if (tabMergeTarget != null)
            {
                // Deferred to next frame's Update() (see pendingMergeTarget's
                // own doc comment) — not a direct TabContainerElement.Merge()
                // call here: that ends with this instance's own Kill(),
                // and this method is itself running from INSIDE this
                // instance's own title-bar drag-bar click dispatch
                // (InputManager.ProcessMouseState). Same "never tear down
                // the element whose click handler is still executing"
                // hazard already fixed twice this session (the sequencer's
                // own close button, AboutModal's buttons) — killing this
                // instance's element tree synchronously here would NRE
                // mid-dispatch the same way.
                pendingMergeTarget = tabMergeTarget;
                tabMergeTarget = null;
                TabMergePreviewOverlay.Hide();
                return;
            }

            if (pendingDockSide != DockSide.None)
            {
                DockTo(pendingDockSide);
            }

            ResetDockHoldGesture();
        }

    }
}
