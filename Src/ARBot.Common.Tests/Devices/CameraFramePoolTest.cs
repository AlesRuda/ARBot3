using System;
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
