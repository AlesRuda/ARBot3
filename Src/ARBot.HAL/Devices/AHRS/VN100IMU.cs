using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using VectorNav.Communication;
using VectorNav.Maths;

namespace ARBot.HAL.Devices.AHRS
{
    public class VN100IMU : UartSensorBase<IMUState>, IIMU
    {
        VnAsciiPacket vn;
        DateTime lastReset;

        public VN100IMU(IUart uart):base(uart)
        {
            vn = new VnAsciiPacket();
            Reset();
            Start();
        }

        public override string Name => "VN100 IMU";

        /// <summary>
        /// Povoluje pouziti modelu magnetickeho a gravitacniho pole zeme v danem miste.
        /// </summary>
        /// <param name="lla"></param>
        public void SetModelParams(LLA lla)
        {
            string s = string.Format(CultureInfo.InvariantCulture, "$VNRRG,83,1,1,0,0,1000,{0:N3},{1:N3},{2:N3},{3:N3}", ((double)TimeBase.Now.Year)+((double)TimeBase.Now.DayOfYear/365), lla.Latitude, lla.Longitude, lla.Altitude);
            s = s + "*" + Compute8BitChecksum(s).ToString("X2");
            uart.WriteLine(s);
        }

        protected static double ParseAsVnDouble(string s)
        {
            double v = double.NaN;
            try
            {
                if (s.Contains("nan") || s.Contains("NAN"))
                    v = double.NaN;
                else
                    v = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Wrong numer {0}.", s), ex);
            }
            return v;
        }

        private byte Compute8BitChecksum(string packet)
        {
            byte num = (byte)0;
            for (int index = (int)packet[0] == 36 ? 1 : 0; index < packet.Length && (int)packet[index] != 42; ++index)
                num ^= (byte)packet[index];
            return num;
        }

        private ushort Compute16BitCrc(string packet)
        {
            ushort num1 = (ushort)0;
            for (int index = (int)packet[0] == 36 ? 1 : 0; index < packet.Length && (int)packet[index] != 42; ++index)
            {
                ushort num2 = (ushort)((uint)(ushort)((int)num1 >> 8 | (int)num1 << 8) ^ (uint)(byte)packet[index]);
                ushort num3 = (ushort)((uint)num2 ^ (uint)(ushort)((uint)(byte)((uint)num2 & (uint)byte.MaxValue) >> 4));
                ushort num4 = (ushort)((uint)num3 ^ (uint)(ushort)((int)num3 << 8 << 4));
                num1 = (ushort)((uint)num4 ^ (uint)(ushort)(((int)num4 & (int)byte.MaxValue) << 4 << 1));
            }
            return num1;
        }

        protected override IMUState GetMeasurement()
        {
            IMUState state = null;
            string l = null;
            try
            {
                l = uart.ReadLine();
                if (l != null)
                {
                    if (vn.VerifyChecksum(l))
                    {
                        IList<string> ls = VnAsciiPacket.SplitPacket(l);

                        if (ls[0] == "VNQTN")
                        {
                            var vals = ls.Skip(1).Select(i => ParseAsVnDouble(i)).ToList();

                            var q = Quaternion.Inverse(new Quaternion(
                                    (float)-vals[1],
                                    (float)-vals[0],
                                    (float)vals[2],
                                    (float)vals[3]));
                            state = new IMUState(q);
                            state.Confidence = 1;
                            state.TimeStamp = TimeBase.Now;
                        }
                        if (ls[0] == "VNQTR")
                        {
                            var vals = ls.Skip(1).Select(i => ParseAsVnDouble(i)).ToList();

                            var q = Quaternion.Inverse(new Quaternion(
                                    (float)-vals[1],
                                    (float)-vals[0],
                                    (float)vals[2],
                                    (float)vals[3]));

                            // Uhlova rychlost z gyroskopu je v BODY framu. Nasledujici prehazeni
                            // os a znamenek sjednocuje osy VN (X vpred, Y vpravo, Z dolu) na body
                            // konvenci projektu (X vpred, Y vlevo, Z nahoru) - stale BODY frame.
                            state = new IMUState(
                                q
                                ,
                                new Vector3(
                                    (float)-vals[5],
                                    (float)-vals[4],
                                    (float)-vals[6]
                                    )
                                );
                            state.Confidence = 1;
                            state.TimeStamp = TimeBase.Now;
                        }
                        if (ls[0] == "VNQMA")
                        {
                            var vals = ls.Skip(1).Select(i => ParseAsVnDouble(i)).ToList();

                            var q = Quaternion.Inverse(new Quaternion(
                                    (float)-vals[1],
                                    (float)-vals[0],
                                    (float)vals[2],
                                    (float)vals[3]));

                            state = new IMUState(
                                q
                                );

                            state.Magnetometer = new Vector3(
                                (float)vals[4],
                                (float)vals[5],
                                (float)vals[6]
                                );

                            state.Confidence = 1;
                            state.TimeStamp = TimeBase.Now;
                        }
                        if (ls.Count > 2 && ls[0] == "VNRRG" && ls[1] == "9")
                        {
                            var vals = ls.Skip(2).Select(i => ParseAsVnDouble(i)).ToList();

                            var q = Quaternion.Inverse(new Quaternion(
                                    (float)-vals[1],
                                    (float)-vals[0],
                                    (float)vals[2],
                                    (float)vals[3]));

                            state = new IMUState(q);
                            state.Confidence = 1;
                            state.TimeStamp = TimeBase.Now;
                        }
                    }
                    else
                    {
                        Debug.WriteLine(string.Format("VN100 wrong packet {0}", l));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            var ssAfterReset = (TimeBase.Now - lastReset).TotalSeconds;

            // pokud prijde z VN100 NAN tak paket zahod, asi by bylo pekne senzor resetnout
            if (double.IsNaN(state?.Rotation.Value.Z ?? 0))
            {
                Debug.WriteLine(string.Format("VN100 NAN received, measurement droped - {0}.",l));
                if(ssAfterReset>10)
                {
                    Reset();
                }
                state = null;
            }
            return state;
        }

        public void Reset()
        {
            uart.WriteLine("$VNRST * 4D");
            lastReset = TimeBase.Now;
        }
    }
}
