using GustUI.Elements;
using Microsoft.Xna.Framework.Input;

namespace GustUI.TraitValues
{
    public class ClickEventArgs : TVEventArgs
    {
        public TVVector GlobalMousePosition { get; set; }
        public TVVector RelativeMousePosition { get; set; }
        public MouseState MouseState { get; set; }

        public Element Element { get; set; }

        /// <summary>How many presses this one closes: 1 for a plain click, 2
        /// for the second of a double, 3 for the third of a triple, and so on
        /// for as long as the presses keep landing in the same place inside
        /// <see cref="Managers.InputManager.MultiClickSeconds"/> of each
        /// other. A handler that only cares about single clicks can ignore it
        /// - every press still fires <see cref="Traits.OnMousePress"/>; this
        /// only says which one in a run it was.</summary>
        public int ClickCount { get; set; } = 1;
    }

    public class ScrollEventArgs : TVEventArgs
    {
        public float ScrollPosition { get; set; }
        public float ScrollPercentage { get; set; }

        public int ScrollWheel { get; set; }
        public int ScrollWheelDelta { get; set; }

        public TVVector GlobalMousePosition { get; set; }
    }
}
