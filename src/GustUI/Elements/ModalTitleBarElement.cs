using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    /// <summary>
    /// The SHARED window-chrome title bar: a flat, neutral-dark bar carrying
    /// the title text plus a close button (and, for resizable
    /// <see cref="ModalWindowElement"/> hosts, a maximize button) that blend
    /// into the bar until hovered — the modern-app idiom (VS Code, Windows
    /// 11) in place of the permanently-lit primary-color buttons this used
    /// to have. Historically welded to ModalWindowElement; now any element
    /// can host it (e.g. <see cref="FullScreenModalElement"/>) — drag/
    /// maximize behaviors only attach when the parent actually is a
    /// ModalWindowElement, and the X routes through
    /// <see cref="OnCloseRequested"/> (default: kill the parent, the
    /// classic modal-window behavior).
    /// </summary>
    [ElementTraits(typeof(FontTrait), typeof(OnExitTrait))]
    public class ModalTitleBarElement : FilledRectangleElement
    {
        /// <summary>The chrome's fixed height (Update re-asserts it).</summary>
        /// <summary>
        /// Title bar height. Everything on the bar derives from it — the close
        /// and maximise squares, the drag bar, the accent underline — so this
        /// one number sizes the whole thing.
        ///
        /// 30 rather than 40 (2026-08-26): the app stacks a title bar, a menu
        /// row and often a tab strip before any content, and at 40 that chrome
        /// was taking a visible bite out of every panel. The title also came
        /// down a step, from subtitle to secondary, because type sized for the
        /// taller bar does not fit this one.
        /// </summary>
        public const int BarHeight = 30;

        // A flat single-shade bar at the same brightness as the content
        // below it reads as "not there" (the "modal title bars have become
        // somewhat invisible" regression) — a subtle top-lit gradient plus
        // an accent underline gives the bar its own presence without going
        // back to the old saturated-primary-color look. An accent wash
        // (2026-08-16, design-guide.md §9; strengthened same day — the
        // first pass at 8%/0% read as too subtle to register) blended into
        // that same neutral gradient — 18% at the top, still fading to a
        // lighter 5% by the bottom where the underline carries the accent
        // at full strength — nudges the bar further from "not there"
        // without reintroducing a fully colored bar.
        // Live Theme reads (not cached), so a title bar built after a theme
        // switch (design-guide.md §1) picks up the current palette.
        private static Color BarFillTop => Color.Lerp(Resources.StaticResources.Theme.SurfaceRaised, Resources.StaticResources.Theme.AccentSelection, 0.18f);
        private static Color BarFillBottom => Color.Lerp(Resources.StaticResources.Theme.SurfaceHeader, Resources.StaticResources.Theme.AccentSelection, 0.05f);
        private static Color AccentUnderline => Resources.StaticResources.Theme.AccentSelection;
        private static Color TitleText => Resources.StaticResources.Theme.BodyText;
        private static Color CloseHoverFill => Resources.StaticResources.Theme.AccentMuteOn;
        private static Color CloseIdleForeground => Color.Lerp(Resources.StaticResources.Theme.BodyText, Resources.StaticResources.Theme.SurfaceBorder, 0.4f);
        private static Color SizeHoverFill => Resources.StaticResources.Theme.SurfaceRaised;

        /// <summary>Inactive-window title bar treatment (2026-08-17): a flat
        /// neutral wash drawn ON TOP of the whole bar — gradient, accent
        /// underline, everything — rather than recoloring the gradient/
        /// underline's own fills. Deliberately NOT implemented by calling
        /// Set&lt;BackgroundFillTrait&gt; on `this` every frame with a
        /// desaturated gradient: `this`'s BackgroundFillTrait has dragBarElement/
        /// closeButton/sizeButton permanently Sync-subscribed to it (see the
        /// constructor's own Sync(...) calls) — Trait&lt;T&gt;.SyncSubscribe
        /// is a live event subscription, not a one-time copy, so calling
        /// Set&lt;BackgroundFillTrait&gt; on `this` on ANY later frame
        /// re-fires it, stomping those three children's own carefully-set
        /// Color.Transparent back to whatever `this` was just set to.
        /// Found the hard way (live user report): re-setting the gradient
        /// every frame permanently painted dragBarElement/closeButton
        /// opaque, hiding the accent underline everywhere except the one
        /// small gap their geometry didn't quite cover. An overlay added
        /// as its own independent child sidesteps that whole hazard class —
        /// it isn't Sync-subscribed to anything, so nothing it does can
        /// cascade sideways onto a sibling.</summary>
        private static Color InactiveOverlayColor => Resources.StaticResources.Theme.SurfaceBackdrop;
        private const float InactiveOverlayOpacity = 0.55f;

        private FilledRectangleElement accentUnderline;
        private FilledRectangleElement inactiveOverlay;

        /// <summary>What the red X does. Hosts with richer teardown than a
        /// bare Kill (hook scopes, view state) set their own close path here
        /// — the same path their Esc/Back uses. Null = Parent.Kill().</summary>
        public System.Action OnCloseRequested;

        private BasicButtonElement dragBarElement;
        private BasicButtonElement closeButton;
        private BasicButtonElement sizeButton;
        private bool hasMaximimizeButton;

        /// <summary>Whether the close X exists at all (2026-08-16 — added
        /// for the sequencer's own title bar: it isn't something you close,
        /// it's the base view every editor modal opens on top of, so a
        /// close button there would either be a confusing no-op or need
        /// invented "close to what?" semantics — better to just not have
        /// one, same as it had no title bar at all before this).</summary>
        private readonly bool closable;

        /// <summary>Chrome width reserved on the right for whichever of the
        /// close/maximize buttons actually exist — the single source both
        /// the constructor and Update() size dragBarElement/position
        /// sizeButton from, so the two stay in sync as closable/
        /// hasMaximimizeButton vary per host instead of each hardcoding
        /// BarHeight/80.</summary>
        private float RightChromeWidth => (closable ? BarHeight : 0) + (hasMaximimizeButton ? BarHeight : 0);

        public ModalTitleBarElement()
        {
            closable = true;
            dragBarElement = AddChildElement<BasicButtonElement>("drag bar");
            closeButton = AddChildElement<BasicButtonElement>("close button");
            sizeButton = AddChildElement<BasicButtonElement>("size button");
            Sync(closeButton);
            Sync(closeButton);
            if (!((ModalWindowElement)Parent).FitModalToContent)
            {
                sizeButton = AddChildElement<BasicButtonElement>("size button");
                Sync(sizeButton);
            }
            Sync(dragBarElement);
            Setup();
        }

        /// <summary>Whether this bar's drag-to-move/maximize affordances are
        /// live at all — false for TabContainerElement's own inert base
        /// title bar (2026-08-17, found live: GustUI's hit-testing doesn't
        /// resolve overlaps within a subtree — EVERY element along EVERY
        /// matching path gets the event regardless of visual z-order — so a
        /// "fully covered, purely inert" title bar sitting underneath the
        /// tab strip was never actually inert: its drag bar's OnMousePress/
        /// OnMouseRelease AND, since fitToContent:false incidentally turns
        /// hasMaximimizeButton on too, its maximize button, both fired
        /// ALONGSIDE whatever the tab strip/tab button on top was supposed
        /// to handle for the exact same click — the container itself would
        /// start "being dragged" as a window and/or swallow pointer capture
        /// on every single tab click. TabContainerElement's own tabStrip
        /// already wires the identical HandleTitleBarPress/Release pair
        /// itself ("the tab bar allows dragging this new container in
        /// blank areas") — so the base title bar's copy is pure redundant
        /// surface area once tabs exist, never load-bearing.</summary>
        private readonly bool interactive;

        public ModalTitleBarElement(string title, Element parent, TVVector position = null, TVVector size = null, bool closable = true, bool interactive = true)
        {

            this.Parent = parent;
            this.closable = closable;
            this.interactive = interactive;

            dragBarElement = AddChildElement<BasicButtonElement>("drag bar");
            if (closable)
            {
                closeButton = AddChildElement<BasicButtonElement>("close button");
                Sync(closeButton);
            }

            hasMaximimizeButton = interactive && Parent is ModalWindowElement modalWindowElement && !modalWindowElement.FitModalToContent;

            if (hasMaximimizeButton)
            {
                sizeButton = AddChildElement<BasicButtonElement>("size button");
                Sync(sizeButton);
            }
          
            Sync(dragBarElement);


            Set<BackgroundFillTrait>(new TVFillSimpleGradient(BarFillTop, BarFillBottom, Direction.Vertically));
            Set<BorderSizeTrait>(new TVInt(0));
            Set<FontTrait>(Resources.StaticResources.Theme.UiFontSecondary);
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));

            accentUnderline = new FilledRectangleElement(0, (int)size.Y - 2, (int)size.X, 2, new TVFillSolidColor(AccentUnderline));
            AddChild(accentUnderline, "accent-underline");

            // Not Sync()'d to anything, not synced FROM anything — see its
            // own doc comment. Explicit high Depth (not just "added last")
            // so it draws above dragBarElement/closeButton/sizeButton/
            // accentUnderline with no dependence on insertion-order
            // assumptions — this is the one element in this file that
            // genuinely needs to win every z-order tie.
            inactiveOverlay = new FilledRectangleElement(0, 0, (int)size.X, (int)size.Y, new TVFillSolidColor(InactiveOverlayColor));
            inactiveOverlay.Depth = 50;
            if (inactiveOverlay.ElementTrait<BackgroundFillTrait>().Value() is TVFill overlayFill)
            {
                overlayFill.Opacity = InactiveOverlayOpacity;
            }

            AddChild(inactiveOverlay, "inactive-overlay");

            if (closable)
            {
                closeButton.Set<SizeTrait>(new TVVector(size.Y, size.Y));
                closeButton.Set<TextTrait>(Resources.StaticResources.Theme.Icons.CloseIcon.ToTextTrait());
                closeButton.Set<FontTrait>(Resources.StaticResources.Theme.AltSymbolFont);
                closeButton.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
                closeButton.Set<ForegroundColorTrait>(new TVColor(CloseIdleForeground));
                closeButton.Set<PositionTrait>(new TVVector(size.X - size.Y, 0));
                closeButton.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) => RequestClose()));
                closeButton.Set<OnEnterTrait>(new TVEvent<ClickEventArgs>((x) =>
                {
                    closeButton.Set<BackgroundFillTrait>(new TVFillSolidColor(CloseHoverFill));
                    closeButton.Set<ForegroundColorTrait>(new TVColor(Color.White));
                }));
                closeButton.Set<OnExitTrait>(new TVEvent<ClickEventArgs>((x) =>
                {
                    closeButton.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
                    closeButton.Set<ForegroundColorTrait>(new TVColor(CloseIdleForeground));
                }));
            }

            if (hasMaximimizeButton)
            {
                sizeButton.Set<SizeTrait>(new TVVector(size.Y, size.Y));

                // Theme.SymbolFont (segmdl2.ttf), not AltSymbolFont
                // (SegoeIcons.ttf) — 2026-08-17, found from the user's own
                // test: AltSymbolFont's SDF bake only ever included the two
                // codepoints this app was previously confirmed to actually
                // ask it for (Cancel/FullScreen — see FontManager.cs's own
                // IconRangesFor comment), so MinimizeIcon (BackToWindow,
                // shown once actually maximized) rendered as nothing at
                // all — blank button. SymbolFont's own bake is
                // comprehensive (every UIFont.Symbol codepoint except the
                // two confirmed SDF-unsafe ones, neither of which is this),
                // so it already safely covers BackToWindow. Both maximize/
                // minimize states now render from the same font rather
                // than one working and the other silently blank.
                sizeButton.Set<FontTrait>(Resources.StaticResources.Theme.SymbolFont);
                sizeButton.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
                sizeButton.Set<ForegroundColorTrait>(new TVColor(CloseIdleForeground));
                sizeButton.Set<PositionTrait>(new TVVector(size.X - RightChromeWidth, 0));
                sizeButton.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) =>
                {
                    // Docked geometry is owned entirely by DockTo/LayoutDocked
                    // (2026-08-17) — toggling isFullScreen while docked has no
                    // visible effect (LayoutDocked overwrites Position/Size
                    // again next frame regardless) AND leaves isFullScreen
                    // stuck true for whenever the panel is later undocked,
                    // which would then snap it to fullscreen out of nowhere
                    // the very next frame. No-op instead — same treatment
                    // ResizeHandlesElement now gives its own handles while
                    // docked.
                    var modalWindow = (ModalWindowElement)Parent;
                    if (modalWindow.DockedSide == DockSide.None)
                    {
                        modalWindow.ToggleFullScreen();
                    }
                }));
                sizeButton.Set<OnEnterTrait>(new TVEvent<ClickEventArgs>((x) =>
                {
                    sizeButton.Set<BackgroundFillTrait>(new TVFillSolidColor(SizeHoverFill));
                    sizeButton.Set<ForegroundColorTrait>(new TVColor(Color.White));
                }));
                sizeButton.Set<OnExitTrait>(new TVEvent<ClickEventArgs>((x) =>
                {
                    sizeButton.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
                    sizeButton.Set<ForegroundColorTrait>(new TVColor(CloseIdleForeground));
                }));
            }

            dragBarElement.Set<SizeTrait>(new TVVector(size.X - RightChromeWidth, size.Y));
            dragBarElement.Set<PositionTrait>(new TVVector(0, 0));
            dragBarElement.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
            dragBarElement.Set<TextTrait>(new TVText(title));
            dragBarElement.Set<FontTrait>(Resources.StaticResources.Theme.UiFontSecondary);
            dragBarElement.Set<ForegroundColorTrait>(new TVColor(TitleText));

            // Drag-to-move is a ModalWindowElement behavior; other hosts
            // (full-screen modals) get a static title strip. Routed through
            // HandleTitleBarPress/Release rather than handleStartDrag/
            // handleStopDrag directly (2026-08-16, docked-modal feature) —
            // those wrappers handle undock-on-grab and commit-dock-on-
            // release; a non-dockable ModalWindowElement (DockedSide always
            // None) behaves identically to before, since both wrappers just
            // fall through to the same base drag calls in that case.
            if (interactive && Parent is ModalWindowElement dragHost)
            {
                dragBarElement.Set<OnMousePress>(new TVEvent<ClickEventArgs>((x) => dragHost.HandleTitleBarPress(x)));
                dragBarElement.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) => dragHost.HandleTitleBarRelease(x)));
            }

            Setup();
        }

        /// <summary>Updates the title text (renames, save-as, etc.).</summary>
        public void SetTitle(string title)
        {
            dragBarElement.Set<TextTrait>(new TVText(title));
        }

        private void RequestClose()
        {
            if (OnCloseRequested != null)
            {
                OnCloseRequested();
            }
            else
            {
                Parent.Kill();
            }
        }

        public override void Update(Element parent = null)
        {
            base.Update(parent);
            var size = parent.GetSize();
            Set<SizeTrait>(new TVVector(size.X, BarHeight));

            // Inactive-window overlay (2026-08-17) — re-sized every frame,
            // same "no dirty-checking" idiom the rest of this method already
            // uses for position/size (Depth/FrontSequence, what "active" is
            // keyed off, can change any frame a window is clicked/dragged).
            // Zero size when active — "hide via zero size," the same idiom
            // DockPreviewOverlay/TabMergePreviewOverlay already use, rather
            // than a visibility trait GustUI doesn't have.
            bool active = ModalWindowElement.IsFrontmostWindow(parent);
            inactiveOverlay?.Set<SizeTrait>(active ? new TVVector(0, 0) : new TVVector(size.X, BarHeight));
            inactiveOverlay?.Set<PositionTrait>(new TVVector(0, 0));

            accentUnderline?.Set<SizeTrait>(new TVVector(size.X, 2));
            accentUnderline?.Set<PositionTrait>(new TVVector(0, BarHeight - 2));
            if (closable)
            {
                closeButton.Set<PositionTrait>(new TVVector(size.X - BarHeight, 0));
                closeButton.Set<SizeTrait>(new TVVector(BarHeight, BarHeight));
            }

            if (hasMaximimizeButton)
            {
                sizeButton.Set<PositionTrait>(new TVVector(size.X - RightChromeWidth, 0));
                sizeButton.Set<SizeTrait>(new TVVector(BarHeight, BarHeight));
                sizeButton.Set<TextTrait>(((ModalWindowElement)Parent).isFullScreen ? Resources.StaticResources.Theme.Icons.MinimizeIcon.ToTextTrait() : Resources.StaticResources.Theme.Icons.MaximizeIcon.ToTextTrait());
            }

            dragBarElement.Set<SizeTrait>(new TVVector(size.X - RightChromeWidth, BarHeight));
        }


        private void Setup()
        {
            Set<BorderSizeTrait, TVInt>(new TVInt(0));

            if (closable)
            {
                this.AddChild(closeButton, "closeButton");
            }

            this.AddChild(dragBarElement, "titleText");
        }
    }
}
