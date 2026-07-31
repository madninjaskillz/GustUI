using GustUI.Elements;
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

        public bool HaveInteracted { get; private set; }
        private MouseState previousMouseState;
        private KeyboardState previousKeyboardState;
        private int previousScrollWheelValue;
        private List<Element> currentlyHovered = new List<Element>();
        private List<Element> currentlyClicked = new List<Element>();

        /// <summary>This frame's mouse state — read this instead of calling Mouse.GetState per element.</summary>
        public MouseState CurrentMouseState { get; private set; }

        /// <summary>While set, held/release mouse events route here regardless of hover.</summary>
        public Element CapturedPointerElement { get; private set; }

        public void CapturePointer(Element element) => CapturedPointerElement = element;

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
            public KeyboardHook(KeyboardShortcut shortcut, Action triggerAction)
            {
                Shortcut = shortcut;
                TriggerAction = triggerAction;
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


        public void Update()
        {
            MouseState mouseState = Mouse.GetState();
            CurrentMouseState = mouseState;
            KeyboardState keyboardState = Keyboard.GetState();
            int scrollWheel = mouseState.ScrollWheelValue;
            var triggeredHooks = Hooks.Where(x => keyboardState.IsKeyDown(x.Shortcut.Key));
            triggeredHooks = triggeredHooks.Where(x => !previousKeyboardState.IsKeyDown(x.Shortcut.Key));

            triggeredHooks = triggeredHooks.Where(x => x.Shortcut.Modifiers == null || x.Shortcut.Modifiers.Count == 0 || x.Shortcut.Modifiers.All(m => keyboardState.IsKeyDown(FromModifier(m))));
            foreach (KeyboardHook hook in triggeredHooks)
            {
                hook.TriggerAction();
            }
            
            currentlyHovered = ProcessHovers(Resources.StaticResources.RootWindow, mouseState.Position.ToVector2());

            if (scrollWheel != previousScrollWheelValue)
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
                previousKeyboardState = keyboardState;
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

            

            UpdateHoverTransitions(mouseState);
            previousMouseState = mouseState;
            previousKeyboardState = keyboardState;
        }

        // Previous frame's hover list is cached rather than recomputed with a
        // second full ProcessHovers tree walk every frame.
        private List<Element> lastHoverList = new List<Element>();

        private void UpdateHoverTransitions(MouseState mouseState)
        {
            var newlyHovered = currentlyHovered.Except(lastHoverList);
            var noLongerHovered = lastHoverList.Except(currentlyHovered);

            foreach (Element element in newlyHovered.Where(e => e.HasTrait<OnEnterTrait>()))
            {
                element.ElementTrait<OnEnterTrait>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
            }

            foreach (Element element in noLongerHovered.Where(e => e.HasTrait<OnExitTrait>()))
            {
                element.ElementTrait<OnExitTrait>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
            }

            foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnHoverTrait>()))
            {
                element.ElementTrait<OnHoverTrait>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
            }

            FloatedElementCount = currentlyHovered.Count;
            FloatedElementName = string.Join(", ", currentlyHovered.Select(e => e.ElementName));
            lastHoverList = currentlyHovered;
        }

        private List<Element> ProcessHovers(Element element, Vector2 position, int depth = 0, int root = -1, List<(int, Element)> HoveredElementsIndexed = null)
        {
            if (HoveredElementsIndexed == null)
            {
                HoveredElementsIndexed = new List<(int, Element)>();
            }
            if (depth == 0)
            {
                HoveredElementsIndexed.Clear();
            }

            if (element.IsMouseOver(position))
            {
                if (depth != 0)
                {
                    HoveredElementsIndexed.Add((root, element));
                }
                int ct = depth == 0 ? 0 : root;
                if (element.HasTrait<ChildrenTrait>())
                {
                    foreach (Element child in element.Children.Items)
                    {
                        ProcessHovers(child, position, depth + 1, ct, HoveredElementsIndexed);

                        if (depth == 0)
                        {
                            ct++;
                        }
                    }
                }
            }

            if (depth == 0)
            {
                if (HoveredElementsIndexed.Count > 0)
                {
                    int maxDepth = HoveredElementsIndexed.Max(e => e.Item1);
                    return HoveredElementsIndexed.Where(e => e.Item1 == maxDepth).Select(e => e.Item2).ToList();
                }
                else
                {
                    return new List<Element>();
                }
            }

            return null;
        }
    }
}
