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
        
        private ModalTitleBarElement titleBarElement;
        private List<BasicButtonElement> buttons = new List<BasicButtonElement>();
        private FilledRectangleElement buttonBackgroundElement;
        private FilledRectangleElement backdropTop;
        private FilledRectangleElement backdropTopLeft;
        private FilledRectangleElement backdropTopRight;
        
        private FilledRectangleElement backdropLeft;
        private FilledRectangleElement backdropRight;

        private FilledRectangleElement backdropBottomLeft;
        private FilledRectangleElement backdropBottomRight;
        private FilledRectangleElement backdropBottom;

        private Element content;
        public ModalWindowElement()
        {
            
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
            content = this.AddChildElement<TextElement>();

            content.Set<PositionTrait>(new TVVector(10, 50));
            content.Set<TextTrait>(new TVText(body));
            content.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            content.Set<ForegroundColorTrait>(new TVColor(Color.Black));
            content.Set<SizeTrait>(new TVVector(size.X - 20, 0));
            content.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment() { Alignment = HorizontalAlignment.Left });

            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();
            }

            backdropTop = this.AddChildElement<FilledRectangleElement>();
            backdropBottom = this.AddChildElement<FilledRectangleElement>();
            backdropBottomLeft = this.AddChildElement<FilledRectangleElement>();
            backdropBottomRight = this.AddChildElement<FilledRectangleElement>();
            backdropTopLeft = this.AddChildElement<FilledRectangleElement>();
            backdropTopRight = this.AddChildElement<FilledRectangleElement>();
            backdropLeft = this.AddChildElement<FilledRectangleElement>();
            backdropRight = this.AddChildElement<FilledRectangleElement>();



            Setup();
        }

        public ModalWindowElement(string title, Element body, List<BasicButtonElement> buttons = null, TVVector position = null, TVVector size = null)
        {
            
            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<ForegroundColorTrait>(new TVColor(Color.Black));
            Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.White, new Color(200, 200, 200), Direction.Vertically));
            
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(new TVVector(content != null ? content.GetSize().X+20 : size.X ,size.Y) ?? new TVVector(0, 0));

            


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

            this.content = body;
            this.AddChild(this.content,"content");

            content.Set<PositionTrait>(new TVVector(10, 50));
            

            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();
            }



            backdropTop = this.AddChildElement<FilledRectangleElement>();
            backdropBottom = this.AddChildElement<FilledRectangleElement>();
            backdropBottomLeft = this.AddChildElement<FilledRectangleElement>();
            backdropBottomRight = this.AddChildElement<FilledRectangleElement>();
            backdropTopLeft = this.AddChildElement<FilledRectangleElement>();
            backdropTopRight = this.AddChildElement<FilledRectangleElement>();
            backdropLeft = this.AddChildElement<FilledRectangleElement>();
            backdropRight = this.AddChildElement<FilledRectangleElement>();



            Setup();
        }

        private void Setup()
        {
            size = this.GetSize();


            float contentHeight = content is TextElement textElement ? textElement.CalculatedSize().Y : content.GetSize().Y;
            float contentWidth = content is TextElement tx ? tx.CalculatedSize().X : content.GetSize().X;
            float buttonHeight = (this.buttons.Count > 0 ? 80 : 10);
        
            
            float calcHeight = 80 + contentHeight + buttonHeight;
            float calcWidth = 20 + contentWidth;
            size = new TVVector(calcWidth, calcHeight);
            titleBarElement.Set<SizeTrait>(new TVVector(size.X, 40));
            
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

            justSpawned = true;
        }
        private bool justSpawned = false;
        public override void Update(Element parent = null)
        {
            base.Update(parent);

            

            Vector2 calculatedModalSize = size.AsXna;
            Vector2 actualPosition = this.GetActualPosition().AsXna;
            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            float opacity = BeingDragged ? 0.15f : 0.35f;

            float nineGridSize = 32;

            
            backdropTopLeft.Set<PositionTrait>(new TVVector(- nineGridSize, - nineGridSize));
            backdropTopLeft.Set<SizeTrait>(new TVVector(nineGridSize, nineGridSize));
            backdropTopLeft.Set<BackgroundFillTrait>(Resources.StaticResources.Theme.NineGridTopLeft);

            backdropTop.Set<PositionTrait>(new TVVector(0, -nineGridSize));
            backdropTop.Set<SizeTrait>(new TVVector(calculatedModalSize.X, nineGridSize));
            backdropTop.Set<BackgroundFillTrait>(Resources.StaticResources.Theme.NineGridTop);

            backdropTopRight.Set<PositionTrait>(new TVVector(new Vector2(calculatedModalSize.X, -nineGridSize)));
            backdropTopRight.Set<SizeTrait>(new TVVector(nineGridSize, nineGridSize));
            backdropTopRight.Set<BackgroundFillTrait>(Resources.StaticResources.Theme.NineGridTopRight);

            backdropLeft.Set<PositionTrait>(new TVVector(- nineGridSize, 0));
            backdropLeft.Set<SizeTrait>(new TVVector(nineGridSize, calculatedModalSize.Y));
            backdropLeft.Set<BackgroundFillTrait>(Resources.StaticResources.Theme.NineGridLeft);


            backdropRight.Set<PositionTrait>(new TVVector(calculatedModalSize.X, 0));
            backdropRight.Set<SizeTrait>(new TVVector(nineGridSize, calculatedModalSize.Y));
            backdropRight.Set<BackgroundFillTrait>(Resources.StaticResources.Theme.NineGridRight);

            backdropBottomLeft.Set<PositionTrait>(new TVVector(-nineGridSize, calculatedModalSize.Y));
            backdropBottomLeft.Set<SizeTrait>(new TVVector(nineGridSize, nineGridSize));
            backdropBottomLeft.Set<BackgroundFillTrait>(Resources.StaticResources.Theme.NineGridBottomLeft);

            backdropBottom.Set<PositionTrait>(new TVVector(0, calculatedModalSize.Y));
            backdropBottom.Set<SizeTrait>(new TVVector(calculatedModalSize.X, nineGridSize));
            backdropBottom.Set<BackgroundFillTrait>(Resources.StaticResources.Theme.NineGridBottom);

            backdropBottomRight.Set<PositionTrait>(new TVVector(new Vector2(calculatedModalSize.X, calculatedModalSize.Y)));
            backdropBottomRight.Set<SizeTrait>(new TVVector(nineGridSize, nineGridSize));
            backdropBottomRight.Set<BackgroundFillTrait>(Resources.StaticResources.Theme.NineGridBottomRight);


            if (justSpawned)
            {
                this.MoveToFront();
                justSpawned = false;
            }
        }
        
    }
}
