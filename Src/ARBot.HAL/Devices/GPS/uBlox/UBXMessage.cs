using ARBot.HAL.Devices.GPSs.uBlox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.GPSs.uBlox
{
    public class UBXMessage
    {
        public UBXMessage(byte cls, byte msgID, byte[] payload)
        {
            Class = cls;
            MessageID = msgID;
            Payload = payload;
        }
        public byte Class { get; set; }
        public byte MessageID { get; set; }
        public byte[] Payload { get; set; }

        public static Tuple<byte, byte> CheckSum(byte cls, byte msgID, byte[] payload)
        {
            byte a = 0, b = 0;
            a += cls;
            b += a;
            a += msgID;
            b += a;
            a += (byte)(payload.Length & 0xff);
            b += a;
            a += (byte)(payload.Length >> 8);
            b += a;

            for (int i = 0; i < payload.Length; i++)
            {
                a += payload[i];
                b += a;
            }
            return new Tuple<byte, byte>(a, b);
        }

        protected Int32 GetInt32(int pos)
        {
            int i = Payload[pos];
            i += Payload[pos + 1] << 8;
            i += Payload[pos + 2] << 16;
            i += Payload[pos + 3] << 24;
            return i;
        }
        protected UInt32 GetUInt32(int pos)
        {
            UInt32 i = Payload[pos];
            i += (UInt32)Payload[pos + 1] << 8;
            i += (UInt32)Payload[pos + 2] << 16;
            i += (UInt32)Payload[pos + 3] << 24;
            return i;
        }

        protected UInt16 GetUInt16(int pos)
        {
            UInt16 i = Payload[pos];
            i += (UInt16)(Payload[pos + 1] << 8);
            return i;
        }

        private static byte Read(IUart u)
        {
            return u.Read(1)[0];
        }

        public void Send(IUart u)
        {
            byte[] buf = new byte[6];
            buf[0] = 0xb5;
            buf[1] = 0x62;
            buf[2] = Class;
            buf[3] = MessageID;
            buf[4] = (byte)(Payload.Length&0xff);
            buf[5] = (byte)(Payload.Length >>8);

            var c = CheckSum(Class, MessageID, Payload);
            u.Write(buf);
            u.Write(Payload);
            buf = new byte[2];
            buf[0] = c.Item1;
            buf[1] = c.Item2;
            u.Write(buf);
        }

        public static UBXMessage Parse(IUart u)
        {
            UBXMessage m = null;
            while (Read(u) != 0xb5) ;
            if (Read(u) == 0x62)
            {
                byte cls = Read(u);
                byte msgID = Read(u);
                ushort len = Read(u);
                len+= (ushort)(Read(u)*256);
                // POZOR: u.Read(buf, 0, len) je JEDNORAZOVE cteni SerialPortu a vraci jen
                // aktualne dostupne bajty (casto < len pri fragmentaci na vysokych baudech).
                // Zbytek payloadu se pak cetl jako checksum + zacatek dalsi zpravy -> checksum
                // nesedi -> zprava se zahodi -> desync -> mereni chodi neravnomerne ("v blocich").
                // u.Read(len) blokujicne docte PRESNE len bajtu (jako Read(1) u ostatnich poli).
                byte[] buf = u.Read(len);
                //byte[] buf = new byte[len];
                //u.Read(buf, 0, len);
                byte ca = Read(u);
                byte cb = Read(u);

                var c = CheckSum(cls, msgID, buf);
                if (c.Item1 == ca && c.Item2 == cb)
                {
                    if(cls==1 && msgID==2)
                        m = new POSLLHMessage(buf);
                    else if (cls == 1 && msgID == 7)
                        m = new PVTMessage(buf);
                    else
                        m = new UBXMessage(cls, msgID, buf);
                }
            }
            return m;
        }
    }
}
