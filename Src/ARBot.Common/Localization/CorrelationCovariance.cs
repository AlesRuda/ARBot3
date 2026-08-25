using System;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Kovariance odhadu korelace, spoctena ze ZAKRIVENI skore v maximu:
    /// <c>C = -Alpha * H^-1</c>, kde H je Hessian skore. Viz doc/map-correlation-localization.md.
    ///
    /// <para><b>Proc takhle:</b> translacni blok se rozlozi na vlastni osy, takze na prime ceste
    /// vyjde jedna sigma mala (napric) a druha velka (podel) SAMA - nic se nedetekuje ani
    /// neprepina. U odbocky se sevrou obe.</para>
    ///
    /// <para><b>Skore neni log-verohodnost</b>, takze zakriveni ma spravny TVAR, ne absolutni skalu.
    /// Tu resi kalibracni <see cref="MapCorrelatorConfig.Alpha"/> (ladi se ve fazi 4). POZOR: skore
    /// je "tent" (<c>S ~ 1 - k*|d|</c>), takze zakriveni je ~ <c>1/h</c> a sigma ~ <c>sqrt(h)</c> -
    /// absolutni skala zavisi i na <see cref="MapCorrelatorConfig.HessianStepM"/>. Obe se proto
    /// ladi SPOLU a zmena kroku prepocita vsechny sigmy. <b>Se zapnutou honestni sigmou
    /// (<see cref="MapCorrelatorConfig.ReferenceInformativeEvidence"/>) tato past MIZI</b>:
    /// informativniho dukazu je take ~<c>h</c>, takze se obe zavislosti vykrati (drzi test
    /// <c>HonestniSigma_ReferenceNezavisiNaKrokuDerivace</c>).</para>
    ///
    /// <para><b>ZNAMA VADA (otevreny ukol):</b> na ceste POD UHLEM k osam gridu vychazi podelne
    /// zakriveni nenulove, takze se hlasi FALESNA podelna jistota (namereno 0,18 m na sikme prime
    /// ceste, coz je "jisteji" nez skutecna T-krizovatka s 0,29 m). Pricina: skore neni lokalne
    /// kvadraticke, takze fit kvadraticke formy je principialne nepresny. Podrobne vcetne
    /// namerenych dat a dvou neuspesnych oprav v doc/map-correlation-localization.md, sekce
    /// Otevrene ukoly.</para>
    /// </summary>
    public readonly struct CorrelationCovariance
    {
        /// <summary>Sigma LEPE urcene osy posunu [m] (na ceste typicky napric).</summary>
        public double SigmaTight { get; }

        /// <summary>Sigma HORE urcene osy posunu [m] (na prime ceste podel).</summary>
        public double SigmaLoose { get; }

        /// <summary>Smer lepe urcene osy [rad], matematicky (0 = vychod).</summary>
        public double TightAxisAngle { get; }

        /// <summary>Marginalni sigma kurzu [rad] - vazba kurz &lt;-&gt; translace je vymarginalizovana
        /// v OBOU vetvich vypoctu, ne jen v te s inverzi.</summary>
        public double SigmaPhi { get; }

        /// <summary>
        /// Je vysledek pouzitelny? <c>false</c> jen pri skutecne degeneraci TRANSLACNIHO bloku
        /// (zadne zakriveni v zadnem smeru, nebo obracene znamenko). Nulove zakriveni v JEDNOM
        /// smeru <c>false</c> NEDAVA - to je na prime ceste normalni stav a resi ho nekonecna
        /// sigma te osy. Spatne zakriveni kurzu taky zahodi jen korekci kurzu, ne cely vysledek.
        /// </summary>
        public bool HasPeak { get; }

        /// <summary>Dukaz, ktery ROZLISUJE mezi kandidaty [m² · log-odds] - diagnostika
        /// k „honestni sigme"; viz <see cref="CorrelationScorer.InformativeEvidence"/>.</summary>
        public double InformativeEvidence { get; }

        private CorrelationCovariance(double sigmaTight, double sigmaLoose, double tightAxisAngle,
                                      double sigmaPhi, bool hasPeak, double informativeEvidence = 0)
        {
            SigmaTight = sigmaTight;
            SigmaLoose = sigmaLoose;
            TightAxisAngle = tightAxisAngle;
            SigmaPhi = sigmaPhi;
            HasPeak = hasPeak;
            InformativeEvidence = informativeEvidence;
        }

        /// <summary>Vysledek "zadne pouzitelne maximum" - volajici ma mlcet.</summary>
        public static CorrelationCovariance NoPeak()
            => new CorrelationCovariance(double.PositiveInfinity, double.PositiveInfinity, 0.0,
                                         double.PositiveInfinity, false);

        /// <summary>
        /// Kovariance se zadanymi hodnotami - JEN PRO TESTY pravidel nad vysledkem, aby nemusely
        /// stavet cely oblak a rastr. V provozu se pouziva <see cref="Estimate"/>.
        /// </summary>
        public static CorrelationCovariance ForTest(double sigmaTight, double sigmaLoose,
                                                    double tightAxisAngle, double sigmaPhi)
            => new CorrelationCovariance(sigmaTight, sigmaLoose, tightAxisAngle, sigmaPhi, hasPeak: true);

        /// <summary>
        /// Spocte kovarianci numerickou druhou derivaci skore okolo maxima.
        /// </summary>
        /// <param name="scorer">Skorovaci funkce (tentyz oblak i rastr jako pri skenovani).</param>
        /// <param name="peak">Nalezene maximum.</param>
        /// <param name="cfg">Kroky derivace, kalibrace a hranice sigma.</param>
        public static CorrelationCovariance Estimate(CorrelationScorer scorer, ScanResult peak,
                                                     MapCorrelatorConfig cfg)
        {
            if (scorer == null) throw new ArgumentNullException(nameof(scorer));
            if (peak == null) throw new ArgumentNullException(nameof(peak));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            double x = peak.Dx, y = peak.Dy, p = peak.Phi;
            double h = cfg.HessianStepM, hp = cfg.HessianStepHeadingRad;

            double S(double dx, double dy, double dphi) => scorer.Score(dx, dy, dphi, 1);

            // HONESTNI SIGMA: skore je normovany podil, takze o mnozstvi dukazu nevi nic a sigma
            // z jeho zakriveni taky ne. Kompenzuje se to tim, ze se Alpha skaluje podle mnozstvi
            // INFORMATIVNIHO dukazu - bunek, ktere skutecne rozlisuji mezi kandidaty, merenych ve
            // FYZIKALNICH jednotkach [m² * log-odds]. Sigma pak roste jako 1/sqrt(E_inf), tedy jako
            // smerodatna odchylka podilu.
            // Nula v konfiguraci = skalovani vypnuto (puvodni chovani).
            // Pocita se VZDY, i kdyz je skalovani vypnute: je to diagnostika, bez ktere nejde
            // referencni hodnotu vubec zvolit, a stoji jeden pruchod oblakem proti stovkam,
            // ktere uz udelalo skenovani.
            double eInf = scorer.InformativeEvidence(x, y, p, h);

            double alpha = cfg.Alpha;
            if (cfg.ReferenceInformativeEvidence > 0)
            {
                // Podlaha: bez informativniho dukazu neni sigma velka, ale NEDEFINOVANA. Nechat
                // deleni nulou vybuchnout by bylo horsi nez vratit hodne velkou sigma, kterou
                // stejne zahodi strop.
                alpha = cfg.Alpha * (cfg.ReferenceInformativeEvidence / Math.Max(1e-9, eInf));
            }

            double s0 = S(x, y, p);

            double sxx = (S(x + h, y, p) - 2 * s0 + S(x - h, y, p)) / (h * h);
            double syy = (S(x, y + h, p) - 2 * s0 + S(x, y - h, p)) / (h * h);
            double spp = (S(x, y, p + hp) - 2 * s0 + S(x, y, p - hp)) / (hp * hp);

            double sxy = (S(x + h, y + h, p) - S(x + h, y - h, p)
                          - S(x - h, y + h, p) + S(x - h, y - h, p)) / (4 * h * h);
            double sxp = (S(x + h, y, p + hp) - S(x + h, y, p - hp)
                          - S(x - h, y, p + hp) + S(x - h, y, p - hp)) / (4 * h * hp);
            double syp = (S(x, y + h, p + hp) - S(x, y + h, p - hp)
                          - S(x, y - h, p + hp) + S(x, y - h, p - hp)) / (4 * h * hp);

            var negH = Matrix<double>.Build.DenseOfArray(new[,]
            {
                { -sxx, -sxy, -sxp },
                { -sxy, -syy, -syp },
                { -sxp, -syp, -spp },
            });

            // Test pozitivni definitnosti. Chyti se JEN Cholesky - `Inverse()` je schvalne az za
            // try, aby jeho pripadne selhani vybublalo jako chyba a netvarilo se jako
            // semidefinitnost. (Spolknout neocekavanou vyjimku a tise zmenit vetev vypoctu je
            // presne ten druh chyby, ktery se pak hleda tyden.)
            bool positiveDefinite;
            try
            {
                negH.Cholesky();
                positiveDefinite = true;
            }
            catch (Exception)
            {
                positiveDefinite = false;
            }

            // IDEALNI CESTA: -H je pozitivne definitni, da se invertovat a sigma kurzu vyjde
            // MARGINALNI (vazba phi <-> translace zohlednena).
            if (positiveDefinite)
                return FromCovariance(alpha * negH.Inverse(), cfg, eInf);

            // DEGRADOVANA CESTA. Na PRIME ceste je singularni -H NORMALNI STAV: posun podel cesty
            // nemeni nic, co robot vidi, takze podelna druha derivace je PRESNE nula. Zahodit kvuli
            // tomu cely vysledek by znamenalo neposlat na prime ceste NIC - a pricna korekce je
            // hlavni vystup cele funkce. Viz doc/map-correlation-localization.md.
            return FromCurvature(-sxx, -sxy, -syy, -spp, -sxp, -syp, cfg, alpha, eInf);
        }

        /// <summary>Idealni pripad: sigma z KOVARIANCE (mensi vlastni cislo = lepe urcena osa).</summary>
        private static CorrelationCovariance FromCovariance(Matrix<double> c, MapCorrelatorConfig cfg, double eInf)
        {
            var e = Eigen2(c[0, 0], c[0, 1], c[1, 1]);
            // Nemelo by nastat: kdyz Cholesky prosla, je -H (a tedy i C) pozitivne definitni.
            if (e.Min <= 0 || double.IsNaN(e.Min) || double.IsNaN(e.Max)) return NoPeak();

            double sigmaPhi = c[2, 2] > 0
                ? Math.Max(Math.Sqrt(c[2, 2]), cfg.SigmaFloorHeadingRad)
                : double.PositiveInfinity;

            return new CorrelationCovariance(
                Math.Max(Math.Sqrt(e.Min), cfg.SigmaFloorM),
                Math.Max(Math.Sqrt(e.Max), cfg.SigmaFloorM),
                e.MinAngle, sigmaPhi, hasPeak: true, informativeEvidence: eInf);
        }

        /// <summary>
        /// Degradovany pripad: sigma ze ZAKRIVENI (vetsi vlastni cislo = lepe urcena osa), takze
        /// nulove zakriveni da nekonecnou sigmu misto zahozeni celeho vysledku.
        ///
        /// <para><b>Sigmy jsou MARGINALNI, stejne jako v idealni ceste</b> - druha promenna se
        /// vzdy vymarginalizuje Schurovym doplnkem. Brat sigmu primo z bloku -H by dalo sigmu
        /// PODMINENOU, a ta je systematicky MENSI nez marginalni (Schuruv doplnek je <= A_tt).
        /// Prilis mala sigma je nebezpecna: fuze by korelatoru verila vic, nez si zaslouzi. Navic
        /// by sigma pri prepnuti vetve skocila.</para>
        /// </summary>
        /// <param name="axx">Prvky -H: translacni blok (axx, axy, ayy), kurz (app), vazba (axp, ayp).</param>
        private static CorrelationCovariance FromCurvature(double axx, double axy, double ayy,
                                                           double app, double axp, double ayp,
                                                           MapCorrelatorConfig cfg, double alpha, double eInf)
        {
            // Dva prahy "plocho", KAZDY VE SVYCH JEDNOTKACH: translace [skore/m^2], kurz
            // [skore/rad^2]. Michat je nelze - pri vychozich hodnotach se lisi 3283x.
            double tol = alpha / (cfg.SigmaCeilingM * cfg.SigmaCeilingM);
            double tolPhi = alpha / (cfg.SigmaCeilingHeadingRad * cfg.SigmaCeilingHeadingRad);

            // TRANSLACE: vymarginalizovat kurz. Kdyz je plochy i kurz, korekce se vynecha - jinak by
            // se delilo skoro nulou.
            double mxx = axx, mxy = axy, myy = ayy;
            if (app > tolPhi)
            {
                mxx -= axp * axp / app;
                mxy -= axp * ayp / app;
                myy -= ayp * ayp / app;
            }
            else if (Math.Sqrt(axp * axp + ayp * ayp) > Math.Sqrt(tol * tolPhi))
            {
                // Vynechani korekce se opira o "kdyz je kurz plochy, je nulova i vazba" - a to plati
                // jen pro PSD matici. V TETO vetvi ale jsme prave proto, ze Cholesky na -H spadla,
                // takze PSD predpokladat nelze. Pri app ~ 0 a nenulove vazbe skutecny Schuruv
                // doplnek DIVERGUJE (translace je uplne neurcena), zatimco vynechanim korekce by se
                // ohlasila plna translacni jistota - tedy chyba presne tim nebezpecnym smerem.
                // Prah je geometricky stred obou toleranci, protoze vazba ma smisene jednotky
                // [skore/(m*rad)]: sqrt(tol * tolPhi) je jediny prah, ktery v nich vychazi.
                return NoPeak();
            }

            var e = Eigen2(mxx, mxy, myy);

            // Zadne zakriveni v ZADNEM smeru = zadne maximum (prazdny grid, sum).
            if (!(e.Max > tol)) return NoPeak();
            // Zakriveni obracene na spatnou stranu = sedlo nebo minimum, ne maximum.
            if (e.Min < -tol) return NoPeak();

            double sigmaTight = Math.Max(Math.Sqrt(alpha / e.Max), cfg.SigmaFloorM);
            double sigmaLoose = e.Min > tol
                ? Math.Max(Math.Sqrt(alpha / e.Min), cfg.SigmaFloorM)
                : double.PositiveInfinity;

            // KURZ: vymarginalizovat translaci. Plochy smer se VYNECHA (pseudoinverze) - je to
            // spravne osetreni "v tom smeru nevim nic", ne deleni nulou.
            double reducedPhi = app - TranslationAbsorbs(axp, ayp, axx, axy, ayy, tol);
            double sigmaPhi = reducedPhi > tolPhi
                ? Math.Max(Math.Sqrt(alpha / reducedPhi), cfg.SigmaFloorHeadingRad)
                : double.PositiveInfinity;

            return new CorrelationCovariance(sigmaTight, sigmaLoose, e.MaxAngle, sigmaPhi,
                                             hasPeak: true, informativeEvidence: eInf);
        }

        /// <summary>
        /// Kolik informace o kurzu "spolkne" translace: <c>g^T * A_tt^+ * g</c>, kde <c>g</c> je
        /// vazba kurz-translace a <c>A_tt^+</c> pseudoinverze translacniho bloku. Smery se
        /// zakrivenim pod <paramref name="tol"/> se do souctu nezapocitavaji.
        /// </summary>
        private static double TranslationAbsorbs(double gx, double gy,
                                                 double axx, double axy, double ayy, double tol)
        {
            var e = Eigen2(axx, axy, ayy);
            double sum = 0.0;

            if (e.Max > tol)
            {
                double p = gx * Math.Cos(e.MaxAngle) + gy * Math.Sin(e.MaxAngle);
                sum += p * p / e.Max;
            }
            if (e.Min > tol)
            {
                double p = gx * Math.Cos(e.MinAngle) + gy * Math.Sin(e.MinAngle);
                sum += p * p / e.Min;
            }
            return sum;
        }

        /// <summary>Vysledek vlastniho rozkladu symetricke 2x2 matice.</summary>
        private readonly struct Eigen2Result
        {
            public readonly double Min, Max;
            /// <summary>Smer vlastniho vektoru k <see cref="Min"/> [rad].</summary>
            public readonly double MinAngle;
            /// <summary>Smer vlastniho vektoru k <see cref="Max"/> [rad].</summary>
            public readonly double MaxAngle;

            public Eigen2Result(double min, double max, double minAngle, double maxAngle)
            {
                Min = min; Max = max; MinAngle = minAngle; MaxAngle = maxAngle;
            }
        }

        /// <summary>Vlastni cisla a vektory symetricke 2x2 [[a,b],[b,d]] uzavrenym tvarem
        /// (deterministicke poradi, zadna zavislost na implementaci Evd).</summary>
        private static Eigen2Result Eigen2(double a, double b, double d)
        {
            double trace = a + d;
            double det = a * d - b * b;
            double disc = trace * trace - 4 * det;
            if (disc < 0) disc = 0;                 // numericky sum u skoro izotropniho pripadu
            double root = Math.Sqrt(disc);
            double max = 0.5 * (trace + root);
            double min = 0.5 * (trace - root);

            if (Math.Abs(b) > 1e-15)
                return new Eigen2Result(min, max, Math.Atan2(b, min - d), Math.Atan2(b, max - d));

            // Diagonalni pripad - osy jsou souradnicove.
            bool aIsMin = a <= d;
            return new Eigen2Result(min, max,
                                    aIsMin ? 0.0 : Math.PI / 2,
                                    aIsMin ? Math.PI / 2 : 0.0);
        }
    }
}
