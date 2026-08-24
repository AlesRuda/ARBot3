using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Common;

namespace ARBot.Common.Tests.Common;

/// <summary>
/// Prolozeni bodu primkou - <see cref="LineFit"/> proti puvodni
/// <see cref="Line2D.LinearRegesion(IEnumerable{Point2D})"/>.
///
/// <para><b>Nacpak dalsi estimator.</b> Hranova lokalizace hradluje inliery a pocita sigma
/// <b>kolmou</b> vzdalenosti, ale prokladala osovou metodou nejmensich kvadratu - estimator tedy
/// neminimalizoval to, co se pak vyhodnocuje. Testy nize meri obe vady, ktere z toho plynou
/// (nespojitost u 45 stupnu a nachylnost na odlehly bod), a drzi vlastnost, na ktere zavisi
/// smysl Huberovy vahy: <b>vzdaleny bod, ktery na primce sedi, nesmi ztratit vahu</b>. Vazeni
/// podle vzdalenosti bylo zmereno 23. 8. 2026 a zhorsovalo - viz doc/map-correlation-localization.md.</para>
///
/// <para><b>Vsechno je deterministicke</b> (seedovany <c>Random</c>, zadny RANSAC), takze cisla
/// v komentarich jde zopakovat.</para>
/// </summary>
public class LineFitTests
{
    /// <summary>Body presne na primce smeru <paramref name="dirRad"/> ve vzdalenosti <c>offset</c> od pocatku.</summary>
    private static List<Point2D> OnLine(double dirRad, double offset, int count = 40,
                                        double from = 1.0, double step = 0.25)
    {
        double ux = Math.Cos(dirRad), uy = Math.Sin(dirRad);
        double nx = -uy, ny = ux;
        var pts = new List<Point2D>(count);
        for (int i = 0; i < count; i++)
        {
            double s = from + i * step;
            pts.Add(new Point2D(ux * s + nx * offset, uy * s + ny * offset));
        }
        return pts;
    }

    /// <summary>Uhel primky normalizovany na +-90 stupnu (primka nema orientaci).</summary>
    private static double Half(double a)
    {
        while (a > Math.PI / 2) a -= Math.PI;
        while (a < -Math.PI / 2) a += Math.PI;
        return a;
    }

    private static double AngleErrDeg(Line2D line, double trueDirRad)
        => Math.Abs(Half(line.Angle - trueDirRad)) * 180 / Math.PI;

    [Test]
    public void Ortogonalni_naPresnychBodech_vratiPrimkuPresne()
    {
        foreach (double deg in new[] { 0, 10, 30, 44, 45, 46, 60, 89, 90, 120, -30 })
        {
            var pts = OnLine(deg * Math.PI / 180, offset: 0.7);
            var line = LineFit.Orthogonal(pts, null);

            Assert.That(line, Is.Not.Null, $"{deg} stupnu");
            Assert.That(AngleErrDeg(line, deg * Math.PI / 180), Is.LessThan(1e-6), $"smer u {deg} stupnu");
            Assert.That(line.Distance(new Point2D(0, 0)), Is.EqualTo(0.7).Within(1e-6),
                        $"odstup od pocatku u {deg} stupnu");
        }
    }

    [Test]
    public void Ortogonalni_neniNespojitaU45Stupnu()
    {
        // Puvodni regrese vybira osu podle |dx| > |dy|, takze u 45 stupnu prepina mezi dvema
        // ruznymi prolozenimi. Na presnych bodech to nevadi (oba jsou presne) - projevi se to
        // az se sumem, viz PriUhluKolem45.
        var rnd = new Random(4242);
        double worst = 0;
        for (double deg = 40; deg <= 50; deg += 0.25)
        {
            var pts = OnLine(deg * Math.PI / 180, offset: 0.5, count: 40);
            var noisy = pts.Select(p => new Point2D(p.X + (rnd.NextDouble() - 0.5) * 0.1,
                                                    p.Y + (rnd.NextDouble() - 0.5) * 0.1)).ToList();
            var line = LineFit.Orthogonal(noisy, null);
            Assert.That(line, Is.Not.Null);
            worst = Math.Max(worst, AngleErrDeg(line, deg * Math.PI / 180));
        }
        // Sum 5 cm na zakladne ~10 m -> radove desetiny stupne. Volny strop; test hlida, ze to
        // NEskace o jednotky stupnu, ne presnou hodnotu.
        Assert.That(worst, Is.LessThan(1.0), "ortogonalni prolozeni nema u 45 stupnu skok");
    }

