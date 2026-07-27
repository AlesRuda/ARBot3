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
        /// <summary>Verze formatu serializace (viz doc/record-replay.md → Verzovani zprav).</summary>
        public const int FormatVersion = 1;

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
        /// Time of day fix was taken
        /// </summary>
        public TimeSpan FixTime { get; set; }

        /// <summary>
        /// Latitude
        /// </summary>
        public double Latitude { get; set; }

        /// <summary>
        /// Longitude
        /// </summary>
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
