using ARBot.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.HAL.Devices.MotorDrivers
{
/*    /// <summary>
    ///  motor control unit driver.
    /// </summary>
    public class MD23 : IMotorControl
    {
        const byte CmdResetEncoders = 0x20;
        const byte CmdDisableSpeedRegulation = 0x30;
        const byte CmdEnableSpeedRegulation = 0x31;
        const byte CmdDisableFailSafe = 0x32;
        const byte CmdEnableFailSafe = 0x33;

        const byte Mode0UnsingnedSeparate = 0x00;
        const byte Mode1SingnedSeparate = 0x01;
        const byte Mode2UnsingnedTurn = 0x02;
        const byte Mode3SingnedTurn = 0x03;

        const byte RegSpeed1 = 0x00;
        const byte RegSpeed2 = 0x01;
        const byte RegEnc1a = 0x02;
        const byte RegEnc1b = 0x03;
        const byte RegEnc1c = 0x04;
        const byte RegEnc1d = 0x05;
        const byte RegEnc2a = 0x06;
        const byte RegEnc2b = 0x07;
        const byte RegEnc2c = 0x08;
        const byte RegEnc2d = 0x09;
        const byte RegBateryVolts = 0x0A;
        const byte RegMotor1Current = 0x0B;
        const byte RegMotor2Current = 0x0C;
        const byte RegRevision = 0x0D;
        const byte RegAcceleration = 0x0E;
        const byte RegMode = 0x0F;
        const byte RegCommand = 0x10;

        const double NoLoadRPM = 216;
        const double MaxTorque = 0.7;
        const double MotorTs = 0.025;

        II2C bus;
        int address;
        double weight;
        double wheelRadius;
        double speedLimit;
        double perimeter;
        double enc2Length;
        byte accConst;
        bool isEmergencyStop=false;


        public MD23(II2C bus, int address, double weight, double wheelRadius, double speedLimit)
        {
            if (bus == null)
                throw new ArgumentNullException("bus");
            this.bus = bus;
            this.address = address;
            this.weight = weight;
            this.wheelRadius = wheelRadius;
            this.speedLimit = speedLimit;
            perimeter = 2 * Math.PI * wheelRadius;
            enc2Length = perimeter / 360.0;
            accConst = 5;
        }


        public void Init()
        {
            bus.Write(address, RegCommand, CmdResetEncoders);
            bus.Write(address, RegCommand, CmdEnableSpeedRegulation);
            bus.Write(address, RegCommand, CmdEnableFailSafe);
            bus.Write(address, RegMode, Mode3SingnedTurn);
            bus.Write(address, RegAcceleration, AccConst);

            Drive(0, 0);
        }

        /// <summary>
        /// MD23 acceleratin constant
        /// </summary>
        public byte AccConst
        {
            get
            {
                return accConst;
            }
            set
            {
                accConst = value;
                Init();
            }
        }

        /// <summary>
        /// Acceleration for AccConst
        /// </summary>
        public double Acceleration
        {
            get
            {
                return AccConst * (perimeter * NoLoadRPM) / (2.0 * 60.0 * 127.0 * MotorTs);
            }
        }


        /// <summary>
        /// Maximal acceleration for weight
        /// </summary>
        public double MaxAcceleration
        {
            get
            {
                return Torque(speedLimit) / wheelRadius / weight;
            }
        }

        /// <summary>
        /// Maximal MD23 acceleratin constant for weight
        /// </summary>
        public byte MaxAccConst
        {
            get
            {
                return (byte)((2.0 * 60.0 * 127.0 * MaxAcceleration * MotorTs) / (perimeter * NoLoadRPM));
            }
        }

        protected double Torque(double velocity)
        {
            double rpm = 60.0 * velocity / perimeter;
            if (rpm > NoLoadRPM)
                rpm = MaxTorque;
            return MaxTorque - rpm * (MaxTorque / NoLoadRPM);

        }

        protected byte CalcMD23Speed(double speed)
        {
            double d = speed;
            if (d > speedLimit)
                d = speedLimit;
            if (d < -speedLimit)
                d = -speedLimit;
            return (byte)((d * 127.0) / (perimeter * NoLoadRPM));
        }

        /// <summary>
        /// Sets motors speed 
        /// </summary>
        /// <param name="forvardSpeed">Forvard speed (left and right motor common speed).</param>
        /// <param name="difSpeed">Diferencial speed. Positive value - right rotation, left motor is faster.</param>
        public void Drive(double forvardSpeed, double difSpeed)
        {
            if (isEmergencyStop)
            {
                bus.Write(address, RegSpeed1, CalcMD23Speed(0));
                bus.Write(address, RegSpeed2, CalcMD23Speed(0));
            }
            else
            {
                bus.Write(address, RegSpeed1, CalcMD23Speed(forvardSpeed));
                bus.Write(address, RegSpeed2, CalcMD23Speed(difSpeed));
            }
        }

        /// <summary>
        /// Returns state of motor control unit.
        /// </summary>
        /// <returns></returns>
        public IMotorState GetState(IMotorState last, double Ts)
        {
            double volts, c1, c2;
            byte[] v = bus.Read(address, RegBateryVolts, 1);
            volts=((double)v[0])/10.0;
            v = bus.Read(address, RegMotor1Current, 1);
            c1=((double)v[0])/10.0;
            v = bus.Read(address, RegMotor2Current, 1);
            c2=((double)v[0])/10.0;

            double left=GetMD23Enc(RegEnc1a);
                double right=GetMD23Enc(RegEnc1a);

            MotorStateBase ms = new MotorStateBase(isEmergencyStop, left+last.LeftEncoder, right+last.RightEncoder, volts, c1, c2, left*Ts, right*Ts);
            return ms;
        }

        double GetMD23Enc(byte reg)
        {
            byte[] v = bus.Read(address, reg, 4);
            if (v.Length != 4)
                throw new Exception("Chybna delka");

            uint r = 0;
            r = v[0];
            r <<= 8;

            r |= v[1];
            r <<= 8;

            r |= v[2];
            r <<= 8;

            r |= v[3];

            return r*enc2Length;
        }


        /// <summary>
        /// Change adress od MD23 unit
        /// </summary>
        /// <param name="newAddress"></param>
        public void ChangeAddress(int newAddress)
        {
            bus.Write(address, RegCommand, 0xA0);
            bus.Write(address, RegCommand, 0xAA);
            bus.Write(address, RegCommand, 0xA5);
            bus.Write(address, RegCommand, (byte)(newAddress * 2));
            address = newAddress;
        }


        /// <summary>
        /// Emergency stop
        /// </summary>
        public void EmergencyStop()
        {
            Drive(0, 0);
            isEmergencyStop = true;
        }

        /// <summary>
        /// Recover from emergency stop
        /// </summary>
        public void Release()
        {
            isEmergencyStop = false;
        }


        public void SetAcceleration(double acceleration)
        {
        }
    }*/
}
