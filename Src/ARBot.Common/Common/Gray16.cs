using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Sedivy pixel o 16 bitech.
    /// </summary>
    public class Gray16 :IPixel
    {
        /// <summary>
        /// Dva byte na pixel
        /// </summary>
        public int Count { get { return 2; } }

        public int Value
        {
            get
            {
                return data[idx] + data[idx + 1] * 256;
            }
            set
            {
                data[idx]=(byte)(value&0xff);
                data[idx+1] = (byte)(value >>8);
            }
        }

        public Gray16()
        {
        }

        public Color Color
        {
            get
            {
                byte b = data[idx + 1];
                return new Color { R = b, G = b, B = b };
            }
            set
            {
                Value = 256*value.R;
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

            if (obj is Gray16)
            {
                Gray16 g = (Gray16)obj;
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
