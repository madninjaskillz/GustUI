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
    public class OnDoubleClickTrait : Trait<TVEvent<ClickEventArgs>> { }

    public class  OnScrollTrait : Trait<TVEvent<ScrollEventArgs>> { }
}
