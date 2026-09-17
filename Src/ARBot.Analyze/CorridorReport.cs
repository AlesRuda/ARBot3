using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// Rozbor <b>hranove lokalizace</b> ze zaznamu: proc cykly propadly, jak presna jsou prijata
    /// merenia a — hlavne — jak to vypada <b>v zavislosti na parovacim rozestupu snimku</b>.
    ///
    /// <para><b>Proc rozpad podle rozestupu.</b> Sirkovy nesouhlas vyskocil z 0,046 na 0,230 m
    /// a vypadalo to jako cena rozsireni parovaciho okna z 60 na 400 ms. Rozpad to vyvratil:
    /// <b>velky rozestup je lepsi</b>, ne horsi, a zlom je ostry u 120 ms — podpis zavadejici
    /// promenne. Tou promennou je misto na trase (faze kamer se prubehem behu posouva, takze
    /// pasma rozestupu jsou ve skutecnosti pasma casu). Skutecna pricina byla zaostavani filtru
    /// sirky na ceste, ktera se rozsiruje — proto tu je i rozpad podle <b>OSM cesty</b>, ktery to
    /// ukazal. Viz doc/map-correlation-localization.md.</para>
    ///
    /// <para><b>Rozestup v zaznamu neni</b> (<see cref="RoadCorridorMsg"/> ho nenese), ale jde
    /// spolehlive zrekonstruovat z indexu: <see cref="CorridorLocalizer"/> si drzi posledni snimek
    /// od kazde kamery a paruje jen <b>dozadu</b>, a pro kazdy zpracovany snimek vznikne prave
    /// jedna <see cref="RoadCorridorMsg"/>. Mnozina jejich casu je tedy presne mnozina snimku,
    /// ktere do stupne dosly (frontou DropOldest se cast snimku zahodi) — a nad ni se parovani
    /// prehraje presne.</para>
    /// </summary>
    public static class CorridorReport
    {
        public static void Run(RecordFile rec, double oldWindowMs)
        {
            var msgs = new List<RoadCorridorMsg>();
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "RoadCorridorMsg") continue;
                if (rec.Read(e) is RoadCorridorMsg m) msgs.Add(m);
            }

            Console.WriteLine($"RoadCorridorMsg: {msgs.Count} zprav");
            if (msgs.Count == 0) return;
            Console.WriteLine($"verze zpravy:    {string.Join(", ", msgs.Select(m => m.Verze).Distinct().OrderBy(x => x))}");
            Console.WriteLine($"delka useku:     {(msgs[msgs.Count - 1].TimeStamp - msgs[0].TimeStamp).TotalSeconds:F1} s");
            Console.WriteLine();

            Console.WriteLine("Duvod (FixReason):");
            foreach (var g in msgs.GroupBy(m => (CorridorFixReason)m.FixReason).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Key,-22} {g.Count(),5}  ({100.0 * g.Count() / msgs.Count,4:F1} %)");

            Console.WriteLine("Duvod koridoru (CorridorReason) tam, kde koridor nevznikl:");
            foreach (var g in msgs.Where(m => m.FixReason == (byte)CorridorFixReason.NoCorridor)
                                  .GroupBy(m => (CorridorReason)m.CorridorReason).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Key,-22} {g.Count(),5}");
            Console.WriteLine();

            var skew = PairingSkew(rec, msgs);
            var ok = msgs.Where(m => m.FixReason == (byte)CorridorFixReason.Ok).ToList();
            Console.WriteLine($"Prijatych merenii (Ok): {ok.Count}");
            Console.WriteLine($"Rozestup zrekonstruovan u {ok.Count(m => skew.ContainsKey(m.TimeStamp))} z nich");
            Console.WriteLine();

            // Doslo to do fuze? „Duvod = Ok" rika jen to, ze merenie PROSLO branami - pri
            // corridorsend=false se nikam neposila a pri plnem oknu ho fuze zahodi jako prilis
            // stare. Presne na tuhle zamenu uz jednou dolehla plosna korelace (telemetrie hlasila
            // Ok i ve chvili, kdy do fuze nedochazelo nic), viz RoadCorridorMsg.DroppedByFusion.
            int emLat = ok.Count(m => m.EmittedLateral);
            int emHead = ok.Count(m => m.EmittedHeading);
            long dropped = msgs.Max(m => m.DroppedByFusion);
            Console.WriteLine("Doslo to do fuze?");
            Console.WriteLine($"  poslana pricna korekce   {emLat,5} z {ok.Count} Ok");
            Console.WriteLine($"  poslana korekce kurzu    {emHead,5} z {ok.Count} Ok");
            Console.WriteLine($"  zahozeno fuzi (prilis stare) {dropped,5}");
            if (emLat == 0 && ok.Count > 0)
                Console.WriteLine("  POZOR: NIC SE NEPOSILALO - merici rezim (corridorsend=false), nebo skrceni corridorhz=.");
            Console.WriteLine();

            Funnel(msgs);
            LateralUngated(msgs);

            Report("VSECHNA prijata merenia", ok);

            var narrow = ok.Where(m => skew.TryGetValue(m.TimeStamp, out double s) && s <= oldWindowMs).ToList();
            var wide = ok.Where(m => skew.TryGetValue(m.TimeStamp, out double s) && s > oldWindowMs).ToList();
            Report($"jen rozestup do {oldWindowMs:F0} ms (co by proslo starym oknem)", narrow);
            Report($"jen rozestup nad {oldWindowMs:F0} ms (co pribylo rozsirenim)", wide);

            Console.WriteLine("Nesouhlas podle parovaciho rozestupu (hleda se TREND, ne jedno cislo):");
            Console.WriteLine("  pasmo [ms]      n   abs sirka p50   sirka p50   abs pricne p50   nerovnobez. p50 [deg]");
            double[] edges = { 0, 20, 60, 120, 200, 300, 400, double.MaxValue };
            for (int i = 0; i + 1 < edges.Length; i++)
            {
                double a = edges[i], b = edges[i + 1];
                var bin = ok.Where(m => skew.TryGetValue(m.TimeStamp, out double s) && s >= a && s < b).ToList();
                if (bin.Count == 0) continue;
                var aw = new Stats(""); var sw = new Stats(""); var al = new Stats(""); var pe = new Stats("");
                foreach (var m in bin)
                {
                    aw.Add(Math.Abs(m.WidthDisagreement)); sw.Add(m.WidthDisagreement);
                    al.Add(Math.Abs(m.LateralDisagreement)); pe.Add(Math.Abs(m.ParallelErrorRad) * 180 / Math.PI);
                }
                string label = b == double.MaxValue ? $"nad {a:F0}" : $"{a:F0}-{b:F0}";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,4}       {2,8:F3}    {3,8:F3}         {4,8:F3}            {5,8:F2}",
                    label, bin.Count, aw.Median, sw.Median, al.Median, pe.Median));
            }
            Console.WriteLine();

            AssociationScore(msgs);
            HeadingVsGps(rec, msgs);
            AssociationSigmas(rec, msgs);
            GeometryCheck(ok);
            ByPose(rec, ok, msgs);
            PrahInlieru(msgs);
        }

        /// <summary>
        /// <b>Co by udelal jiny prah inlieru?</b> <c>MinInliers</c> (dnes 25) zahazuje nejvic
        /// cyklu ze vsech bran — nad zaznamem ze 17. 9. 2026 to bylo 3 987 z 6 173. Prah je
        /// pritom naladeny na STARSIM zaznamu odjinud, takze otazka „je 25 spravne pro tenhle
        /// teren?" je legitimni — a da se zodpovedet BEZ noveho vyjezdu, protoze
        /// <see cref="RoadCorridorMsg"/> nese inliery i <b>usecky obou hranic</b> i u cyklu,
        /// ktere brana zamitla (<c>CorridorFinder</c> je uklada zamerne driv, nez zamita).
        ///
        /// <para><b>Nestaci spocitat, kolik cyklu by navic proslo</b> — to je ta lehka a
        /// zavadejici polovina odpovedi. Podstatne je, JESTLI JSOU DOBRE: prave proti tomu ten
        /// prah vznikl, protoze primky prolozene 3–6 body vychazeji kolmo na cestu (sirka az
        /// 10 m, smer −88°). Proto se u kazde varianty dopocita geometrie koridoru a tiskne se
        /// rozdeleni sirky — nesmyslna sirka je podpis prave te vady.</para>
        ///
        /// <para>⚠️ <b>Nejdriv se meridlo overi proti zname odpovedi</b>: tataz geometrie se
        /// dopocita i pro cykly, kde koridor VZNIKL, a porovna se s tim, co je ve zprave.
        /// Kdyz to nesedi, je vadna rekonstrukce a zbytek bloku nema cenu cist.</para>
        /// </summary>
        private static void PrahInlieru(List<RoadCorridorMsg> msgs)
        {
            Console.WriteLine("PRAH INLIERU - co by pustil jiny MinInliers? (dnes 25)");

            var cfg = new ARBot.Common.Localization.CorridorConfig();
            double maxPar = cfg.MaxParallelErrorRad;

            // --- kontrola meridla proti zname odpovedi ---
            var kontrola = new Stats("");
            int kontrolovano = 0;
            foreach (var m in msgs)
            {
                if (!m.HasLeftLine || !m.HasRightLine) continue;
                if ((ARBot.Common.Localization.CorridorReason)m.CorridorReason
                    != ARBot.Common.Localization.CorridorReason.Ok) continue;
                if (!Geometrie(m, out double w, out _, out _)) continue;
                kontrola.Add(Math.Abs(w - m.Width));
                kontrolovano++;
            }
            Console.WriteLine(kontrolovano == 0
                ? "  kontrola meridla: neni na cem (zadny vzniknuty koridor s useckami)"
                : string.Format(CultureInfo.InvariantCulture,
                    "  kontrola meridla: {0} cyklu, |dopoctena sirka - ulozena| p50 {1:F4} m, max {2:F4} m"
                    + (kontrola.Max < 0.01 ? "  -> SEDI" : "  -> NESEDI, dalsi cisla NECIST"),
                    kontrolovano, kontrola.Median, kontrola.Max));

            // --- sweep prahu ---
            Console.WriteLine();
            Console.WriteLine("  prah   koridoru   z toho NotParallel   sirka p50   sirka p10-p90   mimo 1-8 m");
            foreach (int prah in new[] { 10, 15, 20, 25, 30 })
            {
                int vzniklo = 0, neparalelni = 0, mimo = 0;
                var sirky = new Stats("");
                foreach (var m in msgs)
                {
                    if (!m.HasLeftLine || !m.HasRightLine) continue;
                    if (m.InliersLeft < prah || m.InliersRight < prah) continue;
                    if (!Geometrie(m, out double w, out _, out double par)) continue;
                    if (par > maxPar) { neparalelni++; continue; }
                    vzniklo++;
                    sirky.Add(w);
                    if (w < 1.0 || w > 8.0) mimo++;
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4}   {1,8}   {2,17}   {3,9:F2}   {4,6:F2}-{5,5:F2}   {6,5} ({7,4:F1} %)",
                    prah, vzniklo, neparalelni, sirky.Median, sirky.Percentile(10), sirky.Percentile(90),
                    mimo, vzniklo > 0 ? 100.0 * mimo / vzniklo : 0));
            }
            Console.WriteLine("  (sirka mimo 1-8 m = podpis prave te vady, proti ktere prah vznikl:");
            Console.WriteLine("   primka prolozena par body vyjde kolmo na cestu)");
            Console.WriteLine();
        }

        /// <summary>
        /// Geometrie koridoru z ULOZENYCH usecek — tyz vypocet jako <c>CorridorFinder</c> za
        /// branami. Vraci <c>false</c>, kdyz usecky nejsou k dispozici.
        /// </summary>
        private static bool Geometrie(RoadCorridorMsg m, out double width, out double lateral,
                                      out double parallelError)
        {
            width = lateral = parallelError = 0;
            if (!m.HasLeftLine || !m.HasRightLine) return false;

            double aL = Norm(m.DirectionLeftRad), aR = Norm(m.DirectionRightRad);
            double par = Norm(aL - aR);
            parallelError = Math.Abs(par);

            double dir = Norm(aL - par / 2);
            double nx = -Math.Sin(dir), ny = Math.Cos(dir);

            // ⚠️ Offset se MUSI merit v PATE KOLMICE Z POCATKU, ne v libovolnem bode usecky
            // (tak to dela CorridorFinder.Offset pres ProjectOntoLine). Normala `n` je prumer
            // obou hranic, takze kazda z nich s ni svira az polovinu nerovnobeznosti — a `n · p`
            // se pak podel primky MENI. Pri 10 stupnich a nekolikametrove usecce to dela decimetry:
            // prvni verze tohohle bloku merila z konce usecky a kontrola meridla ji vratila
            // (sirka p50 0,05 m, max 0,39 m vedle). Viz doc/map-correlation-localization.md.
            if (!PataKolmice(m.LeftFromX, m.LeftFromY, m.LeftToX, m.LeftToY, out double lx, out double ly))
                return false;
            if (!PataKolmice(m.RightFromX, m.RightFromY, m.RightToX, m.RightToY, out double rx, out double ry))
                return false;

            double cL = nx * lx + ny * ly;
            double cR = nx * rx + ny * ry;
            if (cL < cR) { var t = cL; cL = cR; cR = t; }

            width = cL - cR;
            lateral = -(cL + cR) / 2;
            return true;
        }

        /// <summary>
        /// Pata kolmice z pocatku na primku danou dvema body usecky. Vraci <c>false</c> u
        /// degenerovane usecky (oba body splyvaji).
        /// </summary>
        private static bool PataKolmice(double ax, double ay, double bx, double by,
                                        out double px, out double py)
        {
            px = py = 0;
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return false;

            // P = A + ((O - A)·u) u, kde O je pocatek a u jednotkovy smer usecky.
            double t = -(ax * dx + ay * dy) / len2;
            px = ax + t * dx;
            py = ay + t * dy;
            return true;
        }

        /// <summary>Uhel primky na +-90 stupnu (primka nema orientaci) — jako v CorridorFinder.</summary>
        private static double Norm(double a)
        {
            while (a > Math.PI / 2) a -= Math.PI;
            while (a < -Math.PI / 2) a += Math.PI;
            return a;
        }

        /// <summary>
        /// <b>Trychtyr:</b> kolik cyklu prezije kterou branu. Samotny vycet <c>FixReason</c> na
        /// otazku „proc je Ok jen 5 %" neodpovi — je to plocha tabulka, ze ktere neni videt, ze
        /// brany jsou v <b>rade za sebou</b> a kazda vidi jen to, co ji predchozi pustila.
        /// </summary>
        private static void Funnel(List<RoadCorridorMsg> all)
        {
            int n = all.Count;
            int paired = all.Count(m => (CorridorFixReason)m.FixReason != CorridorFixReason.NoPair);
            int corridor = all.Count(m =>
            {
                var r = (CorridorFixReason)m.FixReason;
                return r != CorridorFixReason.NoPair && r != CorridorFixReason.NoCorridor;
            });
            int onEdge = all.Count(m => ReachedLateralGate(m));
            int lateralOk = all.Count(m =>
            {
                var r = (CorridorFixReason)m.FixReason;
                return r == CorridorFixReason.WidthNotTrusted
                    || r == CorridorFixReason.WidthDisagreement
                    || r == CorridorFixReason.Ok;
            });
            int okN = all.Count(m => m.FixReason == (byte)CorridorFixReason.Ok);

            // Od 16. 9. 2026 rozhoduje o hrane PRIRAZENI (chi-kvadrat pres kandidaty), ne pricna
            // brana - trychtyr se proto musi jmenovat podle toho, co zaznam skutecne obsahuje.
            bool assoc = all.Exists(m => m.AssocCandidates > 0
                                      || (CorridorFixReason)m.FixReason == CorridorFixReason.EdgeMismatch
                                      || (CorridorFixReason)m.FixReason == CorridorFixReason.AmbiguousEdge);

            Console.WriteLine("TRYCHTYR - kde se cykly ztraceji (kazda brana vidi jen to, co pustila predchozi):");
            Console.WriteLine("  brana                        prezilo   z celku   z predchozi");
            FunnelLine("snimku do stupne", n, n, n);
            FunnelLine("+ naparovana druha kamera", paired, n, n);
            FunnelLine("+ koridor se prolozil", corridor, n, paired);
            FunnelLine(assoc ? "+ prirazena hrana site" : "+ mapa ma po ruce hranu", onEdge, n, corridor);
            if (!assoc) FunnelLine("+ pricna brana", lateralOk, n, onEdge);
            FunnelLine("= Ok (sirkove brany)", okN, n, assoc ? onEdge : lateralOk);
            Console.WriteLine();
        }

        private static void FunnelLine(string name, int v, int total, int prev)
            => Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-28} {1,6}    {2,5:F1} %      {3,5:F1} %",
                name, v, 100.0 * v / Math.Max(1, total), 100.0 * v / Math.Max(1, prev)));

        /// <summary>Dosel cyklus az na pricnou branu? (tedy koridor i mapova hrana existuji)</summary>
        private static bool ReachedLateralGate(RoadCorridorMsg m)
        {
            var r = (CorridorFixReason)m.FixReason;
            return r == CorridorFixReason.LateralDisagreement
                || r == CorridorFixReason.WidthNotTrusted
                || r == CorridorFixReason.WidthDisagreement
                || r == CorridorFixReason.Ok;
        }

        /// <summary>
        /// Pricny nesouhlas na <b>vsech</b> cyklech, ktere na branu vubec doslo — tedy vcetne
        /// zamitnutych.
        ///
        /// <para><b>Proc zvlast.</b> Statistika nad prijatymi merenimi je <b>useknuta prave tou
        /// branou</b>, kterou popisuje (<c>MaxLateralDisagreementM</c>): p90 nemuze vyjit vic nez
        /// strop, at je poloha jakkoli spatna. Cist z ni „chyba pricne pozy je p90 1,2 m" je tedy
        /// selekcni efekt, ne mereni.</para>
        /// </summary>
        private static void LateralUngated(List<RoadCorridorMsg> all)
        {
            var reached = all.Where(ReachedLateralGate).ToList();
            if (reached.Count == 0) return;

            var a = new Stats("abs pricny nesouhlas [m]");
            var v = new Stats("pricny nesouhlas [m]");
            foreach (var m in reached) { a.Add(Math.Abs(m.LateralDisagreement)); v.Add(m.LateralDisagreement); }
            int over = reached.Count(m => Math.Abs(m.LateralDisagreement) > 1.5);

            Console.WriteLine($"PRICNY NESOUHLAS NA VSECH cyklech s hranou (n={reached.Count}, NEuseknuto branou):");
            Console.WriteLine("  " + v.Line());
            Console.WriteLine("  " + a.Line());
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  nad branou 1,5 m: {0} ({1:F1} %) - u nich se skutecna chyba nezmeri, brana je usekla",
                over, 100.0 * over / reached.Count));
            Console.WriteLine("  POZOR: statistika nad PRIJATYMI (nize) je useknuta prave touhle branou.");
            Console.WriteLine();

            // Je ten pricny nesouhlas chyba POZY, nebo se paruje JINA hrana? Rozhodne to rozpad
            // podle nesouhlasu kurzu: kdyz je mapova hrana ta spravna, musi byt rovnobezna s tim,
            // co vidi kamera. Nesouhlas kolem 90 stupnu je podpis PRICNE ulice u krizovatky -
            // RoadAxis.Match bere nejblizsi hranu podle VZDALENOSTI, kurz do vyberu nevstupuje.
            Console.WriteLine("  Je to chyba pozy, nebo spatne naparovana hrana? (rozpad podle nesouhlasu kurzu)");
            Console.WriteLine("  |nesouhlas kurzu|      n   pricne p50   abs pricne p50   odstup od hrany p50");
            double[] hb = { 0, 10, 30, 60, 90.0001 };
            for (int i = 0; i + 1 < hb.Length; i++)
            {
                double a0 = hb[i], b0 = hb[i + 1];
                var bin = reached.Where(m =>
                {
                    double h = Math.Abs(Wrap180(m.HeadingDisagreementRad)) * 180 / Math.PI;
                    return h >= a0 && h < b0;
                }).ToList();
                if (bin.Count == 0) continue;
                var la = new Stats(""); var ed = new Stats(""); var sl = new Stats("");
                foreach (var m in bin)
                {
                    la.Add(Math.Abs(m.LateralDisagreement)); sl.Add(m.LateralDisagreement);
                    ed.Add(m.EdgeDistance);
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,3:F0}-{1,-3:F0} deg          {2,5}      {3,7:F3}          {4,7:F3}               {5,7:F3}",
                    a0, b0, bin.Count, sl.Median, la.Median, ed.Median));
            }
            Console.WriteLine();

            // Byla poza mimo cestu uz od startu, nebo tam ujela? Rozhoduje to o tom, jestli je
            // pricna brana nastavena na chybu, kterou ma zachytit. Odstup se bere z mapove strany
            // (EdgeDistance), tedy nezavisle na tom, co videla kamera.
            Console.WriteLine("  Odstup POZY od nejblizsi mapove hrany v case (polosirka cesty je ~1,6 m):");
            Console.WriteLine("  cas [s]         n   odstup p50   odstup p90   abs pricne p50");
            double t0s = all[0].TimeStamp.Ticks;
            double lastS = (all[all.Count - 1].TimeStamp - all[0].TimeStamp).TotalSeconds;
            double step = Math.Max(30, Math.Ceiling(lastS / 8 / 10) * 10);
            for (double a1 = 0; a1 < lastS; a1 += step)
            {
                double b1 = a1 + step;
                var bin = reached.Where(m =>
                {
                    double t = (m.TimeStamp - all[0].TimeStamp).TotalSeconds;
                    return t >= a1 && t < b1;
                }).ToList();
                if (bin.Count == 0) continue;
                var ed = new Stats(""); var la = new Stats("");
                foreach (var m in bin) { ed.Add(m.EdgeDistance); la.Add(Math.Abs(m.LateralDisagreement)); }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4:F0}-{1,-4:F0}   {2,6}      {3,7:F3}      {4,7:F3}         {5,7:F3}",
                    a1, b1, bin.Count, ed.Median, ed.Percentile(90), la.Median));
            }
            Console.WriteLine();

            // CO BY PUSTILA JINA BRANA. Pricna brana dnes dela DVE prace najednou: rozhoduje
            // „jsem na te ceste?" (prirazeni k hrane) a zaroven „neni to odlehla hodnota?".
            // Prvni prace se pricnou vzdalenosti delat NEMA - to je prave ta velicina, kterou
            // nezname. Tahle tabulka rika, kolik cyklu by prosla kombinace „okno na AZIMUT
            // (na poloze nezavisly) + volnejsi pricna brana".
            Console.WriteLine("  CO BY PUSTILA JINA BRANA (n je z " + reached.Count + " cyklu s hranou):");
            Console.WriteLine("  okno azimutu    pricna brana 1,5 m    3 m      5 m      8 m    bez brany");
            foreach (double aw in new[] { 180.0, 30.0, 15.0 })
            {
                var sel = reached.Where(m => Math.Abs(Wrap180(m.HeadingDisagreementRad)) * 180 / Math.PI <= aw).ToList();
                var cells = new List<string>();
                foreach (double lg in new[] { 1.5, 3.0, 5.0, 8.0, double.MaxValue })
                {
                    int c = sel.Count(m => Math.Abs(m.LateralDisagreement) <= lg);
                    cells.Add(string.Format(CultureInfo.InvariantCulture, "{0,5} ({1,4:F1} %)",
                                            c, 100.0 * c / Math.Max(1, reached.Count)));
                }
                string label = aw >= 180 ? "bez okna" : $"+-{aw:F0} deg";
                Console.WriteLine($"  {label,-14} {string.Join("  ", cells)}");
            }
            Console.WriteLine("  POZOR: okno azimutu je tu spocitane z DNESNIHO kurzu; kdyz je kurz vadny,"
                              + " posune se cele rozdeleni.");
            Console.WriteLine();
        }

        /// <summary>
        /// <b>Koridor jako reference KURZU</b> proti kurzu nad zemi z GPS.
        ///
        /// <para>Merenie kurzu, ktere koridor posila do fuze, je
        /// <c>θ = (PoseTheta + MapHeadingRelRad) − DirectionRad</c>, tedy <b>azimut mapove hrany
        /// minus to, o kolik se cesta v ramci robotu jevi stocena</b>. Prvni scitanec je na poze
        /// nezavisly (je to absolutni azimut hrany), takze cele merenie je nezavisla reference
        /// kurzu — jedina, ktera nema magneticky bias.</para>
        ///
        /// <para><b>Presnost nelze cist z rozptylu proti GPS jako celku:</b> ten nese i sum kurzu
        /// z GPS a hlavne <b>chybu azimutu mapy</b>, ktera je na jedne hrane KONSTANTA. Proto se
        /// tiskne rozpad <b>po OSM cestach</b>: stredni hodnota na ceste = bias (mapa + sikme
        /// jeti), rozptyl uvnitr cesty = sum. A vedle toho σ, kterou hlasi samo prolozeni — pomer
        /// tech dvou rika, jestli je σ poctiva.</para>
        /// </summary>
        private static void HeadingVsGps(RecordFile rec, List<RoadCorridorMsg> all)
        {
            const double MinSpeedMps = 0.3;
            const double MaxSkewSec = 0.15;

            var gps = new List<(DateTime T, double Course)>();
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "GPSState") continue;
                if (!(rec.Read(e) is GPSState g) || !g.DynamicOrientation.HasValue) continue;
                double speed = g.Speed ?? g.DynamicSpeed ?? 0.0;
                if (speed < MinSpeedMps) continue;
                gps.Add((g.TimeStamp, g.DynamicOrientation.Value));
            }
            gps.Sort((x, y) => x.T.CompareTo(y.T));

            Console.WriteLine("KORIDOR JAKO REFERENCE KURZU (proti kurzu nad zemi z GPS):");
            if (gps.Count == 0)
            {
                Console.WriteLine("  zaznam nenese kurz z GPS nad prahem rychlosti - nelze srovnat.");
                Console.WriteLine();
                return;
            }

            var ok = all.Where(m => m.FixReason == (byte)CorridorFixReason.Ok && m.HasPose).ToList();
            var pairs = new List<(RoadCorridorMsg M, double Corr, double Gps, double Pose)>();
            foreach (var m in ok)
            {
                if (!TryNearestCourse(gps, m.TimeStamp, MaxSkewSec, out double course)) continue;
                // Rozdil smeru primek slozit na ±90° a teprve pak z nej udelat SMER - tu dvojici
                // dir / dir+180° rozhodne kurz robotu (kamera to rozhodnout neumi).
                double corr = Orient(m.PoseTheta + Wrap180(m.MapHeadingRelRad - m.DirectionRad),
                                     m.PoseTheta);
                pairs.Add((m, corr, course, m.PoseTheta));
            }
            Console.WriteLine($"  cyklu Ok: {ok.Count}, z toho s kurzem z GPS do {MaxSkewSec * 1000:F0} ms "
                              + $"a nad {MinSpeedMps:F1} m/s: {pairs.Count}");

            // Kolikrat by se na orientaci primky slo spalit? Merenie kurzu, ktere jde do fuze, se
            // pocita jako (PoseTheta + HeadingRel) − Direction BEZ slozeni na ±90° a bez
            // rozhodnuti, kterym smerem cesta vede. Kdyz vyjde vic nez 90° od kurzu robotu, je
            // vybrany OPACNY smer primky - a do fuze jde kurz otoceny.
            int flipped = ok.Count(m => Math.Abs(Wrap(m.MapHeadingRelRad - m.DirectionRad)) > Math.PI / 2);
            Console.WriteLine($"  z toho by BEZ orientace podle kurzu robotu vyslo na opacnou stranu"
                              + $" primky: {flipped} ({100.0 * flipped / Math.Max(1, ok.Count):F1} %)");
            if (pairs.Count < 5) { Console.WriteLine("  malo vzorku."); Console.WriteLine(); return; }

            var dCorr = new Stats("koridor - GPS kurz [deg]");
            var dPose = new Stats("odhad fuze - GPS kurz [deg]");
            var dMap = new Stats("koridor - odhad fuze [deg]");
            var sig = new Stats("sigma prolozeni [deg]");
            foreach (var p in pairs)
            {
                dCorr.Add(Wrap(p.Corr - p.Gps) * 180 / Math.PI);
                dPose.Add(Wrap(p.Pose - p.Gps) * 180 / Math.PI);
                dMap.Add(Wrap(p.Corr - p.Pose) * 180 / Math.PI);
                sig.Add(p.M.SigmaDirectionRad * 180 / Math.PI);
            }
            Console.WriteLine("  " + dCorr.Line());
            Console.WriteLine("  " + dPose.Line());
            Console.WriteLine("  " + dMap.Line());
            Console.WriteLine("  " + sig.Line());
            Console.WriteLine();

            // Rozpad po ceste je tu proto, ze chyba azimutu MAPY je na jedne hrane konstanta:
            // stredni hodnota na ceste ji tedy nese, rozptyl uvnitr cesty uz ne.
            //
            // ⚠️ Tiskne se stred i MEDIAN a sd i ROBUSTNI sd (1,4826·MAD), protoze rozdeleni je
            // dvouvrcholove: u krizovatky se jako "nejblizsi hrana" vybere PRICNA ulice a rozpor
            // vyskoci o ~90 stupnu. Prumer a sd takovou primes rozmazou pres celou cestu a
            // vypadalo by to, ze koridor kurz nemeri - pritom nemeri MAPA spravnou hranu.
            Console.WriteLine("  Rozpad po OSM ceste (stred = bias mapy a sikmeho jeti, rozptyl uvnitr = sum):");
            Console.WriteLine("  wayId              n     stred      sd    median   robust sd   |d|>20deg   sigma fitu");
            double sumSq = 0; int sumN = 0;
            foreach (var g in pairs.GroupBy(p => p.M.WayId).OrderByDescending(x => x.Count()))
            {
                var d = g.Select(p => Wrap(p.Corr - p.Gps) * 180 / Math.PI).ToList();
                if (d.Count < 3) continue;
                double mean = d.Average();
                double sd = Math.Sqrt(d.Sum(x => (x - mean) * (x - mean)) / (d.Count - 1));
                double med = Median(d);
                double rsd = 1.4826 * Median(d.Select(x => Math.Abs(x - med)).ToList());
                int outl = d.Count(x => Math.Abs(x - med) > 20);
                var sg = new Stats(""); foreach (var pr in g) sg.Add(pr.M.SigmaDirectionRad * 180 / Math.PI);
                sumSq += (d.Count - 1) * rsd * rsd; sumN += d.Count - 1;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,5}  {2,8:F2} {3,7:F2}  {4,8:F2}    {5,8:F2}   {6,5:F1} %      {7,6:F2}",
                    g.Key, d.Count, mean, sd, med, rsd, 100.0 * outl / d.Count, sg.Median));
            }
            if (sumN > 0)
            {
                double pooled = Math.Sqrt(sumSq / sumN);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  sdruzena ROBUSTNI sd uvnitr cest: {0:F2} deg (nese i sum kurzu z GPS -> HORNI mez)",
                    pooled));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  proti sigme prolozeni p50 {0:F2} deg -> prolozeni je {1:F0}x optimistictejsi",
                    sig.Median, pooled / Math.Max(1e-9, sig.Median)));
                Console.WriteLine("  Sloupec |d|>20deg je podil cyklu, kde se nejspis paruje JINA hrana"
                                  + " (krizovatka) - to neni sum koridoru.");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Jak dopadlo <b>prirazeni k hrane</b> (verze 6 zpravy): skore viteze, odstup od druheho
        /// a pocet kandidatu.
        ///
        /// <para><b>Nacpak to je:</b> <c>assocchi2=</c> a <c>assocmargin=</c> jdou proladit jen
        /// OFFLINE nad zaznamem — prah odstupu se z niceho jineho nez z rozdeleni odstupu
        /// odvodit neda.</para>
        /// </summary>
        private static void AssociationScore(List<RoadCorridorMsg> all)
        {
            var withScore = all.Where(m => !double.IsNaN(m.AssocChi2)).ToList();
            if (withScore.Count == 0) return;      // starsi zaznam, prirazeni se nepocitalo

            var chi = new Stats("chi2 viteze");
            var second = new Stats("chi2 druheho");
            var margin = new Stats("odstup od druheho");
            var cand = new Stats("kandidatu po vetu");
            foreach (var m in withScore)
            {
                chi.Add(m.AssocChi2);
                cand.Add(m.AssocCandidates);
                if (!double.IsNaN(m.AssocChi2Second))
                {
                    second.Add(m.AssocChi2Second);
                    margin.Add(m.AssocChi2Second - m.AssocChi2);
                }
            }

            Console.WriteLine($"PRIRAZENI K HRANE (skore, n={withScore.Count}):");
            Console.WriteLine("  " + chi.Line());
            Console.WriteLine("  " + second.Line());
            Console.WriteLine("  " + margin.Line());
            Console.WriteLine("  " + cand.Line());
            int amb = all.Count(m => (CorridorFixReason)m.FixReason == CorridorFixReason.AmbiguousEdge);
            int mis = all.Count(m => (CorridorFixReason)m.FixReason == CorridorFixReason.EdgeMismatch);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  nejednoznacnych {0} ({1:F1} %), bez sedici hrany {2} ({3:F1} %)",
                amb, 100.0 * amb / all.Count, mis, 100.0 * mis / all.Count));
            Console.WriteLine("  Jen druhy kandidat s malym odstupem je NEJEDNOZNACNOST - jeden"
                              + " kandidat znamena, ze v okoli zadna jina cesta neni.");
            Console.WriteLine();
        }

        /// <summary>
        /// Podklad pro <b>prirazeni k hrane jako Mahalanobisovu vzdalenost</b>: jake sigmy jsou
        /// vubec k dispozici a jak by z nich vysly obe slozky chi-kvadratu.
        ///
        /// <para>Otazka „jak slozit odchylku VZDALENOSTI a odchylku SMERU do jednoho cisla" ma
        /// odpoved bez volnych vah: obe veliciny maji svou sigmu, takze se deli tou svou a
        /// scitaji az bezrozmerne. Vahy se pak nenastavuji - <b>meri se</b>. Tenhle blok tiskne
        /// vsechny ctyri sigmy, ktere do toho vstupuji, protoze dve z nich jsou v zaznamu
        /// (kovariance pozy z fuze) a dve hlasi samo prolozeni.</para>
        ///
        /// <para>⚠️ <b>Hlasene sigmy jsou nepoctive</b> a je to zmerene (viz blok o kurzu vys
        /// a doc/imu-and-frames.md), takze se tiskne i varianta s <b>podlahou</b> - tatáz lecba
        /// jako <c>imuheadingstd=</c> u kompasu.</para>
        /// </summary>
        private static void AssociationSigmas(RecordFile rec, List<RoadCorridorMsg> all)
        {
            var reached = all.Where(ReachedLateralGate).ToList();
            if (reached.Count == 0) return;
            var poses = new PoseTrack(rec);

            var sLat = new Stats("sigma pozy pricne [m]");
            var sTh = new Stats("sigma pozy kurz [deg]");
            var cLat = new Stats("sigma koridoru pricne [m]");
            var cTh = new Stats("sigma koridoru kurz [deg]");
            var chiLat = new Stats("chi2 pricne (hlasene sigmy)");
            var chiTh = new Stats("chi2 kurz (hlasene sigmy)");
            var chiThF = new Stats("chi2 kurz (podlaha 10 deg)");
            const double FloorDeg = 10.0;

            foreach (var m in reached)
            {
                var st = poses.Nearest(m.TimeStamp);
                if (st?.Covariance == null || st.Covariance.RowCount <= EKFModel.ITh) continue;

                // Pricny smer je NORMALA hrany; azimut hrany je PoseTheta + MapHeadingRelRad.
                double edgeAz = m.PoseTheta + m.MapHeadingRelRad;
                double nx = -Math.Sin(edgeAz), ny = Math.Cos(edgeAz);
                double pxx = st.Covariance[EKFModel.IX, EKFModel.IX];
                double pxy = st.Covariance[EKFModel.IX, EKFModel.IY];
                double pyy = st.Covariance[EKFModel.IY, EKFModel.IY];
                double varLat = nx * nx * pxx + 2 * nx * ny * pxy + ny * ny * pyy;
                double varTh = st.Covariance[EKFModel.ITh, EKFModel.ITh];
                if (varLat <= 0 || varTh <= 0) continue;

                double sigLat = Math.Sqrt(varLat), sigTh = Math.Sqrt(varTh);
                sLat.Add(sigLat); sTh.Add(sigTh * 180 / Math.PI);
                cLat.Add(m.SigmaLateral); cTh.Add(m.SigmaDirectionRad * 180 / Math.PI);

                double dLat = m.LateralDisagreement;
                double dTh = Wrap180(m.HeadingDisagreementRad);
                chiLat.Add(dLat * dLat / (varLat + m.SigmaLateral * m.SigmaLateral));
                chiTh.Add(dTh * dTh / (varTh + m.SigmaDirectionRad * m.SigmaDirectionRad));
                double floor = FloorDeg * Math.PI / 180;
                chiThF.Add(dTh * dTh / Math.Max(varTh + m.SigmaDirectionRad * m.SigmaDirectionRad, floor * floor));
            }

            if (sLat.Count == 0)
            {
                Console.WriteLine("PODKLAD PRO PRIRAZENI K HRANE: zaznam nenese kovarianci pozy.");
                Console.WriteLine();
                return;
            }

            Console.WriteLine($"PODKLAD PRO PRIRAZENI K HRANE (Mahalanobis, n={sLat.Count}):");
            Console.WriteLine("  " + sLat.Line());
            Console.WriteLine("  " + cLat.Line());
            Console.WriteLine("  " + sTh.Line());
            Console.WriteLine("  " + cTh.Line());
            Console.WriteLine();
            Console.WriteLine("  " + chiLat.Line());
            Console.WriteLine("  " + chiTh.Line());
            Console.WriteLine("  " + chiThF.Line());
            Console.WriteLine("  Prah chi2 pro 2 stupne volnosti: 5,99 (95 %), 9,21 (99 %).");
            Console.WriteLine("  POZOR: kdyz je chi2 kurzu radove nad prahem i na SPRAVNE hrane,"
                              + " je vadna sigma, ne prirazeni.");
            Console.WriteLine();
        }

        /// <summary>Kurz z GPS nejblizsi danemu casu, nebo <c>false</c> pri vetsim rozestupu.</summary>
        private static bool TryNearestCourse(List<(DateTime T, double Course)> gps, DateTime t,
                                             double maxSkewSec, out double course)
        {
            course = 0;
            int lo = 0, hi = gps.Count - 1, first = gps.Count;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (gps[mid].T < t) lo = mid + 1; else { first = mid; hi = mid - 1; }
            }
            double bestDt = double.MaxValue; int bestI = -1;
            foreach (int i in new[] { first - 1, first })
            {
                if (i < 0 || i >= gps.Count) continue;
                double dt = Math.Abs((gps[i].T - t).TotalSeconds);
                if (dt < bestDt) { bestDt = dt; bestI = i; }
            }
            if (bestI < 0 || bestDt > maxSkewSec) return false;
            course = gps[bestI].Course;
            return true;
        }

        private static double Median(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            var t = v.OrderBy(x => x).ToList();
            return t.Count % 2 == 1 ? t[t.Count / 2] : 0.5 * (t[t.Count / 2 - 1] + t[t.Count / 2]);
        }

        private static double Wrap(double a) => Math.Atan2(Math.Sin(a), Math.Cos(a));

        /// <summary>
        /// Slozeni uhlu na (−90°, 90°] — <b>rozdil smeru dvou PRIMEK</b>, ne dvou sipek.
        ///
        /// <para><b>Proc to tu je.</b> Kamera vidi cestu, ale ne kterym smerem po ni jedeme:
        /// primka nema orientaci, takze smer x a x+180° jsou totez. <c>RoadCorridor.DirectionRad</c>
        /// i <c>RoadAxisMatch.HeadingRelRad</c> jsou proto oba slozene na ±90°, jenze jejich
        /// ROZDIL uz slozeny neni — a kdyz je cesta zhruba kolma na kurz robotu, vyjde jedno
        /// z cisel u +89° a druhe u −89°, tedy rozdil 178° tam, kde je skutecny nesouhlas 2°.
        /// Bez tohohle skladani se takovy cyklus tvari jako pricna ulice.</para>
        /// </summary>
        private static double Wrap180(double a)
        {
            a = Math.IEEERemainder(a, Math.PI);
            if (a > Math.PI / 2) a -= Math.PI;
            if (a <= -Math.PI / 2) a += Math.PI;
            return a;
        }

        /// <summary>
        /// Rozhodne, kterym smerem cesta vede: <b>podle kurzu robotu</b>. Z dvojice
        /// <c>dir</c> / <c>dir + 180°</c> vrati tu blizsi kurzu.
        /// </summary>
        private static double Orient(double dir, double heading)
            => Math.Abs(Wrap(dir - heading)) <= Math.PI / 2 ? dir : Wrap(dir + Math.PI);

        /// <summary>
        /// Rozpad prijatych merenii podle <b>rychlosti robotu</b> a podle <b>casu v behu</b>
        /// (= misto na trase) — a chyba lokalizace proti ground truth.
        ///
        /// <para><b>Proc to tu je.</b> Nesouhlas sirky vypadal, jako by zavisel na parovacim
        /// rozestupu, ale zlom byl ostry (do 120 ms spatne, nad 120 ms dobre) — to je podpis
        /// <b>zavadejici promenne</b>, ne plynule ceny okna. Rychlost a misto na trase jsou
        /// prvni dva kandidati: <c>SyntetickyKoridor.osm</c> ma krizovatku i slepy konec, kde
        /// koridor existovat nema, a robot v cili stoji.</para>
        /// </summary>
        private static void ByPose(RecordFile rec, List<RoadCorridorMsg> ok, List<RoadCorridorMsg> all)
        {
            var poses = new PoseTrack(rec);
            var t0 = all[0].TimeStamp;

            // KDE robot vlastne byl. Bez toho se "koridor prestal vznikat" pletlo s "dojel na konec
            // cesty" — nad rovnou mapou dlouhou 80 m to pri 1,2 m/s nemuze byt totez, a rozdil je
            // videt jen z trajektorie. Kresli se z tiku RobotStateMsg v casech cyklu koridoru.
            {
                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;
                double? px = null, py = null; double path = 0;
                foreach (var m in all)
                {
                    var p = poses.Nearest(m.TimeStamp);
                    if (p == null) continue;
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                    if (px.HasValue)
                        path += Math.Sqrt((p.X - px.Value) * (p.X - px.Value) + (p.Y - py.Value) * (p.Y - py.Value));
                    px = p.X; py = p.Y;
                }
                if (minX <= maxX)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "Trajektorie (lokalni ENU): X {0:F1}..{1:F1} m, Y {2:F1}..{3:F1} m, ujeto {4:F1} m",
                        minX, maxX, minY, maxY, path));
                    Console.WriteLine();
                }
            }

            Console.WriteLine("Prijata merenia podle RYCHLOSTI robotu:");
            Console.WriteLine("  rychlost [m/s]    n   abs sirka p50   abs pricne p50   nerovnobez. p50 [deg]");
            double[] vEdges = { -0.01, 0.05, 0.3, 0.8, 1.1, double.MaxValue };
            foreach (var b in Bins(vEdges))
            {
                var bin = ok.Where(m => { var p = poses.Nearest(m.TimeStamp);
                                          return p != null && p.V >= b.a && p.V < b.b; }).ToList();
                if (bin.Count == 0) continue;
                PrintBin(b.b == double.MaxValue ? $"nad {b.a:F2}" : $"{b.a:F2}-{b.b:F2}", bin);
            }
            Console.WriteLine();

            // Rozsah pasem se bere z DELKY ZAZNAMU, ne natvrdo. Bylo tu 40 s (delka tehdejsich
            // behu), takze nad 70s jizdou po OSM/SyntetickyRovny.osm se poslednich 30 s vubec
            // netisklo - a prave tam koridor propadal. Pasmo se drzi na ~8 radcich, at je to
            // citelne i u dlouheho behu.
            double runSeconds = Math.Ceiling((all[all.Count - 1].TimeStamp - t0).TotalSeconds);
            double binSeconds = Math.Max(5, Math.Ceiling(runSeconds / 8 / 5) * 5);

            Console.WriteLine($"Prijata merenia podle CASU v behu (= misto na trase, {binSeconds:F0}s pasma):");
            Console.WriteLine("  cas [s]           n   abs sirka p50   abs pricne p50   nerovnobez. p50 [deg]");
            for (double s = 0; s < runSeconds; s += binSeconds)
            {
                double a = s, b = s + binSeconds;
                var bin = ok.Where(m => { double dt = (m.TimeStamp - t0).TotalSeconds;
                                          return dt >= a && dt < b; }).ToList();
                if (bin.Count == 0) continue;
                PrintBin($"{a:F0}-{b:F0}", bin);
            }
            Console.WriteLine();

            Console.WriteLine("Podil zamitnutych (NotParallel) po casu — kde koridor vubec nevznika:");
            Console.WriteLine("  cas [s]        cyklu    Ok   NotParallel");
            for (double s = 0; s < runSeconds; s += binSeconds)
            {
                double a = s, b = s + binSeconds;
                var bin = all.Where(m => { double dt = (m.TimeStamp - t0).TotalSeconds;
                                           return dt >= a && dt < b; }).ToList();
                if (bin.Count == 0) continue;
                int okN = bin.Count(m => m.FixReason == (byte)CorridorFixReason.Ok);
                int np = bin.Count(m => m.FixReason == (byte)CorridorFixReason.NoCorridor
                                     && m.CorridorReason == (byte)CorridorReason.NotParallel);
                Console.WriteLine($"  {a,2:F0}-{b,-2:F0}          {bin.Count,5} {okN,5}         {np,5}");
            }
            Console.WriteLine();

            // Rozpad po OSM cestach. SyntetickyKoridor.osm ma cesty sirky 1, 2 a 3 m, takze
            // "nesouhlas sirky" muze byt cely o tom, ze poza uz je na jine ceste, nez na kterou
            // se kamery koukaji — a to bez jakekoli chyby polohy (pricne to sedi).
            Console.WriteLine("Prijata merenia podle OSM cesty (way):");
            Console.WriteLine("  wayId              n   sirka p50   mapa p50   abs sirka p50   abs pricne p50");
            foreach (var g in ok.GroupBy(m => m.WayId).OrderByDescending(g => g.Count()))
            {
                var w = new Stats(""); var mw = new Stats(""); var aw = new Stats(""); var al = new Stats("");
                foreach (var m in g)
                {
                    w.Add(m.Width); mw.Add(m.MapWidth);
                    aw.Add(Math.Abs(m.WidthDisagreement)); al.Add(Math.Abs(m.LateralDisagreement));
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,6}    {2,8:F3}   {3,8:F3}        {4,8:F3}        {5,8:F3}",
                    g.Key, g.Count(), w.Median, mw.Median, aw.Median, al.Median));
            }
            Console.WriteLine();

            if (!poses.HasTruth)
            {
                Console.WriteLine("Ground truth v zaznamu neni — chybu lokalizace nelze spocitat.");
                return;
            }
            var ep = new Stats("chyba polohy [m]");
            var eh = new Stats("chyba kurzu [deg]");
            foreach (var m in all)
            {
                if (!poses.TryError(m.TimeStamp, out double dx, out double dy, out double dth)) continue;
                ep.Add(Math.Sqrt(dx * dx + dy * dy));
                eh.Add(Math.Abs(dth) * 180 / Math.PI);
            }
            Console.WriteLine("Chyba lokalizace (ground truth minus odhad) v casech cyklu koridoru:");
            Console.WriteLine("  " + ep.Line());
            Console.WriteLine("  " + eh.Line());
            Console.WriteLine();
        }

        private static IEnumerable<(double a, double b)> Bins(double[] edges)
        {
            for (int i = 0; i + 1 < edges.Length; i++) yield return (edges[i], edges[i + 1]);
        }

        private static void PrintBin(string label, List<RoadCorridorMsg> bin)
        {
            var aw = new Stats(""); var al = new Stats(""); var pe = new Stats("");
            foreach (var m in bin)
            {
                aw.Add(Math.Abs(m.WidthDisagreement));
                al.Add(Math.Abs(m.LateralDisagreement));
                pe.Add(Math.Abs(m.ParallelErrorRad) * 180 / Math.PI);
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-14} {1,4}       {2,8:F3}         {3,8:F3}            {4,8:F2}",
                label, bin.Count, aw.Median, al.Median, pe.Median));
        }

        /// <summary>
        /// CSV radek za kazdy cyklus koridoru — pro pripady, kdy percentily nestaci a je potreba
        /// videt <b>casovy prubeh</b> (napr. jestli filtr sirky konverguje, nebo stoji).
        /// </summary>
        public static void Dump(RecordFile rec)
        {
            var msgs = new List<RoadCorridorMsg>();
            foreach (var e in rec.Index)
                if (e.MsgName == "RoadCorridorMsg" && rec.Read(e) is RoadCorridorMsg m) msgs.Add(m);
            if (msgs.Count == 0) return;

            var poses = new PoseTrack(rec);
            var t0 = msgs[0].TimeStamp;
            Console.WriteLine("t;fix;corr;way;width;mapWidth;filtered;dWidth;dLat;parErr_deg;v;inlL;inlR;resL;resR;"
                              + "dirL_deg;dirR_deg;farL;farR");
            foreach (var m in msgs)
            {
                var p = poses.Nearest(m.TimeStamp);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F3};{1};{2};{3};{4:F3};{5:F3};{6:F3};{7:F3};{8:F3};{9:F2};{10:F2};{11};{12};{13:F3};{14:F3};"
                    + "{15:F2};{16:F2};{17:F2};{18:F2}",
                    (m.TimeStamp - t0).TotalSeconds, (CorridorFixReason)m.FixReason,
                    (CorridorReason)m.CorridorReason, m.WayId, m.Width, m.MapWidth, m.FilteredWidth,
                    m.WidthDisagreement, m.LateralDisagreement, m.ParallelErrorRad * 180 / Math.PI,
                    p?.V ?? double.NaN, m.InliersLeft, m.InliersRight, m.ResidualLeft, m.ResidualRight,
                    m.DirectionLeftRad * 180 / Math.PI, m.DirectionRightRad * 180 / Math.PI,
                    Reach(m.HasLeftLine, m.LeftFromX, m.LeftFromY, m.LeftToX, m.LeftToY),
                    Reach(m.HasRightLine, m.RightFromX, m.RightFromY, m.RightToX, m.RightToY)));
            }
        }

        /// <summary>
        /// Jak daleko od robotu dosahuje vzdalenejsi konec prolozene usecky [m]. <b>Klicovy udaj
        /// pri hledani spatneho prolozeni:</b> odchylka hranicnich bodu od okraje vozovky ma
        /// nulovy median v kazde vzdalenosti, ale rozptyl roste (±0,05 m na 1 m, −0,63/+0,40 m na
        /// 10 m; naměřeno 22. 8. 2026), takze usecka tahnouci se do 8-10 m je skoro urcite
        /// nahodne zarovnani rozstriknutych vzdalenych bodu.
        /// </summary>
        private static double Reach(bool has, double ax, double ay, double bx, double by)
        {
            if (!has) return double.NaN;
            return Math.Max(Math.Sqrt(ax * ax + ay * ay), Math.Sqrt(bx * bx + by * by));
        }

        private static void Report(string title, List<RoadCorridorMsg> ok)
        {
            Console.WriteLine($"--- {title} (n={ok.Count}) ---");
            if (ok.Count == 0) { Console.WriteLine(); return; }

            var stats = new[]
            {
                new Stats("sirka merena [m]"),
                new Stats("sirka z mapy/filtru [m]"),
                new Stats("sirkovy nesouhlas [m]"),
                new Stats("abs sirkovy nesouhlas [m]"),
                new Stats("pricny nesouhlas [m]"),
                new Stats("abs pricny nesouhlas [m]"),
                new Stats("nerovnobeznost [deg]"),
                new Stats("abs nesouhlas kurzu [deg]"),
                new Stats("rezidua (L+R)/2 [m]"),
                new Stats("inliery L"),
                new Stats("inliery R"),
                new Stats("sigma pricne [m]"),
            };
            foreach (var m in ok)
            {
                stats[0].Add(m.Width);
                stats[1].Add(m.MapWidth);
                stats[2].Add(m.WidthDisagreement);
                stats[3].Add(Math.Abs(m.WidthDisagreement));
                stats[4].Add(m.LateralDisagreement);
                stats[5].Add(Math.Abs(m.LateralDisagreement));
                stats[6].Add(Math.Abs(m.ParallelErrorRad) * 180 / Math.PI);
                stats[7].Add(Math.Abs(Wrap180(m.HeadingDisagreementRad)) * 180 / Math.PI);
                stats[8].Add(0.5 * (m.ResidualLeft + m.ResidualRight));
                stats[9].Add(m.InliersLeft);
                stats[10].Add(m.InliersRight);
                stats[11].Add(m.SigmaLateral);
            }
            foreach (var s in stats) Console.WriteLine("  " + s.Line());
            Console.WriteLine();
        }

        /// <summary>
        /// Kontrola, jestli hlasena sirka odpovida <b>zaznamenanym useckam</b> prolozeni: sirka ma
        /// byt odstup obou primek. Kdyby se lisily, chyba je ve vypoctu, ne v datech. Zaroven se
        /// meri <b>podelny prekryv</b> usecek — kdyz hranice nepokryvaji tentyz usek cesty, je
        /// sirka dopoctena extrapolaci a chyba smeru se do ni prenasi nasobene.
        /// </summary>
        private static void GeometryCheck(List<RoadCorridorMsg> ok)
        {
            var diff = new Stats("sirka hlasena - z usecek [m]");
            var overlap = new Stats("podelny prekryv usecek [m]");
            var lenL = new Stats("delka usecky L [m]");
            var lenR = new Stats("delka usecky R [m]");
            int both = 0;
            foreach (var m in ok)
            {
                if (!m.HasLeftLine || !m.HasRightLine) continue;
                both++;

                double lmx = 0.5 * (m.LeftFromX + m.LeftToX), lmy = 0.5 * (m.LeftFromY + m.LeftToY);
                double rmx = 0.5 * (m.RightFromX + m.RightToX), rmy = 0.5 * (m.RightFromY + m.RightToY);
                double dir = m.DirectionRad;
                double nx = -Math.Sin(dir), ny = Math.Cos(dir);
                double w = (lmx - rmx) * nx + (lmy - rmy) * ny;
                diff.Add(m.Width - Math.Abs(w));

                double c = Math.Cos(dir), s = Math.Sin(dir);
                double l1 = m.LeftFromX * c + m.LeftFromY * s, l2 = m.LeftToX * c + m.LeftToY * s;
                double r1 = m.RightFromX * c + m.RightFromY * s, r2 = m.RightToX * c + m.RightToY * s;
                overlap.Add(Math.Min(Math.Max(l1, l2), Math.Max(r1, r2)) - Math.Max(Math.Min(l1, l2), Math.Min(r1, r2)));
                lenL.Add(Math.Abs(l2 - l1));
                lenR.Add(Math.Abs(r2 - r1));
            }
            Console.WriteLine($"Geometrie prolozeni (usecky jsou u {both} z {ok.Count} prijatych):");
            Console.WriteLine("  " + diff.Line());
            Console.WriteLine("  " + overlap.Line());
            Console.WriteLine("  " + lenL.Line());
            Console.WriteLine("  " + lenR.Line());
            Console.WriteLine();
        }

        /// <summary>
        /// Prehraje parovani snimku a vrati pro kazdy zpracovany snimek jeho rozestup [ms]
        /// k partnerovi. Klic je cas snimku = <see cref="RoadCorridorMsg.TimeStamp"/>.
        /// </summary>
        private static Dictionary<DateTime, double> PairingSkew(RecordFile rec, List<RoadCorridorMsg> msgs)
        {
            var processed = new HashSet<DateTime>(msgs.Select(m => m.TimeStamp));
            var result = new Dictionary<DateTime, double>();
            var last = new Dictionary<string, DateTime>();

            foreach (var e in rec.Index)
            {
                if (e.MsgName != "CameraFrame") continue;
                var t = e.CaptureTime;
                if (!processed.Contains(t)) continue;

                string cam = e.Name ?? string.Empty;
                last[cam] = t;

                double best = double.MaxValue;
                foreach (var kv in last)
                {
                    if (kv.Key == cam) continue;
                    double dt = Math.Abs((kv.Value - t).TotalMilliseconds);
                    if (dt < best) best = dt;
                }
                if (best < double.MaxValue) result[t] = best;
            }
            return result;
        }
    }
}
