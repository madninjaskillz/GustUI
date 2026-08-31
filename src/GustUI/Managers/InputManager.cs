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

        // ---- key auto-repeat ----------------------------------------------
        // A HELD key keeps firing at the focused element after an initial
        // delay, which every OS text field does and which nothing here did:
        // Update only ever dispatched the press EDGE, so deleting a mistyped
        // word meant tapping backspace once per character and there was no way
        // at all to run the caret along a line. Only the typing path repeats -
        // keyboard SHORTCUT hooks stay edge-only, because a held Ctrl+Z firing
        // 30 times a second is a different thing entirely.
        private readonly System.Diagnostics.Stopwatch keyClock = System.Diagnostics.Stopwatch.StartNew();
        private Keys repeatKey = Keys.None;
        private Element repeatFocus;
        private double repeatNextSeconds;

        /// <summary>How long a key must be held before it starts repeating.</summary>
        public double KeyRepeatDelaySeconds { get; set; } = 0.4;

        /// <summary>Gap between repeats once repeating has started. Capped in
        /// practice by the frame rate - Update dispatches at most one repeat
        /// per frame, so a stalled frame can never dump a burst of backlogged
        /// keystrokes into a field.</summary>
        public double KeyRepeatIntervalSeconds = 0.035;

        // ---- multi-click counting ------------------------------------------
        // Presses landing in the same spot, on the same element, in quick
        // succession are a double/triple click; the count travels on
        // ClickEventArgs.ClickCount and also fires OnDoubleClickTrait, which
        // until now was a trait the toolkit declared and never dispatched.
        private double lastPressSeconds = double.NegativeInfinity;
        private Point lastPressPosition;
        private Element lastPressElement;
        private int pressRunCount;

        /// <summary>Longest gap between two presses that still counts as one
        /// multi-click run (Windows' own default).</summary>
        public double MultiClickSeconds { get; set; } = 0.5;

        /// <summary>How far a press may land from the one before it and still
        /// continue the run - a double click is two presses in the same PLACE,
        /// not two presses that happen to be close in time.</summary>
        public int MultiClickSlopPixels { get; set; } = 4;

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

        /// <summary>This frame's keyboard state as the manager saw it —
        /// synthetic when injected, empty while <see cref="WindowActive"/>
        /// is false, the real poll otherwise. Per-frame pollers (musical
        /// typing, held-key audition) read THIS rather than Keyboard.GetState,
        /// so an inactive window never plays notes from keys typed into
        /// whatever is in front of it.</summary>
        public KeyboardState CurrentKeyboardState { get; private set; }

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

        /// <summary>While set, MIDDLE-button held/release events route here
        /// regardless of hover. Deliberately a second slot rather than a mode
        /// on <see cref="CapturedPointerElement"/>: the two buttons drive
        /// unrelated gestures (the sequencer marquees with the left button and
        /// pans with the middle one), and sharing a slot would let starting
        /// either one silently cancel the other.</summary>
        public Element CapturedMiddleElement { get; private set; }

        public void CaptureMiddlePointer(Element element) => CapturedMiddleElement = element;

        /// <summary>While set, RIGHT-button held/release events route here
        /// regardless of hover. A third slot, for the same reason the middle
        /// button got the second one.</summary>
        public Element CapturedRightElement { get; private set; }

        public void CaptureRightPointer(Element element) => CapturedRightElement = element;

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
        public void ClearFocus() => SetFocus(null);

        public void ReleasePointer(Element element)
        {
            if (CapturedPointerElement == element)
            {
                CapturedPointerElement = null;
            }

            if (CapturedMiddleElement == element)
            {
                CapturedMiddleElement = null;
            }

            if (CapturedRightElement == element)
            {
                CapturedRightElement = null;
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

        /// <summary>
        /// Whether holding <paramref name="key"/> down should keep firing it
        /// at the focused element.
        ///
        /// Everything a text field treats as EDITING repeats - characters,
        /// backspace/delete, the caret keys. What must not is anything whose
        /// handler is a one-shot decision: Enter submits a dialog, Escape
        /// closes one, Tab moves focus, and a key resting on any of those for
        /// half a second should do it once, not thirty times a second.
        /// Modifier keys are held BY DEFINITION and carry no keystroke of
        /// their own.
        /// </summary>
        private static bool CanAutoRepeat(Keys key)
        {
            switch (key)
            {
                case Keys.Enter:
                case Keys.Escape:
                case Keys.Tab:
                case Keys.LeftShift:
                case Keys.RightShift:
                case Keys.LeftControl:
                case Keys.RightControl:
                case Keys.LeftAlt:
                case Keys.RightAlt:
                case Keys.LeftWindows:
                case Keys.RightWindows:
                case Keys.CapsLock:
                case Keys.NumLock:
                case Keys.Scroll:
                case Keys.None:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Gives keyboard focus to <paramref name="element"/> (null to drop
        /// it), firing the OnUnfocused/OnFocused pair the mouse path fires.
        /// The single place focus changes - <see cref="ClearFocus"/>,
        /// <see cref="FocusNext"/> and the press handler all route through
        /// here so no path can leave a stale focused element behind.
        /// </summary>
        public void SetFocus(Element element)
        {
            if (CurrentlyFocused == element)
            {
                return;
            }

            if (CurrentlyFocused != null && CurrentlyFocused.HasTrait<OnUnfocused>())
            {
                CurrentlyFocused.ElementTrait<OnUnfocused>().Value()?.TriggerAction?.Invoke(new TVEventArgs());
            }

            CurrentlyFocused = element;

            if (element != null && element.HasTrait<OnFocused>())
            {
                element.ElementTrait<OnFocused>().Value()?.TriggerAction?.Invoke(new TVEventArgs());
            }
        }

        /// <summary>
        /// Moves focus to the next (or, <paramref name="backwards"/>, the
        /// previous) focusable element, wrapping at the ends - what Tab does
        /// in every form ever written, and what a keyboard user needs in order
        /// to fill one in without reaching for the mouse.
        ///
        /// The ring is scoped to the TOP-LEVEL element the focused one lives
        /// under (the modal, the window), not the whole tree: tabbing out of a
        /// dialog into a field on the screen behind it - which is still very
        /// much in the tree, just covered up - would be worse than not tabbing
        /// at all. Order is by position, top row first then left to right,
        /// which is the order the fields were laid out in and the order they
        /// read in; child order would depend on the order a view happened to
        /// construct its controls.
        /// </summary>
        public bool FocusNext(bool backwards = false)
        {
            Element current = CurrentlyFocused;
            if (current == null)
            {
                return false;
            }

            Element top = current;
            while (top.Parent != null && top.Parent != Resources.StaticResources.RootWindow)
            {
                top = top.Parent;
            }

            var ring = new List<Element>();
            CollectFocusable(top, ring);
            if (ring.Count < 2)
            {
                return false;
            }

            ring.Sort((a, b) =>
            {
                Vector2 pa = a.GetActualXnaPosition();
                Vector2 pb = b.GetActualXnaPosition();
                int byRow = ((int)pa.Y).CompareTo((int)pb.Y);
                return byRow != 0 ? byRow : ((int)pa.X).CompareTo((int)pb.X);
            });

            int index = ring.IndexOf(current);
            if (index < 0)
            {
                return false;
            }

            int next = (index + (backwards ? -1 : 1) + ring.Count) % ring.Count;
            SetFocus(ring[next]);
            return true;
        }

        private static void CollectFocusable(Element element, List<Element> into)
        {
            if (element.CanBeInputFocused && element.HasTrait<OnFocused>())
            {
                into.Add(element);
            }

            ChildrenTrait children = element.CachedChildrenTrait;
            if (children == null)
            {
                return;
            }

            List<Element> items = children.Value().Items;
            for (int i = 0; i < items.Count; i++)
            {
                CollectFocusable(items[i], into);
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
            public readonly bool Middle;
            public readonly bool Right;

            public PointerEdge(int x, int y, bool left, bool middle, bool right)
            {
                X = x;
                Y = y;
                Left = left;
                Middle = middle;
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
        public void PushPointerEdge(int x, int y, bool leftDown, bool rightDown, bool middleDown = false)
        {
            pointerEdges.Add(new PointerEdge(x, y, leftDown, middleDown, rightDown));
        }

        /// <summary>
        /// Whether the host OS window is the active (focused) window — the
        /// host sets this every frame from its Game.IsActive BEFORE calling
        /// <see cref="Update"/>. While false, REAL polled input is
        /// neutralized: KNI's Mouse.GetState/Keyboard.GetState report global
        /// device state regardless of focus (found 2026-08-22, user report:
        /// clicks in a browser in front of the app were still landing on the
        /// app's elements behind it), so the polled mouse is replaced by a
        /// buttons-up state frozen at its last position with no scroll delta,
        /// and the polled keyboard by an empty state. Synthetic input (the
        /// remote-control API, <see cref="SetSyntheticMouseState"/>) bypasses
        /// the gate — driving an unfocused window is exactly its job. A press
        /// that loses focus mid-hold still gets its release edge (the neutral
        /// state reads as released next frame). Defaults to true so hosts
        /// that never set it keep today's behavior.
        /// </summary>
        public bool WindowActive { get; set; } = true;

        public void Update()
        {
            MouseState polledState = syntheticMouseState ?? Mouse.GetState();
            if (!syntheticMouseState.HasValue && !WindowActive)
            {
                // Inactive window: nothing the real mouse does reaches the
                // tree (see WindowActive). Position is frozen at the last
                // processed position so hover state doesn't churn either.
                polledState = new MouseState(
                    previousMouseState.X, previousMouseState.Y,
                    previousScrollWheelValue, 0, 0, 0,
                    ButtonState.Released, ButtonState.Released, ButtonState.Released,
                    ButtonState.Released, ButtonState.Released);
            }
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

            KeyboardState keyboardState = syntheticKeyboardState ?? (WindowActive ? Keyboard.GetState() : default(KeyboardState));
            CurrentKeyboardState = keyboardState;

            // While a text-input element is focused, newly pressed keys go to
            // it and keyboard SHORTCUT hooks are suppressed (typing "z" must
            // not trigger an undo hook).
            bool typing = CurrentlyFocused != null && CurrentlyFocused.CanBeInputFocused;
            if (typing)
            {
                bool shift = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);

                // Control travels with the keystroke so a focused field can
                // implement the clipboard shortcuts. It cannot look this up
                // itself — by the time a handler runs, the modifier may
                // already be released.
                bool control = keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl);

                if (repeatFocus != CurrentlyFocused)
                {
                    // Focus moved (Tab, or a click into another field): the
                    // key still physically down belongs to the field that has
                    // gone, not the one that just arrived.
                    repeatKey = Keys.None;
                    repeatFocus = CurrentlyFocused;
                }

                bool repeatKeyStillDown = false;
                foreach (Keys key in keyboardState.GetPressedKeys())
                {
                    if (key == repeatKey)
                    {
                        repeatKeyStillDown = true;
                    }

                    if (!previousKeyboardState.IsKeyDown(key))
                    {
                        CurrentlyFocused.HandleKeyInput(key, shift, control);

                        if (CanAutoRepeat(key))
                        {
                            repeatKey = key;
                            repeatKeyStillDown = true;
                            repeatNextSeconds = keyClock.Elapsed.TotalSeconds + KeyRepeatDelaySeconds;
                        }
                    }
                }

                if (!repeatKeyStillDown)
                {
                    repeatKey = Keys.None;
                }
                else if (keyClock.Elapsed.TotalSeconds >= repeatNextSeconds)
                {
                    // One repeat per frame, and the next one is scheduled from
                    // NOW rather than from the deadline just missed: a frame
                    // that took 200ms must not owe the field six keystrokes.
                    CurrentlyFocused.HandleKeyInput(repeatKey, shift, control);
                    repeatNextSeconds = keyClock.Elapsed.TotalSeconds + KeyRepeatIntervalSeconds;
                }
            }
            else
            {
                repeatKey = Keys.None;
                repeatFocus = null;
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
                        edge.Middle ? ButtonState.Pressed : ButtonState.Released,
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

            // Before the LEFT button's capture early-return below, so a middle
            // drag keeps getting frames even while something else holds the
            // left-button capture.
            ProcessMiddleButton(mouseState);
            ProcessRightButton(mouseState);

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
                IReadOnlyList<Element> pressTargets = ClickTargets(currentlyHovered);
                int clickCount = CountPress(pressTargets, mouseState);

                foreach (Element element in pressTargets.Where(e => e.HasTrait<OnMousePress>()))
                {
                    Dispatch(element, element.ElementTrait<OnMousePress>().Value(), mouseState, clickCount);
                }

                if (clickCount >= 2)
                {
                    foreach (Element element in pressTargets.Where(e => e.HasTrait<OnDoubleClickTrait>()))
                    {
                        Dispatch(element, element.ElementTrait<OnDoubleClickTrait>().Value(), mouseState, clickCount);
                    }
                }

                // The TOPMOST focusable under the pointer takes focus, not
                // every focusable in the stack in turn - focusing each one on
                // the way up fires a focus/unfocus pair at every field the
                // click passed over. (A press on nothing focusable leaves
                // focus where it is, as it always has.)
                Element focusTarget = currentlyHovered.LastOrDefault(e => e.HasTrait<OnFocused>());
                if (focusTarget != null)
                {
                    SetFocus(focusTarget);
                }
            }
            else if (mouseState.LeftButton == ButtonState.Pressed)
            {
                HaveInteracted = true;

                foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnMouseButtonHeldDown>()))
                {
                    Dispatch(element, element.ElementTrait<OnMouseButtonHeldDown>().Value(), mouseState);
                }
            }
            else if (mouseState.LeftButton == ButtonState.Released && previousMouseState.LeftButton == ButtonState.Pressed)
            {
                HaveInteracted = true;
                foreach (Element element in ClickTargets(currentlyHovered).Where(e => e.HasTrait<OnMouseRelease>()))
                {
                    Dispatch(element, element.ElementTrait<OnMouseRelease>().Value(), mouseState);
                }
            }

            // Right-button press edge → OnRightClickTrait on hovered elements.
            // The trait existed but was never dispatched; press-edge only (no
            // right-drag/capture semantics — capture stays left-button).
            if (mouseState.RightButton == ButtonState.Pressed && previousMouseState.RightButton == ButtonState.Released)
            {
                HaveInteracted = true;
                foreach (Element element in ClickTargets(currentlyHovered).Where(e => e.HasTrait<OnRightClickTrait>()))
                {
                    Dispatch(element, element.ElementTrait<OnRightClickTrait>().Value(), mouseState);
                }
            }



            UpdateHoverTransitions(mouseState);
            previousMouseState = mouseState;
        }

        /// <summary>
        /// The middle button's own press/held/release dispatch, mirroring the
        /// left button's but against <see cref="CapturedMiddleElement"/>.
        ///
        /// Separate from the left-button path rather than folded into it: that
        /// path returns early while a capture is live, and a middle drag has to
        /// survive that (the sequencer pans with the middle button while its
        /// own left-button marquee logic is wired to the same element). Nothing
        /// here touches the left button's state, so the two gestures are
        /// independent in both directions.
        /// </summary>
        private void ProcessMiddleButton(MouseState mouseState)
        {
            bool down = mouseState.MiddleButton == ButtonState.Pressed;
            bool wasDown = previousMouseState.MiddleButton == ButtonState.Pressed;

            if (CapturedMiddleElement != null)
            {
                Element captured = CapturedMiddleElement;

                if (down)
                {
                    HaveInteracted = true;
                    if (captured.HasTrait<OnMiddleMouseHeldDown>())
                    {
                        captured.ElementTrait<OnMiddleMouseHeldDown>().Value().TriggerAction?.Invoke(captured.GetClickArgs(mouseState));
                    }
                }
                else
                {
                    if (wasDown && captured.HasTrait<OnMiddleMouseRelease>())
                    {
                        captured.ElementTrait<OnMiddleMouseRelease>().Value().TriggerAction?.Invoke(captured.GetClickArgs(mouseState));
                    }

                    CapturedMiddleElement = null;
                }

                return;
            }

            if (down && !wasDown)
            {
                HaveInteracted = true;
                foreach (Element element in ClickTargets(currentlyHovered).Where(e => e.HasTrait<OnMiddleMousePress>()))
                {
                    Dispatch(element, element.ElementTrait<OnMiddleMousePress>().Value(), mouseState);
                }
            }
            else if (down)
            {
                HaveInteracted = true;
                foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnMiddleMouseHeldDown>()))
                {
                    Dispatch(element, element.ElementTrait<OnMiddleMouseHeldDown>().Value(), mouseState);
                }
            }
            else if (wasDown)
            {
                HaveInteracted = true;
                foreach (Element element in ClickTargets(currentlyHovered).Where(e => e.HasTrait<OnMiddleMouseRelease>()))
                {
                    Dispatch(element, element.ElementTrait<OnMiddleMouseRelease>().Value(), mouseState);
                }
            }
        }

        /// <summary>
        /// The right button's press/held/release dispatch, the same shape as
        /// <see cref="ProcessMiddleButton"/>.
        ///
        /// This does NOT replace the <see cref="OnRightClickTrait"/> dispatch
        /// further down: that one has consumers all over the app which want a
        /// press edge and nothing else, and it stays exactly where it was so
        /// their behaviour is untouched. An element declaring both simply hears
        /// about the press twice, which is what a context menu that also wants
        /// to know about dragging actually needs.
        /// </summary>
        private void ProcessRightButton(MouseState mouseState)
        {
            bool down = mouseState.RightButton == ButtonState.Pressed;
            bool wasDown = previousMouseState.RightButton == ButtonState.Pressed;

            if (CapturedRightElement != null)
            {
                Element captured = CapturedRightElement;

                if (down)
                {
                    HaveInteracted = true;
                    if (captured.HasTrait<OnRightMouseHeldDown>())
                    {
                        captured.ElementTrait<OnRightMouseHeldDown>().Value().TriggerAction?.Invoke(captured.GetClickArgs(mouseState));
                    }
                }
                else
                {
                    if (wasDown && captured.HasTrait<OnRightMouseRelease>())
                    {
                        captured.ElementTrait<OnRightMouseRelease>().Value().TriggerAction?.Invoke(captured.GetClickArgs(mouseState));
                    }

                    CapturedRightElement = null;
                }

                return;
            }

            if (down && !wasDown)
            {
                HaveInteracted = true;
                foreach (Element element in ClickTargets(currentlyHovered).Where(e => e.HasTrait<OnRightMousePress>()))
                {
                    Dispatch(element, element.ElementTrait<OnRightMousePress>().Value(), mouseState);
                }
            }
            else if (down)
            {
                HaveInteracted = true;
                foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnRightMouseHeldDown>()))
                {
                    Dispatch(element, element.ElementTrait<OnRightMouseHeldDown>().Value(), mouseState);
                }
            }
            else if (wasDown)
            {
                HaveInteracted = true;
                foreach (Element element in ClickTargets(currentlyHovered).Where(e => e.HasTrait<OnRightMouseRelease>()))
                {
                    Dispatch(element, element.ElementTrait<OnRightMouseRelease>().Value(), mouseState);
                }
            }
        }

        // Previous frame's hover list is cached rather than recomputed with a
        // second full ProcessHovers tree walk every frame.
        private List<Element> lastHoverList = new List<Element>();

        /// <summary>
        /// Advances the double/triple-click run for a press that just landed,
        /// and returns how many presses it now stands at.
        ///
        /// The run continues only while the presses stay on the same element
        /// AND within a few pixels of each other - the time gap alone is not
        /// enough, or clicking two adjacent list rows quickly would read as a
        /// double click on the second one.
        /// </summary>
        private int CountPress(IReadOnlyList<Element> targets, MouseState mouseState)
        {
            Element target = targets.Count > 0 ? targets[targets.Count - 1] : null;
            double now = keyClock.Elapsed.TotalSeconds;
            Point position = new Point(mouseState.X, mouseState.Y);

            bool continues = target == lastPressElement
                && now - lastPressSeconds <= MultiClickSeconds
                && Math.Abs(position.X - lastPressPosition.X) <= MultiClickSlopPixels
                && Math.Abs(position.Y - lastPressPosition.Y) <= MultiClickSlopPixels;

            pressRunCount = continues ? pressRunCount + 1 : 1;
            lastPressElement = target;
            lastPressSeconds = now;
            lastPressPosition = position;
            return pressRunCount;
        }

        /// <summary>
        /// Fires one mouse event at one element, tolerating the two states a
        /// dispatch loop can legitimately find mid-iteration.
        ///
        /// A trait can be DECLARED (via [ElementTraits]) and never SET, in
        /// which case Value() is null — HasTrait says yes and the old
        /// `.Value().TriggerAction` dereferenced it anyway. And a handler
        /// earlier in the same loop can tear the screen down (a button that
        /// opens a new view kills every sibling), leaving the elements after it
        /// detached; asking a detached element for its absolute position walks
        /// a null parent chain. Neither is a bug in the element — both are
        /// ordinary — and neither should take the process down mid-click, which
        /// is exactly what used to happen: an unhandled NullReferenceException
        /// straight out of the game loop (found 2026-08-23, clicking "New
        /// project" on the welcome screen).
        /// </summary>
        private static void Dispatch<T>(Element element, TVEvent<T> handler, MouseState mouseState, int clickCount = 1)
            where T : TVEventArgs
        {
            if (handler?.TriggerAction == null)
            {
                return;
            }

            ClickEventArgs args;
            try
            {
                args = element.GetClickArgs(mouseState);
            }
            catch (NullReferenceException)
            {
                return; // torn down by an earlier handler in this same loop
            }

            args.ClickCount = clickCount;

            if (args is T typed)
            {
                handler.TriggerAction.Invoke(typed);
            }
        }

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
        /// <summary>
        /// The hovered elements a CLICK should reach, honouring
        /// <see cref="Element.SwallowsPointer"/>: everything from the
        /// deepest swallowing element onward.
        ///
        /// <see cref="CollectHovered"/> appends parents before children, so
        /// the tail of the list from that element is exactly it plus its own
        /// descendants — its ancestors, which are what a swallowing control
        /// wants to shield, are the part dropped. Returns the list unchanged
        /// (no allocation, no reordering) when nothing swallows, which is
        /// every existing caller.
        /// </summary>
        private static IReadOnlyList<Element> ClickTargets(List<Element> hovered)
        {
            int start = -1;
            for (int i = hovered.Count - 1; i >= 0; i--)
            {
                if (hovered[i].SwallowsPointer)
                {
                    start = i;
                    break;
                }
            }

            return start <= 0 ? hovered : hovered.GetRange(start, hovered.Count - start);
        }

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
