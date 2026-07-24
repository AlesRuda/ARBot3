using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.IO;

namespace ARBot.Common.Logs
{
    [Serializable()]
    public class Lidar : Message, INamedMessage
    {
        public Lidar() : base("Lidar", 1)
        {
        }

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="name"></param>
        public Lidar(string name):this()
        {
            Name = name;
        }

        /// <summary>
        /// Nazev zaznamu
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Pocet mereni
        /// </summary>
        public int Count;

        /// <summary>
        /// Uhel vzorku v radianech v matematickem smeru, 0 pred Lidarem/na sever
        /// </summary>
        public double[] Angle;

        /// <summary>
        /// Vzdalenost
        /// </summary>
        public double?[] Distance;

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Name ?? "Lidar");
            bw.Write(Count);

            for (int i = 0; i < Count; i++)
            {
                bw.Write(Angle[i]);
                if (Distance[i].HasValue)
                {
                    bw.Write(true);
                    bw.Write(Distance[i].Value);
                }
                else
                {
                    bw.Write(false);
                    bw.Write((double)0);
                }
            }
        }

        public override void FromData(BinaryReader br)
        {
            Name = br.ReadString();
            Count = br.ReadInt32();

            Angle = new double[Count];
            Distance = new double?[Count];
            for (int i = 0; i < Count; i++)
            {
                Angle[i] = br.ReadDouble();
                bool b = br.ReadBoolean();
                double? d = br.ReadDouble();
                if (!b)
                    d = null;
                Distance[i] = d;
            }
        }

        public override Message Build()
        {
            return new Lidar();
        }

        public override string ToString()
        {
            return string.Format("Lidar");
        }
    }
}
