using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Point4D
    {
        public float X, Y, Z, A;
        public Point4D Transform(Matrix4x4 m)
        {
            // 1. Vytvoříme standardní 3D vektor ze stávajících souřadnic
            Vector3 p1 = new Vector3((float)X, (float)Y, (float)Z);

            // 2. Provedeme transformaci maticí.
            // Vector3.Transform automaticky správně aplikuje rotaci i translaci (posun) z matice 4x4.
            var p = Vector3.Transform(p1, m);

            // 3. Vrátíme homogenní 4D vektor (vektor se čtyřmi složkami X, Y, Z, W).
            // V System.Numerics se pro 4D body/vektory používá Vector4 a čtvrtá složka se jmenuje 'W' (odpovídá tvému 'A').
            var pp = new Point4D();
            pp.X = (float)p.X;
            pp.Y = (float)p.Y;
            pp.Z = (float)p.Z;
            pp.A = 1;
            return pp;
        }

        public static Point4D operator -(Point4D x1, Point4D x2)
        {
            return new Point4D() { X = x1.X - x2.X, Y = x1.Y - x2.Y, Z = x1.Z - x2.Z, A = 1 };
        }
        public static Point4D operator +(Point4D x1, Point4D x2)
        {
            return new Point4D() { X = x1.X + x2.X, Y = x1.Y + x2.Y, Z = x1.Z + x2.Z, A = 1 };
        }
        public static float operator *(Point4D x1, Point4D x2)
        {
            return x1.X * x2.X+ x1.Y * x2.Y+ x1.Z * x2.Z+x1.A*x2.A;
        }
        public static Point4D operator *(Point4D x, float m)
        {
            return new Point4D() { X = x.X*m, Y = x.Y*m, Z = x.Z*m, A = 1 };
        }

        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public float Length2 => (float)(X * X + Y * Y + Z * Z);
        public static Point4D Invalid => new Point4D() { X = 0, Y = 0, Z = 0, A = 0 };
        public static Point4D Zero => new Point4D() { X = 0, Y = 0, Z = 0, A = 1 };

        public override string ToString()
        {
            if (A != 1)
                return "Invalid";
            return $"{X}, {Y}, {Z}";
        }

        public static Point4D Sum(IEnumerable<Point4D> e)
        {
            var p = new Point4D();
            foreach (var v in e)
            {
                p.X += v.X;
                p.Y += v.Y;
                p.Z += v.Z;
                p.A += v.A;
            }
            return p;
        }
        public static Point4D Avg(IEnumerable<Point4D> e)
        {
            var p = Sum(e);
            if(p.A>0)
            {
                p.X /= p.A;
                p.Y /= p.A;
                p.Z /= p.A;
                p.A = 1;
            }

            return p;
        }
        public static IEnumerable<Point4D> Add(IEnumerable<Point4D> e, Point4D a)
        {
            return e.Select(v=>v+a);
        }
    }
}
