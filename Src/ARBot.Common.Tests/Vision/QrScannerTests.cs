using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Vision.Qr;

namespace ARBot.Common.Tests.Vision;

/// <summary>
/// Testy cteni QR kodu (viz doc/robotour-mission.md, sekce „Cteni QR kodu").
///
/// <para><b>Dve veci se tu hlidaji nejvic:</b> (a) scanner je vypnuty, dokud ho mise nezapne —
/// robot nikdy neskenuje, kdyz muze jet; (b) cesta <c>Image&lt;BGR32&gt;</c> → Y800 <c>byte[]</c>
/// bez <c>System.Drawing</c>, protoze <c>System.Drawing.Common</c> je jen na Windows a na Armbianu
/// by to spadlo.</para>
/// </summary>
public class QrScannerTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Dekoder, ktery si pamatuje, kolikrat ho kdo zavolal, a vraci predem dany text.</summary>
    private sealed class FakeDecoder : IQrDecoder
    {
        private readonly string? text;
        public FakeDecoder(string? text = null) { this.text = text; }

        public int Calls { get; private set; }
        public int LastWidth { get; private set; }
        public int LastHeight { get; private set; }

        public QrResult[] Decode(Image<Gray> img)
        {
            Calls++;
            LastWidth = img.Width;
            LastHeight = img.Height;
            if (text == null) return Array.Empty<QrResult>();
            return new[]
            {
                new QrResult(text, new[]
                {
                    new Point2D(1, 2), new Point2D(3, 2), new Point2D(3, 4), new Point2D(1, 4),
                }),
            };
        }
    }

    /// <summary>Snimek s jednobarevnym RGB obrazem dane velikosti.</summary>
    private static CameraFrame Frame(string name, int w = 64, int h = 48, byte gray = 128)
    {
        var rgb = new Image<BGR32>(w, h);
        var p = new BGR32 { Data = rgb.Data };
        for (int i = 0; i < w * h; i++)
        {
            p.Index = i * 4;
            p.R = gray; p.G = gray; p.B = gray;
        }
        return new CameraFrame { Name = name, ImageRGB = rgb, TimeStamp = T0, RGBTimeStamp = T0 };
    }

    // ---------------- Scanner je vypnuty, dokud ho mise nezapne ----------------

    /// <summary>
    /// <b>Vypnuty scanner nedekoduje nic.</b> Za jizdy je dekodovani cista rezie a nikoho nezajima;
    /// hlavne ale plati opacna garance: robot skenuje VYHRADNE pod drzenym nouzovym zastavenim,
    /// takze obsluha stojici u robotu s krabici v ruce ma fyzickou garanci, ne jen softwarovou.
    /// </summary>
    [Test]
    public void VypnutyScanner_NedekodujeNic()
    {
        var decoder = new FakeDecoder("geo:49.0,16.0");
        var scanner = new QrScanner(decoder, new QrScannerConfig { CameraName = "Right" });

        var results = scanner.Process(Frame("Right"));

        Assert.Multiple(() =>
        {
            Assert.That(scanner.Enabled, Is.False, "vychozi stav je VYPNUTO");
            Assert.That(decoder.Calls, Is.Zero, "dekoder se nesmi ani zavolat");
            Assert.That(results, Is.Empty);
        });
    }

    [Test]
    public void ZapnutyScanner_PrecteKodZOcekavaneKamery()
    {
        var decoder = new FakeDecoder("geo:49.2103,16.5991");
        var scanner = new QrScanner(decoder, new QrScannerConfig { CameraName = "Right" })
        {
            Enabled = true,
        };

        var results = scanner.Process(Frame("Right"));

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(results[0].Text, Is.EqualTo("geo:49.2103,16.5991"));
            Assert.That(results[0].CameraName, Is.EqualTo("Right"));
            Assert.That(results[0].TimeStamp, Is.EqualTo(T0));
            Assert.That(results[0].Corners, Has.Length.EqualTo(4), "rohy v obraze pro pozdejsi vizualni dojezd");
        });
    }

    /// <summary>
    /// Kod se cte z PRAVE kamery — snimky z jinych kamer scanner ignoruje, aby se pod nouzovym
    /// zastavenim nemarnil cas na obraz, ve kterem kod byt nema.
    /// </summary>
    [Test]
    public void JinaKamera_SeIgnoruje()
    {
        var decoder = new FakeDecoder("geo:49.0,16.0");
        var scanner = new QrScanner(decoder, new QrScannerConfig { CameraName = "Right" })
        {
            Enabled = true,
        };

        var results = scanner.Process(Frame("Left"));

        Assert.Multiple(() =>
        {
            Assert.That(decoder.Calls, Is.Zero);
            Assert.That(results, Is.Empty);
        });
    }

    /// <summary>
    /// Prazdne jmeno kamery = skenovat VSECHNY. Levne zmirneni: pod nouzovym zastavenim je vypocetni
    /// cas zdarma a odpada tim cela otazka, na kterou stranu robot dojel.
    /// </summary>
    [Test]
    public void PrazdneJmenoKamery_SkenujeVsechny()
    {
        var decoder = new FakeDecoder("geo:49.0,16.0");
        var scanner = new QrScanner(decoder, new QrScannerConfig { CameraName = "" }) { Enabled = true };

        var left = scanner.Process(Frame("Left"));
        var right = scanner.Process(Frame("Right"));

        Assert.Multiple(() =>
        {
            Assert.That(decoder.Calls, Is.EqualTo(2));
            Assert.That(left, Has.Length.EqualTo(1));
            Assert.That(right, Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void SnimekBezObrazu_NicNerozbije()
    {
        var decoder = new FakeDecoder("geo:49.0,16.0");
        var scanner = new QrScanner(decoder, new QrScannerConfig { CameraName = "" }) { Enabled = true };

        var results = scanner.Process(new CameraFrame { Name = "Right", TimeStamp = T0 });

        Assert.Multiple(() =>
        {
            Assert.That(decoder.Calls, Is.Zero);
            Assert.That(results, Is.Empty);
        });
    }

    // ---------------- Cesta BGR32 -> Y800 ----------------

    // Samotny prevod na sedou (vazeny jas, podvzorkovani, chybi System.Drawing) uz neni vec
    // scanneru — je to Image<T>.ToGray a testuje ho ImageToGrayTests, vcetne pixel typu, ktere
    // scanner nikdy nevidi. Tady zustava jen to, co je chovani SCANNERU: ze dekoderu preda uz
    // podvzorkovany obraz.

    /// <summary>
    /// Scanner posila dekoderu uz podvzorkovany obraz — jinak by podvzorkovani nic neuslo.
    /// Kod velikosti A5 z 2 m ma v 640x480 dost pixelu i po zmenseni na polovinu.
    /// </summary>
    [Test]
    public void Scanner_PosilaDekoderuPodvzorkovanyObraz()
    {
        var decoder = new FakeDecoder();
        var scanner = new QrScanner(decoder, new QrScannerConfig { CameraName = "", Downscale = 2 })
        {
            Enabled = true,
        };

        scanner.Process(Frame("Right", w: 64, h: 48));

        Assert.Multiple(() =>
        {
            Assert.That(decoder.LastWidth, Is.EqualTo(32));
            Assert.That(decoder.LastHeight, Is.EqualTo(24));
        });
    }

    // ---------------- Zprava do zaznamu ----------------

    /// <summary>
    /// Precteny text jde DOSLOVA do zpravy → v zaznamu je videt, co robot precetl, i kdyz to
    /// zamitl. Po soutezi je to jediny zpusob, jak dohledat, co se kdy precetlo.
    /// </summary>
    [Test]
    public void Zprava_JeObousmerna()
    {
        var result = new QrResult("geo:49.2103,16.5991",
                                 new[] { new Point2D(10, 20), new Point2D(30, 20), new Point2D(30, 40) })
        {
            CameraName = "Right",
            TimeStamp = T0,
        };

        var original = result.ToLogMessage();
        var buffer = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            original.ToData(bw);

        buffer.Position = 0;
        var loaded = new ARBot.Common.Logs.QrCodeMsg();
        using (var br = new System.IO.BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Text, Is.EqualTo("geo:49.2103,16.5991"));
            Assert.That(loaded.CameraName, Is.EqualTo("Right"));
            Assert.That(loaded.TimeStamp, Is.EqualTo(T0));
            Assert.That(loaded.CornersX, Has.Length.EqualTo(3), "QR muze mit 3 nebo 4 nalezene body");
            Assert.That(loaded.CornersX[1], Is.EqualTo(30).Within(1e-9));
            Assert.That(loaded.CornersY[2], Is.EqualTo(40).Within(1e-9));
        });
    }

    [Test]
    public void Zprava_JeVKataloguZprav()
    {
        // Bez registrace v katalogu index zpravu ukaze, ale Read vrati null - a tvari se to jako
        // chybejici stupen (presne to se stalo u GPSState v ARBot.Analyze).
        Assert.That(ARBot.Common.Communication.MessageCatalog.CommonDefaults().Contains("QrCodeMsg"),
                    Is.True);
    }
}
