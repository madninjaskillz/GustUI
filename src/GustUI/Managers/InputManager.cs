using GustUI.Elements;
using GustUI.Traits;
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

        public bool HaveInteracted { get; private set; }
        private MouseState previousMouseState;
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

        public ElementState GetElementState(Element element)
        {
            if (currentlyClicked.Contains(element)) { return ElementState.Pressed; }
            if (currentlyHovered.Contains(element)) { return ElementState.Hovered; }
            return ElementState.Normal;
        }

        private List<Element> currentlyHovered = new List<Element>();
        private List<Element> currentlyClicked = new List<Element>();

        internal int FloatedElementCount { get; private set; }
        internal string FloatedElementName { get; private set; }

        public void Update()
        {
            MouseState mouseState = Mouse.GetState();

            currentlyHovered = ProcessHovers(Resources.StaticResources.RootWindow, mouseState.Position.ToVector2());

            if (mouseState.LeftButton == ButtonState.Pressed)
            {
                currentlyClicked = currentlyHovered.Where(e => e.HasTrait<OnMousePress>() || e.HasTrait<OnClickTrait>()).ToList();
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
            }
            else if (mouseState.LeftButton == ButtonState.Released && previousMouseState.LeftButton == ButtonState.Pressed)
            {
                HaveInteracted = true;

                foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnClickTrait>()))
                {
                    element.ElementTrait<OnClickTrait>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
                }

                foreach (Element element in currentlyHovered.Where(e => e.HasTrait<OnMouseRelease>()))
                {
                    element.ElementTrait<OnMouseRelease>().Value().TriggerAction?.Invoke(element.GetClickArgs(mouseState));
                }
            }

            var previouslyHovered = ProcessHovers(Resources.StaticResources.RootWindow, previousMouseState.Position.ToVector2());
            var newlyHovered = currentlyHovered.Except(previouslyHovered);
            var noLongerHovered = previouslyHovered.Except(currentlyHovered);

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
            previousMouseState = mouseState;
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
