using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Extensions
{
    public static class ElementExtensions
    {
        public static TVVector GetActualPosition(this Element element, Element parentElement, Vector2? thisSize = null)
        {
            TVVector actualPosition = new TVVector(0, 0);
            if (element.HasTrait<PositionTrait>())
            {
                actualPosition = element.ElementTrait<PositionTrait>().Value();
                if (parentElement!=null && parentElement.HasTrait<PositionTrait>())
                {
                    actualPosition += parentElement.ElementTrait<PositionTrait>().Value();

                    if (thisSize != null)
                    {
                        VerticalAlignment? vertAlign = element.HasTrait<VerticalAlignmentTrait>() ? element.ElementTrait<VerticalAlignmentTrait>().Value().Alignment : null;
                        HorizontalAlignment? horizAlign = element.HasTrait<VerticalAlignmentTrait>() ? element.ElementTrait<HorizontalAlignmentTrait>().Value().Alignment : null;

                        if (parentElement.HasTrait<SizeTrait>())
                        {
                            Vector2 pos = parentElement.ElementTrait<PositionTrait>().Value().AsXna;
                            Vector2 size = parentElement.ElementTrait<SizeTrait>().Value().AsXna;

                            if (horizAlign.HasValue)
                            {
                                switch (horizAlign.Value)
                                {
                                    case HorizontalAlignment.Left:
                                        actualPosition.X = pos.X; break;
                                    case HorizontalAlignment.Right:
                                        actualPosition.X = pos.X + (size.X - thisSize.Value.X); break;
                                    case HorizontalAlignment.Center:
                                        actualPosition.X = pos.X + size.X / 2 - (thisSize.Value.X / 2); break;
                                }

                            }

                            if (vertAlign.HasValue)
                            {
                                switch (vertAlign.Value)
                                {
                                    case VerticalAlignment.Top:
                                        actualPosition.Y = pos.Y; break;
                                    case VerticalAlignment.Bottom:
                                        actualPosition.Y = pos.Y + (size.Y - thisSize.Value.Y); break;
                                    case VerticalAlignment.Center:
                                        actualPosition.Y = pos.Y + size.Y / 2 - (thisSize.Value.Y / 2); break;
                                }
                            }
                        }
                    }
                }
            }

            return actualPosition;
        }
    }
}
