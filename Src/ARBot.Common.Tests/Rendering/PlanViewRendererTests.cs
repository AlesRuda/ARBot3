using ARBot.Common.Coordinates;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Rendering;

namespace ARBot.Common.Tests.Rendering
{
    /// <summary>
    /// Pudorys pro webovy nahled headless runtime: occupancy grid + sit cest + poza + mrkev.
    /// Vsechno v lokalni ENU rovine, sever nahoru, robot ve stredu. Viz doc/plan-headless-web.md.
    /// </summary>
    public class PlanViewRendererTests
    {
        /// <summary>128 px na 20 m = 6,4 px/m; stred obrazku je (64, 64).</summary>
        private static PlanViewOptions Opt() => new PlanViewOptions { SizePx = 128, SpanM = 20 };

        [Test]
        public void PrazdnyVstup_NespadneAVratiObrazek()
        {
            byte[] png = PlanViewRenderer.Render(new PlanViewInput(), Opt());

            Assert.That(png, Is.Not.Null, "bez dat se kresli prazdna scena, ne null");
            using var bmp = SkiaSharp.SKBitmap.Decode(png);
            Assert.Multiple(() =>
            {
                Assert.That(bmp.Width, Is.EqualTo(128));
                Assert.That(bmp.Height, Is.EqualTo(128));
            });
        }

        [Test]
        public void NullVstup_Vyhodi()
            => Assert.Throws<System.ArgumentNullException>(() => PlanViewRenderer.Render(null, Opt()));

        [Test]
        public void NeprujezdnaBunkaSeObjeviCervene()
        {
            // Grid 8x8 po 1 m se stredem v pocatku; bunka severne od robota je neprujezdna.
            var og = new OccupancyGridMsg
            {
                Size = 8, Resolution = 1.0, OriginX = -4, OriginY = -4,
                Scale = 1f, BlockedThreshold = 0.5f, FreeThreshold = -0.5f,
                Occ = new sbyte[64], Road = new sbyte[64],
            };
            // (i=4, j=6) => stred (0.5, 2.5) m, tedy 2,5 m severne od robota v pocatku.
            og.Occ[4 + 6 * 8] = 100; og.Road[4 + 6 * 8] = 100;

            var input = new PlanViewInput { Grid = og, HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0 };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            int px = (int)(64 + 0.5 * 6.4);
            int py = (int)(64 - 2.5 * 6.4);
            var c = bmp.GetPixel(px, py);

            Assert.That(c.Red, Is.GreaterThan(c.Green).And.GreaterThan(c.Blue),
                        "neprujezdna bunka severne od robota ma byt cervena nad stredem obrazku");
        }

        [Test]
        public void PotvrzeneVolnaBunkaJeZelena()
        {
            var og = new OccupancyGridMsg
            {
                Size = 8, Resolution = 1.0, OriginX = -4, OriginY = -4,
                Scale = 1f, BlockedThreshold = 0.5f, FreeThreshold = -0.5f,
                Occ = new sbyte[64], Road = new sbyte[64],
            };
            og.Occ[4 + 6 * 8] = -100; og.Road[4 + 6 * 8] = -100;

            var input = new PlanViewInput { Grid = og, HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0 };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            var c = bmp.GetPixel((int)(64 + 0.5 * 6.4), (int)(64 - 2.5 * 6.4));
            Assert.That(c.Green, Is.GreaterThan(c.Red), "potvrzene volna bunka ma byt zelena");
        }

        [Test]
        public void RobotJeVeStredu_AKurzMeniTvar()
        {
            var input = new PlanViewInput { HasPose = true, PoseX = 12, PoseY = -7, PoseTheta = 0 };
            using var a = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            // Stred obrazku patri robotovi bez ohledu na jeho svetovou pozici (vyrez ho sleduje).
            Assert.That(a.GetPixel(64, 64).Alpha, Is.GreaterThan(0));

            // Otoceni o 90 stupnu musi obrazek zmenit (trojuhelnik miri jinam).
            input.PoseTheta = System.Math.PI / 2;
            using var b = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            bool nejakyRozdil = false;
            for (int y = 54; y < 74 && !nejakyRozdil; y++)
                for (int x = 54; x < 74; x++)
                    if (a.GetPixel(x, y) != b.GetPixel(x, y)) { nejakyRozdil = true; break; }

            Assert.That(nejakyRozdil, Is.True, "kurz robota se ma na pudorysu poznat");
        }

