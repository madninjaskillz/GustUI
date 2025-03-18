using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.TraitValues
{
    public class TVSize :TraitValue
    {
        public TVSize() { }
        public int PixelSize { get; set; }
        public float PercentSize { get; set; }
        public bool ContentBased { get; set; }
        public int AutoUnits { get; set; }

    }

    public class TVPixelSize : TVSize
    {
     
    }

    public class TVPercentSize : TVSize { }


}
