using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Logs
{
    public class Module:Message, INamedMessage
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public double CPU { get; set; }

        public Module()
            : base("Module", 1)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(ID);
            bw.Write(Name);
            bw.Write(Enabled);
            bw.Write(CPU);
        }

        public override void FromData(BinaryReader br)
        {
            ID = br.ReadInt32();
            Name = br.ReadString();
            Enabled = br.ReadBoolean();
            CPU = br.ReadDouble();
        }

        public override Message Build()
        {
            return new Module();
        }
    }
}