        [Test]
        public void SitCestSeVykresliZUzluVLLA()
        {
            // Dva uzly 10 m od sebe podel osy vychod-zapad; kresli se pruh siroky podle Node.Width.
            // RoadNetwork ma privatni konstruktor - stavi se Builderem (jako CorrelationTestScenes).
            var origin = GeoReference.FromDegrees(50.029, 14.52);
            var a = new Node(1, origin.ToLLA(-5, 0), 2.0);
            var b = new Node(2, origin.ToLLA(5, 0), 2.0);
            var builder = new RoadNetwork.Builder();
            builder.AddEdge(a, b, 10, wayId: 100, traversalCost: 10);
            var net = builder.Build();

            var input = new PlanViewInput
            {
                Network = net, Origin = origin,
                HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0,
            };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            // Bod 2 m vychodne od robota lezi na ose cesty -> nesmi mit barvu pozadi.
            var naCeste = bmp.GetPixel((int)(64 + 2 * 6.4), 64);
            var mimoCestu = bmp.GetPixel(64, (int)(64 - 8 * 6.4));   // 8 m severne, mimo pruh
            Assert.That(naCeste, Is.Not.EqualTo(mimoCestu), "pruh cesty ma byt videt");
        }

        [Test]
        public void RozsirujiciSeCestaJeTRYCHTYR_NeKonstantniPruh()
        {
            // Skutecna geometrie vozovky je kapsle s LINEARNE interpolovanou polosirkou mezi uzly
            // (RoadScene: From.Width*0,5 -> To.Width*0,5), takze cesta, ktera se rozsiruje, ma byt
            // na pudorysu trychtyr. Driv se kreslila jako pruh konstantni sirky (max z obou uzlu),
            // coz na mape s nalevkou neodpovidalo.
            var origin = GeoReference.FromDegrees(50.029, 14.52);
            var a = new Node(1, origin.ToLLA(-6, 0), 1.0);    // uzky konec (zapad)
            var b = new Node(2, origin.ToLLA(6, 0), 6.0);     // siroky konec (vychod)
            var builder = new RoadNetwork.Builder();
            builder.AddEdge(a, b, 12, wayId: 100, traversalCost: 12);

            var input = new PlanViewInput
            {
                Network = builder.Build(), Origin = origin,
                HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0,
            };
            // 40 m na 400 px = 10 px/m, stred (200, 200).
            using var bmp = SkiaSharp.SKBitmap.Decode(
                PlanViewRenderer.Render(input, new PlanViewOptions { SizePx = 400, SpanM = 40 }));

            int SirkaPasu(int x)
            {
                var pozadi = bmp.GetPixel(x, 10);   // horni okraj = pozadi
                int n = 0;
                for (int y = 0; y < bmp.Height; y++)
                    if (bmp.GetPixel(x, y) != pozadi) n++;
                return n;
            }

            int uUzkeho = SirkaPasu(200 - 45);   // 4,5 m zapadne od stredu (blizko uzkeho uzlu)
            int uSirokeho = SirkaPasu(200 + 45); // 4,5 m vychodne (blizko siroke ho uzlu)

            Assert.That(uSirokeho, Is.GreaterThan(uUzkeho + 15),
                        $"u siroke ho konce ma byt pas vyrazne sirsi (uzky={uUzkeho}, siroky={uSirokeho} px)");
        }

        [Test]
        public void SirkaPasuOdpovidaSirceCestyVMetrech()
        {
            // Cesta konstantni sirky 4 m pri 10 px/m ma dat pas ~40 px (osa je uvnitr).
            var origin = GeoReference.FromDegrees(50.029, 14.52);
            var a = new Node(1, origin.ToLLA(-8, 0), 4.0);
            var b = new Node(2, origin.ToLLA(8, 0), 4.0);
            var builder = new RoadNetwork.Builder();
            builder.AddEdge(a, b, 16, wayId: 100, traversalCost: 16);

            var input = new PlanViewInput
            {
                Network = builder.Build(), Origin = origin,
                HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0,
            };
            using var bmp = SkiaSharp.SKBitmap.Decode(
                PlanViewRenderer.Render(input, new PlanViewOptions { SizePx = 400, SpanM = 40 }));

            var pozadi = bmp.GetPixel(10, 10);
            int n = 0;
            for (int y = 0; y < bmp.Height; y++)
                if (bmp.GetPixel(200 + 60, y) != pozadi) n++;   // 6 m vychodne, mimo robota

            Assert.That(n, Is.EqualTo(40).Within(4),
                        $"4 m sirokou cestu pri 10 px/m ma odpovidat ~40 px, bylo {n}");
        }