    [Test]
    public void PriUhluKolem45_jeOrtogonalniStabilnejsiNezOsova()
    {
        // Meri se NEJHORSI chyba smeru na sweepu kolem 45 stupnu - tam, kde osova varianta
        // prepina vetev. Tentyz sum pro oba estimatory (tytez body), takze je to poctive A/B.
        double worstOls = 0, worstTls = 0;
        for (double deg = 35; deg <= 55; deg += 0.5)
        {
            var rnd = new Random(777);          // tentyz sum pro kazdy uhel i estimator
            var pts = OnLine(deg * Math.PI / 180, offset: 0.5, count: 40);
            var noisy = pts.Select(p => new Point2D(p.X + (rnd.NextDouble() - 0.5) * 0.16,
                                                    p.Y + (rnd.NextDouble() - 0.5) * 0.16)).ToList();

            double t = deg * Math.PI / 180;
            worstOls = Math.Max(worstOls, AngleErrDeg(Line2D.LinearRegesion(noisy), t));
            worstTls = Math.Max(worstTls, AngleErrDeg(LineFit.Orthogonal(noisy, null), t));
        }

        TestContext.Out.WriteLine($"nejhorsi chyba smeru kolem 45 st.: osova {worstOls:F3}, ortogonalni {worstTls:F3}");
        Assert.That(worstTls, Is.LessThanOrEqualTo(worstOls),
                    "ortogonalni prolozeni nesmi byt u diagonalni primky horsi nez osove");
    }

    [Test]
    public void Huber_odolaOdlehlemuBodu()
    {
        var pts = OnLine(0.0, offset: 1.0, count: 30);
        pts.Add(new Point2D(4.0, 6.0));          // jeden hruby outlier 5 m mimo

        var plain = LineFit.Orthogonal(pts, null);
        var huber = LineFit.Fit(pts, LineFitMode.OrthogonalHuber, tolerance: null, huberK: 1.5);

        double dPlain = Math.Abs(plain.Distance(new Point2D(0, 0)) - 1.0);
        double dHuber = Math.Abs(huber.Distance(new Point2D(0, 0)) - 1.0);

        TestContext.Out.WriteLine($"odchylka odstupu: bez Hubera {dPlain:F3} m, s Huberem {dHuber:F3} m");
        Assert.That(dHuber, Is.LessThan(dPlain / 3), "Huber musi outlier vyrazne potlacit");
        Assert.That(AngleErrDeg(huber, 0), Is.LessThan(1.0), "a smer nesmi outlier stocit");
    }

    [Test]
    public void Huber_vzdalenemuBoduNAPrimceNEbereVahu()
    {
        // TOHLE je ten podstatny rozdil proti vazeni 1/sigma^2 podle vzdalenosti (zmereno
        // 23. 8. 2026, zhorsovalo): bod daleko od robotu, ktery na primce SEDI, si musi vahu
        // podrzet - jinak se zkrati zakladna a smer zasumi. Tolerance roste se vzdalenosti
        // stejne, jako ji nastavuje CorridorConfig.InlierThresholdPerMeter.
        var pts = OnLine(0.0, offset: 0.0, count: 40, from: 1.0, step: 0.25);   // 1 az ~11 m
        Func<Point2D, double> tol = p => 0.10 + 0.15 * Math.Sqrt(p.X * p.X + p.Y * p.Y);

        var huber = LineFit.Fit(pts, LineFitMode.OrthogonalHuber, tol, huberK: 1.5);
        var plain = LineFit.Orthogonal(pts, null);

        Assert.That(huber, Is.Not.Null);
        // Vsechny body na primce -> vsechny vahy 1 -> vysledek musi byt TOTOZNY s nevazenym.
        Assert.That(AngleErrDeg(huber, 0), Is.LessThan(1e-6));
        Assert.That(huber.Distance(new Point2D(0, 0)), Is.EqualTo(plain.Distance(new Point2D(0, 0))).Within(1e-9));
    }

