using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.HAL.Devices.Sonars
{
    /// <summary>
    /// SRF08 sonar driver
    /// </summary>
    public class SRF08 : ISonar
    {
        const int CmdRangeInches = 0x50;
        const int CmdRangeCentimeters = 0x51;
        const int CmdRangeus = 0x52;
        const int CmdANNInches = 0x53;
        const int CmdANNCentimeters = 0x54;
        const int CmdANNus = 0x55;

        const int RegSWRevision = 0x00;
        const int RegCommand = 0x00;
        const int RegLight = 0x01;
        const int RegMaxGain = 0x01;
        const int Reg1stEchoHi = 0x02;
        const int RegRange = 0x02;
        const int Reg1stEchoLo = 0x03;
        const int CountEchos = 0x11;

        const int MaxGain = 0x1f;
        const int MinGain = 0x00;

        const int MaxRange = 0xff;
        const int Range6m = 0x8c;
        const int Range3m = 0x48;



        II2C bus;
        int address;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="bus"></param>
        /// <param name="address"></param>
        public SRF08(II2C bus, int address)
        {
            if (bus == null)
                throw new ArgumentNullException("bus");

            this.bus = bus;
            this.address = address;

            if (address != 0)
            {
                bus.Write(address, RegMaxGain, MinGain);
                bus.Write(address, RegRange, Range3m);
            }
        }

        public void Ping(byte cmd)
        {
            bus.Write(address, RegCommand, cmd);
        }

        /// <summary>
        /// Ping this sonar
        /// </summary>
        public void Ping()
        {
            Ping(CmdRangeCentimeters);
        }

        /// <summary>
        /// Returns distance in meters
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        public double? Distance(int num)
        {
            byte[] v = bus.Read(address, RegSWRevision, 1);
            if (v.Length == 0 || v[0] == 0xff)
                return -1;

            v = bus.Read(address, Reg1stEchoHi, 2);
            if (v.Length != 2)
                throw new Exception("Chybna delka");
            double r = (v[0] << 8) + v[1];
            if (r == 0)
                return null;
            return r / 100;
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
        /// Number of sonar receivers
        /// </summary>
        public int Count
        {
            get { return 1; }
        }
    }
}
