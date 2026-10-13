using System;
using System.Threading;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Vision;

namespace ARBot.Common.Tests.Devices
{
    /// <summary>
    /// Testy poolu snimku (krok 4, doc/plan-camera-vision-refactor.md):
    /// <see cref="CameraFramePool"/> (per-consumer kopie s release) a
    /// <see cref="CaptureFramePool"/> (triple-buffer capture kamery). Overuje hlubokou kopii
    /// image dat, recyklaci bufferu (bez alokace), predani gridu referenci, best-effort drop
    /// pri vycerpani a round-robin capture slotu.
    /// </summary>
    public class CameraFramePoolTest
    {
        private static Image<BGR32> Rgb(int w, int h, byte fill)
        {
            var img = new Image<BGR32>(w, h);
            var d = img.Data;
            for (int i = 0; i < d.Length; i++) d[i] = fill;
            return img;
        }

        private static CameraFrame Frame(byte fill, PolarTraversabilityGrid grid = null) => new CameraFrame
        {
            Name = "Cam",
            TimeStamp = new DateTime(2026, 1, 1),
            ImageRGB = Rgb(8, 8, fill),
            ImageDepth = new Image<Gray16>(4, 4),
            Grid = grid,
        };

        [Test]
        public void Acquire_DeepCopiesImageData_GridByReference()
        {
            var pool = new CameraFramePool(2);
            var grid = new PolarTraversabilityGrid { AzimuthCount = 1, ColumnsPerCell = 1 };
            var src = Frame(fill: 7, grid: grid);

            var copy = pool.Acquire(src);

            Assert.That(copy, Is.Not.Null);
            Assert.That(copy.ImageRGB.Data[0], Is.EqualTo(7), "data zkopirovana");
            Assert.That(ReferenceEquals(copy.ImageRGB, src.ImageRGB), Is.False, "vlastni buffer, ne stejny objekt");
            Assert.That(ReferenceEquals(copy.Grid, grid), Is.True, "grid se predava referenci");
            Assert.That(copy.Name, Is.EqualTo("Cam"));
            Assert.That(copy.TimeStamp, Is.EqualTo(src.TimeStamp));

            // Mutace zdroje po Acquire neovlivni kopii (skutecna kopie dat).
            src.ImageRGB.Data[0] = 99;
            Assert.That(copy.ImageRGB.Data[0], Is.EqualTo(7), "kopie je nezavisla na zdroji");
        }

        [Test]
        public void Acquire_Exhausted_ReturnsNull_ThenReleaseFrees()
        {
            var pool = new CameraFramePool(2);
            var a = pool.Acquire(Frame(1));
            var b = pool.Acquire(Frame(2));
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
            Assert.That(pool.InUseCount, Is.EqualTo(2));

            Assert.That(pool.Acquire(Frame(3)), Is.Null, "vycerpano -> best-effort drop");

            Assert.That(pool.Release(a), Is.True);
            Assert.That(pool.InUseCount, Is.EqualTo(1));
            Assert.That(pool.Acquire(Frame(4)), Is.Not.Null, "po uvolneni zase lze");
        }

        [Test]
        public void Acquire_ReusesBuffer_AfterRelease_NoAllocation()
        {
            var pool = new CameraFramePool(1);
            var c1 = pool.Acquire(Frame(1));
            var buf1 = c1.ImageRGB;
            pool.Release(c1);

            var c2 = pool.Acquire(Frame(2));   // stejny slot -> stejny buffer (recyklace)
            Assert.That(ReferenceEquals(c2.ImageRGB, buf1), Is.True, "buffer se recykluje bez alokace");
            Assert.That(c2.ImageRGB.Data[0], Is.EqualTo(2), "ale prekryta novymi daty");
        }

        [Test]
        public void Release_ForeignFrame_IsNoOp()
        {
            var pool = new CameraFramePool(2);
            Assert.That(pool.Release(Frame(1)), Is.False, "cizi snimek se ignoruje");
            Assert.That(pool.Release(null), Is.False);
        }

        [Test]
        public void Capture_Next_RoundRobin_ReusesBuffers_ClearsGrid()
        {
            var pool = new CaptureFramePool(3);
            var f0 = pool.Next(true, 8, 8, true, 4, 4);
            var f1 = pool.Next(true, 8, 8, true, 4, 4);
            var f2 = pool.Next(true, 8, 8, true, 4, 4);

            Assert.That(ReferenceEquals(f0, f1), Is.False);
            Assert.That(ReferenceEquals(f1, f2), Is.False);
            Assert.That(f0.ImageRGB, Is.Not.Null);
            Assert.That((f0.ImageRGB.Width, f0.ImageRGB.Height), Is.EqualTo((8, 8)));
            Assert.That((f0.ImageDepth.Width, f0.ImageDepth.Height), Is.EqualTo((4, 4)));

            var rgb0 = f0.ImageRGB;
            f0.Grid = new PolarTraversabilityGrid();   // simuluj procesor
            var f0b = pool.Next(true, 8, 8, true, 4, 4);   // ctvrty -> zpet na slot 0
            Assert.That(ReferenceEquals(f0b, f0), Is.True, "round-robin se vraci na prvni slot");
            Assert.That(ReferenceEquals(f0b.ImageRGB, rgb0), Is.True, "RGB buffer se recykluje");
            Assert.That(f0b.Grid, Is.Null, "Next vycisti Grid");
        }