        [Test]
        public void MrkevSeVykresliJinouBarvouNezPozadi()
        {
            var input = new PlanViewInput
            {
                HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0,
                HasCarrot = true, CarrotX = 0, CarrotY = 5,
            };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            // Spojnice robot -> mrkev vede po ose y; bod 2,5 m severne na ni lezi.
            var naSpojnici = bmp.GetPixel(64, (int)(64 - 2.5 * 6.4));
            var pozadi = bmp.GetPixel(4, 64);
            Assert.That(naSpojnici, Is.Not.EqualTo(pozadi), "mrkev a spojnice k ni maji byt videt");
        }

        // --- Trasa globalni navigace a draha z lokalniho planovace (12. 9. 2026) ---------------
        // Obojí odpovida na otazku „co se robot chysta udelat", jen v jinem meritku: trasa na
        // desitky az stovky metru, lokalni plan na jednotky. Barvy se nesmi splest s ujetou
        // drahou (modra) ani s mrkvi (zluta), proto se testuje odstin, ne jen „neni pozadi".

        [Test]
        public void TrasaGlobalniNavigaceJeFialova()
        {
            var input = new PlanViewInput
            {
                HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0,
                Route = new[] { new PlanViewSegment(-3, 0, 3, 0) },
            };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            // 2 m vychodne od robota - na usecce, ale mimo trojuhelnik robota.
            var c = bmp.GetPixel((int)(64 + 2 * 6.4), 64);
            Assert.Multiple(() =>
            {
                Assert.That(c.Blue, Is.GreaterThan(c.Green), "trasa ma byt fialova");
                Assert.That(c.Red, Is.GreaterThan(c.Green), "trasa ma byt fialova, ne modra");
            });
        }

        [Test]
        public void DrahaLokalnihoPlanovaceJeAzurova()
        {
            var input = new PlanViewInput
            {
                HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0,
                LocalPlan = new[]
                {
                    new PlanViewPoint(0, 0), new PlanViewPoint(0, 2), new PlanViewPoint(0, 4),
                },
            };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            var c = bmp.GetPixel(64, (int)(64 - 3 * 6.4));
            Assert.Multiple(() =>
            {
                Assert.That(c.Blue, Is.GreaterThan(100), "plan ma byt azurovy");
                Assert.That(c.Green, Is.GreaterThan(100), "plan ma byt azurovy");
                Assert.That(c.Red, Is.LessThan(c.Green), "plan ma byt azurovy, ne bily");
            });
        }

        [Test]
        public void PlanZJednohoBoduSeNekresli()
        {
            // Planovac, ktery nic nenasel, muze poslat jediny uzel - cara z nej nevznikne
            // a kolecko uprostred robota by jen matlo.
            var input = new PlanViewInput
            {
                HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0,
                LocalPlan = new[] { new PlanViewPoint(0, 3) },
            };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            var c = bmp.GetPixel(64, (int)(64 - 3 * 6.4));
            var pozadi = bmp.GetPixel(4, 64);
            Assert.That(c, Is.EqualTo(pozadi), "z jednoho uzlu se nekresli nic");
        }

        [Test]
        public void LegendaJeJenKTomu_CoSeKresli()
        {
            // Pravy dolni roh: bez trasy i planu tam nesmi byt nic, s nimi ano. Legenda vznikla
            // proto, ze pet barevnych car bez popisu se da jen hadat.
            var prazdny = new PlanViewInput { HasPose = true };
            var sPlanem = new PlanViewInput
            {
                HasPose = true,
                LocalPlan = new[] { new PlanViewPoint(0, 0), new PlanViewPoint(0, 4) },
            };

            using var bezLegendy = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(prazdny, Opt()));
            using var sLegendou = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(sPlanem, Opt()));

