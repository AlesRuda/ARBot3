using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ARBot.HAL.Devices.MotorDrivers
{
    /// <summary>
    /// Implement Roboteq SDC2160 driver
    /// </summary>
    public class SDC2160: UartSensorBase<IMotorState>, IMotorControl
    {
        double maxPossibleSpeed;
        double speedLimit;
        double enc2Dist;
        double wheelCircumference;
        double enc2Rotation;
        bool isEmergencyStop=true;
        double lastRightEnc, lastLeftEnc;

        /// <summary>Cas predchoziho vzorku - z nej se pocita rychlost kol.</summary>
        DateTime? prevEncTime;
        /// <summary>
        /// Construktor
        /// </summary>
        /// <param name="uart">UART used to comunication</param>
        public SDC2160(IUart uart, double maxPossibleSpeed, double speedLimit, double wheelCircumference, double enc2Rotation):base(uart)
        {
            this.maxPossibleSpeed = maxPossibleSpeed;
            this.speedLimit = Math.Min(speedLimit, maxPossibleSpeed);
            this.wheelCircumference = wheelCircumference;
            this.enc2Rotation = enc2Rotation;

            this.enc2Dist = wheelCircumference / enc2Rotation;

            uart.WriteLine("^ECHOF 1");
            Drive(0, 0);

            Start();
        }

        /// <summary>
        /// Jmeno sensoru, ktere se zobrazuje v logu a GUI
        /// </summary>
        public override string Name => "SDC2160";

        private int CalcSpeed(double speed)
        {
            double d = speed;
            int i = (int)(1000 * d / maxPossibleSpeed);
            return Math.Min(Math.Max(i, -1000), 1000);
        }

        /// <summary>
        /// Sets motors speed 
        /// </summary>
        /// <param name="forvardSpeed">Forvard speed (left and right motor common speed).</param>
        /// <param name="difSpeed">Diferencial speed. Positive value - right rotation, left motor is faster.</param>
        public void Drive(double forvardSpeed, double difSpeed)
        {
            if (forvardSpeed > speedLimit)
                forvardSpeed = speedLimit;
            if (forvardSpeed < -speedLimit)
                forvardSpeed = -speedLimit;

            // pokud by bylo kolo rychlejsi jak maxPossibleSpeed, tak sniz doprednou rychlost
            double diff = forvardSpeed + Math.Abs(difSpeed) - maxPossibleSpeed;
            if (diff > 0)
                forvardSpeed -= diff;
            // pokud by bylo kolo pomalejsi jak -maxPossibleSpeed, tak sniz doprednou rychlost
            diff = forvardSpeed - Math.Abs(difSpeed) + maxPossibleSpeed;
            if (diff < 0)
                forvardSpeed -= diff;
            double d1 = -CalcSpeed(forvardSpeed + difSpeed);
            double d2 = CalcSpeed(forvardSpeed - difSpeed);// tenhle motor je zapojenej s opacnou polaritou
            uart.WriteLine(string.Format("!G 1 {0}", isEmergencyStop ? 0 : d1));
            uart.WriteLine(string.Format("!G 2 {0}", isEmergencyStop ? 0 : d2));
            //            Debug.WriteLine(string.Format("!G {0} {1} {2} {3}", d1, d2, forvardSpeed, difSpeed));
        }

        /// <summary>
        /// Sets motor driver acceleration/deceleration
        /// </summary>
        /// <param name="acceleration"></param>
        public void SetAcceleration(double acceleration)
        {
            int v = (int)Math.Round(10 * 60 * acceleration / wheelCircumference);
//            Debug.WriteLine(string.Format("Acc={0}", v));
            uart.WriteLine(string.Format("!AC 1 {0}", v));
            uart.WriteLine(string.Format("!DC 1 {0}", v));
            uart.WriteLine(string.Format("!AC 2 {0}", v));
            uart.WriteLine(string.Format("!DC 2 {0}", v));
        }
/*
        /// <summary>
        /// Emergency stop
        /// </summary>
        public void EmergencyStop()
        {
            uart.WriteLine("!EX");
        }

        /// <summary>
        /// Recover from emergency stop
        /// </summary>
        public void Release()
        {
            uart.WriteLine("!MG");
        }
*/
        private string GetValue(string str)
        {
            if (str == null)
                return "";
            int idx = str.IndexOf("=");
            if (idx > -1)
                return str.Substring(idx + 1);
            return str;
        }

        protected override void Pickedup(IMotorState s)
        {
            base.Pickedup(s);
            lastRightEnc = 0;
            lastLeftEnc = 0;
        }

        protected override IMotorState GetMeasurement()
        {
            uart.ReadAll();   // vyprazdni vstupni buffer (vysledek se zahazuje)
            var ts = TimeBase.Now;
            uart.WriteLine("?CR");
            uart.WriteLine("?V 2");
            uart.WriteLine("?A");
            uart.WriteLine("?DI 3");


            string str = uart.ReadLine();
            if (str == null)
            {
                // Port nedostupny (ReadLine vraci null hned, ReOpen uz neblokuje): vrat
                // emergency stav misto bogus nuloveho a kratce pockej, aby smycka Process
                // nebusy-spinovala (nenulovy stav ji jinak nenechaji backnout).
                System.Threading.Thread.Sleep(10);
                return new MotorStateBase(isEmergencyStop = true, 0, 0, 0, 0, 0, 0, 0) { TimeStamp = ts };
            }
            str = GetValue(str);
            string[] enc = str.Split(new string[] { ":" }, StringSplitOptions.RemoveEmptyEntries);

            double leftEnc = 0;
            double rightEnc = 0;

            if (enc.Length > 0 && double.TryParse(enc[0], out rightEnc))
                rightEnc *= -enc2Dist;
            if (enc.Length > 1 && double.TryParse(enc[1], out leftEnc))
                leftEnc *= enc2Dist;

            str = uart.ReadLine();
            str = GetValue(str);
            double batVolts = 0;
            if (double.TryParse(str, out batVolts))
                batVolts /= 10;

            str = uart.ReadLine();
            str = GetValue(str);
            string[] amp = str.Split(new string[] { ":" }, StringSplitOptions.RemoveEmptyEntries);

            double leftCurrent = 0;
            double rightCurrent = 0;

            if (amp.Length > 0 && double.TryParse(amp[0], out leftCurrent))
                leftCurrent /= 10;
            if (amp.Length > 1 && double.TryParse(amp[1], out rightCurrent))
                rightCurrent /= 10;

            str = uart.ReadLine();
            str = GetValue(str);

            lastLeftEnc += leftEnc;
            lastRightEnc += rightEnc;

            // Rychlost z vlastniho vzorkovaciho intervalu (leftEnc je prirustek za tento vzorek) -
            // nesmi zaviset na tom, kdo a kdy mereni cte. Viz doc/virtual-hw.md.
            double dt = prevEncTime.HasValue ? (ts - prevEncTime.Value).TotalSeconds : 0;
            double leftSpeed = dt > 0.001 ? leftEnc / dt : 0;
            double rightSpeed = dt > 0.001 ? rightEnc / dt : 0;
            prevEncTime = ts;

            MotorStateBase s = new MotorStateBase(isEmergencyStop = (str == "0"), lastLeftEnc, lastRightEnc,
                                                  batVolts, leftCurrent, rightCurrent,
                                                  leftSpeed, rightSpeed) { TimeStamp = ts };
            return s;
        }
    }
}
