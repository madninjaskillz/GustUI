using GustUI.Elements;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Managers
{
    public class DrawOOPManager
    {
        private Dictionary<Element, Action<SpriteBatch>> _drawOutOfProcess = new Dictionary<Element, Action<SpriteBatch>>();
        public void Draw(SpriteBatch spriteBatch)
        {
            foreach (var draw in _drawOutOfProcess.Values)
            {
                draw(spriteBatch);
            }
        }

        internal void Register(Element element, Action<SpriteBatch> drawOutOfProcess)
        {
            _drawOutOfProcess.Add(element, drawOutOfProcess);
        }

        internal void Unregister(Element element)
        {
            _drawOutOfProcess.Remove(element);
        }
    }
}
