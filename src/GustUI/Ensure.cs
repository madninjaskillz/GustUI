using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    internal static class Ensure
    {
        private const bool THROW = false;
        private const bool LOG = true;
        public static bool NotNull(this object obj, string propName, [CallerMemberName] string memberName = "")
        {
            if (obj == null)
            {
                if (THROW)
                {
                    throw new Exception("Object is null: " + propName + " (" + memberName + ")");
                }

                if (LOG)
                {
                    Log.This("Object is null: " + propName + " (" + memberName + ")");
                }


                return false;
            }

            return true;
        }
    }
}
