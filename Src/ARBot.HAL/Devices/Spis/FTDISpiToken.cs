using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.Spis
{
    public class FTDISpiToken:IDisposable
    {
        private FTDISpi spi;
        public FTDISpiToken(FTDISpi spi)
        {
            if (spi == null)
                throw new ArgumentNullException("spi");
            this.spi = spi;
        }

        public void Init(bool hiSpeedClock, uint clockDivisor)
        {
            spi.CheckToken(this);

            if (spi.HiSpeedClock != hiSpeedClock)
                spi.SetHiSpeedClock(this, hiSpeedClock);
            if (spi.ClockDivisor != clockDivisor)
                spi.SetClock(this, clockDivisor);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;

                if (disposing)
                {
                    spi.ReleaseToken(this);
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~SpiToken() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
