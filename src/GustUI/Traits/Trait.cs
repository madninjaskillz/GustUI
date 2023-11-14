using GustUI.Elements;
using GustUI.Extensions;
using GustUI.TraitValues;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GustUI.Traits
{
    public class Trait<T> where T : TraitValue
    {
        public void Hello()
        {
            Debug.WriteLine("hello");
        }
        public void SyncSubscribe(Element child)
        {
            ValueChangedEventHandler+= (object sender, TraitChangedEventArgs e) => Sync(sender, e, child);
        }

        internal void Sync(object sender, TraitChangedEventArgs e, object child)
        {
            Type thisType = sender.GetType();

            object localCopy = sender;
            object rc = child.ElementTraitByTypeFromObject(thisType);
            MethodInfo theMethod = thisType.GetMethod("CopyTo");
            object[] pr = new object[] { rc };
            theMethod.Invoke(localCopy, pr);
        }

        public event EventHandler<TraitChangedEventArgs> ValueChangedEventHandler;

        internal T _value;
        public bool CopyTo(Trait<T> other)
        {
            Debug.WriteLine($"Copying {typeof(T)}/{this.GetType()} to {typeof(T)}/{other.GetType()}");
            return other.Set(this.Value());
        }
        public bool Set(T value)
        {
            if (TraitExtensions.GenericSet<T>(ref _value, value))
            {
                ValueChangedEventHandler?.Invoke(this, new TraitChangedEventArgs()
                {
                    TraitType = typeof(T),
                    Trait = typeof(Trait<T>),
                });
                return true;
            }
            return false;
        }
        public T Value() => _value;

        public Trait()
        {
            _value = Activator.CreateInstance<T>();
        }

        public static Type GetTraitType()
        {

            return typeof(Trait<>);
        }

    }


    public class TraitChangedEventArgs : EventArgs
    {
        public Type TraitType { get; set; }
        public Type Trait { get; set; }
    }
}
