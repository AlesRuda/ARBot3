using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.NMEA
{
    public class GgaMessage : NmeaMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Gga"/> class.
        /// </summary>
        /// <param name="type">The message type</param>
        /// <param name="message">The NMEA message values.</param>
        public GgaMessage(string type, string[] message) : base(type, message)
        {
            if (message == null || message.Length < 14)
                throw new ArgumentException("Invalid GGA", "message");
            FixTime = StringToTimeSpan(message[0]);
            Latitude = NmeaMessage.StringToLatitude(message[1], message[2]);
            Longitude = NmeaMessage.StringToLongitude(message[3], message[4]);
            Quality = (FixQuality)int.Parse(message[5], CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(message[6]))
                NumberOfSatellites = int.Parse(message[6], CultureInfo.InvariantCulture);
            Hdop = NmeaMessage.StringToDouble(message[7]);
            Altitude = NmeaMessage.StringToDouble(message[8]);
            AltitudeUnits = message[9];
            GeoidalSeparation = NmeaMessage.StringToDouble(message[10]);
            GeoidalSeparationUnits = message[11];
            var timeInSeconds = StringToDouble(message[12]);
            if (!double.IsNaN(timeInSeconds))
                TimeSinceLastDgpsUpdate = TimeSpan.FromSeconds(timeInSeconds);
            else
                TimeSinceLastDgpsUpdate = null;
            if (message[13].Length > 0)
                DgpsStationId = int.Parse(message[13], CultureInfo.InvariantCulture);
            else
                DgpsStationId = -1;
        }

        /// <summary>
        /// Time of day fix was taken
        /// </summary>
        public TimeSpan FixTime { get; }

        /// <summary>
        /// Latitude
        /// </summary>
        public double Latitude { get; }

        /// <summary>
        /// Longitude
        /// </summary>
        public double Longitude { get; }

        /// <summary>
        /// Fix Quality
        /// </summary>
        public FixQuality Quality { get; }

        /// <summary>
        /// Number of satellites being tracked
        /// </summary>
        public int NumberOfSatellites { get; }

        /// <summary>
        /// Horizontal Dilution of Precision
        /// </summary>
        public double Hdop { get; }

        /// <summary>
        /// Altitude
        /// </summary>
        public double Altitude { get; }

        /// <summary>
        /// Altitude units ('M' for Meters)
        /// </summary>
        public string AltitudeUnits { get; }

        /// <summary>
        /// Geoidal separation: the difference between the WGS-84 earth ellipsoid surface and mean-sea-level (geoid) surface.
        /// </summary>
        /// <remarks>
        /// A negative value means mean-sea-level surface is below the WGS-84 ellipsoid surface.
        /// </remarks>
        /// <seealso cref="GeoidalSeparationUnits"/>
        public double GeoidalSeparation { get; }

        /// <summary>
        /// Altitude units ('M' for Meters)
        /// </summary>
        public string GeoidalSeparationUnits { get; }

        /// <summary>
        /// Time since last DGPS update (ie age of the differential GPS data)
        /// </summary>
        public TimeSpan? TimeSinceLastDgpsUpdate { get; }

        /// <summary>
        /// Differential Reference Station ID
        /// </summary>
        public int DgpsStationId { get; }
    }
}
