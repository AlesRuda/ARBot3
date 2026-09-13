using System;
using ARBot.Common.Occupancy;
using NUnit.Framework;

namespace ARBot.Common.Tests.Occupancy
{
    /// <summary>
    /// Doplneni semantiky v <b>klinu mezi zornymi poli barevnych kamer</b>
    /// (<see cref="WedgeFiller"/>). Viz doc/occupancy-and-local-planning.md.
    ///
    /// <para>Testuji se hlavne <b>ctyri pojistky</b>, protoze prave ony delaji rozdil mezi
    /// „interpolace mezi dvema pozorovanimi" a „vymyslena cesta": geometrie se nedoplnuje nikdy,
    /// skutecne mereni se neprepisuje, bez podpory z OBOU stran se nedoplnuje nic, a doplnenim
    /// nemuze vzniknout prekazka.</para>
    /// </summary>
    [TestFixture]
    public class WedgeFillerTest
    {
        private const int N = 128;
        private const double Res = 0.05;

        /// <summary>
        /// Robot je v pocatku a miri na vychod (theta = 0), tedy klin lezi na ose +X.
        ///
        /// <para><b>Geometrie je vsude potvrzene volna</b> — tak to v klinu skutecne je: hloubka ma
        /// zorne pole 89,6 stupnu a klin pokryva, chybi jen barva (55,0 stupnu). Bez toho by se
        /// doplnovani vubec nespustilo, a bylo by to spravne: co nema potvrzenou geometrii, to se
        /// <see cref="CellState.Free"/> stejne nestane.</para>
        /// </summary>
        private static OccupancyGrid Grid()
        {
            var g = new OccupancyGrid(new OccupancyGridConfig { Size = N, Resolution = Res });
            g.Recenter(0, 0);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    for (int k = 0; k < 5; k++)
                        g.ObserveFree(g.OriginX + i, g.OriginY + j, 1f);
            return g;
        }

        private static WedgeFiller Filler(OccupancyGrid g, double sirkaDeg = 6.0)
            => new WedgeFiller(g, sirkaDeg * Math.PI / 180.0 / 2.0);

        /// <summary>Zapise do bunky semantiku „cesta" (zaporne log-odds) tak, aby byla Free.</summary>
        private static void Cesta(OccupancyGrid g, double x, double y)
        {
            int cx = g.CellX(x), cy = g.CellY(y);
            for (int k = 0; k < 5; k++) g.ObserveRoad(cx, cy, 1f, 1f);
        }

        /// <summary>Zapise do bunky semantiku „mimo cestu" (kladne log-odds).</summary>
        private static void Trava(OccupancyGrid g, double x, double y)
        {
            int cx = g.CellX(x), cy = g.CellY(y);
            for (int k = 0; k < 5; k++) g.ObserveRoad(cx, cy, 0f, 1f);
        }

        /// <summary>Naplni pruh semantikou vlevo i vpravo od klinu (to, co vidi obe kamery).</summary>
        private static void ObeStrany(OccupancyGrid g, Action<OccupancyGrid, double, double> co,
                                      double odstup = 0.5, double od = 0.3)
        {
            for (double x = od; x <= 5.0; x += Res)
            {
                co(g, x, +odstup);
                co(g, x, -odstup);
            }
        }

        [Test]
        public void VKlinu_SeSemantikaDoplniZObouStran()
        {
            var g = Grid();
            ObeStrany(g, Cesta);

            // Na ose pred robotem semantika chybi - presne jako v klinu mezi kamerami.
            int cx = g.CellX(3.0), cy = g.CellY(0.0);
            Assert.That(g.LogOddsRoad(cx, cy), Is.EqualTo(0f), "priprava: na ose nesmi byt zadny vzorek");

            int doplneno = Filler(g).Fill(0, 0, 0);

            Assert.That(doplneno, Is.GreaterThan(0), "v klinu se ma neco doplnit");
            Assert.That(g.LogOddsRoad(cx, cy), Is.LessThan(0f),
                        "bunka na ose ma dostat semantiku 'cesta' interpolovanou z obou stran");
        }

