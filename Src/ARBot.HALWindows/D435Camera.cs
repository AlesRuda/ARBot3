using ARBot.Common.Algorithms.ComputeUnit;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.LocalMaps;
using ARBot.HAL;
using Intel.RealSense;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;

namespace HALWindows
{
    /// <summary>
    /// Ovladac hloubkove kamery Intel RealSense D435.
    /// Po vytvoreni (resp. po Init) bezi na pozadi task, ktery cte snimky z pipeline,
    /// vyvolava udalost ImageGrabed a posledni snimek zpristupnuje pres GetLastMeasurement.
    /// </summary>
    public sealed class D435Camera:ICamera, IDisposable
    {
        /// <summary>Vypocetni jednotka pro detekci hran cesty (volitelna).</summary>
        IComputeUnit cu;
        /// <summary>Seriove cislo zarizeni; null = prvni dostupna kamera.</summary>
        string sn;
        /// <summary>Zpetna projekce barev na pravdepodobnostni obraz (volitelna).</summary>
        public IBackProject BackProject { get; set; }

        /// <summary>Bezi prave zpracovavaci task?</summary>
        private bool processingIsRunning = false;
        private Task processingTask;
        CancellationTokenSource ctSource;

        /// <summary>Nastaveni barevneho (RGB) streamu.</summary>
        CameraSettings settingsRGB;
        /// <summary>Nastaveni hloubkoveho streamu.</summary>
        CameraSettings settingsDepth;

        private Pipeline pipeline;
        private PipelineProfile pipelineProfile;

        /// <summary>
        /// Vyvolano z pozadiho tasku pri kazdem nove zachycenem snimku.
        /// </summary>
        public event EventHandler<ImageGrabedEventArgs> ImageGrabed;

        /// <summary>
        /// Otoceni kamery vzuhu nokama, tj. rotace podel z o 180 stupnu
        /// </summary>
        public bool Swap;


        /// <summary>
        /// Prevede timestamp snimku (ms od epochy) na lokalni DateTime.
        /// </summary>
        /// <param name="miliseconds">Cas v milisekundach od 1.1.1970.</param>
        /// <returns>Lokalni cas snimku.</returns>
        public static DateTime CalcTimeStamp(double miliseconds)
        {
            return new DateTime(1970, 1, 1).Add(DateTimeOffset.Now.Offset).AddMilliseconds(miliseconds);

            //new DateTime(1970, 1, 1, 0, 0, 0, 0).ToLocalTime().AddMilliseconds(
        }

        /// <summary>Prvni dostupna kamera, RGB 640x480, bez vypocetni jednotky.</summary>
        public D435Camera():this(null, null, new CameraSettings(640, 480))
        {
        }

        /// <summary>Kamera dle serioveho cisla, RGB 640x480.</summary>
        /// <param name="sn">Seriove cislo zarizeni.</param>
        public D435Camera(string sn) : this(sn, null, new CameraSettings(640, 480))
        {
        }

        /// <summary>Kamera dle serioveho cisla s vypocetni jednotkou, RGB 640x480.</summary>
        /// <param name="sn">Seriove cislo zarizeni.</param>
        /// <param name="cu">Vypocetni jednotka pro detekci hran.</param>
        public D435Camera(string sn, IComputeUnit cu) : this(sn, cu, new CameraSettings(640, 480))
        {
        }

        /// <summary>
        /// Hlavni konstruktor. Nakonfiguruje a spusti kameru pres Init (depth je fixne 480x270).
        /// </summary>
        /// <param name="sn">Seriove cislo zarizeni; null = prvni dostupne.</param>
        /// <param name="cu">Vypocetni jednotka pro detekci hran (volitelna).</param>
        /// <param name="rgb">Nastaveni barevneho streamu.</param>
        public D435Camera(string sn, IComputeUnit cu, CameraSettings rgb)
        {
            this.sn = sn;
            this.cu = cu;

            Init(rgb, new CameraSettings(480, 270));
        }

        /// <summary>Aktualni nastaveni hloubkoveho streamu.</summary>
        public CameraSettings DepthSettings
        {
            get
            {
                return settingsDepth;
            }
        }

        /// <summary>Aktualni nastaveni barevneho (RGB) streamu.</summary>
        public CameraSettings RGBSettings
        {
            get
            {
                return settingsRGB;
            }
        }
        /*
                public bool AWB
                {
                    get
                    {
                        return device.QueryColorAutoWhiteBalance();
                    }
                    set
                    {
                        device.SetColorAutoWhiteBalance(value);
                    }
                }
                */

