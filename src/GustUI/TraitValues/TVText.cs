using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.TraitValues
{
    public class TVText : TraitValue
    {
        public string Text { get; set; }
        public TVText() { }
        public TVText(string text) { Text = text; }
    }

    public class TVFloat : TraitValue
    {
        public float Float { get; set; }
        public TVFloat() { }
        public TVFloat(float value) { Float = value; }
    }

    public class TVInt : TraitValue
    {
        public int Int{ get; set; }
        public TVInt() { }
        public TVInt(int value) { Int = value; }
    }
}
