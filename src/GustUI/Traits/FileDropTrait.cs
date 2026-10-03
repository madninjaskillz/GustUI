using GustUI.TraitValues;

namespace GustUI.Traits
{
    /// <summary>
    /// The files an element takes when they are dropped on it from outside the
    /// app (ezmuze #683) — see <see cref="TVFileDrop"/> and
    /// <see cref="Managers.FileDropRouter"/>. Usually set on a window's body,
    /// or on a tab's content so it goes with the tab.
    /// </summary>
    public class FileDropTrait : Trait<TVFileDrop>
    {
    }
}
