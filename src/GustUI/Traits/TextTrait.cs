using GustUI.TraitValues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Traits
{
    public class TextTrait : Trait<TVText>
    {
    }

    public static class TextTraitExtensions
    {
        public static TVText ToTextTrait(this string input) => new TVText(input);
    }
}
