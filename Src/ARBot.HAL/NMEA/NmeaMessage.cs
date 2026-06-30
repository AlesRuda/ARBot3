using ARBot.HAL.NMEA;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.NMEA
{
    /// <summary>
    /// NMEA Message base class.
    /// </summary>
    public class NmeaMessage
    {
        /// <summary>
        /// Initializes an instance of the NMEA message
        /// </summary>
        /// <param name="messageType">Type</param>
        /// <param name="messageParts">Message values</param>
        protected NmeaMessage(string messageType, string[] messageParts)
        {
            MessageType = messageType;
            MessageParts = messageParts;
            Timestamp = System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>
        /// Parses the specified NMEA message.
        /// </summary>
        /// <param name="message">The NMEA message string.</param>
        /// <param name="ignoreChecksum">If <c>true</c> ignores the checksum completely, if <c>false</c> validates the checksum if present.</param>
        /// <returns>The nmea message that was parsed.</returns>
        /// <exception cref="System.ArgumentException">
        /// Invalid nmea message: Missing starting character '$'
        /// or checksum failure
        /// </exception>
        public static NmeaMessage Parse(string message, bool ignoreChecksum = false)
        {
            if (string.IsNullOrEmpty(message))
                throw new ArgumentNullException(nameof(message));

            int checksum = -1;
            if (message[0] != '$')
                throw new ArgumentException("Invalid NMEA message: Missing starting character '$'");
            var idx = message.IndexOf('*');
            if (idx >= 0)
            {
                if (message.Length > idx + 1)
                {
                    if (int.TryParse(message.Substring(idx + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int c))
                        checksum = c;
                    else
                        throw new ArgumentException("Invalid checksum string");
                }
                message = message.Substring(0, message.IndexOf('*'));
            }
            if (!ignoreChecksum && checksum > -1)
            {
                int checksumTest = 0;
                for (int i = 1; i < message.Length; i++)
                {
                    var c = message[i];
                    if (c < 0x20 || c > 0x7E)
                        throw new System.IO.InvalidDataException("NMEA Message contains invalid characters");
                    checksumTest ^= Convert.ToByte(c);
                }
                if (checksum != checksumTest)
                    throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Invalid NMEA message: Checksum failure. Got {0:X2}, Expected {1:X2}", checksum, checksumTest));
            }
            else
            {
                for (int i = 1; i < message.Length; i++)
                {
                    if (message[i] < 0x20 || message[i] > 0x7E)
                        throw new System.IO.InvalidDataException("NMEA Message contains invalid characters");
                }
            }

            string[] parts = message.Split(new char[] { ',' });
            string MessageType = parts[0].Substring(1);
            if (MessageType == string.Empty)
                throw new ArgumentException("Missing NMEA Message Type");
            string[] MessageParts = parts.Skip(1).ToArray();

            if (MessageType.Substring(2) == "GGA")
                return new GgaMessage(MessageType, parts);
            if (MessageType.Substring(2) == "VTG")
                return new VtgMessage(MessageType, parts);
            return new NmeaMessage(MessageType, parts);
        }

        /// <summary>
        /// Gets the NMEA message parts.
        /// </summary>
        protected IReadOnlyList<string> MessageParts { get; }

        /// <summary>
        /// Gets the NMEA type id for the message.
        /// </summary>
        /// <value>The 5 character string that identifies the message type</value>
        public string MessageType { get; }

        /// <summary>
        /// Gets a value indicating whether this message type is proprietary
        /// </summary>
        public bool IsProprietary => MessageType[0] == 'P'; //Appendix B

        /// <summary>
        /// Returns the original NMEA string that represents this message.
        /// </summary>
        /// <returns>An original NMEA string that represents this message.</returns>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "${0},{1}*{2:X2}", MessageType, string.Join(",", MessageParts), Checksum);
        }

        /// <summary>
        /// Gets the checksum value of the message.
        /// </summary>
        public byte Checksum => GetChecksum(MessageType, MessageParts);

        internal static byte GetChecksum(string messageType, IReadOnlyList<string> messageParts)
        {
            int checksumTest = 0;
            for (int j = -1; j < messageParts.Count; j++)
            {
                string message = j < 0 ? messageType : messageParts[j];
                if (j >= 0)
                    checksumTest ^= 0x2C; //Comma separator
                for (int i = 0; i < message.Length; i++)
                {
                    var c = message[i];
                    if (c < 256)
                        checksumTest ^= Convert.ToByte(c);
                }
            }
            return Convert.ToByte(checksumTest);
        }

        internal static double StringToLatitude(string value, string ns)
        {
            if (value == null || value.Length < 3)
                return double.NaN;
            double latitude = int.Parse(value.Substring(0, 2), CultureInfo.InvariantCulture) + double.Parse(value.Substring(2), CultureInfo.InvariantCulture) / 60;
            if (ns == "S")
                latitude *= -1;
            return latitude;
        }

        internal static double StringToLongitude(string value, string ew)
        {
            if (value == null || value.Length < 4)
                return double.NaN;
            double longitude = int.Parse(value.Substring(0, 3), CultureInfo.InvariantCulture) + double.Parse(value.Substring(3), CultureInfo.InvariantCulture) / 60;
            if (ew == "W")
                longitude *= -1;
            return longitude;
        }

        internal static double StringToDouble(string value)
        {
            if (value != null && double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return double.NaN;
        }
        internal static TimeSpan StringToTimeSpan(string value)
        {
            if (value != null && value.Length >= 6)
            {
                return new TimeSpan(int.Parse(value.Substring(0, 2), CultureInfo.InvariantCulture),
                                   int.Parse(value.Substring(2, 2), CultureInfo.InvariantCulture), 0)
                                   .Add(TimeSpan.FromSeconds(double.Parse(value.Substring(4), CultureInfo.InvariantCulture)));
            }
            return TimeSpan.Zero;
        }

        /// <summary>
        /// Gets a relative timestamp in milliseconds indicating the time the message was created.
        /// </summary>
        /// <remarks>
        /// This value is deduced from <c>System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency</c>.
        /// You can use it to calculate the age of the message in milliseconds by calculating the difference between the timestamp and the above expression
        /// </remarks>
        public double Timestamp { get; }
    }
}
