using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Vision;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Profil sceny pred robotem: graf z(vzdalenost) v jednom azimutu polarniho gridu - surove body
    /// z hloubky a pres ne bunky gridu s vysvetlenim klasifikace (<see cref="SceneProfile"/>).
    /// Nastroj na ladeni detekce terenu; bezi v Run i View (prijima <see cref="CameraFrame"/> ze
    /// <see cref="ARBot.Robot.ARBotRuntime.Stream"/>). Kresli <see cref="ARBot.Views.Controls.SceneProfileControl"/>.
    ///
    /// <para><b>Hloubka se kopiruje</b> - buffery ramce jsou z poolu (<see cref="CameraFramePool"/>)
    /// a nesmi se drzet za hranici <see cref="Post"/>. Aby kopie nezatezovala vlakno producenta,
    /// dela se nejvys <see cref="MaxCopiesPerSecond"/>× za sekundu casu ZAZNAMU na kameru. Kopie je
    /// zaroven to, co umoznuje <see cref="IsFrozen"/>: listovat azimuty nad jednim snimkem.</para>
    /// </summary>
    public partial class SceneProfileDocument : DocumentBase, IMessageSink, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.SceneProfileDocumentView);

        private const int MaxCopiesPerSecond = 10;

        /// <summary>Snimek jedne kamery drzeny dokumentem (vlastni kopie hloubky).</summary>
        private sealed class Snapshot
        {
            public DateTime TimeStamp;
            public Image<Gray16> Depth;
            public PolarTraversabilityGrid Grid;
            public CameraProjectionInfo Info;
        }

        private readonly List<IDisposable> feeds = new List<IDisposable>();

        // Backpressure (viz Views/README.md): nejnovejsi snimek per kamera, koalescovany flush na UI.
        private readonly object gate = new object();
        private readonly Dictionary<string, Snapshot> pending = new Dictionary<string, Snapshot>();
        private readonly Dictionary<string, DateTime> lastCopy = new Dictionary<string, DateTime>();
        private volatile bool updateQueued;
        private volatile bool frozen;

        // Jen UI vlakno.
        private readonly Dictionary<string, Snapshot> current = new Dictionary<string, Snapshot>();
        // Projekce per kamera: CreateProjection stavi tabulky pres cely obraz (MB), proto cache.
        private readonly Dictionary<string, (CameraProjectionInfo info, CameraProjection proj)> projections
            = new Dictionary<string, (CameraProjectionInfo, CameraProjection)>();
        private readonly PolarGridConfig cfg = new PolarGridConfig();   // runtime pouziva vychozi prahy

        /// <summary>Kamery, ktere uz poslaly snimek s hloubkou.</summary>
        public ObservableCollection<string> Cameras { get; } = new ObservableCollection<string>();

        [ObservableProperty] private string selectedCamera;
        [ObservableProperty] private int azimuth = -1;         // -1 = jeste nevybran -> stred
        [ObservableProperty] private int azimuthMax;
        [ObservableProperty] private bool isFrozen;
        [ObservableProperty] private bool equalScale;
        [ObservableProperty] private SceneProfile profile;
        [ObservableProperty] private string info = "Čekám na snímek kamery s hloubkou…";

        /// <summary>Konstruktor pro design-time i runtime (bez vedlejsich efektu).</summary>
        public SceneProfileDocument()
        {
            Id = "SceneProfile";
            Title = "Profil scény";
        }

        /// <summary>Pripoji zdroj/e zprav; dokument je pri zavreni zastavi (Dispose).</summary>
        public void AttachFeed(params IDisposable[] disposables)
        {
            if (disposables != null)
                feeds.AddRange(disposables);
        }

        // --- IMessageSink (bezi na vlakne producenta - musi byt neblokujici) ---
        public void Post(Message msg)
        {
            if (frozen) return;
            if (msg is not CameraFrame f || f.ImageDepth == null) return;

            string name = f.Name ?? string.Empty;
            lock (gate)
            {
                // Skrceni podle casu snimku (ne hodin), takze plati i pri zrychlenem prehravani.
                // Skok zpet (seek) skrceni zrusi.
                if (lastCopy.TryGetValue(name, out var last) && f.TimeStamp >= last
                    && (f.TimeStamp - last).TotalSeconds < 1.0 / MaxCopiesPerSecond)
                    return;
                lastCopy[name] = f.TimeStamp;
            }

            var snap = new Snapshot
            {
                TimeStamp = f.TimeStamp,
                Depth = f.ImageDepth.Clone(),
                Grid = f.Grid,              // grid je per snimek neměnný - staci reference
                Info = f.Projection,        // popis projekce taky (novy objekt per snimek/zaznam)
            };

            lock (gate)
                pending[name] = snap;       // nejnovejsi vyhrava

            if (updateQueued) return;
            updateQueued = true;
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }

        private void Flush()
        {
            updateQueued = false;
            lock (gate)
            {
                if (pending.Count == 0) return;
                foreach (var kv in pending)
                    current[kv.Key] = kv.Value;
                pending.Clear();
            }

            foreach (var name in current.Keys)
                if (!Cameras.Contains(name))
                    InsertSorted(name);
            if (SelectedCamera == null && Cameras.Count > 0)
                SelectedCamera = Cameras[0];      // spusti Recompute pres OnSelectedCameraChanged
            else
                Recompute();
        }

        private void InsertSorted(string name)
        {
            int i = 0;
            while (i < Cameras.Count && string.CompareOrdinal(Cameras[i], name) < 0) i++;
            Cameras.Insert(i, name);
        }

        partial void OnSelectedCameraChanged(string value) => Recompute();
        partial void OnAzimuthChanged(int value) => Recompute();
        partial void OnIsFrozenChanged(bool value)
        {
            frozen = value;
            if (!value)
                lock (gate) lastCopy.Clear();
        }

        /// <summary>Prepocita profil vybrane kamery a azimutu z drzeneho snimku (UI vlakno).</summary>
        private void Recompute()
        {
            if (SelectedCamera == null || !current.TryGetValue(SelectedCamera, out var s))
                return;

            if (s.Info == null)
            {
                Profile = null;
                Info = $"{SelectedCamera}: snímek nenese popis projekce (záznam starší než CameraFrame v4) - body nejde spočítat.";
                return;
            }

            CameraProjection proj;
            try { proj = ProjectionFor(SelectedCamera, s.Info); }
            catch (Exception ex)
            {
                Profile = null;
                Info = $"{SelectedCamera}: projekci nejde postavit: {ex.Message}";
                return;
            }

            int azCount = s.Grid?.AzimuthCount > 0 ? s.Grid.AzimuthCount : s.Depth.Width / cfg.ColumnsPerCell;
            AzimuthMax = Math.Max(0, azCount - 1);
            if (Azimuth < 0 || Azimuth > AzimuthMax)
            {
                Azimuth = Math.Clamp(Azimuth < 0 ? azCount / 2 : Azimuth, 0, AzimuthMax);
                return;   // OnAzimuthChanged zavola Recompute znovu
            }

            var p = SceneProfile.Extract(s.Depth, proj, s.Grid, Azimuth, cfg);
            Profile = p;

            int inGrid = 0;
            foreach (var q in p.Points) if (q.Radial >= 0) inGrid++;
            string mismatch = p.Cells == null ? "  · snímek bez gridu"
                : p.Mismatches > 0 ? $"  · ⚠ {p.Mismatches} buněk: přepočet dává jinou třídu než grid (jiné prahy při záznamu?)"
                : string.Empty;
            Info = string.Format(CultureInfo.InvariantCulture,
                "{0}  {1:HH:mm:ss.fff}  ·  azimut {2}/{3}  (sloupce {4}–{5})  ·  bodů {6}, v gridu {7}{8}",
                SelectedCamera, s.TimeStamp, Azimuth, AzimuthMax, p.ColumnFrom, p.ColumnTo - 1,
                p.Points.Length, inGrid, mismatch);
        }

        private CameraProjection ProjectionFor(string camera, CameraProjectionInfo info)
        {
            if (projections.TryGetValue(camera, out var cached) && SameGeometry(cached.info, info))
                return cached.proj;
            var proj = info.CreateProjection();
            projections[camera] = (info, proj);
            return proj;
        }

        // Popis projekce je per snimek novy objekt; stavet tabulky znovu jen pri zmene geometrie.
        private static bool SameGeometry(CameraProjectionInfo a, CameraProjectionInfo b)
            => a.Transformation == b.Transformation && a.From == b.From && a.To == b.To
               && SameIntrinsics(a.Intrinsics, b.Intrinsics) && SameIntrinsics(a.InverseIntrinsics, b.InverseIntrinsics);

        private static bool SameIntrinsics(Intrinsics a, Intrinsics b)
            => a == null ? b == null
             : b != null && a.Width == b.Width && a.Height == b.Height && a.Fx == b.Fx && a.Fy == b.Fy
               && a.PPx == b.PPx && a.PPy == b.PPy && a.Model == b.Model;

        public override bool OnClose()
        {
            Dispose();
            return base.OnClose();
        }

        public void Dispose()
        {
            foreach (var d in feeds)
            {
                try { d.Dispose(); } catch { }
            }
            feeds.Clear();
        }
    }
}
