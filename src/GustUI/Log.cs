using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    internal static class Log
    {
        internal const bool ENABLED = true;
        internal static void This(string message)
        {
            if (ENABLED)
            {
                Console.WriteLine(message);
                Debug.WriteLine(message);
            }
        }
    }
}
