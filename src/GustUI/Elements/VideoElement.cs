using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Elements
{
    public class VideoElement : NativeElement
    {
        private Video vid;
        private VideoPlayer player;
        public VideoElement(Video video)
        {
            vid = video;
            player = new VideoPlayer();
            player.IsLooped = true;
            
            Set<SizeTrait>(new TVVector(video.Width, video.Height));
        }

        public override void DrawToRenderTarget()
        {
            if (player.State == MediaState.Stopped)
            {
                player.Play(vid);
            }
            Resources.StaticResources.DrawManager.Draw(player.GetTexture(), new Microsoft.Xna.Framework.Rectangle(0, 0, vid.Width, vid.Height), Microsoft.Xna.Framework.Color.White);
            base.DrawToRenderTarget();
        }
    }
}
