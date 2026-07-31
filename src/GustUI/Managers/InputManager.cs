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
            for (int i = 0; i < Hooks.Count; i++)
            {
                KeyboardHook hook = Hooks[i];
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
            TVVector size = sizeTrait.Value();

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
