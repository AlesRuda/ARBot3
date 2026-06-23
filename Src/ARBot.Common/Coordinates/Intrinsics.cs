using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Coordinates
{
    /// <summary>
    /// Popisuje zkresleni kamery
    /// </summary>
    public class Intrinsics
    {
        /// <summary>
        /// Model zkresleni
        /// </summary>
        public enum Distortion
        {
            None = 0,
            ModifiedBrownConrady = 1,
            InverseBrownConrady = 2,
            Ftheta = 3,
            BrownConrady = 4
        }
        /// <summary>
        /// Sirka
        /// </summary>
        public int Width;
        /// <summary>
        /// Vyska
        /// </summary>
        public int Height;
        /// <summary>
        /// X sourednice stredu
        /// </summary>
        public float PPx;
        /// <summary>
        /// Y souradnice stredu
        /// </summary>
        public float PPy;
        /// <summary>
        /// Ohniskova vzdalenost v x smeru
        /// </summary>
        public float Fx;
        /// <summary>
        /// Ohniskova vzdalenost v y smeru
        /// </summary>
        public float Fy;
        public Distortion Model;
        /// <summary>
        /// Koeficienty modelu
        /// </summary>
        public float[] Coeffs;

        public Intrinsics()
        {
        }
        /// <summary>
        /// Konstruktor kompatibilni s puvodnim modelem
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="viewF"></param>
        /// <param name="pixelWidth"></param>
        public Intrinsics(int width, int height, float viewF, float pixelWidth)
        {
            Fx = Fy = viewF / pixelWidth;
            Height = height;
            Width = width;
            PPx = width/2;
            PPy = height/2;
            Model = Distortion.None;
        }
        /// <summary>
        /// Odpovida konstrukci z parametru MATLABu
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="k"></param>
        /// <param name="radialDistortion"></param>
        /// <param name="tangentialDistortion"></param>
        public Intrinsics(int width, int height, Matrix k, Matrix radialDistortion, Matrix tangentialDistortion)
        {
            Height = height;
            Width = width;

            Fx = (float)k[0, 0];
            Fy = (float)k[1, 1];
//            s = k[1, 0];
            PPx = (float)k[2, 0];
            PPy = (float)k[2, 1];

            Coeffs = new float[5];
            Coeffs[0]= (float)radialDistortion[0, 0];
            Coeffs[1] = (float)radialDistortion[1, 0];
            Coeffs[4] = (float)radialDistortion[2, 0];

            Coeffs[2] = (float)tangentialDistortion[0, 0];
            Coeffs[3] = (float)tangentialDistortion[1, 0];
            Model = Distortion.ModifiedBrownConrady;

            Simplify();
        }
        /// <summary>
        /// Inverzni transformace, ale jen omezene
        /// </summary>
        /// <returns></returns>
        public Intrinsics Inverse()
        {
            if (Model == Distortion.None)
                return this;
            var ii = new Intrinsics();
            ii.Coeffs = Coeffs;
            ii.Fx = Fx;
            ii.Fy = Fy;
            ii.Height = Height;
            ii.Width = Width;
            ii.PPx = PPx;
            ii.PPy = PPy;
            if(Model == Distortion.BrownConrady)
                ii.Model = Distortion.InverseBrownConrady;
            else if (Model == Distortion.InverseBrownConrady)
                ii.Model = Distortion.BrownConrady;
            else if (Model == Distortion.None)
                ii.Model = Distortion.None;
            return ii;
        }

        public void Simplify()
        {
            if (Coeffs.All(f => f == 0))
                Model = Intrinsics.Distortion.None;
        }

        public override string ToString()
        {
            return string.Format("Model={0}, Width={1}, Height={2}, PPx={3}, PPy={4}, Fx={5}, Fy={6}, Coefs={7}", Model, Width, Height, PPx, PPy, Fx, Fy, string.Join("; ", Coeffs?.Select(i=>i.ToString())));
        }
        /// <summary>
        /// Transformace pro testovaci ucely
        /// </summary>
        public static Intrinsics TestDepth
        {
            get
            {
                var i = new Intrinsics();
                i.Width = 480;
                i.Height = 270;
                i.PPx = 242.5034F;
                i.PPy = 134.5324F;
                i.Fx = 241.5518F;
                i.Fy = 241.5518F;
                i.Model = Distortion.None;
                i.Coeffs = new float[] { 0, 0, 0, 0, 0 };
                return i;
            }
        }
        /// <summary>
        /// Transformace pro testovaci ucely
        /// </summary>
        public static Intrinsics TestColor
        {
            get
            {
                var i = new Intrinsics();
                i.Width = 640;
                i.Height = 480;
                i.PPx = 320;
                i.PPy = 240;
                i.Fx = 614;
                i.Fy = 614F;
                i.Model = Distortion.None;
                i.Coeffs = new float[] { 0, 0, 0, 0, 0 };
                return i;
            }
        }
    }
}
