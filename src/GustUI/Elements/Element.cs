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

    /// <summary>Read-only view of this element's traits, keyed by trait type
    /// — for generic tooling (e.g. a tree/DOM dump) that needs to enumerate
    /// whatever traits are present without a hardcoded switch over known
    /// trait types.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<Type, object> AllTraits => traits;

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

        // Only meaningful for the PARENT's child-visibility cull cache
        // (see cachedVisibleChildren below) — reading Parent dynamically at
        // fire time (not capturing it here) means this stays correct even
        // though Parent is usually null until AddChild runs later. Safe to
        // rely on Trait<T>.Set firing here: TVVector.X/Y are init-only
        // specifically so nothing can change a position/size without going
        // through Set (see TVVector's own doc comment).
        if (CachedPositionTrait != null)
        {
            CachedPositionTrait.ValueChangedEventHandler += (s, e) => Parent?.MarkChildCullDirty();
        }

        if (CachedSizeTrait != null)
        {
            CachedSizeTrait.ValueChangedEventHandler += (s, e) => Parent?.MarkChildCullDirty();
        }
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

    /// <summary>Top-left corner a maximized window should occupy. Default is
    /// the raw window origin; <see cref="ModalWindowElement"/> overrides this
    /// to respect <see cref="Managers.DockLayout"/>'s live insets, so
    /// maximizing while something is docked elsewhere fills only the space
    /// docking leaves free instead of the whole screen.</summary>
    protected virtual Vector2 FullScreenTargetPosition() => Vector2.Zero;

    /// <summary>Size a maximized window should occupy — see
    /// <see cref="FullScreenTargetPosition"/>.</summary>
    protected virtual Vector2 FullScreenTargetSize() => Resources.StaticResources.RootWindow.GetSize().AsXna;

    /// <summary>Below this many px of difference on either axis, a captured
    /// "previous size" counts as "the same as fullscreen" — floats from a
    /// lerped transition rarely land on an exact integer, and a handful of
    /// px of drift shouldn't be treated as a real distinct size the user
    /// meant to return to.</summary>
    private const float FullScreenSizeEqualityToleragePx = 4f;

    internal void ToggleFullScreen()
    {
        isFullScreen = !isFullScreen;
        if (isFullScreen)
        {
            fs_prepos = ElementTrait<PositionTrait>().Value();
            fs_presize = ElementTrait<SizeTrait>().Value();
            desired_position = FullScreenTargetPosition();
            desired_size = FullScreenTargetSize();
            sizeTransition = true;
        }
        else
        {
            // The size/position we're leaving — reliable to read right now
            // because Update()'s own `if (isFullScreen)` block force-set
            // both to exactly this every frame while isFullScreen was true.
            Vector2 fullSize = ElementTrait<SizeTrait>().Value().AsXna;
            Vector2 fullCenter = ElementTrait<PositionTrait>().Value().AsXna + fullSize / 2f;

            // Found 2026-08-17 (live user test): "un-fullscreening a modal
            // doesn't shrink". Root cause — fs_presize/fs_prepos are only a
            // USEFUL restore target when they genuinely differ from the
            // fullscreen rect; a modal that was already filling its
            // available space (the common case: the sequencer's default
            // open size, or any panel maximized right after opening, with
            // no resize in between) captures a "previous" size that's
            // identical to fullscreen, so restoring it was an invisible
            // no-op — the modal visibly stayed full. fs_presize can also be
            // null outright (isFullScreen set directly via the public
            // IsFullScreen property, e.g. preserving the sequencer's
            // maximized state across a rebuild — see ModalWindowElement.cs
            // — never goes through the `if (isFullScreen)` capture branch
            // above). Both cases are "no real previous size to return to",
            // so both fall through to the same explicit fallback: shrink to
            // 70% of the fullscreen size, keeping the same center point,
            // rather than silently doing nothing.
            bool hasDistinctPreviousSize = fs_presize != null && fs_prepos != null
                && (Math.Abs(fs_presize.AsXna.X - fullSize.X) > FullScreenSizeEqualityToleragePx
                    || Math.Abs(fs_presize.AsXna.Y - fullSize.Y) > FullScreenSizeEqualityToleragePx);

            if (hasDistinctPreviousSize)
            {
                desired_size = fs_presize.AsXna;
                desired_position = fs_prepos.AsXna;
            }
            else
            {
                desired_size = fullSize * 0.7f;
                desired_position = fullCenter - desired_size / 2f;
            }

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
        // and explicit tiers (tooltip 1,000,000 > dock preview 700,000 >
        // popup 500,000 > status bar 100,000 > loading 90,000 > modal
        // 60,000 > side panels 50,000 > content 0) behave as
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

    /// <summary>Logical-space (unscaled — same units as PositionTrait/
    /// SizeTrait) stack of the currently-visible region, mirroring
    /// DrawManager's own (physical-pixel, RenderScale-applied) scissorStack
    /// one level behind it: pushed/popped at the exact same ClipChildren
    /// boundaries, just kept in the element tree's own coordinate space so
    /// <see cref="IsPotentiallyVisible"/> can test a child's bounds without
    /// any RenderScale conversion. Static/single-threaded, matching every
    /// other per-frame traversal state in this class (FrameProfiler et al).</summary>
    private static readonly Stack<Rectangle> visibleClipStack = new Stack<Rectangle>();

    /// <summary>Cheap AABB reject: false only when this element has real
    /// bounds (Position+SizeTrait, the same guard <see cref="IsMouseOver"/>
    /// uses) AND those bounds provably don't overlap <paramref name="clip"/>.
    /// Elements without resolvable bounds (e.g. SizeFitsChildren containers
    /// that never write SizeTrait) always report visible — safe default,
    /// never culls something we can't actually measure.</summary>
    private bool IsPotentiallyVisible(Rectangle clip)
    {
        if (CachedPositionTrait == null || CachedSizeTrait == null)
        {
            return true;
        }

        Vector2 pos = this.GetActualXnaPosition();
        TVVector size = SizeFitsChildren ? this.GetSize() : CachedSizeTrait.Value();
        Rectangle bounds = new Rectangle((int)pos.X, (int)pos.Y, size.X.AsInt(), size.Y.AsInt());
        return bounds.Intersects(clip);
    }

    // ---- child-visibility cull cache --------------------------------------
    // Caches Draw()'s per-child IsPotentiallyVisible scan below so a frame
    // where nothing moved doesn't re-run it. Found 2026-08-20 profiling:
    // ~5ms/frame at ~5100 elements was exactly this walk (virtual dispatch +
    // AABB test per child), on an otherwise-static scene between rebinds.
    //
    // Invalidated automatically, not by any caller remembering to mark it:
    // MarkChildCullDirty fires whenever a CHILD's own Position/Size trait
    // actually changes (subscribed once per element in the constructor) —
    // reliable now that TVVector.X/Y are init-only, so Trait<T>.Set is the
    // ONLY way to change either (see TVVector's own doc comment for why
    // that used to not be true). The clip-rect/children-version check below
    // catches everything else: an ancestor's clip changing, add/remove,
    // Depth-reorder.
    //
    // NOT safe for a child using SizeFitsChildren (VerticalScrollElement's
    // inner container is the only one in this codebase): its effective
    // bounds are derived from grandchildren on demand, with no Set() call
    // for anything to hook, so a container that owns one is simply never
    // cached — falls back to a fresh scan every frame, same as before this
    // existed. That's cheap in practice: it's always a small, static child
    // set (VerticalScrollElement's is exactly 2 items).
    private List<Element> cachedVisibleChildren;
    private Rectangle cachedVisibleClip;
    private int cachedChildrenVersion = -1;
    private bool childCullDirty = true;

    internal void MarkChildCullDirty() => childCullDirty = true;

    private List<Element> GetVisibleChildren(List<Element> items, Rectangle activeClip, int childrenVersion)
    {
        if (!childCullDirty
            && cachedVisibleChildren != null
            && cachedChildrenVersion == childrenVersion
            && cachedVisibleClip == activeClip)
        {
            return cachedVisibleChildren;
        }

        bool cacheable = true;
        List<Element> visible = cachedVisibleChildren ?? new List<Element>(items.Count);
        visible.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].SizeFitsChildren)
            {
                cacheable = false;
            }

            if (items[i].IsPotentiallyVisible(activeClip))
            {
                visible.Add(items[i]);
            }
        }

        if (cacheable)
        {
            cachedVisibleChildren = visible;
            cachedVisibleClip = activeClip;
            cachedChildrenVersion = childrenVersion;
            childCullDirty = false;
        }
        else
        {
            cachedVisibleChildren = null;
            childCullDirty = true;
        }

        return visible;
    }

    public virtual void Draw()
    {
        FrameProfiler.CountElementDraw();
        ChildrenTrait childrenTrait = CachedChildrenTrait;
        if (childrenTrait != null)
        {
            bool clip = ClipChildren && CachedPositionTrait != null && CachedSizeTrait != null;
            bool pushedVisibleClip = false;
            if (clip)
            {
                Vector2 clipPos = this.GetActualXnaPosition();
                TVVector clipSize = this.GetSize();
                Rectangle logicalClipRect = new Rectangle((int)clipPos.X, (int)clipPos.Y, clipSize.X.AsInt(), clipSize.Y.AsInt());
                Resources.StaticResources.DrawManager.PushScissor(logicalClipRect);

                visibleClipStack.Push(visibleClipStack.Count > 0
                    ? Rectangle.Intersect(visibleClipStack.Peek(), logicalClipRect)
                    : logicalClipRect);
                pushedVisibleClip = true;
            }

            TVElements childrenValue = childrenTrait.Value();
            List<Element> items = childrenValue.Items;
            // Cull against the nearest ClipChildren ancestor's region (not
            // just this element's own) — a non-clipping passthrough
            // container's children still sit inside whatever ancestor
            // region is active, e.g. a row inside a scrolled panel.
            if (visibleClipStack.Count > 0)
            {
                Rectangle activeClip = visibleClipStack.Peek();
                List<Element> visible = GetVisibleChildren(items, activeClip, childrenValue.Version);
                for (int i = 0; i < visible.Count; i++)
                {
                    DrawChild(visible[i]);
                }
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                {
                    DrawChild(items[i]);
                }
            }

            if (clip)
            {
                Resources.StaticResources.DrawManager.PopScissor();
            }
            if (pushedVisibleClip)
            {
                visibleClipStack.Pop();
            }
        }
    }

    /// <summary>
    /// Marks this element's ENTIRE subtree as needing strict draw-order
    /// preservation in GeometryBatch's "overlay" stream — see that
    /// class's own doc comment. Set this on anything positioned (via
    /// Depth) to render on top of OTHER elements' text, not just its
    /// own — the two-way non-text/text split is otherwise safe (a
    /// label already draws after its own container's fill; siblings in
    /// a non-overlapping layout can reorder freely) but breaks for
    /// content specifically engineered to sit above someone else's
    /// label (found via SequencerView's dragDropGhost/dragDropBadge/
    /// playhead, the only elements placed above DepthRunLabel there).
    /// Checked once per child in DrawChild below, not per-Append* call.
    /// </summary>
    public bool IsOverlay { get; set; }

    /// <summary>Draws one child, wrapping its ENTIRE subtree in
    /// GeometryBatch's overlay stream when IsOverlay is set — pushed
    /// before Draw() runs (so even the child's OWN content, drawn by
    /// its subclass's Draw() override before it calls base.Draw() for
    /// recursion, lands in the overlay stream) and popped after,
    /// nesting-safe via GeometryBatch's own depth counter.</summary>
    private static void DrawChild(Element child)
    {
        if (child.IsOverlay)
        {
            Resources.StaticResources.DrawManager.GeometryBatch.PushOverlay();
            child.Draw();
            Resources.StaticResources.DrawManager.GeometryBatch.PopOverlay();
        }
        else
        {
            child.Draw();
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
            desired_position = new Vector2(fs_prepos.AsXna.X, FullScreenTargetPosition().Y);
        }

        BeingDragged = true;
        if (x is ClickEventArgs clickEventArgs)
        {
            dragOffset = clickEventArgs.GlobalMousePosition.AsXna;

            // Found 2026-08-17 (user report): dragging a modal's title bar
            // never captured the pointer — unlike ResizeHandlesElement,
            // which needed the exact same fix earlier this session for the
            // exact same reason (see its own BeginResize doc comment).
            // Continuation itself doesn't depend on this (the delta-move
            // below, in Update()'s own `if (BeingDragged)` block, is an
            // unconditional per-frame poll of raw mouse state, hover-
            // independent) — this is purely to stop OTHER elements from
            // reacting to OnMouseButtonHeldDown while the cursor sweeps
            // over them mid-drag, which is exactly what happens whenever
            // the window itself can't actually follow the cursor (clamped
            // by a min-size, FillsAvailableSpace re-asserting its own
            // position, a screen-edge clamp) — the drag keeps running, so
            // the cursor drifts arbitrarily far from the window's own
            // (stuck) position, sweeping over whatever's underneath.
            // Capturing clickEventArgs.Element (the drag bar itself, the
            // SAME element OnMouseRelease is wired to below in
            // ModalTitleBarElement's constructor) rather than `this` (the
            // modal) matters: InputManager's captured-pointer dispatch
            // delivers release hover-independently ONLY to the captured
            // element, so capturing anything else would silently swallow
            // the release DockTo/tab-merge commit depends on.
            clickEventArgs.Element.CapturePointer();
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

        // Same poisoning class as the TooltipElement exclusion above, just
        // from the OTHER direction: any other permanently-present, fixed-
        // tier root-window child (a status bar, a persistent transport bar
        // — both app-level types GustUI can't name here) sitting ABOVE the
        // modal tier by design would otherwise get treated as a legitimate
        // "current front" to leapfrog past. Found 2026-08-17: a resizable
        // ModalWindowElement's own justSpawned->MoveToFront() picked up
        // such a bar's depth as the pool max and jumped itself to
        // (that bar's depth)+1 — comfortably above FullScreenModalElement's
        // own fixed 60,000 tier — so every full-screen editor opened "over"
        // it afterward silently rendered (and hit-tested) BEHIND it
        // instead, with no exception anywhere to reveal why. MoveToFront is
        // only ever used to reorder PEERS within the base content/floating-
        // window layer (regular drag-to-front, ModalWindowElement spawn/
        // dock) — never called on a tier singleton itself — so clamping the
        // result just below the modal tier is safe for every current
        // caller and keeps the documented tier ordering (tooltip > dock
        // preview > popup > status bar > loading > MODAL > side panels >
        // content) intact no matter what else happens to be alive when a
        // floating window is brought to front.
        int ceiling = FullScreenModalElement.ModalDepth - 1;
        this.Depth = candidates.Any() ? Math.Min(candidates.Max(x => x.Depth) + 1, ceiling) : 0;

        // 2026-08-17 (inactive-title-bar-desaturation feature): Depth alone
        // can't answer "which window was brought to front most recently"
        // once two or more windows are both clamped to the same `ceiling`
        // above — found live, via the control API, the very first time
        // ModalWindowElement.IsFrontmostWindow tried to use Depth for
        // exactly that: it ties, so both windows read as active. A separate,
        // never-clamped, always-increasing sequence number — bumped here,
        // the one place "this window is now the front one" is decided —
        // has no ceiling to saturate against.
        this.FrontSequence = ++frontSequenceCounter;
    }

    private static long frontSequenceCounter = 0;

    /// <summary>Monotonically increasing — bumped by <see cref="MoveToFront"/>,
    /// never reset, never clamped. The highest value among a set of
    /// siblings is unambiguously "whichever was brought to front most
    /// recently," unlike <see cref="Depth"/> once several of them share the
    /// same clamped ceiling. internal: <see cref="ModalWindowElement.IsFrontmostWindow"/>
    /// is the one reader.</summary>
    internal long FrontSequence { get; private set; }

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
            // Same logical-clip cull as Draw() (visibleClipStack is shared
            // between the two passes — safe since a frame's Update sweep
            // always fully unwinds before that frame's Draw sweep starts,
            // never interleaved). An off-screen element skipping Update
            // just means its own animation/drag state holds for one frame
            // rather than progressing unseen — same "one frame of
            // staleness" tradeoff QueuePendingBake already makes. Input
            // hit-testing is unaffected: InputManager.CollectHovered walks
            // the tree independently of Update/Draw.
            bool clip = ClipChildren && CachedPositionTrait != null && CachedSizeTrait != null;
            bool pushedVisibleClip = false;
            if (clip)
            {
                Vector2 clipPos = this.GetActualXnaPosition();
                TVVector clipSize = this.GetSize();
                Rectangle logicalClipRect = new Rectangle((int)clipPos.X, (int)clipPos.Y, clipSize.X.AsInt(), clipSize.Y.AsInt());
                visibleClipStack.Push(visibleClipStack.Count > 0
                    ? Rectangle.Intersect(visibleClipStack.Peek(), logicalClipRect)
                    : logicalClipRect);
                pushedVisibleClip = true;
            }

            List<Element> items = CachedChildrenTrait.Value().Items;
            if (visibleClipStack.Count > 0)
            {
                Rectangle activeClip = visibleClipStack.Peek();
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].IsPotentiallyVisible(activeClip))
                    {
                        items[i].Update(this);
                    }
                }
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                {
                    items[i].Update(this);
                }
            }

            if (pushedVisibleClip)
            {
                visibleClipStack.Pop();
            }
        }

        if (BeingDragged)
        {
            var delta = mouseState.Position.ToVector2() - dragOffset;
            dragOffset = mouseState.Position.ToVector2();
            TVVector current = ElementTrait<PositionTrait>().Value();
            Set<PositionTrait>(new TVVector(current.X + (int)delta.X, current.Y + (int)delta.Y));
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
            // Found 2026-08-17 (user report): maximize never animated while
            // unmaximize did, and maximize ignored docked panels. Root cause
            // of the first half — this block used to force Size/Position
            // straight to the target every frame, UNCONDITIONALLY, right
            // after the sizeTransition lerp above computed a partial step
            // toward that same target; the lerp's intermediate frames were
            // therefore always immediately overwritten, so entering
            // fullscreen visibly snapped instead of animating. Exiting
            // fullscreen never hit this (isFullScreen is already false by
            // then), which is why only that direction ever animated.
            //
            // Fix: only retarget (and only via the SAME lerp everything else
            // uses) when the target actually moved — from a window resize or
            // a dock/undock elsewhere changing FullScreenTargetPosition/Size
            // — rather than force-setting the traits directly every frame.
            // This also fixes the second half of the report for free: since
            // ModalWindowElement overrides those two methods to route through
            // Managers.DockLayout's live insets (see FillsAvailableSpace's
            // identical formula), a maximized window now continuously tracks
            // whatever space docking currently leaves available, animating
            // smoothly to the new size if that changes while it's maximized.
            Vector2 target = FullScreenTargetSize();
            Vector2 targetPos = FullScreenTargetPosition();
            if (Vector2.DistanceSquared(desired_size, target) > 1f || Vector2.DistanceSquared(desired_position, targetPos) > 1f)
            {
                desired_size = target;
                desired_position = targetPos;
                sizeTransition = true;
            }
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