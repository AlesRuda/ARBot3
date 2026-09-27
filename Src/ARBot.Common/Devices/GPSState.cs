using ARBot.Common.Logs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Devices
{
    public class GPSState: SensorStateBase
    {
        /// <summary>
        /// Verze formatu serializace (viz doc/record-replay.md → Verzovani zprav).
        ///
        /// <para><b>Verze 2</b> (2026-08-26): <see cref="Latitude"/> / <see cref="Longitude"/> jsou
        /// v <b>RADIANECH</b>, drive ve stupnich. Bajty jsou na temze miste, takze se stary zaznam
        /// pozna <b>jen podle verze</b> — a bez prevodu by se z nej stala tichá nesmyslna data
        /// (50 „radianu" je platne cislo, takze by se to projevilo az chovanim fuze o desitky tisic
        /// kilometru dal).</para>
        ///
        /// <para><b>Verze 3</b> (2026-09-27): <see cref="FixTime"/> je u u-bloxu <b>UTC cas dne</b>
        /// (z UTC poli UBX-NAV-PVT), tedy tataz vec jako u NMEA. Do verze 2 do nej ovladac u-bloxu
        /// skladal ITOW (cas v GPS tydnu) a skladal ho <b>spatne</b> — vychazelo i „9 dni".
        /// Bajty se nemeni; stary vyznam prepocitava <see cref="UtcTimeOfDay"/>.</para>
        /// </summary>
        public const int FormatVersion = 3;

        public GPSState() : base(FormatVersion)
        {
        }

        /// <summary>
        /// Fix quality indicater
        /// </summary>
        public enum FixQuality
        {
            /// <summary>Fix not available or invalid</summary>
            Invalid = 0,
            /// <summary>GPS SPS Mode, fix valid</summary>
            GpsFix = 1,
            /// <summary>Differential GPS, SPS Mode, or Satellite Based Augmentation System (SBAS), fix valid</summary>
            DgpsFix = 2,
            /// <summary>GPS PPS (Precise Positioning Service) mode, fix valid</summary>
            PpsFix = 3,
            /// <summary>Real Time Kinematic (Fixed). System used in RTK mode with fixed integers</summary>
            Rtk = 4,
            /// <summary>Real Time Kinematic (Floating). Satellite system used in RTK mode, floating integers</summary>
            FloatRtk = 5,
            /// <summary>Estimated (dead reckoning) mode</summary>
            Estimated = 6,
            /// <summary>Manual input mode</summary>
            ManualInput = 7,
            /// <summary>Simulator mode</summary>
            Simulation = 8
        }

        /// <summary>
        /// Data o poloze jsou platna.
        /// </summary>
        public bool IsFixed => Quality == FixQuality.DgpsFix || Quality == FixQuality.PpsFix || Quality == FixQuality.GpsFix;
        /// <summary>
        /// <b>UTC cas dne</b>, kdy byl fix porizen (od verze 3 u vsech ovladacu); <see cref="TimeSpan.Zero"/>
        /// = neznamy (virtualni GPS, u-blox bez platneho UTC). Pro cteni ze zaznamu pouzij
        /// <see cref="UtcTimeOfDay"/> — ta zvladne i starsi verze, kde to u u-bloxu byl rozbity ITOW.
        /// </summary>
        public TimeSpan FixTime { get; set; }

        /// <summary>Rozdil GPS − UTC (prestupne sekundy, plati od 1. 1. 2017).</summary>
        public static readonly TimeSpan GpsMinusUtc = TimeSpan.FromSeconds(18);

        /// <summary>
        /// UTC cas dne fixu, nebo <c>null</c>, kdyz ho zprava nenese. Jedine misto, ktere zna
        /// vyznam <see cref="FixTime"/> napric verzemi zaznamu:
        /// <list type="bullet">
        /// <item>verze 3+: <see cref="FixTime"/> primo;</item>
        /// <item>verze 1–2 a hodnota pod 1 den: cas dne z NMEA (nebo u-blox v nedeli 00–01 h GPS,
        /// kde rozbity rozklad dava spravne cislo — pak je to GPS cas, o 18 s napred);</item>
        /// <item>verze 1–2 a hodnota nad 1 den: rozbity ITOW z u-bloxu → <see cref="ItowFromBrokenUblox"/>
        /// a odecet prestupnych sekund.</item>
        /// </list>
        /// </summary>
        public TimeSpan? UtcTimeOfDay()
        {
            if (FixTime <= TimeSpan.Zero) return null;
            if (Verze >= 3 || FixTime < TimeSpan.FromDays(1)) return FixTime;
            long itow = ItowFromBrokenUblox(FixTime);
            if (itow < 0) return null;
            var gps = TimeSpan.FromMilliseconds(itow % 86400000L) - GpsMinusUtc;
            return gps < TimeSpan.Zero ? gps + TimeSpan.FromDays(1) : gps;
        }

        /// <summary>
        /// Zpetny prevod <see cref="FixTime"/> ze zaznamu verze 1–2 na ITOW [ms] (cas v GPS tydnu).
        ///
        /// <para>Ovladac u-bloxu ho do 27. 9. 2026 skladal <b>spatne</b>: sekundy pocital jako
        /// <c>ITOW/1000 − ((d·24 + h)·60 + m·60)</c>, tedy hodiny nasobil 60 misto 3600. Chyba je
        /// <b>deterministicka</b>: <c>TotalMs = ITOW + 84 960 000·D + 3 540 000·H</c>, kde <c>D</c> a
        /// <c>H</c> jsou den a hodina plynouci z ITOW — a da se invertovat. Musi se, protoze starsi
        /// zaznamy se prepsat nedaji a je to v nich jediny absolutni cas, ktery nepochazi z hodin Pi.</para>
        ///
        /// <para>Vraci −1, kdyz zadna dvojice (D, H) nevyjde konzistentne. ⚠️ Nevola se na hodnoty
        /// pod 1 den: tam je rozbity i spravny rozklad totez cislo (D = H = 0) a mohl by to byt
        /// i NMEA cas dne.</para>
        /// </summary>
        public static long ItowFromBrokenUblox(TimeSpan fix)
        {
            long total = (long)fix.TotalMilliseconds;
            const long Tyden = 7L * 86400000L;
            for (int d = 0; d < 7; d++)
                for (int hh = 0; hh < 24; hh++)
                {
                    long i = total - 84_960_000L * d - 3_540_000L * hh;
                    if (i < 0 || i >= Tyden) continue;
                    if (i / 86400000L != d) continue;
                    if (i / 3600000L - 24L * d != hh) continue;
                    return i;
                }
            return -1;
        }

        /// <summary>
        /// Zemepisna sirka v <b>RADIANECH</b> (od 26. 8. 2026; do te doby to byly stupne).
        ///
        /// <para><b>Proc radiany:</b> tatáž jednotka, jakou drzi <see cref="Coordinates.LLA"/>,
        /// <c>GeoReference</c> i cely zbytek systemu. Dokud tady byly stupne, byl <c>GPSState</c>
        /// jedine misto s jinou konvenci — a protoze <c>new LLA(gps.Latitude, gps.Longitude)</c> je
        /// ta nejprirozenejsi vec, kterou clovek napise, byla to <b>tichá a fatalni</b> past:
        /// <c>DefaultMeasurementMapper</c> na ni musel mit varovny komentar a mise Robotour do ni
        /// stejne spadla (uvizla v armovani, protoze rozptyl fixu vysel astronomicky).
        /// Rozhodnuti autora: zmenit jednotku tak, aby nejprirozenejsi zapis byl <b>spravny</b>.</para>
        ///
        /// <para><b>Prevod je na okrajich:</b> drivery (NMEA, u-blox) parsuji stupne a prevadeji je
        /// sem, UI a telemetrie prevadeji zpatky na stupne pro zobrazeni.</para>
        /// </summary>
        public double Latitude { get; set; }

        /// <summary>Zemepisna delka v <b>RADIANECH</b>; viz <see cref="Latitude"/>.</summary>
        public double Longitude { get; set; }

        /// <summary>
        /// Fix Quality
        /// </summary>
        public FixQuality Quality { get; set; }

        /// <summary>
        /// Number of satellites being tracked
        /// </summary>
        public int NumberOfSatellites { get; set; }

        /// <summary>
        /// Horizontal Dilution of Precision
        /// </summary>
        public double Hdop { get; set; }

        /// <summary>
        /// Altitude
        /// </summary>
        public double Altitude { get; set; }
        /// <summary>
        /// Orientation calculated from motion.
        /// Matematicky smysl  v radianech.
        /// </summary>
        public double? DynamicOrientation { get; set; }
        /// <summary>
        /// Speed calculated from motion in m/s.
        /// </summary>
        public double? DynamicSpeed { get; set; }
        /// <summary>
        /// Orientation calculated from two antena GPS
        /// </summary>
        public double? Orientation { get; set; }
        /// <summary>
        /// Speed reported by GPS in m/s.
        /// </summary>
        public double? Speed { get; set; }

        /// <inheritdoc/>
        public override Message Build() => new GPSState();

        /// <inheritdoc/>
        public override void ToData(BinaryWriter bw)
        {
            WriteMeta(bw);
            bw.Write(FixTime.Ticks);
            bw.Write(Latitude);
            bw.Write(Longitude);
            bw.Write((int)Quality);
            bw.Write(NumberOfSatellites);
            bw.Write(Hdop);
            bw.Write(Altitude);
            Write(bw, DynamicOrientation);
            Write(bw, DynamicSpeed);
            Write(bw, Orientation);
            Write(bw, Speed);
        }

        /// <inheritdoc/>
        public override void FromData(BinaryReader br)
        {
            ReadMeta(br);
            FixTime = new TimeSpan(br.ReadInt64());
            Latitude = br.ReadDouble();
            Longitude = br.ReadDouble();

            // Verze 1 drzela STUPNE. Prevod na radiany je jediny zpusob, jak archivni zaznamy
            // nezmenit v tiche nesmysly - viz FormatVersion.
            if (Verze < 2)
            {
                Latitude = Common.Conversions.Deg2Rad(Latitude);
                Longitude = Common.Conversions.Deg2Rad(Longitude);
            }
            Quality = (FixQuality)br.ReadInt32();
            NumberOfSatellites = br.ReadInt32();
            Hdop = br.ReadDouble();
            Altitude = br.ReadDouble();
            DynamicOrientation = ReadDouble(br);
            DynamicSpeed = ReadDouble(br);
            Orientation = ReadDouble(br);
            Speed = ReadDouble(br);
        }
    }
}
