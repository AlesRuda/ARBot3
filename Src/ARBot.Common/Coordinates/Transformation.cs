using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Coordinates
{
    public class Transformation
    {
        public Matrix<double> Rotation { get; private set; }
        public Matrix<double> Offset { get; private set; }

        public Transformation()
        {
            Reset();
        }

        public Transformation(LLA lla, bool back)
            : this()
        {
            Rotate(lla.Latitude, lla.Longitude, back);
        }


        /// <summary>
        /// Pootaci souradnicovy system
        /// </summary>
        /// <param name="la">Zemepisna sirka v radianech. S nulou na rovniku. Severni pol ma 90 stupnu.</param>
        /// <param name="lo">Zemepisna delka v radianech. S nulou na nultem poledniku. Roste smerem na vychod.</param>
        /// <param name="back"></param>
        public Transformation(double latitude, double longitude, bool back)
            : this()
        {
            Rotate(latitude, longitude, back);
        }

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="ecef"></param>
        /// <param name="back">true - pootoci soustavu tak, ze osa X prochazi bodem ecef, tj. Y se da pouzit jako x a Z jako y pro kresleni</param>
        public Transformation(ECEF ecef, bool back)
            : this()
        {
            Rotate(ecef, back);
        }

        /// <summary>
        /// Vynuluje posunuti
        /// </summary>
        public void ResetOffset()
        {
            Offset = Matrix<double>.Build.Dense(3, 1);
        }
        /// <summary>
        /// Vynuluje rotaci
        /// </summary>
        public void ResetRotation()
        {
            Rotation = Matrix<double>.Build.DenseIdentity(3);
        }
        /// <summary>
        /// Vynuluje posunuti a rotaci
        /// </summary>
        public void Reset()
        {
            ResetRotation();
            ResetOffset();
        }
        /// <summary>
        /// Zvetseni
        /// </summary>
        public double Scale
        {
            get
            {
                return Math.Sqrt(Rotation[0, 0] * Rotation[0, 0] + Rotation[1, 0] * Rotation[1, 0] + Rotation[2, 0] * Rotation[2, 0]);
            }
            set
            {
                double s = Scale;
                if (s != value)
                {
                    Rotation = Rotation * (value / s);
                }
            }
        }

        /// <summary>
        /// Otaci souradnicovou soustavu podle osy Z.
        /// </summary>
        /// <param name="angle">Pootoceni v radianech, pouziva matematicky smer (proti smeru hodinek).</param>
        /// <returns></returns>
        public void RotateZ(double angle)
        {
            var t = Matrix<double>.Build.Dense(3, 3);
            t[0, 0] = Math.Cos(angle);
            t[0, 1] = -Math.Sin(angle);
            t[1, 0] = Math.Sin(angle);
            t[1, 1] = Math.Cos(angle);
            t[2, 2] = 1;

            Rotation = Rotation * t;
        }

        /// <summary>
        /// Otaci souradnicovou soustavu podle osy Y.
        /// </summary>
        /// <param name="angle">Pootoceni v radianech, pouziva matematicky smer (proti smeru hodinek).</param>
        /// <returns></returns>
        public void RotateY(double angle)
        {
            var t = Matrix<double>.Build.Dense(3, 3);
            t[0, 0] = Math.Cos(angle);
            t[1, 1] = 1;
            t[2, 2] = Math.Cos(angle);
            t[0, 2] = Math.Sin(angle);
            t[2, 0] = -Math.Sin(angle);

            Rotation = Rotation * t;
        }

        /// <summary>
        /// Otaci souradnicovou soustavu podle osy X.
        /// </summary>
        /// <param name="angle">Pootoceni v radianech, pouziva matematicky smer (proti smeru hodinek).</param>
        /// <returns></returns>
        public void RotateX(double angle)
        {
            var t = Matrix<double>.Build.Dense(3, 3);
            t[0, 0] = 1;
            t[1, 1] = Math.Cos(angle);
            t[2, 2] = Math.Cos(angle);
            t[1, 2] = -Math.Sin(angle);
            t[2, 1] = Math.Sin(angle);

            Rotation = Rotation * t;
        }
        /// <summary>
        /// Pootaci souradnicovy system
        /// </summary>
        /// <param name="la">Zemepisna sirka v radianech. S nulou na rovniku. Severni pol ma 90 stupnu.</param>
        /// <param name="lo">Zemepisna delka v radianech. S nulou na nultem poledniku. Roste smerem na vychod.</param>
        /// <param name="back"></param>
        public void Rotate(double la, double lo, bool back)
        {
            if(back)
            {
                RotateZ(lo);
                RotateY(-la);
            }
            else
            {
                RotateY(la);
                RotateZ(-lo);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="ecef"></param>
        /// <param name="back">true - pootoci soustavu tak, ze osa X procazi bodem ecef, tj. Y se da pouzit jako x a Z jako y pro kresleni</param>
        public void Rotate(ECEF ecef, bool back)
        {
            double la = Math.Atan2(ecef.Z, Math.Sqrt(ecef.X * ecef.X + ecef.Y * ecef.Y));
            double lo = Math.Atan2(ecef.Y, ecef.X);
            Rotate(la, lo, back);
        }

        public void Move(double x, double y, double z)
        {
            Offset[0, 0] += x;
            Offset[1, 0] += y;
            Offset[2, 0] += z;
        }

        public ECEF Transform(ECEF ecef)
        {
            return new ECEF(Rotation * ecef.ToColumn() + Offset);
        }

        public Matrix<double> ToMatrix()
        {
            var m = Matrix<double>.Build.Dense(4, 3);
            m[0, 0] = Rotation[0, 0];
            m[0, 1] = Rotation[0, 1];
            m[0, 2] = Rotation[0, 2];
            m[1, 0] = Rotation[1, 0];
            m[1, 1] = Rotation[1, 1];
            m[1, 2] = Rotation[1, 2];
            m[2, 0] = Rotation[2, 0];
            m[2, 1] = Rotation[2, 1];
            m[2, 2] = Rotation[2, 2];

            m[3, 0] = Offset[0, 0];
            m[3, 1] = Offset[1, 0];
            m[3, 2] = Offset[2, 0];

            return m;
        }

        public Transformation Clone()
        {
            Transformation t = new Transformation();
            t.Rotation = this.Rotation.Clone();
            t.Offset = this.Offset.Clone();
            return t;
        }
    }
}
