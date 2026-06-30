using ARBot.Common.Common;
using System;
using System.Collections.Generic;

namespace ARBot.HAL.Devices.Lidar
{
    public interface ILidar
    {
        event EventHandler<ScanReceivedEventArgs> ScanReceived;

        IEnumerable<BlindRegion> BlindRegions { get; }

        void Cancel();
        void Reset();
        void Scan();
        void Stop();
    }
}