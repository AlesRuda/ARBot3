using ARBot.Common.Common;
using ARBot.HAL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.Gimbal
{
    /// <summary>
    /// Gimbal s ridici jednotkou Storm32
    /// </summary>
    public class Storm32
    {
        public class ReadResult
        {
            public byte Cmd;
            public byte[] Data;

            protected ushort GetInt(int idx)
            {
                if (Data.Length > idx + 1)
                    return (ushort)(Data[idx] + (((int)Data[idx + 1]) << 8));
                return 0;
            }

            protected void SetInt(int idx, ushort v)
            {
                if (Data.Length > idx + 1)
                {
                    Data[idx] = (byte)(v & 0xff);
                    Data[idx + 1]=(byte)(v>> 8);
                }
            }

            protected short GetSInt(int idx)
            {
                if (Data.Length > idx + 1)
                {
                    return (short)((short)Data[idx] + (((short)Data[idx + 1]) << 8));
                }
                return 0;
            }

            protected void SetSInt(int idx, short v)
            {
                if (Data.Length > idx + 1)
                {
                    Data[idx] = (byte)(v & 0xff);
                    Data[idx + 1]=(byte)(v >> 8);
                }
            }
        }
        public class Version : ReadResult
        {
            public string Value
            {
                get
                {
                    string s = Encoding.ASCII.GetString(Data);
                    if (s.Length >= 1)
                        return s.Substring(0, s.IndexOf("\0"));
                    return null;
                }
            }
            public string Name
            {
                get
                {
                    string s = Encoding.ASCII.GetString(Data);
                    if (s.Length >= 17)
                        return s.Substring(16, s.IndexOf("\0", 16)-16);
                    return null;
                }
            }
            public string Board
            {
                get
                {
                    string s = Encoding.ASCII.GetString(Data);
                    if (s.Length >= 33)
                        return s.Substring(32, s.IndexOf("\0", 32)-32);
                    return null;
                }
            }
        }
        public class DataFields : ReadResult, IHistoryItem<DataFields>
        {
            //please use LIVEDATA_STATUS_V2, may deprecate in future
            //delka 10 bajtu, posledni dva bajty jsou napeti v mV
            public const ushort LIVEDATA_STATUS_V1 = 0x0001;
            // delka 4 bajtu, 0 a 1 bajt timestamp
            public const ushort LIVEDATA_TIMES = 0x0002;
            // delka 6 bajtu
            public const ushort LIVEDATA_IMU1GYRO = 0x0004;
            // delka 6 bajtu
            public const ushort LIVEDATA_IMU1ACC = 0x0008;
            // delka 6 bajtu
            public const ushort LIVEDATA_IMU1R = 0x0010;
            // delka 6 bajtu
            public const ushort LIVEDATA_IMU1ANGLES = 0x0020;
            // delka 6 bajtu
            public const ushort LIVEDATA_PIDCNTRL = 0x0040;
            // delka 8 bajtu
            public const ushort LIVEDATA_INPUTS = 0x0080;
            // delka 6 bajtu
            public const ushort LIVEDATA_IMU2ANGLES = 0x0100;
            // delka 2 bajtu
            public const ushort LIVEDATA_MAGANGLES = 0x0200;  //deprectaed       
            // delka 2 bajtu
            public const ushort LIVEDATA_STORM32LINK = 0x0400;
            // delka 2 bajtu
            public const ushort LIVEDATA_IMUACCCONFIDENCE = 0x0800;
            // delka 0 bajtu
            public const ushort LIVEDATA_ATTITUDE_RELATIVE = 0x1000;
            // delka 0 bajtu
            public const ushort LIVEDATA_STATUS_V2 = 0x2000;
            // delka 0 bajtu
            public const ushort LIVEDATA_ENCODERANGLES = 0x4000;
            // delka 0 bajtu
            public const ushort LIVEDATA_IMUACCABS = 0x8000;

            private static Dictionary<ushort, int> lengths = new Dictionary<ushort, int>();
            static DataFields()
            {
                lengths.Add(LIVEDATA_STATUS_V1, 10);
                lengths.Add(LIVEDATA_TIMES, 4);
                lengths.Add(LIVEDATA_IMU1GYRO, 6);
                lengths.Add(LIVEDATA_IMU1ACC, 6);
                lengths.Add(LIVEDATA_IMU1R, 6);
                lengths.Add(LIVEDATA_IMU1ANGLES, 6);
                lengths.Add(LIVEDATA_PIDCNTRL, 6);
                lengths.Add(LIVEDATA_INPUTS, 8);
                lengths.Add(LIVEDATA_IMU2ANGLES, 6);
                lengths.Add(LIVEDATA_MAGANGLES,2);
                lengths.Add(LIVEDATA_STORM32LINK, 2);
                lengths.Add(LIVEDATA_IMUACCCONFIDENCE, 2);
                lengths.Add(LIVEDATA_ATTITUDE_RELATIVE, 0);
                lengths.Add(LIVEDATA_STATUS_V2, 0);
                lengths.Add(LIVEDATA_ENCODERANGLES, 0);
                lengths.Add(LIVEDATA_IMUACCABS, 0);
            }

            private int LIVEDATA_STATUS_V1_IDX=-1;
            private int LIVEDATA_TIMES_IDX = -1;
            private int LIVEDATA_IMU1ANGLES_IDX = -1;
            private int LIVEDATA_IMU2ANGLES_IDX = -1;

            private int GetIndex(ushort liveData)
            {
                int idx = 2;

                ushort tag=GetInt(0);
                ushort flag = 1;
                while (flag != liveData)
                {
                    if ((tag & flag) != 0)
                    {
                        idx += lengths[flag];
                    }
                    flag <<= 1;
                }
                return idx;
            }

            public DateTime TimeStamp
            {
                get;set;
            }

            public int Time
            {
                get
                {
                    if (LIVEDATA_TIMES_IDX == -1)
                        LIVEDATA_TIMES_IDX = GetIndex(LIVEDATA_TIMES);
                    return GetInt(LIVEDATA_TIMES_IDX);
                }
                set
                {
                    if (LIVEDATA_TIMES_IDX == -1)
                        LIVEDATA_TIMES_IDX = GetIndex(LIVEDATA_TIMES);
                    SetInt(LIVEDATA_TIMES_IDX, (ushort)value);
                }
            }

            public double Voltage
            {
                get
                {
                    if (LIVEDATA_STATUS_V1_IDX == -1)
                        LIVEDATA_STATUS_V1_IDX = GetIndex(LIVEDATA_STATUS_V1);
                    return GetInt(LIVEDATA_STATUS_V1_IDX+8) *0.001;
                }
                set
                {
                    if (LIVEDATA_STATUS_V1_IDX == -1)
                        LIVEDATA_STATUS_V1_IDX = GetIndex(LIVEDATA_STATUS_V1);
                    SetInt(LIVEDATA_STATUS_V1_IDX + 8, (ushort)(value/0.001));
                }
            }
            public double IMU1Pitch
            {
                get
                {
                    if (LIVEDATA_IMU1ANGLES_IDX == -1)
                        LIVEDATA_IMU1ANGLES_IDX = GetIndex(LIVEDATA_IMU1ANGLES);
                    return GetSInt(LIVEDATA_IMU1ANGLES_IDX) *0.01;
                }
                set
                {
                    if (LIVEDATA_IMU1ANGLES_IDX == -1)
                        LIVEDATA_IMU1ANGLES_IDX = GetIndex(LIVEDATA_IMU1ANGLES);
                    SetSInt(LIVEDATA_IMU1ANGLES_IDX, (short)(value / 0.01));
                }
            }
            public double IMU1Roll
            {
                get
                {
                    if (LIVEDATA_IMU1ANGLES_IDX == -1)
                        LIVEDATA_IMU1ANGLES_IDX = GetIndex(LIVEDATA_IMU1ANGLES);
                    return GetSInt(LIVEDATA_IMU1ANGLES_IDX + 2) * 0.01;
                }
                set
                {
                    if (LIVEDATA_IMU1ANGLES_IDX == -1)
                        LIVEDATA_IMU1ANGLES_IDX = GetIndex(LIVEDATA_IMU1ANGLES);
                    SetSInt(LIVEDATA_IMU1ANGLES_IDX + 2, (short)(value/0.01));
                }
            }
            /// <summary>
            /// IMU1 - Otaceni podle svisle osy ve stupnich v metematickem smyslu. 0 dopredu.
            /// </summary>
            public double IMU1Yaw
            {
                get
                {
                    if (LIVEDATA_IMU1ANGLES_IDX == -1)
                        LIVEDATA_IMU1ANGLES_IDX = GetIndex(LIVEDATA_IMU1ANGLES);
                    return GetSInt(LIVEDATA_IMU1ANGLES_IDX + 4) * 0.01;
                }
                set
                {
                    if (LIVEDATA_IMU1ANGLES_IDX == -1)
                        LIVEDATA_IMU1ANGLES_IDX = GetIndex(LIVEDATA_IMU1ANGLES);
                    SetSInt(LIVEDATA_IMU1ANGLES_IDX + 4, (short)(value / 0.01));
                }
            }

            public double IMU2Pitch
            {
                get
                {
                    if (LIVEDATA_IMU2ANGLES_IDX == -1)
                        LIVEDATA_IMU2ANGLES_IDX = GetIndex(LIVEDATA_IMU2ANGLES);
                    return GetSInt(LIVEDATA_IMU2ANGLES_IDX ) * 0.01;
                }
                set
                {
                    if (LIVEDATA_IMU2ANGLES_IDX == -1)
                        LIVEDATA_IMU2ANGLES_IDX = GetIndex(LIVEDATA_IMU2ANGLES);
                    SetSInt(LIVEDATA_IMU2ANGLES_IDX, (short)(value / 0.01));
                }
            }
            public double IMU2Roll
            {
                get
                {
                    if (LIVEDATA_IMU2ANGLES_IDX == -1)
                        LIVEDATA_IMU2ANGLES_IDX = GetIndex(LIVEDATA_IMU2ANGLES);
                    return GetSInt(LIVEDATA_IMU2ANGLES_IDX + 2) * 0.01;
                }
                set
                {
                    if (LIVEDATA_IMU2ANGLES_IDX == -1)
                        LIVEDATA_IMU2ANGLES_IDX = GetIndex(LIVEDATA_IMU2ANGLES);
                    SetSInt(LIVEDATA_IMU2ANGLES_IDX + 2, (short)(value / 0.01));
                }
            }
            /// <summary>
            /// IMU2 - Otaceni podle svisle osy ve stupnich v metematickem smyslu. 0 dopredu.
            /// </summary>
            public double IMU2Yaw
            {
                get
                {
                    if (LIVEDATA_IMU2ANGLES_IDX == -1)
                        LIVEDATA_IMU2ANGLES_IDX = GetIndex(LIVEDATA_IMU2ANGLES);
                    return GetSInt(LIVEDATA_IMU2ANGLES_IDX + 4) * 0.01;
                }
                set
                {
                    if (LIVEDATA_IMU2ANGLES_IDX == -1)
                        LIVEDATA_IMU2ANGLES_IDX = GetIndex(LIVEDATA_IMU2ANGLES);
                    SetSInt(LIVEDATA_IMU2ANGLES_IDX + 4, (short)(value / 0.01));
                }
            }
            /// <summary>
            /// Predozadni naklon v stupnich. 0 vodorovne a roste smerem dolu.
            /// </summary>
            public double Pitch
            {
                get
                {
                    return IMU1Pitch - IMU2Pitch;
                }
            }
            public double Roll
            {
                get
                {
                    return IMU1Roll - IMU2Roll;
                }
            }
            /// <summary>
            /// Otaceni podle svisle osy ve stupnich v metematickem smyslu. 0 dopredu.
            /// </summary>
            public double Yaw
            {
                get
                {
                    return IMU1Yaw - IMU2Yaw;
                }
            }

            public override string ToString()
            {
                return string.Format(@"Voltage={0},
Time={1},
Pitch={2},
Roll={3},
Yaw={4}", Voltage, Time, Pitch, Roll, Yaw);
            }

            public DataFields Interpolate(DataFields prev, DataFields next, float d)
            {
                DataFields df = new DataFields();
                df.Cmd = prev.Cmd;
                df.Data = new byte[prev.Data.Length];

                df.Time = prev.Time + (int)(d * (next.Time - prev.Time));
                df.Voltage = prev.Voltage + d * (next.Voltage - prev.Voltage);
                df.IMU1Pitch = prev.IMU1Pitch + d * (next.IMU1Pitch - prev.IMU1Pitch);
                df.IMU1Roll = prev.IMU1Roll + d * (next.IMU1Roll - prev.IMU1Roll);
                df.IMU1Yaw = prev.IMU1Yaw + d * (next.IMU1Yaw - prev.IMU1Yaw);
                df.IMU2Pitch = prev.IMU2Pitch + d * (next.IMU2Pitch - prev.IMU2Pitch);
                df.IMU2Roll = prev.IMU2Roll + d * (next.IMU2Roll - prev.IMU2Roll);
                df.IMU2Yaw = prev.IMU2Yaw + d * (next.IMU2Yaw - prev.IMU2Yaw);

                return df;
            }
        }

        public class ACK: ReadResult
        {
            public const byte OK = 0;
            public const byte ERR_FAIL = 1;
            public const byte ERR_ACCESS_DENIED = 2;
            public const byte ERR_NOT_SUPPORTED = 3;
            public const byte ERR_TIMEOUT = 150;
            public const byte ERR_CRC = 151;
            public const byte ERR_PAYLOADLEN = 152;
            public const byte ERR_DATALEN = 255;
            public const byte ERR_UNEXPECTED = 254;

            private ReadResult unexpectedResult;


            public ACK(ReadResult unexpectedResult)
            {
                this.unexpectedResult = unexpectedResult;
                Cmd = 150;
                Data = new byte[1];
                Data[0] = ERR_UNEXPECTED;
            }

            public ACK()
            {
            }

            public ReadResult UnexpectedResult
            {
                get
                {
                    return unexpectedResult;
                }
            }

            public bool IsSuccess
            {
                get
                {
                    return Result==OK;
                }
            }
            public Byte Result
            {
                get
                {
                    if (Data.Length == 1)
                        return Data[0];
                    return ERR_DATALEN;
                }
            }
            public string Message
            {
                get
                {
                    switch (Result)
                    {
                        case OK:
                            return "OK";
                        case ERR_FAIL:
                            return "Fail";
                        case ERR_ACCESS_DENIED:
                            return "Access denied";
                        case ERR_NOT_SUPPORTED:
                            return "Nnot supported";
                        case ERR_TIMEOUT:
                            return "Timeout";
                        case ERR_CRC:
                            return "CRC fail";
                        case ERR_PAYLOADLEN:
                            return "Invalid payload length";
                        case ERR_DATALEN:
                            return "Invalid ACK payload length";
                        case ERR_UNEXPECTED:
                            return "Unexpected message";
                        default:
                            return "";
                    }
                }
            }
        }

        IUart uart;
        public Storm32(IUart uart)
        {
            this.uart = uart;
        }

        ushort CalcCRC(byte[] data)
        {
            ushort tmp;
            ushort crcAccum = 0xffff;

            foreach (byte d in data)
            {
                tmp = (byte)(d ^ (crcAccum & 0xff));
                tmp ^= (byte)(tmp << 4);
                crcAccum = (ushort)((crcAccum >> 8) ^ (tmp << 8) ^ (tmp << 3) ^ (tmp >> 4));
            }
            return crcAccum;
        }

        private void Send(byte cmd, byte[] payload)
        {
            byte[] data = new byte[payload.Length + 2];
            byte[] msg = new byte[payload.Length + 5];

            data[0] = (byte)payload.Length;
            data[1] = cmd;
            payload.CopyTo(data, 2);

            var crc = CalcCRC(data);

            msg[0] = 0xfa;
            data.CopyTo(msg, 1);
            msg[msg.Length - 2] = (byte)(crc & 0xff);
            msg[msg.Length - 1] = (byte)(crc >> 8);

            uart.Write(msg);
        }

        private ReadResult Read()
        {
            byte[] r;
            do
            {
                r = uart.Read(1);
            } while (r == null || r.Length != 1 || r[0] != 0xfb);

            r = uart.Read(2);
            int len = r[0];

            ReadResult ret = new ReadResult();
            ret.Cmd = r[1];
            byte[] msg = uart.Read(len + 2);
            ret.Data = new byte[len];
            Array.Copy(msg, ret.Data, len);

            byte[] crcData = new byte[len + 2];
            Array.Copy(r, crcData, r.Length);
            Array.Copy(ret.Data, 0, crcData, r.Length, ret.Data.Length);

            var crc = CalcCRC(crcData);

            if (msg[msg.Length - 2] != (byte)(crc & 0xff) || msg[msg.Length - 1] != (byte)(crc >> 8))
                return null;

            return ret;
        }
        private ReadResult Decode(ReadResult msg, byte? expected)
        {
            if (msg == null)
                return null;

            ReadResult ret = new ReadResult();

            switch(msg.Cmd)
            {
                case 2:
                    ret = new Version();
                    break;
                case 5:
                    ret = new Version();
                    break;
                case 6:
                    ret = new DataFields();
                    break;
                case 150:
                    ret = new ACK();
                    break;
            }
            ret.Cmd = msg.Cmd;
            ret.Data = msg.Data;

            if (expected != null && msg.Cmd != expected)
                return new ACK(ret);

            return ret;
        }
        public Version GetVersion()
        {
            Version ret = null;
            lock (this)
            {
                Send(2, new byte[0]);
                ret = Decode(Read(), 5) as Version;
            }
            return ret;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="pitch">Predozadni naklo. 0 vodorovne. Kladne hodnoty naklon smerem dolu.</param>
        /// <param name="roll">Levopravy naklo. 0 vodorovne. Pri pohledu dopredu je kladny smer ve smeru hodinek.</param>
        /// <param name="yaw">Otaceni podle svisle osy ve stupnich v metematickem smyslu. 0 dopredu.</param>
        /// <returns></returns>
        public ACK SetAngle(float pitch, float roll, float yaw)
        {
            ACK ret = null;
            lock (this)
            {
                MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);
            bw.Write(pitch);
            bw.Write(roll);
            bw.Write(yaw);
            bw.Write((byte)0);
            bw.Write((byte)0);


            Send(17, ms.ToArray());
            ret= Decode(Read(), 150) as ACK;
            }
            return ret;
        }
        public DataFields GetParams()
        {
            DataFields ret=null;
            lock (this)
            {
                MemoryStream ms = new MemoryStream();
                BinaryWriter bw = new BinaryWriter(ms);
                bw.Write((ushort)(DataFields.LIVEDATA_STATUS_V1 + DataFields.LIVEDATA_TIMES + DataFields.LIVEDATA_IMU1ANGLES + DataFields.LIVEDATA_IMU2ANGLES));
                //            bw.Write((ushort)(DataFields.LIVEDATA_STATUS_V1 + DataFields.LIVEDATA_TIMES));

                Send(6, ms.ToArray());
                ret = Decode(Read(), 6) as DataFields;
                if(ret!=null)
                    ret.TimeStamp = TimeBase.Now;
            }
            return ret;
        }
    }
}