    [Test]
    public void Huber_sToleranci_potlaciBodPodleVLASTNItolerance_neniPodleDalky()
    {
        // Tentyz absolutni vybocek 1,5 m znamena neco jineho blizko a neco jineho daleko:
        // na 1 m je tolerance 0,26 m (vybocek 5,7x mimo -> hruby outlier, potlacit),
        // na 10 m je tolerance 1,60 m (vybocek 0,9x -> v poradku, respektovat).
        //
        // Meri se REZIDUUM VE VYBOCUJICIM BODU: kdyz je bod potlaceny, primka zustane u ostatnich
        // a rezidum tam zustane velke; kdyz je respektovany, primka se k nemu prihne a rezidum
        // klesne. Ne odstup od pocatku - do toho se michá pakovy efekt polohy bodu.
        Func<Point2D, double> tol = p => 0.10 + 0.15 * Math.Sqrt(p.X * p.X + p.Y * p.Y);

        var nearOut = new Point2D(1.0, 1.5);
        var farOut = new Point2D(10.0, 1.5);

        var near = OnLine(0.0, 0.0, count: 40); near.Add(nearOut);
        var far = OnLine(0.0, 0.0, count: 40); far.Add(farOut);

        double resNear = LineFit.Fit(near, LineFitMode.OrthogonalHuber, tol, huberK: 1.5).Distance(nearOut);
        double resFar = LineFit.Fit(far, LineFitMode.OrthogonalHuber, tol, huberK: 1.5).Distance(farOut);

        TestContext.Out.WriteLine($"rezidum ve vybocujicim bodu: blizky {resNear:F4} m, vzdaleny {resFar:F4} m");
        Assert.That(resNear, Is.GreaterThan(resFar),
                    "blizky bod mimo svoji toleranci musi byt potlacen vic nez vzdaleny ve svoji tolerandi");
    }

    /// <summary>
    /// <b>Jadro vady, ktera stala 18 mm v sirce cesty.</b>
    ///
    /// <para>Odchylky hranicnich bodu od skutecneho okraje vozovky maji <b>zesikmene</b> rozdeleni:
    /// median sedi na okraji, ale je tam dlouhy chvost VEN z cesty (nameřeno 24. 8. 2026 nad
    /// OSM/SyntetickyRovny.osm: median leve hranice −1,8 mm, ale prumer +2,4 mm; prave +0,9 vs
    /// +10,9 mm). Metoda nejmensich kvadratu sleduje <b>prumer</b>, takze prolozena hranice lezi
    /// ven a cesta se jevi sirsi - 2,018 m proti skutecnym 2,000.</para>
    ///
    /// <para>Test to reprodukuje na cistem prikladu a hlida, ze robustni prolozeni to napravi.
    /// Nad zaznamem to srazilo chybu sirky ze 17,6 na 6,1 mm.</para>
    /// </summary>
    [Test]
    public void ZesikmenySum_vychyliNejmensiKvadraty_aleNeHuberSMAD()
    {
        // Body na primce y=0, sum zesikmeny: vetsina presne, mensina vyrazne na JEDNU stranu.
        // Median zustava 0, prumer je posunuty - presne jako u skutecnych hranicnich bodu.
        var rnd = new Random(20260824);
        var pts = new List<Point2D>();
        for (int i = 0; i < 200; i++)
        {
            double x = 1.0 + i * 0.05;
            double e = rnd.NextDouble() < 0.15 ? 0.05 + rnd.NextDouble() * 0.10   // chvost jen nahoru
                                               : (rnd.NextDouble() - 0.5) * 0.01;
            pts.Add(new Point2D(x, e));
        }

        double lsOffset = Math.Abs(Line2D.LinearRegesion(pts).Distance(new Point2D(0, 0)));
        double madOffset = Math.Abs(LineFit.Fit(pts, LineFitMode.OrthogonalHuber,
                                                tolerance: null, huberK: 1.5)
                                    .Distance(new Point2D(0, 0)));

        // A pro srovnani Huber s TOLERANCI, ktera je volna - ta nezabere vubec (to byla vada
        // prvni implementace: prah 0,10+0,15*r je v centimetrovych reziduich nedosazitelny).
        Func<Point2D, double> loose = p => 0.10 + 0.15 * Math.Sqrt(p.X * p.X + p.Y * p.Y);
        double tolOffset = Math.Abs(LineFit.Fit(pts, LineFitMode.OrthogonalHuber, loose, 1.5)
                                    .Distance(new Point2D(0, 0)));

        TestContext.Out.WriteLine($"odchylka primky od pravdy: nejmensi kvadraty {lsOffset * 1000:F1} mm, "
                                  + $"Huber s toleranci {tolOffset * 1000:F1} mm, Huber s MAD {madOffset * 1000:F1} mm");

        Assert.Multiple(() =>
        {
            Assert.That(lsOffset, Is.GreaterThan(0.005), "nejmensi kvadraty MUSI byt vychylene (jinak test nic nemeri)");
            Assert.That(madOffset, Is.LessThan(lsOffset / 2), "Huber s MAD musi vychyleni vyrazne srazit");
            Assert.That(tolOffset, Is.EqualTo(lsOffset).Within(0.004),
                        "Huber s volnou toleranci nezabere - je to skoro totez jako nevazene");
        });
    }

