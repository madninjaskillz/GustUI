using System;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements;

/// <summary>
/// A vertical scrollbar for panning a viewport over taller content — the
/// vertical twin of <see cref="HorizontalScrollbarElement"/>, sharing its
/// model exactly: a pure view of <see cref="ContentSize"/>,
/// <see cref="ViewportSize"/> and <see cref="ScrollPosition"/> (all in content
/// units), thumb drag via pointer capture, track press pages one viewport
/// toward the pointer (repeating while held), programmatic
/// <see cref="ScrollPosition"/> sets never echo <see cref="OnUserScroll"/>,
/// and <see cref="IsDragging"/> lets owners with their own scroll agenda
/// yield while the user holds the thumb.
///
/// (Rewritten 2026-08: the previous version was a Func-injected prototype
/// only <see cref="VerticalScrollElement"/> consumed; both now share the
/// scrollbar API introduced with the horizontal bar.)
/// </summary>
[ElementTraits(typeof(PositionTrait), typeof(SizeTrait), typeof(OnMousePress), typeof(OnMouseButtonHeldDown), typeof(OnMouseRelease))]
public class VerticalScrollbarElement : Element
{
    private const double PageRepeatFirstMs = 350.0;
    private const double PageRepeatMs = 120.0;

    public Color TrackColor { get; set; } = new Color(24, 24, 30);
    public Color ThumbColor { get; set; } = new Color(64, 64, 78);
    public Color ThumbHoverColor { get; set; } = new Color(84, 84, 100);
    public Color ThumbDragColor { get; set; } = new Color(110, 145, 235);

    /// <summary>Shortest the thumb may get, in bar pixels (stays grabbable however tall the content).</summary>
    public int MinThumbLength { get; set; } = 24;

    /// <summary>Horizontal inset of the thumb inside the track.</summary>
    public int ThumbMargin { get; set; } = 2;

    /// <summary>Fraction of the viewport a track-click pages by.</summary>
    public float PageFraction { get; set; } = 0.9f;

    /// <summary>Raised only for user gestures (thumb drag / track paging), with the new ScrollPosition.</summary>
    public Action<float> OnUserScroll;

    /// <summary>True while the user is holding the thumb (owners running their own scrolling should yield).</summary>
    public bool IsDragging { get; private set; }

    private float contentSize = 1f;
    private float viewportSize = 1f;
    private float scrollPosition;

    public float ContentSize
    {
        get => contentSize;
        set { contentSize = Math.Max(0f, value); ClampScroll(); }
    }

    public float ViewportSize
    {
        get => viewportSize;
        set { viewportSize = Math.Max(1f, value); ClampScroll(); }
    }

    /// <summary>Scroll offset in content units, clamped to [0, MaxScroll]. Programmatic sets do NOT raise OnUserScroll.</summary>
    public float ScrollPosition
    {
        get => scrollPosition;
        set => scrollPosition = MathHelper.Clamp(value, 0f, MaxScroll);
    }

    public float MaxScroll => Math.Max(0f, contentSize - viewportSize);

    /// <summary>See HorizontalScrollbarElement.IsNeeded's matching doc
    /// comment — same fix, same reasoning.</summary>
    public bool IsNeeded => contentSize > viewportSize;

    private float dragGrabOffset;      // pointer y - thumb top at grab time
    private bool paging;
    private int pageDirection;
    private DateTime nextPageAt = DateTime.MinValue;
    private float pagePointerY;

    public VerticalScrollbarElement()
    {
        ElementTrait<OnMousePress>().Set(new TVEvent<ClickEventArgs>(HandlePress));
        ElementTrait<OnMouseButtonHeldDown>().Set(new TVEvent<ClickEventArgs>(HandleHeld));
        ElementTrait<OnMouseRelease>().Set(new TVEvent<ClickEventArgs>(HandleRelease));
    }

