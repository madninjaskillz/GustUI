using GustUI.Extensions;
using GustUI.Traits;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Attributes
{
    public class ElementTraitsAttribute : Attribute
    {
        public Type[] Traits { get; set; }
        public ElementTraitsAttribute(params Type[] Traits)
        {
            this.Traits = Traits.ToArray();
            if (this.Traits.Any(x=>!x.IsItReally(typeof(Trait<>))))
            {
                throw new Exception($"Type isn't trait: {this.Traits.First(x=>!x.IsItReally(typeof(Trait<>)))}");
            }
        }
    }
}
