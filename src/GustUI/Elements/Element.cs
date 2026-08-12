using GustUI.Attributes;
using GustUI.Exceptions;
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
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using static GustUI.Managers.FontManager;
using static System.Net.Mime.MediaTypeNames;

namespace GustUI.Elements;


public class Element : IDisposable
{
    private Guid Id = Guid.NewGuid();

    internal virtual bool CanBeInputFocused { get; private set; } = false;

    /// <summary>Keyboard input routed to the focused element by the
    /// InputManager (one call per newly pressed key). Base: ignore.</summary>
    internal virtual void HandleKeyInput(Microsoft.Xna.Framework.Input.Keys key, bool shift)
    {
    }
    public bool SizeFitsChildren { get; set; } = false;

    [JsonIgnore]
    public Element Parent { get; set; } = null;
    private string elementName = null;
    public string ElementName { get => elementName ?? this.ToString(); set => elementName = value; }
    private Dictionary<Type, object> traits = new Dictionary<Type, object>();

    private int depth = 0;
    public int Depth
    {
        get => depth;
        set
        {
            if (depth != value)
            {
                depth = value;
                Parent?.Children?.InvalidateSort();
            }
        }
    }

    /// <summary>When true, children are scissor-clipped to this element's bounds during Draw.</summary>
    public bool ClipChildren { get; set; } = false;

    /// <summary>
    /// Marks this element as persistent app chrome (menu bar, status bar):
    /// screens that clear the stage by killing the window's children should
    /// skip chrome elements so navigation survives view switches. The flag is
    /// advisory — <see cref="Kill"/> still works when called directly.
    /// </summary>
    public bool IsChrome { get; set; } = false;

    private Dictionary<string, Tuple<Element, string>> traitMapping = new Dictionary<string, Tuple<Element, string>>();
    public Element()
    {
        traits = Reflection.GetTraitsFromAttributes(this.GetType());
        RefreshTraitCache();
    }

    // ---- hot-trait cache -------------------------------------------------
    // Children/Position/Size are consulted for nearly every element on every
    // frame (draw, update, hover); resolving them through the
    // Dictionary<Type, object> costs a type-keyed hash lookup per access,
    // which dominates per-element cost under interpreted WASM. These direct
    // references are refreshed whenever the trait set changes.
    internal ChildrenTrait CachedChildrenTrait;
    internal PositionTrait CachedPositionTrait;
    internal SizeTrait CachedSizeTrait;

    private void RefreshTraitCache()
    {
        object o;
        CachedChildrenTrait = traits.TryGetValue(typeof(ChildrenTrait), out o) ? (ChildrenTrait)o : null;
        CachedPositionTrait = traits.TryGetValue(typeof(PositionTrait), out o) ? (PositionTrait)o : null;
        CachedSizeTrait = traits.TryGetValue(typeof(SizeTrait), out o) ? (SizeTrait)o : null;
    }

    // ---- draw-phase absolute-position cache ------------------------------
    // GetActualXnaPosition walks the ancestor chain; during a single draw pass
    // positions cannot change (no Draw override mutates PositionTrait), so
    // each element's absolute position is memoized for the duration of the
    // root draw. Inactive (stamp 0 / flag false) outside the draw pass.
    internal static bool PositionCacheActive;
    internal static int PositionCacheStamp;
    internal int absPosStamp;
    internal Vector2 absPosCache;

    internal static void BeginPositionCache()
    {
        PositionCacheStamp++;
        if (PositionCacheStamp == 0)
        {
            PositionCacheStamp = 1;
        }

        PositionCacheActive = true;
    }

    internal static void EndPositionCache()
    {
        PositionCacheActive = false;
    }

    private TVVector fs_prepos;
    private TVVector fs_presize;
    internal bool isFullScreen;
    bool sizeTransition = false;