    /// <summary>
    /// <b>L1 cili MEDIAN, nejmensi kvadraty prumer</b> — a u zesikmeneho sumu je to cely rozdil.
    /// Test to overuje na sade, kde median a prumer znam presne.
    ///
    /// <para>Nad zaznamem to je rozdil mezi vychylenim sirky 17,6 mm (nejmensi kvadraty)
    /// a 1,4 mm (L1). Viz doc/map-correlation-localization.md.</para>
    /// </summary>
    [Test]
    public void L1_cili_median_ne_prumer()
    {
        // 80 bodu presne na primce y=0, 20 bodu 70 mm nad ni.
        //   median odchylek = 0      -> L1 ma dat primku na y = 0
        //   prumer odchylek = 14 mm  -> nejmensi kvadraty daji y = 0,014
        var pts = new List<Point2D>();
        for (int i = 0; i < 80; i++) pts.Add(new Point2D(1.0 + i * 0.1, 0.0));
        for (int i = 0; i < 20; i++) pts.Add(new Point2D(1.0 + i * 0.4, 0.070));

        double ls = Line2D.LinearRegesion(pts).Distance(new Point2D(0, 0));
        double l1 = LineFit.Fit(pts, LineFitMode.OrthogonalL1).Distance(new Point2D(0, 0));
        double tukey = LineFit.Fit(pts, LineFitMode.OrthogonalTukey).Distance(new Point2D(0, 0));

        TestContext.Out.WriteLine($"odstup primky od pravdy: LS {ls * 1000:F1} mm, "
                                  + $"L1 {l1 * 1000:F1} mm, Tukey {tukey * 1000:F1} mm "
                                  + "(prumer odchylek je 14 mm, median 0)");

        Assert.Multiple(() =>
        {
            Assert.That(ls, Is.EqualTo(0.014).Within(0.004), "LS musi sednout na PRUMER odchylek");
            Assert.That(l1, Is.LessThan(0.004), "L1 musi sednout na MEDIAN, tedy na nulu");
            Assert.That(tukey, Is.LessThan(0.004), "Tukey chvost utne, takze taky na nulu");
        });
    }

    [Test]
    public void Fit_vychoziRezimJeStaraRegrese()
    {
        // Pojistka, ze prepnuti rezimu je opravdu OPT-IN: LeastSquares musi vratit presne to,
        // co vracela puvodni Line2D.LinearRegesion, bit za bit.
        var rnd = new Random(9);
        var pts = OnLine(20 * Math.PI / 180, 0.3, 30)
                  .Select(p => new Point2D(p.X + (rnd.NextDouble() - 0.5) * 0.2,
                                           p.Y + (rnd.NextDouble() - 0.5) * 0.2)).ToList();

        var viaFit = LineFit.Fit(pts, LineFitMode.LeastSquares);
        var direct = Line2D.LinearRegesion(pts);

        Assert.That(viaFit.A, Is.EqualTo(direct.A).Within(1e-12));
        Assert.That(viaFit.B, Is.EqualTo(direct.B).Within(1e-12));
        Assert.That(viaFit.C, Is.EqualTo(direct.C).Within(1e-12));
    }

    [Test]
    public void MaloBodu_neboJedenBod_vratiNull()
    {
        Assert.That(LineFit.Fit(new List<Point2D>(), LineFitMode.Orthogonal), Is.Null);
        Assert.That(LineFit.Fit(new List<Point2D> { new Point2D(1, 1) }, LineFitMode.Orthogonal), Is.Null);
    }

    [Test]
    public void VsechnyBodyVJednomMiste_vratiNullMistoVodorovnePrimky()
    {
        // atan2(0,0) je 0, takze naivni implementace by tvrdila "vodorovna primka" - tichá lez.
        var pts = Enumerable.Range(0, 10).Select(_ => new Point2D(2.0, 3.0)).ToList();
        Assert.That(LineFit.Orthogonal(pts, null), Is.Null);
    }
}
