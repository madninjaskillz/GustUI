using GustUI.Extensions;
using GustUI.Models;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace GustUI.Elements
{
    /// <summary>
    /// A full-screen MODAL surface: an opaque panel that fills the window
    /// between whatever <see cref="Managers.DockLayout"/> reserves on each
    /// edge (docked panels — the loop browser, wave bank, Stack) and an
    /// app-declared <see cref="BottomInset"/> (e.g. a status bar) — chrome
    /// is never covered. Content views host their elements as
    /// children of the modal; whatever lies beneath stays alive and untouched
    /// and is simply revealed again when the modal closes.
    ///
    /// Modality is enforced on both input channels:
    /// - POINTER: the modal draws (and therefore hit-tests) in its own depth
    ///   tier, <see cref="ModalDepth"/> — above content and side panels,
    ///   below the loading overlay / status bar / popup / tooltip tiers — so
    ///   the hover pass lands on the modal branch, never on elements beneath.
    /// - KEYBOARD: construction pushes an <see cref="Managers.InputManager"/>
    ///   hook SCOPE; hooks the hosted view registers after creating the modal
    ///   join that scope, and only the active scope's hooks fire — the
    ///   underlying view's shortcuts are suppressed for the modal's lifetime.
    ///   <see cref="Close"/> pops the scope (always close via Close, not a
    ///   bare Kill, or the scope leaks).
    ///
    /// The element is marked <see cref="Element.IsChrome"/>: an app that
    /// rebuilds the view beneath the modal (stage-clearing the window's
    /// non-chrome children) must not tear down the modal above it.
    ///
    /// Setting <see cref="Title"/> composes the shared window chrome — the
    /// same <see cref="ModalTitleBarElement"/> the ModalWindowElement wears
    /// (green title bar, red X) — with the X routed through
    /// <see cref="OnCloseRequested"/>; content starts at <see cref="ContentTop"/>.
    /// </summary>
    public class FullScreenModalElement : FilledRectangleElement
    {
        /// <summary>Depth tier for full-screen modals. The documented root
        /// tiers: tooltip 1000000 &gt; dock preview 700000 (see
        /// <see cref="DockPreviewOverlay"/>) &gt; popup 500000 &gt; status bar
        /// 100000 &gt; loading 90000 &gt; MODAL 60000 &gt; side panels 50000 &gt;
        /// content 0.</summary>
        public const int ModalDepth = 60000;

        /// <summary>
        /// True while at least one full-screen modal is open — i.e. while
        /// something opaque genuinely covers the content beneath.
        ///
        /// An app that hand-drives per-frame logic for the view underneath
        /// (GustUI updates the ELEMENT tree itself, but a host's own view
        /// objects are not elements) can use this to decide whether that
        /// work is still worth doing. Asking here rather than enumerating
        /// the host's own modal-ish views is the difference between a check
        /// that stays true and a list that silently rots the next time one
        /// of those views becomes a <see cref="ModalWindowElement"/> — a
        /// real window you work ALONGSIDE, which covers nothing.
        ///
        /// Counted, not a bool: modals stack (a wave picker over a device
        /// panel), so the last one to close is what uncovers the content.
        /// Maintained by the constructor and <see cref="Close"/>, so it
        /// inherits the same close-me-properly contract as the hook scope —
        /// a bare <c>Kill()</c> leaks both.
        /// </summary>
        public static bool AnyOpen => openCount > 0;

        private static int openCount;

        /// <summary>Window-bottom pixels left uncovered (the app's bottom
        /// chrome, e.g. a status bar). 0 = fill to the bottom edge.
        /// Setting re-lays-out immediately, so object-initializer assignment
        /// leaves the modal correctly sized before its content builds.</summary>
        public int BottomInset
        {
            get => bottomInset;
            set
            {
                bottomInset = value;
                Layout();
            }
        }

        private int bottomInset;

        private readonly int hookScopeToken;
        private Vector2 lastWindowSize = Vector2.Zero;
        private float lastLeftInset = -1f;
        private float lastRightInset = -1f;
        private float lastTopInset = -1f;
        private float lastBottomInset = -1f;
        private bool closed;
        private ModalTitleBarElement titleBar;
        private string title;
        private System.Action onCloseRequested;
        private MenuBarElement menuBar;
        private ToolbarElement toolbar;

        /// <summary>See ModalWindowElement's identically-named/-purposed
        /// fields (including toolbarSlot's own doc comment on why toolbar
        /// isn't itself a chromeRow child) — same wrapping-row mechanism,
        /// this is the FullScreenModalElement counterpart.</summary>
        private HorizontalStackElement chromeRow;
        private FilledRectangleElement menuSpacer;
        private FilledRectangleElement toolbarSlot;

        /// <summary>The chrome row's ONE full-width SurfaceHeader strip,
        /// drawn behind the menu bar and toolbar (Depth −1): the bars
        /// themselves are transparent (see MenuBarElement's ctor comment),
        /// so their differing heights (28 vs 40) can't leave a dark gap
        /// under the shorter one — the container paints the background,
        /// not the elements. Sized every Update to the modal's width ×
        /// ChromeRowHeight (which already covers the wrapped two-row and
        /// menu-only cases).</summary>
        private FilledRectangleElement chromeRowBg;

        /// <summary>
        /// The modal's window-chrome title (the welcome/About-modal look:
        /// the shared <see cref="ModalTitleBarElement"/> — green bar, white
        /// title text, red X). Setting a non-empty title creates the chrome;
        /// hosted content should start at <see cref="ContentTop"/>. Set again
        /// to retitle live (e.g. after a rename/save-as).
        /// </summary>
        public string Title
        {
            get => title;
            set
            {
                title = value;
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }

                EnsureTitleBar();
                titleBar.SetTitle(value);
            }
        }

        /// <summary>What the title bar's X does — the hosting view's close
        /// path (the same path as its Esc/Back). Defaults to <see cref="Close"/>.</summary>
        public System.Action OnCloseRequested
        {
            get => onCloseRequested;
            set
            {
                onCloseRequested = value;
                if (titleBar != null)
                {
                    titleBar.OnCloseRequested = value ?? Close;
                }
            }
        }

        /// <summary>Height of the chrome row(s) directly below the title
        /// bar — see ModalWindowElement.ChromeRowHeight's identical doc
        /// comment for the wrap-aware reasoning.</summary>
        private int ChromeRowHeight => chromeRow != null
            ? (int)chromeRow.GetSize().Y
            : (menuBar != null ? MenuBarElement.BarHeight : 0);

        /// <summary>Y (modal-relative) where hosted content starts: below the
        /// title-bar chrome when one exists (else 0), plus the shared menu/
        /// toolbar row (see <see cref="ChromeRowHeight"/>).</summary>
        public int ContentTop => (titleBar != null ? ModalTitleBarElement.BarHeight : 0) + ChromeRowHeight;

        /// <summary>The composed chrome (null until a <see cref="Title"/> is set).</summary>
        public ModalTitleBarElement TitleBar => titleBar;

        /// <summary>
        /// Shows (or replaces) this modal's own menu strip, directly below
        /// its title bar — the per-modal replacement for the old global
        /// FruitMenuElement (2026-08-17 rework: no global menu bar; views
        /// that implement IViewMenuContext call this with their own
        /// MenuSections() right after setting Title). No-op if
        /// <paramref name="sections"/> is null/empty — a view with nothing
        /// to contribute (Preferences, About, etc.) simply never calls this
        /// and gets no menu row, same as before.
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
                menuBar.Set<PositionTrait>(new TVVector(0, titleBar != null ? ModalTitleBarElement.BarHeight : 0));
                AddChild(menuBar, "modal-menu-bar");
            }
            else
            {
                menuBar.SetItems(sections);
            }
        }

        /// <summary>Lazily builds (or returns the existing) toolbar strip —
        /// see ModalWindowElement.EnsureToolbar's identical doc comment
        /// (wrap-below-the-menu-bar included); this is the
        /// FullScreenModalElement counterpart. Call AFTER SetMenu if this
        /// modal also has a menu, so the reserved space matches what's
        /// actually there.</summary>
        public ToolbarElement EnsureToolbar()
        {
            if (toolbar == null)
            {
                EnsureChromeRowBackground();

                // toolbar itself stays a direct child of `this`, not of
                // chromeRow — see ModalWindowElement.toolbarSlot's doc
                // comment for why.
                toolbar = new ToolbarElement(this);
                AddChild(toolbar, "modal-toolbar");

                float y = titleBar != null ? ModalTitleBarElement.BarHeight : 0;
                chromeRow = new HorizontalStackElement { WrapWidth = this.GetSize().X };
                chromeRow.Set<PositionTrait>(new TVVector(0, y));
                AddChild(chromeRow, "chrome-row");

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

        private void EnsureChromeRowBackground()
        {
            if (chromeRowBg == null)
            {
                chromeRowBg = new FilledRectangleElement(0, 0, (int)this.GetSize().X, MenuBarElement.BarHeight,
                    new TVFillSolidColor(() => Resources.StaticResources.Theme.SurfaceHeader))
                {
                    Depth = -1, // behind the (transparent) bars and everything else in the modal
                };
                AddChild(chromeRowBg, "chrome-row-bg");
            }
        }

        private void EnsureTitleBar()
        {
            if (titleBar != null)
            {
                return;
            }

            titleBar = new ModalTitleBarElement(
                title ?? "",
                this,
                new TVVector(0, 0),
                new TVVector(System.Math.Max(ModalTitleBarElement.BarHeight, this.GetSize().X), ModalTitleBarElement.BarHeight));
            titleBar.OnCloseRequested = onCloseRequested ?? Close;
            AddChild(titleBar, "modal-title-bar");
        }

        public FullScreenModalElement()
        {
            IsChrome = true;
            Depth = ModalDepth;
            // Theme.ModalBackgroundAlpha (see its own doc comment): every
            // full-screen view's base background is now a subtly
            // translucent wash of its own color rather than fully opaque,
            // so the decorative WindowElement background video shows
            // through faintly instead of being fully hidden the instant
            // any view opens. Callers that override this (see e.g.
            // PianoRollView/ModuleEditorView) should multiply by the same
            // constant rather than going back to full opacity.
            Set<BackgroundFillTrait>(new TVFillSolidColor(() => Resources.StaticResources.Theme.SurfaceBackdrop * Theme.ModalBackgroundAlpha));
            hookScopeToken = Resources.StaticResources.InputManager.PushHookScope();
            openCount++;
            Layout();
        }

        /// <summary>Pops the modal's keyboard-hook scope and removes the
        /// element. Idempotent.</summary>
        public void Close()
        {
            if (closed)
            {
                return;
            }

            closed = true;
            openCount--;
            Resources.StaticResources.InputManager.PopHookScope(hookScopeToken);
            Kill();
        }

        public override void Update(Element parent = null)
        {
            base.Update(parent);

            // Keeps the toolbar/menu wrap decision live across a resize —
            // see ModalWindowElement.Update's identical block.
            if (chromeRow != null)
            {
                chromeRow.WrapWidth = this.GetSize().X;
                if (menuSpacer != null)
                {
                    menuSpacer.Set<SizeTrait>(new TVVector(menuBar.ContentWidth, MenuBarElement.BarHeight));
                }

                toolbarSlot.Set<SizeTrait>(new TVVector(toolbar.ContentWidth, ToolbarElement.BarHeight));
                float rowTop = titleBar != null ? ModalTitleBarElement.BarHeight : 0;
                TVVector slotPosition = toolbarSlot.ElementTrait<PositionTrait>().Value();
                toolbar.Set<PositionTrait>(new TVVector(slotPosition.X, rowTop + slotPosition.Y));
            }

            if (chromeRowBg != null)
            {
                chromeRowBg.Set<PositionTrait>(new TVVector(0, titleBar != null ? ModalTitleBarElement.BarHeight : 0));
                chromeRowBg.Set<SizeTrait>(new TVVector(this.GetSize().X, ChromeRowHeight));
            }

            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            // DockLayout insets (2026-08-16 docked-modal feature) change
            // independent of the window's own size — a panel docking/
            // undocking elsewhere doesn't resize the window at all — so
            // they need their own poll-and-compare here, same idiom as
            // windowSize itself already uses; there's no push/event path
            // from DockLayout to notify every open modal a dock changed.
            bool sizeChanged = windowSize != lastWindowSize && windowSize.X > 0 && windowSize.Y > 0;
            // All four sides, not just Left/Right (2026-08-17 — found live:
            // Top/Bottom docking was added specifically for the Stack, which
            // docks Bottom, but this check never looked at Top/BottomInset —
            // opening/closing the Stack while a FullScreenModalElement view
            // was open silently failed to resize it unless something else
            // ALSO changed that same frame).
            bool insetsChanged = Managers.DockLayout.LeftInset != lastLeftInset
                || Managers.DockLayout.RightInset != lastRightInset
                || Managers.DockLayout.TopInset != lastTopInset
                || Managers.DockLayout.BottomInset != lastBottomInset;
            if (sizeChanged || insetsChanged)
            {
                lastWindowSize = windowSize;
                lastLeftInset = Managers.DockLayout.LeftInset;
                lastRightInset = Managers.DockLayout.RightInset;
                lastTopInset = Managers.DockLayout.TopInset;
                lastBottomInset = Managers.DockLayout.BottomInset;
                Layout();
            }
        }

        // No more window-root chrome to inset below (the global fruit menu
        // was removed entirely in the 2026-08-17 per-modal menu rework) —
        // top/left/right/bottom insets now come purely from whatever's
        // docked (Managers.DockLayout, Top/Bottom added same day) plus this
        // instance's own app-declared BottomInset (status bar).
        private void Layout()
        {
            var available = Managers.DockLayout.AvailableRect(BottomInset);
            Set<PositionTrait>(new TVVector(available.Position));
            Set<SizeTrait>(new TVVector(available.Size));
        }
    }
}
