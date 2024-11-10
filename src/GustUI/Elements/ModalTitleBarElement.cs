using GustUI.Attributes;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Elements
{
    [ElementTraits(typeof(TitleTextTrait), typeof(FontTrait), typeof(OnExitTrait))]
    public class ModalTitleBarElement : FilledRectangleElement
    {
        private TextElement textElement = new TextElement();
        private BasicButtonElement closeButton = new BasicButtonElement();

        public ModalTitleBarElement()
        {
            Sync(textElement);
            Sync(closeButton);
            Setup();
        }

        public ModalTitleBarElement(TVFont font, string title, TVVector position = null, TVVector size = null)
        {
            Sync(textElement);
            Sync(closeButton);
            Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Gray));
            Set<BorderSizeTrait>(new TVInt(0));
            Set<FontTrait>(font);
            Set<TitleTextTrait>(new TVText(title));
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));
            Setup();
        }

        private void Setup()
        {
            Set<BorderSizeTrait, TVInt>(new TVInt(2));
            MapTraitToChild<TitleTextTrait>(textElement, nameof(TextTrait));
            ElementTrait<ChildrenTrait>().Value().Items.Add(textElement);

            Sync();

        }
    }
}
