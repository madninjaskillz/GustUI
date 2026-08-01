using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    /// <summary>
    /// The SHARED window-chrome title bar: green gradient bar carrying the
    /// title text plus the red X close button (and, for resizable
    /// <see cref="ModalWindowElement"/> hosts, the blue maximize button).
    /// Historically welded to ModalWindowElement; now any element can host it
    /// (e.g. <see cref="FullScreenModalElement"/>) — drag/maximize behaviors
    /// only attach when the parent actually is a ModalWindowElement, and the
    /// X routes through <see cref="OnCloseRequested"/> (default: kill the
    /// parent, the classic modal-window behavior).
    /// </summary>
    [ElementTraits(typeof(FontTrait), typeof(OnExitTrait))]
    public class ModalTitleBarElement : FilledRectangleElement
    {
        /// <summary>The chrome's fixed height (Update re-asserts it).</summary>
        public const int BarHeight = 40;

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


            Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.Green*0.9f, Color.DarkGreen*0.9f, Direction.Vertically));
            Set<BorderSizeTrait>(new TVInt(0));
            Set<BorderFillTrait>(new TVBorderColorFill(Color.Gray));
            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));

            closeButton.Set<SizeTrait>(new TVVector(size.Y, size.Y));
            closeButton.Set<TextTrait>(Resources.StaticResources.Theme.Icons.CloseIcon.ToTextTrait());
            closeButton.Set<FontTrait>(Resources.StaticResources.Theme.AltSymbolFont);
            closeButton.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.Red, Color.DarkRed, Direction.Vertically));
            closeButton.Set<ForegroundColorTrait>(new TVColor(Color.White));
            closeButton.Set<PositionTrait>(new TVVector(size.X - size.Y, 0));
            closeButton.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) => RequestClose()));

            if (hasMaximimizeButton)
            {
                sizeButton.Set<SizeTrait>(new TVVector(size.Y, size.Y));

                sizeButton.Set<FontTrait>(Resources.StaticResources.Theme.AltSymbolFont);
                sizeButton.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.Blue, Color.DarkBlue, Direction.Vertically));
                sizeButton.Set<ForegroundColorTrait>(new TVColor(Color.White));
                sizeButton.Set<PositionTrait>(new TVVector(size.X - (80), 0));
                sizeButton.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) => ((ModalWindowElement)Parent).ToggleFullScreen()));
            }

            dragBarElement.Set<SizeTrait>(new TVVector(size.X - size.Y, size.Y));
            dragBarElement.Set<PositionTrait>(new TVVector(0, 0));
            dragBarElement.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.Green, Color.DarkGreen, Direction.Vertically));
            dragBarElement.Set<TextTrait>(new TVText(title));
            dragBarElement.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            dragBarElement.Set<ForegroundColorTrait>(new TVColor(Color.White));

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
