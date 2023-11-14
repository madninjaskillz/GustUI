using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;

namespace GustUI.Elements;


public class Element
{
    private Dictionary<Type, object> traits = new Dictionary<Type, object>();
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
            }
        }
    }

    public void Sync(Element child)
    {
        var thisTraits = Reflection.GetAllTraitTypes(this.GetType()).ToList();
        var childTraits = Reflection.GetAllTraitTypes(child.GetType()).ToList();

        List<Type> sharedTraitTypes = thisTraits.Where(t => childTraits.Any(c => c.Name == t.Name)).ToList();

        foreach (Type sharedTraitType in sharedTraitTypes)
        {
            object trait = traits.Values.First(x => x.GetType() == sharedTraitType);

            MethodInfo theMethod = sharedTraitType.GetMethod("SyncSubscribe");
            object[] pr = new object[] { child };
            theMethod.Invoke(trait, pr);
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
        if (this.HasTrait<ChildrenTrait>())
        {
            foreach (var child in this.ElementTrait<ChildrenTrait>().Value().Items)
            {
                child.Draw(spriteBatch, this);
            }
        }

        //for now lets call update from draw to test logic.
        this.Update(parent);
    }

    MouseState previousMouseState = Mouse.GetState();
    public void Update(Element parent)
    {
        MouseState mouseState = Mouse.GetState();
        if (parent != null && HasTrait<SizeTrait>() && HasTrait<PositionTrait>()) 
        {
            TVVector actualPosition = this.GetActualPosition(parent);
            TVVector size = ElementTrait<SizeTrait>().Value();

            if (mouseState.Position.X>=actualPosition.X &&
                mouseState.Position.X<=actualPosition.X + size.X &&
                mouseState.Position.Y>=actualPosition.Y &&
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
                                RelativeMousePosition = new TVVector(mouseState.X-actualPosition.X, mouseState.Y-actualPosition.Y),
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