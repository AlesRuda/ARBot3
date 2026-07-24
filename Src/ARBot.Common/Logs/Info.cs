using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Logs
{
    public class Info:Message
    {
        string msg;
        public Info():base("Info", 1)
        {
        }
        public Info(string msg):this()
        {
            this.msg=msg;
        }

        public string Message
        {
            get
            {
                return msg;
            }
            set
            {
                msg = value;
            }
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(msg);
        }

        public override void FromData(BinaryReader br)
        {
            msg = br.ReadString();
        }

        public override void FromData(Encoding encoding, byte[] data)
        {
    /*        if (data.Length > 3 && !(data[0] >= 128 || data[1] >= 128 || data[2] >= 128))
            {
                msg = Encoding.ASCII.GetString(data);
                return;
            }*/
            base.FromData(encoding, data);
        }
        public override Message Build()
        {
            return new Info();
        }

        public override string ToString()
        {
            return Message;
        }
    }
}