        /// <summary>
        /// Zkopiruje data hloubkoveho snimku (16 bit) do ciloveho bufferu, pripadne v obracenem poradi (Swap).
        /// </summary>
        /// <param name="f">Zdrojovy hloubkovy frame (bude uvolnen).</param>
        /// <param name="d">Cilovy buffer.</param>
        private void GetDataGray(VideoFrame f, byte[] d)
        {
            if (f == null)
                return;

            using (f)
            {
                if (Swap)
                {
                    NativeComputeUnit.ReverseInt16IntPtr(d, f.Data, f.Width * f.Height);

                    /*
                    Marshal.Copy(f.Data, d, 0, d.Length);
                    byte b;
                    int cnt = f.Stride * f.Height / 2;
                    for (int i = 0, j = f.Stride * f.Height - 2; i < cnt; i += 2, j -= 2)
                    {
                        b = d[i];
                        d[i] = d[j];
                        d[j] = b;

                        b = d[i + 1];
                        d[i + 1] = d[j + 1];
                        d[j + 1] = b;
                    }
                    */
                }
                else
                {
                    NativeComputeUnit.CopyIntPtr(d, f.Data, f.Width * f.Height*2);

//                    Marshal.Copy(f.Data, d, 0, d.Length);
                }
            }
        }

        /// <summary>
        /// Zkopiruje barevny snimek (RGB24) do ciloveho bufferu jako BGR32, pripadne v obracenem poradi (Swap).
        /// </summary>
        /// <param name="f">Zdrojovy barevny frame (bude uvolnen).</param>
        /// <param name="d">Cilovy buffer (BGR32).</param>
        private void GetDataRGB(VideoFrame f, byte[] d)
        {
            if (f == null)
                return;

            using (f)
            {
                if (Swap)
                {
                    NativeComputeUnit.ReverseRGB24ToBGR32IntPtr(d, f.Data, f.Width * f.Height);
                    /*
                Marshal.Copy(f.Data, d, 0, d.Length);
                    byte b;
                    int cnt = f.Stride * f.Height / 2;
                    for (int i = 0, j = f.Stride * f.Height - 3; i < cnt; i += 3, j -= 3)
                    {
                        for (int k = 0; k < 3; k += 3)
                        {
                            b = d[i + k];
                            d[i + k] = d[j + k + 2];
                            d[j + k + 2] = b;

                            b = d[i + k + 1];
                            d[i + k + 1] = d[j + k + 1];
                            d[j + k + 1] = b;

                            b = d[i + k + 2];
                            d[i + k + 2] = d[j + k];
                            d[j + k] = b;
                        }
                    }
                    */
                }
                else
                {
                    NativeComputeUnit.CopyRGB24ToBGR32IntPtr(d, f.Data, f.Width * f.Height);
                    /*
                    Marshal.Copy(f.Data, d, 0, d.Length);
                    byte b;
                    int cnt = f.Stride * f.Height;
                    for (int i = 0; i < cnt; i +=3)
                    {
                        for (int j = 0; j < 3; j += 3)
                        {
                            b = d[i + j];
                            d[i + j] = d[i + j + 2];
                            d[i + j + 2] = b;
                        }
                    }*/
                }
            }
        }

        /// <summary>Priznak, ze je k dispozici novy (jeste neodebrany) snimek.</summary>
        bool imageGrabed;

        /// <summary>
        /// Pocka na dalsi snimek z pipeline, zpracuje ho (RGB, hloubka, volitelne backprojection/hrany)
        /// a vrati jako novy CameraFrame s vlastnimi buffery.
        /// </summary>
        protected CameraFrame GetMeasurement()
        {
            Image<BGR32> imageRGB = null;
            Image<BGR32> resizedColorImage = null;
            Image<Gray16> imageDepth = null;
            Image<Gray> probabilityImage = null;
            List<PathEdge> edges = null;


            if (settingsRGB != null)
            {
                imageRGB = new Image<BGR32>(settingsRGB.Width, settingsRGB.Height);
                if (BackProject != null)
                {
                    var size = BackProject.Size(settingsRGB.Width, settingsRGB.Height);
                    if (size.Width != settingsRGB.Width || size.Height != settingsRGB.Height)
                        resizedColorImage = new Image<BGR32>(size.Width, size.Height);
                    probabilityImage = new Image<Gray>(size.Width, size.Height);
                }
            }
            if (settingsDepth != null)
                imageDepth = new Image<Gray16>(settingsDepth.Width, settingsDepth.Height);

            using (var frames = pipeline.WaitForFrames())
            {
                var ts = TimeBase.Now;
                var colorFrame = frames.ColorFrame;
                var depthFrame = frames.DepthFrame;

                var RGBTimeStamp = CalcTimeStamp(colorFrame.Timestamp);
                var DepthTimeStamp = CalcTimeStamp(depthFrame.Timestamp);
                if (imageDepth != null)
                    GetDataGray(depthFrame, imageDepth.Data);
                if (imageRGB != null)
                {
                    GetDataRGB(colorFrame, imageRGB.Data);

                    if (probabilityImage != null)
                    {
                        if (resizedColorImage != null)
                        {
                            resizedColorImage.Resize(imageRGB);
                            BackProject.Process(resizedColorImage, probabilityImage);
                        }
                        else
                            BackProject.Process(imageRGB, probabilityImage);

                        if (cu != null)
                        {
                            edges = cu.PathEdges(probabilityImage, (double)imageRGB.Width / (double)probabilityImage.Width, (double)imageRGB.Height / (double)probabilityImage.Height);
                        }
                    }
                }

                return new CameraFrame() { ImageRGB = imageRGB, ImageDepth = imageDepth, ImageProbability = probabilityImage, TimeStamp = ts, RGBTimeStamp = RGBTimeStamp, DepthTimeStamp = DepthTimeStamp };
            }
        }

