using System;
using System.Collections.Generic;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements;

/// <summary>Which corner toasts gather in.</summary>
public enum ToastCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// A live toast, handed back to whoever raised it and to the builder that
/// draws it — so a toast's own close button has something to call, and a
/// long-running one can be updated or taken away by the code that owns it.
/// </summary>
public sealed class Toast
{
    internal Toast(TimeSpan? lifetime)
    {
        Lifetime = lifetime;
    }

    /// <summary>The wrapper the host positions. Set once, by the host, after
    /// the caller's builder has run — the builder needs the Toast to wire its
    /// own close button, so neither can be finished before the other starts.</summary>
    public Element Content { get; internal set; }

    /// <summary>Null means it stays until something dismisses it.</summary>
    public TimeSpan? Lifetime { get; internal set; }

    public bool IsOpen { get; internal set; } = true;

    /// <summary>
    /// Resting opacity, 0..1. Defaults to fully opaque, which is what a toast
    /// that is only up for a few seconds wants.
    ///
    /// MULTIPLIED INTO the enter/exit fade rather than replacing it, so a
    /// half-opaque toast still animates all the way from nothing up to its own
    /// resting level and back down. Setting Content.Opacity directly cannot
    /// work: the host overwrites it every frame to drive that fade.
    ///
    /// For a toast that STAYS UP -- one showing transport or progress rather
    /// than reporting an event -- so it reads as a layer over the app instead
    /// of a panel bolted onto it.
    /// </summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>
    /// Holds its place in the stack and is never dropped to make room.
    ///
    /// Two things follow, and a pinned toast needs both. It is exempt from
    /// MaxVisible culling, which takes the OLDEST toast -- and a toast that
    /// stays up for a long time is by definition the oldest, so the transport
    /// panel was being thrown away by the fourth notification to arrive during
    /// a song. And it sits AT the corner rather than being pushed away from it
    /// as newer toasts land, because something that stays on screen should
    /// stay where it was put; transient toasts stack above it.
    ///
    /// It still expires, and still dismisses. Pinned is about not being
    /// evicted by OTHER toasts, not about being permanent.
    /// </summary>
    public bool Pinned { get; set; }

    /// <summary>Seconds this toast has been up, used for its own expiry and
    /// for the entrance animation.</summary>
    internal double Age { get; set; }

    /// <summary>Where it is being drawn, so the stack can slide rather than
    /// jump when the one below it goes away.</summary>
    internal float DrawnAt { get; set; } = float.NaN;

    internal bool Closing { get; set; }

    internal double ClosingFor { get; set; }

    /// <summary>Takes this toast away, with its exit animation. Idempotent —
    /// a toast that expires while its close button is being pressed must not
    /// be a problem.</summary>
    public void Dismiss()
    {
        if (IsOpen)
        {
            Closing = true;
        }
    }

    /// <summary>Restarts the countdown — for a toast that says the same thing
    /// again rather than stacking a duplicate.</summary>
    public void Renew()
    {
        Age = 0;
        Closing = false;
        ClosingFor = 0;
    }

    /// <summary>
    /// Gives a toast that was staying a lifetime after all, starting now.
    ///
    /// The end of a long job: it was sticky because it had no idea how long it
    /// would take, and now it is finished and should behave like any other
    /// "that worked" — say so, then go. Without this the only options are
    /// leaving a completed job on screen or making it vanish the instant it
    /// completes, and the second is worse, because the user never sees the
    /// outcome of the thing they were watching.
    /// </summary>
    public void ExpireIn(TimeSpan lifetime)
    {
        Lifetime = lifetime;
        Age = 0;
        Closing = false;
        ClosingFor = 0;
    }
}