        [Test]
        public void MimoKlin_SeNedoplnujeNic()
        {
            // Semantika je VSUDE krome uzke mezery 1,5 m vlevo od osy. Ta mezera ma podporu z obou
            // stran uplne stejne jako klin - jediny rozdil je azimut, takze test opravdu zkousi
            // omezeni na klin, ne to, ze se nenasli sousedi.
            var g = Grid();
            for (double x = 0.5; x <= 5.0; x += Res)
                for (double y = -2.0; y <= 2.0; y += Res)
                    if (Math.Abs(y - 1.5) > 0.06) Cesta(g, x, y);

            int cx = g.CellX(3.0), cy = g.CellY(1.5);
            Assert.That(g.LogOddsRoad(cx, cy), Is.EqualTo(0f), "priprava: mezera ma byt prazdna");

            Filler(g).Fill(0, 0, 0);

            Assert.That(g.LogOddsRoad(cx, cy), Is.EqualTo(0f),
                        "mimo klin se doplnovat nesmi - tam kamera vidi a chybejici vzorek ma jinou pricinu");
        }

        /// <summary>
        /// ⚠️ <b>Doplnit se musi i bunka se SLABYM vzorkem</b>, nejen s nulovym. Prvni verze
        /// doplnovala jen bunky s <c>LogOddsRoad == 0</c> a merenim nad zaznamem se ukazalo, ze
        /// <b>nedela prakticky nic</b>: bunky v klinu maji vetsinou slaby vzorek z okraje zorneho
        /// pole (kde <c>RoadConfidence</c> klesa se vzdalenosti), takze podminka nikdy neplatila.
        /// </summary>
        [Test]
        public void BunkaSeSlabymVzorkem_SeTakyDoplni()
        {
            var g = Grid();
            ObeStrany(g, Cesta);

            // Jediny slaby vzorek - bunka je porad Unknown, ale uz neni "bez vzorku".
            int cx = g.CellX(3.0), cy = g.CellY(0.0);
            g.ObserveRoad(cx, cy, 1f, 0.5f);
            Assert.That(g.State(cx, cy), Is.EqualTo(CellState.Unknown), "priprava: porad Unknown");
            Assert.That(g.LogOddsRoad(cx, cy), Is.Not.EqualTo(0f), "priprava: uz ma nejaky vzorek");

            Filler(g).Fill(0, 0, 0);

            Assert.That(g.State(cx, cy), Is.EqualTo(CellState.Free),
                        "doplneni se ma pricist i k slabemu vzorku a bunku rozhodnout");
        }

        [Test]
        public void BezPodporyZObouStran_SeNedoplnujeNic()
        {
            var g = Grid();
            // Jen leva strana; prava zustane bez semantiky.
            for (double x = 0.5; x <= 5.0; x += Res) Cesta(g, x, +0.1);

            int doplneno = Filler(g).Fill(0, 0, 0);

            Assert.That(doplneno, Is.Zero,
                        "s podporou jen z jedne strany to neni interpolace, ale extrapolace");
        }

        [Test]
        public void SkutecneMereni_SeNeprepisuje()
        {
            var g = Grid();
            ObeStrany(g, Cesta);
            // Uprostred klinu je NAMERENA trava - doplneni na ni sahnout nesmi.
            Trava(g, 3.0, 0.0);
            int cx = g.CellX(3.0), cy = g.CellY(0.0);
            float pred = g.LogOddsRoad(cx, cy);

            Filler(g).Fill(0, 0, 0);

            Assert.That(g.LogOddsRoad(cx, cy), Is.EqualTo(pred),
                        "bunka se skutecnym vzorkem se nesmi zmenit");
        }

        [Test]
        public void Geometrie_SeNedoplnujeNikdy()
        {
            var g = Grid();
            ObeStrany(g, Cesta);
            int cx = g.CellX(3.0), cy = g.CellY(0.0);
            float pred = g.LogOddsOcc(cx, cy);

            Filler(g).Fill(0, 0, 0);

            Assert.That(g.LogOddsOcc(cx, cy), Is.EqualTo(pred),
                        "prekazku vidi hloubka; vymyslet za ni 'volno' by bylo nebezpecne");
        }

        [Test]
        public void DoplnenimNemuzeVzniknoutPrekazka()
        {
            var g = Grid();
            // Z obou stran TRAVA (kladne log-odds) - interpolace by mirila k prekazce.
            ObeStrany(g, Trava);

            Filler(g).Fill(0, 0, 0);

            for (double x = 0.6; x <= 5.0; x += 0.1)
            {
                int cx = g.CellX(x), cy = g.CellY(0.0);
                Assert.That(g.State(cx, cy), Is.Not.EqualTo(CellState.Blocked),
                            $"doplneni samo nesmi vyrobit prekazku (x={x:F2} m)");
            }
        }

