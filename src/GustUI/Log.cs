using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    public static class Log
    {
        internal static Queue log = new Queue();
        internal static int logIndex = 0;
        internal static int logPointer = 0;

        public const bool ENABLED = true;
        public static void This(string message)
        {
            if (ENABLED)
            {
                Console.WriteLine(message);
                Debug.WriteLine(message);
            }

            log.Enqueue(DateTime.Now+": "+message);
            while (log.Count > 100)
            {
                log.Dequeue();
            }
        }
    }
}
