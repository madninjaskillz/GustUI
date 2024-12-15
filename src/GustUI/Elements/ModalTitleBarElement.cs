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

        public ModalTitleBarElement()
        {
            dragBarElement = AddChildElement<BasicButtonElement>();
            closeButton = AddChildElement<BasicButtonElement>();
            Sync(closeButton);
            Sync(dragBarElement);
            Setup();
        }

        public ModalTitleBarElement(string title, TVVector position = null, TVVector size = null)
        {
            dragBarElement = AddChildElement<BasicButtonElement>();
            closeButton = AddChildElement<BasicButtonElement>();
            Sync(closeButton);
            Sync(dragBarElement);


            Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.Green, Color.DarkGreen, Direction.Vertically));
            Set<BorderSizeTrait>(new TVInt(0));
            Set<BorderColorTrait>(new TVColor(Color.Gray));
            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));

            closeButton.Set<SizeTrait>(new TVVector(size.Y, size.Y));
            closeButton.Set<TextTrait>(Resources.StaticResources.Theme.Icons.CloseIcon.ToTextTrait());
            closeButton.Set<FontTrait>(Resources.StaticResources.Theme.AltSymbolFont);
            closeButton.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.Red, Color.DarkRed, Direction.Vertically));
            closeButton.Set<ForegroundColorTrait>(new TVColor(Color.White));
            closeButton.Set<PositionTrait>(new TVVector(size.X - size.Y, 0));
            closeButton.Set<OnClickTrait>(new TVEvent((x) => Parent.Kill()));

            dragBarElement.Set<SizeTrait>(new TVVector(size.X - size.Y, size.Y));
            dragBarElement.Set<PositionTrait>(new TVVector(0, 0));
            dragBarElement.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.Green, Color.DarkGreen, Direction.Vertically));
            dragBarElement.Set<TextTrait>(new TVText(title));
            dragBarElement.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            dragBarElement.Set<ForegroundColorTrait>(new TVColor(Color.White));
            dragBarElement.Set<OnMousePress>(new TVEvent((x) => ((ModalWindowElement)Parent).handleStartDrag(x)));
            dragBarElement.Set<OnMouseRelease>(new TVEvent((x) => ((ModalWindowElement)Parent).handleStopDrag(x)));


            Setup();
        }

        public override void Update(Element parent = null)
        {
            var size = parent.GetSize();
            closeButton.Set<PositionTrait>(new TVVector(size.X - 40, 0));
            closeButton.Set<SizeTrait>(new TVVector(40,40));
            dragBarElement.Set<SizeTrait>(new TVVector(size.X - 40, 40));
            base.Update(parent);
        }


        private void Setup()
        {
            Set<BorderSizeTrait, TVInt>(new TVInt(0));

            this.AddChild(closeButton, "closeButton");
            this.AddChild(dragBarElement, "titleText");
        }
    }
}
