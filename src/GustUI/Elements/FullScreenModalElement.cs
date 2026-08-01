using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System.Linq;

namespace GustUI.Elements
{
    /// <summary>
    /// A full-screen MODAL surface: an opaque panel that fills the window
    /// between the top chrome (a <see cref="FruitMenuElement"/>, when one is
    /// present) and an app-declared <see cref="BottomInset"/> (e.g. a status
    /// bar) — chrome is never covered. Content views host their elements as
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
        /// tiers: tooltip 1000000 &gt; popup 500000 &gt; status bar 100000 &gt;
        /// loading 90000 &gt; MODAL 60000 &gt; side panels 50000 &gt; content 0.</summary>
        public const int ModalDepth = 60000;

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
        private bool closed;
        private ModalTitleBarElement titleBar;
        private string title;
        private System.Action onCloseRequested;

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

        /// <summary>Y (modal-relative) where hosted content starts: below the
        /// title-bar chrome when one exists, else 0.</summary>
        public int ContentTop => titleBar != null ? ModalTitleBarElement.BarHeight : 0;

        /// <summary>The composed chrome (null until a <see cref="Title"/> is set).</summary>
        public ModalTitleBarElement TitleBar => titleBar;

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
            Set<BackgroundFillTrait>(new TVFillSolidColor(new Color(16, 16, 20)));
            hookScopeToken = Resources.StaticResources.InputManager.PushHookScope();
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
            Resources.StaticResources.InputManager.PopHookScope(hookScopeToken);
            Kill();
        }

        public override void Update(Element parent = null)
        {
            base.Update(parent);

            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            if (windowSize != lastWindowSize && windowSize.X > 0 && windowSize.Y > 0)
            {
                lastWindowSize = windowSize;
                Layout();
            }
        }

        private void Layout()
        {
            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            float top = 0;
            Element menu = Resources.StaticResources.RootWindow.Children?.Items.FirstOrDefault(x => x is FruitMenuElement);
            if (menu != null)
            {
                top = menu.GetSize().Y;
            }

            Set<PositionTrait>(new TVVector(0, top));
            Set<SizeTrait>(new TVVector(windowSize.X, System.Math.Max(0, windowSize.Y - top - BottomInset)));
        }
    }
}