        /// <summary>Posledni zachyceny snimek (sdileny mezi pozadim taskem a GetLastMeasurement).</summary>
        CameraFrame lastFrame;

        /// <summary>
        /// Spusti zpracovani snimku na pozadi (volano z Init po (re)konfiguraci pipeline).
        /// </summary>
        private void Start()
        {
            if (!processingIsRunning)
            {
                ctSource = new CancellationTokenSource();
                processingTask = new Task(Process, ctSource.Token);
                processingIsRunning = true;
                processingTask.Start();
            }
        }

        /// <summary>
        /// Pozadi smycka: na kazdy prichozi snimek ho zapamatuje, nastavi priznak a vyvola ImageGrabed.
        /// Bezi az do zruseni tokenu (StopProcessing/Dispose).
        /// </summary>
        private void Process()
        {
            try
            {
                while (!ctSource.IsCancellationRequested)
                {
                    var frame = GetMeasurement();

                    lock (this)
                    {
                        lastFrame = frame;
                        imageGrabed = true;
                    }

                    ImageGrabed?.Invoke(this, new ImageGrabedEventArgs() { Frames = new List<CameraFrame>() { frame } });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
            finally
            {
                processingIsRunning = false;
            }
        }


        /// <summary>
        /// Vraci posledni zachyceny snimek. Opakovane volani bez prichodu noveho snimku vraci null.
        /// </summary>
        public CameraFrame GetLastMeasurement()
        {
            lock (this)
            {
                if (imageGrabed)
                {
                    imageGrabed = false;
                    return lastFrame;
                }
                return null;
            }
        }

        /// <summary>
        /// Zastavi zpracovani na pozadi a pocka na dobehnuti tasku.
        /// </summary>
        private void StopProcessing()
        {
            if (processingIsRunning)
            {
                ctSource?.Cancel();
                try { processingTask?.Wait(); }
                catch (Exception ex) { Debug.WriteLine(ex.ToString()); }
                processingIsRunning = false;
            }
        }

        /// <summary>
        /// (Re)konfiguruje kameru dle zadanych rozliseni a (znovu)spusti pipeline.
        /// Lze volat opakovane za behu - bezici zpracovani se pred rekonfiguraci zastavi a po ni obnovi.
        /// </summary>
        public bool Init(CameraSettings rgbSettings, CameraSettings depthSettings)
        {
            if (processingIsRunning)
                StopProcessing();

            settingsRGB = rgbSettings;
            settingsDepth = depthSettings;

            var cfg = new Config();
            if (sn != null)
                cfg.EnableDevice(sn);
            if (settingsDepth != null)
                cfg.EnableStream(Stream.Depth, settingsDepth.Width, settingsDepth.Height, Format.Z16, 30);
            if (settingsRGB != null)
                cfg.EnableStream(Stream.Color, settingsRGB.Width, settingsRGB.Height, Format.Rgb8, 30);
//            cfg.EnableStream(Stream.Infrared);

            if (pipeline == null)
                pipeline = new Pipeline();
            else
                pipeline.Stop();

            pipelineProfile = pipeline.Start(cfg);

            Start();

            return true;
        }
/*
        public ICameraProjection<Image<BGR>> CreateColorProjector(ILocalMap lm, BackProject bp)
        {
            return new ColorProjector(Projection, lm, bp);
        }
        */
        bool disposed;

        /// <summary>
        /// Zastavi zpracovani na pozadi a uvolni pipeline (nativni prostredky kamery). Idempotentni.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            StopProcessing();       // zrusi token a pocka na dobehnuti tasku
            ctSource?.Dispose();
            pipeline?.Dispose();    // uvolni nativni prostredky kamery
        }

        /// <summary>
        /// Prevede RealSense vnitrni parametry kamery (Intrinsics) na vlastni ARBot Intrinsics
        /// vcetne mapovani modelu zkresleni.
        /// </summary>
        private ARBot.Common.Coordinates.Intrinsics Simplify(Intel.RealSense.Intrinsics i)
        {
            var ii = new ARBot.Common.Coordinates.Intrinsics();
            ii.Coeffs = i.coeffs;
            ii.Fx = i.fx;
            ii.Fy = i.fy;
            ii.Height = i.height;
            ii.Width = i.width;
            ii.PPx = i.ppx;
            ii.PPy = i.ppy;
            if (i.coeffs.All(f => f == 0))
                ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.None;
            else
            {
                switch (i.model)
                {
                    case Distortion.BrownConrady:
                        ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.BrownConrady;
                        break;
                    case Distortion.Ftheta:
                        ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.Ftheta;
                        break;
                    case Distortion.InverseBrownConrady:
                        ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.InverseBrownConrady;
                        break;
                    case Distortion.ModifiedBrownConrady:
                        ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.ModifiedBrownConrady;
                        break;
                    default:
                        ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.None;
                        break;
                }
            }
            return ii;
        }

        /// <summary>
        /// Sestavi projekci kamery z RealSense intrinsics/extrinsics. Pokud je zadana hloubkova
        /// intrinsika, vrati D435CameraProjection (3D), jinak zakladni CameraProjection.
        /// Zohlednuje prevraceni obrazu (Swap).
        /// </summary>
        /// <param name="name">Popis pro ladici vypis.</param>
        /// <param name="colorIntrin">Vnitrni parametry barevne kamery.</param>
        /// <param name="depthIntrin">Vnitrni parametry hloubkove kamery (null = jen barevna projekce).</param>
        /// <param name="color2Depth">Transformace z barevne do hloubkove kamery.</param>
        /// <param name="depth2Color">Transformace z hloubkove do barevne kamery.</param>
        private CameraProjection CreateProjector(string name,
            Intel.RealSense.Intrinsics? colorIntrin,
            Intel.RealSense.Intrinsics? depthIntrin,
            Intel.RealSense.Extrinsics? color2Depth,
            Intel.RealSense.Extrinsics? depth2Color
            )
        {
            Intel.RealSense.Intrinsics? i = depthIntrin?? colorIntrin;

            var i1 = Simplify(i.Value);
            Debug.WriteLine(name+": " + i1.ToString());
            var ii = i1.Inverse();
            if (Swap)
            {
                i1.PPx = i1.Width - i1.PPx;
                i1.PPy = i1.Height - i1.PPy;

                ii.PPx = ii.Width - ii.PPx;
                ii.PPy = ii.Height - ii.PPy;
            }
            if(depthIntrin == null)
                return new CameraProjection(i1, ii, Extrinsic2Transform(color2Depth.Value), Extrinsic2Transform(depth2Color.Value));
            else
                return new D435CameraProjection(i1, ii, colorIntrin.Value, depthIntrin.Value, color2Depth.Value, depth2Color.Value);
        }

        /// <summary>
        /// Prevede RealSense extrinsics (rotace 3x3 + translace) na transformacni matici 4x4.
        /// </summary>
        Matrix4x4 Extrinsic2Transform(Intel.RealSense.Extrinsics e)
        {
            return new Matrix4x4(e.rotation[0], e.rotation[1], e.rotation[2], 0, e.rotation[3], e.rotation[4], e.rotation[5], 0, e.rotation[6], e.rotation[7], e.rotation[8], 0, e.translation[0], e.translation[1], e.translation[2], 1);
        }

        /// <summary>
        /// Vytvori projekci barevne kamery do roviny po ktere jede robot (bez hloubky).
        /// </summary>
        public ICameraProjection CreateProjector()
        {
            var c = pipelineProfile.GetStream<VideoStreamProfile>(Stream.Color);
            var d = pipelineProfile.GetStream<VideoStreamProfile>(Stream.Depth);
            return CreateProjector("Color Intrinsics", c.GetIntrinsics(), null, c.GetExtrinsicsTo(d), d.GetExtrinsicsTo(c));
        }

        /// <summary>
        /// Vytvori hloubkovou projekci (3D rekonstrukce bodu z hloubkove mapy).
        /// </summary>
        public IDepthCameraProjection CreateDepthProjector()
        {
            var c = pipelineProfile.GetStream<VideoStreamProfile>(Stream.Color);
            var d = pipelineProfile.GetStream<VideoStreamProfile>(Stream.Depth);
            return CreateProjector("Depth Intrinsics", c.GetIntrinsics(), d.GetIntrinsics(), c.GetExtrinsicsTo(d), d.GetExtrinsicsTo(c));
        }
    }
}
