using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.TraitValues
{
    public class TVFont : TraitValue
    {
        public string Family { get; set; }
        public float Size { get; set; }
        public int Border { get; set; }

        /// <summary>
        /// Outline color for <see cref="Border"/> &gt; 0. Null keeps the
        /// original look (the text color faded to 10%); set an explicit color
        /// (e.g. translucent black behind white text) for a contrast halo that
        /// keeps labels legible over busy backgrounds.
        /// </summary>
        public Microsoft.Xna.Framework.Color? BorderColor { get; set; }
    }

    public class TVBool : TraitValue
    {
        public bool Bool { get; set; }
        public TVBool(bool value) { Bool = value; }
        public TVBool() { }
    }

    public enum HorizontalAlignment { Left, Center, Right };
    public enum VerticalAlignment { Top, Center, Bottom };

    public class TVHorizontalAlignment : TraitValue { public HorizontalAlignment Alignment { get; set;} }
    public class TVVerticalAlignment : TraitValue { public VerticalAlignment Alignment { get; set; } }
}
