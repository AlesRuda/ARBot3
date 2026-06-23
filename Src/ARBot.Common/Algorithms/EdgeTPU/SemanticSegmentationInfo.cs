using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.EdgeTPU
{
    /// <summary>
    /// Struktura pro semantickou segmentaci
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SemanticSegmentationInfo
    {
        public IntPtr Model;
        public IntPtr Options;
        public IntPtr Delegate;
        public IntPtr Interpreter;
        public IntPtr InputTensor;
        public IntPtr OutputTensor;
        public int Inputs;
        public int Outputs;
        public Size2D InputSize;
        public Size2D OutputSize;
        public int InputType;
        public int OutputType;
        public int OutputChanels;
        // pocet bajtu jednoho prvku vstupu
        public int InputItemLen;
        // pocet bajtu jednoho prvku vystupu
        public int OutputItemLen;
        // pocet prvku vstupu
        public int InputLen;
        // pocet prvku vystupu
        public int OutputLen;
        public IntPtr InputData;
        public IntPtr OutputData;
    }
}
