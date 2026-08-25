using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Kolik z těch měření je NEZÁVISLÝCH?</b> Autokorelace chybové posloupnosti korelátoru
    /// a z ní odhad, o kolik si fúze věří víc, než jí patří.
    ///
    /// <para><b>Nacpak.</b> Grid drzi ~2,5 s historie a korelator jede na 2 Hz, takze sousedni
    /// cykly koreluji z VELKE CASTI TEHOZ nahromadeneho oblaku. Jejich chyby proto nejsou nezavisle
    /// — ale fuze je jako nezavisle bere. Kovariance se pak zuzuje jako <c>1/sqrt(N)</c>, zatimco
    /// informace roste jen jako <c>1/sqrt(N_eff)</c>. To je druha polovina otevreneho ukolu
    /// „honestni sigma": chyba neni jen v HODNOTE sigma, ale i v POCTU merenii, kterymi se deli.
    /// Viz doc/map-correlation-localization.md, „Casova korelace mezi cykly".</para>
    ///
    /// <para><b>Proc slotovana autokorelace.</b> Prijate cykly nejsou vzorkovane rovnomerne —
    /// zamitnute cykly delaji v posloupnosti dury. Naivni „posun o index" by tedy michal lag 0,5 s
    /// s lagem 2 s. Kazdy par se proto zaradi podle SKUTECNEHO casoveho odstupu do slotu sirky
    /// jedne periody; pary, ktere do zadneho slotu nepadnou, se zahodi misto aby se pretlacily.</para>
    /// </summary>
    public static class TimeCorrelationReport
    {
        /// <summary>Nejmensi pocet cyklu, ze ktereho ma smysl autokorelaci pocitat.</summary>
        private const int MinCycles = 12;

        /// <summary>
        /// Do jakeho casoveho odstupu se autokorelace jeste pocita [s].
        ///
        /// <para><b>Nesmi to byt maly.</b> Pri prvnim mereni (25. 8. 2026) bylo 8 s a korelace se
        /// v tom okne VUBEC nerozpadla — VIF se pak utal na hranici okna a tvaril se jako hotove
        /// cislo, i kdyz byl jen dolni hranici. Proto se okno bere velke a
        /// <see cref="Result.DecayedWithinWindow"/> hlasi, jestli vubec doslo k rozpadu.</para>
        /// </summary>
        private const double MaxLagSeconds = 30.0;

        /// <summary>Lag se nepocita z paru, kterych je mene nez tolik — bylo by to sum.</summary>
        private const int MinPairsPerLag = 8;

        /// <summary>Vysledek — vse, co z autokorelace plyne.</summary>
        public sealed class Result
        {
            /// <summary>Perioda cyklu (median odstupu sousednich prijatych cyklu) [s].</summary>
            public double PeriodS;

            /// <summary>Autokorelace podle lagu; index = lag v periodach (0 = 1 perioda).</summary>
            public double[] Rho = Array.Empty<double>();

            /// <summary>Kolik paru pripadlo na kazdy lag (spolehlivost odhadu).</summary>
            public int[] Pairs = Array.Empty<int>();

            /// <summary>
            /// <b>Cinitel nadsazeni informace</b> = <c>1 + 2·Σρ(L)</c> pres pocatecni KLADNOU
            /// posloupnost. Rovna se poctu merenii, ktera nesou informaci jednoho nezavisleho —
            /// tedy tomu, cim se ma delit pocet merenii (nebo <c>sqrt</c> z ceho nasobit sigma).
            /// </summary>
            public double Vif;

            /// <summary>Dekorelacni cas = <see cref="Vif"/> × perioda [s].</summary>
            public double DecorrelationS => Vif * PeriodS;

            /// <summary>Kde se pocatecni kladna posloupnost utala (lag v periodach); 0 = hned.</summary>
            public int VifTruncatedAtLag;

            /// <summary>
            /// Rozpadla se korelace jeste v MERENEM okne? Kdyz <c>false</c>, je <see cref="Vif"/>
            /// jen <b>dolni hranici</b> — soucet se utal proto, ze skoncila data, ne proto, ze
            /// korelace zmizela. Bez tohoto priznaku by se dolni hranice cetla jako vysledek.
            /// </summary>
            public bool DecayedWithinWindow;

            /// <summary>Nejdelsi lag, ktery se jeste dal spocitat [s].</summary>
            public double MeasuredWindowS;

            /// <summary>
            /// Prumerna korelace VSECH paru — jen tahle velicina ridi zkresleni vyberoveho
            /// rozptylu, protoze vetsina paru je daleko od sebe.
            /// </summary>
            public double RhoBar;

            /// <summary>Efektivni pocet nezavislych merenii v celem useku (pro odhad prumeru).</summary>
            public double EffectiveCount;

            /// <summary>Da se <see cref="EffectiveCount"/> verit? Pri zaporne <see cref="RhoBar"/>
            /// (kratka rada, trend) vzorec neplati — viz komentar u vypoctu.</summary>
            public bool EffectiveCountValid;

            /// <summary>Pocet prijatych cyklu, ze kterych se to pocitalo.</summary>
            public int Count;
        }

        /// <summary>
        /// Spocte autokorelaci chybove posloupnosti. <paramref name="times"/> a <paramref name="errors"/>
        /// musi byt stejne dlouhe a setridene v case.
        /// </summary>
        public static Result Compute(IReadOnlyList<double> times, IReadOnlyList<double> errors)
        {
            if (times == null) throw new ArgumentNullException(nameof(times));
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (times.Count != errors.Count)
                throw new ArgumentException("times a errors musi mit stejnou delku.");

            int n = times.Count;
            var r = new Result { Count = n };
            if (n < MinCycles) return r;

            // Perioda z MEDIANU odstupu, ne z prumeru: zamitnute cykly delaji v posloupnosti dury
            // a prumer by je rozmazal do periody, ktera nikde neni.
            var gaps = new List<double>(n - 1);
            for (int i = 1; i < n; i++) gaps.Add(times[i] - times[i - 1]);
            gaps.Sort();
            r.PeriodS = gaps[gaps.Count / 2];
            if (!(r.PeriodS > 0)) return r;

            double mean = errors.Average();
            double var0 = 0;
            for (int i = 0; i < n; i++) var0 += (errors[i] - mean) * (errors[i] - mean);
            var0 /= n;
            if (!(var0 > 0)) return r;

            int maxLag = Math.Max(1, (int)Math.Round(MaxLagSeconds / r.PeriodS));
            var rho = new double[maxLag];
            var pairs = new int[maxLag];
            var sums = new double[maxLag];

            // Slotovani podle SKUTECNEHO odstupu. Zaroven se posbira soucet korelaci VSECH paru
            // (rhoBar), ktery je potreba na zkresleni vyberoveho rozptylu; pary za maxLag se do nej
            // pocitaji jako nulove — po dekorelacnim case uz tam nic byt nema, a kdyby bylo, ukaze
            // to sam vypis rho.
            double allPairsSum = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dt = times[j] - times[i];
                    int slot = (int)Math.Round(dt / r.PeriodS) - 1;
                    if (slot < 0 || slot >= maxLag) continue;
                    // Slot sirky jedne periody; par mimo nej by michal lagy.
                    if (Math.Abs(dt - (slot + 1) * r.PeriodS) > r.PeriodS / 2) continue;

                    double c = (errors[i] - mean) * (errors[j] - mean);
                    sums[slot] += c;
                    pairs[slot]++;
                }
            }

            double lastMeasured = 0;
            for (int L = 0; L < maxLag; L++)
            {
                bool enough = pairs[L] >= MinPairsPerLag;
                rho[L] = enough ? (sums[L] / pairs[L]) / var0 : double.NaN;
                if (pairs[L] > 0) allPairsSum += sums[L] / var0;
                if (enough) lastMeasured = (L + 1) * r.PeriodS;
            }
            r.Rho = rho;
            r.Pairs = pairs;
            r.MeasuredWindowS = lastMeasured;

            // VIF = 1 + 2*sum rho po POCATECNI KLADNOU posloupnost. Utnuti u prvniho nekladneho
            // clenu je standardni postup: dal uz je rho samy sum a scitat ho znamena scitat nahodu.
            //
            // POZOR NA ROZDIL DVOU DUVODU UTNUTI: kdyz se utne na NEKLADNEM rho, korelace skutecne
            // vyprsela a VIF je vysledek. Kdyz se utne proto, ze DOSLA DATA (rho uz nejde spocitat),
            // je VIF jen DOLNI HRANICE. Prvni mereni 25. 8. 2026 skoncilo presne timhle druhym
            // pripadem, a bez rozliseni by se cetlo jako hotove cislo.
            double sum = 0;
            int cut = maxLag;
            bool decayed = false;
            for (int L = 0; L < maxLag; L++)
            {
                if (double.IsNaN(rho[L])) { cut = L; break; }          // dosla data
                if (rho[L] <= 0) { cut = L; decayed = true; break; }   // korelace vyprsela
                sum += rho[L];
            }
            r.VifTruncatedAtLag = cut;
            r.DecayedWithinWindow = decayed;
            r.Vif = 1.0 + 2.0 * sum;

            // rhoBar = prumer korelace pres VSECHNY pary (jich je n(n-1)/2).
            r.RhoBar = allPairsSum / (0.5 * n * (n - 1));

            // Efektivni pocet: n / (1 + (n-1)*rhoBar). Pri ZAPORNE rhoBar (kratka rada, trend,
            // antikorelace na dlouhych lagech) jde jmenovatel k nule i pod ni a vzorec ztraci smysl
            // - nezavislych merenii nemuze byt vic nez merenii samych. Proto se to STRIHA a hlasi;
            // tise vratit 1,3e10 (co se stalo pri prvnim mereni) je horsi nez priznat, ze vzorec
            // v tomhle rezimu neplati.
            double denom = 1.0 + (n - 1) * r.RhoBar;
            r.EffectiveCountValid = denom > 1e-3;
            r.EffectiveCount = r.EffectiveCountValid ? Math.Min(n, n / denom) : n;
            return r;
        }

        /// <summary>Vytiskne rozbor; <paramref name="reportedSigma"/> a <paramref name="rawSd"/>
        /// slouzi k prepoctu poctivosti. <paramref name="series"/> se vytiskne jako posloupnost —
        /// bez ni nejde poznat, jestli je vysoka autokorelace „pomaly sum", nebo „skoro konstantni
        /// bias s obcasnym vyskokem", a to jsou uplne jine vady.</summary>
        public static void Print(Result r, double reportedSigma, double rawSd,
                                 IReadOnlyList<double> times = null, IReadOnlyList<double> series = null)
        {
            if (times != null && series != null && series.Count > 0 && series.Count == times.Count)
            {
                Console.WriteLine("CHYBA CYKLUS PO CYKLU (podel tesne osy) — tvar posloupnosti:");
                double lo = series.Min(), hi = series.Max();
                double span = Math.Max(1e-9, hi - lo);
                for (int i = 0; i < series.Count; i++)
                {
                    int col = (int)Math.Round((series[i] - lo) / span * 48);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,6:F2} s  {1,7:F3} m  |{2}*", times[i], series[i], new string(' ', col)));
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  (osa {0:F3} .. {1:F3} m)", lo, hi));
                Console.WriteLine();
            }

            Console.WriteLine("CASOVA KORELACE MEZI CYKLY — kolik z tech merenii je NEZAVISLYCH?");
            if (r.Count < MinCycles || r.Rho.Length == 0)
            {
                Console.WriteLine($"  prilis malo prijatych cyklu ({r.Count}) — autokorelace by byla sum.");
                Console.WriteLine();
                return;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  perioda cyklu (median):   {0:F3} s   ({1} prijatych cyklu)", r.PeriodS, r.Count));
            Console.WriteLine("  autokorelace chyby podel tesne osy:");
            for (int L = 0; L < r.Rho.Length; L++)
            {
                if (r.Pairs[L] == 0) continue;
                double lagS = (L + 1) * r.PeriodS;
                string bar = double.IsNaN(r.Rho[L])
                    ? ""
                    : new string('#', Math.Max(0, (int)Math.Round(r.Rho[L] * 40)));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    lag {0,2} ({1,5:F2} s)  rho={2,6:F3}  paru={3,4}  {4}",
                    L + 1, lagS, r.Rho[L], r.Pairs[L], bar));
            }
            Console.WriteLine();

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  cinitel nadsazeni informace (1+2*sum rho): {0,6:F2}{1}",
                r.Vif, r.DecayedWithinWindow ? "" : "   <<< DOLNI HRANICE"));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  dekorelacni cas:                           {0,6:F2} s{1}",
                r.DecorrelationS, r.DecayedWithinWindow ? "" : "   <<< DOLNI HRANICE"));
            if (!r.DecayedWithinWindow)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  ⚠️ Korelace se v merenem okne ({0:F1} s, lagy do {1}) VUBEC NEROZPADLA — soucet",
                    r.MeasuredWindowS, r.VifTruncatedAtLag));
                Console.WriteLine("     se utal proto, ze DOSLA DATA, ne proto, ze korelace vyprsela.");
                Console.WriteLine("     Skutecna hodnota je vyssi; potreba DELSI usek.");
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  => sigma by mela byt {0,4:F2}x vetsi, NEBO merit {1,4:F2}x rideji{2}",
                Math.Sqrt(Math.Max(1.0, r.Vif)), Math.Max(1.0, r.Vif),
                r.DecayedWithinWindow ? "" : " (aspon)"));
            Console.WriteLine();

            // Zkresleni VYBEROVEHO rozptylu: odchylky se meri od VLASTNIHO prumeru, a ten je pri
            // korelovanych datech blizsi datum, nez by mel byt. Namereny rozptyl je proto MENSI
            // nez skutecny — tedy poctivost sigmy je HORSI, nez rikal puvodni pomer.
            double corr = Math.Sqrt(Math.Max(1e-9, 1.0 - r.RhoBar));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  prumerna korelace vsech paru (rho_bar):    {0,6:F4}", r.RhoBar));
            Console.WriteLine(r.EffectiveCountValid
                ? string.Format(CultureInfo.InvariantCulture,
                    "  efektivni pocet nezavislych merenii:       {0,6:F1} z {1}", r.EffectiveCount, r.Count)
                : "  efektivni pocet nezavislych merenii:       NELZE (rho_bar <= 0 - kratka rada nebo trend)");
            if (rawSd > 0 && reportedSigma > 0)
            {
                double fixedSd = rawSd / corr;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  rozptyl po opravě zkreslení:               {0,6:F4} m  (merene {1:F4} m)",
                    fixedSd, rawSd));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  poctivost sigmy po opravě:                 {0,6:F2}x (bylo {1:F2}x)",
                    fixedSd / reportedSigma, rawSd / reportedSigma));
            }
            Console.WriteLine();
            Console.WriteLine("  Dve RUZNE vady, at se nesmichaji: cinitel nadsazeni informace mluvi o tom,");
            Console.WriteLine("  jak fuze SCITA merenia (posila se jich vic, nez kolik jich je nezavislych);");
            Console.WriteLine("  oprava zkresleni mluvi o tom, ze i JEDNO merenie ma vetsi rozptyl, nez se");
            Console.WriteLine("  z korelovaneho vzorku zdalo. Prvni resi frekvence nebo skala sigma, druha ne.");
            Console.WriteLine();
        }
    }
}
