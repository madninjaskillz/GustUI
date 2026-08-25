using System;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GustUI.Elements;

/// <summary>
/// The draggable boundary between a docked panel and whatever shares the
/// screen with it.
///
/// There is exactly ONE boundary between a dock and the space beside it, and
/// this is it — dragging it sets the dock's reservation
/// (<see cref="DockLayout.SetReservation"/>) and everything else follows from
/// that: the panel renders at the new size, and every fills-available window
/// re-reads the inset on its next frame and gives up (or takes back) the
/// difference. Which side of the line you grab from is not a distinction the
/// layout has to know about, because there is only one line.
///
/// It exists because the panel's own title bar sits exactly on that line. A
/// drag there was being read as "tear this window out of the dock", which
/// moved the panel, left its size alone, and left the filler believing it had
/// the whole window — so the one gesture a user would naturally try to resize
/// a dock was also the one that broke it. This strip is a child of the panel
/// with a depth above the title bar's, so it wins that overlap; the rest of
/// the title bar still drags the window out, which is a gesture worth keeping.
/// </summary>
[ElementTraits(
    typeof(PositionTrait),
    typeof(SizeTrait),
    typeof(BackgroundFillTrait),
    typeof(ChildrenTrait),
    typeof(OnMousePress),
    typeof(OnMouseButtonHeldDown),
    typeof(OnMouseRelease),
    typeof(OnEnterTrait),
    typeof(OnExitTrait))]
public class DockSplitterElement : Element
{
    /// <summary>
    /// How thick the grab strip is.
    ///
    /// Generous on purpose, and the reason is the gesture immediately beside
    /// it: below this strip is the panel's title bar, and a drag there tears
    /// the panel out of the dock. Missing the boundary by three pixels should
    /// not undock a panel, so the target is wide enough to hit without aiming.
    /// </summary>
    public const int GrabThickness = 10;

    private readonly ModalWindowElement panel;

    private bool dragging;
    private float grabOffset;

    public DockSplitterElement(ModalWindowElement panel)
    {
        this.panel = panel;

        Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));

        // A press reaches EVERY hovered element that wants one, not just the
        // front-most, so depth alone was not enough: the title bar underneath
        // this strip was still getting the same press and tearing the panel
        // out of the dock while the splitter resized it. SwallowsPointer drops
        // everything traversed before this element, and being the deepest-
        // sorted child means that is exactly the title bar and the panel.
        SwallowsPointer = true;

        Set<OnMousePress>(new TVEvent<ClickEventArgs>(args =>
        {
            dragging = true;

            // Where in the strip the grab happened, so the boundary does not
            // jump to centre itself under the cursor on the first frame.
            Vector2 mouse = args.GlobalMousePosition.AsXna;
            grabOffset = Horizontal ? mouse.X - EdgePosition() : mouse.Y - EdgePosition();
            CapturePointer();
        }));

        Set<OnMouseButtonHeldDown>(new TVEvent<ClickEventArgs>(args =>
        {
            if (dragging)
            {
                Apply(args.GlobalMousePosition.AsXna);
            }
        }));

        Set<OnMouseRelease>(new TVEvent<ClickEventArgs>(_ => dragging = false));

        AddTrait<OnEnterTrait>().Set(new TVEvent<ClickEventArgs>(_ =>
            Set<BackgroundFillTrait>(new TVFillSolidColor(
                Resources.StaticResources.Theme.AccentSelection))));

        AddTrait<OnExitTrait>().Set(new TVEvent<ClickEventArgs>(_ =>
        {
            if (!dragging)
            {
                Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
            }
        }));
    }

    /// <summary>True when the boundary runs vertically (a left/right dock),
    /// so the drag axis is X.</summary>
    private bool Horizontal =>
        panel.DockedSide == DockSide.Left || panel.DockedSide == DockSide.Right;

    /// <summary>True while the user is dragging — the panel suppresses its
    /// own drag/dock gestures for the duration, or the two fight.</summary>
    public bool IsDragging => dragging;

    /// <summary>Screen position of the boundary right now.</summary>
    private float EdgePosition()
    {
        Vector2 origin = panel.GetActualXnaPosition();
        Vector2 size = panel.GetSize().AsXna;

        return panel.DockedSide switch
        {
            DockSide.Left => origin.X + size.X,
            DockSide.Right => origin.X,
            DockSide.Top => origin.Y + size.Y,
            _ => origin.Y,
        };
    }

    private void Apply(Vector2 mouse)
    {
        Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
        float at = (Horizontal ? mouse.X : mouse.Y) - grabOffset;

        // The reservation is the distance from the panel's OWN screen edge to
        // the boundary, so which edge it grows from depends on the side.
        float offset = DockLayout.StackOffset(panel, panel.DockedSide);
        float size = panel.DockedSide switch
        {
            DockSide.Left => at - offset,
            DockSide.Right => windowSize.X - at - offset,
            DockSide.Top => at - offset,
            _ => windowSize.Y - at - offset,
        };

        // A floor rather than zero: a panel dragged to nothing is unrecoverable
        // without finding a 7px strip against the screen edge. DockLayout still
        // applies its own ceilings (half the axis, and whatever the fillers
        // need), so this only has to stop the bottom end.
        DockLayout.SetReservation(panel, Math.Max(48f, size));
    }

    /// <summary>Positions the strip along the panel's inboard edge. Called by
    /// the panel each frame, since the edge moves whenever anything in the
    /// layout does.</summary>
    public void LayoutFor(Vector2 panelSize)
    {
        switch (panel.DockedSide)
        {
            case DockSide.Left:
                Set<PositionTrait>(new TVVector(panelSize.X - GrabThickness, 0));
                Set<SizeTrait>(new TVVector(GrabThickness, panelSize.Y));
                break;

            case DockSide.Right:
                Set<PositionTrait>(new TVVector(0, 0));
                Set<SizeTrait>(new TVVector(GrabThickness, panelSize.Y));
                break;

            case DockSide.Top:
                Set<PositionTrait>(new TVVector(0, panelSize.Y - GrabThickness));
                Set<SizeTrait>(new TVVector(panelSize.X, GrabThickness));
                break;

            default:
                Set<PositionTrait>(new TVVector(0, 0));
                Set<SizeTrait>(new TVVector(panelSize.X, GrabThickness));
                break;
        }
    }
}