    private void HandlePress(ClickEventArgs args)
    {
        if (MaxScroll <= 0f)
        {
            return; // nothing to scroll: inert
        }

        float trackTop = this.GetActualXnaPosition().Y;
        float mouseY = args.MouseState.Y;
        GetThumbMetrics(out float thumbY, out float thumbLength);

        CapturePointer();

        if (mouseY >= trackTop + thumbY && mouseY <= trackTop + thumbY + thumbLength)
        {
            IsDragging = true;
            dragGrabOffset = mouseY - (trackTop + thumbY);
        }
        else
        {
            paging = true;
            pagePointerY = mouseY;
            pageDirection = mouseY < trackTop + thumbY ? -1 : 1;
            Page();
            nextPageAt = DateTime.UtcNow.AddMilliseconds(PageRepeatFirstMs);
        }
    }

    private void HandleHeld(ClickEventArgs args)
    {
        if (IsDragging)
        {
            float trackTop = this.GetActualXnaPosition().Y;
            GetThumbMetrics(out _, out float thumbLength);
            float span = this.GetSize().Y - thumbLength;
            if (span <= 0f)
            {
                return;
            }

            float thumbY = MathHelper.Clamp(args.MouseState.Y - dragGrabOffset - trackTop, 0f, span);
            float target = thumbY / span * MaxScroll;
            if (target != scrollPosition)
            {
                scrollPosition = target;
                OnUserScroll?.Invoke(scrollPosition);
            }
        }
        else if (paging)
        {
            pagePointerY = args.MouseState.Y;
            if (DateTime.UtcNow >= nextPageAt)
            {
                Page();
                nextPageAt = DateTime.UtcNow.AddMilliseconds(PageRepeatMs);
            }
        }
    }

    private void HandleRelease(ClickEventArgs args)
    {
        IsDragging = false;
        paging = false;
    }

    /// <summary>One page toward the held pointer; stops when the thumb reaches it (no oscillation).</summary>
    private void Page()
    {
        float trackTop = this.GetActualXnaPosition().Y;
        GetThumbMetrics(out float thumbY, out float thumbLength);
        if (pageDirection < 0 && pagePointerY >= trackTop + thumbY)
        {
            return;
        }

        if (pageDirection > 0 && pagePointerY <= trackTop + thumbY + thumbLength)
        {
            return;
        }

        float target = MathHelper.Clamp(
            scrollPosition + pageDirection * viewportSize * PageFraction, 0f, MaxScroll);
        if (target != scrollPosition)
        {
            scrollPosition = target;
            OnUserScroll?.Invoke(scrollPosition);
        }
    }

    private void ClampScroll()
    {
        scrollPosition = MathHelper.Clamp(scrollPosition, 0f, MaxScroll);
    }

    /// <summary>Thumb geometry in element-local pixels.</summary>
    private void GetThumbMetrics(out float thumbY, out float thumbLength)
    {
        float trackLength = this.GetSize().Y;
        if (contentSize <= viewportSize || trackLength <= 0f)
        {
            thumbY = 0f;
            thumbLength = trackLength;
            return;
        }

        thumbLength = MathHelper.Clamp(trackLength * (viewportSize / contentSize), Math.Min(MinThumbLength, trackLength), trackLength);
        float span = trackLength - thumbLength;
        thumbY = MaxScroll > 0f ? span * (scrollPosition / MaxScroll) : 0f;
    }

    public override void Draw()
    {
        var manager = Resources.StaticResources.DrawManager;
        Vector2 pos = this.GetActualXnaPosition();
        Vector2 size = this.GetSize().AsXna;
        if (IsNeeded && size.Y >= 1f && size.X >= 2f)
        {
            manager.DrawFilledRectangle(new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y), TrackColor);

            GetThumbMetrics(out float thumbY, out float thumbLength);
            Color thumb = ThumbColor;
            if (IsDragging)
            {
                thumb = ThumbDragColor;
            }
            else if (Resources.StaticResources.InputManager.GetElementState(this) == Managers.InputManager.ElementState.Hovered)
            {
                thumb = ThumbHoverColor;
            }

            int inset = (int)MathHelper.Clamp(ThumbMargin, 0f, size.X / 2f - 1f);
            manager.DrawFilledRectangle(
                new Rectangle((int)pos.X + inset, (int)(pos.Y + thumbY), (int)size.X - inset * 2, (int)thumbLength),
                thumb);
        }

        base.Draw();
    }
}
