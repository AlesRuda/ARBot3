using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.NMEA
{
    /// <summary>
    /// Course over ground and ground speed
    /// </summary>
    /// <remarks>
    /// The actual course and speed relative to the ground.
    /// </remarks>
    public class VtgMessage:NmeaMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Vtg"/> class.
        /// </summary>
        /// <param name="type">The message type</param>
        /// <param name="message">The NMEA message values.</param>
        public VtgMessage(string type, string[] message) : base(type, message)
        {
            if (message == null || message.Length < 7)
                throw new ArgumentException("Invalid VTG", "message");
            CourseTrue = NmeaMessage.StringToDouble(message[0]);
            CourseMagnetic = NmeaMessage.StringToDouble(message[2]);
            SpeedKnots = NmeaMessage.StringToDouble(message[4]);
            SpeedKph = NmeaMessage.StringToDouble(message[6]);
        }

        /// <summary>
        ///  Course over ground relative to true north
        /// </summary>
        public double CourseTrue { get; }

        /// <summary>
        ///  Course over ground relative to magnetic north
        /// </summary>
        public double CourseMagnetic { get; }

        /// <summary>
        /// Speed over ground in knots
        /// </summary>
        public double SpeedKnots { get; }

        /// <summary>
        /// Speed over ground in kilometers/hour
        /// </summary>
        public double SpeedKph { get; }
    }
}
