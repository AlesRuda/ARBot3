using System.Net.Http;
using System.Threading.Tasks;
using ARBot.Common.Logs;
using ARBot.Robot.Web;

namespace ARBot.Runtime.Tests.Web
{
    /// <summary>
    /// Webovy nahled: server odpovida na pet cest, POST /stop zavola callback a port 0 si necha
    /// pridelit od OS (pevny port by testy rozbil pri soubehu). Viz doc/plan-headless-web.md.
    /// </summary>
    [NonParallelizable]
    public class WebPreviewServerTests
    {
        private WebStatus status;
        private WebPreviewServer server;
        private HttpClient klient;
        private int stopu;

        [SetUp]
        public void Start()
        {
            stopu = 0;
            status = new WebStatus();
            server = new WebPreviewServer(status, () => stopu++);
            Assert.That(server.Start(0), Is.True, "server se ma nastartovat na portu pridelenem OS");
            klient = new HttpClient { BaseAddress = new System.Uri($"http://127.0.0.1:{server.Port}/") };
        }

        [TearDown]
        public void Konec()
        {
            klient?.Dispose();
            server?.Dispose();
        }

        [Test]
        public async Task Koren_VratiHtmlStranku()
        {
            var r = await klient.GetAsync("/");
            string telo = await r.Content.ReadAsStringAsync();

            Assert.Multiple(() =>
            {
                Assert.That((int)r.StatusCode, Is.EqualTo(200));
                Assert.That(r.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
                Assert.That(telo, Does.Contain("/world.png"), "stranka ma nacitat pudorys");
                Assert.That(telo, Does.Contain("/camera.jpg"), "stranka ma nacitat kameru");
                Assert.That(telo, Does.Contain("/stop"), "stranka ma mit tlacitko zastaveni");
            });
        }

        [Test]
        public async Task Pudorys_JePngIBezDat()
        {
            var r = await klient.GetAsync("/world.png?t=1");
            byte[] telo = await r.Content.ReadAsByteArrayAsync();

            Assert.Multiple(() =>
            {
                Assert.That((int)r.StatusCode, Is.EqualTo(200));
                Assert.That(r.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
                Assert.That(telo[0], Is.EqualTo(0x89), "magicke bajty PNG");
                Assert.That(telo[1], Is.EqualTo((byte)'P'));
            });
        }

        [Test]
        public async Task Kamera_BezSnimku_Vrati204()
        {
            var r = await klient.GetAsync("/camera.jpg");
            Assert.That((int)r.StatusCode, Is.EqualTo(204), "bez snimku se vraci No Content, ne chyba");
        }

        [Test]
        public async Task Kamera_PosleRgbIPravdepodobnostCesty()
        {
            // Snimek se kopiruje jen pri zajmu - o ten se prvni pozadavek postara.
            await klient.GetAsync("/camera.jpg");

            var frame = new ARBot.Common.Devices.CameraFrame
            {
                Name = "Left",
                ImageRGB = new ARBot.Common.Common.Image<ARBot.Common.Common.BGR32>(8, 4),
                ImageProbability = new ARBot.Common.Common.Image<ARBot.Common.Common.Gray>(8, 4),
            };
            status.Post(frame);

            var rgb = await klient.GetAsync("/camera.jpg?cam=Left");
            var prob = await klient.GetAsync("/camera.jpg?cam=Left&layer=prob");
            byte[] rgbTelo = await rgb.Content.ReadAsByteArrayAsync();
            byte[] probTelo = await prob.Content.ReadAsByteArrayAsync();

            Assert.Multiple(() =>
            {
                Assert.That((int)rgb.StatusCode, Is.EqualTo(200));
                Assert.That(rgb.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/jpeg"));
                Assert.That(rgbTelo[0], Is.EqualTo(0xFF), "magicke bajty JPEG");
                Assert.That(rgbTelo[1], Is.EqualTo(0xD8));
                Assert.That((int)prob.StatusCode, Is.EqualTo(200), "layer=prob posila ImageProbability");
                Assert.That(probTelo[0], Is.EqualTo(0xFF));
            });
            Assert.That(status.CameraNames, Does.Contain("Left"));
        }

        [Test]
        public async Task Status_JeJsonSeStavemRobota()
        {
            status.Post(new RobotStateMsg { X = 1.5, Y = -2.5, Theta = 0.25, V = 0.8 });

            var r = await klient.GetAsync("/status.json");
            string json = await r.Content.ReadAsStringAsync();

            Assert.Multiple(() =>
            {
                Assert.That(r.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
                Assert.That(json, Does.Contain("\"v\""), "rychlost patri do stavu");
                Assert.That(json, Does.Contain("0.8"));
                Assert.That(json, Does.Contain("\"running\""));
            });
        }

        [Test]
        public async Task Pudorys_RespektujeVolbuMeritka()
        {
            // Stejna scena ve trech meritkach musi dat tri rozdilne obrazky (jiny vyrez).
            status.Post(new RobotStateMsg { X = 0, Y = 0, Theta = 0 });

            byte[] m2 = await klient.GetByteArrayAsync("/world.png?scale=2");
            byte[] m10 = await klient.GetByteArrayAsync("/world.png?scale=10");
            byte[] m50 = await klient.GetByteArrayAsync("/world.png?scale=50");
            byte[] bez = await klient.GetByteArrayAsync("/world.png");

            Assert.Multiple(() =>
            {
                Assert.That(m2, Is.Not.EqualTo(m10), "2 m a 10 m maji jiny vyrez");
                Assert.That(m50, Is.Not.EqualTo(m10), "50 m a 10 m maji jiny vyrez");
                Assert.That(bez.Length, Is.EqualTo(m10.Length), "bez scale= plati 10 m");
                Assert.That(m2[0], Is.EqualTo(0x89), "porad je to PNG");
            });
        }

        [Test]
        public async Task NesmyslneMeritko_SpadneNaVychozi()
        {
            status.Post(new RobotStateMsg { X = 0, Y = 0, Theta = 0 });

            byte[] nesmysl = await klient.GetByteArrayAsync("/world.png?scale=abc");
            byte[] vychozi = await klient.GetByteArrayAsync("/world.png?scale=10");

            Assert.That(nesmysl.Length, Is.EqualTo(vychozi.Length),
                        "nesmyslne meritko nesmi shodit stranku, plati vychozich 10 m");
        }

        [Test]
        public async Task NeznamaCesta_Vrati404()
        {
            var r = await klient.GetAsync("/neexistuje");
            Assert.That((int)r.StatusCode, Is.EqualTo(404));
        }

        [Test]
        public async Task StopJenPostem()
        {
            var get = await klient.GetAsync("/stop");
            Assert.Multiple(() =>
            {
                Assert.That((int)get.StatusCode, Is.EqualTo(405), "GET nesmi zastavit robota");
                Assert.That(stopu, Is.EqualTo(0));
            });

            var post = await klient.PostAsync("/stop", new StringContent(string.Empty));
            Assert.Multiple(() =>
            {
                Assert.That((int)post.StatusCode, Is.EqualTo(200));
                Assert.That(stopu, Is.EqualTo(1), "POST /stop ma zavolat callback presne jednou");
            });
        }

        [Test]
        public async Task PudorysUkazeNeprujezdnouBunku()
        {
            var og = new OccupancyGridMsg
            {
                Size = 8, Resolution = 1.0, OriginX = -4, OriginY = -4,
                Scale = 1f, BlockedThreshold = 0.5f, FreeThreshold = -0.5f,
                Occ = new sbyte[64], Road = new sbyte[64],
            };
            og.Occ[4 + 6 * 8] = 100; og.Road[4 + 6 * 8] = 100;
            status.Post(og);
            status.Post(new RobotStateMsg { X = 0, Y = 0, Theta = 0 });

            byte[] png = await klient.GetByteArrayAsync("/world.png");
            using var bmp = SkiaSharp.SKBitmap.Decode(png);

            bool nasel = false;
            for (int y = 0; y < bmp.Height && !nasel; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.Red > 150 && c.Green < 100) { nasel = true; break; }
                }

            Assert.That(nasel, Is.True, "neprujezdna bunka ma byt na pudorysu cervene videt");
        }
    }

    /// <summary>
    /// <b>Vyber mise ze stranky</b> (<c>POST /mission</c>) — jediny zasah, ktery robota nakonec
    /// rozjede, takze se hlida hlavne to, co ho <b>nesmi</b> pustit: bez drzeneho nouzoveho
    /// zastaveni, s neznamou misi, s <c>none</c>, podruhe, nebo jinak nez POSTem.
    ///
    /// <para>Gate se testuje <b>na serveru</b>, ne v prohlizeci: klientska kontrola je pohodli,
    /// tahle je pojistka.</para>
    /// </summary>
    [NonParallelizable]
    public class WebMissionPickTests
    {
        private WebStatus status;
        private WebPreviewServer server;
        private HttpClient klient;
        private readonly List<string> zvolene = new List<string>();

        [SetUp]
        public void Start()
        {
            zvolene.Clear();
            status = new WebStatus { AwaitingMission = true };
            // Callback dela TOTEZ co v aplikaci: hodnota jde do ParamStore, ktery ji taky overi.
            // Kdyby tady byl jen sberac, test by tvrdil, ze neznama mise projde - a v aplikaci by
            // ji odmitl az store. Odmitnuti neplatne hodnoty je soucast tehle cesty, ne detail.
            server = new WebPreviewServer(status, onStop: () => { }, onMission: m =>
            {
                ARBot.Common.Configuration.ParamStore.Current.SetRuntimeOverride("mission", m);
                zvolene.Add(m);
            });
            Assert.That(server.Start(0), Is.True);
            klient = new HttpClient { BaseAddress = new System.Uri($"http://127.0.0.1:{server.Port}/") };
        }

        [TearDown]
        public void Konec()
        {
            klient?.Dispose();
            server?.Dispose();
            // ParamStore je staticky - vratit ho, at si testy nelezou do zeli.
            ARBot.Common.Configuration.ParamStore.Build(new string[0]);
        }

        /// <summary>Stav motoru s nouzovym zastavenim - to je ta fyzicka pojistka.</summary>
        private void Stop(bool drzi)
            => status.Post(new ARBot.Common.Devices.MotorStateBase(drzi, 0, 0, 24, 0, 0, 0, 0));

        [Test]
        public async Task BezDrzenehoStopu_SeMiseNespusti()
        {
            Stop(drzi: false);

            var r = await klient.PostAsync("/mission?m=freerun", null);

            Assert.Multiple(() =>
            {
                Assert.That((int)r.StatusCode, Is.EqualTo(409));
                Assert.That(zvolene, Is.Empty, "mise se NESMI spustit");
            });
            Assert.That(await r.Content.ReadAsStringAsync(), Does.Contain("nouzove zastaveni"),
                        "obsluha musi vedet, co ma udelat - ne jen ze to nejde");
        }

        [Test]
        public async Task BezStavuMotoru_SeMiseNespusti()
        {
            // Motor, ktery nic nehlasi, NENI "stop neni stisknuty" - to je nejnebezpecnejsi zamena.
            var r = await klient.PostAsync("/mission?m=freerun", null);

            Assert.Multiple(() =>
            {
                Assert.That((int)r.StatusCode, Is.EqualTo(409));
                Assert.That(zvolene, Is.Empty);
            });
        }

        [Test]
        public async Task PriDrzenemStopu_SeMiseSpustiAZapiseDoKonfigurace()
        {
            Stop(drzi: true);

            var r = await klient.PostAsync("/mission?m=freerun", null);

            Assert.Multiple(() =>
            {
                Assert.That(r.IsSuccessStatusCode, Is.True);
                Assert.That(zvolene, Is.EqualTo(new[] { "freerun" }));
                // Volba MUSI byt v ucinne konfiguraci: tu cte ARBotRuntime.Start a tece do zaznamu.
                // Kdyby se mise predala bokem, zaznam by tvrdil mission=none, i kdyz se jelo.
                Assert.That(ARBot.Common.Configuration.ParamRegistry.Mission.Value, Is.EqualTo("freerun"));
                Assert.That(ARBot.Common.Configuration.ParamRegistry.Mission.Origin,
                            Is.EqualTo(ARBot.Common.Configuration.ParamOrigin.Runtime));
            });
        }

        [TestCase("none", 400, TestName = "None_NeniMise")]
        [TestCase("neexistuje", 400, TestName = "NeznamaMise_SeOdmitne")]
        [TestCase("", 400, TestName = "PrazdnaHodnota_SeOdmitne")]
        public async Task NeplatnaVolba_SeOdmitne(string mise, int kod)
        {
            Stop(drzi: true);

            var r = await klient.PostAsync("/mission?m=" + mise, null);

            Assert.Multiple(() =>
            {
                Assert.That((int)r.StatusCode, Is.EqualTo(kod));
                Assert.That(zvolene, Is.Empty);
            });
        }

        [Test]
        public async Task Podruhe_SeMiseNezmeni()
        {
            Stop(drzi: true);
            await klient.PostAsync("/mission?m=freerun", null);
            status.AwaitingMission = false;   // aplikace uz Run s misi rozjela

            var r = await klient.PostAsync("/mission?m=robotour", null);

            Assert.Multiple(() =>
            {
                Assert.That((int)r.StatusCode, Is.EqualTo(409));
                Assert.That(zvolene, Is.EqualTo(new[] { "freerun" }), "druha volba se ignoruje");
            });
        }

        [Test]
        public async Task GetMiseNespusti()
        {
            // Prefetch prohlizece ani nahled odkazu nesmi robota rozjet - tyz duvod jako u /stop.
            Stop(drzi: true);

            var r = await klient.GetAsync("/mission?m=freerun");

            Assert.Multiple(() =>
            {
                Assert.That((int)r.StatusCode, Is.EqualTo(405));
                Assert.That(zvolene, Is.Empty);
            });
        }

        [Test]
        public async Task VirtualniStop_SeSkutecnymHwNeexistuje()
        {
            // Dalkove ovladani nouzoveho zastaveni na skutecnem robotu tu nikdy nesmi byt.
            var r = await klient.PostAsync("/virtualestop?on=true", null);

            Assert.That((int)r.StatusCode, Is.EqualTo(404));
        }

        [Test]
        public async Task NabidkaMisi_JeVeStavuAJdeZRegistru()
        {
            string json = await klient.GetStringAsync("/status.json");

            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Contain("\"pick\":[\"freerun\",\"robotour\"]"),
                            "seznam misi se bere z registru parametru, ne z druheho seznamu");
                Assert.That(json, Does.Contain("\"estop\":false"));
                Assert.That(json, Does.Contain("\"pickBlocked\""));
            });
        }

