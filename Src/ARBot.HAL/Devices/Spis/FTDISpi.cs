using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ARBot.HAL.Devices.Spis
{
    public class FTDISpi
    {
        public enum ChipSelectPin
        {
            Pin3ChipSelect = 0,
            Pin4 = 1,
            Pin5 = 2,
            Pin6 = 3,
            Pin7 = 4
        }

        public struct InitCondition
        {
            public bool ClockPinState;
            public bool DataOutPinState;
            public bool ChipSelectPinState;
            public ChipSelectPin ChipSelectPin;
        }

        public enum WaitDataWritePin
        {
            DataIn = 0,
            Pin0 = 1,
            Pin1 = 2,
            Pin2 = 3,
            Pin3 = 4
        }

        public struct WaitDataWrite
        {
            /// <summary>
            /// Wait until all data bytes have been written to an External device, wait(TRUE), do not wait(FALSE).
            /// </summary>
            public bool WaitDataWriteComplete;
            /// <summary>
            /// Specifies which pin on the FT2232D dual device, indicates, when all the data bytes have been 
            /// written to an external device.If one of the 4 higher GPIO pins (GPIOH1 - GPIOH4) is selected,
            /// it must have been previously configured as an input pin.
            /// </summary>
            public WaitDataWritePin WaitDataWritePin;
            /// <summary>
            /// Specifies what state indicates that all data bytes have been written to an external device, low(FALSE), high(TRUE).
            /// </summary>
            public bool DataWriteCompleteState;
            /// <summary>
            /// Timeout interval in milliseconds to wait for all data bytes to be written to an external device.
            /// </summary>
            public uint DataWriteTimeoutmSecs;
        }

        public enum HiSpeedDeviceType
        {
            FT2232H = 1,
            FT4232H = 2
        };

        public class HiSpeedDeviceInfo
        {
            public uint LocationID { get; set; }
            public string DeviceName { get; set; }
            public string Channel { get; set; }
            public HiSpeedDeviceType Type { get; set; }
            public override string ToString()
            {
                return string.Format("DeviceName='{0}', LocationID='{1}', Channel='{2}', Type='{3}'", DeviceName, LocationID, Channel, Type);
            }
        }

        public struct HigherOutputPins
        {
            public bool Pin1State;
            public bool Pin1ActiveState;
            public bool Pin2State;
            public bool Pin2ActiveState;
            public bool Pin3State;
            public bool Pin3ActiveState;
            public bool Pin4State;
            public bool Pin4ActiveState;
            public bool Pin5State;
            public bool Pin5ActiveState;
            public bool Pin6State;
            public bool Pin6ActiveState;
            public bool Pin7State;
            public bool Pin7ActiveState;
            public bool Pin8State;
            public bool Pin8ActiveState;
        }

        public struct ChipSelectPins
        {
            public bool Pin3ChipSelectState;
            public bool Pin4State;
            public bool Pin5State;
            public bool Pin6State;
            public bool Pin7State;
        }

        public struct LowHighPins
        {
            public bool Pin1LowHighState;
            public bool Pin2LowHighState;
            public bool Pin3LowHighState;
            public bool Pin4LowHighState;
            public bool Pin5LowHighState;
            public bool Pin6LowHighState;
            public bool Pin7LowHighState;
            public bool Pin8LowHighState;
        }

        public struct InputOutputPins
        {
            public bool Pin1InputOutputState;
            public bool Pin1LowHighState;
            public bool Pin2InputOutputState;
            public bool Pin2LowHighState;
            public bool Pin3InputOutputState;
            public bool Pin3LowHighState;
            public bool Pin4InputOutputState;
            public bool Pin4LowHighState;
            public bool Pin5InputOutputState;
            public bool Pin5LowHighState;
            public bool Pin6InputOutputState;
            public bool Pin6LowHighState;
            public bool Pin7InputOutputState;
            public bool Pin7LowHighState;
            public bool Pin8InputOutputState;
            public bool Pin8LowHighState;
        }

        public struct CloseFinalStatePins
        {
            public bool TCKPinState;
            public bool TCKPinActiveState;
            public bool TDIPinState;
            public bool TDIPinActiveState;
            public bool TMSPinState;
            public bool TMSPinActiveState;
        }

#if IsX64
        [DllImportAttribute("ftcspi64.dll", EntryPoint = "SPI_GetDllVersion", CallingConvention = CallingConvention.StdCall)]
        static extern uint GetDllVersion(byte[] pDllVersion, uint buufferSize);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetErrorCodeString(string language, uint statusCode, byte[] pErrorMessage, uint bufferSize);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetNumHiSpeedDevices(ref uint NumHiSpeedDevices);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetHiSpeedDeviceNameLocIDChannel(uint deviceNameIndex, byte[] pDeviceName, uint deviceNameBufferSize, ref uint locationID, byte[] pChannel, uint channelBufferSize, ref uint hiSpeedDeviceType);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_OpenHiSpeedDevice(string DeviceName, uint locationID, string channel, ref Int32 pftHandle);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetHiSpeedDeviceType(Int32 ftHandle, ref uint hiSpeedDeviceType);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_Close(IntPtr ftHandle);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_CloseDevice(IntPtr ftHandle, ref CloseFinalStatePins pCloseFinalStatePinsData);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_InitDevice(Int32 ftHandle, uint clockDivisor);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_TurnOnDivideByFiveClockingHiSpeedDevice(Int32 ftHandle);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_TurnOffDivideByFiveClockingHiSpeedDevice(Int32 ftHandle);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_SetDeviceLatencyTimer(Int32 ftHandle, byte timerValue);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetDeviceLatencyTimer(Int32 ftHandle, ref byte timerValue);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetHiSpeedDeviceClock(uint ClockDivisor, ref uint clockFrequencyHz);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetClock(uint clockDivisor, ref uint clockFrequencyHz);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_SetClock(Int32 ftHandle, uint clockDivisor, ref uint clockFrequencyHz);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_SetLoopback(IntPtr ftHandle, bool bLoopBackState);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_SetHiSpeedDeviceGPIOs(IntPtr ftHandle, ref ChipSelectPins pChipSelectsDisableStates, ref InputOutputPins pHighInputOutputPinsData);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetHiSpeedDeviceGPIOs(IntPtr ftHandle, out LowHighPins pHighPinsInputData);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_WriteHiSpeedDevice(Int32 ftHandle, ref InitCondition pWriteStartCondition, bool bClockOutDataBitsMSBFirst, bool bClockOutDataBitsPosEdge, uint numControlBitsToWrite, byte[] pWriteControlBuffer, uint numControlBytesToWrite, bool bWriteDataBits, uint numDataBitsToWrite, byte[] pWriteDataBuffer, uint numDataBytesToWrite, ref WaitDataWrite pWaitDataWriteComplete, ref HigherOutputPins pHighPinsWriteActiveStates);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_ReadHiSpeedDevice(Int32 ftHandle, ref InitCondition pReadStartCondition, bool bClockOutControBitsMSBFirst, bool bClockOutControBitsPosEdge, uint numControlBitsToWrite, byte[] pWriteControlBuffer, uint numControlBytesToWrite, bool bClockInDataBitsMSBFirst, bool bClockInDataBitsPosEdge, uint numDataBitsToRead, byte[] pReadDataBuffer, out uint pnumDataBytesReturned, ref HigherOutputPins pHighPinsReadActiveStates);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_ClearDeviceCmdSequence(Int32 ftHandle);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_AddHiSpeedDeviceReadCmd(Int32 ftHandle, ref InitCondition pReadStartCondition, bool bClockOutControBitsMSBFirst, bool bClockOutControBitsPosEdge, uint numControlBitsToWrite, byte[] pWriteControlBuffer, uint numControlBytesToWrite, bool bClockInDataBitsMSBFirst, bool bClockInDataBitsPosEdge, uint numDataBitsToRead, ref HigherOutputPins pHighPinsReadActiveStates);

        [DllImportAttribute("ftcspi64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_ExecuteDeviceCmdSequence(Int32 ftHandle, byte[] pReadCmdSequenceDataBuffer, out uint pnumDataBytesReturned);
#else
        [DllImportAttribute("ftcspi.dll", EntryPoint = "SPI_GetDllVersion", CallingConvention = CallingConvention.StdCall)]
        static extern uint GetDllVersion(byte[] pDllVersion, uint buufferSize);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetErrorCodeString(string language, uint statusCode, byte[] pErrorMessage, uint bufferSize);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetNumHiSpeedDevices(ref uint NumHiSpeedDevices);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetHiSpeedDeviceNameLocIDChannel(uint deviceNameIndex, byte[] pDeviceName, uint deviceNameBufferSize, ref uint locationID, byte[] pChannel, uint channelBufferSize, ref uint hiSpeedDeviceType);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_OpenHiSpeedDevice(string DeviceName, uint locationID, string channel, ref IntPtr pftHandle);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetHiSpeedDeviceType(IntPtr ftHandle, ref uint hiSpeedDeviceType);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_Close(IntPtr ftHandle);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_CloseDevice(IntPtr ftHandle, ref CloseFinalStatePins pCloseFinalStatePinsData);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_InitDevice(IntPtr ftHandle, uint clockDivisor);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_TurnOnDivideByFiveClockingHiSpeedDevice(IntPtr ftHandle);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_TurnOffDivideByFiveClockingHiSpeedDevice(IntPtr ftHandle);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_SetDeviceLatencyTimer(IntPtr ftHandle, byte timerValue);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetDeviceLatencyTimer(IntPtr ftHandle, ref byte timerValue);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetHiSpeedDeviceClock(uint ClockDivisor, ref uint clockFrequencyHz);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetClock(uint clockDivisor, ref uint clockFrequencyHz);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_SetClock(IntPtr ftHandle, uint clockDivisor, ref uint clockFrequencyHz);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_SetLoopback(IntPtr ftHandle, bool bLoopBackState);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_SetHiSpeedDeviceGPIOs(IntPtr ftHandle, ref ChipSelectPins pChipSelectsDisableStates, ref InputOutputPins pHighInputOutputPinsData);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_GetHiSpeedDeviceGPIOs(IntPtr ftHandle, out LowHighPins pHighPinsInputData);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_WriteHiSpeedDevice(IntPtr ftHandle, ref InitCondition pWriteStartCondition, bool bClockOutDataBitsMSBFirst, bool bClockOutDataBitsPosEdge, uint numControlBitsToWrite, byte[] pWriteControlBuffer, uint numControlBytesToWrite, bool bWriteDataBits, uint numDataBitsToWrite, byte[] pWriteDataBuffer, uint numDataBytesToWrite, ref WaitDataWrite pWaitDataWriteComplete, ref HigherOutputPins pHighPinsWriteActiveStates);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_ReadHiSpeedDevice(IntPtr ftHandle, ref InitCondition pReadStartCondition, bool bClockOutControBitsMSBFirst, bool bClockOutControBitsPosEdge, uint numControlBitsToWrite, byte[] pWriteControlBuffer, uint numControlBytesToWrite, bool bClockInDataBitsMSBFirst, bool bClockInDataBitsPosEdge, uint numDataBitsToRead, byte[] pReadDataBuffer, out uint pnumDataBytesReturned, ref HigherOutputPins pHighPinsReadActiveStates);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_ClearDeviceCmdSequence(IntPtr ftHandle);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_AddHiSpeedDeviceReadCmd(IntPtr ftHandle, ref InitCondition pReadStartCondition, bool bClockOutControBitsMSBFirst, bool bClockOutControBitsPosEdge, uint numControlBitsToWrite, byte[] pWriteControlBuffer, uint numControlBytesToWrite, bool bClockInDataBitsMSBFirst, bool bClockInDataBitsPosEdge, uint numDataBitsToRead, ref HigherOutputPins pHighPinsReadActiveStates);

        [DllImportAttribute("ftcspi.dll", CallingConvention = CallingConvention.StdCall)]
        static extern uint SPI_ExecuteDeviceCmdSequence(IntPtr ftHandle, byte[] pReadCmdSequenceDataBuffer, out uint pnumDataBytesReturned);
#endif

        private static Dictionary<uint, string> errors = new Dictionary<uint, string> {
            {0, "FTC_SUCCESS"},
            {1, "FTC_INVALID_HANDLE"},
            {2, "FTC_DEVICE_NOT_FOUND"},
            {3, "FTC_DEVICE_NOT_OPENED"},
            {4, "FTC_IO_ERROR"},
            {5, "FTC_INSUFFICIENT_RESOURCES"},
            {20, "FTC_FAILED_TO_COMPLETE_COMMAND"},
            {21, "FTC_FAILED_TO_SYNCHRONIZE_DEVICE_MPSSE"},
            {22, "FTC_INVALID_DEVICE_NAME_INDEX"},
            {23, "FTC_NULL_DEVICE_NAME_BUFFER_POINTER"},
            {24, "FTC_DEVICE_NAME_BUFFER_TOO_SMALL"},
            {25, "FTC_INVALID_DEVICE_NAME"},
            {26, "FTC_INVALID_LOCATION_ID"},
            {27, "FTC_DEVICE_IN_USE"},
            {28, "FTC_TOO_MANY_DEVICES"},
            {29, "FTC_NULL_CHANNEL_BUFFER_POINTER"},
            {30, "FTC_CHANNEL_BUFFER_TOO_SMALL"},
            {31, "FTC_INVALID_CHANNEL"},
            {32, "FTC_INVALID_TIMER_VALUE"},
            {33, "FTC_INVALID_CLOCK_DIVISOR"},
            {34, "FTC_NULL_INPUT_BUFFER_POINTER"},
            {35, "FTC_NULL_CHIP_SELECT_BUFFER_POINTER"},
            {36, "FTC_NULL_INPUT_OUTPUT_BUFFER_POINTER"},
            {37, "FTC_NULL_OUTPUT_PINS_BUFFER_POINTER"},
            {38, "FTC_NULL_INITIAL_CONDITION_BUFFER_POINTER"},
            {39, "FTC_NULL_WRITE_CONTROL_BUFFER_POINTER"},
            {40, "FTC_NULL_WRITE_DATA_BUFFER_POINTER"},
            {41, "FTC_NULL_WAIT_DATA_WRITE_BUFFER_POINTER"},
            {42, "FTC_NULL_READ_DATA_BUFFER_POINTER"},
            {43, "FTC_NULL_READ_CMDS_DATA_BUFFER_POINTER"},
            {44, "FTC_INVALID_NUMBER_CONTROL_BITS"},
            {45, "FTC_INVALID_NUMBER_CONTROL_BYTES"},
            {46, "FTC_NUMBER_CONTROL_BYTES_TOO_SMALL"},
            {47, "FTC_INVALID_NUMBER_WRITE_DATA_BITS"},
            {48, "FTC_INVALID_NUMBER_WRITE_DATA_BYTES"},
            {49, "FTC_NUMBER_WRITE_DATA_BYTES_TOO_SMALL"},
            {50, "FTC_INVALID_NUMBER_READ_DATA_BITS"},
            {51, "FTC_INVALID_INIT_CLOCK_PIN_STATE"},
            {52, "FTC_INVALID_FT2232C_CHIP_SELECT_PIN"},
            {53, "FTC_INVALID_FT2232C_DATA_WRITE_COMPLETE_PIN"},
            {54, "FTC_DATA_WRITE_COMPLETE_TIMEOUT"},
            {55, "FTC_INVALID_CONFIGURATION_HIGHER_GPIO_PIN"},
            {56, "FTC_COMMAND_SEQUENCE_BUFFER_FULL"},
            {57, "FTC_NO_COMMAND_SEQUENCE"},
            {58, "FTC_NULL_CLOSE_FINAL_STATE_BUFFER_POINTER"},
            {59, "FTC_NULL_DLL_VERSION_BUFFER_POINTER"},
            {60, "FTC_DLL_VERSION_BUFFER_TOO_SMALL"},
            {61, "FTC_NULL_LANGUAGE_CODE_BUFFER_POINTER"},
            {62, "FTC_NULL_ERROR_MESSAGE_BUFFER_POINTER"},
            {63, "FTC_ERROR_MESSAGE_BUFFER_TOO_SMALL"},
            {64, "FTC_INVALID_LANGUAGE_CODE"},
            {65, "FTC_INVALID_STATUS_CODE"}};


        private static string TranslateStatus(uint status)
        {
            if (errors.ContainsKey(status))
                return errors[status];
            return string.Format("Unknow SPI error {0}", status);
        }

        private static Exception Status2Exception(uint status)
        {
            if (status == 0)
                return null;
            return new Exception(GetErrorCodeString(status));
        }

        private static Exception Status2Exception(string name, uint status)
        {
            if (status == 0)
                return null;
            return new Exception(string.Format("{0}: {1}", name, GetErrorCodeString(status)));
        }

        private static string GetErrorCodeString(uint status)
        {
            byte[] msg = new byte[256];
            uint s = SPI_GetErrorCodeString(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToUpper(), status, msg, 255);
            if (s == 0)
            {
                string m = Encoding.ASCII.GetString(msg);
                // Trim strings to first occurrence of a null terminator character
                m = m.Substring(0, m.IndexOf("\0"));

                return m;
            }
            return TranslateStatus(status);
        }
        /// <summary>
        /// Pocet zarizeni k dispozici
        /// </summary>
        public static uint NumDevices
        {
            get
            {
                uint num = 0;
                uint status = SPI_GetNumHiSpeedDevices(ref num);
                if (status != 0)
                    throw Status2Exception(status);
                return num;
            }
        }
        /// <summary>
        /// Podrobnejsi informace o konkretnim zarizeni
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        public static HiSpeedDeviceInfo GetDeviceInfo(uint num)
        {
            uint MAX_NUM_DEVICE_NAME_CHARS = 100;
            uint MAX_NUM_CHANNEL_CHARS = 5;
            uint hiSpeedDeviceType = 0;
            uint locationID = 0;

            byte[] byteHiSpeedDeviceName = new byte[MAX_NUM_DEVICE_NAME_CHARS];
            byte[] byteHiSpeedDeviceChannel = new byte[MAX_NUM_CHANNEL_CHARS];

            uint status = SPI_GetHiSpeedDeviceNameLocIDChannel(num, byteHiSpeedDeviceName, MAX_NUM_DEVICE_NAME_CHARS, ref locationID, byteHiSpeedDeviceChannel, MAX_NUM_CHANNEL_CHARS, ref hiSpeedDeviceType);

            if (status != 0)
                throw Status2Exception(status);

            string hiSpeedChannel = Encoding.ASCII.GetString(byteHiSpeedDeviceChannel);
            // Trim strings to first occurrence of a null terminator character
            hiSpeedChannel = hiSpeedChannel.Substring(0, hiSpeedChannel.IndexOf("\0"));


            string hiSpeedDeviceName = Encoding.ASCII.GetString(byteHiSpeedDeviceName);
            // Trim strings to first occurrence of a null terminator character
            hiSpeedDeviceName = hiSpeedDeviceName.Substring(0, hiSpeedDeviceName.IndexOf("\0"));
            return new HiSpeedDeviceInfo() { DeviceName = hiSpeedDeviceName, Channel = hiSpeedChannel, Type = (HiSpeedDeviceType)hiSpeedDeviceType, LocationID = locationID };
        }

        private uint clockDivisor;
        Int32 handle;
        bool hiSpeedClock;
        bool emptySequence = true;
        public HiSpeedDeviceInfo Info { get; private set; }

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="info"></param>
        public FTDISpi(HiSpeedDeviceInfo info)
        {
            Console.WriteLine(string.Format("FTDI 01"));
            Info = info;
            uint status = SPI_OpenHiSpeedDevice(info.DeviceName, info.LocationID, info.Channel, ref handle);
            Console.WriteLine(string.Format("FTDI handle='{0}'", handle));
            if (status != 0)
                throw Status2Exception("SPI_OpenHiSpeedDevice", status);
            status = SPI_InitDevice(handle, 0);
            Console.WriteLine(string.Format("FTDI 1", handle));

            clockDivisor = 0;
            if (status != 0)
                throw Status2Exception("SPI_InitDevice", status);
            Console.WriteLine(string.Format("FTDI 2", handle));

            status = SPI_TurnOffDivideByFiveClockingHiSpeedDevice(handle);
            Console.WriteLine(string.Format("FTDI 3", handle));
             
            if (status != 0)
                throw Status2Exception("SPI_TurnOffDivideByFiveClockingHiSpeedDevice", status);


            this.hiSpeedClock = true;
        }

        /// <summary>
        /// Indikuje 60MHz/12MHz
        /// </summary>
        public bool HiSpeedClock
        {
            get
            {
                return hiSpeedClock;
            }
        }
        /// <summary>
        /// Nastavuje hodiny 60MHz/12MHz
        /// </summary>
        /// <param name="token"></param>
        /// <param name="hiSpeedClock"></param>
        public void SetHiSpeedClock(FTDISpiToken token, bool hiSpeedClock)
        {
            CheckToken(token);

            uint status;
            if (hiSpeedClock)
                status = SPI_TurnOffDivideByFiveClockingHiSpeedDevice(handle);
            else
                status = SPI_TurnOnDivideByFiveClockingHiSpeedDevice(handle);

            if (status != 0)
                throw Status2Exception(status);

            this.hiSpeedClock = hiSpeedClock;
        }
        /// <summary>
        /// Aktualni delici pmer
        /// </summary>
        public uint ClockDivisor
        {
            get
            {
                return clockDivisor;
            }
        }

        /// <summary>
        /// Typ zarizeni
        /// </summary>
        public HiSpeedDeviceType DeviceType
        {
            get
            {
                uint hiSpeedDeviceType = 0;
                uint status = SPI_GetHiSpeedDeviceType(handle, ref hiSpeedDeviceType);
                if (status != 0)
                    throw Status2Exception(status);

                return (HiSpeedDeviceType)hiSpeedDeviceType;
            }
        }
        /// <summary>
        /// USB latenci
        /// </summary>
        public byte LatencyTimer
        {
            get
            {
                byte timerValue = 0;
                uint status = SPI_GetDeviceLatencyTimer(handle, ref timerValue);
                if (status != 0)
                    throw Status2Exception(status);
                return timerValue;
            }
            set
            {
                uint status = SPI_SetDeviceLatencyTimer(handle, value);
                if (status != 0)
                    throw Status2Exception(status);
            }
        }
        /// <summary>
        /// Base clock frequency in HZ
        /// </summary>
        public uint DeviceClock
        {
            get
            {
                uint clockFrequencyHz = 0;
                uint status = SPI_GetHiSpeedDeviceClock(0, ref clockFrequencyHz);
                if (status != 0)
                    throw Status2Exception(status);
                return clockFrequencyHz;
            }
        }
        /// <summary>
        /// Calculates frequency for divisor
        /// </summary>
        /// <param name="clockDivisor"></param>
        /// <returns></returns>
        public uint CalcFrequencyHz(uint clockDivisor)
        {
            return DeviceClock / ((1 + clockDivisor) * 2);
        }
        /// <summary>
        /// Calculates divisor from frequency
        /// </summary>
        /// <param name="frequencyHz"></param>
        /// <returns></returns>
        public uint CalcDivisor(uint frequencyHz)
        {
            return (DeviceClock / (2 * frequencyHz)) - 1;
        }
        /// <summary>
        /// Nastavuje delici pomer
        /// </summary>
        /// <param name="token"></param>
        /// <param name="clockDivisor"></param>
        /// <returns></returns>
        public uint SetClock(FTDISpiToken token, uint clockDivisor)
        {
            CheckToken(token);

            uint clockFrequencyHz = 0;
            uint status = SPI_SetClock(handle, clockDivisor, ref clockFrequencyHz);
            if (status != 0)
                throw Status2Exception(status);
            this.clockDivisor = clockDivisor;
            return clockFrequencyHz;
        }
        /// <summary>
        /// Zapis na SPI
        /// </summary>
        /// <param name="token"></param>
        /// <param name="writeStartCondition"></param>
        /// <param name="clockOutDataBitsMSBFirst"></param>
        /// <param name="clockOutDataBitsPosEdge"></param>
        /// <param name="numControlBitsToWrite"></param>
        /// <param name="writeControlBuffer"></param>
        /// <param name="numDataBitsToWrite"></param>
        /// <param name="writeDataBuffer"></param>
        /// <param name="waitDataWriteComplete"></param>
        public void Write(FTDISpiToken token, InitCondition writeStartCondition, bool clockOutDataBitsMSBFirst, bool clockOutDataBitsPosEdge,
            uint numControlBitsToWrite, byte[] writeControlBuffer, uint numDataBitsToWrite, byte[] writeDataBuffer,
            WaitDataWrite waitDataWriteComplete)
        {
            CheckToken(token);

            if (!emptySequence)
                throw new Exception("Command sequence is not empty.");

            HigherOutputPins hiPins = new HigherOutputPins();

            uint numControlBytesToWrite = (numControlBitsToWrite + 7) / 8;
            if (numControlBytesToWrite < writeControlBuffer.Length)
                throw new ArgumentException("(numControlBitsToWrite+7)/8<writeControlBuffer.Length", "numControlBitsToWrite");

            uint numDataBytesToWrite = 0;
            if (writeDataBuffer != null)
                numDataBytesToWrite = (numDataBitsToWrite + 7) / 8;

            if (numDataBytesToWrite < (writeDataBuffer != null ? writeDataBuffer.Length : 0))
                throw new ArgumentException("(numDataBitsToWrite+7)/8<writeDataBuffer.Length", "numDataBitsToWrite");

            uint status = SPI_WriteHiSpeedDevice(handle, ref writeStartCondition,
                clockOutDataBitsMSBFirst, clockOutDataBitsPosEdge,
                numControlBitsToWrite, writeControlBuffer, numControlBytesToWrite,
                writeDataBuffer != null && writeDataBuffer.Length != 0, numDataBitsToWrite, writeDataBuffer, numDataBytesToWrite,
                ref waitDataWriteComplete, ref hiPins);
            if (status != 0)
                throw Status2Exception(status);
        }
        /// <summary>
        /// Cteni z SPI
        /// </summary>
        /// <param name="token"></param>
        /// <param name="readStartCondition"></param>
        /// <param name="clockOutDataBitsMSBFirst"></param>
        /// <param name="clockOutDataBitsPosEdge"></param>
        /// <param name="numControlBitsToWrite"></param>
        /// <param name="writeControlBuffer"></param>
        /// <param name="clockInDataBitsMSBFirst"></param>
        /// <param name="clockInDataBitsPosEdge"></param>
        /// <param name="numDataBitsToRead"></param>
        /// <returns></returns>
        public byte[] Read(FTDISpiToken token, InitCondition readStartCondition, bool clockOutDataBitsMSBFirst, bool clockOutDataBitsPosEdge,
            uint numControlBitsToWrite, byte[] writeControlBuffer,
            bool clockInDataBitsMSBFirst, bool clockInDataBitsPosEdge, uint numDataBitsToRead)
        {
            CheckToken(token);

            if (!emptySequence)
                throw new Exception("Command sequence is not empty.");

            HigherOutputPins hiPins = new HigherOutputPins();

            uint numControlBytesToWrite = (numControlBitsToWrite + 7) / 8;
            if (numControlBytesToWrite < writeControlBuffer.Length)
                throw new ArgumentException("(numControlBitsToWrite+7)/8<writeControlBuffer.Length", "numControlBitsToWrite");

            uint numDataBytesToRead = (numDataBitsToRead + 7) / 8;
            uint numDataBytesReaded = 0;
            byte[] data = new byte[numDataBytesToRead];

            uint status = SPI_ReadHiSpeedDevice(handle, ref readStartCondition,
                clockOutDataBitsMSBFirst, clockOutDataBitsPosEdge,
                numControlBitsToWrite, numControlBitsToWrite > 0 ? writeControlBuffer : new byte[1], Math.Max(1, numControlBytesToWrite),
                clockInDataBitsMSBFirst, clockInDataBitsPosEdge, numDataBitsToRead, data, out numDataBytesReaded, ref hiPins);

            if (status != 0)
                throw Status2Exception(status);

            byte[] ret = new byte[numDataBytesReaded];

            if (numDataBytesReaded != 0)
                Array.Copy(data, ret, numDataBytesReaded);
            return ret;
        }

        /// <summary>
        /// Maze sequenci prikazu
        /// </summary>
        /// <param name="token"></param>
        public void ClearSequence(FTDISpiToken token)
        {
            CheckToken(token);

            uint status = SPI_ClearDeviceCmdSequence(handle);
            if (status != 0)
                throw Status2Exception(status);
            emptySequence = true;
        }
        /// <summary>
        /// Pridava dalsi prikaz cteni do sequence
        /// </summary>
        /// <param name="token"></param>
        /// <param name="readStartCondition"></param>
        /// <param name="clockOutDataBitsMSBFirst"></param>
        /// <param name="clockOutDataBitsPosEdge"></param>
        /// <param name="numControlBitsToWrite"></param>
        /// <param name="writeControlBuffer"></param>
        /// <param name="clockInDataBitsMSBFirst"></param>
        /// <param name="clockInDataBitsPosEdge"></param>
        /// <param name="numDataBitsToRead"></param>
        public void AddRead(FTDISpiToken token, InitCondition readStartCondition, bool clockOutDataBitsMSBFirst, bool clockOutDataBitsPosEdge,
            uint numControlBitsToWrite, byte[] writeControlBuffer,
            bool clockInDataBitsMSBFirst, bool clockInDataBitsPosEdge, uint numDataBitsToRead)
        {
            CheckToken(token);

            HigherOutputPins hiPins = new HigherOutputPins();

            uint numControlBytesToWrite = (numControlBitsToWrite + 7) / 8;
            if (numControlBytesToWrite < writeControlBuffer.Length)
                throw new ArgumentException("(numControlBitsToWrite+7)/8<writeControlBuffer.Length", "numControlBitsToWrite");

            uint status = SPI_AddHiSpeedDeviceReadCmd(handle, ref readStartCondition,
                clockOutDataBitsMSBFirst, clockOutDataBitsPosEdge,
                numControlBitsToWrite, writeControlBuffer, numControlBytesToWrite,
                clockInDataBitsMSBFirst, clockInDataBitsPosEdge, numDataBitsToRead, ref hiPins);

            if (status != 0)
                throw Status2Exception(status);

            emptySequence = false;
        }

        /// <summary>
        /// Odesila sequenci prikazu
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public byte[] ExecuteSequence(FTDISpiToken token)
        {
            CheckToken(token);

            uint numDataBytesReturned = 0;
            byte[] data = new byte[131071];

            uint status =SPI_ExecuteDeviceCmdSequence(handle, data, out numDataBytesReturned);
            if (status != 0)
                throw Status2Exception(status);

            byte[] ret = new byte[numDataBytesReturned];

            if (numDataBytesReturned != 0)
                Array.Copy(data, ret, numDataBytesReturned);
            emptySequence = true;
            return ret;
        }

        private FTDISpiToken token = null;
        Thread thread = null;
        ReaderWriterLock lck = new ReaderWriterLock();

        /// <summary>
        /// Ziskava token pro pristup k funkcim SPI.
        /// Jen jeden thread muze vlastnist token, ostatni thready musi pockat.
        /// </summary>
        /// <param name="hiSpeedClock"></param>
        /// <param name="clockDivisor"></param>
        /// <returns></returns>
        public FTDISpiToken GetToken(bool hiSpeedClock, uint clockDivisor, int timeOut)
        {
            lck.AcquireWriterLock(timeOut);
            if (token != null)
                throw new Exception("ReleaseToken first.");
            token = new FTDISpiToken(this);
            thread = Thread.CurrentThread;
            token.Init(hiSpeedClock, clockDivisor);

            return token;
        }
        /// <summary>
        /// Ziskava token pro pristup k funkcim SPI.
        /// Jen jeden thread muze vlastnist token, ostatni thready musi pockat.
        /// </summary>
        /// <param name="hiSpeedClock"></param>
        /// <param name="clockDivisor"></param>
        /// <returns></returns>
        public FTDISpiToken GetToken(bool hiSpeedClock, uint clockDivisor)
        {
            return GetToken(HiSpeedClock, clockDivisor, int.MaxValue);
        }
        /// <summary>
        /// Vraci token
        /// </summary>
        /// <param name="token"></param>
        public void ReleaseToken(FTDISpiToken token)
        {
            if (token == null)
                throw new ArgumentNullException("token");
            CheckToken(token);
            token.Dispose();
            this.token = null;
            this.thread = null;
            lck.ReleaseLock();
        }
        /// <summary>
        /// Testuje rovnost tokenu a poznost pristupu threadu.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public void CheckToken(FTDISpiToken token)
        {
            if (this.token != token)
                throw new Exception("Token is not current.");
            if (!CheckAccess())
                throw new Exception("Token is not owned by this thread.");
        }
        /// <summary>
        /// Vraci true pokud je mozny pristup z current threadu.
        /// </summary>
        /// <returns></returns>
        public bool CheckAccess()
        {
            return thread == Thread.CurrentThread;
        }
    }
}