using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Jak velké `corridorstd=` by zkrotilo skoky pózy — proměřeno nad záznamem.</b>
    ///
    /// <para><b>Nač to je:</b> na Robotouru 19. 9. 2026 přišel každý skok pózy (0,6–4 m) do 0,1 s po
    /// přijatém měření koridoru, ačkoli profil měl `corridorstd=0,1`. Autor: „0,1 je hodně málo,
    /// viděl bych to kolem 5". Tady se to nehádá: záznam nese u každého měření koridoru inovaci
    /// (<c>RoadCorridorMsg.LateralDisagreement</c> = kamera − mapa příčně), σ proložení, id hrany
    /// a pózu, a v <c>RobotStateMsg</c> kovarianci pózy. Z toho jde (1) ověřit, že skok = K·inovace
    /// s K = P/(P + R), (2) odhadnout příčný procesní šum z růstu P mezi měřeními, a (3) pro
    /// kandidáty σ udělat <b>jednorozměrný protifaktický replay</b>: jak velké by kroky byly, jak
    /// daleko by se protifaktická trajektorie odchýlila od zaznamenané a kolik informace by
    /// koridor měl proti GPS.</para>
    ///
    /// <para><b>Model (příčná osa, 1-D):</b> mezi měřeními <c>P += q·dt</c>; při měření
    /// <c>K = P/(P+R)</c>, krok <c>s = K·ν</c>, <c>P = (1−K)·P</c>. Protifaktická trajektorie se od
    /// zaznamenané liší o <c>δ</c>: inovace v ní je <c>ν − δ</c> (měření je absolutní vůči mapě),
    /// takže <c>δ' = δ + K'(ν − δ) − s_rec</c>, kde <c>s_rec = K_rec·ν</c> je krok, který udělal
    /// skutečný běh. GPS se v δ zanedbává (σ 30×HDOP ≈ 45–90 m, K ~ 10⁻⁴), odometrie a IMU působí
    /// na obě trajektorie stejně.</para>
    ///
    /// <para>⚠️ Je to 1-D aproximace: neříká, jestli byla inovace <i>pravdivá</i> (póza opravdu
    /// vedle) nebo <i>falešná</i> (špatná hrana / proložení). Proto se u velkých inovací tiskne
    /// i změna <c>WayId</c> — skok při přepnutí hrany je problém přiřazení, ne σ.</para>
    ///
    /// <para><b>Blok 4 (21. 9. 2026): kandidáti `corridorslew=`</b> — týž replay, ale místo σ se
    /// zkouší <b>limit kroku</b>: na jedno měření smí póza uhnout nejvýš <c>slew × Δt</c>
    /// (Δt od předchozího měření, ořezaný na 0,02–1 s jako v <c>CorridorLocalizer</c>); filtr
    /// toho dosáhne nafouknutím R (<c>R' = P·(|ν|/L − 1)</c>, viz <c>Ekf.UpdateStep</c>).
    /// Limit se uplatňuje na krok podle skalárního vzorce (tak, jak to dělá filtr); vytištěný
    /// krok je pak kalibrovaný faktorem <c>K_eff</c>, takže skutečný posun pózy je pod limitem.
    /// Závěr 20. 9.: σ skoky odstraní jen za cenu, že drift zůstane — limit má dovolit rychlé
    /// stažení driftu bez skoku. Tady se to měří, ne hádá.</para>
    /// </summary>
    public static class CorridorStdReport
    {
        /// <summary>Strop a podlaha Δt pro limit kroku — stejné jako <c>CorridorLocalizerConfig</c>.</summary>
        private const double SlewDtCap = 1.0, SlewDtFloor = 0.02;

        public static void Run(RecordFile rec, double[] kandidati, double jumpM,
                               double[] slewKandidati = null, double? stdProSlew = null,
                               double[] hdgSlewKandidatiDegS = null)
        {
            var cors = rec.ReadAll<RoadCorridorMsg>("RoadCorridorMsg").Where(c => c.EmittedLateral && c.HasPose)
                          .OrderBy(c => c.TimeStamp).ToList();
            var states = rec.ReadAll<RobotStateMsg>("RobotStateMsg").OrderBy(s => s.TimeStamp).ToList();
            var gps = rec.ReadAll<ARBot.Common.Devices.GPSState>("GPSState").OrderBy(g => g.TimeStamp).ToList();
            double stdRec = ParamZLogu(rec, "corridorstd", 0);
            double hzRec = ParamZLogu(rec, "corridorhz", 0);
            double gpsPosStd = ParamZLogu(rec, "gpsposstd", 1.5);

            Console.WriteLine($"Merenia koridoru poslana do fuze (EmittedLateral): {cors.Count}; RobotStateMsg {states.Count}; "
                              + $"v zaznamu corridorstd={stdRec}, corridorhz={hzRec}, gpsposstd={gpsPosStd}");
            if (cors.Count < 10 || states.Count < 10) { Console.WriteLine("Malo dat."); return; }

            // ---------- 1. Inovace a skutecny krok ----------
            Console.WriteLine();
            Console.WriteLine("=== 1. INOVACE (kamera - mapa pricne) A SKUTECNY KROK POZY ===");
            var radky = new List<Radek>();
            int si = 0;
            for (int i = 0; i < cors.Count; i++)
            {
                var c = cors[i];
                while (si + 1 < states.Count && states[si + 1].TimeStamp <= c.TimeStamp) si++;
                var pred = states[si];
                var po = states.Skip(si + 1).FirstOrDefault(s => (s.TimeStamp - c.TimeStamp).TotalSeconds >= 0.05) ?? pred;
                double nx = -Math.Sin(c.PoseTheta), ny = Math.Cos(c.PoseTheta);   // leva normala kurzu ~ normala cesty
                double plat = Plat(pred, nx, ny);
                double sigmaFit = c.SigmaLateral;
                double rRec = sigmaFit * sigmaFit + stdRec * stdRec;
                double kRec = plat / (plat + rRec);
                // skutecny pricny posun mezi stavem pred a po mereni, minus co vysvetli rychlost
                double dt = (po.TimeStamp - pred.TimeStamp).TotalSeconds;
                double dx = po.X - pred.X - Math.Cos(pred.Theta) * pred.V * dt;
                double dy = po.Y - pred.Y - Math.Sin(pred.Theta) * pred.V * dt;
                double krokSkut = dx * nx + dy * ny;
                bool zmenaWay = i > 0 && cors[i - 1].WayId != c.WayId;
                radky.Add(new Radek { T = c.TimeStamp, Nu = c.LateralDisagreement, SigmaFit = sigmaFit, PLat = plat, KRec = kRec,
                                      KrokModel = kRec * c.LateralDisagreement, KrokSkut = krokSkut, ZmenaWay = zmenaWay, WayId = c.WayId });
            }
            var absNu = radky.Select(r => Math.Abs(r.Nu)).OrderBy(x => x).ToList();
            Console.WriteLine($"  |inovace| [m]: p50 {Q(absNu, .5):F2}  p90 {Q(absNu, .9):F2}  p99 {Q(absNu, .99):F2}  max {absNu.Last():F2}; "
                              + $"nad 0,5 m: {absNu.Count(x => x > 0.5)}, nad 1 m: {absNu.Count(x => x > 1)}, nad 2 m: {absNu.Count(x => x > 2)}");
            var sf = radky.Select(r => r.SigmaFit).OrderBy(x => x).ToList();
            var pl = radky.Select(r => Math.Sqrt(r.PLat)).OrderBy(x => x).ToList();
            var kr = radky.Select(r => r.KRec).OrderBy(x => x).ToList();
            Console.WriteLine($"  sigma prolozeni [m]: p50 {Q(sf, .5):F3}  p90 {Q(sf, .9):F3}; sigma pozy pricne pred merenim [m]: p50 {Q(pl, .5):F3}  p90 {Q(pl, .9):F3}; "
                              + $"K_rec = P/(P+R): p50 {Q(kr, .5):F2}  p90 {Q(kr, .9):F2}");
            // Overeni modelu: skutecny krok proti K*nu u velkych inovaci.
            var velke = radky.Where(r => Math.Abs(r.Nu) > jumpM).ToList();
            if (velke.Count > 0)
            {
                double sx = 0, sxx = 0, sxy = 0; int n = 0;
                foreach (var r in velke) { sx += r.KrokModel; sxx += r.KrokModel * r.KrokModel; sxy += r.KrokModel * r.KrokSkut; n++; }
                double slope = sxx > 0 ? sxy / sxx : double.NaN;
                Console.WriteLine($"  overeni modelu (|inovace| > {jumpM} m, n={n}): skutecny krok / (K_rec*inovace) = {slope:F2} "
                                  + "(1,0 = fuze delala presne to, co 1-D model rika; <1 = jine mereni krok tlumi)");
                Console.WriteLine($"  {"cas",-11} {"inovace",8} {"sigmaFit",8} {"sigPoza",8} {"K_rec",6} {"krok model",10} {"krok skut",10}  way");
                foreach (var r in velke.OrderByDescending(r => Math.Abs(r.Nu)).Take(15).OrderBy(r => r.T))
                    Console.WriteLine($"  {Cas(r.T),-11} {r.Nu,8:F2} {r.SigmaFit,8:F3} {Math.Sqrt(r.PLat),8:F3} {r.KRec,6:F2} {r.KrokModel,10:F2} {r.KrokSkut,10:F2}  {r.WayId}{(r.ZmenaWay ? " <- ZMENA HRANY" : "")}");
                int zmen = velke.Count(r => r.ZmenaWay);
                Console.WriteLine($"  velkych inovaci pri ZMENE hrany (prirazeni): {zmen} z {velke.Count}"
                                  + (zmen * 2 >= velke.Count ? "  <- vetsina: problem prirazeni hrany, sigma je spatny knoflik" : ""));
            }

            // Kalibrace: fuze (fixed-lag smoother, dalsi merenia v okne) dela kroky mensi nez cisty
            // skalarni vzorec. Pomer skutecny/modelovy krok se pouzije jako nasobek zesileni v replayi,
            // aby protifakticke kroky byly ve stejnych jednotkach jako ty skutecne.
            double kal = 1;
            if (velke.Count >= 5)
            {
                double sxx = velke.Sum(r => r.KrokModel * r.KrokModel), sxy = velke.Sum(r => r.KrokModel * r.KrokSkut);
                if (sxx > 0 && sxy / sxx > 0.05 && sxy / sxx < 1.5) kal = sxy / sxx;
            }
            double sumRec = radky.Sum(r => Math.Abs(kal * r.KRec * r.Nu));
            Console.WriteLine($"  kalibrace zesileni pro replay: K_eff = {kal:F2} x K; soucet |kroku| koridoru za zaznam (kalibrovany): {sumRec:F1} m "
                              + "= kolik pricne korekce koridor celkem udelal (bez nej by to byl drift pozy)");

            // ---------- 2. Procesni sum pricne ----------
            // q = rust P mezi po sobe jdoucimi merenimi koridoru (P pred merenim k+1 minus P po mereni k) / dt.
            var qs = new List<double>();
            for (int i = 1; i < radky.Count; i++)
            {
                double dt = (radky[i].T - radky[i - 1].T).TotalSeconds;
                if (dt <= 0 || dt > 5) continue;
                double pPo = (1 - radky[i - 1].KRec) * radky[i - 1].PLat;
                double q = (radky[i].PLat - pPo) / dt;
                if (q > 0) qs.Add(q);
            }
            qs.Sort();
            double qMed = qs.Count > 0 ? Q(qs, .5) : 0.05;
            Console.WriteLine();
            Console.WriteLine("=== 2. PRICNY PROCESNI SUM Z RUSTU P MEZI MERENIMI ===");
            Console.WriteLine($"  q [m^2/s]: p50 {qMed:F4}  p90 {(qs.Count > 0 ? Q(qs, .9) : 0):F4}  (n={qs.Count}); "
                              + $"za typicky odstup mereni {(radky.Count > 1 ? (radky[^1].T - radky[0].T).TotalSeconds / (radky.Count - 1) : 0):F2} s naroste sigma pozy o ~{Math.Sqrt(qMed * Math.Max(0.1, (radky.Count > 1 ? (radky[^1].T - radky[0].T).TotalSeconds / (radky.Count - 1) : 0.5))):F2} m");

            // ---------- 3. Protifakticky replay ----------
            Console.WriteLine();
            Console.WriteLine("=== 3. PROTIFAKTICKY 1-D REPLAY PRO KANDIDATY corridorstd ===");
            double hdopMed = gps.Count > 0 ? gps.Where(g => g.Hdop > 0).Select(g => g.Hdop).DefaultIfEmpty(1.5).Median() : 1.5;
            double rGps = Math.Pow(gpsPosStd * Math.Max(1, hdopMed), 2);
            double rateC = radky.Count > 1 ? (radky.Count - 1) / (radky[^1].T - radky[0].T).TotalSeconds : 1;
            Console.WriteLine($"  GPS: sigma {gpsPosStd}x{hdopMed:F1} = {Math.Sqrt(rGps):F0} m, 10 Hz; koridor {rateC:F1} merenia/s");
            Console.WriteLine($"  {"sigma",6} {"R'",8} {"K' p50",7} {"krok p50",9} {"krok p90",9} {"krok max",9} {">0,3 m",7} {">0,5 m",7} {"sigPoza p50",11} {"|delta| p50",11} {"|delta| p90",11} {"|delta| max",11} {"info kor:GPS",12}");
            foreach (double sc in kandidati)
            {
                double P = radky[0].PLat, delta = 0;
                var kroky = new List<double>(); var deltas = new List<double>(); var sig = new List<double>(); var ks = new List<double>();
                DateTime tPrev = radky[0].T;
                foreach (var r in radky)
                {
                    double dt = Math.Max(0, (r.T - tPrev).TotalSeconds); tPrev = r.T;
                    P += qMed * dt;
                    double R = r.SigmaFit * r.SigmaFit + sc * sc;
                    double K = kal * P / (P + R);
                    double nuCf = r.Nu - delta;
                    double s = K * nuCf;
                    double sRec = kal * r.KRec * r.Nu;
                    delta = delta + s - sRec;
                    P = (1 - P / (P + R)) * P;
                    kroky.Add(Math.Abs(s)); deltas.Add(Math.Abs(delta)); sig.Add(Math.Sqrt(P)); ks.Add(K);
                }
                kroky.Sort(); deltas.Sort(); sig.Sort(); ks.Sort();
                double rTyp = Q(radky.Select(r => r.SigmaFit * r.SigmaFit).OrderBy(x => x).ToList(), .5) + sc * sc;
                double info = (rateC / rTyp) / (10.0 / rGps);
                Console.WriteLine($"  {sc,6:F2} {rTyp,8:F3} {Q(ks, .5),7:F2} {Q(kroky, .5),9:F2} {Q(kroky, .9),9:F2} {kroky.Last(),9:F2} {kroky.Count(x => x > 0.3),7} {kroky.Count(x => x > 0.5),7} {Q(sig, .5),11:F2} {Q(deltas, .5),11:F2} {Q(deltas, .9),11:F2} {deltas.Last(),11:F1} {info,12:F0}"
                                  + (Math.Abs(sc - stdRec) < 1e-9 ? "  <- v zaznamu" : ""));
            }
            Console.WriteLine("  krok = |K'·(inovace − delta)| za jedno mereni; delta = odchylka protifakticke trajektorie od zaznamenane (pricne);");
            Console.WriteLine("  sigPoza = pricna sigma pozy po mereni; info kor:GPS = (kadence/R) koridoru proti GPS za sekundu.");
            Console.WriteLine("  POZOR: 1-D model bez GPS a bez zmeny prirazeni hrany; velka delta znamena, ze by robot jel jinudy a merenia by byla jina.");

            // ---------- 4. Protifakticky replay pro LIMIT KROKU (corridorslew=) ----------
            if (slewKandidati != null && slewKandidati.Length > 0)
                Blok4Pricne(radky, slewKandidati, stdProSlew ?? stdRec, stdProSlew.HasValue, qMed, kal);

            // ---------- 4b. Totez pro KURZ (corridorheadingslew=) ----------
            if (hdgSlewKandidatiDegS != null && hdgSlewKandidatiDegS.Length > 0)
                Blok4bKurz(rec, states, hdgSlewKandidatiDegS);
        }

        private static void Blok4Pricne(List<Radek> radky, double[] slewKandidati, double sigmaSlew, bool sigmaZArg,
                                        double qMed, double kal)
        {
            Console.WriteLine();
            Console.WriteLine($"=== 4. PROTIFAKTICKY 1-D REPLAY PRO KANDIDATY corridorslew= (limit kroku; sigma {sigmaSlew:F2} m{(sigmaZArg ? " z --std" : " ze zaznamu")}) ===");
            Console.WriteLine("  limit na mereni L = slew x dt, dt = odstup od predchoziho mereni orezany na 0,02-1 s; kdyz by krok K*nu prekrocil L,");
            Console.WriteLine("  nafoukne se R na P*(|nu|/L - 1) (tak to dela Ekf.UpdateStep). Kroky jsou kalibrovane K_eff, tedy skutecny posun pozy je POD limitem.");
            Console.WriteLine($"  {"slew",6} {"omezeno",8} {"krok p50",9} {"krok p90",9} {"krok max",9} {">0,3 m",7} {">0,5 m",7} {"sigPoza p50",11} {"|delta| p50",11} {"|delta| p90",11} {"|delta| max",11} {"nad 1 m [s]",11}");
            foreach (double slew in slewKandidati)
            {
                double P = radky[0].PLat, delta = 0;
                var kroky = new List<double>(); var deltas = new List<double>(); var sig = new List<double>();
                int omezeno = 0;
                double sekundNad1m = 0;
                DateTime tPrev = radky[0].T;
                bool prvni = true;
                foreach (var r in radky)
                {
                    double dt = Math.Max(0, (r.T - tPrev).TotalSeconds);
                    double dtLim = prvni || r.T < tPrev ? SlewDtCap : Math.Min(SlewDtCap, Math.Max(SlewDtFloor, dt));
                    prvni = false;
                    tPrev = r.T;
                    P += qMed * dt;
                    double R = r.SigmaFit * r.SigmaFit + sigmaSlew * sigmaSlew;
                    double nuCf = r.Nu - delta;
                    if (slew > 0)
                    {
                        double L = slew * dtLim;
                        double krokRaw = P / (P + R) * Math.Abs(nuCf);
                        if (krokRaw > L)
                        {
                            R = Math.Max(R, P * (Math.Abs(nuCf) / L - 1));
                            omezeno++;
                        }
                    }
                    double K = kal * P / (P + R);
                    double s = K * nuCf;
                    double sRec = kal * r.KRec * r.Nu;
                    delta = delta + s - sRec;
                    P = (1 - P / (P + R)) * P;
                    kroky.Add(Math.Abs(s)); deltas.Add(Math.Abs(delta)); sig.Add(Math.Sqrt(P));
                    if (Math.Abs(delta) > 1.0) sekundNad1m += dt;
                }
                kroky.Sort(); deltas.Sort(); sig.Sort();
                Console.WriteLine($"  {slew,6:F2} {omezeno,8} {Q(kroky, .5),9:F2} {Q(kroky, .9),9:F2} {kroky.Last(),9:F2} {kroky.Count(x => x > 0.3),7} {kroky.Count(x => x > 0.5),7} {Q(sig, .5),11:F2} {Q(deltas, .5),11:F2} {Q(deltas, .9),11:F2} {deltas.Last(),11:F1} {sekundNad1m,11:F0}"
                                  + (slew == 0 ? "  <- bez limitu" : ""));
            }
            Console.WriteLine("  omezeno = kolik mereni narazilo na limit; nad 1 m [s] = jak dlouho by protifakticka trajektorie byla dal nez 1 m od zaznamenane");
            Console.WriteLine("  (= cena za pomalejsi stazeni driftu). Kolik z driftu bylo PRAVDIVEHO, 1-D model nerekne - viz blok 1 (zmeny hrany).");
        }

        /// <summary>
        /// <b>Blok 4b (21. 9. 2026): kandidáti `corridorheadingslew=`</b> — týž 1-D replay jako blok 4,
        /// ale pro <b>kurz</b>. Inovace je <c>−HeadingDisagreementRad</c> (měření − póza, tak jak ji
        /// skládá <c>CorridorLocalizer.Send</c>: <c>θ_meas = θ_pose + (sklon hrany − směr koridoru)</c>),
        /// P je <c>Covariance[ITh, ITh]</c> stavu před měřením, R = σ směru z proložení² +
        /// <c>corridorheadingstd</c>² (ze záznamu). Skutečný krok kurzu = Δθ mezi stavem před a po
        /// měření minus <c>ω·dt</c>; z velkých inovací se kalibruje K_eff jako u příčné osy.
        ///
        /// <para><b>Proč zvlášť:</b> grid je kotvený ve světě, takže otočení pózy o dθ posune jeho
        /// obsah o <c>R·dθ</c>. ⚠️ „−59°" u skoků z Kola 3b (14:25:05–07) ve výpisu <c>nav</c> je
        /// ale <b>směr posunu</b>, ne změna kurzu — změřeno tímhle blokem 21. 9. 2026: inovace kurzu
        /// byla −6,2°, krok −2,7°, a bez limitu je krok kurzu z koridoru max 2,0° (Kolo 3b) /
        /// 0,6° (Kolo 4), tedy nikdy nad tolerancí detektoru. Práh detektoru skoku pro kurz je
        /// <c>PoseJumpDetector.ToleranceRad</c> (5°), proto se počítají kroky nad 5° (a nad 2° jako
        /// přísnější měřítko).</para>
        ///
        /// <para>⚠️ Kompas (<c>imuheadinghz=1</c>) ani gyro v modelu nejsou — odchylka kurzu
        /// protifaktické trajektorie se stahuje jen dalšími měřeními koridoru, takže <c>delta</c>
        /// je <b>horní</b> odhad ceny.</para>
        /// </summary>
        private static void Blok4bKurz(RecordFile rec, List<RobotStateMsg> states, double[] kandidatiDegS)
        {
            const double R2D = 180.0 / Math.PI, D2R = Math.PI / 180.0;
            var cors = rec.ReadAll<RoadCorridorMsg>("RoadCorridorMsg").Where(c => c.EmittedHeading && c.HasPose)
                          .OrderBy(c => c.TimeStamp).ToList();
            double hstdDeg = ParamZLogu(rec, "corridorheadingstd", 0);
            double hstd = hstdDeg * D2R;
            Console.WriteLine();
            Console.WriteLine($"=== 4b. PROTIFAKTICKY 1-D REPLAY PRO KANDIDATY corridorheadingslew= (limit kroku KURZU; corridorheadingstd={hstdDeg} st. ze zaznamu) ===");
            Console.WriteLine($"  merenia kurzu poslana do fuze (EmittedHeading): {cors.Count}");
            if (cors.Count < 10) { Console.WriteLine("  Malo dat."); return; }

            // radky: inovace [rad], sigma prolozeni, P_theta pred merenim, K_rec, skutecny krok
            var radky = new List<Radek>();
            int si = 0;
            for (int i = 0; i < cors.Count; i++)
            {
                var c = cors[i];
                while (si + 1 < states.Count && states[si + 1].TimeStamp <= c.TimeStamp) si++;
                var pred = states[si];
                var po = states.Skip(si + 1).FirstOrDefault(s => (s.TimeStamp - c.TimeStamp).TotalSeconds >= 0.05) ?? pred;
                double pth = PTheta(pred);
                double sigmaFit = c.SigmaDirectionRad;
                double rRec = sigmaFit * sigmaFit + hstd * hstd;
                double kRec = pth / (pth + rRec);
                double dt = (po.TimeStamp - pred.TimeStamp).TotalSeconds;
                double dth = Uhel(po.Theta - pred.Theta) - pred.Omega * dt;
                double nu = -c.HeadingDisagreementRad;   // mereni - poza (viz CorridorLocalizer.Send)
                radky.Add(new Radek { T = c.TimeStamp, Nu = nu, SigmaFit = sigmaFit, PLat = pth, KRec = kRec,
                                      KrokModel = kRec * nu, KrokSkut = dth, ZmenaWay = i > 0 && cors[i - 1].WayId != c.WayId, WayId = c.WayId });
            }
            var absNu = radky.Select(r => Math.Abs(r.Nu) * R2D).OrderBy(x => x).ToList();
            var sf = radky.Select(r => r.SigmaFit * R2D).OrderBy(x => x).ToList();
            var pl = radky.Select(r => Math.Sqrt(r.PLat) * R2D).OrderBy(x => x).ToList();
            var kr = radky.Select(r => r.KRec).OrderBy(x => x).ToList();
            Console.WriteLine($"  |inovace kurzu| [st.]: p50 {Q(absNu, .5):F2}  p90 {Q(absNu, .9):F2}  p99 {Q(absNu, .99):F1}  max {absNu.Last():F1}; "
                              + $"nad 2 st.: {absNu.Count(x => x > 2)}, nad 5 st.: {absNu.Count(x => x > 5)}, nad 20 st.: {absNu.Count(x => x > 20)}");
            Console.WriteLine($"  sigma smeru z prolozeni [st.]: p50 {Q(sf, .5):F2}  p90 {Q(sf, .9):F2}; sigma kurzu pozy pred merenim [st.]: p50 {Q(pl, .5):F2}  p90 {Q(pl, .9):F2}; "
                              + $"K_rec: p50 {Q(kr, .5):F2}  p90 {Q(kr, .9):F2}");

            // Kalibrace K_eff z velkych inovaci (nad 2 st.)
            double prahRad = 2 * D2R;
            var velke = radky.Where(r => Math.Abs(r.Nu) > prahRad).ToList();
            double kal = 1;
            if (velke.Count >= 5)
            {
                double sxx = velke.Sum(r => r.KrokModel * r.KrokModel), sxy = velke.Sum(r => r.KrokModel * r.KrokSkut);
                double slope = sxx > 0 ? sxy / sxx : double.NaN;
                Console.WriteLine($"  overeni modelu (|inovace| > 2 st., n={velke.Count}): skutecny krok / (K_rec*inovace) = {slope:F2}");
                Console.WriteLine($"  {"cas",-11} {"inovace",8} {"sigmaFit",8} {"sigPoza",8} {"K_rec",6} {"krok model",10} {"krok skut",10}  way   [stupne]");
                foreach (var r in velke.OrderByDescending(r => Math.Abs(r.Nu)).Take(15).OrderBy(r => r.T))
                    Console.WriteLine($"  {Cas(r.T),-11} {r.Nu * R2D,8:F2} {r.SigmaFit * R2D,8:F2} {Math.Sqrt(r.PLat) * R2D,8:F2} {r.KRec,6:F2} {r.KrokModel * R2D,10:F2} {r.KrokSkut * R2D,10:F2}  {r.WayId}{(r.ZmenaWay ? " <- ZMENA HRANY" : "")}");
                if (sxx > 0 && slope > 0.05 && slope < 1.5) kal = slope;
            }
            double sumRec = radky.Sum(r => Math.Abs(kal * r.KRec * r.Nu)) * R2D;
            Console.WriteLine($"  kalibrace zesileni pro replay: K_eff = {kal:F2} x K; soucet |kroku| kurzu za zaznam (kalibrovany): {sumRec:F0} st.");

            // Procesni sum kurzu z rustu P mezi merenimi
            var qs = new List<double>();
            for (int i = 1; i < radky.Count; i++)
            {
                double dt = (radky[i].T - radky[i - 1].T).TotalSeconds;
                if (dt <= 0 || dt > 5) continue;
                double pPo = (1 - radky[i - 1].KRec) * radky[i - 1].PLat;
                double q = (radky[i].PLat - pPo) / dt;
                if (q > 0) qs.Add(q);
            }
            qs.Sort();
            double qMed = qs.Count > 0 ? Q(qs, .5) : 1e-4;
            Console.WriteLine($"  procesni sum kurzu q [st.^2/s]: p50 {qMed * R2D * R2D:F4}  (n={qs.Count})");

            Console.WriteLine("  limit na mereni L = slew x dt [st.], dt orezany na 0,02-1 s; kroky kalibrovane K_eff; prah detektoru skoku kurzu je 5 st. (PoseJumpDetector.ToleranceRad)");
            Console.WriteLine($"  {"slew st/s",9} {"omezeno",8} {"krok p50",9} {"krok p90",9} {"krok max",9} {">2 st",6} {">5 st",6} {"sigPoza p50",11} {"|delta| p50",11} {"|delta| p90",11} {"|delta| max",11} {"nad 5st [s]",11}");
            foreach (double slewDeg in kandidatiDegS)
            {
                double slew = slewDeg * D2R;
                double P = radky[0].PLat, delta = 0;
                var kroky = new List<double>(); var deltas = new List<double>(); var sig = new List<double>();
                int omezeno = 0; double sekundNad = 0;
                DateTime tPrev = radky[0].T; bool prvni = true;
                foreach (var r in radky)
                {
                    double dt = Math.Max(0, (r.T - tPrev).TotalSeconds);
                    double dtLim = prvni || r.T < tPrev ? SlewDtCap : Math.Min(SlewDtCap, Math.Max(SlewDtFloor, dt));
                    prvni = false; tPrev = r.T;
                    P += qMed * dt;
                    double R = r.SigmaFit * r.SigmaFit + hstd * hstd;
                    double nuCf = Uhel(r.Nu - delta);
                    if (slew > 0)
                    {
                        double L = slew * dtLim;
                        double krokRaw = P / (P + R) * Math.Abs(nuCf);
                        if (krokRaw > L) { R = Math.Max(R, P * (Math.Abs(nuCf) / L - 1)); omezeno++; }
                    }
                    double K = kal * P / (P + R);
                    double s = K * nuCf;
                    double sRec = kal * r.KRec * r.Nu;
                    delta = Uhel(delta + s - sRec);
                    P = (1 - P / (P + R)) * P;
                    kroky.Add(Math.Abs(s) * R2D); deltas.Add(Math.Abs(delta) * R2D); sig.Add(Math.Sqrt(P) * R2D);
                    if (Math.Abs(delta) * R2D > 5) sekundNad += dt;
                }
                kroky.Sort(); deltas.Sort(); sig.Sort();
                Console.WriteLine($"  {slewDeg,9:F1} {omezeno,8} {Q(kroky, .5),9:F2} {Q(kroky, .9),9:F2} {kroky.Last(),9:F2} {kroky.Count(x => x > 2),6} {kroky.Count(x => x > 5),6} {Q(sig, .5),11:F2} {Q(deltas, .5),11:F2} {Q(deltas, .9),11:F2} {deltas.Last(),11:F1} {sekundNad,11:F0}"
                                  + (slewDeg == 0 ? "  <- bez limitu" : ""));
            }
            Console.WriteLine("  omezeno = kolik mereni narazilo na limit; nad 5st [s] = jak dlouho by kurz protifakticke trajektorie byl dal nez 5 st. od zaznamenaneho.");
            Console.WriteLine("  POZOR: kompas (imuheadinghz=1) a gyro v modelu nejsou - odchylka kurzu se stahuje jen dalsimi merenimi koridoru, takze delta je HORNI odhad.");
        }

        private static double PTheta(RobotStateMsg s)
        {
            var P = s.Covariance;
            if (P == null || P.RowCount < 3 || P.ColumnCount < 3) return 1e-4;
            return Math.Max(1e-8, P[2, 2]);   // EKFModel.ITh = 2
        }

        private static double Uhel(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        private sealed class Radek
        {
            public DateTime T; public double Nu, SigmaFit, PLat, KRec, KrokModel, KrokSkut; public bool ZmenaWay; public long WayId;
        }

        private static double Plat(RobotStateMsg s, double nx, double ny)
        {
            var P = s.Covariance;
            if (P == null || P.RowCount < 2 || P.ColumnCount < 2) return 0.05;
            double v = nx * nx * P[0, 0] + 2 * nx * ny * P[0, 1] + ny * ny * P[1, 1];
            return Math.Max(1e-6, v);
        }

        private static double ParamZLogu(RecordFile rec, string klic, double fallback)
        {
            foreach (var e in rec.Index.Where(e => e.MsgName == "Info").Take(400))
                if (rec.Read(e) is Info info && info.Message != null)
                {
                    string m = info.Message.Trim();
                    if (m.StartsWith(klic + "=", StringComparison.Ordinal))
                    {
                        string v = m.Substring(klic.Length + 1).Split(' ')[0];
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
                    }
                }
            return fallback;
        }

        private static double Median(this IEnumerable<double> xs) { var l = xs.OrderBy(x => x).ToList(); return l.Count == 0 ? double.NaN : Q(l, .5); }
        private static double Q(List<double> sorted, double q)
            => sorted.Count == 0 ? double.NaN : sorted[Math.Min(sorted.Count - 1, Math.Max(0, (int)Math.Round(q * (sorted.Count - 1))))];
        private static string Cas(DateTime t) => t.ToString("HH:mm:ss.f", CultureInfo.InvariantCulture);
    }
}