        [Test]
        public async Task KdyzSeNaMisiNeceka_NabidkaNeni()
        {
            status.AwaitingMission = false;

            string json = await klient.GetStringAsync("/status.json");

            Assert.That(json, Does.Not.Contain("\"pick\""), "panel se nesmi ukazat za jizdy");
        }
    }

    /// <summary>
    /// Sam <see cref="WebStatus"/> bez serveru - hlavne <b>lizny render</b>: bez zajmu se snimek
    /// kamery vubec nekopiruje, takze nahled bez publika nestoji ani memcpy. To je jadro navrhu,
    /// protoze rozpocet CPU na Pi neni znamy - viz doc/plan-headless-web.md.
    /// </summary>
    public class WebStatusTests
    {
        private static ARBot.Common.Devices.CameraFrame Snimek() => Snimek("Left", 0);

        /// <summary>Snimek vyplneny jednou hodnotou - podle ni se pozna, ktery snimek to je.</summary>
        private static ARBot.Common.Devices.CameraFrame Snimek(string jmeno, byte hodnota)
        {
            var img = new ARBot.Common.Common.Image<ARBot.Common.Common.BGR32>(16, 16);
            for (int i = 0; i < img.Data.Length; i++) img.Data[i] = hodnota;
            return new ARBot.Common.Devices.CameraFrame { Name = jmeno, ImageRGB = img };
        }

