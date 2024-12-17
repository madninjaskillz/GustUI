using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace GustUI.Elements;


public class Element : IDisposable
{
    private Guid Id = Guid.NewGuid();

    [JsonIgnore]
    public Element Parent { get; set; } = null;
    private string elementName = null;
    public string ElementName { get => elementName ?? this.ToString(); set => elementName = value; }
    private Dictionary<Type, object> traits = new Dictionary<Type, object>();
    public int Depth { get; set; } = 0;

    private Dictionary<string, Tuple<Element, string>> traitMapping = new Dictionary<string, Tuple<Element, string>>();
    public Element()
    {
        traits = Reflection.GetTraitsFromAttributes(this.GetType());

        foreach (KeyValuePair<Type, object> trait in traits)
        {
        }
    }

    public T AddChildElement<T>(string name = null) where T : Element
    {
        var result = Activator.CreateInstance<T>();
        ((Element)result).Parent = this;

        if (this.HasTrait<ChildrenTrait>())
        {
            this.AddChild(result, result.ElementName);
        }

        return result as T;
    }

    public void AddChildElement(Element element, string overrideName = null)
    {
        element.Parent = this;
        if (this.HasTrait<ChildrenTrait>())
        {
            this.AddChild(element, element.ElementName);
        }
    }

