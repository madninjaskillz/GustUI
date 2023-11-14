using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Extensions
{
    public static class LazyMathExtensions
    {
        public static int AsInt(this float integerValue)=>(int)integerValue;
    }
}
