using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements;

[ElementTraits(typeof(PositionTrait), typeof(SizeTrait))]
public class NativeElement : Element
{
    public NativeElement()
    {
        Resources.StaticResources.DrawOOPManager.Register(this, DrawOutOfProcess);
    }

    public NativeElement(int width, int height)
    {
        Set<SizeTrait>(new TVVector(width, height));
        Resources.StaticResources.DrawOOPManager.Register(this, DrawOutOfProcess);
    }

    public override void Dispose()
    {
        Resources.StaticResources.DrawOOPManager.Unregister(this);
        base.Dispose();
    }

    private RenderTarget2D renderTarget;

    public override void Update(Element parent = null)
    {
        if (ElementTrait<SizeTrait>().Value().X > 0 && ElementTrait<SizeTrait>().Value().Y > 0) { 
        if (renderTarget == null || renderTarget.Width != ElementTrait<SizeTrait>().Value().X || renderTarget.Height != ElementTrait<SizeTrait>().Value().Y)
        {
            Log.This($"Creating new render target - {(int)ElementTrait<SizeTrait>().Value().X}, {(int)ElementTrait<SizeTrait>().Value().Y}");
            renderTarget = new RenderTarget2D(Resources.StaticResources.GraphicsDevice, (int)ElementTrait<SizeTrait>().Value().X, (int)ElementTrait<SizeTrait>().Value().Y);
        }
    }
        base.Update(parent);
    }
    public override void Draw()
    {
        
        TVVector actualPosition = this.GetActualPosition();

        TVVector size = ElementTrait<SizeTrait>().Value();

        if (renderTarget != null)
        {
            Resources.StaticResources.DrawManager.Draw(renderTarget, this.GetActualPosition().AsXna, Color.White);
        }
        
        base.Draw();
    }

    public override void DrawOutOfProcess()
    {
        if (renderTarget != null)
        {
            Resources.StaticResources.GraphicsDevice.SetRenderTarget(renderTarget);
            Resources.StaticResources.GraphicsDevice.Clear(Color.Green);

            Resources.StaticResources.DrawManager.Begin();
            DrawToRenderTarget();
            Resources.StaticResources.DrawManager.End();

            Resources.StaticResources.GraphicsDevice.SetRenderTarget(null);
        }
        base.DrawOutOfProcess();
    }

    public virtual void DrawToRenderTarget()
    {
    }
}