    public void Kill()
    {
        if (Parent != null)
        {
            Parent.Children.Remove(this);
        }
    }
    public void Sync()
    {
        if (HasTrait<ChildrenTrait>())
        {
            foreach (Element child in ElementTrait<ChildrenTrait>().Value().Items)
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

    public void MapTraitToChild<TraitType>(Element child, string childTraitType)
    {
        AddChildTraitMapping(typeof(TraitType).Name, child, childTraitType);
    }

    public void Sync(Element child)
    {
        if (child == null)
        {
            return;
        }
        var thisTraits = Reflection.GetAllTraitTypes(this.GetType()).ToList();
        var childTraits = Reflection.GetAllTraitTypes(child.GetType()).ToList();

        List<Type> sharedTraitTypes = thisTraits.Where(t => childTraits.Any(c => c.Name == t.Name)).ToList();
        List<Type> mappedTraitTypes = new List<Type>();

        foreach (Type sharedTraitType in sharedTraitTypes)
        {
            object trait = traits.Values.First(x => x.GetType() == sharedTraitType);
            if (!(trait is PositionTrait))
            {
                MethodInfo theMethod = sharedTraitType.GetMethod("SyncSubscribe");

                object[] pr = new object[] { child };
                theMethod.Invoke(trait, pr);
            }
        }

        SyncMappings(child);
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

            Log.This("Trait object: " + trait + ", '" + traitType + "'");

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

            MethodInfo theMethod = traitType.GetMethod("SubscribeMapped");

            if (theMethod.NotNull(theMethod.Name))
            {
                Log.This("Using method: " + theMethod.Name);
                //Debugging.DebugBreak();
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

            return null;
        }
    }

    public void AddChild(Element child, string name)
    {
        child.ElementName = name;
        Children.Add(child, name);
        child.Parent = this;
        MoveToFront();
    }

    public TraitTypeValue ETV<TraitType, TraitTypeValue>()
        where TraitTypeValue : TraitValue
        where TraitType : Trait<TraitTypeValue>
    {
        return ((TraitType)traits[typeof(TraitType)]).Value();
    }

    public object ElementTraitByType(Type type) => traits[type];

    public bool Set<TraitType, TraitValueType>(TraitValueType value) where TraitValueType : TraitValue where TraitType : Trait<TraitValueType> => ElementTrait<TraitType>().Set(value);

    public bool Set<TraitType>(TraitValue value)
    {
        var method = typeof(TraitType).GetMethod("Set");
        return (bool)method.Invoke(this.ElementTraitByTypeFromObject(typeof(TraitType)), new object[] { value });
    }

    public virtual void Draw()
    {
        if (this.HasTrait<ChildrenTrait>())
        {
            foreach (var child in this.ElementTrait<ChildrenTrait>().Value().Items)
            {
                child.Draw();
            }
        }
    }

    public virtual void DebugDraw()
    {
        Resources.StaticResources.DrawManager.DrawRectangle(this.GetActualPosition().Rectangle(this.ElementTrait<SizeTrait>().Value()), Color.Red, 1);
        if (this.HasTrait<ChildrenTrait>())
        {
            foreach (var child in this.ElementTrait<ChildrenTrait>().Value().Items)
            {
                child.DebugDraw();
            }
        }
    }

    public List<Action> FlattenDraws()
    {
        List<Action> existing = new List<Action>();
        existing.Add(Draw);
        if (this.HasTrait<ChildrenTrait>())
        {
            foreach (var child in this.ElementTrait<ChildrenTrait>().Value().Items)
            {
                existing.AddRange(child.FlattenDraws());
            }
        }

        return existing;
    }

    public virtual void DrawOutOfProcess()
    {
        if (this.HasTrait<ChildrenTrait>())
        {
            foreach (var child in this.ElementTrait<ChildrenTrait>().Value().Items)
            {
                child.DrawOutOfProcess();
            }
        }
    }

    MouseState previousMouseState = Mouse.GetState();

    public bool BeingDragged = false;
    private Vector2 dragOffset = Vector2.Zero;
    internal void handleStartDrag(TVEventArgs x)
    {
        MoveToFront();
        BeingDragged = true;
        if (x is ClickEventArgs clickEventArgs)
        {
            dragOffset = clickEventArgs.GlobalMousePosition.AsXna;
        }

        this.Set<OnExitTrait>(new TVEvent<ClickEventArgs>((x) =>
        {
            if (x.MouseState.LeftButton == ButtonState.Released)
            {
                handleStopDrag(x);
            }
        }));
    }

    int escapeDragging = 0;

    internal void MoveToFront()
    {

        this.Depth = Resources.StaticResources.RootWindow.Children.Items.Any() ? Resources.StaticResources.RootWindow.Children.Items.Max(x => x.Depth) + 1 : 0;

    }

    internal void handleStopDrag(TVEventArgs x)
    {
        BeingDragged = false;
    }

    private bool IsObstructedAtPoint(Vector2 point, Element parent = null)
    {
        bool foundSource = false;
        foreach (var element in (parent != null ? parent : Resources.StaticResources.RootWindow).Children.Items)
        {
            if (element == this)
            {
                foundSource = true;
                continue;
            }
            if (element.HasTrait<SizeTrait>() && element.HasTrait<PositionTrait>())
            {
                TVVector actualPosition = element.GetActualPosition();
                TVVector size = element.ElementTrait<SizeTrait>().Value();
                if (point.X >= actualPosition.X &&
                    point.X <= actualPosition.X + size.X &&
                    point.Y >= actualPosition.Y &&
                    point.Y <= actualPosition.Y + size.Y)
                {
                    if (foundSource)
                    {
                        return true;
                    }
                }
            }
            if (foundSource && element != this && element.HasTrait<ChildrenTrait>())
            {
                if (IsObstructedAtPoint(point, element))
                {
                    return true;
                }


            }
        }

        return false;
    }
    public bool IsMouseOver()
    {
        return IsMouseOver(Mouse.GetState().Position.ToVector2());
    }
    public bool IsMouseOver(Vector2 position)
    {
        if (HasTrait<SizeTrait>() && HasTrait<PositionTrait>())
        {
            TVVector actualPosition = this.GetActualPosition();
            TVVector size = ElementTrait<SizeTrait>().Value();

            return
                position.X >= actualPosition.X &&
                position.X <= actualPosition.X + size.X &&
                position.Y >= actualPosition.Y &&
                position.Y <= actualPosition.Y + size.Y;
        }
        return false;
    }

    public virtual void Update(Element parent = null)
    {
        MouseState mouseState = Mouse.GetState();


        if (BeingDragged && mouseState.LeftButton==ButtonState.Released)
        {
            BeingDragged = false;
        }


        if (this.HasTrait<ChildrenTrait>())
        {
            foreach (var child in this.ElementTrait<ChildrenTrait>().Value().Items)
            {
                child.Update(this);
            }
        }

        if (BeingDragged)
        {
            var delta = mouseState.Position.ToVector2() - dragOffset;
            dragOffset = mouseState.Position.ToVector2();
            ElementTrait<PositionTrait>().Value().X += (int)delta.X;
            ElementTrait<PositionTrait>().Value().Y += (int)delta.Y;
        }

        previousMouseState = mouseState;
    }

    internal ClickEventArgs GetClickArgs(MouseState mouseState)
    {
        TVVector actualPosition = this.GetActualPosition();
        return new ClickEventArgs
        {
            GlobalMousePosition = new TVVector(mouseState.X, mouseState.Y),
            RelativeMousePosition = new TVVector(mouseState.X - actualPosition.X, mouseState.Y - actualPosition.Y),
            MouseState = mouseState,
            Element = this

        };
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

    public virtual void Dispose()
    {

    }
}