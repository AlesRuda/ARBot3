using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Textova zprava v proudu (log). Nese <c>Trace</c>/<c>Debug</c> vystup aplikace, takze se
    /// zaznamena spolu s ostatnimi zpravami a pri cteni zaznamu jde precist, co program v tu chvili
    /// hlasil - i z behu na zarizeni, kde k oknu Debug output nikdo nesedi.
    ///
    /// <para><b>Verze 2</b> (2026-08-14) pridala <see cref="TimeStamp"/>, <see cref="Area"/>
    /// a <see cref="Level"/>. Cas je nutny proto, ze obalka zaznamu ho nenese (kazda zprava si ho
    /// uklada sama) - bez nej by slo jen poradi a log by nesel sparovat s konkretnim snimkem nebo
    /// planem. Oblast a uroven slouzi k filtrovani AZ PRI CTENI: do proudu jde vse (i hlasky
    /// Avalonie/Mapsui), at se nic neztrati, a co je sum se rozhodne az nad zaznamem.</para>
    ///
    /// <para><b>Verze 1</b> nesla jen text. <c>MessageReader</c> nastavuje <see cref="Message.Verze"/>
    /// jeste pred <see cref="FromData(BinaryReader)"/>, takze se stare zaznamy ctou dal.</para>
    /// </summary>
    public class Info:Message
    {
        string msg;
        public Info():base("Info", 2)
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

        /// <summary>Cas vzniku zpravy. Ve verzi 1 se neukladal - tam zustane <c>default</c>.</summary>
        public DateTime TimeStamp { get; set; }

        /// <summary>Zdroj hlasky (napr. "App" pro vlastni Debug.WriteLine, "Avalonia:Layout").
        /// Prazdne = neznamo.</summary>
        public string Area { get; set; } = string.Empty;

        /// <summary>Uroven hlasky (napr. "Debug", "Warning"). Prazdne = neznamo.</summary>
        public string Level { get; set; } = string.Empty;

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(msg ?? string.Empty);
            Write(bw, TimeStamp);
            bw.Write(Area ?? string.Empty);
            bw.Write(Level ?? string.Empty);
        }

        public override void FromData(BinaryReader br)
        {
            msg = br.ReadString();
            if (Verze < 2)
            {
                // Stary zaznam: dal uz nic neni.
                TimeStamp = default;
                Area = string.Empty;
                Level = string.Empty;
                return;
            }
            TimeStamp = ReadDateTime(br);
            Area = br.ReadString();
            Level = br.ReadString();
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
