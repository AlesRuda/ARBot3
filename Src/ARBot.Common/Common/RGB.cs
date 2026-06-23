using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ARBot.Common.Common
{
    public class RGB :IPixel
    {
        public int Count { get { return 3; } }
        public byte R
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

        public byte G
        {
            get
            {
                return data[index+1];
            }
            set
            {
                data[index+1] = value;
            }
        }
        public byte B
        {
            get
            {
                return data[index+2];
            }
            set
            {
                data[index+2] = value;
            }
        }

        public RGB()
        {
        }

        public Color Color
        {
            get
            {
                return new Color {R=R, G=G, B=B};
            }
            set
            {
                R = value.R;
                G = value.G;
                B = value.B;
            }
        }

        int[] b = new int[3];
        public int[] Values
        {
            get
            {
                b[0] = R;
                b[1] = G;
                b[2] = B;
                return b;
            }
            set
            {
                R = (byte)value[0];
                G = (byte)value[1];
                B = (byte)value[2];
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

            if (obj is RGB)
            {
                RGB rgb = (RGB)obj;
                return R == rgb.R && G == rgb.G && B == rgb.B;
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
        //        return PixelFormats.Rgb24;
        //    }
        //}
    }
}
