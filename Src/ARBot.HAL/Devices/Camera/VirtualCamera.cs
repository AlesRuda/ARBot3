using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Vision;
using ARBot.Common.Vision.Synthetic;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// Virtualni kamera - nahrada D435, ktera misto snimani rendruje scenu z OsmNav mapy
    /// a aktualni pozy robota (viz doc/virtual-hw.md).
    /// </summary>
    public sealed class VirtualCamera : SensorBase<CameraFrame>, ICamera
    {
        private readonly string cameraName;
        private readonly SyntheticFrameRenderer renderer;
        private readonly Func<DateTime, RobotState> poseAt;
        private readonly Matrix4x4 mount;
        private readonly VirtualCameraOptions options;

        /// <summary>Recyklovane buffery snimku (stejny vzor jako <c>D435Camera</c>).</summary>
        private readonly CaptureFramePool capturePool = new CaptureFramePool(3);

        private CameraSettings settingsRGB;
        private CameraSettings settingsDepth;

        /// <summary>Projekce se stavi lazy - konstruktor <see cref="CameraProjection"/> plni tabulky pres cele W*H.</summary>
        private CameraProjection colorProjection;
        private CameraProjection depthProjection;

        /// <summary>Poradi snimku - vstup do sumu, aby byl kazdy snimek jiny, ale reprodukovatelny.</summary>
        private int frameIndex;

        /// <summary>Cas dalsiho snimku - drzi zadany takt.</summary>
        private DateTime nextFrameAt = DateTime.MinValue;

        /// <inheritdoc/>
        public ICameraFrameProcessor FrameProcessor { get; set; }

        /// <inheritdoc/>
        public override string Name => cameraName;

        /// <summary>Virtualni kamera nema jak selhat - nema HW.</summary>
        public override bool IsError => base.IsError;

        /// <inheritdoc/>
        public CameraSettings RGBSettings => settingsRGB;

        /// <inheritdoc/>
        public CameraSettings DepthSettings => settingsDepth;

        /// <summary>
        /// Zalozi virtualni kameru a rovnou spusti snimani (stejne jako <c>D435Camera</c>).
        /// </summary>
        /// <param name="name">Jmeno kamery (napr. "Left"/"Right") - klic pro resolvery projekci.</param>
        /// <param name="scene">Geometrie sceny (vozovka v lokalni ENU rovine).</param>
        /// <param name="sceneOptions">Parametry vzhledu a sumu.</param>
        /// <param name="mountTransform">Montazni transformace kamery v ramci robota (z <c>Profile</c>).</param>
        /// <param name="poseAt">Zdroj pozy robota k danemu casu (v aplikaci fuze, v testech konstanta).</param>
        /// <param name="options">Rozliseni, zorne pole a takt; null = vychozi (jako D435).</param>
        public VirtualCamera(string name, RoadScene scene, SyntheticSceneOptions sceneOptions,
                             Matrix4x4 mountTransform, Func<DateTime, RobotState> poseAt,
                             VirtualCameraOptions options = null)
        {
            cameraName = name ?? throw new ArgumentNullException(nameof(name));
            this.poseAt = poseAt ?? throw new ArgumentNullException(nameof(poseAt));
            this.options = options ?? new VirtualCameraOptions();
            mount = mountTransform;

            renderer = new SyntheticFrameRenderer(scene, sceneOptions ?? new SyntheticSceneOptions());

            Init(this.options.RGB, this.options.Depth);
        }

        /// <summary>
        /// (Re)konfiguruje rozliseni streamu a (znovu)spusti snimani.
        /// </summary>
        public bool Init(CameraSettings rgbSettings, CameraSettings depthSettings)
        {
            if (IsRunning)
                Stop();   // pockej na dobehnuti smycky - pote lze bezpecne menit nastaveni

            settingsRGB = rgbSettings;
            settingsDepth = depthSettings;

            // Projekce zavisi na rozliseni - pri zmene se musi postavit znovu.
            colorProjection = null;
            depthProjection = null;
            nextFrameAt = DateTime.MinValue;

            Start();
            return true;
        }

        /// <summary>
        /// Vyrendruje dalsi snimek v zadanem taktu. Volano ze <see cref="SensorBase{TState}.Process"/>.
        /// </summary>
        protected override CameraFrame GetMeasurement()
        {
            WaitForNextTick();

            var frame = capturePool.Next(
                settingsRGB != null, settingsRGB?.Width ?? 0, settingsRGB?.Height ?? 0,
                settingsDepth != null, settingsDepth?.Width ?? 0, settingsDepth?.Height ?? 0);

            var ts = TimeBase.Now;
            var pose = poseAt(ts);
            if (pose == null)
                return null;   // poza jeste neni k dispozici (napr. fuze bez mereni) - snimek preskocit

            int index = frameIndex++;

            if (frame.ImageDepth != null)
                renderer.RenderDepth(DepthProjection, pose, index, frame.ImageDepth);
            if (frame.ImageRGB != null)
                renderer.RenderColor(ColorProjection, pose, index, frame.ImageRGB);

            frame.Name = Name;
            frame.TimeStamp = ts;
            frame.RGBTimeStamp = ts;
            frame.DepthTimeStamp = ts;

            // Stejny synchronni dopocet jako u realne kamery - pipeline za kamerou se nesmi lisit.
            FrameProcessor?.Process(frame);

            return frame;
        }

        /// <summary>Pocka do casu dalsiho snimku (drzi <see cref="VirtualCameraOptions.FrameRateHz"/>).</summary>
        private void WaitForNextTick()
        {
            int periodMs = Math.Max(1, 1000 / Math.Max(1, options.FrameRateHz));
            var now = DateTime.UtcNow;

            if (nextFrameAt == DateTime.MinValue)
            {
                nextFrameAt = now;
                return;
            }

            nextFrameAt = nextFrameAt.AddMilliseconds(periodMs);
            var wait = nextFrameAt - now;
            if (wait > TimeSpan.Zero)
                Thread.Sleep(wait);
            else if (wait < TimeSpan.FromMilliseconds(-5 * periodMs))
                nextFrameAt = now;   // vyrazne zpozdeni: nedohanet davku snimku, jen se srovnat
        }

        /// <summary>Projekce barevneho streamu (stavi se pri prvnim pouziti).</summary>
        private CameraProjection ColorProjection
            => colorProjection ??= BuildProjection(settingsRGB, options.RgbHorizontalFovDeg);

        /// <summary>Projekce hloubkoveho streamu (stavi se pri prvnim pouziti).</summary>
        private CameraProjection DepthProjection
            => depthProjection ??= BuildProjection(settingsDepth, options.DepthHorizontalFovDeg);

        /// <summary>
        /// Sestavi projekci pro dane rozliseni. Tatáz instance se pouziva k renderovani i vraci
        /// ven z <see cref="CreateProjector"/>/<see cref="CreateDepthProjector"/>, takze vize
        /// dostane presne tu projekci, ve ktere byl obraz vykreslen.
        /// </summary>
        private CameraProjection BuildProjection(CameraSettings settings, double horizontalFovDeg)
        {
            if (settings == null)
                throw new InvalidOperationException($"{Name}: stream neni nakonfigurovany.");

            var intrinsics = SyntheticIntrinsics.Pinhole(settings.Width, settings.Height, horizontalFovDeg);
            // Bez zkresleni je inverzni intrinsika totozna; from/to jsou identity (jeden stream,
            // zadny prevod mezi barevnou a hloubkovou kamerou).
            var projection = new CameraProjection(intrinsics, intrinsics, Matrix4x4.Identity, Matrix4x4.Identity);
            projection.SetOrientation(mount);
            return projection;
        }

        /// <inheritdoc/>
        public ICameraProjection CreateProjector() => ColorProjection;

        /// <inheritdoc/>
        public IDepthCameraProjection CreateDepthProjector() => DepthProjection;
    }
}
