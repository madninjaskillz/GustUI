using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GustUI
{
    internal static class Debugging
    {
        [DebuggerHidden]
        [Conditional("DEBUG")]
        internal static void DebugBreak()
        {
            if (!System.Diagnostics.Debugger.IsAttached)
            {
                System.Diagnostics.Debugger.Launch();
                Thread.Sleep(1000);
            }

            if (System.Diagnostics.Debugger.IsAttached)
                System.Diagnostics.Debugger.Break();
        }
    }
}
