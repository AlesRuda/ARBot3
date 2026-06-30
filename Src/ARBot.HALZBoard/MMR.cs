using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ARBot.HAL;
using System.Runtime.InteropServices;

namespace ARBot.HALLinux
{
    /// <summary>
    /// Zpristupnuje registry mapovane do pameti
    /// </summary>
    public class MMR:IMMR
    {
        const int O_RDWR = 2;

        [DllImport("libc", EntryPoint = "mmap", SetLastError = true)]
        private static extern IntPtr Lmmap(IntPtr addr, int len, int prot, int flags, IntPtr fd, uint pgoffset);
        [DllImport("libc", EntryPoint = "munmap", SetLastError = true)]
        private static extern int Lmunmap(IntPtr addr, int len);

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern IntPtr LOpen(string fn, Int32 mode);
        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern void LClose(IntPtr fd);


        uint physicalAddres;
        int len;
        bool disposed = false;
        IntPtr registerFileHandler;
        IntPtr registerFileVirtualAddress;

        public MMR(uint physicalAddres, int len)
        {
            this.physicalAddres = physicalAddres;
            this.len = len;

            registerFileHandler = LOpen("/dev/mem", O_RDWR);
            if (((int)registerFileHandler) < 0)
                throw new Exception(string.Format("Can't open {0}", "/dev/mem"));

            registerFileVirtualAddress = Lmmap((IntPtr)0, len, 3 /*PROT_READ | PROT_WRITE*/, 1 /*MAP_SHARED*/, registerFileHandler, physicalAddres);
        }

        ~MMR()
        {
            Dispose(false);
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

                Lmunmap(registerFileVirtualAddress, len);
                LClose(registerFileHandler);

            }
            disposed = true;
        }



        private void CheckAddres(int adr, int boundary)
        {
            if (adr < 0)
                throw new ArgumentException("Parameter adr can't be negative.");
            if (adr*boundary>len)
                throw new ArgumentException(string.Format("Parameter adr {0:x} can't exceed len {1:x}.", adr, len/boundary));
        }

        /// <summary>
        /// Cte 8bitovou hodnotu z adresy 
        /// </summary>
        /// <param name="adr">Adresa bytu</param>
        /// <returns></returns>
        public uint Get8(int adr)
        {
            CheckAddres(adr, 1);
            unsafe
            {
                byte* bp = (byte *)registerFileVirtualAddress;
                bp += adr;
                return *bp;
            }
        }

        /// <summary>
        /// Cte 16bitovou hodnotu z adr
        /// </summary>
        /// <param name="adr">Adresa slova. Slova s adresou 1 je pristupne jako dva bajty s adresou 2 a 3.</param>
        /// <returns></returns>
        public uint Get16(int adr)
        {
            CheckAddres(adr, 2);
            unsafe
            {
                Int16* bp = (Int16*)registerFileVirtualAddress;
                bp += adr;
                return (uint)*bp;
            }
        }

        /// <summary>
        /// Cte 32bitovou hodnotu z adr
        /// </summary>
        /// <param name="adr">Adresa dwordu. DWord s adresou 1 je pristupny jako dva wordy s adresou 2 a 3.</param>
        /// <returns></returns>
        public uint Get32(int adr)
        {
            CheckAddres(adr, 4);
            unsafe
            {
                UInt32* bp = (UInt32*)registerFileVirtualAddress;
                bp += adr;
                return *bp;
            }
        }

        public void Set8(int adr, uint val)
        {
            CheckAddres(adr, 1);
            unsafe
            {
                byte* bp = (byte*)registerFileVirtualAddress;
                bp += adr;
                *bp=(byte)val;
            }
        }

        public void Set16(int adr, uint val)
        {
            CheckAddres(adr, 2);
            unsafe
            {
                UInt16* bp = (UInt16*)registerFileVirtualAddress;
                bp += adr;
                *bp = (UInt16)val;
            }
        }

        public void Set32(int adr, uint val)
        {
            CheckAddres(adr, 2);
            unsafe
            {
                UInt32* bp = (UInt32*)registerFileVirtualAddress;
                bp += adr;
                *bp = (UInt32)val;
            }
        }
    }
}
