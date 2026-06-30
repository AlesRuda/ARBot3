using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.HAL;

namespace ARBot.HALLinux
{
    public class HcSr04:ISonar
    {
        private IMMR mmr;
        private double cnt2len;
        private int baseAdr;
        private int cnt;
        private int pingAdr;
        private uint pingMask;

        public HcSr04(IMMR mmr, double freq, int baseAdr, int cnt, int pingAdr, uint pingMask)
        {
            this.cnt2len = 170.0 / freq;
            this.mmr = mmr;
            this.baseAdr = baseAdr;
            this.cnt = cnt;
            this.pingAdr = pingAdr;
            this.pingMask = pingMask;
        }
        public int Count
        {
            get { return cnt; }
        }

        public void Ping()
        {
            mmr.Set32(pingAdr + RegisterFile.OrOffset, pingMask);
            //            Logger..WriteLine(string.Format("2 {0:x08}", mmr.Get32(0)));
            mmr.Set32(pingAdr + RegisterFile.XorOffset, pingMask);
        }

        /// <summary>
        /// Returns distance in meters
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        public double? Distance(int num)
        {
            double l = mmr.Get32(baseAdr + num);
            double d = cnt2len * l;
            if (d > 5)
                return null;

            return d;
        }
    }
}