        [Test]
        public void Vypnuto_NedelaNic()
        {
            var g = Grid();
            ObeStrany(g, Cesta);

            Assert.That(Filler(g, sirkaDeg: 0).Fill(0, 0, 0), Is.Zero,
                        "wedgefill=0 musi vratit presne puvodni chovani");
        }

        [Test]
        public void KlinSeOtaciSRobotem()
        {
            // Tyz test jako ten prvni, jen robot miri na sever (theta = 90 stupnu): klin musi byt
            // v TELESOVEM ramci, ne ve svetovem, jinak by se s otocenim robota rozjel.
            var g = Grid();
            for (double y = 0.5; y <= 5.0; y += Res)
            {
                Cesta(g, +0.5, y);
                Cesta(g, -0.5, y);
            }

            Filler(g).Fill(0, 0, Math.PI / 2);

            int cx = g.CellX(0.0), cy = g.CellY(3.0);
            Assert.That(g.LogOddsRoad(cx, cy), Is.LessThan(0f),
                        "pri kurzu na sever ma klin lezet na ose +Y");
        }

        /// <summary>
        /// ⚠️ <b>Doplnit se musi i bunka se SLABYM SOUSEDEM.</b> Puvodne se zadal soused
        /// <i>rozhodnuty</i> (Free nebo Blocked ze semantiky) s odvodnenim „nesirit nejistotu" —
        /// a merenim nad zaznamem ze zarizeni se ukazalo, ze prave tim doplnovani padalo: v klinu
        /// je soused rozhodnuty z obou stran jen v <b>17,3 %</b> pripadu, v <b>78,6 %</b> je aspon
        /// jeden jen slaby. Je to zakonite — bunky u klinu vidi kamera sikmo na okraji zorneho
        /// pole, takze maji nizkou duveru.
        /// </summary>
        [Test]
        public void SlabiSousede_TakyStaci()
        {
            var g = Grid();
            // Sousede tesne POD prahem Free (jeden vzorek misto peti) - typicky okraj zorneho pole.
            for (double x = 0.5; x <= 5.0; x += Res)
            {
                g.ObserveRoad(g.CellX(x), g.CellY(+0.15), 1f, 1f);
                g.ObserveRoad(g.CellX(x), g.CellY(-0.15), 1f, 1f);
            }
            int scx = g.CellX(3.0), scy = g.CellY(0.15);
            Assert.That(g.State(scx, scy), Is.EqualTo(CellState.Unknown),
                        "priprava: soused ma byt slaby, ne rozhodnuty");

            int doplneno = Filler(g).Fill(0, 0, 0);

            Assert.That(doplneno, Is.GreaterThan(0),
                        "slaby soused je v klinu pravidlo, ne vyjimka - doplnit se z nej musi");
        }

        /// <summary>
        /// ⚠️ <b>Doplnuje se uz od <see cref="WedgeFiller.MinRangeM"/>, ne az od pulmetru.</b>
        /// Puvodni prah 0,5 m nechal <b>20,4 %</b> zastaveni paprsku bez lecby — a prave blizka
        /// bunka bolí nejvic, protoze <c>VBrake</c> je na kratke vzdalenosti nejcitlivejsi.
        /// </summary>
        [Test]
        public void BunkaTesnePredRobotem_SeTakyDoplni()
        {
            var g = Grid();
            ObeStrany(g, Cesta, odstup: 0.15);

            Filler(g).Fill(0, 0, 0);

            int cx = g.CellX(0.35), cy = g.CellY(0.0);
            Assert.That(g.LogOddsRoad(cx, cy), Is.LessThan(0f),
                        "bunka 0,35 m pred robotem uz do klinu patri (MinRangeM = 0,3 m)");
        }

        [Test]
        public void PodRobotem_SeNedoplnuje()
        {
            // Blize nez pulmetr je slepa zona pod kamerami - jiny jev s jinou lecbou
            // (LocalPlannerConfig.FootprintRadiusM), a klin je tam uzsi nez bunka.
            var g = Grid();
            ObeStrany(g, Cesta, odstup: 0.1);

            Filler(g).Fill(0, 0, 0);

            int cx = g.CellX(0.2), cy = g.CellY(0.0);
            Assert.That(g.LogOddsRoad(cx, cy), Is.EqualTo(0f),
                        "pod robotem (bliz nez MinRangeM) se klin nedoplnuje - tam si planovac "
                        + "bere pudorys robotu jako sjizdny sam");
        }
    }
}
