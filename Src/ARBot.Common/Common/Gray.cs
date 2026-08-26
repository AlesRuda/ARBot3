using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ARBot.Common.Common
{
//    [StructLayout(LayoutKind.Sequential)]
    public class Gray :IPixel
    {
        public int Count { get { return 1; } }

        public byte Value
        {
            get
            {
                return data[index];
            }
            set
            {
                data[index]=value;
            }
        }

        public Gray()
        {
        }

        public Color Color
        {
            get
            {
                return new Color { R = Value, G = Value, B = Value };
            }
            set
            {
                Value = value.R;
            }
        }

        /// <summary>Sedy pixel vraci svou hodnotu ve vsech treh kanalech (viz <see cref="IPixel.R"/>).</summary>
        public byte R => Value;
        /// <inheritdoc cref="R"/>
        public byte G => Value;
        /// <inheritdoc cref="R"/>
        public byte B => Value;

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
            int v = 0;
            for (int i = 0; i < Values.Length; i++)
                v = v * 256 + Values[i];
            return v;
        }

        public override bool Equals(object obj)
        {

            if (obj is Gray)
            {
                Gray g = (Gray)obj;
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

        int index;
        public int Index
        {
            get
            {
                return index;
            }
            set
            {
                index = value;
            }
        }
        /// <summary>
        /// Format pixelu
        /// </summary>
        //public PixelFormat Format
        //{
        //    get
        //    {
        //        return PixelFormats.Gray8;
        //    }
        //}
    }
}