        private static SkiaSharp.SKColor Dekoduj(byte[] jpeg)
        {
            Assert.That(jpeg, Is.Not.Null, "snimek se mel zakodovat");
            using var bmp = SkiaSharp.SKBitmap.Decode(jpeg);
            return bmp.GetPixel(8, 8);
        }

        [Test]
        public void DveKamery_SeObeAktualizuji()
        {
            // Regrese: pool kopii mel kapacitu 2, takze po PRVNIM snimku z kazde ze dvou kamer
            // uz nebyl volny slot a vsechny dalsi se ticho zahazovaly - obraz na strance zamrzl.
            var status = new WebStatus();
            status.NoteCameraInterest();

            status.Post(Snimek("Left", 40));
            status.Post(Snimek("Right", 40));
            status.Post(Snimek("Left", 200));
            status.Post(Snimek("Right", 200));

            var left = Dekoduj(status.RenderCameraJpeg("Left", null));
            var right = Dekoduj(status.RenderCameraJpeg("Right", null));

            Assert.Multiple(() =>
            {
                Assert.That(left.Blue, Is.EqualTo(200).Within(12), "Left ma nest POSLEDNI snimek");
                Assert.That(right.Blue, Is.EqualTo(200).Within(12), "Right ma nest POSLEDNI snimek");
            });
        }