        // ---------- verze slotu (seqlock), lp-navigator-zivy-slot-kamery, 13. 10. 2026 ----------

        [Test]
        public void Acquire_RozepsanySlot_SeZahodi_SlotSeVrati()
        {
            var pool = new CameraFramePool(2);
            var src = Frame(1);
            src.CaptureGeneration = 3;   // licha = kamera do slotu prave pise

            Assert.That(pool.Acquire(src), Is.Null, "rozepsany slot se nekopiruje");
            Assert.That(pool.TornDrops, Is.EqualTo(1));
            Assert.That(pool.ExhaustedDrops, Is.EqualTo(0));
            Assert.That(pool.InUseCount, Is.EqualTo(0), "slot zahozene kopie se vraci do poolu");

            src.CaptureGeneration = 4;   // hotovo
            Assert.That(pool.Acquire(src), Is.Not.Null);
        }

        [Test]
        public void Acquire_Vycerpano_CitaSeZvlast()
        {
            var pool = new CameraFramePool(1);
            Assert.That(pool.Acquire(Frame(1)), Is.Not.Null);
            Assert.That(pool.Acquire(Frame(2)), Is.Null);
            Assert.That(pool.ExhaustedDrops, Is.EqualTo(1));
            Assert.That(pool.TornDrops, Is.EqualTo(0));
        }

        [Test]
        public void Capture_NextLicha_PublishSuda_NepublikovanyZustavaLichy()
        {
            var pool = new CaptureFramePool(2);
            var a = pool.Next(true, 8, 8, false, 0, 0);
            int g1 = a.CaptureGeneration;
            Assert.That(g1 & 1, Is.EqualTo(1), "Next = zacina zapis -> licha");
            CaptureFramePool.Publish(a);
            Assert.That(a.CaptureGeneration, Is.EqualTo(g1 + 1), "Publish = hotovo -> suda");

            var b = pool.Next(true, 8, 8, false, 0, 0);   // nepublikovany (snimek preskocen)
            int gb = b.CaptureGeneration;
            var a2 = pool.Next(true, 8, 8, false, 0, 0);
            Assert.That(ReferenceEquals(a2, a), Is.True);
            Assert.That(a2.CaptureGeneration & 1, Is.EqualTo(1));
            Assert.That(a2.CaptureGeneration, Is.Not.EqualTo(g1), "novy zapis = nova hodnota");

            var b2 = pool.Next(true, 8, 8, false, 0, 0);
            Assert.That(ReferenceEquals(b2, b), Is.True);
            Assert.That(b2.CaptureGeneration & 1, Is.EqualTo(1), "nepublikovany slot zustava lichy");
            Assert.That(b2.CaptureGeneration, Is.Not.EqualTo(gb), "... ale s jinou hodnotou");
        }

        [Test]
        public void Capture_VychoziPocetSlotu8()
        {
            Assert.That(new CaptureFramePool().Count, Is.EqualTo(CaptureFramePool.DefaultCount));
            Assert.That(CaptureFramePool.DefaultCount, Is.EqualTo(8));
        }

        /// <summary>
        /// Soubeh: kamera (vlakno) dokola prepisuje sloty a kazdy vyplni JEDNOU hodnotou, ctenar porad
        /// kopiruje tentyz slot. Kopie, ktera projde, nesmi byt nikdy roztrzena (smes dvou zapisu).
        /// </summary>
        [Test]
        public void Acquire_SoubehSKamerou_NikdyNevratiRoztrzenouKopii()
        {
            var capture = new CaptureFramePool(2);
            var slot = capture.Next(true, 512, 512, false, 0, 0);
            Array.Fill(slot.ImageRGB.Data, (byte)0);
            CaptureFramePool.Publish(slot);

            var pool = new CameraFramePool(1);
            using var konec = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            var kamera = Task.Run(() =>
            {
                byte v = 1;
                while (!konec.IsCancellationRequested)
                {
                    var f = capture.Next(true, 512, 512, false, 0, 0);
                    Array.Fill(f.ImageRGB.Data, v++);
                    CaptureFramePool.Publish(f);
                }
            });

            int ok = 0, roztrzene = 0;
            while (!konec.IsCancellationRequested)
            {
                var c = pool.Acquire(slot);
                if (c == null) continue;
                var d = c.ImageRGB.Data;
                byte prvni = d[0];
                for (int i = 1; i < d.Length; i++)
                    if (d[i] != prvni) { roztrzene++; break; }
                ok++;
                pool.Release(c);
            }
            kamera.Wait();

            Assert.That(roztrzene, Is.EqualTo(0), "roztrzena kopie se nesmi vratit");
            Assert.That(ok, Is.GreaterThan(0), "aspon nektere kopie musi projit");
        }

        [Test]
        public void Capture_Next_ReallocatesOnSizeChange()
        {
            var pool = new CaptureFramePool(2);
            var a = pool.Next(true, 8, 8, false, 0, 0);
            var bufA = a.ImageRGB;
            pool.Next(true, 8, 8, false, 0, 0);   // slot 1
            var a2 = pool.Next(true, 16, 16, false, 0, 0);   // zpet slot 0, jiny rozmer
            Assert.That(ReferenceEquals(a2, a), Is.True);
            Assert.That(ReferenceEquals(a2.ImageRGB, bufA), Is.False, "pri zmene rozmeru se realokuje");
            Assert.That((a2.ImageRGB.Width, a2.ImageRGB.Height), Is.EqualTo((16, 16)));
        }
    }
}