/// <summary>
/// The MECHANISM behind toasts, and none of the look.
///
/// This owns the things every toast system has to get right and no
/// application should have to solve twice: stacking several at once,
/// expiring the timed ones, sliding the survivors into the gap when one
/// goes, animating entrances and exits, keeping the stack pinned to its
/// corner as the window resizes, and dropping the oldest when too many
/// arrive at once.
///
/// It owns NO appearance. A caller hands it a finished element — its own
/// colours, padding, icon, typography, close affordance — and this positions
/// and animates it. That split is deliberate: "what a notification looks like"
/// is one of the strongest bits of an app's identity, and a toolkit that
/// answers it makes every app that uses the toolkit look the same.
///
/// Two kinds, which is the whole taxonomy: one with a lifetime that takes
/// itself away, and one without that stays until dismissed. Anything that
/// needs an answer is a modal, not a toast.
/// </summary>
public sealed class ToastHost
{
    /// <summary>How long the slide/fade in and out take. Short — a toast that
    /// makes an entrance is worse than one that just appears.</summary>
    private const double EnterSeconds = 0.18;
    private const double ExitSeconds = 0.16;

    /// <summary>How far a toast travels on the way in, along the axis it is
    /// stacked on.</summary>
    private const float SlideDistance = 24f;

    private readonly WindowElement window;
    private readonly List<Toast> toasts = new();

    /// <summary>Draw order, rebuilt each frame — pinned toasts first. A field
    /// rather than a local so the per-frame layout allocates nothing.</summary>
    private readonly List<Toast> ordered = new();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    private double lastSeconds;
    private int nextId;

    public ToastHost(WindowElement window)
    {
        this.window = window;
    }

    public ToastCorner Corner { get; set; } = ToastCorner.BottomRight;

    /// <summary>Gap between the stack and the window edges.</summary>
    public float Margin { get; set; } = 16f;

    /// <summary>Gap between stacked toasts.</summary>
    public float Spacing { get; set; } = 8f;

    /// <summary>Pixels of app chrome along the corner's own edge — a status
    /// bar, a transport strip — that toasts must clear. Caller-supplied, since
    /// GustUI knows nothing about app chrome.</summary>
    public float EdgeInset { get; set; }

    /// <summary>
    /// Most toasts on screen at once. Beyond this the oldest is dismissed as a
    /// new one arrives: a column of them tall enough to reach the top of the
    /// window has stopped being a notification and become an obstruction.
    /// </summary>
    public int MaxVisible { get; set; } = 4;

    public IReadOnlyList<Toast> Live => toasts;

    /// <summary>
    /// Raises a toast. <paramref name="build"/> receives the Toast so the
    /// content it returns can carry its own dismiss affordance — the two
    /// cannot be built in either order alone.
    ///
    /// A null <paramref name="lifetime"/> stays until dismissed.
    /// </summary>
    public Toast Show(Func<Toast, Element> build, TimeSpan? lifetime)
    {
        if (build == null || window == null)
        {
            return null;
        }

        var toast = new Toast(lifetime);
        Element content = build(toast);
        if (content == null)
        {
            return null;
        }

        var wrapper = new FilledRectangleElement(0, 0, 1, 1, new TVFillSolidColor(Color.Transparent))
        {
            SizeFitsChildren = true,

            // Over everything, including modals (ModalDepth is 60000) — a
            // toast a dialog can hide is a toast nobody reads. Depth alone
            // settles it now that the batch draws in strict order.
            Depth = 90000,
        };

        wrapper.AddChild(content, "toast-content");
        window.AddChild(wrapper, "toast-" + nextId++);

        toast.Content = wrapper;
        toasts.Add(toast);

        // One per arrival, oldest first: dismissing is animated, so the
        // over-count resolves itself over the next few frames rather than
        // needing a loop that would take several away at once.
        //
        // The oldest UNPINNED one. Pinned toasts still count towards the limit
        // -- they take up just as much room, and the limit is about the stack
        // becoming an obstruction -- but they are never the one thrown away. A
        // stack that is entirely pinned drops nothing, which is what pinning
        // means.
        if (toasts.Count > Math.Max(1, MaxVisible))
        {
            for (int i = 0; i < toasts.Count; i++)
            {
                if (!toasts[i].Pinned && !toasts[i].Closing)
                {
                    toasts[i].Dismiss();
                    break;
                }
            }
        }

        return toast;
    }

