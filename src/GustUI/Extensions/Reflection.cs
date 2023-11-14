using GustUI.Attributes;
using GustUI.Traits;
using GustUI.TraitValues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Extensions
{
    public static class Reflection
    {
        public static TValue GetAttributeValue<TAttribute, TValue>(
        this Type type,
        Func<TAttribute, TValue> valueSelector)
        where TAttribute : Attribute
        {
            var att = type.GetCustomAttributes(
                typeof(TAttribute), true
            ).FirstOrDefault() as TAttribute;
            if (att != null)
            {
                return valueSelector(att);
            }
            return default(TValue);
        }

        public static IEnumerable<Type> GetParentTypes(this Type type)
        {
            // is there any base type?
            if (type == null)
            {
                yield break;
            }

            // return all implemented or inherited interfaces
            foreach (var i in type.GetInterfaces())
            {
                yield return i;
            }

            // return all inherited types
            var currentBaseType = type.BaseType;
            while (currentBaseType != null)
            {
                yield return currentBaseType;
                currentBaseType = currentBaseType.BaseType;
            }
        }

        public static IEnumerable<Type> GetParentTypes(this Type type, Type upperType)
        {
            // is there any base type?
            if (type == null)
            {
                yield break;
            }

            // return all implemented or inherited interfaces
            foreach (var i in type.GetInterfaces())
            {
                yield return i;
            }

            // return all inherited types
            var currentBaseType = type.BaseType;
            while (currentBaseType != null && currentBaseType != upperType)
            {
                yield return currentBaseType;
                currentBaseType = currentBaseType.BaseType;
                if (currentBaseType == upperType)
                {
                    break;
                }
            }
        }

        internal static List<Type> GetAllTraitTypes(Type type)
        {
            List<Type> types = new List<Type>();
            ElementTraitsAttribute[] traitAttributes = (ElementTraitsAttribute[])(type.GetCustomAttributes(typeof(ElementTraitsAttribute), true));

            types.AddRange(traitAttributes.SelectMany(x=>x.Traits));
            var parentTypes = GetParentTypes(type, typeof(Elements.Element));
            foreach (var parentType in parentTypes)
            {
                types.AddRange(GetAllTraitTypes(parentType));
            }

            List<Type> allTypes = new List<Type>();
            foreach(var t in types)
            {
                if (!allTypes.Contains(t))
                {
                    allTypes.Add(t);
                }
            }

            return allTypes;
        }

        internal static Dictionary<Type, object> GetTraitsFromAttributes(Type type)
        {
            Dictionary<Type, object> traits = new Dictionary<Type, object>();

            ElementTraitsAttribute[] traitAttributes = (ElementTraitsAttribute[])(type.GetCustomAttributes(typeof(ElementTraitsAttribute), true));
            foreach (ElementTraitsAttribute attribute in traitAttributes)
            {
                foreach (var childAttribute in attribute.Traits)
                {
                    object instance = Activator.CreateInstance(childAttribute, true);
                    Type t = instance.GetType();
                    if (!t.IsItReally(typeof(Trait<>)))
                    {
                        throw new Exception($"{t.Name} is not a Trait");
                    }
                    traits.Add(instance.GetType(), instance);
                }
            }

            var parentTypes = GetParentTypes(type, typeof(Elements.Element));
            foreach (var parentType in parentTypes)
            {
                var parentTraits = GetTraitsFromAttributes(parentType);
                foreach (var parentTraitKey in parentTraits.Keys.Where(x => !traits.ContainsKey(x)))
                {
                    traits.Add(parentTraitKey, parentTraits[parentTraitKey]);
                }

            }

            return traits;
        }

        public static bool IsItReally(this Type extendType, Type baseType)
        {
            while (!baseType.IsAssignableFrom(extendType))
            {
                if (extendType.Equals(typeof(object)))
                {
                    return false;
                }
                if (extendType.IsGenericType && !extendType.IsGenericTypeDefinition)
                {
                    extendType = extendType.GetGenericTypeDefinition();
                }
                else
                {
                    extendType = extendType.BaseType;
                }
            }
            return true;
        }
    }
}
