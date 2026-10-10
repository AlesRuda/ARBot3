using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.GPSs.uBlox
{
    public class PVTMessage : UBXMessage
    {
        public PVTMessage(byte[] payload):base(1, 2, payload)
        {
        }

        public UInt32 ITOW
        {
            get
            {
                return GetUInt32(0);
            }
        }

        // Offset 4 (ICD UBX-NAV-PVT); do 27. 9. 2026 tu bylo 2, tedy horni pulka ITOW.
        public UInt16 Year
        {
            get
            {
                return GetUInt16(4);
            }
        }

        public byte Month
        {
            get
            {
                return Payload[6];
            }
        }

        public byte Dey
        {
            get
            {
                return Payload[7];
            }
        }

        public byte Hour
        {
            get
            {
                return Payload[8];
            }
        }

        public byte Min
        {
            get
            {
                return Payload[9];
            }
        }

        public byte Sec
        {
            get
            {
                return Payload[10];
            }
        }

        public byte Valid
        {
            get
            {
                return Payload[11];
            }
        }

        public UInt32 TimeAccuracy
        {
            get
            {
                return GetUInt32(12);
            }
        }

        public Int32 NanoSec
        {
            get
            {
                return GetInt32(16);
            }
        }

        public byte FixType
        {
            get
            {
                return Payload[20];
            }
        }

        /// <summary>
        /// Fix status flags
        /// </summary>
        public byte Flags
        {
            get
            {
                return Payload[21];
            }
        }

        /// <summary>
        /// Plati <see cref="HeadVeh"/>? Bit <c>headVehValid</c> v <c>flags</c> a zaroven zprava dost
        /// dlouha, aby pole <c>headVeh</c> (offset 84) vubec nesla - starsi protokol posilal NAV-PVT
        /// bez nej (84 B), kdezto dnesni ma 92 B.
        /// </summary>
        public bool HeadVehValid => Payload.Length >= HeadVehOffset + 4 && (Flags & 32) != 0;

        /// <summary>Offset pole <c>headVeh</c> v UBX-NAV-PVT (ICD: za <c>pDOP</c> 76, <c>flags3</c> 78
        /// a rezervou 79-83).</summary>
        public const int HeadVehOffset = 84;

        public byte Flags2
        {
            get
            {
                return Payload[22];
            }
        }

        public byte NumSV
        {
            get
            {
                return Payload[23];
            }
        }

        public double Longitude
        {
            get
            {
                return GetInt32(24) * 1e-7;
            }
        }
        public double Latitude
        {
            get
            {
                return GetInt32(28) * 1e-7;
            }
        }
        /// <summary>
        /// Vyska nad elipsoidem v m
        /// </summary>
        public double Height
        {
            get
            {
                return GetInt32(32) * 1e-3;
            }
        }
        /// <summary>
        /// Vyska na hladinou more v m
        /// </summary>
        public double HeightMSL
        {
            get
            {
                return GetInt32(36)*1e-3;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        public double HorizontalAccuracy
        {
            get
            {
                return GetUInt32(40) * 1e-3;
            }
        }
        public double VerticalAccuracy
        {
            get
            {
                return GetUInt32(44) * 1e-3;
            }
        }
        /// <summary>
        /// North velocity in m/s
        /// </summary>
        public double VelocityN
        {
            get
            {
                return GetInt32(48) * 1e-3;
            }
        }
        /// <summary>
        /// East velocity in m/s
        /// </summary>
        public double VelocityE
        {
            get
            {
                return GetInt32(52) * 1e-3;
            }
        }
        /// <summary>
        /// Down velocity in m/s
        /// </summary>
        public double VelocityD
        {
            get
            {
                return GetInt32(56) * 1e-3;
            }
        }
        /// <summary>
        /// Ground speed in m/s
        /// </summary>
        public double GroundSpeed
        {
            get
            {
                return GetInt32(60) * 1e-3;
            }
        }
        /// <summary>
        /// Heading od moution in deg
        /// </summary>
        public double HeadMot
        {
            get
            {
                return GetInt32(64) * 1e-5;
            }
        }
        /// <summary>
        /// Speed accuracy in m/s
        /// </summary>
        public double SpeedAcc
        {
            get
            {
                return GetUInt32(68) * 1e-3;
            }
        }
        /// <summary>
        /// Speed accuracy in m/s
        /// </summary>
        public double HeadAcc
        {
            get
            {
                return GetUInt32(72) * 1e-5;
            }
        }
        /// <summary>
        /// Position DOP
        /// </summary>
        public double PDOP
        {
            get
            {
                return GetUInt16(76) * 1e-2;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        public UInt16 Flags3
        {
            get
            {
                return GetUInt16(78);
            }
        }
        /// <summary>
        /// Kurz VOZIDLA ve stupnich (azimut), plati jen s <see cref="HeadVehValid"/>; u kratke zpravy NaN.
        ///
        /// <para>Do 10. 10. 2026 se cetl z offsetu 64, tedy vracel kurz POHYBU (<see cref="HeadMot"/>).
        /// <c>uBloxGps</c> z nej plni <c>GPSState.Orientation</c>, ktere ma ve fuzi prednost s pevnou
        /// sigmou a obchazi prah rychlosti i vylouceni jizdy vzad - pri couvani by sel do fuze kurz
        /// otoceny o 180°. Spici: v zadnem ze 46 zaznamu 2. 9. - 1. 10. prijimac <c>headVehValid</c>
        /// nenastavil (<c>ARBot.Analyze gpskurz</c>), takze se to nikdy neprojevilo.</para>
        /// </summary>
        public double HeadVeh
        {
            get
            {
                return Payload.Length >= HeadVehOffset + 4 ? GetInt32(HeadVehOffset) * 1e-5 : double.NaN;
            }
        }
    }
}
