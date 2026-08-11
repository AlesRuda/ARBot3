using ARBot.Common.Occupancy;
using NUnit.Framework;
using System;

namespace ARBot.Common.Tests.Occupancy
{
    /// <summary>
    /// Testy kartezskeho occupancy gridu: souradnice, kruhovy buffer (posun + nulovani pruhu),
    /// akumulace log-odds a odvozeni <see cref="CellState"/> z obou kanalu.
    /// Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    [TestFixture]
    public class OccupancyGridTest
    {
        /// <summary>Maly grid (16 x 16 pri 10 cm) - snadno se v testu prochazi cely.</summary>
        private static OccupancyGrid Small() => new OccupancyGrid(new OccupancyGridConfig
        {
            Size = 16,
            Resolution = 0.1,
        });

        // ---------------- konfigurace ----------------

        [Test]
        public void Config_SizeNeniMocninaDvou_Vyhodi()
        {
            var cfg = new OccupancyGridConfig { Size = 100 };
            Assert.Throws<ArgumentException>(() => new OccupancyGrid(cfg));
        }

        [Test]
        public void Config_ClampSeNevejdeDoSbyte_Vyhodi()
        {
            var cfg = new OccupancyGridConfig { Clamp = 50f, Scale = 0.05f };   // 50/0,05 = 1000
            Assert.Throws<ArgumentException>(() => new OccupancyGrid(cfg));
        }

        [Test]
        public void Config_VychoziJeKonzistentni()
        {
            var g = new OccupancyGrid();
            Assert.That(g.Size, Is.EqualTo(256));
            Assert.That(g.Resolution, Is.EqualTo(0.05));
            Assert.That(g.Occ.Length, Is.EqualTo(256 * 256));
            Assert.That(g.Road.Length, Is.EqualTo(256 * 256));
        }

        // ---------------- souradnice ----------------

        [Test]
        public void Souradnice_CellAStred_JsouKonzistentni()
        {
            var g = Small();

            // floor: 0,25 m pri 10 cm -> bunka 2 (stred 0,25); -0,05 -> bunka -1 (stred -0,05)
            Assert.That(g.CellX(0.25), Is.EqualTo(2));
            Assert.That(g.CellX(-0.05), Is.EqualTo(-1));
            Assert.That(g.CellY(0.0), Is.EqualTo(0));

            Assert.That(g.CenterX(2), Is.EqualTo(0.25).Within(1e-9));
            Assert.That(g.CenterY(-1), Is.EqualTo(-0.05).Within(1e-9));

            // stred bunky se musi mapovat sam na sebe
            for (int c = -20; c <= 20; c++)
                Assert.That(g.CellX(g.CenterX(c)), Is.EqualTo(c), $"bunka {c}");
        }

        [Test]
        public void Recenter_DaRobotaDoStredu()
        {
            var g = Small();
            g.Recenter(1.0, -2.0);   // bunka (10, -20) pri 10 cm

            Assert.That(g.OriginX, Is.EqualTo(10 - 8));
            Assert.That(g.OriginY, Is.EqualTo(-20 - 8));
            Assert.That(g.Contains(10, -20), Is.True);
            // stred gridu = robot
            Assert.That(g.OriginX + g.Size / 2, Is.EqualTo(10));
            Assert.That(g.OriginY + g.Size / 2, Is.EqualTo(-20));
        }

        [Test]
        public void Contains_MimoOkno_JeFalse()
        {
            var g = Small();
            g.Recenter(0, 0);   // origin (-8,-8), okno [-8..7]

            Assert.That(g.Contains(-8, -8), Is.True);
            Assert.That(g.Contains(7, 7), Is.True);
            Assert.That(g.Contains(-9, 0), Is.False);
            Assert.That(g.Contains(0, 8), Is.False);
        }

        [Test]
        public void ZapisMimoOkno_SeZahodi()
        {
            var g = Small();
            g.Recenter(0, 0);

            g.ObserveOccupied(100, 100, 1f);

            Assert.That(g.State(100, 100), Is.EqualTo(CellState.Unknown));
            // a nesmi to zaspinit zadnou bunku uvnitr (kruhovy buffer by to jinak "zabalil")
            for (int i = 0; i < g.Size; i++)
                for (int j = 0; j < g.Size; j++)
                    Assert.That(g.StateAt(g.LocalIndex(i, j)), Is.EqualTo(CellState.Unknown),
                                $"bunka [{i},{j}] byla zaspinena zapisem mimo okno");
        }