            Assert.That(RozdilnychVRohu(bezLegendy), Is.Zero, "bez dat se legenda nekresli");
            Assert.That(RozdilnychVRohu(sLegendou), Is.GreaterThan(0), "s planem ma legenda byt");
        }

        /// <summary>
        /// Kazda kreslena cara musi mit polozku v legende. Prvni verze legendy vynechala prave
        /// <b>ujetou drahu</b> — modrou caru za robotem, ktera se navic odstinem plete s azurovym
        /// lokalnim planem, takze byla ze vsech car nejpotrebnejsi popsat.
        /// </summary>
        [Test]
        public void UjetaDrahaMaPolozkuVLegende()
        {
            var sDrahou = new PlanViewInput
            {
                HasPose = true,
                Trail = new[] { new PlanViewPoint(0, -4), new PlanViewPoint(0, 0) },
            };
            var bezDrahy = new PlanViewInput { HasPose = true };

            using var a = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(sDrahou, Opt()));
            using var b = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(bezDrahy, Opt()));

            Assert.That(RozdilnychVRohu(a), Is.GreaterThan(0),
                        "ujeta draha ma mit v legende svuj radek");
            Assert.That(RozdilnychVRohu(b), Is.Zero, "bez drahy tam nema byt nic");
        }

        /// <summary>Kolik pixelu v pravem dolnim rohu (legenda) se lisi od pozadi.</summary>
        private static int RozdilnychVRohu(SkiaSharp.SKBitmap bmp)
        {
            var pozadi = bmp.GetPixel(4, 64);
            int n = 0;
            for (int y = 110; y < 128; y++)
                for (int x = 90; x < 128; x++)
                    if (bmp.GetPixel(x, y) != pozadi) n++;
            return n;
        }

        // --- Zony, ktere maji byt dosazeny (12. 9. 2026) --------------------------------------
        // Pri dohledu nad zavodem v terenu je z pudorysu potreba poznat, kam robot MUSI dojet;
        // mrkev rika jen, kam miri v pristich metrech.

        [Test]
        public void ZonaSeKresliVeSvemDojezdovemPolomeru()
        {
            // Zona 5 m severne od robota s polomerem 2 m: kruznice ma protnout osu y 3 m a 7 m
            // severne, a mezi tim (ve stredu zony) ma byt pozadi.
            var input = new PlanViewInput
            {
                HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0,
                Zones = new[] { new PlanViewZone(0, 5, 2, "cil", active: true) },
            };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));
            var pozadi = bmp.GetPixel(4, 64);

            Assert.Multiple(() =>
            {
                Assert.That(bmp.GetPixel(64, (int)(64 - 3 * 6.4)), Is.Not.EqualTo(pozadi),
                            "blizsi okraj zony (3 m severne) ma byt nakresleny");
                Assert.That(bmp.GetPixel(64, (int)(64 - 7 * 6.4)), Is.Not.EqualTo(pozadi),
                            "vzdalenejsi okraj zony (7 m severne) ma byt nakresleny");
                Assert.That(bmp.GetPixel(64 + 10, (int)(64 - 5 * 6.4)), Is.EqualTo(pozadi),
                            "vnitrek zony se nevyplnuje - prekryl by grid");
            });
        }

        [Test]
        public void ZonaMaPolozkuVLegende()
        {
            var input = new PlanViewInput
            {
                HasPose = true,
                Zones = new[] { new PlanViewZone(0, 5, 2, "cil", active: true) },
            };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            Assert.That(RozdilnychVRohu(bmp), Is.GreaterThan(0), "zona ma mit v legende svuj radek");
        }

        [Test]
        public void AktivniZonaJeVyraznejsiNezNeaktivni()
        {
            // Aktivni se kresli plnou carou, ostatni carkovane - jinak by z obrazku nebylo poznat,
            // ktere misto robot resi ted. Merí se poctem nakreslenych pixelu na kruznici.
            int Pixelu(bool aktivni)
            {
                var input = new PlanViewInput
                {
                    HasPose = true,
                    Zones = new[] { new PlanViewZone(0, 0, 4, null, aktivni) },
                };
                using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));
                var pozadi = bmp.GetPixel(4, 4);
                int n = 0;
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                        if (bmp.GetPixel(x, y) != pozadi) n++;
                return n;
            }

            Assert.That(Pixelu(true), Is.GreaterThan(Pixelu(false)),
                        "plna cara ma mit vic pixelu nez carkovana");
        }

        [Test]
        public void ZonaBezPolomeruSePoradNakresli()
        {
            // Stary zaznam (GlobalNavMsg verze 1) polomer nenese. Poloha je porad pravdiva,
            // takze se kresli aspon znacka stredu - zmizet nesmi.
            var input = new PlanViewInput
            {
                HasPose = true,
                Zones = new[] { new PlanViewZone(0, 3, 0, "cil", active: true) },
            };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            var pozadi = bmp.GetPixel(4, 64);
            Assert.That(bmp.GetPixel(64, (int)(64 - 3 * 6.4)), Is.Not.EqualTo(pozadi),
                        "stred zony ma byt oznaceny i bez polomeru");
        }
    }
}
