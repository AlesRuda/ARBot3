using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.IO;

namespace ARBot.Common.Logs
{
    [Serializable()]
    public class VFH:Message
    {
        public VFH():base("VFH", 1)
        {
        }

        public int SelSegment, Segments;
        public double SegmentSize, TauLo, TauHi;
        public double Direction;
        public double? Distance;
        public bool[] HB;
        public double[] HP;

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Segments);
            bw.Write(SelSegment);
            bw.Write(SegmentSize);
            bw.Write(TauLo);
            bw.Write(TauHi);
            bw.Write(Direction);
            bw.Write(Distance.HasValue);
            bw.Write(Distance.GetValueOrDefault(0));

            for (int i = 0; i < Segments; i++)
            {
                bw.Write(HB[i]);
                bw.Write(HP[i]);
            }
        }

        public override void FromData(BinaryReader br)
        {
            Segments = br.ReadInt32();
            SelSegment = br.ReadInt32();
            SegmentSize = br.ReadDouble();
            TauLo = br.ReadDouble();
            TauHi = br.ReadDouble();
            Direction = br.ReadDouble();
            if (br.ReadBoolean())
                Distance = br.ReadDouble();
            else
            {
                Distance = null;
                br.ReadDouble();
            }

            HB = new bool[Segments];
            HP = new double[Segments];
            for (int i = 0; i < Segments; i++)
            {
                HB[i] = br.ReadBoolean();
                HP[i] = br.ReadDouble();
            }
        }

        public override Message Build()
        {
            return new VFH();
        }

        public override string ToString()
        {
            return string.Format("VFH SelSegment={0}", SelSegment);
        }
    }
}
