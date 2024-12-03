using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    public static class Log
    {
        public const bool ENABLED = true;
        public static void This(string message)
        {
            if (ENABLED)
            {
                Console.WriteLine(message);
                Debug.WriteLine(message);
            }
        }
    }
}
