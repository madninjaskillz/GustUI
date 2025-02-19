﻿using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace GustUI.Elements
{
    [ElementTraits(typeof(BodyTextTrait), typeof(FontTrait), typeof(ForegroundColorTrait), typeof(OnMouseButtonHeldDown))]
    public class ModalWindowElement : FilledRectangleElement
    {
        private ModalTitleBarElement titleBarElement;
        private List<BasicButtonElement> buttons = new List<BasicButtonElement>();
        private FilledRectangleElement buttonBackgroundElement;


        public bool FitModalToContent { get; private set; } = true;
        private Element content;
        public ModalWindowElement()
        {

            titleBarElement = this.AddChildElement<ModalTitleBarElement>();
            buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();

            Setup();
        }
        public ModalWindowElement(string title, string body, List<BasicButtonElement> buttons = null, TVVector position = null, TVVector size = null)
        {
            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<ForegroundColorTrait>(new TVColor(Color.Black));
            
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
            content.Set<ForegroundColorTrait>(new TVColor(Color.Black));
            content.Set<SizeTrait>(new TVVector(size.X - 20, 0));
            content.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment() { Alignment = HorizontalAlignment.Left });

            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();
            }

            Setup();
        }

        public ModalWindowElement(string title, Element body, List<BasicButtonElement> buttons = null, TVVector position = null, TVVector size = null)
        {

            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<ForegroundColorTrait>(new TVColor(Color.Black));
            
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
                new TVVector(size != null ? size.X : 400, 40));

            AddChildElement(titleBarElement);

            this.content = body;
            this.AddChild(this.content, "content");

            content.Set<PositionTrait>(new TVVector(0, 0));


            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();
            }

            Setup();
        }

        private void Setup()
        {
            //Set<BackgroundFillTrait>(new TVBlurFill(0.125f, new TVFillSimpleGradient(Color.White * 0.8f, new Color(200, 200, 200), Direction.Vertically) { Opacity = 0.25f }));
            Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.White * 0.95f, new Color(200, 200, 200), Direction.Vertically) { Opacity = 0.998f });
            var size = this.GetSize();
            Set<BorderFillTrait>(new TVBorder9Grid());

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
        private int maximizeAttemptCount = 0;
        public override void Update(Element parent = null)
        {
            base.Update(parent);

            if (FitModalToContent)
            {
                float contentHeight = content is TextElement textElement ? textElement.CalculatedSize().Y : content.GetSize().Y;
                float contentWidth = content is TextElement tx ? tx.CalculatedSize().X : content.GetSize().X;
                float buttonHeight = (this.buttons.Count > 0 ? 80 : 10);

                this.Set<SizeTrait>(new TVVector(contentWidth, 40 + contentHeight + buttonHeight));
            }

            var size = this.GetSize();

            Vector2 calculatedModalSize = size.AsXna;
            Vector2 actualPosition = this.GetActualPosition().AsXna;
            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            float opacity = BeingDragged ? 0.15f : 0.35f;

            bool limitToScreenSpace = false;

            if (limitToScreenSpace)
            {
                if (actualPosition.X + calculatedModalSize.X > windowSize.X)
                {
                    Set<PositionTrait>(new TVVector(windowSize.X - calculatedModalSize.X, actualPosition.Y));
                }

                if (actualPosition.Y + calculatedModalSize.Y > windowSize.Y)
                {
                    Set<PositionTrait>(new TVVector(actualPosition.X, windowSize.Y - calculatedModalSize.Y));
                }

                float topLimit = 0;
                if (Resources.StaticResources.RootWindow.Children.Items.Any(x => x is FruitMenuElement))
                {
                    topLimit = Resources.StaticResources.RootWindow.Children.Items.First(x => x is FruitMenuElement).GetSize().Y;
                }

                if (actualPosition.Y < topLimit)
                {
                    Set<PositionTrait>(new TVVector(actualPosition.X, topLimit));
                    
                }

                if (actualPosition.X < 0)
                {
                    Set<PositionTrait>(new TVVector(0, actualPosition.Y));
                }
            }
            else
            {
                float topLimit = 0;
                if (Resources.StaticResources.RootWindow.Children.Items.Any(x => x is FruitMenuElement))
                {
                    topLimit = Resources.StaticResources.RootWindow.Children.Items.First(x => x is FruitMenuElement).GetSize().Y;
                }

                if (!isFullScreen)
                {
                    if (actualPosition.Y < topLimit)
                    {
                        Set<PositionTrait>(new TVVector(actualPosition.X, topLimit));
                        maximizeAttemptCount++;
                        if (maximizeAttemptCount > 10)
                        {
                            ToggleFullScreen();
                            maximizeAttemptCount = 0;
                        }
                    }
                    else
                    {
                        maximizeAttemptCount = 0;
                    }
                }

            }

            if (justSpawned)
            {
                this.MoveToFront();
                justSpawned = false;
            }

            if (ElementTrait<BorderFillTrait>().Value() is TVBorder9Grid nineGrid)
            {
                if (BeingDragged)
                {
                    nineGrid.NineGridSize = MathHelper.Lerp(nineGrid.NineGridSize, 32, 0.2f);
                }
                else
                {
                    nineGrid.NineGridSize = MathHelper.Lerp(nineGrid.NineGridSize, 16, 0.2f);
                }
            }

            //if (isFullScreen)
            //{
            //    Set<SizeTrait>(new TVVector(Resources.StaticResources.RootWindow.GetSize().X, Resources.StaticResources.RootWindow.GetSize().Y - 40));
            //    Set<PositionTrait>(new TVVector(0, 40));
            //}

           
            buttonBackgroundElement.Set<PositionTrait>(new TVVector(0, size.Y - 80));
            buttonBackgroundElement.Set<SizeTrait>(new TVVector(size.X, 80));
            float xPos = size.X - 20;
            foreach (var button in buttonBackgroundElement.Children.Items)
            {
                button.Set<PositionTrait>(new TVVector(xPos - button.GetSize().X, 40 - (button.GetSize().Y / 2f)));
                xPos -= button.GetSize().X + 20;
            }

            //content.Set<PositionTrait>(new TVVector((size.X / 2f) - (content.GetSize().X / 2f), (size.Y / 2f) - (content.GetSize().Y / 2f)));
            content.Set<PositionTrait>(new TVVector((size.X / 2f) - (content.GetSize().X / 2f), 40));

        }

    }
}
