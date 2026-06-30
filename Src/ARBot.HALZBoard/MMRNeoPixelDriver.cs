using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.HAL;

namespace ARBot.HALLinux
{
    public class MMRNeoPixelDriver : INeoPixelDriver
    {

        const int PixelOff=0;
        const int IndexOff = 1;
        const int StartOff = 2;
        const int CountOff = 3;

        public const int PixelCount=1024;

        IMMR mmr;
        int baseAdr;

        public MMRNeoPixelDriver(IMMR mmr, int baseAdr)
        {
            this.mmr = mmr;
            this.baseAdr = baseAdr;
        }

        public int Index
        {
            get
            {
                return (int)mmr.Get32(baseAdr + IndexOff);
            }
            set
            {
                mmr.Set32(baseAdr + IndexOff, (uint)value);
            }
        }
        public int Start
        {
            get
            {
                return (int)mmr.Get32(baseAdr + StartOff);
            }
            set
            {
                mmr.Set32(baseAdr + StartOff, (uint)value);
            }
        }

        public int Count
        {
            get
            {
                return (int)mmr.Get32(baseAdr + CountOff);
            }
            set
            {
                mmr.Set32(baseAdr + CountOff, (uint)value);
            }
        }

        /// <summary>
        /// RGB color
        /// </summary>
        public int Color
        {
            get
            {
                uint v = mmr.Get32(baseAdr + PixelOff);
                return (int)v;
            }
            set
            {
                uint v = (uint)value;
                mmr.Set32(baseAdr + PixelOff, v);
            }
        }

        public int this[int adr]
        {
            get
            {
                Index = adr;
                return Color;
            }
            set
            {
                Index = adr;
                Color = value;
            }
        }

        public void Send(Color[] values)
        {
            for(int i=0;i<values.Length;i++)
            {
                Color c=values[i];
                int val = (((int)c.G) << 16) + (((int)c.R) << 8) + c.B;
                this[i] = val;
            }
            Start = 0;
            Count = values.Length;
        }
    }
}
