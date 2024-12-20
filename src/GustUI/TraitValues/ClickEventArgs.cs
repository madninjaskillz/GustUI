﻿using GustUI.Elements;
using Microsoft.Xna.Framework.Input;

namespace GustUI.TraitValues
{
    public class ClickEventArgs : TVEventArgs
    {
        public TVVector GlobalMousePosition { get; set; }
        public TVVector RelativeMousePosition { get; set; }
        public MouseState MouseState { get; set; }

        public Element Element { get; set; }

    }
}