    /// <summary>Dismisses everything, immediately and without animation — for
    /// a screen teardown, where the toasts' window is going away anyway.</summary>
    public void Clear()
    {
        foreach (Toast toast in toasts)
        {
            toast.Content?.Kill();
            toast.IsOpen = false;
        }

        toasts.Clear();
    }

    /// <summary>
    /// Ages, expires and lays out the stack. Call once per frame — a toast
    /// host has no frame hook of its own, and neither does the window it
    /// hangs off.
    /// </summary>
    public void Pump()
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = lastSeconds < 0 ? 0 : now - lastSeconds;
        lastSeconds = now;

        if (dt <= 0 || toasts.Count == 0)
        {
            return;
        }

        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            Toast toast = toasts[i];
            toast.Age += dt;

            if (!toast.Closing && toast.Lifetime.HasValue && toast.Age >= toast.Lifetime.Value.TotalSeconds)
            {
                toast.Closing = true;
            }

            if (toast.Closing)
            {
                toast.ClosingFor += dt;
                if (toast.ClosingFor >= ExitSeconds)
                {
                    toast.Content?.Kill();
                    toast.IsOpen = false;
                    toasts.RemoveAt(i);
                }
            }
        }

        Layout();
    }

    private void Layout()
    {
        Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
        bool bottom = Corner is ToastCorner.BottomLeft or ToastCorner.BottomRight;
        bool right = Corner is ToastCorner.TopRight or ToastCorner.BottomRight;

        // Newest nearest the corner, older ones pushed away from it — so the
        // one that just arrived is where the eye already is.
        //
        // Except the pinned ones, which take the corner and keep it. A toast
        // that stays up for the length of a song is not news, and letting the
        // news shove it around the screen makes the thing you are trying to
        // watch move every time something else happens.
        //
        // Built into a scratch list reused between frames rather than a LINQ
        // ordering: this runs every frame, for a handful of items.
        ordered.Clear();
        for (int i = 0; i < toasts.Count; i++)
        {
            if (toasts[i].Pinned)
            {
                ordered.Add(toasts[i]);
            }
        }

        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            if (!toasts[i].Pinned)
            {
                ordered.Add(toasts[i]);
            }
        }

        float cursor = bottom
            ? windowSize.Y - Margin - EdgeInset
            : Margin + EdgeInset;

        for (int i = 0; i < ordered.Count; i++)
        {
            Toast toast = ordered[i];
            Element content = toast.Content;
            if (content == null)
            {
                continue;
            }

            Vector2 size = content.GetSize().AsXna;

            float targetY = bottom ? cursor - size.Y : cursor;
            float x = right ? windowSize.X - Margin - size.X : Margin;

            // Ease in on arrival, out on the way away — both as a slide along
            // the stacking axis plus opacity, which is why Element.Opacity had
            // to exist before this could.
            float enter = (float)Math.Clamp(toast.Age / EnterSeconds, 0, 1);
            float exit = toast.Closing ? (float)Math.Clamp(toast.ClosingFor / ExitSeconds, 0, 1) : 0f;
            float eased = 1f - ((1f - enter) * (1f - enter));

            float offset = (1f - eased) * SlideDistance + (exit * SlideDistance);
            float y = targetY + (bottom ? offset : -offset);

            // Settle into place rather than snapping when the toast below is
            // taken away: the first frame is placed outright (DrawnAt is NaN),
            // every frame after that chases.
            if (float.IsNaN(toast.DrawnAt))
            {
                toast.DrawnAt = y;
            }
            else
            {
                toast.DrawnAt += (y - toast.DrawnAt) * 0.35f;
            }

            content.Opacity = Math.Clamp(eased * (1f - exit) * toast.Opacity, 0f, 1f);
            content.Set<PositionTrait>(new TVVector(x, toast.DrawnAt));

            cursor = bottom
                ? cursor - size.Y - Spacing
                : cursor + size.Y + Spacing;
        }
    }
}
