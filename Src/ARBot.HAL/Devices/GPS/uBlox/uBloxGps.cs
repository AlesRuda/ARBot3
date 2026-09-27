using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Diagnostics;
using ARBot.Common.Logs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.GPSs.uBlox
{
    public class uBloxGps : UartSensorBase<GPSState>, IGPS
    {
        // Skrceni hlaseni o chybach cteni (viz PoruchaHlasic): pri poskozene lince se sem chodi
        // mnohokrat za sekundu a prvni hlaska je ta nejzajimavejsi.
        private readonly PoruchaHlasic hlasic = new PoruchaHlasic();

        /// <summary>
        /// Constructor for the Device class.
        /// </summary>
        public uBloxGps(IUart uart):base(uart)
        {
            Start();
        }

        /// <summary>
        /// Jmeno sensoru, ktere se zobrazuje v logu a GUI
        /// </summary>
        public override string Name => "uBloxGps";

        protected override GPSState GetMeasurement()
        {
            PVTMessage pos = null;
            // ⚠️ Podminka nese i stopRequired (nalez auditu 15. 9. 2026). Bez toho se smycka tocila
            // DONEKONECNA: Uart.Read(int) si priznak zruseni na zacatku kazdeho volani NULUJE
            // („novy pozadavek na cteni"), takze CancelRead z UartSensorBase.Stop() odblokoval jen
            // jedno cteni a hned dalsi zase blokovalo — Stop() senzoru pak cekal navzdy a s nim
            // i ARBotRuntime.Stop(), ktery bezi pod zamkem.
            //
            // null se vraci schvalne: Process() ho bere jako „zadne merenie" a smycku ukonci podle
            // stopRequired. Zadne merenie se tim nezahodi, protoze zadne neni.
            while (pos == null && !stopRequired)
            {
                var msg = Read();
                if (msg is PVTMessage)
                {
                    pos = msg as PVTMessage;
                }
            }
            if (pos == null)
                return null;      // zastavujeme se
            // Puvodni rozklad ITOW (do 27. 9. 2026). Byl SPATNE - sekundy odecitaly hodiny *60
            // misto *3600, takze vychazelo i „9 dni" (viz GPSState.ItowFromBrokenUblox, ktera ho
            // pro starsi zaznamy invertuje). Ponechano do potvrzeni nove cesty na zarizeni.
            //int d = 0, h = 0, m = 0, s = 0, ms = 0;
            //d = (int)pos.ITOW / (1000 * 60 * 60 * 24);
            //h = (int)pos.ITOW / (1000 * 60 * 60) - d * 24;
            //m = (int)pos.ITOW / (1000 * 60) - (d * 24 + h) * 60;
            //s = (int)pos.ITOW / 1000 - ((d * 24 + h) * 60 + m * 60);
            //ms = (int)pos.ITOW - (((d * 24 + h) * 60 + m * 60) + s) * 1000;
            return new GPSState()
            {
                FixTime = FixTimeFrom(pos.Hour, pos.Min, pos.Sec, pos.NanoSec, pos.Valid),
                // u-blox posila 1e-7 STUPNE; GPSState drzi RADIANY (viz GPSState.Latitude).
                Latitude = Conversions.Deg2Rad(pos.Latitude),
                Longitude = Conversions.Deg2Rad(pos.Longitude),
                Quality = FixQualityFrom(pos.FixType),
                NumberOfSatellites = pos.NumSV,
                // POZOR: je to PDOP (prostorovy), ne HDOP (vodorovny) - NAV-PVT jiny neposila.
                // PDOP >= HDOP vzdy, takze jako miru kvality je to konzervativni odhad; prahy
                // (gpsmaxdop=) tomu musi odpovidat. Pole se jmenuje Hdop kvuli NMEA, kde to HDOP je.
                Hdop = pos.PDOP,
                Altitude = pos.HeightMSL,
                DynamicOrientation = Math.Atan2(pos.VelocityN, pos.VelocityE),
                Orientation= pos.HeadVehValid?Conversions.Azimut2Orientation(Conversions.Deg2Rad(pos.HeadVeh)):(double?)null,
                Speed = pos.GroundSpeed,
                TimeStamp = TimeBase.Now
            };
        }

        /// <summary>Bit <c>validTime</c> v poli <c>valid</c> UBX-NAV-PVT: UTC cas dne je platny.</summary>
        public const byte ValidTime = 0x02;

        /// <summary>
        /// <see cref="GPSState.FixTime"/> z UTC poli UBX-NAV-PVT: <b>UTC cas dne</b>, tataz vec
        /// jako u NMEA (<c>GGA</c>). Bez bitu <see cref="ValidTime"/> vraci <see cref="TimeSpan.Zero"/>
        /// (= neznamy) — prijimac pred prvnim fixem posila v tech polich nesmysl.
        ///
        /// <para><c>nano</c> je <b>znamenkova</b> oprava k <c>h:m:s</c> (−1e9 … +1e9 ns), takze
        /// vysledek muze vyjit pred pulnoci nebo za ni; zabali se do jednoho dne.</para>
        ///
        /// <para>Proc ne ITOW: je to GPS cas v tydnu (o 18 s napred proti UTC a s dnem v tydnu),
        /// zatimco vsichni konzumenti (panel GPS, export GPX, rozbor hodin) chteji cas dne v UTC.
        /// Rozkladat ho navic uz jednou selhalo — viz komentar v <c>GetMeasurement</c>.</para>
        /// </summary>
        public static TimeSpan FixTimeFrom(byte hour, byte min, byte sec, int nano, byte valid)
        {
            if ((valid & ValidTime) == 0) return TimeSpan.Zero;
            var t = new TimeSpan(hour, min, sec) + TimeSpan.FromTicks(nano / 100);
            long day = TimeSpan.TicksPerDay;
            var z = TimeSpan.FromTicks(((t.Ticks % day) + day) % day);
            // Presne pulnoc by se cetla jako „neznamy" (Zero); o tik vedle je to jedno.
            return z == TimeSpan.Zero ? TimeSpan.FromTicks(1) : z;
        }

        /// <summary>
        /// Prevod u-bloxiho <c>fixType</c> (UBX-NAV-PVT) na <see cref="GPSState.FixQuality"/>.
        ///
        /// <para>⚠️ <b>Do 6. 9. 2026 se tu jen PRETYPOVAVALO</b> (<c>(FixQuality)pos.FixType</c>)
        /// a ty dva vycty spolu nesouvisi — <c>fixType</c> rika ZPUSOB reseni, <c>FixQuality</c>
        /// pochazi z NMEA GGA a rika DRUH korekce. Dopadalo to takhle:</para>
        /// <list type="bullet">
        /// <item><c>1</c> = <b>jen mrtvy odhad</b> (dead reckoning, bez druzic) se tvarilo jako
        /// <c>GpsFix</c>, tedy platny fix — a <see cref="GPSState.IsFixed"/> ho pustilo do fuze.
        /// Prave takove reseni <b>ujizdi jednim smerem</b>, i kdyz robot stoji.</item>
        /// <item><c>4</c> = <b>GNSS + mrtvy odhad</b> (dobre reseni) se tvarilo jako <c>Rtk</c>,
        /// ktere <c>IsFixed</c> naopak NEPOUSTI — tedy přesně naopak, nez by melo byt.</item>
        /// <item><c>2</c> = 2D fix (bez vysky, horsi) se hlasilo jako <c>DgpsFix</c>, co zni lip
        /// nez <c>GpsFix</c>.</item>
        /// </list>
        /// <para>Prevod je proto vyslovny. 2D fix zustava <c>GpsFix</c> (platny, ale nic navic),
        /// 3D i kombinovany s DR jsou <c>DgpsFix</c> — v obou pripadech je to poloha z druzic.
        /// Kvalitu dal rozlisuje pocet druzic a DOP, ne tenhle vycet.</para>
        /// </summary>
        public static GPSState.FixQuality FixQualityFrom(byte fixType)
        {
            switch (fixType)
            {
                case 2:  return GPSState.FixQuality.GpsFix;    // 2D
                case 3:  return GPSState.FixQuality.DgpsFix;   // 3D
                case 4:  return GPSState.FixQuality.DgpsFix;   // 3D + mrtvy odhad
                case 1:  return GPSState.FixQuality.Estimated; // JEN mrtvy odhad - poloha z druzic to neni
                default: return GPSState.FixQuality.Invalid;   // 0 = bez fixu, 5 = jen cas
            }
        }

        private UBXMessage Read()
        {
            UBXMessage m = null;

            try
            {
                m = UBXMessage.Parse(uart);
            }
            catch (Exception ex)
            {
                // Trace, ne Debug: tohle je jedine misto, kde se hlasi, ze z GPS nejde nic precist.
                // V Release (a ten bezi na zarizeni) by po nem nezustala zadna stopa - GPS by jen
                // mlcelo. Skrceno, protoze pri poskozene lince se sem chodi mnohokrat za sekundu.
                hlasic.Hlas($"{Name}: cteni UBX selhalo", ex);
            }
            return m;
        }
    }
}

