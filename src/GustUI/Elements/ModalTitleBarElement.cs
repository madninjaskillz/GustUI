using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    [ElementTraits(typeof(FontTrait), typeof(OnExitTrait))]
    public class ModalTitleBarElement : FilledRectangleElement
    {
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
            closeButton.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) => Parent.Kill()));

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
            dragBarElement.Set<OnMousePress>(new TVEvent<ClickEventArgs>((x) => ((ModalWindowElement)Parent).handleStartDrag(x)));
            dragBarElement.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) => ((ModalWindowElement)Parent).handleStopDrag(x)));


            Setup();
        }

        public override void Update(Element parent = null)
        {
            base.Update(parent);
            var size = parent.GetSize();
            Set<SizeTrait>(new TVVector(size.X,40));
            closeButton.Set<PositionTrait>(new TVVector(size.X - 40, 0));
            closeButton.Set<SizeTrait>(new TVVector(40, 40));
            if (hasMaximimizeButton)
            {
                sizeButton.Set<PositionTrait>(new TVVector(size.X - 80, 0));
                sizeButton.Set<SizeTrait>(new TVVector(40, 40));
                sizeButton.Set<TextTrait>(((ModalWindowElement)Parent).isFullScreen ? Resources.StaticResources.Theme.Icons.MinimizeIcon.ToTextTrait() : Resources.StaticResources.Theme.Icons.MaximizeIcon.ToTextTrait());
            }
            dragBarElement.Set<SizeTrait>(new TVVector(size.X - (hasMaximimizeButton ? 80 : 40), 40));

           
            
        }


        private void Setup()
        {
            Set<BorderSizeTrait, TVInt>(new TVInt(0));

            this.AddChild(closeButton, "closeButton");
            this.AddChild(dragBarElement, "titleText");
        }
    }
}
