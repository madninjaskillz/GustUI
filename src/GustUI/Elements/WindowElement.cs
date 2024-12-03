using GustUI.Attributes;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GustUI.Elements;

[ElementTraits(typeof(SizeTrait), typeof(PositionTrait), typeof(ChildrenTrait))]
public class WindowElement : FilledRectangleElement
{
    public WindowElement(GameWindow gameWindow, GraphicsDevice graphicsDevice, VirtualContent content) : base()
    {
        Resources.StaticResources = new Resources(graphicsDevice, content, this);

        gameWindow.ClientSizeChanged += GameWindow_ClientSizeChanged;
        ElementTrait<SizeTrait>().Set(new TVVector(gameWindow.ClientBounds.Width, gameWindow.ClientBounds.Height));

        this.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.DarkBlue,Color.Purple, Direction.Vertically));
        this.Set<BorderColorTrait, TVColor>(new TVColor(Color.White));
        this.Set<BorderSizeTrait, TVInt>(new TVInt(0));
    }

    private void GameWindow_ClientSizeChanged(object sender, EventArgs e)
    {
        GameWindow gameWindow = (GameWindow)sender;

        ElementTrait<SizeTrait>().Set(new TVVector(gameWindow.ClientBounds.Width, gameWindow.ClientBounds.Height));

        Debug.WriteLine(ElementTrait<SizeTrait>().Value());
    }
}

