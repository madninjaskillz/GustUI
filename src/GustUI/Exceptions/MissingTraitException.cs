using GustUI.TraitValues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GustUI.Traits;

namespace GustUI.Exceptions
{
    public class MissingTraitException : Exception
    {
        public MissingTraitException(Type trait, Elements.Element element, string caller) : base($"Trait missing: {trait.ToString()} in {element.ElementName} (called from {caller})")
        {
        }

        public MissingTraitException(string message) : base(message)
        {

        }
    }
}
