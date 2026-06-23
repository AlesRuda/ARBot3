using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ARBot.Common.Coordinates
{
    public class MoveScale2DTransformation
    {
        /// <summary>
        /// Posunuti
        /// </summary>
        public Vector3 Offset { get; private set; }

        /// <summary>
        /// Zvetseni
        /// </summary>
        public float Scale
        {
            get;
            set;
        }

        public MoveScale2DTransformation()
        {
        }

        public void Move(float x, float y)
        {
            Offset=new Vector3(Offset.X+x, Offset.Y+y, 0);
        }

        public ECEF Transform(ECEF ecef)
        {
            return new ECEF() { X = ecef.X, Y = ecef.Y * Scale + Offset.X, Z = ecef.Z * Scale + Offset.Y };
        }
        public Vector3 Transform(Vector3 v)
        {
            return new Vector3(v.X * Scale + Offset.X, v.Y * Scale + Offset.Y, 0 );
        }

    }
}
