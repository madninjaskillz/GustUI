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
