using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Hrboly pod robotem</b> — je v záznamu vidět, že na tutéž nerovnost najede nejdřív
    /// přední hnaná náprava a o <i>rozvor</i> později zadní pasivní kolo? A klopí se robot
    /// v tu chvíli víc, když zrovna brzdí?
    ///
    /// <para><b>Nač to je.</b> Dva záměry (registr <c>lp-drsnost-povrchu-rychlostni-strop</c>
    /// a <c>lp-reflex-klopeni-zadni-kolo</c>) stojí na jednom předpokladu: že <b>první ráz
    /// ohlašuje druhý</b> a že ten druhý je ten nebezpečný. Než se kvůli tomu sáhne na lokální
    /// mapu nebo na řídicí smyčku, musí se ten předpoklad <b>změřit</b> — po zkušenosti
    /// s <c>VAlong</c> (7. 9. 2026), který robota přibrzdil v 77 % plánů, protože se zapnul
    /// dřív, než se změřilo, jak často váže.</para>
    ///
    /// <para><b>Ground truth je náklon z VN100, ne RMS zrychlení</b> (rozhodnutí autora
    /// 22. 9. 2026): otřes sám o sobě nic neznamená, nebezpečné je <b>klopení dopředu</b>.
    /// Akcelerometr se přesto čte — ale jen jako <b>rozhodčí konvence znamének</b> (blok 1),
    /// ne jako měřidlo.</para>
    ///
    /// <para><b>Dvě nezávislé cesty k témuž číslu.</b> Blok 2 hledá rozvor
    /// <b>autokorelací v dráze</b> (bez prahů a bez detekce událostí), blok 3 <b>párováním
    /// špiček</b> (s prahem). Kdyby vyšly různě, neplatí ani jedno.</para>
    ///
    /// <para>⚠️ <b>Past, na kterou se musí dávat pozor:</b> obvod kola je
    /// <c>2π·0,0808 = 0,508 m</c>, tedy <b>řádově týž rozměr jako rozvor</b>. Nevyvážené kolo
    /// nebo vzorek dezénu vyrobí v dráze periodický signál se <b>stejnou</b> periodou a
    /// v autokorelaci vypadá jako ozvěna hrbolu. Rozliší je <b>harmonické</b>: otáčka kola dá
    /// vrcholy i na 2× a 3× (je to periodický jev), kdežto ozvěna hrbolu je <b>jednorázová</b>
    /// a na násobcích rozvoru nic nemá. Blok 2 proto tiskne celou křivku, ne jen vrchol.</para>
    ///
    /// <para><b>Co tohle měřidlo NEUMÍ:</b> říct, jestli byl hrbol vidět v hloubkové kameře.
    /// Polární grid svou drsnost (<c>StdZ</c>) do záznamu neposílá, takže odpověď na
    /// „byl kořen v hloubce vidět?" vyžaduje replay snímků přes celou vizuální cestu — to je
    /// samostatný krok (viz <c>lp-drsnost-povrchu-rychlostni-strop</c>).</para>
    /// </summary>
    public static class BumpReport
    {
        /// <summary>Jak daleko dopredu se blok 9 jeste pta [m].</summary>
        private const double Arg_maxAhead = 4.0;

        private sealed class Sample
        {
            public double T;        // cas od zacatku zaznamu [s]
            public double Pitch;    // KLOPENI (nos nahoru +) - osu i znamenko urci blok 1
            public double YprPitch;  // ypr.Pitch [rad] tak, jak ho vraci YawPitchRoll
            public double YprRoll;   // ypr.Roll  [rad] tatáž poznamka
            public double GyroY;    // uhlova rychlost KLOPENI [rad/s] - osu urci blok 1
            public double GyroXRaw; // AngularVelocity.X
            public double GyroYRaw; // AngularVelocity.Y
            public double GyroZ;    // AngularVelocity.Z - staceni (primost jizdy)
            public double Roll;     // rychlost naklonu do STRANY [rad/s] - osa doplnkova ke klopeni
            public double AccX;     // zrychleni v ose X telesa [m/s2] - rozhodci konvence
            public double AccY;     // zrychleni v ose Y telesa [m/s2]
            public double AccZ;     // zrychleni v ose Z telesa [m/s2]
            public double SmX, SmY, SmZ, SmMag;   // tytez slozky po vyhlazeni (blok 1)
            public double V;        // rychlost z motoru v tom case [m/s]
            public double S;        // ujeta draha od zacatku [m]
            public double Hp;       // gyroY po odecteni pomaleho prumeru [rad/s]
        }

        /// <summary>Jedna zachycena spicka (lokalni maximum |Hp| nad prahem).</summary>
        private sealed class Spike
        {
            public int Idx;
            public double T;
            public double S;
            public double V;
            public double Amp;          // |Hp| ve vrcholu [rad/s]
            public double NoseDownDeg;  // vychylka klopeni dopredu proti klidu pred udalosti [deg]
            public double GyroNoseDeg;  // totez z integrace gyra [deg] - nezavisla cesta
            public double SpanDeg;      // rozkmit klopeni vrchol-vrchol v okoli udalosti [deg]
            public double DvCmd;        // derivace prikazovane rychlosti v te chvili [m/s^2]
            public Spike Partner;       // nasledujici spicka v okne rozvoru (null = zadna)
            public double PairDist;     // vzdalenost k partnerovi [m]
            public double YawRate;      // nejvetsi |staceni| v okoli udalosti [rad/s]
            public double RollRate;     // nejvetsi |naklon do strany| tamtez [rad/s]
            public double VPre;         // rychlost PRED udalosti [m/s] - draha imunni vuci prokluzu
        }

        /// <param name="dsM">Krok rovnomerne mrizky v draze pro autokorelaci [m].</param>
        /// <param name="minSpeed">Pod touto rychlosti se vzorky neberou (stani) [m/s].</param>
        /// <param name="maxLagM">Nejvetsi zkoumane zpozdeni v draze [m].</param>
        /// <param name="madK">Prah spicky = median + madK * MAD z |Hp|.</param>
        /// <param name="refractoryM">Nejmensi rozestup dvou spicek [m].</param>
        /// <param name="pairLoM">Dolni mez okna, ve kterem se hleda partner [m].</param>
        /// <param name="pairHiM">Horni mez tehoz okna [m].</param>
        /// <param name="topN">Kolik nejsilnejsich udalosti vypsat.</param>
        /// <param name="hpSec">Sirka okna horni propusti [s]; 0 = bez propusti (kontrola).</param>
        /// <param name="fromArg">Zacatek okna: <c>HH:MM:SS</c> (hodiny ze zaznamu) nebo sekundy; null = od zacatku.</param>
        /// <param name="toArg">Konec okna, tamtez.</param>
        /// <param name="detailN">Kolik nejsilnejsich udalosti vypsat i se SYROVYM prubehem.</param>
        /// <param name="straightDegS">Do jakeho staceni se jizda povazuje za primou [deg/s].</param>
        /// <param name="rozvorM">Ocekavany rozvor [m] — misto, kde se ozvena podle predpovedi ma objevit.</param>
        /// <param name="depthN">Kolik snimku nejvyse precist pro blok 9 (0 = blok vynechat).</param>
        public static void Run(RecordFile rec, double dsM, double minSpeed, double maxLagM,
                               double madK, double refractoryM, double pairLoM, double pairHiM,
                               int topN, double hpSec, string fromArg, string toArg, int detailN,
                               double straightDegS, double rozvorM, int depthN)
        {
            var s = new List<Sample>();
            var mot = new List<(double T, double V)>();
            var cmd = new List<(double T, double V, bool Held, bool EStop)>();
            DateTime t0 = DateTime.MinValue;
            var zdroje = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int relativnich = 0;

            foreach (var e in rec.Index)
            {
                if (e.MsgName != "IMUState" && e.MsgName != "MotorStateBase"
                    && e.MsgName != "DriveCommandMsg") continue;
                var msg = rec.Read(e);
                switch (msg)
                {
                    case IMUState i when i.Rotation.HasValue && i.AngularVelocity.HasValue:
                        // Tyz duvod jako ve Vn100Report: IMU je v robotu vic a T265 posila
                        // RELATIVNI yaw. Klopeni by z ni sice slo cist (pitch je absolutni
                        // i u ni), ale michat dva senzory s ruznou kadenci do jedne rady by
                        // rozbilo jak autokorelaci, tak prahovani.
                        if (!i.HasAbsoluteHeading) { relativnich++; break; }
                        string jm = i.Name ?? "(bez jmena)";
                        zdroje.TryGetValue(jm, out int n);
                        zdroje[jm] = n + 1;
                        var ypr = i.YPR();
                        if (ypr == null) break;
                        s.Add(new Sample
                        {
                            T = Sec(i.TimeStamp, ref t0),
                            YprPitch = ypr.Pitch,
                            YprRoll = ypr.Roll,
                            GyroXRaw = i.AngularVelocity.Value.X,
                            GyroYRaw = i.AngularVelocity.Value.Y,
                            GyroZ = i.AngularVelocity.Value.Z,
                            AccX = i.Acceleration?.X ?? double.NaN,
                            AccY = i.Acceleration?.Y ?? double.NaN,
                            AccZ = i.Acceleration?.Z ?? double.NaN,
                        });
                        break;
                    case MotorStateBase mo:
                        mot.Add((Sec(mo.TimeStamp, ref t0),
                                 0.5 * (mo.LeftWheelSpeed + mo.RightWheelSpeed)));
                        break;
                    case DriveCommandMsg d:
                        cmd.Add((Sec(d.TimeStamp, ref t0), d.Speed, d.Held, d.EmergencyStop));
                        break;
                }
            }

            s.Sort((a, b) => a.T.CompareTo(b.T));
            mot.Sort((a, b) => a.T.CompareTo(b.T));
            cmd.Sort((a, b) => a.T.CompareTo(b.T));

            // Absolutni cas prvniho vzorku - bez nej nejde rict "v 14:12:20", a prave takhle
            // si clovek pamatuje, kde se na trati neco stalo.
            t0Abs = t0;

            double odS = Resolve(fromArg, double.NegativeInfinity);
            double doS = Resolve(toArg, double.PositiveInfinity);
            bool okno = !double.IsInfinity(odS) || !double.IsInfinity(doS);

            Console.WriteLine("=== 0. CO ZAZNAM NESE ===");
            foreach (var z in zdroje) Console.WriteLine($"  IMUState [{z.Key}]  {z.Value} vzorku");
            if (relativnich > 0)
                Console.WriteLine($"  (vynechano {relativnich} vzorku ze zdroju s relativnim yaw)");
            if (s.Count < 500 || mot.Count < 50)
            {
                Console.WriteLine("Prilis malo vzorku IMU nebo motoru - nelze merit.");
                return;
            }
            double dur = s[s.Count - 1].T - s[0].T;
            Console.WriteLine(F("  IMU: {0} vzorku, {1:F1} s, kadence {2:F1} Hz",
                                s.Count, dur, (s.Count - 1) / Math.Max(1e-9, dur)));
            Console.WriteLine(F("  motor: {0} vzorku ({1:F1} Hz), prikazy smycky: {2} ({3:F1} Hz)",
                                mot.Count, mot.Count / Math.Max(1e-9, dur),
                                cmd.Count, cmd.Count / Math.Max(1e-9, dur)));
            if (s.All(x => double.IsNaN(x.AccX)))
                Console.WriteLine("  !! zaznam nenese zrychleni - blok 1 nebude mit rozhodciho");

            // Rychlost a ujeta draha ke kazdemu vzorku IMU. Draha se integruje z RYCHLOSTI KOL,
            // ne z pozy fuze: poza skace pri korekcich (19. 9. az o 4 m) a skok v draze by
            // v autokorelaci vyrobil nesmyslnou ozvenu presne tam, kde se hleda.
            InterpolateSpeed(s, mot);
            double sAcc = 0;
            for (int i = 0; i < s.Count; i++)
            {
                if (i > 0)
                {
                    double dt = s[i].T - s[i - 1].T;
                    if (dt > 0 && dt < 0.5) sAcc += 0.5 * (s[i].V + s[i - 1].V) * dt;
                }
                s[i].S = sAcc;
            }
            var jizda = s.Where(x => x.V >= minSpeed).ToList();
            Console.WriteLine(F("  ujeta draha {0:F1} m; vzorku v jizde (v >= {1:F2} m/s): {2} ({3:F1} %)",
                                sAcc, minSpeed, jizda.Count, 100.0 * jizda.Count / s.Count));
            if (jizda.Count > 0)
            {
                var sv = new Stats("  rychlost v jizde [m/s]");
                foreach (var x in jizda) sv.Add(x.V);
                Console.WriteLine(sv.Line());
            }
            Console.WriteLine();
            if (jizda.Count < 200) { Console.WriteLine("Prilis malo jizdy - konec."); return; }

            // ⚠️ Poradi je POVINNE, a to dvakrat:
            // (a) blok 1 teprve urci, KTERA osa gyroskopu je klopeni, a horni propust uz s ni
            //     pracuje. Kdyz se prohodi, filtruje se neinicializovana nula a cely zbytek
            //     reportu vyjde prazdny - presne to se pri psani stalo.
            // (b) konvence se urcuji nad CELYM zaznamem, teprve pak se orezava okno. Montaz
            //     senzoru se behem jizdy nemeni, kdezto na 40s okne je klidovych vzorku par
            //     stovek, osy se nerozlisi a vyber by pripadl na jinou osu nez nad celym
            //     zaznamem - tise, jen s varovanim. Presne to se stalo pri prvnim behu nad
            //     usekem s koreny: okno vybralo ypr.Roll a gyro X misto ypr.Pitch a gyro Y.
            int noseUpSign = Conventions(s);

            // ⚠️ OREZ OKNA. Prumer pres cely zaznam utopi par skutecnych zakopnuti mezi stovkami
            // metru bezne jizdy - par udalosti na 1561 prahovych prekroceni je zlomek procenta,
            // tedy hluboko v sumu. Kdyz clovek vi, KDY se to stalo, je spravne se ptat jen tam.
            var sVse = s;   // plny seznam kvuli draze pred zacatkem okna (blok 9)
            if (okno)
            {
                int predtim = s.Count;
                s = s.Where(x => x.T >= odS && x.T <= doS).ToList();
                cmd = cmd.Where(x => x.T >= odS - 1 && x.T <= doS + 1).ToList();
                Console.WriteLine();
                Console.WriteLine(F("=== OKNO {0} - {1} (relativne {2:F1} - {3:F1} s), {4} z {5} vzorku IMU ===",
                                    Abs(odS), Abs(doS), double.IsInfinity(odS) ? 0 : odS, doS,
                                    s.Count, predtim));
                Console.WriteLine("  Vsechna cisla nize plati JEN pro tohle okno; prahy (MAD) se pocitaji z nej,");
                Console.WriteLine("  takze se s celozaznamovym behem primo neporovnavaji. Konvence (blok 1)");
                Console.WriteLine("  a ujeta draha se naopak berou z CELEHO zaznamu.");
                if (s.Count < 200) Console.WriteLine("  !! v okne je malo vzorku");
            }
            Console.WriteLine();

            // Horni propust: odecist pomaly prumer, aby ve signalu zustal jen raz, ne zataceni
            // a naklon svahu. Okno 0,3 s je kompromis - kratsi nez odstup obou naprav pri
            // beznych rychlostech, delsi nez samotny raz.
            // ⚠️ Horni propust je filtr, a filtr sam umi vyrobit korelaci. Artefakt by ale byl
            // vazany na CAS (sirku okna), kdezto hledana ozvena na DRAHU - a protoze se rychlost
            // v zaznamu meni, blok 2 pocita v draze a artefakt se v nem rozmaze. Kdo tomu neveri,
            // ma --hp= : vrchol se pri jine sirce okna (i pri 0 = bez propusti) posunout NESMI.
            HighPass(s, hpSec);
            Console.WriteLine(F("  horni propust: okno {0:F2} s ({1})", hpSec,
                                hpSec <= 0 ? "VYPNUTA - kontrolni beh" : "odecten klouzavy prumer"));
            Console.WriteLine();
            double lag = AutoCorrelation(s, dsM, minSpeed, maxLagM);
            Roughness(s, minSpeed);
            var spikes = Detect(s, minSpeed, madK, refractoryM, noseUpSign, cmd);
            TriggeredAverage(s, spikes, dsM, maxLagM);
            EchoByDefect(s, spikes, dsM, maxLagM, straightDegS, rozvorM);
            PairSpikes(spikes, pairLoM, pairHiM, lag);
            TiltByCommand(spikes);
            TiltBySpeed(spikes);
            Braking(spikes);
            Top(spikes, topN);
            Detail(s, spikes, detailN);
            if (depthN > 0)
            {
                // Draha v libovolnem case se bere z PLNEHO seznamu - snimky, na kterych je misto
                // jeste daleko, lezi pred zacatkem okna.
                Func<double, double> draha = tt =>
                {
                    int i = Bisect(sVse.Count, k => sVse[k].T, tt);
                    if (i <= 0) return sVse[0].S;
                    if (i >= sVse.Count) return sVse[sVse.Count - 1].S;
                    double dd = sVse[i].T - sVse[i - 1].T;
                    double fr = dd > 0 ? (tt - sVse[i - 1].T) / dd : 0;
                    return sVse[i - 1].S + fr * (sVse[i].S - sVse[i - 1].S);
                };
                // ⚠️ PRVNI spicky, ne partneri: hrbol je pod robotem v okamziku, kdy na nej najede
                // PREDNI naprava, a ta je pocatkem telesoveho ramce. Partner je o 0,2-0,5 s pozdeji,
                // tedy o 0,2-0,5 m dal - a to je vic nez bunka gridu, takze by se hledalo vedle.
                // A jen ty nejsilnejsi: slabe spicky jsou z velke casti bezna vibrace.
                double prahU = spikes.Count == 0 ? 0
                    : spikes.Select(x => x.Amp).OrderByDescending(x => x).ElementAt(spikes.Count / 3);
                var udal = spikes.Where(x => x.Amp >= prahU).Select(x => (x.T, x.S)).ToList();
                BumpDepthReport.Run(rec, udal, draha,
                                    double.IsInfinity(odS) ? s[0].T : odS,
                                    double.IsInfinity(doS) ? s[s.Count - 1].T : doS,
                                    Arg_maxAhead, 0.35, depthN);
            }
        }

        // ---------------------------------------------------------------- blok 1

        /// <summary>
        /// <b>Blok 1 — znaménka.</b> Které znaménko <c>ypr.Pitch</c> znamená nos nahoru a souhlasí
        /// s ním gyroskop? Bez toho je „klopení dopředu" jen dohad — a znaménka už v tomhle
        /// projektu kousla (viz konvence rámce u registru 23, doc/imu-and-frames.md).
        ///
        /// <para><b>Rozhodčí je gravitace:</b> akcelerometr v klidu měří reakci, takže při nose
        /// nahoru o úhel θ je <c>a_x = +g·sin θ</c> (osa X vpřed, FLU). Směrnice regrese
        /// <c>a_x</c> na <c>sin(Pitch)</c> tedy musí vyjít +g, nebo −g — a to znaménko je
        /// odpověď.</para>
        /// </summary>
        private static int Conventions(List<Sample> s)
        {
            Console.WriteLine("=== 1. ZNAMENKA (rozhodci: gravitace a gyroskop) ===");
            // ⚠️ KTERE POLE JE VLASTNE KLOPENI. `YawPitchRoll(q, Euler.zxy)` sklada rotace
            // v poradi Z, X, Y a do pole `Pitch` uklada uhel PROSTREDNI rotace, tedy tu kolem
            // osy X. Pri telesovem ramci FLU (X vpred) je ale rotace kolem X naklon do STRANY
            // a klopeni je kolem Y - jmena poli tedy podle algebry neodpovidaji fyzice. Hadat
            // se o to nema smysl: rozhodcim je gravitace, ktera o svislici vi vsechno.
            //
            // Meri se UHEL proti UHLU, ne m/s2 proti sinu: akcelerometr ma zmerenou chybu
            // meritka +6,9 % (doc/imu-and-frames.md), takze regrese v m/s2 by nevysla 9,81 ani
            // pri spravnem znamenku, a vzorec a_x = g*sin(uhel) navic plati jen pri nulovem
            // naklonu do strany. Podil slozek obe vady odstrani.
            // ⚠️ A jeste VYHLADIT. Samotne "v < 0,03" klid nezarucuje: v depu s robotem hybe
            // obsluha a akcelerometr to vidi. Nad Kolo3a mel naklon z gravitace ve stani
            // sd 5,6 deg, kdezto atituda ze senzoru 1,3 deg - ten rozdil je otres, ne teren,
            // a srazil korelaci na 0,12. Sekundove okno ho odstrani.
            SmoothAcc(s, 1.0);
            var use = s.Where(x => !double.IsNaN(x.AccX) && x.V < 0.03).ToList();
            bool zeStani = use.Count > 100;
            if (!zeStani) use = s.Where(x => !double.IsNaN(x.AccX)).ToList();
            int sign = 1;
            bool rollJeKlopeni = false;
            if (use.Count > 100)
            {
                Console.WriteLine(F("  vzorku {0} ({1})", use.Count,
                                    zeStani ? "VE STANI" : "vsechny - ve stani jich je malo"));
                // Naklon dopredu a do strany, oba z gravitace.
                var vpred = use.Select(x => Math.Atan2(
                    x.SmX, Math.Sqrt(Math.Max(0, x.SmMag * x.SmMag - x.SmX * x.SmX)))).ToList();
                var bok = use.Select(x => Math.Atan2(
                    x.SmY, Math.Sqrt(Math.Max(0, x.SmMag * x.SmMag - x.SmY * x.SmY)))).ToList();
                var yp = use.Select(x => x.YprPitch).ToList();
                var yr = use.Select(x => x.YprRoll).ToList();
                double kPP = Slope(yp, vpred, out double rPP);
                double kRP = Slope(yr, vpred, out double rRP);
                Slope(yp, bok, out double rPB);
                Slope(yr, bok, out double rRB);
                var sm = new Stats("x");
                foreach (var x in use) sm.Add(x.SmMag);
                Console.WriteLine(F("  |a| ve vzorcich: p50 {0:F3} m/s2 (g = 9,807; pri odchylce od g"
                                    + " to NENI klidova gravitace)", sm.Median));
                Console.WriteLine(F("  rozptyl uhlu [deg]: naklon vpred z gravitace sd {0:F2},"
                                    + " do strany sd {1:F2}; ypr.Pitch sd {2:F2}, ypr.Roll sd {3:F2}",
                                    Sd(vpred) * 180 / Math.PI, Sd(bok) * 180 / Math.PI,
                                    Sd(yp) * 180 / Math.PI, Sd(yr) * 180 / Math.PI));
                Console.WriteLine("  korelace uhlu ze senzoru s naklonem z GRAVITACE:");
                Console.WriteLine(F("    ypr.Pitch vs naklon VPRED {0,7:F3} | do STRANY {1,7:F3}", rPP, rPB));
                Console.WriteLine(F("    ypr.Roll  vs naklon VPRED {0,7:F3} | do STRANY {1,7:F3}", rRP, rRB));
                rollJeKlopeni = Math.Abs(rRP) > Math.Abs(rPP);
                double k = rollJeKlopeni ? kRP : kPP;
                double r = rollJeKlopeni ? rRP : rPP;
                sign = k >= 0 ? 1 : -1;
                Console.WriteLine(F("  => KLOPENI je pole ypr.{0} (korelace {1:F3}, smernice {2:F3})",
                                    rollJeKlopeni ? "Roll" : "Pitch", r, k));
                Console.WriteLine("     a nos dolu je " + (sign > 0 ? "-" : "+") + " jeho zmena");
                // ⚠️ Rozhoduje ROZLISENI os, ne smernice. Smernici stahuje k nule sum v regresoru
                // (nad Kolo3a vysla 0,37 pri korelaci 0,61, a pomer smerodatnych odchylek 0,77/1,27
                // to presne vysvetluje), takze jako kriterium by lhala. Na otazku "ktera osa je
                // klopeni" odpovida to, ze kazde pole koreluje s JINYM naklonem.
                double kriz = rollJeKlopeni ? Math.Abs(rRB) : Math.Abs(rPB);
                if (double.IsNaN(r) || Math.Abs(r) < 0.4 || kriz > 0.7 * Math.Abs(r))
                    Console.WriteLine("  !! osy se NEROZLISILY (kazde pole koreluje s obema naklony)"
                                      + " - znamenko je nejiste, cti opatrne");
            }
            else Console.WriteLine("  zrychleni v zaznamu neni, predpoklada se ypr.Pitch a nos nahoru +");
            foreach (var x in s) x.Pitch = rollJeKlopeni ? x.YprRoll : x.YprPitch;

            // Ktera osa gyroskopu patri ke klopeni? Tataz otazka jako u uhlu, tyz rozhodci:
            // musi souhlasit s derivaci uhlu, ktery jsme prave urcili jako klopeni.
            var dp = new List<double>();
            var gx = new List<double>();
            var gy = new List<double>();
            for (int i = 1; i < s.Count - 1; i++)
            {
                double dt = s[i + 1].T - s[i - 1].T;
                if (dt <= 0 || dt > 0.2) continue;
                dp.Add(Unwrap(s[i + 1].Pitch - s[i - 1].Pitch) / dt);
                gx.Add(s[i].GyroXRaw);
                gy.Add(s[i].GyroYRaw);
            }
            bool gyroX = false;
            if (dp.Count > 100)
            {
                double kx = Slope(gx, dp, out double rx);
                double ky = Slope(gy, dp, out double ry);
                Console.WriteLine(F("  gyroskop proti derivaci klopeni: osa X smernice {0,6:F3} (r {1,6:F3}),"
                                    + " osa Y smernice {2,6:F3} (r {3,6:F3})", kx, rx, ky, ry));
                gyroX = Math.Abs(rx) > Math.Abs(ry);
                Console.WriteLine(F("  => rychlost klopeni je gyro {0}; |smernice| {1:F3} - uhel ze senzoru"
                                    + " je proti gyru {2}", gyroX ? "X" : "Y",
                                    Math.Abs(gyroX ? kx : ky),
                                    Math.Abs(gyroX ? kx : ky) < 0.85
                                        ? "UTLUMENY (VPE kratky raz nestiha - proto se klopeni meri i z gyra)"
                                        : "ve shode"));
            }
            foreach (var x in s)
            {
                x.GyroY = gyroX ? x.GyroXRaw : x.GyroYRaw;
                x.Roll = gyroX ? x.GyroYRaw : x.GyroXRaw;   // ta druha vodorovna osa
            }

            Console.WriteLine();
            return sign;
        }

        // ---------------------------------------------------------------- blok 2

        /// <summary>
        /// <b>Blok 2 — rozvor bez prahu.</b> Autokorelace energie klopení <b>v dráze</b>
        /// (ne v čase): když na tutéž nerovnost najede za přední nápravou o rozvor později
        /// i zadní kolo, musí být celý záznam sám sobě podobný právě při posunu o rozvor —
        /// a to bez ohledu na to, co považujeme za „hrbol".
        /// </summary>
        /// <returns>Poloha nejvyššího vrcholu v metrech (NaN, když se nenajde).</returns>
        private static double AutoCorrelation(List<Sample> s, double ds, double minSpeed, double maxLag)
        {
            Console.WriteLine("=== 2. ROZVOR Z AUTOKORELACE (bez prahu a bez detekce udalosti) ===");
            // Souvisle useky jizdy; kazdy se navzorkuje na rovnomernou mrizku v draze.
            var segs = new List<List<double>>();
            var cur = new List<Sample>();
            foreach (var x in s)
            {
                if (x.V >= minSpeed) cur.Add(x);
                else { if (cur.Count > 50) segs.Add(Resample(cur, ds)); cur = new List<Sample>(); }
            }
            if (cur.Count > 50) segs.Add(Resample(cur, ds));
            segs = segs.Where(g => g != null && g.Count * ds > 3 * maxLag).ToList();
            if (segs.Count == 0)
            {
                Console.WriteLine("  zadny dost dlouhy usek jizdy.");
                Console.WriteLine();
                return double.NaN;
            }
            Console.WriteLine(F("  useku jizdy: {0}, celkem {1:F1} m, krok mrizky {2:F3} m",
                                segs.Count, segs.Sum(g => g.Count) * ds, ds));

            int lags = (int)Math.Round(maxLag / ds);
            var num = new double[lags + 1];
            var cnt = new long[lags + 1];
            foreach (var g in segs)
            {
                // Normalizace po useku: kazdy usek ma jiny povrch i rychlost, takze by dlouhy
                // usek na hladkem asfaltu jinak prehlasil kratky usek v terenu.
                double mean = g.Average();
                double sd = Math.Sqrt(g.Sum(v => (v - mean) * (v - mean)) / g.Count);
                if (sd <= 0) continue;
                var z = g.Select(v => (v - mean) / sd).ToArray();
                for (int L = 0; L <= lags; L++)
                {
                    double acc = 0;
                    for (int i = 0; i + L < z.Length; i++) acc += z[i] * z[i + L];
                    num[L] += acc;
                    cnt[L] += z.Length - L;
                }
            }
            var r = new double[lags + 1];
            for (int L = 0; L <= lags; L++) r[L] = cnt[L] > 0 ? num[L] / cnt[L] : double.NaN;

            Console.WriteLine("  korelace energie klopeni pri posunu o vzdalenost:");
            var radky = new List<string>();
            for (int L = 0; L <= lags; L += Math.Max(1, (int)Math.Round(0.05 / ds)))
                radky.Add(F("{0,8:F2} m {1,7:F3}", L * ds, r[L]));
            for (int i = 0; i < radky.Count; i += 3)
                Console.WriteLine("  " + string.Join("  ", radky.Skip(i).Take(3)));

            // Vrchol se hleda az od 0,15 m: kolem nuly je autokorelace vzdy velka (sirka samotneho
            // razu), takze by vratila nulu bez ohledu na to, jestli nejaka ozvena existuje.
            int lo = (int)Math.Round(0.15 / ds);
            int best = -1;
            for (int L = lo + 1; L < lags; L++)
                if (r[L] > r[L - 1] && r[L] >= r[L + 1] && (best < 0 || r[L] > r[best])) best = L;
            double peak = best > 0 ? best * ds : double.NaN;
            if (best > 0)
            {
                // Sum krivky odhadnu z jeji druhe poloviny - tam uz zadna fyzika nesedi.
                var tail = r.Skip(lags / 2).Where(v => !double.IsNaN(v)).ToList();
                double tm = tail.Average();
                double tsd = Math.Sqrt(tail.Sum(v => (v - tm) * (v - tm)) / Math.Max(1, tail.Count));
                Console.WriteLine(F("  nejvyssi vrchol: {0:F2} m, r = {1:F4} ({2:F1} sigma nad sumem krivky)",
                                    peak, r[best], tsd > 0 ? (r[best] - tm) / tsd : double.NaN));
                // ⚠️ Zamena s otackou kola: obvod je 0,508 m, tedy tentyz rad.
                double obvod = 2 * Math.PI * ARBot.Common.Configuration.Profile.WheelRadius;
                Console.WriteLine(F("  POZOR na zamenu: obvod kola je {0:F3} m. Periodicky jev od kola ma", obvod));
                Console.WriteLine("  vrcholy i na nasobcich, jednorazova ozvena hrbolu ne:");
                for (int k = 1; k <= 3; k++)
                {
                    int i1 = (int)Math.Round(k * peak / ds), i2 = (int)Math.Round(k * obvod / ds);
                    Console.WriteLine(F("    {0}x vrchol = {1:F2} m -> r {2}    {0}x obvod = {3:F3} m -> r {4}",
                        k, k * peak, i1 <= lags ? F("{0:F4}", r[i1]) : "(mimo)",
                        k * obvod, i2 <= lags ? F("{0:F4}", r[i2]) : "(mimo)"));
                }
            }
            else Console.WriteLine("  ZADNY vrchol nad 0,15 m - ozvena zadniho kola v datech NENI");
            Console.WriteLine();
            return peak;
        }

        /// <summary>
        /// Navzorkuje energii klopeni useku na rovnomernou mrizku v draze — <b>prumerem pres
        /// bunku</b>, ne nejblizsim vzorkem.
        ///
        /// <para>⚠️ <b>Proc prumerem.</b> Pri 0,9 m/s je 100Hz vzorek 9 mm, tedy zhruba krok
        /// mrizky — ale pri 0,2 m/s jsou 2 mm a vyber nejblizsiho vzorku by ctyri patiny dat
        /// ZAHODIL. To neni jen ztrata presnosti: podvzorkovani sirokopasmoveho signalu ho
        /// <b>zbeli</b>, tedy smaze prave tu ozvenu, kvuli ktere se to pocita — a pomale useky
        /// jsou zrovna ty, kde je hrbolu nejvic.</para>
        /// </summary>
        private static List<double> Resample(List<Sample> seg, double ds)
        {
            double s0 = seg[0].S, s1 = seg[seg.Count - 1].S;
            int n = (int)((s1 - s0) / ds);
            if (n < 10) return null;
            var sum = new double[n];
            var cnt = new int[n];
            foreach (var x in seg)
            {
                int b = (int)((x.S - s0) / ds);
                if (b < 0 || b >= n) continue;
                // Energie, ne amplituda: hleda se OZVENA razu, tedy shoda v tom, kde bylo
                // hrbolato - ne v tom, kterym smerem se robot naklonil.
                sum[b] += x.Hp * x.Hp;
                cnt[b]++;
            }
            var outp = new List<double>(n);
            double last = 0;
            for (int i = 0; i < n; i++)
            {
                if (cnt[i] > 0) last = sum[i] / cnt[i];
                outp.Add(last);   // prazdna bunka (rychla jizda) prebira predchozi
            }
            return outp;
        }

        // ---------------------------------------------------------------- blok 2c

        /// <summary>
        /// <b>Blok 2c — jak daleko dopředu drsnost platí?</b> Rozdělí jízdu na okna po 5 m,
        /// spočítá v každém RMS klopení a podívá se, jak rychle si jsou sousední okna podobná.
        ///
        /// <para><b>Nač to je.</b> Záměr <c>lp-drsnost-povrchu-rychlostni-strop</c> má
        /// naformulované go/no-go takto: „korelace drsnost → náklon jen do ~1 m znamená, že robot
        /// nestihne brzdit a kanál nemá smysl". To je ale postavené na <b>jednotlivém hrbolu</b>,
        /// který musí kamera vidět dřív, než na něj robot najede — a šum hloubky roste s r², takže
        /// je čitelný sotva do 1,5 m. Když ale drsnost <b>drží přes desítky metrů</b> (úsek dlažby,
        /// lesní pěšina), nemusí se předpovídat hrbol: stačí poznat, že <i>tenhle úsek je
        /// hrbolatý</i>, a strop se nastaví z toho, po čem robot už <b>projel</b>. To je úplně
        /// jiná léčba se stejným účinkem a mnohem menšími nároky na senzor.</para>
        ///
        /// <para>⚠️ Měří se <b>v dráze</b>, ne v čase — jinak by číslo mluvilo o tom, jak dlouho
        /// robot jel, ne o tom, jak dlouhý je hrbolatý úsek.</para>
        /// </summary>
        private static void Roughness(List<Sample> s, double minSpeed)
        {
            Console.WriteLine("=== 2c. DRZI DRSNOST PRES DELSI USEK? (jak daleko dopredu plati) ===");
            const double win = 5.0;
            var okna = new List<(double S, double Rms, double V)>();
            double sum = 0, sumV = 0;
            int n = 0;
            double baseS = double.NaN;
            foreach (var x in s)
            {
                if (x.V < minSpeed) continue;
                if (double.IsNaN(baseS)) baseS = x.S;
                if (x.S - baseS >= win)
                {
                    if (n > 20) okna.Add((baseS, Math.Sqrt(sum / n), sumV / n));
                    baseS = x.S; sum = 0; sumV = 0; n = 0;
                }
                sum += x.Hp * x.Hp; sumV += x.V; n++;
            }
            if (okna.Count < 10) { Console.WriteLine("  malo oken."); Console.WriteLine(); return; }
            var st = new Stats("  RMS klopeni v okne 5 m [deg/s]");
            foreach (var o in okna) st.Add(o.Rms * 180 / Math.PI);
            Console.WriteLine(F("  oken po {0:F0} m: {1}", win, okna.Count));
            Console.WriteLine(st.Line());
            Console.WriteLine(F("  nejhorsi okno je {0:F1}x drsnejsi nez median - drsnost tedy {1}",
                                st.Max / st.Median,
                                st.Max / st.Median > 2 ? "NENI rovnomerna (je co mapovat)"
                                                       : "je skoro rovnomerna (mapovat neni co)"));

            // ⚠️ NENI "drsny usek" jen "rychly usek"? Vibrace roste s rychlosti, takze kdyby RMS
            // silne koreloval s rychlosti okna, byla by mapa drsnosti z velke casti mapou rychlosti
            // - a strop postaveny na ni by se honil za vlastnim ocasem (zpomal -> vypada hladce ->
            // zrychli). Tatáž kruhovost jako u camerapose=fusion.
            {
                var rms = okna.Select(o => o.Rms).ToList();
                var vs = okna.Select(o => o.V).ToList();
                Slope(vs, rms, out double rv);
                var sv2 = new Stats("x");
                foreach (var o in okna) sv2.Add(o.V);
                Console.WriteLine(F("  rychlost v oknech: p50 {0:F2} m/s, rozsah {1:F2}-{2:F2};"
                                    + " korelace RMS s rychlosti r = {3:F3}",
                                    sv2.Median, sv2.Min, sv2.Max, rv));
                Console.WriteLine(Math.Abs(rv) > 0.5
                    ? "  !! RMS silne zavisi na rychlosti - drsnost se musi merit na jednotku DRAHY,"
                      + Environment.NewLine
                      + "     jinak by mapa drsnosti byla z velke casti mapou rychlosti (kruhovost)"
                    : "  => zavislost na rychlosti je slaba, RMS mluvi o povrchu, ne o rychlosti");
            }

            // Korelace RMS mezi okny vzdalenymi o k*win metru - jen pres SOUVISLE useky,
            // aby se do toho nepocital skok pres stani.
            Console.WriteLine("  podobnost drsnosti dvou mist vzdalenych o:");
            for (int k = 1; k <= 6; k++)
            {
                double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
                int m = 0;
                for (int i = 0; i + k < okna.Count; i++)
                {
                    // Okna musi byt skutecne sousedni v draze (jinak je mezi nimi stani).
                    if (Math.Abs(okna[i + k].S - okna[i].S - k * win) > 0.5 * win) continue;
                    double a = okna[i].Rms, b = okna[i + k].Rms;
                    sa += a; sb += b; saa += a * a; sbb += b * b; sab += a * b; m++;
                }
                if (m < 5) continue;
                double cov = sab / m - (sa / m) * (sb / m);
                double va = saa / m - (sa / m) * (sa / m), vb = sbb / m - (sb / m) * (sb / m);
                double r = (va > 0 && vb > 0) ? cov / Math.Sqrt(va * vb) : double.NaN;
                Console.WriteLine(F("    {0,5:F0} m  r = {1,6:F3}  (n={2})", k * win, r, m));
            }
            Console.WriteLine("  (vysoke r i na desitkach metru = staci poznat HRBOLATY USEK, ne hrbol;");
            Console.WriteLine("   pak se strop da postavit z toho, po cem robot UZ projel, a kamera");
            Console.WriteLine("   se svym dosahem 1,5 m k tomu neni potreba)");
            Console.WriteLine();
        }

        // ---------------------------------------------------------------- blok 3

        /// <summary>Blok 3a — detekce spicek (lokalni maxima |Hp| nad robustnim prahem).</summary>
        private static List<Spike> Detect(List<Sample> s, double minSpeed, double madK,
                                          double refractoryM, int noseUpSign,
                                          List<(double T, double V, bool Held, bool EStop)> cmd)
        {
            Console.WriteLine("=== 3. SPICKY KLOPENI A JEJICH PAROVANI ===");
            var amp = s.Where(x => x.V >= minSpeed).Select(x => Math.Abs(x.Hp)).OrderBy(x => x).ToList();
            if (amp.Count < 100) return new List<Spike>();
            double med = amp[amp.Count / 2];
            var dev = amp.Select(x => Math.Abs(x - med)).OrderBy(x => x).ToList();
            double mad = 1.4826 * dev[dev.Count / 2];
            double thr = med + madK * mad;

            // ⚠️ Nejdriv KLIDOVE srovnani. Kdyz je |Hp| pri stani skoro stejne jako za jizdy,
            // neni to otres z terenu, ale sum senzoru (nebo vibrace od motoru) - a pak nemá smysl
            // hledat v tom hrboly. Bez tehle kontroly by cely zbytek reportu mohl merit nic.
            var klid = s.Where(x => x.V < 0.03).Select(x => Math.Abs(x.Hp)).OrderBy(x => x).ToList();
            Console.WriteLine(F("  |gyroY| po horni propusti: v JIZDE p50 {0:F4}, p90 {1:F4}, p99 {2:F4},"
                                + " max {3:F4} rad/s",
                                med, amp[(int)(0.90 * (amp.Count - 1))], amp[(int)(0.99 * (amp.Count - 1))],
                                amp[amp.Count - 1]));
            if (klid.Count > 100)
                Console.WriteLine(F("  {0,-24} ve STANI  p50 {1:F4}, p90 {2:F4}, p99 {3:F4},"
                                    + " max {4:F4} rad/s (n={5})", "",
                                    klid[klid.Count / 2], klid[(int)(0.90 * (klid.Count - 1))],
                                    klid[(int)(0.99 * (klid.Count - 1))], klid[klid.Count - 1], klid.Count));
            else Console.WriteLine("  (ve stani je malo vzorku, klidove srovnani chybi)");
            Console.WriteLine(F("  p50 v jizde ku p50 ve stani: {0}",
                                klid.Count > 100 && klid[klid.Count / 2] > 0
                                    ? F("{0:F1}x", med / klid[klid.Count / 2]) : "n/a"));
            Console.WriteLine(F("  {0,-24} MAD {1:F4} rad/s", "", mad));
            Console.WriteLine(F("  prah spicky = p50 + {0:F1}*MAD = {1:F4} rad/s ({2:F2} deg/s)",
                                madK, thr, thr * 180 / Math.PI));

            var sp = new List<Spike>();
            for (int i = 1; i < s.Count - 1; i++)
            {
                if (s[i].V < minSpeed) continue;
                double a = Math.Abs(s[i].Hp);
                if (a < thr || a < Math.Abs(s[i - 1].Hp) || a < Math.Abs(s[i + 1].Hp)) continue;
                if (sp.Count > 0 && s[i].S - sp[sp.Count - 1].S < refractoryM)
                {
                    // V refrakterni vzdalenosti si nechame tu silnejsi z obou.
                    if (a > sp[sp.Count - 1].Amp) sp.RemoveAt(sp.Count - 1);
                    else continue;
                }
                sp.Add(new Spike { Idx = i, T = s[i].T, S = s[i].S, V = s[i].V, Amp = a });
            }
            foreach (var x in sp)
            {
                x.NoseDownDeg = NoseDown(s, x.Idx, noseUpSign);
                x.GyroNoseDeg = GyroNoseDown(s, x.Idx, noseUpSign);
                x.SpanDeg = Span(s, x.Idx);
                Okoli(s, x);
                x.DvCmd = CommandDerivative(cmd, x.T);
            }
            var vj = s.Where(x => x.V >= minSpeed).ToList();
            double drahaJizdy = vj.Count > 0 ? vj.Max(x => x.S) - vj.Min(x => x.S) : 0;
            Console.WriteLine(F("  spicek: {0} na {1:F0} m jizdy = jedna na {2:F2} m",
                                sp.Count, drahaJizdy, sp.Count > 0 ? drahaJizdy / sp.Count : double.NaN));
            return sp;
        }

        /// <summary>
        /// <b>Blok 3x — ozvěna po SILNÉM rázu</b> (spike-triggered average). Poslední a nejcitlivější
        /// pokus, jak hypotézu obhájit.
        ///
        /// <para><b>Proč ještě tohle, když blok 2 nic nenašel.</b> Globální autokorelace počítá
        /// přes celý záznam, tedy i přes stovky metrů hladkého asfaltu, kde žádný ráz není —
        /// a případná ozvěna se v tom rozředí. Tenhle blok se ptá úžeji a poctivěji: <b>vezmi jen
        /// nejsilnější rázy a zprůměruj, co se dělo těsně ZA nimi.</b> Když zadní kolo najíždí na
        /// tentýž hrbol, musí v průměru vystoupit hrbolek právě na rozvoru — a to i tehdy, když je
        /// slabý, protože průměrování přes stovky událostí potlačí všechno ostatní.</para>
        ///
        /// <para>Výsledek je v <b>násobcích běžné energie</b> (1,0 = tady se neděje nic
        /// zvláštního), takže se čte bez dalšího přepočtu.</para>
        /// </summary>
        private static void TriggeredAverage(List<Sample> s, List<Spike> sp, double ds, double maxLag)
        {
            Console.WriteLine();
            Console.WriteLine("  OZVENA PO SILNEM RAZU (prumer energie za spickou, 1,0 = bezny stav):");
            if (sp.Count < 20) { Console.WriteLine("    malo spicek."); return; }
            // Jen nejsilnejsi tretina - slabe spicky jsou z velke casti sum a rozmazaly by prumer.
            double prah = sp.Select(x => x.Amp).OrderByDescending(x => x).ElementAt(sp.Count / 3);
            var silne = sp.Where(x => x.Amp >= prah).ToList();
            Console.WriteLine(F("    (z {0} nejsilnejsich spicek, prah {1:F2} deg/s)",
                                silne.Count, prah * 180 / Math.PI));

            var vDraze = Profil(s, silne, ds, maxLag, vDraze: true, out double peakD, out double maxD);
            Tisk(vDraze, ds, "m");
            Console.WriteLine(F("    nejvyssi hodnota za 0,15 m: {0:F2}x na {1:F2} m", maxD, peakD));

            // ⚠️ ROZHODCI MEZI DVEMA VYSVETLENIMI. Druhy vrchol muze byt ozvena zadniho kola
            // (pevna VZDALENOST, at robot jede jakkoli rychle), ale taky vlastni kmit karoserie
            // na pneumatikach (pevny CAS, tedy vzdalenost rostouci s rychlosti). Na syrovem
            // prubehu jsou k nerozeznani - rozlisi je jedine to, co se s rychlosti NEMENI.
            double dt = ds / 1.0;   // krok v case volim tak, aby pri ~1 m/s odpovidal kroku v draze
            var vCase = Profil(s, silne, dt, maxLag / 1.0, vDraze: false, out double peakT, out double maxT);
            Console.WriteLine();
            Console.WriteLine("  TOTEZ V CASE (rozhodci: ozvena kola je na pevne VZDALENOSTI,");
            Console.WriteLine("  vlastni kmit karoserie na pevnem CASE):");
            Tisk(vCase, dt, "s");
            Console.WriteLine(F("    nejvyssi hodnota za 0,15 s: {0:F2}x na {1:F2} s", maxT, peakT));

            // A hlavne: rozpad podle rychlosti. Kdyz se vrchol v DRAZE s rychlosti posouva
            // a v CASE stoji, je to kmit; kdyz naopak, je to geometrie.
            double vMed = silne.Select(x => x.V).OrderBy(x => x).ElementAt(silne.Count / 2);
            var pomalu = silne.Where(x => x.V < vMed).ToList();
            var rychle = silne.Where(x => x.V >= vMed).ToList();
            Console.WriteLine();
            if (pomalu.Count >= 8 && rychle.Count >= 8)
            {
                Profil(s, pomalu, ds, maxLag, true, out double pD, out _);
                Profil(s, rychle, ds, maxLag, true, out double rD, out _);
                Profil(s, pomalu, dt, maxLag, false, out double pT, out _);
                Profil(s, rychle, dt, maxLag, false, out double rT, out _);
                double vp = pomalu.Average(x => x.V), vr = rychle.Average(x => x.V);
                Console.WriteLine("  ROZPAD PODLE RYCHLOSTI (kde lezi druhy vrchol):");
                Console.WriteLine(F("    pomalejsi pulka (v {0:F2} m/s, n={1}): {2:F2} m  /  {3:F2} s",
                                    vp, pomalu.Count, pD, pT));
                Console.WriteLine(F("    rychlejsi pulka (v {0:F2} m/s, n={1}): {2:F2} m  /  {3:F2} s",
                                    vr, rychle.Count, rD, rT));
                // Predpoved obou hypotez: kmit drzi CAS, takze draha roste pomerem rychlosti.
                Console.WriteLine(F("    kdyby to byl KMIT (pevny cas), byla by draha rychlejsi pulky"
                                    + " {0:F2} m; kdyby OZVENA kola (pevna draha), pak {1:F2} m",
                                    pD * vr / Math.Max(1e-9, vp), pD));
                double kKmit = Math.Abs(rD - pD * vr / Math.Max(1e-9, vp));
                double kOzvena = Math.Abs(rD - pD);
                Console.WriteLine("    namereno " + F("{0:F2} m", rD) + " => blize je "
                                  + (kKmit < kOzvena ? "KMIT KAROSERIE (pevny cas)"
                                                     : "OZVENA KOLA (pevna vzdalenost)"));
                Console.WriteLine("    ⚠️ pri malem rozdilu rychlosti obou pulek to nerozhodne nic -");
                Console.WriteLine(F("       tady je pomer rychlosti {0:F2}, tedy predpovedi se lisi o {1:F2} m",
                                    vr / Math.Max(1e-9, vp), Math.Abs(pD * vr / Math.Max(1e-9, vp) - pD)));
            }
            else Console.WriteLine("  (na rozpad podle rychlosti je malo silnych spicek)");
        }

        /// <summary>
        /// Prumerny prubeh energie za spickou — bud v draze (<paramref name="vDraze"/>), nebo
        /// v case. Vraci profil v nasobcich bezne energie a polohu jeho maxima za 0,15.
        /// </summary>
        private static double[] Profil(List<Sample> s, List<Spike> silne, double krok, double maxLag,
                                       bool vDraze, out double peak, out double maxv)
            => Profil(s, silne, krok, maxLag, vDraze ? 0 : 1, out peak, out maxv);

        /// <param name="rezim">0 = draha z odometrie, 1 = cas, 2 = draha z rychlosti PRED udalosti.</param>
        private static double[] Profil(List<Sample> s, List<Spike> silne, double krok, double maxLag,
                                       int rezim, out double peak, out double maxv)
        {
            int lags = (int)Math.Round(maxLag / krok);
            var sum = new double[lags + 1];
            var cnt = new int[lags + 1];
            double bezna = s.Where(x => x.V > 0).Select(x => x.Hp * x.Hp).DefaultIfEmpty(0).Average();
            foreach (var x in silne)
                for (int i = x.Idx; i < s.Count; i++)
                {
                    double d = rezim == 0 ? s[i].S - x.S
                             : rezim == 1 ? s[i].T - x.T
                                          : x.VPre * (s[i].T - x.T);
                    if (d < 0) continue;
                    if (d > maxLag) break;
                    int b = (int)(d / krok);
                    if (b > lags) break;
                    sum[b] += s[i].Hp * s[i].Hp;
                    cnt[b]++;
                }
            var prof = new double[lags + 1];
            for (int b = 0; b <= lags; b++)
                prof[b] = (cnt[b] > 0 && bezna > 0) ? sum[b] / cnt[b] / bezna : double.NaN;
            peak = double.NaN; maxv = 0;
            int skip = (int)Math.Round(0.15 / krok);
            for (int b = skip; b <= lags; b++)
                if (!double.IsNaN(prof[b]) && prof[b] > maxv) { maxv = prof[b]; peak = b * krok; }
            return prof;
        }

        private static void Tisk(double[] prof, double krok, string jednotka)
        {
            var radky = new List<string>();
            for (int b = 0; b < prof.Length; b += Math.Max(1, (int)Math.Round(0.05 / krok)))
                radky.Add(F("{0,6:F2} {1} {2,6:F2}x", b * krok, jednotka, prof[b]));
            for (int i = 0; i < radky.Count; i += 4)
                Console.WriteLine("    " + string.Join("  ", radky.Skip(i).Take(4)));
        }

        /// <summary>
        /// <b>Blok 3z — ozvěna tam, kde se vůbec MÁ čekat.</b> Zadní kolo je volně otočná ostruha
        /// <b>mezi</b> hnanými koly, takže ve stopě předních kol nejede; ozvěnu tedy můžou dát
        /// jen <b>příčné</b> (nebo velké) defekty, které zasáhnou všechna kola — a jen při
        /// <b>přímé</b> jízdě, protože při manévrování se ostruha vytočí a podélný odstup se mění.
        /// (Rozměry a chování podvozku sdělil autor 22. 9. 2026.)
        ///
        /// <para>Měří se na <b>dvou osách dráhy</b>. ⚠️ Ta obvyklá, z odometrie, je u zakopnutí
        /// <b>vadná</b>: dráha se integruje z týchž kol, která přes hrbol šplhají a prokluzují
        /// (u události 14:12:52 kolísá rychlost 0,30–0,86 m/s), takže skutečnou dráhu
        /// <b>podhodnocuje</b> a odstup ozvěny vyjde kratší než rozvor. Druhá osa používá rychlost
        /// <b>před</b> událostí, takže na prokluzu během ní nezávisí.</para>
        /// </summary>
        private static void EchoByDefect(List<Sample> s, List<Spike> sp, double ds, double maxLag,
                                         double straightDegS, double rozvor)
        {
            Console.WriteLine();
            Console.WriteLine("  OZVENA TAM, KDE SE MA CEKAT (pricny defekt + prima jizda):");
            if (sp.Count < 20) { Console.WriteLine("    malo spicek."); return; }
            double prah = sp.Select(x => x.Amp).OrderByDescending(x => x).ElementAt(sp.Count / 3);
            var silne = sp.Where(x => x.Amp >= prah).ToList();
            double yawMax = straightDegS * Math.PI / 180;
            // Delici prah odezvy do strany = median pres silne udalosti. Absolutni cislo by bylo
            // vycucane z prstu; median rozdeli udalosti na "spis pricne" a "spis jednostranne".
            double rollMed = silne.Select(x => x.RollRate).OrderBy(x => x)
                                  .ElementAt(Math.Max(0, silne.Count / 2));
            Console.WriteLine(F("    prima jizda: |staceni| < {0:F0} deg/s; pricny defekt: odezva do strany"
                                + " pod medianem {1:F0} deg/s", straightDegS, rollMed * 180 / Math.PI));

            var skupiny = new (string Nazev, List<Spike> L)[]
            {
                ("vse (kontrola)          ", silne),
                ("prima jizda             ", silne.Where(x => x.YawRate < yawMax).ToList()),
                ("prima + PRICNY defekt   ", silne.Where(x => x.YawRate < yawMax && x.RollRate <= rollMed).ToList()),
                ("prima + JEDNOSTRANNY    ", silne.Where(x => x.YawRate < yawMax && x.RollRate > rollMed).ToList()),
            };
            Console.WriteLine(F("    predpoved: u PRICNEHO defektu ma byt vrchol na rozvoru {0:F2} m,"
                                + " u jednostranneho", rozvor));
            Console.WriteLine("    NE (ostruha je mezi hnanymi koly, takze jednostranny defekt mine)");
            Console.WriteLine();
            Console.WriteLine("      skupina                    n   osa drahy    vrchol   hodnota   na rozvoru");
            foreach (var g in skupiny)
            {
                if (g.L.Count < 6) { Console.WriteLine(F("      {0} {1,4}   (malo udalosti)", g.Nazev, g.L.Count)); continue; }
                foreach (var (rezim, jmeno) in new[] { (0, "odometrie  "), (2, "bez prokluzu") })
                {
                    var prof = Profil(s, g.L, ds, maxLag, rezim, out double peak, out double maxv);
                    int ir = (int)Math.Round(rozvor / ds);
                    double naRozvoru = ir < prof.Length ? prof[ir] : double.NaN;
                    // Pozadi: prumer profilu daleko za udalosti, kde uz zadna ozvena byt nema.
                    var pozadi = prof.Skip((int)(1.0 / ds)).Where(v => !double.IsNaN(v)).DefaultIfEmpty(1).Average();
                    Console.WriteLine(F("      {0} {1,4}   {2}  {3,6:F2} m  {4,6:F2}x   {5,6:F2}x  (pozadi {6:F2}x)",
                                        rezim == 0 ? g.Nazev : "                         ",
                                        rezim == 0 ? g.L.Count.ToString() : "    ",
                                        jmeno, peak, maxv, naRozvoru, pozadi));
                }
            }
            Console.WriteLine("    ⚠️ 'na rozvoru' proti 'pozadi' je to cislo, o ktere jde: vrchol muze");
            Console.WriteLine("       padnout kamkoli, ale predpoved mluvi o JEDNOM konkretnim miste");
        }

        /// <summary>
        /// Blok 3b — má každá špička partnera v okně rozvoru? Histogram vzdáleností k NÁSLEDUJÍCÍ
        /// špičce: když je párování skutečné, musí z hladkého pozadí vystoupit vrchol.
        /// </summary>
        private static void PairSpikes(List<Spike> sp, double lo, double hi, double acLag)
        {
            if (sp.Count < 10)
            {
                Console.WriteLine("  prilis malo spicek.");
                Console.WriteLine();
                return;
            }
            Console.WriteLine();
            Console.WriteLine("  vzdalenost k NASLEDUJICI spicce (hledany vrchol = rozvor):");
            const double bin = 0.05;
            int bins = (int)(3.0 / bin);
            var h = new int[bins + 1];
            var dist = new List<double>();
            for (int i = 0; i + 1 < sp.Count; i++)
            {
                double d = sp[i + 1].S - sp[i].S;
                if (d <= 0 || d > 3.0) continue;
                dist.Add(d);
                h[(int)(d / bin)]++;
            }
            int hmax = h.Max();
            for (int b = 0; b < bins; b++)
            {
                if (b * bin > 2.0 && h[b] == 0) continue;
                int stars = hmax > 0 ? (int)Math.Round(40.0 * h[b] / hmax) : 0;
                Console.WriteLine(F("    {0,4:F2}-{1,4:F2} m  {2,5}  {3}", b * bin, (b + 1) * bin,
                                    h[b], new string('#', stars)));
            }
            var ds = new Stats("  vzdalenost k nasledujici spicce [m]");
            foreach (var d in dist) ds.Add(d);
            Console.WriteLine(ds.Line());

            foreach (var x in sp) { x.Partner = null; x.PairDist = double.NaN; }
            int paired = 0;
            for (int i = 0; i < sp.Count; i++)
                for (int j = i + 1; j < sp.Count; j++)
                {
                    double d = sp[j].S - sp[i].S;
                    if (d < lo) continue;
                    if (d > hi) break;
                    sp[i].Partner = sp[j];
                    sp[i].PairDist = d;
                    paired++;
                    break;
                }
            Console.WriteLine(F("  spicek s partnerem v okne {0:F2}-{1:F2} m: {2} z {3} ({4:F1} %)",
                                lo, hi, paired, sp.Count, 100.0 * paired / sp.Count));

            // ⚠️ NULOVA HYPOTEZA. Samotne procento sparovanych nic neznamena: kdyz je spicka
            // kazdych 0,5 m a okno je siroke 1,0 m, najde se partner skoro vzdy i pri naprosto
            // NAHODNEM rozmisteni spicek. Poissonovsky odhad rika, kolik by jich vyslo bez
            // jakekoli fyziky - a teprve rozdil proti nemu je dukaz.
            double mezera = dist.Count > 0 ? dist.Average() : double.NaN;
            double nahodne = 100.0 * (1 - Math.Exp(-(hi - lo) / Math.Max(1e-9, mezera)));
            Console.WriteLine(F("  NAHODNE by jich vyslo {0:F1} % (Poisson pri prumerne mezere {1:F2} m)",
                                nahodne, mezera));
            Console.WriteLine(100.0 * paired / sp.Count > nahodne + 5
                ? "  => parovani je nad nahodou - stoji za to se na tvar histogramu divat"
                : "  => parovani je NA UROVNI NAHODY: procento sparovanych nic nedokazuje,"
                  + "\n     rozhoduje jen to, jestli ma histogram vyse VRCHOL, nebo jen klesa");
            if (!double.IsNaN(acLag))
                Console.WriteLine(F("  (autokorelace z bloku 2 rikala {0:F2} m - sedi to s vrcholem histogramu?)",
                                    acLag));
            // Kontrola z devlogu: odstup x rychlost ma dat rozvor, tedy odstup v CASE se musi
            // s rychlosti menit. Kdyz je konstantni v case a ne v draze, neni to geometrie robotu.
            var dt = new Stats("  odstup paru v CASE [s]");
            var dd = new Stats("  odstup paru v DRAZE [m]");
            foreach (var x in sp)
                if (x.Partner != null) { dt.Add(x.Partner.T - x.T); dd.Add(x.PairDist); }
            Console.WriteLine(dt.Line());
            Console.WriteLine(dd.Line());
            Console.WriteLine("  (rozptyl musi byt mensi v DRAZE nez v case - jinak to neni rozvor)");
            Console.WriteLine();
        }

        // ---------------------------------------------------------------- blok 4

        /// <summary>
        /// <b>Blok 4 — přirozený experiment.</b> Je klopení při druhé špičce větší, když smyčka
        /// v tu chvíli brzdila? To je jediný údaj, který rozhodne, jestli má reflex „nebrzdit"
        /// (<c>k = 0</c>) vůbec smysl — a za čtyři kola Robotouru se ta situace odehrála
        /// mnohokrát sama, bez zásahu do kódu.
        /// </summary>
        private static void TiltByCommand(List<Spike> sp)
        {
            Console.WriteLine("=== 4. KLOPENI PODLE TOHO, CO SMYCKA PRAVE DELALA ===");
            var druhe = sp.Where(x => x.Partner != null).Select(x => x.Partner).Distinct().ToList();
            if (druhe.Count < 10)
            {
                Console.WriteLine("  prilis malo sparovanych udalosti - bez zaveru.");
                Console.WriteLine();
                return;
            }
            Console.WriteLine(F("  parovanych udalosti: {0} (radek = DRUHA spicka, tedy zadni kolo)",
                                druhe.Count));
            // Rampa je 0,50 m/s2, takt 100 ms, tedy jeden plny krok rampy je 0,05 m/s.
            // Delici prah 0,1 m/s2 = petina rampy: pod tim uz je to drzeni, ne zasah.
            Group("brzdil   (dv < -0,1 m/s2)", druhe.Where(x => x.DvCmd < -0.1));
            Group("drzel    (|dv| <= 0,1)   ", druhe.Where(x => Math.Abs(x.DvCmd) <= 0.1));
            Group("zrychlov (dv > +0,1 m/s2)", druhe.Where(x => x.DvCmd > 0.1));
            Console.WriteLine();
            Console.WriteLine("  totez pro PRVNI spicky (predni naprava) - kontrola, ze rozdil neni");
            Console.WriteLine("  jen tim, ze se na hrbolatem useku obecne brzdi:");
            var prvni = sp.Where(x => x.Partner != null).ToList();
            Group("brzdil   (dv < -0,1 m/s2)", prvni.Where(x => x.DvCmd < -0.1));
            Group("drzel    (|dv| <= 0,1)   ", prvni.Where(x => Math.Abs(x.DvCmd) <= 0.1));
            Group("zrychlov (dv > +0,1 m/s2)", prvni.Where(x => x.DvCmd > 0.1));
            Console.WriteLine();
        }

        private static void Group(string nazev, IEnumerable<Spike> g)
        {
            var l = g.ToList();
            if (l.Count == 0) { Console.WriteLine($"    {nazev}  n=0"); return; }
            var nd = new Stats("x");
            foreach (var x in l) nd.Add(x.NoseDownDeg);
            var gd = new Stats("x");
            foreach (var x in l) gd.Add(x.GyroNoseDeg);
            var v = new Stats("x");
            foreach (var x in l) v.Add(x.V);
            var sp2 = new Stats("x");
            foreach (var x in l) sp2.Add(x.SpanDeg);
            Console.WriteLine(F("    {0}  n={1,4}  klopeni dopredu [deg] p50 {2,6:F2} p90 {3,6:F2} max {4,6:F2}"
                                + " | z gyra p50 {5,5:F2} max {6,6:F2} | rozkmit p90 {7,6:F2} | v p50 {8:F2}",
                                nazev, l.Count, nd.Median, nd.Percentile(90), nd.Max,
                                gd.Median, gd.Max, sp2.Percentile(90), v.Median));
        }

        /// <summary>Blok 5 — roste klopeni s rychlosti? Pomaha tedy vubec zpomalit?</summary>
        private static void TiltBySpeed(List<Spike> sp)
        {
            Console.WriteLine("=== 5. ZAVISLOST KLOPENI NA RYCHLOSTI (pomaha zpomalit?) ===");
            var all = sp.Where(x => x.Partner != null).Select(x => x.Partner).Distinct().ToList();
            if (all.Count < 10) { Console.WriteLine("  malo dat."); Console.WriteLine(); return; }
            for (double v = 0; v < 1.2; v += 0.2)
            {
                double lo = v, hi = v + 0.2;
                var g = all.Where(x => x.V >= lo && x.V < hi).ToList();
                if (g.Count == 0) continue;
                var nd = new Stats("x");
                foreach (var x in g) nd.Add(x.NoseDownDeg);
                var am = new Stats("x");
                foreach (var x in g) am.Add(x.Amp * 180 / Math.PI);
                Console.WriteLine(F("    v {0:F1}-{1:F1} m/s  n={2,4}  klopeni p50 {3,6:F2} p90 {4,6:F2} max {5,6:F2} deg"
                                    + " | rychlost klopeni p50 {6,6:F1} deg/s",
                                    lo, hi, g.Count, nd.Median, nd.Percentile(90), nd.Max, am.Median));
            }
            Console.WriteLine();
        }

        /// <summary>
        /// <b>Blok 6 — měl by reflex co dělat?</b> Zákaz brzdění pomůže jen tehdy, když robot
        /// v okamžiku druhé špičky skutečně brzdí. Když to je jednou za sto událostí, je
        /// <c>k = 0</c> mrtvé opatření a má smysl jen <c>k = 1</c> (přidat).
        /// </summary>
        private static void Braking(List<Spike> sp)
        {
            Console.WriteLine("=== 6. MEL BY REFLEX CO DELAT? ===");
            var druhe = sp.Where(x => x.Partner != null).Select(x => x.Partner).Distinct().ToList();
            if (druhe.Count == 0) { Console.WriteLine("  malo dat."); Console.WriteLine(); return; }
            int brzdil = druhe.Count(x => x.DvCmd < -0.1);
            Console.WriteLine(F("  v okamziku druhe spicky smycka BRZDILA v {0} z {1} udalosti ({2:F1} %)",
                                brzdil, druhe.Count, 100.0 * brzdil / druhe.Count));
            Console.WriteLine("  => tolikrat by zakaz brzdeni (k = 0) mel co zakazat; pridani (k = 1)");
            Console.WriteLine("     zabere vzdy, ale je to aktivni zasah proti planovaci");

            // Kolik casu ma smycka od prvni spicky k druhe? Takt je 100 ms, takze pod 200 ms
            // nestihne okno naplanovat a reflex nema smysl (viz lp-reflex-klopeni-zadni-kolo).
            var lagy = new Stats("  cas od prvni spicky k druhe [s]");
            foreach (var x in sp) if (x.Partner != null) lagy.Add(x.Partner.T - x.T);
            Console.WriteLine(lagy.Line());
            int pod2 = sp.Count(x => x.Partner != null && x.Partner.T - x.T < 0.2);
            int pod1 = sp.Count(x => x.Partner != null && x.Partner.T - x.T < 0.1);
            Console.WriteLine(F("  pod 100 ms (jeden takt): {0}; pod 200 ms (dva takty): {1} z {2}",
                                pod1, pod2, sp.Count(x => x.Partner != null)));
            Console.WriteLine(F("  (takt ridici smycky je {0:F3} s)",
                                ARBot.Common.Configuration.Profile.Ts / 1000.0));
            Console.WriteLine();
        }

        private static void Top(List<Spike> sp, int topN)
        {
            // Distinct: jedna spicka muze byt partnerem VIC predchozich, takze bez toho by se
            // tataz udalost v tabulce objevila dvakrat (a vypadala by jako dve ruzne).
            var druhe = sp.Where(x => x.Partner != null).Select(x => x.Partner).Distinct()
                          .OrderByDescending(x => x.NoseDownDeg).Take(topN).ToList();
            if (druhe.Count == 0) return;
            Console.WriteLine($"=== 7. NEJSILNEJSICH {druhe.Count} UDALOSTI (druha spicka) ===");
            Console.WriteLine("      cas     rel[s]  v[m/s]  klopeni[deg]  z gyra[deg]  rozkmit[deg]  dv prikazu");
            foreach (var x in druhe)
                Console.WriteLine(F("  {0} {1,9:F2} {2,7:F2} {3,13:F2} {4,12:F2} {5,13:F2} {6,11:F3}",
                                    Abs(x.T), x.T, x.V, x.NoseDownDeg, x.GyroNoseDeg, x.SpanDeg, x.DvCmd));
            Console.WriteLine();
        }

        /// <summary>
        /// <b>Blok 8 — SYROVY PRUBEH nejsilnejsich udalosti.</b> Statistika rekne, co dela
        /// tisic udalosti prumerne; tohle ukaze jednu tak, jak se stala.
        ///
        /// <para><b>Nacpak.</b> Kdyz clovek vi, ze „tady jsem videl zakopnout o koren", je prumer
        /// pres cely zaznam to nejhorsi, cim se na to divat — 6 skutecnych zakopnuti mezi 1561
        /// prahovymi prekrocenimi je 0,4 %, tedy hluboko pod rozlisenim. Graf rychlosti klopeni
        /// proti UJETE DRAZE od udalosti dovoli precist dvojici razu (nebo jeji nepritomnost)
        /// primo, bez jakehokoli prumerovani — a vodorovna osa je rovnou v metrech, takze se
        /// odstup obou vrcholu cte jako rozvor.</para>
        /// </summary>
        private static void Detail(List<Sample> s, List<Spike> sp, int n)
        {
            if (n <= 0 || sp.Count == 0) return;
            var vyber = sp.OrderByDescending(x => x.Amp).Take(n).OrderBy(x => x.T).ToList();
            Console.WriteLine($"=== 8. SYROVY PRUBEH {vyber.Count} NEJSILNEJSICH UDALOSTI ===");
            Console.WriteLine("  sloupec 'draha' je vzdalenost od vrcholu [m] - dvojice razu by tedy byla");
            Console.WriteLine("  druhy vrchol kolem rozvoru; '|' je nula, vychylka doprava = nos DOLU");
            foreach (var x in vyber)
            {
                Console.WriteLine();
                Console.WriteLine(F("  --- {0} (rel. {1:F2} s), v = {2:F2} m/s, prikaz dv = {3:+0.000;-0.000;0.000} m/s2",
                                    Abs(x.T), x.T, x.V, x.DvCmd));
                Console.WriteLine("     cas[s] draha[m]  v[m/s]  klopeni[deg]  rychlost klopeni [deg/s]");
                double last = double.NegativeInfinity;
                for (int i = 0; i < s.Count; i++)
                {
                    double dt = s[i].T - x.T;
                    if (dt < -0.5) continue;
                    if (dt > 1.5) break;
                    if (s[i].T - last < 0.045) continue;   // 100 Hz -> ~20 radku za sekundu
                    last = s[i].T;
                    double rate = s[i].Hp * 180 / Math.PI;
                    Console.WriteLine(F("  {0,8:F2} {1,8:F2} {2,7:F2} {3,13:F2}  {4}",
                                        dt, s[i].S - x.S, s[i].V, s[i].Pitch * 180 / Math.PI,
                                        Bar(rate, 120, 28)));
                }
            }
            Console.WriteLine();
        }

        /// <summary>Vodorovny sloupec kolem nuly — aby se dvojice vrcholu dala precist okem.</summary>
        private static string Bar(double v, double max, int half)
        {
            int k = (int)Math.Round(half * Math.Max(-1, Math.Min(1, v / max)));
            var b = new char[2 * half + 1];
            for (int i = 0; i < b.Length; i++) b[i] = ' ';
            b[half] = '|';
            if (k >= 0) for (int i = 1; i <= k; i++) b[half + i] = '#';
            else for (int i = 1; i <= -k; i++) b[half - i] = '#';
            return new string(b) + F(" {0,7:F1}", v);
        }

        // ---------------------------------------------------------------- pomocne

        /// <summary>
        /// Vychylka klopeni DOPREDU proti klidu tesne pred udalosti [deg], z uhlu VN100.
        /// Klid se bere z okna -0,6 az -0,3 s, aby v nem uz nebyla samotna prvni spicka.
        /// </summary>
        private static double NoseDown(List<Sample> s, int idx, int noseUpSign)
        {
            double t = s[idx].T;
            double bas = 0;
            int nb = 0;
            for (int i = idx; i >= 0 && s[i].T > t - 0.6; i--)
                if (s[i].T <= t - 0.3) { bas += s[i].Pitch; nb++; }
            if (nb == 0) return double.NaN;
            bas /= nb;
            double worst = 0;
            // ⚠️ Okno 0,8 s, ne 0,5: u zakopnuti 14:12:52 trvala cesta z +9,7 na -9,9 stupne
            // 0,75 s, takze kratsi okno by ji uriznulo v pulce.
            for (int i = idx; i < s.Count && s[i].T < t + 0.8; i++)
            {
                double down = -noseUpSign * (s[i].Pitch - bas);   // + = nos dolu
                if (down > worst) worst = down;
            }
            return worst * 180 / Math.PI;
        }

        /// <summary>
        /// Totéž z <b>integrace gyra</b> — největší <i>souvislá</i> rotace nosem dolů [deg].
        ///
        /// <para>⚠️ <b>Integrál se při změně smyslu NULUJE, a je to podstata věci.</b> První
        /// verze tohohle měřidla sčítala přes celé okno, takže se u <b>kmitavé</b> události fáze
        /// nahoru a dolů navzájem vyrušily a vyšlo skoro nic — u zakopnutí 14:12:52 z Kola 3b
        /// vrátila <b>0,82°</b>, ačkoli se robot podle úhlu i podle gyra (−36, +44, +44, −55,
        /// −95 °/s) sklopil z +9,7° na −9,9°. Na ten rozpor se pak dalo usoudit „velké výchylky
        /// úhlu gyro nepotvrzuje, budou to artefakty atitudy" — což bylo <b>obráceně</b>: vadná
        /// byla kontrolní veličina, ne měřený úhel. Nulování při přechodu přes nulu měří to, na
        /// čem záleží: o kolik se robot v jednom kuse sklopil dopředu.</para>
        /// </summary>
        private static double GyroNoseDown(List<Sample> s, int idx, int noseUpSign)
        {
            double t = s[idx].T, acc = 0, worst = 0;
            int start = idx;
            while (start > 0 && s[start].T > t - 0.2) start--;
            for (int i = start + 1; i < s.Count && s[i].T < t + 0.8; i++)
            {
                double dt = s[i].T - s[i - 1].T;
                if (dt <= 0 || dt > 0.1) continue;
                acc += -noseUpSign * s[i].GyroY * dt;
                if (acc < 0) acc = 0;              // jina faze kmitu - zacina se znovu
                if (acc > worst) worst = acc;
            }
            return worst * 180 / Math.PI;
        }

        /// <summary>
        /// Doplni k udalosti tri veliciny, ktere rozhoduji, jestli se u ni ozvena vubec MA cekat:
        /// primost jizdy, odezvu do strany a rychlost PRED udalosti.
        /// </summary>
        private static void Okoli(List<Sample> s, Spike x)
        {
            double t = x.T, yaw = 0, roll = 0, vs = 0;
            int nv = 0, ny = 0;
            for (int i = x.Idx; i > 0 && s[i].T > t - 0.5; i--)
            {
                if (s[i].T <= t - 0.2) { vs += s[i].V; nv++; }
            }
            for (int i = Math.Max(0, x.Idx - 30); i < s.Count && s[i].T < t + 0.8; i++)
            {
                // ⚠️ Staceni PRUMEREM, ne maximem. Pri otresu ma gyro Z velke prechodne vychylky
                // i pri jizde rovne (hrbol robotem skubne), takze maximum vybere jako "zatacel"
                // uplne vsechno - pri prahu 10 deg/s nezbyla ani jedna udalost. Prumer vibraci
                // vyrusi a nechá jen skutecne otaceni.
                yaw += s[i].GyroZ; ny++;
                // Naklon do strany naopak MAXIMEM: hleda se, jestli udalost vubec mela
                // jednostrannou slozku, a ta je ze sve podstaty prechodna.
                roll = Math.Max(roll, Math.Abs(s[i].Roll));
            }
            x.YawRate = ny > 0 ? Math.Abs(yaw / ny) : 0;
            x.RollRate = roll;
            // ⚠️ Rychlost PRED udalosti, ne v ni. Draha se integruje z kol, a prave ta kola pri
            // zakopnuti prokluzuji nebo se zastavi (u 14:12:52 kolisa 0,30-0,86 m/s), takze
            // odometricka draha behem udalosti skutecnou drahu PODHODNOCUJE - a odstup ozveny
            // tim vyjde kratsi, nez je rozvor. Tohle je ta imunni varianta.
            x.VPre = nv > 0 ? vs / nv : x.V;
        }

        /// <summary>
        /// Rozkmit klopeni vrchol-vrchol v okoli udalosti [deg] — od nejvyssiho nosu nahoru
        /// po nejnizsi nos dolu. U zakopnuti o koren je to ta nazornejsi velicina: robot se
        /// nejdriv PREDNIMI koly vyhoupne nahoru a teprve pak sklopi dopredu.
        /// </summary>
        private static double Span(List<Sample> s, int idx)
        {
            double t = s[idx].T, lo = double.MaxValue, hi = double.MinValue;
            for (int i = idx; i >= 0 && s[i].T > t - 0.5; i--) { }
            int start = idx;
            while (start > 0 && s[start].T > t - 0.5) start--;
            for (int i = start; i < s.Count && s[i].T < t + 0.8; i++)
            {
                if (s[i].Pitch < lo) lo = s[i].Pitch;
                if (s[i].Pitch > hi) hi = s[i].Pitch;
            }
            return hi > lo ? (hi - lo) * 180 / Math.PI : double.NaN;
        }

        /// <summary>Derivace prikazovane rychlosti [m/s2] v case t (z dvou sousednich prikazu).</summary>
        private static double CommandDerivative(List<(double T, double V, bool Held, bool EStop)> cmd, double t)
        {
            if (cmd.Count < 2) return double.NaN;
            int i = Bisect(cmd.Count, k => cmd[k].T, t);
            if (i <= 0 || i >= cmd.Count) return double.NaN;
            double dt = cmd[i].T - cmd[i - 1].T;
            if (dt <= 0 || dt > 0.5) return double.NaN;
            return (cmd[i].V - cmd[i - 1].V) / dt;
        }

        private static void InterpolateSpeed(List<Sample> s, List<(double T, double V)> mot)
        {
            foreach (var x in s)
            {
                int i = Bisect(mot.Count, k => mot[k].T, x.T);
                if (i <= 0) { x.V = mot.Count > 0 ? Math.Abs(mot[0].V) : 0; continue; }
                if (i >= mot.Count) { x.V = Math.Abs(mot[mot.Count - 1].V); continue; }
                double dt = mot[i].T - mot[i - 1].T;
                double f = dt > 0 ? (x.T - mot[i - 1].T) / dt : 0;
                x.V = Math.Abs(mot[i - 1].V + f * (mot[i].V - mot[i - 1].V));
            }
        }

        /// <summary>Odecte klouzavy prumer sirky <paramref name="win"/> sekund od gyroY.</summary>
        private static void HighPass(List<Sample> s, double win)
        {
            if (win <= 0) { foreach (var x in s) x.Hp = x.GyroY; return; }
            int lo = 0, hi = 0;
            double sum = 0;
            var ma = new double[s.Count];
            for (int i = 0; i < s.Count; i++)
            {
                while (hi < s.Count && s[hi].T <= s[i].T + win / 2) { sum += s[hi].GyroY; hi++; }
                while (lo < hi && s[lo].T < s[i].T - win / 2) { sum -= s[lo].GyroY; lo++; }
                ma[i] = hi > lo ? sum / (hi - lo) : s[i].GyroY;
            }
            for (int i = 0; i < s.Count; i++) s[i].Hp = s[i].GyroY - ma[i];
        }

        /// <summary>Klouzavy prumer slozek zrychleni — bez nej je „klid" plny otresu.</summary>
        private static void SmoothAcc(List<Sample> s, double win)
        {
            int lo = 0, hi = 0;
            double sx = 0, sy = 0, sz = 0;
            for (int i = 0; i < s.Count; i++)
            {
                while (hi < s.Count && s[hi].T <= s[i].T + win / 2)
                {
                    if (!double.IsNaN(s[hi].AccX)) { sx += s[hi].AccX; sy += s[hi].AccY; sz += s[hi].AccZ; }
                    hi++;
                }
                while (lo < hi && s[lo].T < s[i].T - win / 2)
                {
                    if (!double.IsNaN(s[lo].AccX)) { sx -= s[lo].AccX; sy -= s[lo].AccY; sz -= s[lo].AccZ; }
                    lo++;
                }
                int n = Math.Max(1, hi - lo);
                s[i].SmX = sx / n; s[i].SmY = sy / n; s[i].SmZ = sz / n;
                s[i].SmMag = Math.Sqrt(s[i].SmX * s[i].SmX + s[i].SmY * s[i].SmY + s[i].SmZ * s[i].SmZ);
            }
        }

        private static double Sd(List<double> v)
        {
            if (v.Count < 2) return double.NaN;
            double m = v.Average();
            return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / v.Count);
        }

        /// <summary>Smernice regrese y na x plus Pearsonuv koeficient (obe rady stejne dlouhe).</summary>
        private static double Slope(List<double> x, List<double> y, out double r)
        {
            int n = Math.Min(x.Count, y.Count);
            double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                sx += x[i]; sy += y[i]; sxx += x[i] * x[i]; syy += y[i] * y[i]; sxy += x[i] * y[i];
            }
            double vx = sxx / n - (sx / n) * (sx / n), vy = syy / n - (sy / n) * (sy / n);
            double cov = sxy / n - (sx / n) * (sy / n);
            r = (vx > 0 && vy > 0) ? cov / Math.Sqrt(vx * vy) : double.NaN;
            return vx > 0 ? cov / vx : double.NaN;
        }

        private static int Bisect(int n, Func<int, double> key, double t)
        {
            int lo = 0, hi = n;
            while (lo < hi) { int m = (lo + hi) / 2; if (key(m) < t) lo = m + 1; else hi = m; }
            return lo;
        }

        private static double Unwrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        private static double Sec(DateTime t, ref DateTime t0)
        {
            if (t0 == DateTime.MinValue) t0 = t;
            return (t - t0).TotalSeconds;
        }

        /// <summary>Absolutni cas prvniho vzorku — kvuli prevodu „v 14:12:20" na sekundy.</summary>
        private static DateTime t0Abs;

        /// <summary>Relativni sekundy na hodiny ze zaznamu.</summary>
        private static string Abs(double t)
            => double.IsInfinity(t) ? (t < 0 ? "zacatek" : "konec")
                                    : t0Abs.AddSeconds(t).ToString("HH:mm:ss.f", CultureInfo.InvariantCulture);

        /// <summary>
        /// Prepinac okna: <c>HH:MM:SS</c> (hodiny ze zaznamu) nebo proste sekundy od zacatku.
        /// Obe formy schvalne — clovek si pamatuje hodiny, report tiskne sekundy.
        /// </summary>
        private static double Resolve(string arg, double vychozi)
        {
            if (string.IsNullOrEmpty(arg)) return vychozi;
            if (arg.Contains(":"))
            {
                if (!TimeSpan.TryParse(arg, CultureInfo.InvariantCulture, out var tod))
                    throw new ArgumentException($"Cas '{arg}' nejde precist (cekam HH:MM:SS nebo sekundy).");
                // Den se bere z prvniho vzorku; zaznam pres pulnoc by chtel vlastni osetreni,
                // ale zadny takovy zatim neexistuje a tise spatne by to bylo horsi nez vyjimka.
                double v = (t0Abs.Date + tod - t0Abs).TotalSeconds;
                if (v < -1) throw new ArgumentException(
                    $"Cas {arg} je PRED zacatkem zaznamu ({t0Abs:HH:mm:ss}).");
                return v;
            }
            return double.Parse(arg, CultureInfo.InvariantCulture);
        }

        private static string F(string fmt, params object[] a)
            => string.Format(CultureInfo.InvariantCulture, fmt, a);
    }
}
