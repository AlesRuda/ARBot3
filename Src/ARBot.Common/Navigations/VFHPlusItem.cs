using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    public class VFHPlusItem
    {
        public int Segment; // cislo segmentu, doplnuje se behem vypoctu
        public double Distance;	 // vzdalenost
        public double Beta;	 // smer relativne k smeru robota v radianech a matematickem smyslu
        public double Coeficient;    // duveryhodnots
    }
}
