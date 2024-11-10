using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;

namespace GustUI.Elements;


public class Element
{
    private Dictionary<Type, object> traits = new Dictionary<Type, object>();
    private Dictionary<string , Tuple<Element,string>> traitMapping = new Dictionary<string, Tuple<Element, string>>();
    public Element()
    {
        traits = Reflection.GetTraitsFromAttributes(this.GetType());

        foreach (KeyValuePair<Type, object> trait in traits)
        {
        }
    }

    public void Sync()
    {
        if (HasTrait<ChildrenTrait>())
        {
            foreach (var child in ElementTrait<ChildrenTrait>().Value().Items)
            {
                Sync(child);
                SyncMappings(child);
            }
        }
    }

    public void AddChildTraitMapping(string parent, Element child, string childName)
    {
        traitMapping.Add(parent, new Tuple<Element, string>(child, childName));
    }

    public void MapTraitToChild<TraitType>(Element child, string childTraitType) {
        AddChildTraitMapping(typeof(TraitType).Name, child, childTraitType);
    }

    public void Sync(Element child)
    {
        var thisTraits = Reflection.GetAllTraitTypes(this.GetType()).ToList();
        var childTraits = Reflection.GetAllTraitTypes(child.GetType()).ToList();

        List<Type> sharedTraitTypes = thisTraits.Where(t => childTraits.Any(c => c.Name == t.Name)).ToList();
        List<Type> mappedTraitTypes = new List<Type>();

        foreach (Type sharedTraitType in sharedTraitTypes)
        {
            object trait = traits.Values.First(x => x.GetType() == sharedTraitType);
            Log.This("Syncing trait: " + trait + " -> " + sharedTraitType);
            MethodInfo theMethod = sharedTraitType.GetMethod("SyncSubscribe");
            
            object[] pr = new object[] { child };
            theMethod.Invoke(trait, pr);
        }
    }

    public void SyncMappings(Element child)
    {
        var thisTraits = Reflection.GetAllTraitTypes(this.GetType()).ToList();
        var childTraits = Reflection.GetAllTraitTypes(child.GetType()).ToList();

        foreach (var x in traitMapping.Where(p => p.Value.Item1 == child))
        {
            
            string sourceName = x.Key;
            string targetName = x.Value.Item2;

            Log.This("Mapping trait (name): " + sourceName + " to " + targetName);
            Type sourceType = thisTraits.First(t => t.Name == sourceName);
            Type targetType = childTraits.First(t => t.Name == targetName);
            Log.This("Mapping trait (type): " + sourceType + " to " + targetType);

            //object trait = traits.First(x=>x.Key.GetType().Name == sourceName).Value;

            object trait = null;

            foreach (var t in traits)
            {
                if (t.Key.Name == sourceName)
                {
                    trait = t.Value;
                    Log.This("Got correct mapping trait: " + t.Key.Name);
                }
            }

            Type traitType = trait.GetType();

            Log.This("Trait object: " + trait+", '"+ traitType+"'");

            foreach (var m in traitType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                              BindingFlags.Static | BindingFlags.Instance))
            {
                Log.This("traitType Method: " + m.Name);
            }


            foreach (var m in sourceType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                              BindingFlags.Static | BindingFlags.Instance))
            {
                Log.This("sourceType Method: " + m.Name);
            }



            MethodInfo theMethod = traitType.GetMethod("SubscribeMapped", BindingFlags.Public | BindingFlags.NonPublic |
                              BindingFlags.Static | BindingFlags.Instance);

            if (theMethod.NotNull(theMethod.Name)){
                Log.This("Using method: " + theMethod.Name);
                Debugging.DebugBreak();
                object[] pr = new object[] { child, targetType };
                theMethod.Invoke(trait, pr);

                Log.This("Mapping trait: " + sourceName + " to " + targetName);
            }
            else
            {
                Log.This("Cant find: SyncSubscribeMapped on " + sourceType);
            }
        }
    }

    public bool HasTrait<TraitType>() => traits.ContainsKey(typeof(TraitType));

    public TraitType ElementTrait<TraitType>() => (TraitType)traits[typeof(TraitType)];

    public TVElements Children
    {
        get
        {
            if (HasTrait<ChildrenTrait>())
            {
                return ETV<ChildrenTrait, TVElements>();
            }

            throw new Exception("Element doesnt have children");
        }
    }

    public void AddChild(Element child) => Children.Add(child);

    public TraitTypeValue ETV<TraitType, TraitTypeValue>()
        where TraitTypeValue : TraitValue
        where TraitType : Trait<TraitTypeValue>
    {
        return ((TraitType)traits[typeof(TraitType)]).Value();
    }

    public object ElementTraitByType(Type type) => traits[type];

    public bool Set<TraitType, TraitValueType>(TraitValueType value) where TraitValueType : TraitValue where TraitType : Trait<TraitValueType> => ElementTrait<TraitType>().Set(value);

    public bool Set<TraitType>(TraitValue value) => (bool)typeof(TraitType).GetMethod("Set").Invoke(this.ElementTraitByTypeFromObject(typeof(TraitType)), new object[] { value });

    public virtual void Draw(SpriteBatch spriteBatch, Element parent)
    {
        Log.This("Drawing element: " + this.GetType().Name);
        if (this.HasTrait<ChildrenTrait>())
        {
            foreach (var child in this.ElementTrait<ChildrenTrait>().Value().Items)
            {
                Log.This("Drawing child: " + child.GetType().Name);
                child.Draw(spriteBatch, this);
            }
        }

        //for now lets call update from draw to test logic.
        this.Update(parent);
    }

    MouseState previousMouseState = Mouse.GetState();
    public void Update(Element parent)
    {
        Log.This("Updating element: " + this.GetType().Name);
        MouseState mouseState = Mouse.GetState();
        if (parent != null && HasTrait<SizeTrait>() && HasTrait<PositionTrait>())
        {
            TVVector actualPosition = this.GetActualPosition(parent);
            TVVector size = ElementTrait<SizeTrait>().Value();

            if (mouseState.Position.X >= actualPosition.X &&
                mouseState.Position.X <= actualPosition.X + size.X &&
                mouseState.Position.Y >= actualPosition.Y &&
                mouseState.Position.Y <= actualPosition.Y + size.Y)
            {
                if (previousMouseState.LeftButton == ButtonState.Released &&
                    mouseState.LeftButton == ButtonState.Pressed)
                {
                    if (HasTrait<OnClickTrait>())
                    {
                        TVEvent act = ElementTrait<OnClickTrait>().Value();
                        if (act?.TriggerAction != null)
                        {
                            act.TriggerAction(new ClickEventArgs
                            {
                                GlobalMousePosition = new TVVector(mouseState.X, mouseState.Y),
                                RelativeMousePosition = new TVVector(mouseState.X - actualPosition.X, mouseState.Y - actualPosition.Y),
                                MouseState = mouseState
                            });
                        }
                    }
                }
            }
        }
        previousMouseState = mouseState;
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

}