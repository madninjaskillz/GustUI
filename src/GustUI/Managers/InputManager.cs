using GustUI.Elements;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace GustUI.Managers
{
    public class InputManager
    {
        public Element CurrentlyFocused = null;
        public List<KeyboardHook> Hooks = new List<KeyboardHook>();

        // ---- keyboard hook scopes -----------------------------------------
        // A modal surface (FullScreenModalElement) must suppress the keyboard
        // shortcuts of whatever lies beneath it: the sequencer's Delete hook
        // firing while a piano roll modal is open would delete clips the user
        // cannot even see. Scopes solve this at the framework level: pushing a
        // scope makes it the ACTIVE scope; every KeyboardHook records the
        // active scope at construction; Update only fires hooks whose scope is
        // the active one. Popping (by token, order-independent) reactivates
        // the scope beneath. Scope 0 is the base scope (no modal open).
        private readonly List<int> hookScopeStack = new List<int>();
        private int nextHookScopeId = 1;

        /// <summary>The scope newly created hooks join and the only scope
        /// whose hooks fire. 0 = base (no modal scope pushed).</summary>
        public int ActiveHookScope => hookScopeStack.Count > 0 ? hookScopeStack[hookScopeStack.Count - 1] : 0;

        /// <summary>Pushes a new hook scope (see field notes) and returns its
        /// token for <see cref="PopHookScope"/>.</summary>
        public int PushHookScope()
        {
            int id = nextHookScopeId++;
            hookScopeStack.Add(id);
            return id;
        }

        /// <summary>Removes a pushed scope by token (safe out of order — an
        /// inner modal closing after its parent still resolves correctly).</summary>
        public void PopHookScope(int token) => hookScopeStack.Remove(token);

        public bool HaveInteracted { get; private set; }
        private MouseState previousMouseState;
        private KeyboardState previousKeyboardState;
        private int previousScrollWheelValue;

        /// <summary>Resyncs the scroll-delta baseline to <paramref name="value"/>
        /// WITHOUT firing an OnScroll/OnScrollWheelChanged event — for a
        /// caller that just drove one or more synthetic scroll frames (e.g.
        /// the desktop remote-control API's /scroll endpoint) and is about
        /// to hand control back to real mouse polling. Real hardware scroll
        /// state never moved during the synthetic override, so the next
        /// real poll would otherwise read a value that differs from
        /// whatever the synthetic frames last left <c>previousScrollWheelValue</c>
        /// at, firing a spurious "correcting" scroll event (in the OPPOSITE
        /// direction, silently undoing the intended scroll) the instant real
        /// input resumes. Call this with the real baseline (captured before
        /// any synthetic override) right before releasing synthetic state.</summary>
        public void SyncScrollBaseline(int value) => previousScrollWheelValue = value;
        private List<Element> currentlyHovered = new List<Element>();
        private List<Element> currentlyClicked = new List<Element>();

        /// <summary>This frame's mouse state — read this instead of calling Mouse.GetState per element.</summary>
        public MouseState CurrentMouseState { get; private set; }

        /// <summary>
        /// Divides polled mouse coordinates before hit-testing — the general
        /// counterpart to <c>WindowElement.DevicePixelRatio</c> shrinking
        /// <c>SizeTrait</c> from <c>GameWindow.ClientBounds</c>: hit-testing
        /// compares raw mouse position against element bounds that live in
        /// that divided space, so whenever a platform's raw pointer
        /// coordinates are NOT already pre-divided to match, the host must
        /// set this to the same ratio or roughly half the UI becomes
        /// unclickable/misaligned. Two backends land on opposite sides of
        /// this by construction: KNI's Blazor mouse tracking reports the
        /// browser's own already-CSS-pixel (i.e. already-divided) DOM event
        /// coordinates, so that host leaves this at the default 1 and gets
        /// correct hit-testing "for free"; KNI's DesktopGL/SDL2 backend
        /// reports raw physical window-client pixels 1:1 with
        /// <c>ClientBounds</c> with no such adjustment, so a host applying a
        /// DevicePixelRatio != 1 there must set this to match. Default 1 =
        /// no-op, matching GustUI's original single-pass behavior.
        /// </summary>
        public float MouseScale { get; set; } = 1f;

        /// <summary>True while a text-input element (e.g. a rename/save-as
        /// field) holds keyboard focus — the same gate <see cref="Update"/>
        /// uses internally to suppress shortcut hooks while typing. Exposed
        /// for callers that poll raw key state directly every frame (e.g. a
        /// QWERTY note-audition keyboard) instead of registering a
        /// <see cref="KeyboardHook"/>, so they can skip note keys the same
        /// way "z" doesn't trigger undo while renaming something.</summary>
        public bool IsTyping => CurrentlyFocused != null && CurrentlyFocused.CanBeInputFocused;

        /// <summary>True only during the frame that observed the left button's
        /// press edge (elements can react to "a click started somewhere",
        /// e.g. popups closing on an outside press).</summary>
        public bool LeftJustPressed { get; private set; }

        /// <summary>While set, held/release mouse events route here regardless of hover.</summary>
        public Element CapturedPointerElement { get; private set; }

        public void CapturePointer(Element element) => CapturedPointerElement = element;

        // ---- synthetic input override -------------------------------------
        // Lets an external driver (e.g. an in-process remote-control server)
        // author authoritative MouseState/KeyboardState for a frame instead
        // of the real OS poll. Unlike PushPointerEdge (which REPLAYS an extra
        // pass before the real poll, which still runs last and wins), this
        // REPLACES the poll's source outright, so the synthetic state is what
        // Update() actually dispatches. Null (the default) is a no-op —
        // behavior is unchanged from a real OS poll.
        private MouseState? syntheticMouseState;
        private KeyboardState? syntheticKeyboardState;

        /// <summary>Set to make this frame's mouse state synthetic instead of
        /// Mouse.GetState(); pass null to resume real OS polling.</summary>
        public void SetSyntheticMouseState(MouseState? state) => syntheticMouseState = state;

        /// <summary>Set to make this frame's keyboard state synthetic instead
        /// of Keyboard.GetState(); pass null to resume real OS polling.</summary>
        public void SetSyntheticKeyboardState(KeyboardState? state) => syntheticKeyboardState = state;

        /// <summary>Drops keyboard focus (e.g. when a dialog holding a text
        /// field closes — a focused element would keep suppressing shortcut
        /// hooks forever otherwise).</summary>
        public void ClearFocus()
        {
            if (CurrentlyFocused != null)
            {
                if (CurrentlyFocused.HasTrait<OnUnfocused>())
                {
                    CurrentlyFocused.ElementTrait<OnUnfocused>().Value().TriggerAction?.Invoke(new TVEventArgs());
                }

                CurrentlyFocused = null;
            }
        }

        public void ReleasePointer(Element element)
        {
            if (CapturedPointerElement == element)
            {
                CapturedPointerElement = null;
            }
        }
        
        internal int FloatedElementCount { get; private set; }
        internal string FloatedElementName { get; private set; }

        public InputManager()
        {
            Hooks.Add(new KeyboardHook(new KeyboardShortcut(Keys.Oem8), () =>
            {
                Resources.StaticResources.DebugMode.Next();
            }));

            Hooks.Add(new KeyboardHook(new KeyboardShortcut(Keys.OemTilde), () =>
            {
                Resources.StaticResources.DebugMode.Next();
            }));

            Hooks.Add(new KeyboardHook(new KeyboardShortcut(Keys.CapsLock), () =>
            {
                Resources.StaticResources.DebugMode.Next();
            }));
        }


        public class KeyboardShortcut
        {
            public List<KeyboardModifiers> Modifiers;
            public Keys Key;

            public KeyboardShortcut(Keys keys, params KeyboardModifiers[] modifiers)
            {
                Key = keys;
                Modifiers = modifiers.ToList();
            }
        }

        public class KeyboardHook
        {
            public KeyboardShortcut Shortcut;
            public Action TriggerAction;

            /// <summary>The hook scope this hook belongs to (recorded at
            /// construction = the scope that was active when the owning view
            /// registered it). Only hooks of the ACTIVE scope fire.</summary>
            public int Scope;

            public KeyboardHook(KeyboardShortcut shortcut, Action triggerAction)
            {
                Shortcut = shortcut;
                TriggerAction = triggerAction;
                // Null-safe: the InputManager's own ctor hooks run before
                // StaticResources.InputManager is assigned — they land in the
                // base scope.
                Scope = Resources.StaticResources?.InputManager?.ActiveHookScope ?? 0;
            }
        }

      
        public enum KeyboardModifiers
        {
            shift,
            ctrl,
            alt
        }
        public enum ElementState
        {
            Normal,
            Hovered,
            Pressed
        }

        public Keys FromModifier(KeyboardModifiers modifier)
        {
            switch (modifier)
            {
                case KeyboardModifiers.shift:
                    return Keys.LeftShift;
                case KeyboardModifiers.ctrl:
                    return Keys.LeftControl;
                case KeyboardModifiers.alt:
                    return Keys.LeftAlt;
                default:
                    return Keys.None;
            }
        }

        public ElementState GetElementState(Element element)
        {
            if (currentlyClicked.Contains(element)) { return ElementState.Pressed; }
            if (currentlyHovered.Contains(element)) { return ElementState.Hovered; }
            return ElementState.Normal;
        }


        /// <summary>
        /// A pointer button transition pushed by the host platform (see
        /// <see cref="PushPointerEdge"/>): the position and the button states
        /// that resulted from the transition.
        /// </summary>
        private readonly struct PointerEdge
        {
            public readonly int X;
            public readonly int Y;
            public readonly bool Left;
            public readonly bool Right;

            public PointerEdge(int x, int y, bool left, bool right)
            {
                X = x;
                Y = y;
                Left = left;
                Right = right;
            }
        }

        private readonly List<PointerEdge> pointerEdges = new List<PointerEdge>();

        /// <summary>
        /// Feeds a pointer button transition from the platform's event layer.
        ///
        /// GustUI samples the mouse once per frame, and some backends (e.g.
        /// KNI's Blazor mouse) keep only the CURRENT button state fed from DOM
        /// events — so a click whose press AND release both happen inside one
        /// frame (a fast click, or any click during a long frame) is invisible
        /// to the poll and used to be dropped entirely. Hosts that can observe
        /// the underlying events push each down/up transition here; Update
        /// replays every pushed edge through the full dispatch path before the
        /// polled state, so no click is ever lost. Optional: platforms that
        /// push nothing behave exactly as before.
        /// </summary>
        public void PushPointerEdge(int x, int y, bool leftDown, bool rightDown)
        {
            pointerEdges.Add(new PointerEdge(x, y, leftDown, rightDown));
        }

        public void Update()
        {
            MouseState polledState = syntheticMouseState ?? Mouse.GetState();
            if (!syntheticMouseState.HasValue && MouseScale != 1f && MouseScale > 0f)
            {
                // Scale correction only makes sense for real, physical-pixel
                // OS coordinates — synthetic state is already authored in
                // the same (divided) space element bounds live in.
                polledState = new MouseState(
                    (int)(polledState.X / MouseScale),
                    (int)(polledState.Y / MouseScale),
                    polledState.ScrollWheelValue,
                    polledState.HorizontalScrollWheelValue,
                    polledState.RawX, polledState.RawY,
                    polledState.LeftButton, polledState.MiddleButton, polledState.RightButton,
                    polledState.XButton1, polledState.XButton2);
            }

            KeyboardState keyboardState = syntheticKeyboardState ?? Keyboard.GetState();

            // While a text-input element is focused, newly pressed keys go to
            // it and keyboard SHORTCUT hooks are suppressed (typing "z" must
            // not trigger an undo hook).
            bool typing = CurrentlyFocused != null && CurrentlyFocused.CanBeInputFocused;
            if (typing)
            {
                bool shift = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);
                foreach (Keys key in keyboardState.GetPressedKeys())
                {
                    if (!previousKeyboardState.IsKeyDown(key))
                    {
                        CurrentlyFocused.HandleKeyInput(key, shift);
                    }
                }
            }

            int activeScope = ActiveHookScope;
            for (int i = 0; !typing && i < Hooks.Count; i++)
            {
                KeyboardHook hook = Hooks[i];
                if (hook.Scope != activeScope)
                {
                    continue; // belongs to a view beneath (or above) the active modal scope
                }

                if (!keyboardState.IsKeyDown(hook.Shortcut.Key) || previousKeyboardState.IsKeyDown(hook.Shortcut.Key))
                {
                    continue;
                }

                bool modifiersDown = true;
                if (hook.Shortcut.Modifiers != null)
                {
                    for (int m = 0; m < hook.Shortcut.Modifiers.Count; m++)
                    {
                        if (!keyboardState.IsKeyDown(FromModifier(hook.Shortcut.Modifiers[m])))
                        {
                            modifiersDown = false;
                            break;
                        }
                    }
                }

                if (modifiersDown)
                {
                    hook.TriggerAction();
                }
            }

            previousKeyboardState = keyboardState;

            // Mouse: replay host-pushed pointer edges first (each is a full
            // dispatch pass at the position the transition happened), then the
            // polled state as the final pass. With no pushed edges this is the
            // single-pass behavior GustUI always had. The edge replay is what
            // makes sub-frame clicks land: [down, up] between two polls fires
            // press then release even though the poll never saw Pressed.
            LeftJustPressed = false;
            if (pointerEdges.Count > 0)
            {
                for (int i = 0; i < pointerEdges.Count; i++)
                {
                    PointerEdge edge = pointerEdges[i];
                    MouseState edgeState = new MouseState(
                        edge.X, edge.Y, polledState.ScrollWheelValue,
                        edge.Left ? ButtonState.Pressed : ButtonState.Released,
                        ButtonState.Released,
                        edge.Right ? ButtonState.Pressed : ButtonState.Released,
                        ButtonState.Released, ButtonState.Released);
                    ProcessMouseState(edgeState, isFinal: false);
                }

                pointerEdges.Clear();
            }

            ProcessMouseState(polledState, isFinal: true);
        }

        /// <summary>One full mouse dispatch pass (hover, capture, press/held/
        /// release, right-click; scroll only on the final pass per frame).</summary>
        private void ProcessMouseState(MouseState mouseState, bool isFinal)
        {
            CurrentMouseState = mouseState;
            int scrollWheel = mouseState.ScrollWheelValue;

            if (mouseState.LeftButton == ButtonState.Pressed && previousMouseState.LeftButton == ButtonState.Released)
            {
                LeftJustPressed = true;
            }

            currentlyHovered = ProcessHovers(Resources.StaticResources.RootWindow, mouseState.Position.ToVector2());

            if (isFinal && scrollWheel != previousScrollWheelValue)
            {
                // Either scroll trait subscribes an element; OnScrollWheelChanged
                // wins when both exist (matches the old behavior for elements
                // that declared the historical trait pair).
                foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnScrollTrait>() || e.HasTrait<OnScrollWheelChanged>()))
                {
                    var args = new ScrollEventArgs
                    {
                        ScrollWheel = scrollWheel,
                        ScrollWheelDelta = previousScrollWheelValue - scrollWheel,
                        GlobalMousePosition = new TVVector(mouseState.X, mouseState.Y),
                    };

                    if (element.HasTrait<OnScrollWheelChanged>())
                    {
                        element.ElementTrait<OnScrollWheelChanged>().Value().TriggerAction?.Invoke(args);
                    }
                    else
                    {
                        element.ElementTrait<OnScrollTrait>().Value().TriggerAction?.Invoke(args);
                    }
                }
                previousScrollWheelValue = scrollWheel;
            }

            if (CapturedPointerElement != null)
            {
                Element captured = CapturedPointerElement;

                if (mouseState.LeftButton == ButtonState.Pressed)
                {
                    HaveInteracted = true;
                    if (captured.HasTrait<OnMouseButtonHeldDown>())
                    {
                        captured.ElementTrait<OnMouseButtonHeldDown>().Value().TriggerAction?.Invoke(captured.GetClickArgs(mouseState));
                    }
                }
                else
                {
                    if (previousMouseState.LeftButton == ButtonState.Pressed && captured.HasTrait<OnMouseRelease>())
                    {
                        captured.ElementTrait<OnMouseRelease>().Value().TriggerAction?.Invoke(captured.GetClickArgs(mouseState));
                    }

                    CapturedPointerElement = null;
                }

                currentlyClicked = mouseState.LeftButton == ButtonState.Pressed
                    ? new List<Element> { captured }
                    : new List<Element>();

                UpdateHoverTransitions(mouseState);
                previousMouseState = mouseState;
                return;
            }

            if (mouseState.LeftButton == ButtonState.Pressed)
            {
                currentlyClicked = currentlyHovered.Where(e => e.HasTrait<OnMousePress>() || e.HasTrait<OnMouseButtonHeldDown>()).ToList();
            }
            else
            {
                currentlyClicked = new List<Element>();
            }

            if (mouseState.LeftButton == ButtonState.Pressed && previousMouseState.LeftButton == ButtonState.Released)
            {
                foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnMousePress>()))
                {
                    element.ElementTrait<OnMousePress>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
                }


                foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnFocused>()))
                {
                    if (CurrentlyFocused != element)
                    {
                        if (CurrentlyFocused != null)
                        {
                            CurrentlyFocused.ElementTrait<OnUnfocused>().Value().TriggerAction?.Invoke(new TVEventArgs());
                        }
                        CurrentlyFocused = element;
                        element.ElementTrait<OnFocused>().Value().TriggerAction?.Invoke(new TVEventArgs());
                    }
                }

            }
            else if (mouseState.LeftButton == ButtonState.Pressed)
            {
                HaveInteracted = true;

                foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnMouseButtonHeldDown>()))
                {
                    element.ElementTrait<OnMouseButtonHeldDown>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
                }
            }
            else if (mouseState.LeftButton == ButtonState.Released && previousMouseState.LeftButton == ButtonState.Pressed)
            {
                HaveInteracted = true;
                foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnMouseRelease>()))
                {
                    element.ElementTrait<OnMouseRelease>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
                }
            }

            // Right-button press edge → OnRightClickTrait on hovered elements.
            // The trait existed but was never dispatched; press-edge only (no
            // right-drag/capture semantics — capture stays left-button).
            if (mouseState.RightButton == ButtonState.Pressed && previousMouseState.RightButton == ButtonState.Released)
            {
                HaveInteracted = true;
                foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnRightClickTrait>()))
                {
                    element.ElementTrait<OnRightClickTrait>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
                }
            }



            UpdateHoverTransitions(mouseState);
            previousMouseState = mouseState;
        }

        // Previous frame's hover list is cached rather than recomputed with a
        // second full ProcessHovers tree walk every frame.
        private List<Element> lastHoverList = new List<Element>();

        private void UpdateHoverTransitions(MouseState mouseState)
        {
            for (int i = 0; i < currentlyHovered.Count; i++)
            {
                Element element = currentlyHovered[i];
                if (!lastHoverList.Contains(element) && element.HasTrait<OnEnterTrait>())
                {
                    element.ElementTrait<OnEnterTrait>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
                }
            }

            for (int i = 0; i < lastHoverList.Count; i++)
            {
                Element element = lastHoverList[i];
                if (!currentlyHovered.Contains(element) && element.HasTrait<OnExitTrait>())
                {
                    element.ElementTrait<OnExitTrait>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
                }
            }

            for (int i = 0; i < currentlyHovered.Count; i++)
            {
                Element element = currentlyHovered[i];
                if (element.HasTrait<OnHoverTrait>())
                {
                    element.ElementTrait<OnHoverTrait>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
                }
            }

            FloatedElementCount = currentlyHovered.Count;
            if (Resources.StaticResources.DebugMode != DebugMode.None)
            {
                // Debug-overlay string; skipped normally (string.Join per frame).
                FloatedElementName = string.Join(", ", currentlyHovered.Select(e => e.ElementName));
            }

            lastHoverList = currentlyHovered;
        }

        // Scratch list reused across frames for per-branch hover collection.
        private readonly List<Element> hoverScratch = new List<Element>();

        /// <summary>
        /// Hit-tests the tree top-down, accumulating absolute positions on the
        /// way down (child abs = child rel + parent abs) so each element is
        /// O(1) instead of walking its ancestor chain. Children are only
        /// visited under hovered elements, and the front-most hovered
        /// top-level branch (highest child index = drawn last) wins — the same
        /// result the old indexed two-pass collection produced.
        /// </summary>
        private List<Element> ProcessHovers(Element root, Vector2 position)
        {
            List<Element> best = new List<Element>();
            if (!root.IsMouseOver(position) || root.CachedChildrenTrait == null)
            {
                return best;
            }

            Vector2 rootAbs = root.GetActualXnaPosition();
            Vector2 rootContribution = root.CachedPositionTrait != null ? rootAbs : Vector2.Zero;

            List<Element> branches = root.CachedChildrenTrait.Value().Items;
            for (int i = 0; i < branches.Count; i++)
            {
                hoverScratch.Clear();
                CollectHovered(branches[i], position, rootContribution, hoverScratch);
                if (hoverScratch.Count > 0)
                {
                    best.Clear();
                    best.AddRange(hoverScratch);
                }
            }

            return best;
        }

        private static void CollectHovered(Element element, Vector2 position, Vector2 parentContribution, List<Element> into)
        {
            PositionTrait positionTrait = element.CachedPositionTrait;
            SizeTrait sizeTrait = element.CachedSizeTrait;
            if (positionTrait == null || sizeTrait == null)
            {
                return; // mirrors IsMouseOver: both traits required to hover
            }

            TVVector rel = positionTrait.Value();
            float ax = rel.X + parentContribution.X;
            float ay = rel.Y + parentContribution.Y;

            // A SizeFitsChildren container never WRITES its SizeTrait — its
            // extent is computed on demand by GetSize() — so reading the raw
            // trait here made every such container test as 0x0 and, because
            // children are only visited under a hovered parent, silently made
            // its whole subtree unclickable. That hit anything inside a
            // VerticalScrollElement, whose content container is exactly this
            // shape. The GetSize() path is O(children), so it is taken only
            // for the (rare) fit-to-children containers.
            TVVector size = element.SizeFitsChildren ? element.GetSize() : sizeTrait.Value();

            if (position.X < ax || position.X > ax + size.X || position.Y < ay || position.Y > ay + size.Y)
            {
                return;
            }

            into.Add(element);

            if (element.CachedChildrenTrait != null)
            {
                Vector2 abs = new Vector2(ax, ay);
                List<Element> items = element.CachedChildrenTrait.Value().Items;
                for (int i = 0; i < items.Count; i++)
                {
                    CollectHovered(items[i], position, abs, into);
                }
            }
        }
    }
}
