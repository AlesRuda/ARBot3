using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ARBot.HAL;
using System.IO;
using System.Runtime.InteropServices;

namespace ARBot.HALLinux
{
    public class I2C : II2C, IDisposable
    {
        const int I2C_SLAVE=0x0703;	/* Use this slave address */
        const int I2C_SLAVE_FORCE=0x0706;	// Use this slave address, even if it is already in use by a driver! 
        const int O_RDWR=2;

        int bus;
        bool disposed = false;
        object lck = new object();


        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        private static extern int LSetAdr(IntPtr fd, Int32 code, Int32 adr);
        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern IntPtr LOpen(string fn, Int32 mode);
        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern void LClose(IntPtr fd);
        [DllImport("libc", EntryPoint = "read", SetLastError = true)]
        private static extern int LRead(IntPtr fd, byte[] data, Int32 len);
        [DllImport("libc", EntryPoint = "write", SetLastError = true)]
        private static extern int LWrite(IntPtr fd, byte[] data, Int32 len);

        public I2C(int bus)
        {
            this.bus = bus;
            LinuxOpen(string.Format("/dev/i2c-{0}", bus));
        }
        ~I2C()
        {
            Dispose(false);
        }

#if true
        IntPtr handler;
        void LinuxOpen(string file)
        {
            handler = LOpen(file, O_RDWR);
            if(((int)handler)<0)
                throw new Exception(string.Format("Can't open {0}", file));
        }
        void LinuxClose()
        {
            LClose(handler);
        }
        void SetAddr(int addr)
        {
            if (LSetAdr(handler, I2C_SLAVE, addr) == -1)
                throw new Exception(string.Format("Failed to set address to {0}.\n", addr));
        }


        public void Write(int address, byte register, byte[] data)
        {
            lock (lck)
            {
                byte[] d = new byte[data.Length + 1];
                d[0] = register;
                data.CopyTo(d, 1);
                SetAddr(address);
                LWrite(handler, d, d.Length);
            }
        }

        public void Write(int address, byte register, byte data)
        {
            lock (lck)
            {
                byte[] d = new byte[2];
                d[0] = register;
                d[1] = data;
                SetAddr(address);
                LWrite(handler, d, 2);
            }
        }

        public byte[] Read(int address, byte register, int len)
        {
            lock (lck)
            {
                int l;
                byte[] data = new byte[len];
                data[0] = 255;
                SetAddr(address);
                byte[] d = new byte[1];
                d[0] = register;
                LWrite(handler, d, 1);

                if ((l = LRead(handler, data, len)) != len)
                    throw new Exception(string.Format("Wrong len {0} expected {1}.", l, len));
                return data;
            }
        }

#else
        FileStream stream;
        void LinuxOpen(string file)
        {
            stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite);
        }
        void LinuxClose()
        {
                if (stream != null)
                {
                    stream.Flush();
                    stream.Close();
                    stream.Dispose();
                }
                stream = null;
        }
        void SetAddr(int addr)
        {
            if (IoCtrlSetAdr(stream.Handle, I2C_SLAVE, addr) == -1)
                throw new Exception(string.Format("Failed to set address to {0}.\n", addr));
        }


        public void Write(int address, byte register, byte[] data)
        {
            SetAddr(address);
            stream.WriteByte(register);
            stream.Write(data, 0, data.Length);
        }

        public byte[] Read(int address, byte register, int len)
        {
            byte[] data = new byte[len];
            SetAddr(address);
            stream.WriteByte(register);
            stream.Read(data, 0, data.Length);
            return data;
        }
#endif


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

                LinuxClose();

            }
            disposed = true;
        }
    }
}
