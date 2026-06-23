using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Sedivy pixel o 32 bitech.
    /// </summary>
    public class Gray32 :IPixel
    {
        /// <summary>
        /// 4 byte na pixel
        /// </summary>
        public int Count { get { return 4; } }

        public Int32 Value
        {
            get
            {
                return data[idx] + (data[idx + 1]  + (data[idx + 2] + data[idx + 3] * 256) * 256) * 256;
            }
            set
            {
                data[idx]=(byte)(value&0xff);
                data[idx+1] = (byte)((value >>8)&0xff);
                data[idx + 2] = (byte)((value >> 16) & 0xff);
                data[idx + 3] = (byte)((value >> 24) & 0xff);
            }
        }

        public Gray32()
        {
        }

        public Color Color
        {
            get
            {
                byte b = data[idx + 3];
                return new Color { R = b, G = b, B = b };
            }
            set
            {
                Value = 256* 256 * 256 * value.R;
            }
        }

        int[] b = new int[1];
        public int[] Values
        {
            get
            {
                b[0] = Value;
                return b;
            }
            set
            {
                Value = (byte)value[0];
            }
        }
        public override int GetHashCode()
        {
            return Value;
        }

        public override bool Equals(object obj)
        {

            if (obj is Gray32)
            {
                Gray32 g = (Gray32)obj;
                return Value == g.Value;
            }
            return false;
        }


        byte[] data;
        public byte[] Data
        {
            get
            {
                return data;
            }
            set
            {
                data = value;
            }
        }

        int idx;
        public int Index
        {
            get
            {
                return idx;
            }
            set
            {
                idx = value;
            }
        }
        /// <summary>
        /// Format pixelu
        /// </summary>
        //public PixelFormat Format
        //{
        //    get
        //    {
        //        return PixelFormats.Gray16;
        //    }
        //}
    }
}
