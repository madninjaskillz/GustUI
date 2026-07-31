using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Text.Json;

namespace GustUI.Extensions
{
    public static class ElementExtensions
    {
        public static TVVector GetSize(this Element element)
        {
            if (!element.HasTrait<SizeTrait>())
            {
                return new TVVector(0,0);
            }
            else
            {
                if (element.SizeFitsChildren && element.HasTrait<ChildrenTrait>())
                {
                    var items = element.Children.Items;
                    Vector2 mx = Vector2.Zero;
                    foreach(var item in items)
                    {
                        var size = (item.Parent is VerticalStackElement ? Vector2.Zero : item.GetRelativePosition().AsXna) + item.GetSize().AsXna;
                        if (size.X > mx.X || size.Y > mx.Y)
                        {
                            mx = new Vector2(float.Max(size.X, mx.X), float.Max(size.Y, mx.Y));
                        }
                    }
                    return new TVVector(mx);
                }
                else
                {
                    return element.ElementTrait<SizeTrait>().Value();
                }
            }
        }
        public static TVVector GetRelativePosition(this Element element)
        {
            if (!element.HasTrait<PositionTrait>())
            {
                return new TVVector(0,0);
            }
            else
            {
                return element.ElementTrait<PositionTrait>().Value();
            }
        }
        public static TVVector GetActualPosition(this Element element, Vector2? thisSize = null, bool managedHorizontalJustification=false)
        {
            // Kept for compatibility; the allocation-free core below does the
            // work, so a call allocates one TVVector instead of one per
            // ancestor (plus operator+ temporaries).
            return new TVVector(element.GetActualXnaPosition(thisSize, managedHorizontalJustification));
        }

        /// <summary>
        /// Allocation-free core of <see cref="GetActualPosition"/>: identical
        /// semantics, returns the struct <see cref="Vector2"/> directly.
        /// Prefer this in per-frame Draw/hit-test code.
        /// </summary>
        public static Vector2 GetActualXnaPosition(this Element element, Vector2? thisSize = null, bool managedHorizontalJustification = false)
        {
            if (!element.HasTrait<PositionTrait>())
            {
                return Vector2.Zero;
            }

            TVVector relativePosition = element.ElementTrait<PositionTrait>().Value();
            Vector2 actualPosition = relativePosition.AsXna;
            Element parentElement = element.Parent;
            if (parentElement != null && parentElement.HasTrait<PositionTrait>())
            {
                Vector2 storePosition = actualPosition;
                Vector2 parentPosition = parentElement.GetActualXnaPosition();
                actualPosition += parentPosition;

                if (thisSize != null)
                {
                    VerticalAlignment? vertAlign = element.HasTrait<VerticalAlignmentTrait>() ? element.ElementTrait<VerticalAlignmentTrait>().Value().Alignment : null;
                    HorizontalAlignment? horizAlign = element.HasTrait<VerticalAlignmentTrait>() ? element.ElementTrait<HorizontalAlignmentTrait>().Value().Alignment : null;

                    if (parentElement.HasTrait<SizeTrait>())
                    {
                        Vector2 pos = parentPosition;
                        Vector2 size = parentElement.ElementTrait<SizeTrait>().Value().AsXna;

                        if (horizAlign.HasValue && !managedHorizontalJustification)
                        {
                            switch (horizAlign.Value)
                            {
                                case HorizontalAlignment.Left:
                                    actualPosition.X = pos.X + storePosition.X; break;
                                case HorizontalAlignment.Right:
                                    actualPosition.X = (pos.X + (size.X - thisSize.Value.X)); break;
                                case HorizontalAlignment.Center:
                                    actualPosition.X = (pos.X + size.X / 2 - (thisSize.Value.X / 2)); break;
                                    //why doesnt this work adding store position?
                            }

                        }

                        if (vertAlign.HasValue)
                        {
                            switch (vertAlign.Value)
                            {
                                case VerticalAlignment.Top:
                                    actualPosition.Y = pos.Y + storePosition.Y; break;
                                case VerticalAlignment.Bottom:
                                    actualPosition.Y = (pos.Y + (size.Y - thisSize.Value.Y)); break;
                                case VerticalAlignment.Center:
                                    actualPosition.Y = (pos.Y + size.Y / 2 - (thisSize.Value.Y / 2)); break;
                                    //why doesnt this work adding store position?
                            }
                        }
                    }
                }
            }

            return actualPosition;
        }

        public static void Debug(this Element element, int depth = 0)
        {
            string r = "";
            for (int i = 0; i < depth; i++) { r = r + "  "; }
            r = r + "> " + element.ElementName;
            Log.This(r);
            if (element.HasTrait<ChildrenTrait>())
            {
                var children = element.Children;

                foreach(var child in children.Items)
                {
                    child.Debug(depth + 1);
                }
            }
        }

    }
}
