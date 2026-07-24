using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.IO;
using ARBot.Common.Common;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Logs
{
    [Serializable()]
    public class ICPMsg : Message
    {
        public class ICPPoint
        {
            public double X, Y;
            public int Generace;
            public int Iterace;
            public int LastMatch;
            public bool IsMain;
            public int Type;
            public int SubType;
            public Matrix<double> P;
            public double? Orientation;
        }

        public ICPMsg() : base("ICP", 2)
        {
        }

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="name"></param>
        public ICPMsg(string name, List<ICPPoint> points, double offX, double offY, double angle):this()
        {
            Name = name;
            Points = points;
            OffX = offX;
            OffY = offY;
            Angle = angle;
        }

        /// <summary>
        /// Nazev zaznamu
        /// </summary>
        public string Name { get; private set; }


        public List<ICPPoint> Points { get; private set; }
        public double OffX { get; private set; }
        public double OffY { get; private set; }
        public double Angle { get; private set; }


        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Name ?? "ICP");

            bw.Write(OffX);
            bw.Write(OffY);
            bw.Write(Angle);

            bw.Write(Points.Count);
            for (int i = 0; i < Points.Count; i++)
            {
                bw.Write(Points[i].X);
                bw.Write(Points[i].Y);
                bw.Write(Points[i].Generace);
                bw.Write(Points[i].Iterace);
                bw.Write(Points[i].LastMatch);
                bw.Write(Points[i].IsMain);
                bw.Write(Points[i].Type);
                bw.Write(Points[i].SubType);
                Write(bw, Points[i].P);
                Write(bw, Points[i].Orientation);
            }
        }

        public override void FromData(BinaryReader br)
        {
            Name = br.ReadString();
            OffX = br.ReadDouble();
            OffY = br.ReadDouble();
            Angle = br.ReadDouble();

            int cnt = br.ReadInt32();
            Points = new List<ICPPoint>();
            for (int i = 0; i < cnt; i++)
            {
                double x = br.ReadDouble();
                double y = br.ReadDouble();
                int generace = br.ReadInt32();
                int iterace = br.ReadInt32();
                int lastMatch = br.ReadInt32();
                bool isMain = br.ReadBoolean();
                int type = br.ReadInt32();
                int subType = br.ReadInt32();
                var p = ReadMatrixDouble(br);
                double? orientation = null;
                if(Verze>=2)
                    orientation=ReadDouble(br);

                Points.Add(new ICPPoint() { X = x, Y = y, Generace = generace, Iterace = iterace, LastMatch = lastMatch, IsMain = isMain, Type = type, SubType = subType, P = p, Orientation=orientation });
            }
        }

        public override Message Build()
        {
            return new ICPMsg();
        }

        public override string ToString()
        {
            return string.Format("ICP");
        }
    }
}
