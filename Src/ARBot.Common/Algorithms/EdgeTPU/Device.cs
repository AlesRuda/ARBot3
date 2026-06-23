
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.EdgeTPU
{
    /// <summary>
    /// Popisuje EdgeTPU zarizeni
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Device
    {
        /// <summary>
        /// Typ zarizeni, odpovida edgetpu_device_type
        /// </summary>
        public int Type;
        /// <summary>
        /// Pointer na cestu k zarizeni
        /// </summary>
        public IntPtr PathPtr;

        public string Path
        {
            get
            {
                return Marshal.PtrToStringAnsi(PathPtr);
            }
        }
    }
}
