using GustUI.TraitValues;

namespace GustUI.Traits
{
    public class OnMouseButtonHeldDown : Trait<TVEvent<ClickEventArgs>> { }

    public class OnMousePress : Trait<TVEvent<ClickEventArgs>> { }
    public class OnMouseRelease : Trait<TVEvent<ClickEventArgs>> { }
    public class OnHoverTrait : Trait<TVEvent<ClickEventArgs>> { }
    public class OnEnterTrait : Trait<TVEvent<ClickEventArgs>> { }
    public class OnExitTrait : Trait<TVEvent<ClickEventArgs>> { }
    public class OnRightClickTrait : Trait<TVEvent<ClickEventArgs>> { }

    // Middle button, as a full press/held/release trio rather than the
    // press-edge-only shape OnRightClickTrait has. A middle drag is a real
    // gesture (the ezmuze sequencer pans its timeline with one), so it needs
    // the same "keep receiving frames after the pointer leaves" contract the
    // left button gets - see InputManager.CaptureMiddlePointer, which is a
    // SEPARATE capture slot so a middle drag and a left drag cannot cancel
    // each other.
    public class OnMiddleMousePress : Trait<TVEvent<ClickEventArgs>> { }

    public class OnMiddleMouseHeldDown : Trait<TVEvent<ClickEventArgs>> { }

    public class OnMiddleMouseRelease : Trait<TVEvent<ClickEventArgs>> { }

    // Right button as a full trio too, alongside the press-edge-only
    // OnRightClickTrait above rather than replacing it: a right DRAG is a real
    // gesture (the ezmuze sequencer draws its time selection with one), and
    // every existing consumer of OnRightClickTrait wants the edge and nothing
    // more. Both fire on a press; a handler wanting only "was this a click,
    // not a drag" decides that from its own movement threshold, the same way
    // the left button's PendingBlock/Block split already does.
    public class OnRightMousePress : Trait<TVEvent<ClickEventArgs>> { }

    public class OnRightMouseHeldDown : Trait<TVEvent<ClickEventArgs>> { }

    public class OnRightMouseRelease : Trait<TVEvent<ClickEventArgs>> { }
    public class OnDoubleClickTrait : Trait<TVEvent<ClickEventArgs>> { }

    public class  OnScrollTrait : Trait<TVEvent<ScrollEventArgs>> { }

    public class OnScrollWheelChanged : Trait<TVEvent<ScrollEventArgs>> { }

    public class OnFocused : Trait<TVEvent<TVEventArgs>> { }
    public class OnUnfocused : Trait<TVEvent<TVEventArgs>> { }


}