    private Vector2 desired_position;
    private Vector2 desired_size = Vector2.Zero;
    internal void ToggleFullScreen()
    {
        isFullScreen = !isFullScreen;
        if (isFullScreen)
        {
            fs_prepos = ElementTrait<PositionTrait>().Value();
            fs_presize = ElementTrait<SizeTrait>().Value();
            desired_position = new Vector2(0, 40);
            desired_size = new Vector2(Resources.StaticResources.RootWindow.GetSize().X, Resources.StaticResources.RootWindow.GetSize().Y - 40);
            sizeTransition = true;
        }
        else
        {
            desired_size = fs_presize.AsXna;
            desired_position = fs_prepos.AsXna;
            sizeTransition = true;
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

    public virtual void AddChildElement(Element element, string overrideName = null)
    {
        element.Parent = this;
        if (this.HasTrait<ChildrenTrait>())
        {
            this.AddChild(element, element.ElementName);
        }
    }

    /// <summary>Removes this element from its parent. Virtual so a host that
    /// wants a disappear transition (e.g. <see cref="ModalWindowElement"/>)
    /// can intercept every removal path — including one a caller reaches
    /// via a plain <c>Element</c>-typed reference (<c>Parent.Kill()</c>,
    /// common in button click handlers) — animate, then call this base
    /// implementation once the animation finishes.</summary>
    public virtual void Kill()
    {
        if (Parent != null)
        {
            Parent.Children.Remove(this);
            Parent = null;
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
            return CachedChildrenTrait?.Value();
        }
    }

    public virtual void AddChild(Element child, string name)
    {
        child.ElementName = name;
        Children.Add(child, name);
        child.Parent = this;

        // NOTE: AddChild used to MoveToFront() the RECEIVER whenever a
        // root-level element gained a child (the "root-sibling self-bump"
        // quirk). That never implemented its stated intent (popping a newly
        // ADDED top-level element above its siblings — the root window's own
        // Parent is null, so the intended case never fired) and it silently
        // leapfrogged busy panels over carefully tiered chrome: any root
        // panel that gained children jumped above the depth-50000 loop
        // browser / depth-500000 popups the moment a higher-depth sibling
        // (status bar, tooltip) existed. Depth is now authoritative:
        // equal-depth siblings draw in insertion order (TVElements' OrderBy
        // is stable), so "added later = on top" still holds within a tier,
        // and explicit tiers (tooltip 1000000 > popup 500000 > status bar
        // 100000 > loading 90000 > side panels 50000 > content 0) behave as
        // written. Click-to-front for modal windows still calls
        // MoveToFront() explicitly on press.
    }

    public TraitTypeValue ETV<TraitType, TraitTypeValue>()
        where TraitTypeValue : TraitValue
        where TraitType : Trait<TraitTypeValue>
    {
        return ((TraitType)traits[typeof(TraitType)]).Value();
    }

    public object ElementTraitByType(Type type) => traits[type];

    public bool Set<TraitType, TraitValueType>(TraitValueType value) where TraitValueType : TraitValue where TraitType : Trait<TraitValueType> => ElementTrait<TraitType>().Set(value);

    public bool Set<TraitType>(TraitValue value, [CallerMemberName] string callMemberName = "")
    {
        if (!HasTrait<TraitType>())
        {
            throw new MissingTraitException(typeof(TraitType), this, callMemberName);
        }

        if (traits[typeof(TraitType)] is ISettableTrait settable)
        {
            return settable.SetValue(value);
        }

        var method = typeof(TraitType).GetMethod("Set");
        return (bool)method.Invoke(this.ElementTraitByTypeFromObject(typeof(TraitType)), new object[] { value });
    }

    /// <summary>Attaches a trait at runtime (no-op if already present) — avoids needing a subclass + [ElementTraits] just to opt into an event.</summary>
    public TraitType AddTrait<TraitType>() where TraitType : class, new()
    {
        if (!traits.ContainsKey(typeof(TraitType)))
        {
            traits.Add(typeof(TraitType), new TraitType());
            RefreshTraitCache();
        }

        return (TraitType)traits[typeof(TraitType)];
    }

    /// <summary>Routes held/release mouse events to this element regardless of hover until the button releases (or ReleasePointer).</summary>
    public void CapturePointer() => Resources.StaticResources.InputManager.CapturePointer(this);

    public void ReleasePointer() => Resources.StaticResources.InputManager.ReleasePointer(this);

    public virtual void Draw()
    {
        FrameProfiler.CountElementDraw();
        ChildrenTrait childrenTrait = CachedChildrenTrait;
        if (childrenTrait != null)
        {
            bool clip = ClipChildren && CachedPositionTrait != null && CachedSizeTrait != null;
            if (clip)
            {
                Vector2 clipPos = this.GetActualXnaPosition();
                TVVector clipSize = this.GetSize();
                Resources.StaticResources.DrawManager.PushScissor(
                    new Rectangle((int)clipPos.X, (int)clipPos.Y, clipSize.X.AsInt(), clipSize.Y.AsInt()));
            }

            List<Element> items = childrenTrait.Value().Items;
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Draw();
            }

            if (clip)
            {
                Resources.StaticResources.DrawManager.PopScissor();
            }
        }
    }

    Managers.SdfFont debugSdfFont;
    private float DebugFontSize => Resources.StaticResources.Theme.UiFontSmall.Size;
    public virtual void DebugDraw(Color? filled = null)
    {
        if (debugSdfFont == null)
        {
            debugSdfFont = Resources.StaticResources.FontManager.LoadSdfFont(Resources.StaticResources.Theme.UiFontSmall.Family);
        }
        Resources.StaticResources.DrawManager.DrawRectangle(this.GetActualPosition().Rectangle(this.ElementTrait<SizeTrait>().Value()), Color.Red, 1);
        if (filled.HasValue)
        {
            var sz = this.GetSize();
            var ap = this.GetActualPosition();
            var r = ap.Rectangle(sz);
            Resources.StaticResources.DrawManager.DrawFilledRectangle(r, filled.Value);
        }
        if (Resources.StaticResources.InputManager.GetElementState(this) == InputManager.ElementState.Hovered)
        {
            string ot = this.ElementName + ": " + this.GetRelativePosition() + " / " + this.GetSize();
            Vector2 dbgpos = this.GetActualPosition().AsXna + this.GetSize().AsXna - debugSdfFont.MeasureString(ot, DebugFontSize);
            Resources.StaticResources.DrawManager.DrawSdfString(debugSdfFont, ot, dbgpos + new Vector2(0, 0), DebugFontSize, Color.Black * 0.5f);
            Resources.StaticResources.DrawManager.DrawSdfString(debugSdfFont, ot, dbgpos + new Vector2(2, 0), DebugFontSize, Color.Black * 0.5f);
            Resources.StaticResources.DrawManager.DrawSdfString(debugSdfFont, ot, dbgpos + new Vector2(0, 0 + 2), DebugFontSize, Color.Black * 0.5f);
            Resources.StaticResources.DrawManager.DrawSdfString(debugSdfFont, ot, dbgpos + new Vector2(2, 0 + 2), DebugFontSize, Color.Black * 0.5f);
            Resources.StaticResources.DrawManager.DrawSdfString(debugSdfFont, ot, dbgpos + new Vector2(1, 0 + 1), DebugFontSize, Color.White);
        }
        if (this.HasTrait<ChildrenTrait>())
        {
            foreach (var child in this.ElementTrait<ChildrenTrait>().Value().Items)
            {
                child.DebugDraw();
            }
        }
    }

    int debugColorDirection = 1;
    int debugColorAmount = 1;
    internal int DebugWrite(int depth, int top = 60)
    {
        string ot = this.ElementName + ": " + this.GetRelativePosition() + " / " + this.GetSize();
        Vector2 dbgpos = new Vector2(10+ (depth * 20), top);
        if (debugSdfFont == null)
        {
            debugSdfFont = Resources.StaticResources.FontManager.LoadSdfFont(Resources.StaticResources.Theme.UiFontSmall.Family);
        }

        Vector2 measured = debugSdfFont.MeasureString(ot, DebugFontSize);
        Rectangle r = new Rectangle((int)dbgpos.X, (int)dbgpos.Y, (int)measured.X, (int)measured.Y);
        Color c = Color.White*0.8f;
        MouseState ms = Mouse.GetState();
        if (r.Contains(ms.Position))
        {
            c = Color.Red;
            debugColorAmount = debugColorAmount + debugColorDirection;
            if (debugColorAmount == 0 || debugColorAmount == 255)
            {
                debugColorDirection = -debugColorDirection;
            }
            DebugDraw(new Color(debugColorAmount,0,255) * 0.5f);
        }

        if (Resources.StaticResources.InputManager.GetElementState(this) == InputManager.ElementState.Hovered)
        {
            c = Color.Green;
        }

        if (Resources.StaticResources.InputManager.GetElementState(this) == InputManager.ElementState.Pressed)
        {
            c = Color.Purple;
        }

        Resources.StaticResources.DrawManager.DrawSdfString(debugSdfFont, ot, dbgpos + new Vector2(0, 0), DebugFontSize, Color.Black * 0.5f);
        Resources.StaticResources.DrawManager.DrawSdfString(debugSdfFont, ot, dbgpos + new Vector2(2, 0), DebugFontSize, Color.Black * 0.5f);
        Resources.StaticResources.DrawManager.DrawSdfString(debugSdfFont, ot, dbgpos + new Vector2(0, 0 + 2), DebugFontSize, Color.Black * 0.5f);
        Resources.StaticResources.DrawManager.DrawSdfString(debugSdfFont, ot, dbgpos + new Vector2(2, 0 + 2), DebugFontSize, Color.Black * 0.5f);
        Resources.StaticResources.DrawManager.DrawSdfString(debugSdfFont, ot, dbgpos + new Vector2(1, 0 + 1), DebugFontSize, c);

        top = top + 20;
        if (this.HasTrait<ChildrenTrait>())
        {
            foreach (var child in this.ElementTrait<ChildrenTrait>().Value().Items)
            {
                top = child.DebugWrite(depth+1,top);
                top = top + 1;
            }
        }

        return top;

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

        if (isFullScreen)
        {
            ToggleFullScreen();
            desired_position = new Vector2(fs_prepos.AsXna.X,40);
            //this.Set<PositionTrait>(new TVVector(this.ElementTrait<PositionTrait>().Value().X, 20));
        }

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
        // Excludes TooltipElement: its shared instance sits permanently in
        // RootWindow.Children (Hide() only toggles visibility, never
        // detaches it) at a fixed depth of 1,000,000 — the highest tier in
        // the app, above popups (500,000) and modals. Without this
        // exclusion, the FIRST tooltip shown anywhere in the session
        // poisons every future MoveToFront() call (modal spawn, drag-to-
        // front) into jumping above the tooltip tier and, worse, above the
        // popup tier — a reopened modal would then bury its own dropdown
        // popups behind itself, since those draw at the fixed PopupDepth
        // rather than via MoveToFront.
        var candidates = Resources.StaticResources.RootWindow.Children.Items.Where(x => !(x is TooltipElement));
        this.Depth = candidates.Any() ? candidates.Max(x => x.Depth) + 1 : 0;
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
                Vector2 actualPosition = element.GetActualXnaPosition();
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
        // InputManager.CurrentMouseState, NOT a fresh Mouse.GetState() —
        // that's the raw, undivided physical-pixel position; CurrentMouseState
        // is the one already divided by InputManager.MouseScale (DesktopGL's
        // DPI compensation, see EzmuzeStudioGame.SyncDevicePixelRatio). Every
        // element's own bounds (PositionTrait/SizeTrait) live in that same
        // divided LOGICAL space, so comparing them against the raw physical
        // position silently drifts apart at any DPI scale other than 1 — the
        // drift grows with distance from the origin, so elements near (0,0)
        // still read correctly while ones further down/right increasingly
        // don't. Invisible for years at DPI scale 1 (physical == logical
        // numerically); surfaced 2026-08-12 as FruitPopupMenu's outside-click
        // dismiss firing on genuine clicks inside a tall dropdown — the
        // popup's own IsMouseOver() call here disagreed with the click that
        // just landed on one of its own lower rows and killed itself first.
        MouseState scaled = Resources.StaticResources.InputManager.CurrentMouseState;
        return IsMouseOver(new Vector2(scaled.X, scaled.Y));
    }
    public bool IsMouseOver(Vector2 position)
    {
        if (CachedSizeTrait != null && CachedPositionTrait != null)
        {
            Vector2 actualPosition = this.GetActualXnaPosition();
            // Fit-to-children containers never write their SizeTrait (see the
            // matching note in InputManager.CollectHovered).
            TVVector size = SizeFitsChildren ? this.GetSize() : CachedSizeTrait.Value();

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
        FrameProfiler.CountElementUpdate();
        // One Mouse.GetState per frame (InputManager), not one per element.
        MouseState mouseState = Resources.StaticResources.InputManager.CurrentMouseState;


        if (BeingDragged && mouseState.LeftButton==ButtonState.Released)
        {
            BeingDragged = false;
        }


        if (CachedChildrenTrait != null)
        {
            List<Element> items = CachedChildrenTrait.Value().Items;
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Update(this);
            }
        }

        if (BeingDragged)
        {
            var delta = mouseState.Position.ToVector2() - dragOffset;
            dragOffset = mouseState.Position.ToVector2();
            ElementTrait<PositionTrait>().Value().X += (int)delta.X;
            ElementTrait<PositionTrait>().Value().Y += (int)delta.Y;
        }

        if (sizeTransition)
        {
            var currentSize = this.ElementTrait<SizeTrait>().Value().AsXna;
            var currentPosition = this.ElementTrait<PositionTrait>().Value().AsXna;

            var newSize = Vector2.Lerp(currentSize, desired_size, 0.4f);
            var newPosition = Vector2.Lerp(currentPosition, desired_position, 0.4f);
            Set<SizeTrait>(new TVVector(newSize));
            Set<PositionTrait>(new TVVector(newPosition));

            if (Math.Abs(newSize.X - desired_size.X) < 1 && Math.Abs(newSize.Y - desired_size.Y) < 1)
            {
                sizeTransition = false;
            }
        }

        if (isFullScreen)
        {
            float topLimit = 0;
            if (Resources.StaticResources.RootWindow.Children.Items.Any(x => x is FruitMenuElement))
            {
                topLimit = Resources.StaticResources.RootWindow.Children.Items.First(x => x is FruitMenuElement).GetSize().Y;
            }


            Set<SizeTrait>(new TVVector(Resources.StaticResources.RootWindow.GetSize().X, Resources.StaticResources.RootWindow.GetSize().Y - topLimit));
            Set<PositionTrait>(new TVVector(0, topLimit));
        }

        previousMouseState = mouseState;
    }

    internal ClickEventArgs GetClickArgs(MouseState mouseState)
    {
        Vector2 actualPosition = this.GetActualXnaPosition();
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