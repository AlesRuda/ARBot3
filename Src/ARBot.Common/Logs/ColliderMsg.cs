using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.IO;
using ARBot.Common.Common;

namespace ARBot.Common.Logs
{
    [Serializable()]
    public class ColliderMsg : Message
    {
        public double Length { get; private set; }
        public double R1 { get; private set; }
        public double R2 { get; private set; }
        public double Angle { get; private set; }
        public double? X { get; set; }
        public double? Y { get; set; }

        public ColliderMsg() : base("Collider", 1)
        {
        }

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="name"></param>
        public ColliderMsg(double length, double r1, double r2, double angle) :this()
        {
            Length = length;
            R1 = r1;
            R2 = r2;
            Angle = angle;
        }


        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Length);
            bw.Write(R1);
            bw.Write(R2);
            bw.Write(Angle);
            Write(bw, X);
            Write(bw, Y);
        }

        public override void FromData(BinaryReader br)
        {
            Length = br.ReadDouble();
            R1 = br.ReadDouble();
            R2 = br.ReadDouble();
            Angle = br.ReadDouble();
            X = ReadDouble(br);
            Y = ReadDouble(br);
        }

        public override Message Build()
        {
            return new ColliderMsg();
        }

        public override string ToString()
        {
            return string.Format("Collider");
        }
    }
}
