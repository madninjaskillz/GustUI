using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Exceptions
{
    public class GustException : Exception
    {
        public GustException(string message) : base(message)
        {
        }
    }
}
