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
}