        // ---------------- akumulace log-odds ----------------

        [Test]
        public void Akumulace_PrekazkaSeSectaAzNaBlocked()
        {
            var g = Small();
            g.Recenter(0, 0);

            Assert.That(g.State(0, 0), Is.EqualTo(CellState.Unknown));

            g.ObserveOccupied(0, 0, 1f);   // +0,85 -> jeste pod prahem 1,0
            Assert.That(g.State(0, 0), Is.EqualTo(CellState.Unknown));
            Assert.That(g.LogOddsOcc(0, 0), Is.EqualTo(0.85f).Within(0.05f));

            g.ObserveOccupied(0, 0, 1f);   // +1,70 -> nad prahem
            Assert.That(g.State(0, 0), Is.EqualTo(CellState.Blocked));
        }

        [Test]
        public void Akumulace_VolnoMusiPlatitProObaKanaly()
        {
            var g = Small();
            g.Recenter(0, 0);

            // jen geometrie hlasi volno -> o ceste porad nic nevime -> Unknown
            for (int i = 0; i < 10; i++) g.ObserveFree(0, 0, 1f);
            Assert.That(g.State(0, 0), Is.EqualTo(CellState.Unknown),
                        "volno jen z jednoho kanalu nesmi stacit na Free");

            // pridame jistou cestu z barvy -> teprve nyni Free
            for (int i = 0; i < 10; i++) g.ObserveRoad(0, 0, 1f, 1f);
            Assert.That(g.State(0, 0), Is.EqualTo(CellState.Free));
        }

        [Test]
        public void Akumulace_MimoCestuBlokujeStejneJakoPrekazka()
        {
            var g = Small();
            g.Recenter(0, 0);

            // geometrie: jiste volno
            for (int i = 0; i < 10; i++) g.ObserveFree(0, 0, 1f);
            // barva: jiste mimo cestu
            for (int i = 0; i < 10; i++) g.ObserveRoad(0, 0, 0f, 1f);

            Assert.That(g.State(0, 0), Is.EqualTo(CellState.Blocked),
                        "kanaly jsou rovnocenne - neprujezdnost od kterehokoli staci");
        }

