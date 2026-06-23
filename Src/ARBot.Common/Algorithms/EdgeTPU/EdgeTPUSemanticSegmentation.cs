using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.EdgeTPU
{
    public class EdgeTPUSemanticSegmentation:IBackProject, IDisposable 
    {
        private object lck = new object();
        private static object staticLck = new object();

#if IsX64
        [DllImport("EdgeTPUDll.dll", EntryPoint = "GetDevicesCount", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        private static extern int GetDevicesCount();
        /// <summary>
        /// Do pole devs nacte informace o dostupnych zarizenich. 
        /// </summary>
        /// <param name="devs">Vystupni pole, musi byt predem alokovano. Potrebny pocet prvku je GetDevicesCount</param>
        /// <param name="count">Pocet prvku pole devs.</param>
        [DllImport("EdgeTPUDll.dll", EntryPoint = "GetDevices", SetLastError = true)]        
        private static extern void GetDevices([In, Out] Device[] devs, int count);
        [DllImport("EdgeTPUDll.dll", EntryPoint = "LoadSemanticSegmentationModel", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        private static extern int LoadSemanticSegmentationModel(ref SemanticSegmentationInfo model, Device device, string modelPath);
        [DllImport("EdgeTPUDll.dll", EntryPoint = "FreeSemanticSegmentationModel", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void FreeSemanticSegmentationModel(ref SemanticSegmentationInfo model);
        [DllImport("EdgeTPUDll.dll", EntryPoint = "SemanticSegmentation", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        private static extern int SemanticSegmentation(ref SemanticSegmentationInfo model, int srcWidth, int srcHeight, byte[] src, byte[] dst);

#endif
        static Device[] devices;
        public static Device[] Devices
        {
            get
            {
                if (devices == null) 
                {
                    lock (staticLck)
                    {
                        if (devices == null)
                        {
                            var cnt = GetDevicesCount();
                            devices = new Device[cnt];
                            GetDevices(devices, cnt);
                        }
                    }
                }
                return devices;
            }
        }

        public static bool IsLoaded(Device d)
        {
            bool b = false;
            lock (staticLck)
                b = loaded.Values.Any(s => s.device.Path == d.Path && s.device.Type == d.Type);
            return b;
        }

        public static EdgeTPUSemanticSegmentation Create(Device dev, string modelPath)
        {
            EdgeTPUSemanticSegmentation s = null;
            lock (staticLck)
            {
                if (IsLoaded(dev))
                    throw new ArgumentException("Device is in use.", "dev");
                s = new EdgeTPUSemanticSegmentation(dev, modelPath);
                loaded.Add(modelPath, s);
            }
            return s;
        }

        public static EdgeTPUSemanticSegmentation GetOrCreate(string modelPath)
        {
            EdgeTPUSemanticSegmentation s = null;
            lock (staticLck)
            {
                if (loaded.ContainsKey(modelPath))
                    s = loaded[modelPath];
                else
                {
                    var devs = Devices.Where(d => !IsLoaded(d));
                    if (!devs.Any())
                        throw new Exception("All devices is in use.");

                    s = Create(devs.First(), modelPath);
                }
            }
            Interlocked.Increment(ref s.cnt);
            return s;
        }

        public static void Free(EdgeTPUSemanticSegmentation s)
        {
            if (s != null)
            {
                Interlocked.Decrement(ref s.cnt);
                if (s.cnt == 0)
                    loaded.Remove(s.modelPath);
            }
        }


        private static Dictionary<string, EdgeTPUSemanticSegmentation> loaded = new Dictionary<string, EdgeTPUSemanticSegmentation>();

        protected SemanticSegmentationInfo info;
        private bool disposedValue;
        private string modelPath;
        private Device device;
        private int cnt;




        private EdgeTPUSemanticSegmentation(Device dev, string modelPath)
        {
            lock(lck)
            {
                info = new SemanticSegmentationInfo();
                if (LoadSemanticSegmentationModel(ref info, dev, modelPath) != 1)
                    throw new Exception("Model not loaded.");
                this.modelPath = modelPath;
                this.device = dev;
            }
        }
        public void Process(Image<BGR32> srcImg, Image<Gray> destImg)
        {
            lock(lck)
                SemanticSegmentation(ref info, srcImg.Width, srcImg.Height, srcImg.Data, destImg.Data);
        }

        public Size Size(int width, int height)
        {
            return new Size(info.OutputSize.Width, info.OutputSize.Height);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                lock (staticLck)
                {
                    FreeSemanticSegmentationModel(ref info);
                    loaded.Remove(modelPath);
                }
                disposedValue = true;
            }
        }

         ~EdgeTPUSemanticSegmentation()
         {
             // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
             Dispose(disposing: false);
         }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
