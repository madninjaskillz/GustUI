using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    public enum DebugMode
    {
        None = 0,
        FPS = 1,
        Detailed = 2,
        Outlines = 3,
        Mini = 4,
        Full = 5
    }

    public static class DebugModeExtensions
    {
        public static void Next(this ref DebugMode mode)
        {
            int dbg = (int)mode;
            dbg++;
            if (dbg > 5)
            {
                dbg = 0;
            }
            mode = (DebugMode)dbg;
        }

    }
}
