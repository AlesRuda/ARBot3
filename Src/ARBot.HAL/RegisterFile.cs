using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.HAL
{
    public static class RegisterFile
    {
        /// <summary>
        /// RegisterFile physical address
        /// </summary>
        public const uint RegisterFilePhysicalAddres = 0x43C00000;//0x6FE00000;
        /// <summary>
        /// RegisterFile bytes
        /// </summary>
        public const int RegisterFileBytes = 32 * 4;

        /// <summary>
        /// ControlRegister1
        /// </summary>
        public static class CR1
        {
            public const int Address = 0;
            public const uint CAM1_RightAddr = 1 << 0;
            public const uint CAM1_Exp = 1 << 1;
            public const uint SonarPingRq = 1 << 6;

            public static uint LeftBlueRedpresent(uint val)
            {
                return val << 2;
            }

            public static uint RightBlueRedpresent(uint val)
            {
                return val << 4;
            }

            // 1. line GR, 2. line RG
            public const uint GRBG=0;
            // 1. line RG, 2. line GB
            public const uint RGGB=1;
            // 1. line BG, 2. line GR
            public const uint BGGR=2;
            // 1. line GB, 2. line RG
            public const uint GBRG=3;

            /// <summary>
            /// Globalni reset logiky v PL, aktivni v nule
            /// </summary>
            public const uint GlobalReset = 0x80000000;
        }

        /// <summary>
        /// Second control register
        /// </summary>
        //        public const int ControlReg2=4;
        public static class CR2
        {
            public const int Address = 4;
            public static uint DataTaps(uint val)
            {
                return val & 0x1f;
            }
        }

        /// <summary>
        /// First status register
        /// </summary>
        public static class StatusReg1
        {
            public const int Address = 8;
            public const int CAM1_DELAYCTRL1 = 1;
            public const int CAM1_CAM1_SerErr = 2;
            public const int CAM1_Sync = 4;
        }
            
        /// <summary>
        /// Control register Or offset
        /// </summary>
        public const int OrOffset = 1;
        /// <summary>
        /// Control register And offset
        /// </summary>
        public const int AndOffset = 2;
        /// <summary>
        /// Control register Xor offset
        /// </summary>
        public const int XorOffset = 3;

        /// <summary>
        /// First read only register
        /// </summary>
        public const int ReadOnlyReg1=10;
        /// <summary>
        /// CAM1 AEC R
        /// </summary>
        public const int ReadOnlyReg2=11;
        /// <summary>
        /// CAM1 AEC G
        /// </summary>
        public const int ReadOnlyReg3=12;
        /// <summary>
        /// CAM1 AEC B
        /// </summary>
        public const int ReadOnlyReg4=13;

        /// <summary>
        /// Segmentation unit
        /// </summary>
        public const int RGBSegmentationUnit=14;

        /// <summary>
        /// Camera control register
        /// </summary>
        public const int ControlRegCam=16;

        /// <summary>
        /// SND Generator
        /// </summary>
        public const int SoundGenerator=20;

        /// <summary>
        /// NeoPixel driver
        /// </summary>
        public const int NeoPixel = 24;

        /// <summary>
        /// Sonars
        /// </summary>
        public const int Sonar0 = 30;


        public static uint Read(IMMR mmr, int adr)
        {
    		return mmr.Get32(adr);
    	}

        public static void Write(IMMR mmr, int adr, uint val)
        {
		    mmr.Set32(adr, val);
        }

        public static void Set(IMMR mmr, int adr, uint val)
        {
		    mmr.Set32(adr+OrOffset, val);
        }

        public static void Clear(IMMR mmr, int adr, uint val)
        {
            mmr.Set32(adr + AndOffset, ~val);
        }
    }
}
