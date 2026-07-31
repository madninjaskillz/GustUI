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

        // Value equality: assigning an equal text through Trait.Set is a no-op
        // (keeps the stored instance, fires no changed-event).
        public override bool Equals(object obj) => obj is TVText other && other.Text == Text;

        public override int GetHashCode() => Text != null ? Text.GetHashCode() : 0;
    }
}
