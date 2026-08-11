using System;
using System.Collections.Generic;
using System.IO;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Tests.Runtime;   // TestHelpers, DelegateTarget
using ARBot.Common.Vision;

namespace ARBot.Common.Tests.Devices
{
    /// <summary>
    /// Round-trip (de)serializace <see cref="CameraFrame"/> přes záznam a replay
    /// (<see cref="RecordingTarget"/> → <see cref="FileMessageSource"/> s katalogem). Ověřuje verzní
    /// rámování i komprese vrstev: RGB = Jpeg (ztrátové), Probability = Png a Depth = Deflate
    /// (bezztrátové) — viz <see cref="CameraFrame.ToData"/>.
    /// </summary>
    public class CameraFrameSerializationTest
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static Image<T> MakeImage<T>(int w, int h, int seed) where T : IPixel, new()
        {
            var img = new Image<T>(w, h);
            var d = img.Data;
            for (int i = 0; i < d.Length; i++)
                d[i] = (byte)((i + seed) & 0xFF);
            return img;
        }

        private static Image<BGR32> SolidBgr32(int w, int h, byte r, byte g, byte b)
        {
            var img = new Image<BGR32>(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = img[x, y];
                    p.R = r; p.G = g; p.B = b;
                }
            return img;
        }

        [Test]
        public void CameraFrame_RoundTrips_ViaRecordReplay()
        {
            var frame = new CameraFrame
            {
                Name = "Left 740112071040",
                FrameNum = 7,
                TimeStamp = T0,
                RGBTimeStamp = T0.AddMilliseconds(1),
                DepthTimeStamp = T0.AddMilliseconds(2),
                ImageRGB = SolidBgr32(16, 16, r: 200, g: 120, b: 60),   // None (bezztrátové)
                ImageProbability = MakeImage<Gray>(8, 6, 3),            // None (bezztrátové)
                ImageDepth = MakeImage<Gray16>(4, 4, 100),             // None (bezztrátové)
            };

            // záznam
            byte[] dataBytes;
            using (var ms = new MemoryStream())
            {
                var rec = new RecordingTarget(ms, null, TestHelpers.Enc);
                rec.Start();
                rec.Post(frame);
                rec.Stop();
                Assert.That(rec.Count, Is.EqualTo(1));
                dataBytes = ms.ToArray();
            }

            // replay
            var catalog = MessageCatalog.CommonDefaults().Register(new CameraFrame());
            var got = new List<CameraFrame>();
            var sink = new DelegateTarget(m => { if (m is CameraFrame c) got.Add(c); });
            sink.Start();
            using (var ms = new MemoryStream(dataBytes))
            {
                var src = new FileMessageSource(ms, TestHelpers.Enc, catalog);
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            Assert.That(got.Count, Is.EqualTo(1));
            var r = got[0];

            Assert.That(r.Verze, Is.EqualTo(CameraFrame.FormatVersion));
            Assert.That(r.Name, Is.EqualTo(frame.Name));
            Assert.That(r.FrameNum, Is.EqualTo(frame.FrameNum));
            Assert.That(r.TimeStamp, Is.EqualTo(frame.TimeStamp));
            Assert.That(r.RGBTimeStamp, Is.EqualTo(frame.RGBTimeStamp));
            Assert.That(r.DepthTimeStamp, Is.EqualTo(frame.DepthTimeStamp));

            // Vsechny vrstvy None (bezztratove) -> presna shoda dat.
            Assert.That(r.ImageRGB, Is.Not.Null);
            Assert.That((r.ImageRGB.Width, r.ImageRGB.Height), Is.EqualTo((16, 16)));
            Assert.That(r.ImageRGB.Data, Is.EqualTo(frame.ImageRGB.Data));

            Assert.That(r.ImageProbability, Is.Not.Null);
            Assert.That((r.ImageProbability.Width, r.ImageProbability.Height), Is.EqualTo((8, 6)));
            Assert.That(r.ImageProbability.Data, Is.EqualTo(frame.ImageProbability.Data));

            Assert.That(r.ImageDepth, Is.Not.Null);
            Assert.That((r.ImageDepth.Width, r.ImageDepth.Height), Is.EqualTo((4, 4)));
            Assert.That(r.ImageDepth.Data, Is.EqualTo(frame.ImageDepth.Data));
        }

        [Test]
        public void CameraFrame_NullLayer_RoundTripsToNull()
        {
            var frame = new CameraFrame
            {
                Name = "Left",
                TimeStamp = T0,
                ImageRGB = null,
                ImageProbability = MakeImage<Gray>(4, 4, 1),
                ImageDepth = null,
            };

            using var ms = new MemoryStream();
            var rec = new RecordingTarget(ms, null, TestHelpers.Enc);
            rec.Start(); rec.Post(frame); rec.Stop();

            var catalog = MessageCatalog.CommonDefaults().Register(new CameraFrame());
            CameraFrame r = null;
            var sink = new DelegateTarget(m => { if (m is CameraFrame c) r = c; });
            sink.Start();
            using (var rms = new MemoryStream(ms.ToArray()))
            {
                var src = new FileMessageSource(rms, TestHelpers.Enc, catalog);
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            Assert.That(r, Is.Not.Null);
            Assert.That(r.ImageRGB, Is.Null);
            Assert.That(r.ImageDepth, Is.Null);
            Assert.That(r.ImageProbability, Is.Not.Null);
        }

        [Test]
        public void CameraFrame_UnknownVersion_Throws()
        {
            // FromData musí odmítnout neznámou (budoucí) verzi místo tichého špatného čtení.
            var f = new CameraFrame { Verze = 999 };
            using var ms = new MemoryStream(new byte[64]);
            using var br = new BinaryReader(ms, TestHelpers.Enc);
            Assert.That(() => f.FromData(br), Throws.TypeOf<NotSupportedException>());
        }

        // --- Verzovani gridu (od FormatVersion 2) a hranic cesty (od FormatVersion 3) ---

        private static PolarTraversabilityGrid MakeGrid()
        {
            // A=2 azimuty, R=2 prstence (RadialEdges.Length = R+1 = 3).
            return new PolarTraversabilityGrid
            {
                AzimuthCount = 2,
                ColumnsPerCell = 16,
                RadialEdges = new[]
                {
                    new RadialEdge(0.30f, 40),
                    new RadialEdge(0.80f, 30),
                    new RadialEdge(1.50f, 20),
                },
                Cells = new[]
                {
                    new PolarCell { Count = 10, MeanX = 0.5f, MeanY = 0.1f, MeanZ = 0.01f, StdZ = 0.02f, MaxZ = 0.05f, EdgeRange = 0.40f, Confidence = 0.70f, Class = TraversabilityClass.Free },
                    new PolarCell { Count = 12, MeanX = 0.9f, MeanY = 0.2f, MeanZ = 0.30f, StdZ = 0.08f, MaxZ = 0.35f, EdgeRange = 0.85f, Confidence = 0.40f, Class = TraversabilityClass.Obstacle },
                    new PolarCell { Count = 0,  MeanX = 0f,   MeanY = 0f,   MeanZ = 0f,    StdZ = 0f,    MaxZ = 0f,    EdgeRange = float.NaN, Confidence = 0f,  Class = TraversabilityClass.Unknown },
                    new PolarCell { Count = 9,  MeanX = 1.1f, MeanY = -0.3f, MeanZ = 0.02f, StdZ = 0.03f, MaxZ = 0.06f, EdgeRange = 1.05f, Confidence = 0.55f, Class = TraversabilityClass.Free },
                },
                ComputeMs = 12.3,   // diagnostika - NESMI se serializovat (po replay = 0)
            };
        }

        [Test]
        public void CameraFrame_WithGrid_RoundTrips()
        {
            var frame = new CameraFrame
            {
                Name = "Left",
                TimeStamp = T0,
                RGBTimeStamp = T0.AddMilliseconds(1),
                DepthTimeStamp = T0.AddMilliseconds(2),
                ImageDepth = MakeImage<Gray16>(4, 4, 100),
                Grid = MakeGrid(),
            };

            using var ms = new MemoryStream();
            var rec = new RecordingTarget(ms, null, TestHelpers.Enc);
            rec.Start(); rec.Post(frame); rec.Stop();

            var catalog = MessageCatalog.CommonDefaults().Register(new CameraFrame());
            CameraFrame r = null;
            var sink = new DelegateTarget(m => { if (m is CameraFrame c) r = c; });
            sink.Start();
            using (var rms = new MemoryStream(ms.ToArray()))
            {
                var src = new FileMessageSource(rms, TestHelpers.Enc, catalog);
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            Assert.That(r, Is.Not.Null);
            Assert.That(r.Verze, Is.EqualTo(CameraFrame.FormatVersion));
            Assert.That(r.Grid, Is.Not.Null, "grid se prenesl");

            var g = MakeGrid();
            Assert.That(r.Grid.AzimuthCount, Is.EqualTo(g.AzimuthCount));
            Assert.That(r.Grid.ColumnsPerCell, Is.EqualTo(g.ColumnsPerCell));
            Assert.That(r.Grid.RadialEdges.Length, Is.EqualTo(g.RadialEdges.Length));
            for (int i = 0; i < g.RadialEdges.Length; i++)
            {
                Assert.That(r.Grid.RadialEdges[i].Range, Is.EqualTo(g.RadialEdges[i].Range), $"Edge.Range[{i}]");
                Assert.That(r.Grid.RadialEdges[i].Row, Is.EqualTo(g.RadialEdges[i].Row), $"Edge.Row[{i}]");
            }
            Assert.That(r.Grid.Cells.Length, Is.EqualTo(g.Cells.Length));
            for (int i = 0; i < g.Cells.Length; i++)
            {
                Assert.That(r.Grid.Cells[i].Count, Is.EqualTo(g.Cells[i].Count), $"Count[{i}]");
                Assert.That(r.Grid.Cells[i].MeanX, Is.EqualTo(g.Cells[i].MeanX), $"MeanX[{i}]");
                Assert.That(r.Grid.Cells[i].MeanY, Is.EqualTo(g.Cells[i].MeanY), $"MeanY[{i}]");
                Assert.That(r.Grid.Cells[i].MeanZ, Is.EqualTo(g.Cells[i].MeanZ), $"MeanZ[{i}]");
                Assert.That(r.Grid.Cells[i].StdZ, Is.EqualTo(g.Cells[i].StdZ), $"StdZ[{i}]");
                Assert.That(r.Grid.Cells[i].MaxZ, Is.EqualTo(g.Cells[i].MaxZ), $"MaxZ[{i}]");
                Assert.That(r.Grid.Cells[i].Confidence, Is.EqualTo(g.Cells[i].Confidence), $"Conf[{i}]");
                Assert.That(r.Grid.Cells[i].Class, Is.EqualTo(g.Cells[i].Class), $"Class[{i}]");
            }
            Assert.That(r.Grid.ComputeMs, Is.EqualTo(0.0), "ComputeMs je diagnostika - neserializuje se");
        }

        [Test]
        public void CameraFrame_NullGridAndEdges_RoundTripsToNull()
        {
            var frame = new CameraFrame { Name = "Left", TimeStamp = T0, Grid = null, PathEdges = null };

            using var ms = new MemoryStream();
            var rec = new RecordingTarget(ms, null, TestHelpers.Enc);
            rec.Start(); rec.Post(frame); rec.Stop();

            var catalog = MessageCatalog.CommonDefaults().Register(new CameraFrame());
            CameraFrame r = null;
            var sink = new DelegateTarget(m => { if (m is CameraFrame c) r = c; });
            sink.Start();
            using (var rms = new MemoryStream(ms.ToArray()))
            {
                var src = new FileMessageSource(rms, TestHelpers.Enc, catalog);
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            Assert.That(r, Is.Not.Null);
            Assert.That(r.Verze, Is.EqualTo(CameraFrame.FormatVersion));
            Assert.That(r.Grid, Is.Null);
            Assert.That(r.PathEdges, Is.Null);
        }

        [Test]
        public void CameraFrame_V3_WithPathEdges_RoundTrips()
        {
            var frame = new CameraFrame
            {
                Name = "Left",
                TimeStamp = T0,
                ImageProbability = MakeImage<Gray>(8, 6, 3),
                PathEdges = new List<PathEdge>
                {
                    new PathEdge { Y = 5, Left = 10, Right = 30 },
                    new PathEdge { Y = 4, Left = null, Right = 28 },
                    new PathEdge { Y = 3, Left = 12, Right = null },
                },
            };

            using var ms = new MemoryStream();
            var rec = new RecordingTarget(ms, null, TestHelpers.Enc);
            rec.Start(); rec.Post(frame); rec.Stop();

            var catalog = MessageCatalog.CommonDefaults().Register(new CameraFrame());
            CameraFrame r = null;
            var sink = new DelegateTarget(m => { if (m is CameraFrame c) r = c; });
            sink.Start();
            using (var rms = new MemoryStream(ms.ToArray()))
            {
                var src = new FileMessageSource(rms, TestHelpers.Enc, catalog);
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            Assert.That(r, Is.Not.Null);
            Assert.That(r.Verze, Is.EqualTo(CameraFrame.FormatVersion));
            Assert.That(r.PathEdges, Is.Not.Null, "hrany se prenesly");
            Assert.That(r.PathEdges.Count, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(r.PathEdges[i].Y, Is.EqualTo(frame.PathEdges[i].Y), $"Y[{i}]");
                Assert.That(r.PathEdges[i].Left, Is.EqualTo(frame.PathEdges[i].Left), $"Left[{i}]");
                Assert.That(r.PathEdges[i].Right, Is.EqualTo(frame.PathEdges[i].Right), $"Right[{i}]");
            }
        }

        [Test]
        public void CameraFrame_V2_ReadsWithoutPathEdges()
        {
            // Zaznam verze 2 (grid, ale jeste bez hranic cesty) se musi precist bez chyby (PathEdges=null).
            var frame = new CameraFrame
            {
                Name = "Left",
                FrameNum = 6,
                TimeStamp = T0,
                RGBTimeStamp = T0.AddMilliseconds(1),
                DepthTimeStamp = T0.AddMilliseconds(2),
                ImageProbability = MakeImage<Gray>(4, 4, 1),
            };

            byte[] v2 = SerializeV2(frame);

            var read = new CameraFrame { Verze = 2 };
            read.FromData(TestHelpers.Enc, v2);

            Assert.That(read.PathEdges, Is.Null, "verze 2 nema hranice cesty");
            Assert.That(read.Grid, Is.Null);
            Assert.That(read.Name, Is.EqualTo("Left"));
            Assert.That(read.FrameNum, Is.EqualTo(6u));
            Assert.That(read.ImageProbability, Is.Not.Null);
            Assert.That(read.ImageProbability.Data, Is.EqualTo(frame.ImageProbability.Data));
        }

        /// <summary>Zapise ramec ve v2 layoutu: meta + name + 3 obrazy + 2 casy + grid (zde bez gridu), BEZ hranic cesty.</summary>
        private static byte[] SerializeV2(CameraFrame f)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, TestHelpers.Enc, leaveOpen: true))
            {
                bw.Write(f.FrameNum);
                bw.Write(f.DropedOutNum);
                bw.Write(f.FrameReceivePeriod.Ticks);
                bw.Write(f.FramePickupPeriod.Ticks);
                bw.Write(f.TimeStamp.Ticks);
                bw.Write(f.Name ?? string.Empty);
                ImageMsg.Write(bw, f.ImageRGB, ImageMsg.Compression.None);
                ImageMsg.Write(bw, f.ImageProbability, ImageMsg.Compression.None);
                ImageMsg.Write(bw, f.ImageDepth, ImageMsg.Compression.None);
                bw.Write(f.RGBTimeStamp.ToBinary());
                bw.Write(f.DepthTimeStamp.ToBinary());
                bw.Write(false);   // grid flag: bez gridu
            }
            return ms.ToArray();
        }

        // --- Projekce kamery (od FormatVersion 4) ---

        [Test]
        public void CameraFrame_Projekce_RoundTrip()
        {
            var frame = new CameraFrame
            {
                Name = "Left",
                TimeStamp = T0,
                ImageProbability = MakeImage<Gray>(4, 4, 1),
                Grid = MakeGrid(),
                Projection = MakeProjectionInfo(),
            };

            var r = RoundTrip(frame);

            Assert.That(r.Verze, Is.EqualTo(CameraFrame.FormatVersion));
            Assert.That(r.Grid, Is.Not.Null);

            Assert.That(r.Projection, Is.Not.Null, "projekce se prenesla");
            Assert.That(r.Projection.Intrinsics.Width, Is.EqualTo(8));
            Assert.That(r.Projection.Intrinsics.Height, Is.EqualTo(6));
            Assert.That(r.Projection.Intrinsics.Fx, Is.EqualTo(5.5f));
            Assert.That(r.Projection.Intrinsics.PPy, Is.EqualTo(3.25f));
            Assert.That(r.Projection.Intrinsics.Model,
                        Is.EqualTo(ARBot.Common.Coordinates.Intrinsics.Distortion.InverseBrownConrady));
            Assert.That(r.Projection.Intrinsics.Coeffs, Is.EqualTo(new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f }));
            Assert.That(r.Projection.InverseIntrinsics.Width, Is.EqualTo(8));
            Assert.That(r.Projection.Transformation, Is.EqualTo(frame.Projection.Transformation));
            Assert.That(r.Projection.From, Is.EqualTo(frame.Projection.From));
        }

        [Test]
        public void CameraFrame_BezProjekce_RoundTripsToNull()
        {
            var frame = new CameraFrame
            {
                Name = "Left",
                TimeStamp = T0,
                ImageProbability = MakeImage<Gray>(4, 4, 1),
                Grid = MakeGrid(),
                Projection = null,
            };

            var r = RoundTrip(frame);

            Assert.That(r.Grid, Is.Not.Null);
            Assert.That(r.Projection, Is.Null);
        }

        [Test]
        public void CameraFrame_V3_ReadsWithoutProjection()
        {
            // Zaznam verze 3 (grid + hrany cesty, ale bez projekce).
            var frame = new CameraFrame
            {
                Name = "Left",
                FrameNum = 9,
                TimeStamp = T0,
                RGBTimeStamp = T0.AddMilliseconds(1),
                DepthTimeStamp = T0.AddMilliseconds(2),
                ImageProbability = MakeImage<Gray>(4, 4, 1),
                PathEdges = new List<PathEdge> { new PathEdge { Y = 3, Left = 1, Right = 7 } },
            };

            byte[] v3 = SerializeV3(frame);

            var read = new CameraFrame { Verze = 3 };
            read.FromData(TestHelpers.Enc, v3);

            Assert.That(read.Name, Is.EqualTo("Left"));
            Assert.That(read.FrameNum, Is.EqualTo(9u));
            Assert.That(read.Grid, Is.Null);
            Assert.That(read.Projection, Is.Null, "verze 3 nema projekci");
            Assert.That(read.PathEdges, Is.Not.Null);
            Assert.That(read.PathEdges.Count, Is.EqualTo(1));
            Assert.That(read.PathEdges[0].Y, Is.EqualTo(3));
        }

        /// <summary>Zapise ramec ve v3 layoutu: meta + name + 3 obrazy + 2 casy + grid + hrany cesty,
        /// BEZ azimutovych hranic gridu a BEZ projekce.</summary>
        private static byte[] SerializeV3(CameraFrame f)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, TestHelpers.Enc, leaveOpen: true))
            {
                bw.Write(f.FrameNum);
                bw.Write(f.DropedOutNum);
                bw.Write(f.FrameReceivePeriod.Ticks);
                bw.Write(f.FramePickupPeriod.Ticks);
                bw.Write(f.TimeStamp.Ticks);
                bw.Write(f.Name ?? string.Empty);
                ImageMsg.Write(bw, f.ImageRGB, ImageMsg.Compression.None);
                ImageMsg.Write(bw, f.ImageProbability, ImageMsg.Compression.None);
                ImageMsg.Write(bw, f.ImageDepth, ImageMsg.Compression.None);
                bw.Write(f.RGBTimeStamp.ToBinary());
                bw.Write(f.DepthTimeStamp.ToBinary());
                bw.Write(false);   // grid flag: bez gridu
                bw.Write(true);    // hrany cesty
                bw.Write(f.PathEdges.Count);
                foreach (var e in f.PathEdges)
                {
                    bw.Write(e.Y);
                    bw.Write(e.Left.HasValue);
                    if (e.Left.HasValue) bw.Write(e.Left.Value);
                    bw.Write(e.Right.HasValue);
                    if (e.Right.HasValue) bw.Write(e.Right.Value);
                }
            }
            return ms.ToArray();
        }

        private static ARBot.Common.Coordinates.CameraProjectionInfo MakeProjectionInfo()
            => new ARBot.Common.Coordinates.CameraProjectionInfo
            {
                Intrinsics = new ARBot.Common.Coordinates.Intrinsics
                {
                    Width = 8, Height = 6, PPx = 4.5f, PPy = 3.25f, Fx = 5.5f, Fy = 5.25f,
                    Model = ARBot.Common.Coordinates.Intrinsics.Distortion.InverseBrownConrady,
                    Coeffs = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f },
                },
                InverseIntrinsics = new ARBot.Common.Coordinates.Intrinsics
                {
                    Width = 8, Height = 6, PPx = 4.5f, PPy = 3.25f, Fx = 5.5f, Fy = 5.25f,
                    Model = ARBot.Common.Coordinates.Intrinsics.Distortion.None,
                    Coeffs = new float[5],
                },
                From = System.Numerics.Matrix4x4.CreateRotationZ(0.3f),
                To = System.Numerics.Matrix4x4.CreateRotationZ(-0.3f),
                Transformation = System.Numerics.Matrix4x4.CreateTranslation(1, 2, 3),
            };

        /// <summary>Zapise a znovu precte ramec pres zaznam/replay (aktualni FormatVersion).</summary>
        private static CameraFrame RoundTrip(CameraFrame frame)
        {
            using var ms = new MemoryStream();
            var rec = new RecordingTarget(ms, null, TestHelpers.Enc);
            rec.Start(); rec.Post(frame); rec.Stop();

            var catalog = MessageCatalog.CommonDefaults().Register(new CameraFrame());
            CameraFrame r = null;
            var sink = new DelegateTarget(m => { if (m is CameraFrame c) r = c; });
            sink.Start();
            using (var rms = new MemoryStream(ms.ToArray()))
            {
                var src = new FileMessageSource(rms, TestHelpers.Enc, catalog);
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            Assert.That(r, Is.Not.Null, "ramec se neprecetl");
            return r;
        }

        [Test]
        public void CameraFrame_V1_ReadsWithoutGrid()
        {
            // Stary zaznam (verze 1) NEobsahoval grid uvnitr ramce - musi se precist bez chyby (Grid=null).
            var frame = new CameraFrame
            {
                Name = "Left",
                FrameNum = 5,
                TimeStamp = T0,
                RGBTimeStamp = T0.AddMilliseconds(1),
                DepthTimeStamp = T0.AddMilliseconds(2),
                ImageProbability = MakeImage<Gray>(4, 4, 1),
            };

            byte[] v1 = SerializeV1(frame);

            var read = new CameraFrame { Verze = 1 };
            read.FromData(TestHelpers.Enc, v1);

            Assert.That(read.Grid, Is.Null, "verze 1 nema grid");
            Assert.That(read.Name, Is.EqualTo("Left"));
            Assert.That(read.FrameNum, Is.EqualTo(5u));
            Assert.That(read.TimeStamp, Is.EqualTo(T0));
            Assert.That(read.RGBTimeStamp, Is.EqualTo(frame.RGBTimeStamp));
            Assert.That(read.DepthTimeStamp, Is.EqualTo(frame.DepthTimeStamp));
            Assert.That(read.ImageRGB, Is.Null);
            Assert.That(read.ImageDepth, Is.Null);
            Assert.That(read.ImageProbability, Is.Not.Null);
            Assert.That(read.ImageProbability.Data, Is.EqualTo(frame.ImageProbability.Data));
        }

        /// <summary>Zapise ramec ve STAREM (verze 1) layoutu: meta + name + 3 obrazy + 2 casy, BEZ gridu.</summary>
        private static byte[] SerializeV1(CameraFrame f)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, TestHelpers.Enc, leaveOpen: true))
            {
                // meta (viz Message.Write(SensorStateBase)): FrameNum, DropedOutNum, periody(Ticks), TimeStamp(Ticks)
                bw.Write(f.FrameNum);
                bw.Write(f.DropedOutNum);
                bw.Write(f.FrameReceivePeriod.Ticks);
                bw.Write(f.FramePickupPeriod.Ticks);
                bw.Write(f.TimeStamp.Ticks);
                bw.Write(f.Name ?? string.Empty);
                ImageMsg.Write(bw, f.ImageRGB, ImageMsg.Compression.None);
                ImageMsg.Write(bw, f.ImageProbability, ImageMsg.Compression.None);
                ImageMsg.Write(bw, f.ImageDepth, ImageMsg.Compression.None);
                bw.Write(f.RGBTimeStamp.ToBinary());
                bw.Write(f.DepthTimeStamp.ToBinary());
            }
            return ms.ToArray();
        }
    }
}
