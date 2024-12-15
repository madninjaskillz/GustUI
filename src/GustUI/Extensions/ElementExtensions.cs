using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Text.Json;

namespace GustUI.Extensions
{
    public static class ElementExtensions
    {
        public static TVVector GetSize(this Element element)
        {
            if (!element.HasTrait<SizeTrait>())
            {
                return null;
            }
            else
            {
                return element.ElementTrait<SizeTrait>().Value();
            }
        }
        public static TVVector GetActualPosition(this Element element, Vector2? thisSize = null, bool managedHorizontalJustification=false)
        {
            Element parentElement = element.Parent;
            TVVector actualPosition = new TVVector(0, 0);
            if (element.HasTrait<PositionTrait>())
            {
                actualPosition = element.ElementTrait<PositionTrait>().Value();
                if (parentElement != null && parentElement.HasTrait<PositionTrait>())
                {
                    var storePosition = actualPosition.AsXna;
                    actualPosition += parentElement.GetActualPosition();
                    
                    if (thisSize != null)
                    {
                        VerticalAlignment? vertAlign = element.HasTrait<VerticalAlignmentTrait>() ? element.ElementTrait<VerticalAlignmentTrait>().Value().Alignment : null;
                        HorizontalAlignment? horizAlign = element.HasTrait<VerticalAlignmentTrait>() ? element.ElementTrait<HorizontalAlignmentTrait>().Value().Alignment : null;

                        if (parentElement.HasTrait<SizeTrait>())
                        {
                            Vector2 pos = parentElement.GetActualPosition().AsXna;
                            Vector2 size = parentElement.ElementTrait<SizeTrait>().Value().AsXna;

                            if (horizAlign.HasValue && !managedHorizontalJustification)
                            {
                                switch (horizAlign.Value)
                                {
                                    case HorizontalAlignment.Left:
                                        actualPosition.X = pos.X+ storePosition.X; break;
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
                                        actualPosition.Y = pos.Y+ storePosition.Y; break;
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