        [Test]
        public void JednaKamera_SeAktualizujeOpakovane()
        {
            var status = new WebStatus();
            status.NoteCameraInterest();

            for (int k = 0; k < 10; k++)
                status.Post(Snimek("Left", (byte)(20 * k)));

            Assert.That(Dekoduj(status.RenderCameraJpeg("Left", null)).Blue,
                        Is.EqualTo(180).Within(12), "po deseti snimcich ma byt videt ten desaty");
        }

        [Test]
        public void BezZajmu_SeSnimekNezkopiruje()
        {
            var status = new WebStatus();

            status.Post(Snimek());

            Assert.Multiple(() =>
            {
                Assert.That(status.CameraNames, Is.Empty, "bez zajmu se snimek zahodi bez kopirovani");
                Assert.That(status.RenderCameraJpeg(null, null), Is.Null);
            });
        }

        [Test]
        public void PoOhlaseniZajmu_SeSnimekZkopirujeAZakoduje()
        {
            var status = new WebStatus();

            status.NoteCameraInterest();
            status.Post(Snimek());

            Assert.Multiple(() =>
            {
                Assert.That(status.CameraNames, Does.Contain("Left"));
                Assert.That(status.RenderCameraJpeg(null, null), Is.Not.Null);
            });
        }

        [Test]
        public void UjetaDrahaSeSbiraAzOdUrciteVzdalenosti()
        {
            var status = new WebStatus();

            status.Post(new RobotStateMsg { X = 0, Y = 0 });
            status.Post(new RobotStateMsg { X = 0.01, Y = 0 });   // pod prahem 0,1 m -> nezapise se
            status.Post(new RobotStateMsg { X = 1.0, Y = 0 });    // nad prahem -> zapise se

            // Draha neni verejna; overi se pres to, ze stav nese posledni pozici a pudorys jde nakreslit.
            string json = status.ToJson(running: true);
            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Contain("\"x\":1"));
                Assert.That(status.RenderPlanView(), Is.Not.Null);
            });
        }

        [Test]
        public void VekSeMeriProtiTimeBase_NeProtiSystemovymHodinam()
        {
            // Vek je "jak davno zprava vysla do streamu" a mericka zakladna musi byt TimeBase
            // (cas startu aplikace + monotonni stopky), tedy tataz, jakou pouziva zbytek aplikace.
            // Test je citlivy na michani zakladen: zapsat TimeBase.Now (lokalni) a odecist
            // DateTime.UtcNow by dalo vek rovny offsetu zony - u nas 3600 nebo 7200 s.
            var status = new WebStatus();

            status.Post(new ARBot.Common.Models.IMUState());

            string json = status.ToJson(running: true);
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"age\":([0-9.]+)");

            Assert.That(m.Success, Is.True, "stav ma nest vek mereni: " + json);
            double vek = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(vek, Is.LessThan(1),
                        "prave dorucena zprava ma mit vek pod sekundu; vetsi cislo = michani "
                        + "casovych zakladen (TimeBase proti DateTime.UtcNow/Now)");
        }

        [Test]
        public void StavNeseSenzoryIVekMereni()
        {
            var status = new WebStatus();

            status.Post(new ARBot.Common.Models.IMUState());
            string json = status.ToJson(running: true);

            Assert.Multiple(() =>
            {
                // Senzory se ctou z ARBotHW, ktery v testu neexistuje - pole je prazdne, ne chyba.
                Assert.That(json, Does.Contain("\"sensors\":[]"),
                            "bez ARBotHW je seznam senzoru prazdny (a HW se NESMI zalozit)");
                Assert.That(json, Does.Contain("\"measurements\""));
                Assert.That(json, Does.Contain("IMUState"), "vek mereni se sleduje podle druhu zpravy");
            });
        }

        [Test]
        public void VekMereniOdlisiKameryPodleJmena()
        {
            var status = new WebStatus();
            status.NoteCameraInterest();

            status.Post(new ARBot.Common.Devices.CameraFrame
            {
                Name = "Left",
                ImageRGB = new ARBot.Common.Common.Image<ARBot.Common.Common.BGR32>(4, 4),
            });

            Assert.That(status.ToJson(true), Does.Contain("CameraFrame:Left"),
                        "kamery se rozlisuji jmenem, jinak by dve kamery splynuly v jeden udaj");
        }

        [Test]
        public void MeritkoUrciVyrez_AUseckaOdpovidaCislu()
        {
            // Tlacitko „10 m" na strance = usecka 10 m = vyrez 40 m (ctvrtina). Viz doc/headless.md.
            Assert.Multiple(() =>
            {
                Assert.That(ARBot.Common.Rendering.PlanViewRenderer.SpanForScaleBar(10), Is.EqualTo(40));
                Assert.That(ARBot.Common.Rendering.PlanViewRenderer.SpanForScaleBar(2), Is.EqualTo(8));
                Assert.That(ARBot.Common.Rendering.PlanViewRenderer.SpanForScaleBar(50), Is.EqualTo(200));
                Assert.That(ARBot.Common.Rendering.PlanViewRenderer.ScaleBarMeters(40), Is.EqualTo(10));
                Assert.That(ARBot.Common.Rendering.PlanViewRenderer.ScaleBarMeters(8), Is.EqualTo(2));
                Assert.That(ARBot.Common.Rendering.PlanViewRenderer.ScaleBarMeters(200), Is.EqualTo(50));
                Assert.That(ARBot.Common.Rendering.PlanViewRenderer.SpanForScaleBar(0), Is.EqualTo(40),
                            "nesmyslna hodnota spadne na 10 m");
                Assert.That(ARBot.Common.Rendering.PlanViewRenderer.SpanForScaleBar(-5), Is.EqualTo(40));
            });
        }

        // ---------------- Hlavicka stranky ----------------

        /// <summary>
        /// Hlavicka musi rict, <b>ktera binarka bezi</b> a jak dlouho — na zarizeni se nasazuje casto
        /// a bez toho nejde poznat, jestli na Pi bezi to, co jsem pred chvili nahral, nebo predchozi
        /// verze (a jestli se proces mezitim nerestartoval).
        /// </summary>
        [Test]
        public void Hlavicka_NeseVerziDobuBehuASystemovyCas()
        {
            string json = new WebStatus().ToJson(running: true);

            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Contain("\"head\":{"));
                Assert.That(json, Does.Contain("\"version\":"));
                Assert.That(json, Does.Contain("\"uptime\":"));
                Assert.That(json, Does.Contain("\"now\":"));
            });
        }

        /// <summary>
        /// Bez bezici mise se hlasi prazdne jmeno (stranka pak napise „mise: žádná") — a hlavne to
        /// nesmi spadnout: <c>ARBotRuntime</c> v testech vubec neexistuje a cteni jeho
        /// <c>Current</c> by ho zalozilo i s inicializaci hardwaru.
        /// </summary>
        [Test]
        public void BezMise_JeJmenoPrazdneANesahaSeNaRuntime()
        {
            string json = new WebStatus().ToJson(running: false);

            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Contain("\"mission\":\"\""));
                Assert.That(json, Does.Not.Contain("\"waiting\""), "neni mise, neni na co cekat");
                Assert.That(ARBot.Robot.ARBotRuntime.HasCurrent, Is.False, "hlavicka nesmi runtime zalozit");
            });
        }

        /// <summary>
        /// Doba behu roste a jde z <see cref="ARBot.Common.Common.TimeBase"/> — tedy z monotonni
        /// zakladny, kterou meri cela aplikace, ne ze systemovych hodin.
        /// </summary>
        [Test]
        public void DobaBehu_Roste()
        {
            var status = new WebStatus();
            double prvni = Uptime(status.ToJson(true));

            System.Threading.Thread.Sleep(30);

            Assert.That(Uptime(status.ToJson(true)), Is.GreaterThan(prvni));
        }

        /// <summary>Vytahne <c>uptime</c> z JSON bez parseru - staci na jedno cislo.</summary>
        private static double Uptime(string json)
        {
            const string klic = "\"uptime\":";
            int i = json.IndexOf(klic, StringComparison.Ordinal);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), "hlavicka nema uptime");
            int od = i + klic.Length;
            int po = od;
            while (po < json.Length && (char.IsDigit(json[po]) || json[po] == '.' || json[po] == '-')) po++;
            return double.Parse(json.Substring(od, po - od), System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Faze mise uz v tabulce NENI jako cislo — presla do hlavicky jako text. Cislo fáze
        /// obsluze nic nerika a dve mista se stejnym udajem se rozejdou.
        /// </summary>
        [Test]
        public void FazeMise_NeniVTabulceJakoCislo()
        {
            var status = new WebStatus();
            status.Post(new ARBot.Common.Logs.MissionMsg { Phase = 3, ElapsedSec = 12 });

            string json = status.ToJson(running: true);

            Assert.That(json, Does.Not.Contain("\"missionPhase\""));
        }

        [Test]
        public void NeznamaZpravaSeIgnoruje()
        {
            var status = new WebStatus();

            Assert.DoesNotThrow(() =>
            {
                status.Post(null);
                status.Post(new Info("cokoliv"));
            });
        }
    }
}
