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
        public const int BarHeight = 40;

        // A flat single-shade bar at the same brightness as the content
        // below it reads as "not there" (the "modal title bars have become
        // somewhat invisible" regression) — a subtle top-lit gradient plus
        // an accent underline gives the bar its own presence without going
        // back to the old saturated-primary-color look. A faint accent wash
        // (2026-08-16, design-guide.md §9) blended into that same neutral
        // gradient — 8% at the top fading to nothing by the bottom, where
        // the underline already carries the full-strength accent — nudges
        // the bar a little further from "not there" without reintroducing
        // a colored bar.
        // Live Theme reads (not cached), so a title bar built after a theme
        // switch (design-guide.md §1) picks up the current palette.
        private static Color BarFillTop => Color.Lerp(Resources.StaticResources.Theme.SurfaceRaised, Resources.StaticResources.Theme.AccentSelection, 0.08f);
        private static Color BarFillBottom => Resources.StaticResources.Theme.SurfaceHeader;
        private static Color AccentUnderline => Resources.StaticResources.Theme.AccentSelection;
        private static Color TitleText => Resources.StaticResources.Theme.BodyText;
        private static Color CloseHoverFill => Resources.StaticResources.Theme.AccentMuteOn;
        private static Color CloseIdleForeground => Color.Lerp(Resources.StaticResources.Theme.BodyText, Resources.StaticResources.Theme.SurfaceBorder, 0.4f);
        private static Color SizeHoverFill => Resources.StaticResources.Theme.SurfaceRaised;

        private FilledRectangleElement accentUnderline;

        /// <summary>What the red X does. Hosts with richer teardown than a
        /// bare Kill (hook scopes, view state) set their own close path here
        /// — the same path their Esc/Back uses. Null = Parent.Kill().</summary>
        public System.Action OnCloseRequested;

        private BasicButtonElement dragBarElement;
        private BasicButtonElement closeButton;
        private BasicButtonElement sizeButton;
        private bool hasMaximimizeButton;
        public ModalTitleBarElement()
        {
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

        public ModalTitleBarElement(string title, Element parent, TVVector position = null, TVVector size = null)
        {

            this.Parent = parent;

            dragBarElement = AddChildElement<BasicButtonElement>("drag bar");
            closeButton = AddChildElement<BasicButtonElement>("close button");
            

            Sync(closeButton);

            hasMaximimizeButton = Parent is ModalWindowElement modalWindowElement && !modalWindowElement.FitModalToContent;

            if (hasMaximimizeButton)
            {
                sizeButton = AddChildElement<BasicButtonElement>("size button");
                Sync(sizeButton);
            }
          
            Sync(dragBarElement);


            Set<BackgroundFillTrait>(new TVFillSimpleGradient(BarFillTop, BarFillBottom, Direction.Vertically));
            Set<BorderSizeTrait>(new TVInt(0));
            Set<FontTrait>(Resources.StaticResources.Theme.UiFontBold);
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));

            accentUnderline = new FilledRectangleElement(0, (int)size.Y - 2, (int)size.X, 2, new TVFillSolidColor(AccentUnderline));
            AddChild(accentUnderline, "accent-underline");

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

            if (hasMaximimizeButton)
            {
                sizeButton.Set<SizeTrait>(new TVVector(size.Y, size.Y));

                sizeButton.Set<FontTrait>(Resources.StaticResources.Theme.AltSymbolFont);
                sizeButton.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
                sizeButton.Set<ForegroundColorTrait>(new TVColor(CloseIdleForeground));
                sizeButton.Set<PositionTrait>(new TVVector(size.X - (80), 0));
                sizeButton.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) => ((ModalWindowElement)Parent).ToggleFullScreen()));
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

            dragBarElement.Set<SizeTrait>(new TVVector(size.X - size.Y, size.Y));
            dragBarElement.Set<PositionTrait>(new TVVector(0, 0));
            dragBarElement.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
            dragBarElement.Set<TextTrait>(new TVText(title));
            dragBarElement.Set<FontTrait>(Resources.StaticResources.Theme.UiFontBold);
            dragBarElement.Set<ForegroundColorTrait>(new TVColor(TitleText));

            // Drag-to-move is a ModalWindowElement behavior; other hosts
            // (full-screen modals) get a static title strip.
            if (Parent is ModalWindowElement dragHost)
            {
                dragBarElement.Set<OnMousePress>(new TVEvent<ClickEventArgs>((x) => dragHost.handleStartDrag(x)));
                dragBarElement.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) => dragHost.handleStopDrag(x)));
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
            accentUnderline?.Set<SizeTrait>(new TVVector(size.X, 2));
            accentUnderline?.Set<PositionTrait>(new TVVector(0, BarHeight - 2));
            closeButton.Set<PositionTrait>(new TVVector(size.X - BarHeight, 0));
            closeButton.Set<SizeTrait>(new TVVector(BarHeight, BarHeight));
            if (hasMaximimizeButton)
            {
                sizeButton.Set<PositionTrait>(new TVVector(size.X - 80, 0));
                sizeButton.Set<SizeTrait>(new TVVector(BarHeight, BarHeight));
                sizeButton.Set<TextTrait>(((ModalWindowElement)Parent).isFullScreen ? Resources.StaticResources.Theme.Icons.MinimizeIcon.ToTextTrait() : Resources.StaticResources.Theme.Icons.MaximizeIcon.ToTextTrait());
            }
            dragBarElement.Set<SizeTrait>(new TVVector(size.X - (hasMaximimizeButton ? 80 : BarHeight), BarHeight));
        }


        private void Setup()
        {
            Set<BorderSizeTrait, TVInt>(new TVInt(0));

            this.AddChild(closeButton, "closeButton");
            this.AddChild(dragBarElement, "titleText");
        }
    }
}
