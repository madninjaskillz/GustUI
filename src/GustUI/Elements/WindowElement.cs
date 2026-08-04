using GustUI.Attributes;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GustUI.Elements;

[ElementTraits(typeof(SizeTrait), typeof(PositionTrait), typeof(ChildrenTrait))]
public class WindowElement : FilledRectangleElement
{
    private readonly GameWindow gameWindow;

    /// <summary>
    /// Divides <see cref="GameWindow.ClientBounds"/> by this factor when
    /// computing the window's logical <see cref="SizeTrait"/>. Confirmed
    /// live on KNI's Blazor canvas backend: ClientBounds/BackBufferWidth/
    /// Viewport all mirror the &lt;canvas&gt; element's width/height attribute
    /// (the PHYSICAL backing store), while mouse coordinates arriving at
    /// InputManager (both the polled Mouse.GetState() path and the pushed-
    /// edge path) are the browser's native CSS-pixel event coordinates,
    /// UNSCALED — the two are different numbers once a host inflates that
    /// attribute for HiDPI supersampling. Diving ClientBounds back down
    /// here keeps GustUI's own layout (every constant in this codebase is a
    /// fixed pixel unit) and its hit-testing in the SAME space mouse
    /// coordinates naturally already arrive in — with this unset (1, the
    /// default), a host that inflates the canvas attribute makes over half
    /// its own UI permanently unclickable, since SizeTrait/hit-testing
    /// bounds grow past where any real mouse position can ever reach.
    /// Default 1 = today's exact behavior (desktop platforms, or a host
    /// that never sets this). Call <see cref="SyncClientSize"/> after
    /// changing this outside the constructor so SizeTrait picks it up
    /// immediately rather than waiting for the next genuine
    /// <see cref="GameWindow.ClientSizeChanged"/>.
    /// </summary>
    public float DevicePixelRatio { get; set; } = 1f;

    public WindowElement(SpriteBatch spriteBatch, GameWindow gameWindow, GraphicsDevice graphicsDevice, VirtualContent virtualContent, ContentManager content) : base()
    {
        this.gameWindow = gameWindow;
        Resources.StaticResources = new Resources(spriteBatch, graphicsDevice, virtualContent, content, this);
        Resources.StaticResources.Theme = new Theme();

        gameWindow.ClientSizeChanged += GameWindow_ClientSizeChanged;
        SyncClientSize();

        this.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.DarkBlue,Color.Purple, Direction.Vertically));
        this.Set<BorderFillTrait>(new TVBorderColorFill(Color.White));
        this.Set<BorderSizeTrait, TVInt>(new TVInt(0));

        AddChildElement<BackdropElement>();
    }

    /// <summary>Recomputes SizeTrait from the current ClientBounds and
    /// <see cref="DevicePixelRatio"/> — idempotent (Set only fires
    /// trait-changed events on an actual value change), safe to call every
    /// frame.</summary>
    public void SyncClientSize()
    {
        float dpr = DevicePixelRatio > 0f ? DevicePixelRatio : 1f;
        ElementTrait<SizeTrait>().Set(new TVVector(gameWindow.ClientBounds.Width / dpr, gameWindow.ClientBounds.Height / dpr));
    }

    private void GameWindow_ClientSizeChanged(object sender, EventArgs e)
    {
        SyncClientSize();
        Debug.WriteLine(ElementTrait<SizeTrait>().Value());
    }
}

