using System;
using System.Collections.Generic;
using System.Text;
using ARBot.HAL;
using System.IO;
using System.Diagnostics;

namespace ARBot.HALLinux
{
    /// <summary>
    /// Access to Linux GPIO
    /// </summary>
    public class GPIO:IGPIO, IDisposable
    {
        const string controlFS="/sys/class/gpio/";
        const string exportFS=controlFS+"export";
        const string unExportFS = controlFS + "unexport";
        const string directionFS="direction";
        const string valueFS="value";
        const string edgeFS="edge";

        int number;
        bool disposed = false;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="number"></param>
        public GPIO(int number)
        {
            this.number = number;
            try
            {
                UnExport();
            }
            catch
            {
            }
            Export();
        }

        ~GPIO()
        {
            Dispose(false);
        }

        private void Export()
        {
            File.WriteAllText(exportFS, string.Format("{0}", number));
        }

        private void UnExport()
        {
            File.WriteAllText(unExportFS, string.Format("{0}", number));
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources.
                }

                UnExport();
            }
            disposed = true;
        }

        /// <summary>
        /// Value of the pin
        /// </summary>
        public bool Value
        {
            get
            {
                string s=File.ReadAllText(string.Format("{0}gpio{1}/{2}", controlFS, number, valueFS));
                return  s.StartsWith("1");
            }
            set
            {
                File.WriteAllText(string.Format("{0}gpio{1}/{2}", controlFS, number, valueFS), value ? "1" : "0");
            }
        }

        /// <summary>
        /// Direction
        /// </summary>
        public bool IsOutput
        {
            get
            {
                return File.ReadAllText(string.Format("{0}gpio{1}/{2}", controlFS, number, directionFS))=="out";
            }
            set
            {
                File.WriteAllText(string.Format("{0}gpio{1}/{2}", controlFS, number, directionFS), value?"out":"in");
            }
        }

        /// <summary>
        /// Edge sensitivity
        /// </summary>
        public GPIOEdge Edge
        {
            get
            {
                string s=File.ReadAllText(string.Format("{0}gpio{1}/{2}", controlFS, number, edgeFS));
                switch (s)
                {
                    case "both":
                        return GPIOEdge.Both;
                    case "rising":
                        return GPIOEdge.Rising;
                    case "falling":
                        return GPIOEdge.Falling;
                    case "none":
                        return GPIOEdge.None;
                }
                return GPIOEdge.None;
            }
            set
            {
                string s = "none";
                switch (value)
                {
                    case GPIOEdge.Both:
                        s = "both";
                        break;
                    case GPIOEdge.Rising:
                        s = "rising";
                        break;
                    case GPIOEdge.Falling:
                        s = "falling";
                        break;
                    case GPIOEdge.None:
                        s = "none";
                        break;
                }
                File.WriteAllText(string.Format("{0}gpio{1}/{2}", controlFS, number, edgeFS), s);
            }
        }
    }
}
