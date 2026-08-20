using System;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements;

/// <summary>
/// A horizontal scrollbar for panning a viewport over wider content. The
/// thumb's length is the viewport/content ratio; dragging it uses pointer
/// capture (fast drags that leave the element keep working — same input model
/// as <see cref="SliderElement"/>/<see cref="KnobElement"/>); pressing the
/// track pages one viewport toward the pointer, repeating while held.
///
/// The element is a pure view of three numbers — <see cref="ContentSize"/>,
/// <see cref="ViewportSize"/>, <see cref="ScrollPosition"/> — all in content
/// units (pixels of the scrolled world, not of the bar). Two-way sync is
/// feedback-safe by design: setting <see cref="ScrollPosition"/>
/// programmatically (e.g. the view panning/zooming on its own) just moves the
/// thumb, while user gestures on the bar raise <see cref="OnUserScroll"/>.
/// <see cref="IsDragging"/> lets an owner with its own scroll agenda (e.g.
/// playback follow-scroll) yield while the user holds the thumb.
/// </summary>
[ElementTraits(typeof(PositionTrait), typeof(SizeTrait), typeof(OnMousePress), typeof(OnMouseButtonHeldDown), typeof(OnMouseRelease))]
public class HorizontalScrollbarElement : Element
{
    private const double PageRepeatFirstMs = 350.0;
    private const double PageRepeatMs = 120.0;

    public Color TrackColor { get; set; } = new Color(24, 24, 30);
    public Color ThumbColor { get; set; } = new Color(64, 64, 78);
    public Color ThumbHoverColor { get; set; } = new Color(84, 84, 100);
    public Color ThumbDragColor { get; set; } = new Color(110, 145, 235);

    /// <summary>Shortest the thumb may get, in bar pixels (stays grabbable however long the content).</summary>
    public int MinThumbLength { get; set; } = 24;

    /// <summary>Vertical inset of the thumb inside the track.</summary>
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

    /// <summary>False once ContentSize fits within ViewportSize with
    /// nothing to scroll — Draw() skips rendering entirely in that case
    /// (2026-08-17: previously always drew a full-length, permanently
    /// "disabled-looking" track+thumb even with nothing to scroll — dead
    /// chrome, not an intentional design). Press handling was already
    /// inert then (HandlePress's own MaxScroll &lt;= 0f early-return); this
    /// just stops it from drawing too. A caller that also wants to reclaim
    /// the bar's own reserved layout space when hidden should check this
    /// too — GustUI can't do that part itself, it doesn't own the
    /// caller's layout.</summary>
    public bool IsNeeded => contentSize > viewportSize;

    private float dragGrabOffset;      // pointer x - thumb left at grab time
    private bool paging;
    private int pageDirection;
    private DateTime nextPageAt = DateTime.MinValue;
    private float pagePointerX;

    public HorizontalScrollbarElement()
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

        float trackLeft = this.GetActualXnaPosition().X;
        float mouseX = args.MouseState.X;
        GetThumbMetrics(out float thumbX, out float thumbLength);

        CapturePointer();

        if (mouseX >= trackLeft + thumbX && mouseX <= trackLeft + thumbX + thumbLength)
        {
            IsDragging = true;
            dragGrabOffset = mouseX - (trackLeft + thumbX);
        }
        else
        {
            paging = true;
            pagePointerX = mouseX;
            pageDirection = mouseX < trackLeft + thumbX ? -1 : 1;
            Page();
            nextPageAt = DateTime.UtcNow.AddMilliseconds(PageRepeatFirstMs);
        }
    }

    private void HandleHeld(ClickEventArgs args)
    {
        if (IsDragging)
        {
            float trackLeft = this.GetActualXnaPosition().X;
            GetThumbMetrics(out _, out float thumbLength);
            float span = this.GetSize().X - thumbLength;
            if (span <= 0f)
            {
                return;
            }

            float thumbX = MathHelper.Clamp(args.MouseState.X - dragGrabOffset - trackLeft, 0f, span);
            float target = thumbX / span * MaxScroll;
            if (target != scrollPosition)
            {
                scrollPosition = target;
                OnUserScroll?.Invoke(scrollPosition);
            }
        }
        else if (paging)
        {
            pagePointerX = args.MouseState.X;
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
        float trackLeft = this.GetActualXnaPosition().X;
        GetThumbMetrics(out float thumbX, out float thumbLength);
        if (pageDirection < 0 && pagePointerX >= trackLeft + thumbX)
        {
            return;
        }

        if (pageDirection > 0 && pagePointerX <= trackLeft + thumbX + thumbLength)
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
    private void GetThumbMetrics(out float thumbX, out float thumbLength)
    {
        float trackLength = this.GetSize().X;
        if (contentSize <= viewportSize || trackLength <= 0f)
        {
            thumbX = 0f;
            thumbLength = trackLength;
            return;
        }

        thumbLength = MathHelper.Clamp(trackLength * (viewportSize / contentSize), Math.Min(MinThumbLength, trackLength), trackLength);
        float span = trackLength - thumbLength;
        thumbX = MaxScroll > 0f ? span * (scrollPosition / MaxScroll) : 0f;
    }

    public override void Draw()
    {
        var manager = Resources.StaticResources.DrawManager;
        Vector2 pos = this.GetActualXnaPosition();
        Vector2 size = this.GetSize().AsXna;
        if (IsNeeded && size.X >= 1f && size.Y >= 2f)
        {
            manager.DrawFilledRectangle(new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y), TrackColor);

            GetThumbMetrics(out float thumbX, out float thumbLength);
            Color thumb = ThumbColor;
            if (IsDragging)
            {
                thumb = ThumbDragColor;
            }
            else if (Resources.StaticResources.InputManager.GetElementState(this) == Managers.InputManager.ElementState.Hovered)
            {
                thumb = ThumbHoverColor;
            }

            int inset = (int)MathHelper.Clamp(ThumbMargin, 0f, size.Y / 2f - 1f);
            manager.DrawFilledRectangle(
                new Rectangle((int)(pos.X + thumbX), (int)pos.Y + inset, (int)thumbLength, (int)size.Y - inset * 2),
                thumb);
        }

        base.Draw();
    }
}
