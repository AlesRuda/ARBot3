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

        /// <summary>
        /// Sedy pixel vraci svou hodnotu ve vsech treh kanalech (viz <see cref="IPixel.R"/>).
        ///
        /// <para><b>Bere horni bajt</b> (<c>Value / 256</c>), tedy stejnou konvenci jako
        /// <see cref="Color"/> — ne saturaci na 255. Duvod je jednak konzistence (jinak by tentyz
        /// pixel hlasil jinou cervenou pres <c>R</c> a jinou pres <c>Color.R</c>), jednak to, ze
        /// saturace by u hloubkoveho obrazu v milimetrech udelala z celeho obrazu bilou placku,
        /// zatimco skalovani zachova prubeh.</para>
        /// </summary>
        public byte R => data[idx + 1];
        /// <inheritdoc cref="R"/>
        public byte G => data[idx + 1];
        /// <inheritdoc cref="R"/>
        public byte B => data[idx + 1];

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
