using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace GustUI.Elements
{
    [ElementTraits(typeof(BodyTextTrait), typeof(FontTrait), typeof(ForegroundColorTrait), typeof(OnClickTrait))]
    public class ModalWindowElement : FilledRectangleElement
    {
        private string body;
        private TVVector size;
        private TextElement textElement;
        private ModalTitleBarElement titleBarElement;
        private List<BasicButtonElement> buttons = new List<BasicButtonElement>();
        private FilledRectangleElement buttonBackgroundElement;
        private FilledRectangleElement backdropTop;
        private FilledRectangleElement backdropBottom;
        private FilledRectangleElement backdropLeft;
        private FilledRectangleElement backdropRight;
        public ModalWindowElement()
        {
            textElement = this.AddChildElement<TextElement>();
            titleBarElement = this.AddChildElement<ModalTitleBarElement>();
            buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();


            Setup();
        }
        public ModalWindowElement(string title, string body, List<BasicButtonElement> buttons = null, TVVector position = null, TVVector size = null)
        {
            this.body = body;



            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<ForegroundColorTrait>(new TVColor(Color.Black));
            Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.White, new Color(200, 200, 200), Direction.Vertically));
            Set<BodyTextTrait>(new TVText(body));
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));


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
                new TVVector(0, 0),
                new TVVector(size != null ? size.X : 400, 40));

            AddChildElement(titleBarElement);
            textElement = this.AddChildElement<TextElement>();
            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();
            }

            backdropBottom = this.AddChildElement<FilledRectangleElement>();
            backdropTop = this.AddChildElement<FilledRectangleElement>();
            backdropLeft = this.AddChildElement<FilledRectangleElement>();
            backdropRight = this.AddChildElement<FilledRectangleElement>();



            Setup();
        }

        private void Setup()
        {
            size = this.GetSize();

            textElement.Set<PositionTrait>(new TVVector(10, 50));
            textElement.Set<TextTrait>(new TVText(body));
            textElement.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            textElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
            textElement.Set<SizeTrait>(new TVVector(size.X - 20, 0));

            textElement.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment() { Alignment = HorizontalAlignment.Left });


            float buttonHeight = (this.buttons.Count > 0 ? 80 : 10);
            float textSize = textElement.CalculatedSize().Y;
            float calcHeight = 80 + textSize + buttonHeight;
            size = new TVVector(size.X, calcHeight);
            Set<SizeTrait>(size);

            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement.Set<PositionTrait>(new TVVector(0, size.Y - 80));
                buttonBackgroundElement.Set<SizeTrait>(new TVVector(size.X, 80));
                buttonBackgroundElement.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.Black * 0.75f, Color.Black * 0.80f, Direction.Vertically));
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

            backdropLeft.Set<PositionTrait>(new TVVector(-actualPosition));
            backdropLeft.Set<SizeTrait>(new TVVector(actualPosition.X, windowSize.Y));
            backdropLeft.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Black * 0.5f));

            backdropRight.Set<PositionTrait>(new TVVector(new Vector2(calculatedModalSize.X,-actualPosition.Y)));
            backdropRight.Set<SizeTrait>(new TVVector(windowSize.X-(actualPosition.X+calculatedModalSize.X), windowSize.Y));
            backdropRight.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Black * 0.5f));

            backdropTop.Set<PositionTrait>(new TVVector(0, -actualPosition.Y));
            backdropTop.Set<SizeTrait>(new TVVector(calculatedModalSize.X, actualPosition.Y));
            backdropTop.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Black * 0.5f));

            backdropBottom.Set<PositionTrait>(new TVVector(0, calculatedModalSize.Y));
            backdropBottom.Set<SizeTrait>(new TVVector(calculatedModalSize.X, windowSize.Y-(actualPosition.Y+calculatedModalSize.Y)));
            backdropBottom.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Black * 0.5f));

        }

    }
}