        [Test]
        public void Akumulace_NeutralniPravdepodobnostNicNemeni()
        {
            var g = Small();
            g.Recenter(0, 0);

            for (int i = 0; i < 50; i++) g.ObserveRoad(0, 0, 0.5f, 1f);

            Assert.That(g.LogOddsRoad(0, 0), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(g.State(0, 0), Is.EqualTo(CellState.Unknown),
                        "\"o ceste nic nevim\" nesmi znamenat \"neni to cesta\"");
        }

        [Test]
        public void Akumulace_ClampOmezujeAObnovaJeKonecna()
        {
            var cfg = new OccupancyGridConfig { Size = 16, Resolution = 0.1 };
            var g = new OccupancyGrid(cfg);
            g.Recenter(0, 0);

            for (int i = 0; i < 1000; i++) g.ObserveOccupied(0, 0, 1f);
            Assert.That(g.LogOddsOcc(0, 0), Is.EqualTo(cfg.Clamp).Within(cfg.Scale));

            // Prepsani na volno musi byt konecne: clamp / |FreeUpdate| ~ 12,5 pozorovani na prah
            int steps = 0;
            while (g.LogOddsOcc(0, 0) > cfg.FreeThreshold && steps < 1000)
            {
                g.ObserveFree(0, 0, 1f);
                steps++;
            }
            Assert.That(steps, Is.LessThan(40), "prepsani obsazene bunky na volnou trva prilis dlouho");
        }

        [Test]
        public void Akumulace_SlabsiNezPulKvantaSeZahodi()
        {
            var cfg = new OccupancyGridConfig { Size = 16, Resolution = 0.1 };
            var g = new OccupancyGrid(cfg);
            g.Recenter(0, 0);

            // 0,85 * 0,01 = 0,0085 < Scale/2 = 0,025 -> zaokrouhli se na 0 (dokumentovany zamer)
            for (int i = 0; i < 100; i++) g.ObserveOccupied(0, 0, 0.01f);

            Assert.That(g.LogOddsOcc(0, 0), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(g.State(0, 0), Is.EqualTo(CellState.Unknown));
        }

        // ---------------- kruhovy buffer ----------------

        [Test]
        public void Posun_ZachovaPrekryvAVynulujeNovePruhy()
        {
            var g = Small();
            g.Recenter(0, 0);   // origin (-8,-8)

            // naplnit celý grid jako Blocked
            for (int i = 0; i < g.Size; i++)
                for (int j = 0; j < g.Size; j++)
                    for (int k = 0; k < 3; k++)
                        g.ObserveOccupied(g.OriginX + i, g.OriginY + j, 1f);

            g.MoveOrigin(g.OriginX + 3, g.OriginY);   // posun o 3 bunky na vychod

            // prekryv (stare sloupce, ktere zustaly) musi byt porad Blocked
            for (int cx = g.OriginX; cx < g.OriginX + g.Size - 3; cx++)
                Assert.That(g.State(cx, 0), Is.EqualTo(CellState.Blocked), $"prekryv sloupec {cx}");

            // tri nove sloupce na vychodnim kraji musi byt vynulovane
            for (int cx = g.OriginX + g.Size - 3; cx < g.OriginX + g.Size; cx++)
                for (int cy = g.OriginY; cy < g.OriginY + g.Size; cy++)
                    Assert.That(g.State(cx, cy), Is.EqualTo(CellState.Unknown), $"novy sloupec {cx},{cy}");
        }

        [Test]
        public void Posun_ZapadASeverJsouSymetricke()
        {
            foreach (var (dx, dy) in new[] { (-3, 0), (0, 3), (0, -3), (-2, -2), (2, 3) })
            {
                var g = Small();
                g.Recenter(0, 0);
                for (int i = 0; i < g.Size; i++)
                    for (int j = 0; j < g.Size; j++)
                        for (int k = 0; k < 3; k++)
                            g.ObserveOccupied(g.OriginX + i, g.OriginY + j, 1f);

                int ox = g.OriginX, oy = g.OriginY;
                g.MoveOrigin(ox + dx, oy + dy);

                // Kazda bunka nove drzena gridu: pokud byla drzena i pred posunem, ma zustat Blocked;
                // pokud je nova, ma byt Unknown.
                for (int cx = g.OriginX; cx < g.OriginX + g.Size; cx++)
                    for (int cy = g.OriginY; cy < g.OriginY + g.Size; cy++)
                    {
                        bool byloDrzeno = cx >= ox && cx < ox + g.Size && cy >= oy && cy < oy + g.Size;
                        var expected = byloDrzeno ? CellState.Blocked : CellState.Unknown;
                        Assert.That(g.State(cx, cy), Is.EqualTo(expected),
                                    $"posun ({dx},{dy}), bunka ({cx},{cy})");
                    }
            }
        }

        [Test]
        public void Posun_SkokMimoOkno_ZahodiVse()
        {
            var g = Small();
            g.Recenter(0, 0);
            for (int k = 0; k < 3; k++) g.ObserveOccupied(0, 0, 1f);
            Assert.That(g.State(0, 0), Is.EqualTo(CellState.Blocked));

            g.MoveOrigin(g.OriginX + 1000, g.OriginY);

            for (int i = 0; i < g.Size; i++)
                for (int j = 0; j < g.Size; j++)
                    Assert.That(g.StateAt(g.LocalIndex(i, j)), Is.EqualTo(CellState.Unknown),
                                $"bunka [{i},{j}] po skoku mimo okno");
        }

        [Test]
        public void Posun_OStejnyOrigin_NicNezmeni()
        {
            var g = Small();
            g.Recenter(0, 0);
            for (int k = 0; k < 3; k++) g.ObserveOccupied(0, 0, 1f);

            g.Recenter(0.049, 0.049);   // porad stejna bunka pri 10 cm

            Assert.That(g.State(0, 0), Is.EqualTo(CellState.Blocked));
        }

        [Test]
        public void Posun_TudyATamZachovaNepretrzitePozorovanouBunku()
        {
            // Robot se posouva dopredu a bunka pred nim je opakovane pozorovana; nesmi se "vynulovat"
            // jen tim, ze se grid posouva.
            var g = Small();
            double x = 0;
            g.Recenter(x, 0);
            int cell = g.CellX(0.5);

            for (int step = 0; step < 20; step++)
            {
                x += 0.1;                       // presne jedna bunka
                g.Recenter(x, 0);
                g.ObserveOccupied(cell + step, 0, 1f);
                g.ObserveOccupied(cell + step, 0, 1f);
                Assert.That(g.State(cell + step, 0), Is.EqualTo(CellState.Blocked), $"krok {step}");
            }
        }
    }
}
