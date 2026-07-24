using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Logs
{
    [Serializable()]
    public class Marker:Message, INamedMessage
    {
        public enum MarkerType
        {
            Cross=0,
            Circle=1,
            Marker=2
        }

        public Marker():base("Marker", 1)
        {
        }
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double MinDistance { get; set; }
        public double MaxDistance { get; set; }
        public double MinAngel { get; set; }
        public MarkerType Type { get; set; }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Name);
            bw.Write(X);
            bw.Write(Y);
            bw.Write(MinDistance);
            bw.Write(MaxDistance);
            bw.Write(MinAngel);
            bw.Write((int)Type);
        }

        public override void FromData(BinaryReader br)
        {
            Name = br.ReadString();
            X = br.ReadDouble();
            Y = br.ReadDouble();
            MinDistance = br.ReadDouble();
            MaxDistance = br.ReadDouble();
            MinAngel = br.ReadDouble();
            Type = (MarkerType)br.ReadInt32();
        }

        public override Message Build()
        {
            return new Marker();
        }

        public LLA LLA(Transformation t)
        {
            //            return new LLA(new ECEF((new ECEF() { X = s.ECEFRef.Radius, Y = 300, Z = 0 }).Mul(new Transform(s.ECEFRef, false))));
            return new LLA(Ellipsoid.Sphere, t.Transform(new ECEF() { X = Ellipsoid.Sphere.SemiMajorAxis, Y = X, Z = Y }));
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}, {2}", Name, X, Y);
        }
    }
}
