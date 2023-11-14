using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Extensions
{
    public static class TraitExtensions
    {
        public static bool GenericSet<T>(ref T existing, T newValue)
        {
            if ((existing == null && newValue != null) ||
                (existing != null && newValue == null) ||
                 (existing != null && !existing.Equals(newValue)))
            {
                existing = newValue;
                return true;
            }
            else
            {
                return false;
            }
        }

        public static object ElementTraitByTypeFromObject(this object element, Type type)
        {
            MethodInfo theMethod = element.GetType().GetMethod("ElementTraitByType");
            object[] pr = new object[] { type };
            return theMethod.Invoke(element, pr);
        }
    }
}
