using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Je před robotem klín, ve kterém chybí SEMANTIKA?</b> — a stojí kvůli němu rychlost?
    ///
    /// <para><b>Nač to je.</b> Praktické pozorování z terénu: zorná pole barevných streamů obou
    /// D435 se ve směru jízdy nemusí překrývat (kamery jsou pootočené o ±29°), takže přímo před
    /// robotem může zůstat úzký klín, kam barva nikdy nedosáhne. Buňka je pak
    /// <see cref="CellState.Unknown"/>, ačkoli hloubka o ní říká „volno" — a protože <c>VBrake</c>
    /// počítá volno po první buňku, která NENÍ <c>Free</c>, srazí to dopřednou rychlost.</para>
    ///
    /// <para><b>Co se měří</b> (všechno ze zpráv, tedy z toho, co skutečně vyrobila běžící
    /// aplikace):</para>
    /// <list type="number">
    /// <item><description><b>Rozpad Unknown podle PŘÍČINY</b> — chybí geometrie, chybí semantika,
    /// nebo obojí. Bez toho se „unknown" nedá léčit: každá příčina má jinou léčbu.</description></item>
    /// <item><description><b>Profil podle AZIMUTU</b> v tělesovém rámci (0 = směr jízdy). Klín se
    /// pozná tím, že podíl „chybí jen semantika" má <b>maximum kolem nuly</b> a do stran klesá.
    /// Kdyby byl plochý, není to klín mezi kamerami, ale prostě málo dat.</description></item>
    /// <item><description><b>Dopad na rychlost</b> — po paprsku vpřed se spočítá <c>freeAhead</c>
    /// dnešním pravidlem (po první ne-<c>Free</c>) a pravidlem „stačí potvrzená geometrie", a z obou
    /// <c>VBrake</c>. Rozdíl je <b>celý zisk</b>, který doplnění klínu může přinést; když je nula,
    /// nemá cenu nic měnit.</description></item>
    /// </list>
    ///
    /// <para>⚠️ <b>Měří se jen to, co grid nese</b> — jestli díru způsobilo zorné pole barvy, nebo
    /// stín za překážkou, z gridu samotného poznat nejde. Od toho je azimutový profil: stín je tam,
    /// kde jsou překážky, kdežto klín mezi kamerami sedí pořád na témže azimutu.</para>
    /// </summary>
    public static class WedgeReport
    {
        /// <summary>Dosah, do kterého se buňky zkoumají [m].</summary>
        private const double RangeM = 6.0;

        /// <summary>
        /// Poloměr půdorysu robota [m] — buňky blíž jsou sjízdné, pokud nejsou
        /// <see cref="CellState.Blocked"/>. Musí odpovídat
        /// <see cref="LocalPlannerConfig.FootprintRadiusM"/>, jinak měřidlo měří jiný robot.
        /// </summary>
        private static readonly double Pudorys = new LocalPlannerConfig().FootprintRadiusM;

        /// <summary>Šířka azimutového pásma [°].</summary>
        private const double BinDeg = 5.0;

        /// <summary>Kolik pásem na každou stranu od směru jízdy (±60°).</summary>
        private const int Bins = 12;

        public static void Run(RecordFile rec, int limit, double wedgeDeg, double wedgeConf)
        {
            Geometrie(rec);

            var poses = new PoseTrack(rec);
            var entries = rec.Index.Where(e => e.MsgName == "OccupancyGridMsg").ToList();
            if (limit > 0 && limit < entries.Count)
            {
                // Rovnoměrně po celém záznamu, ne prvních N - začátek běhu je vždycky prázdný grid.
                int krok = Math.Max(1, entries.Count / limit);
                entries = entries.Where((e, i) => i % krok == 0).Take(limit).ToList();
            }

            Console.WriteLine($"OccupancyGridMsg: {entries.Count} gridu (dosah {RangeM:F1} m, "
                              + $"pasma po {BinDeg:F0} stupnich)");
            if (entries.Count == 0) { Console.WriteLine("Zaznam zadny occupancy grid nema."); return; }

            var free = new long[2 * Bins];
            var blocked = new long[2 * Bins];
            var chybiSem = new long[2 * Bins];     // occ potvrzeno volné, road nic
            var chybiGeom = new long[2 * Bins];    // road potvrzeno cesta, occ nic
            var chybiOboji = new long[2 * Bins];

            // Klin podle vzdalenosti: pasmo |azimut| < 5 stupnu, po pulmetrech.
            var klinFree = new long[(int)(RangeM * 2)];
            var klinSem = new long[(int)(RangeM * 2)];
            var klinCelkem = new long[(int)(RangeM * 2)];

            var freeAheadDnes = new Stats("freeAhead dnes         ");
            var freeAheadGeom = new Stats("freeAhead z geometrie  ");
            var vBrakeDnes = new Stats("VBrake dnes            ");
            var vBrakeGeom = new Stats("VBrake z geometrie     ");
            var freeAheadFill = new Stats("freeAhead s doplnenim  ");
            var vBrakeFill = new Stats("VBrake s doplnenim     ");
            var doplneno = new Stats("doplnenych bunek       ");
            var kandidatu = new Stats("kandidatu na doplneni  ");
            var bezSouseda = new Stats("z toho bez souseda     ");

            // Na cem se paprsek vpred zastavil: 0 blokuje geometrie, 1 blokuje semantika,
            // 2 chybi geometrie, 3 chybi semantika, 4 chybi oboji, 5 mimo grid / az na dosah.
            var duvody = new long[6];
            var duvodyPoDoplneni = new long[6];

            // Proc se NEDOPLNILA prave ta bunka, na ktere se paprsek zastavil: 0 mimo klin,
            // 1 blize nez minRange, 2 geometrie nepotvrzena, 3 bez pricneho souseda,
            // 4 doplneno, ale nestacilo to na prah, 5 doplneno a bunka je Free.
            var procNe = new long[6];

            // Kdyz paprsek zastavi "chybi semantika": jak SIROKA je ta dira pricne a jak daleko
            // od robota lezi. Rozhoduje to o lecbe - uzkou diru lze interpolovat z okoli,
            // metrovou uz ne.
            var diraSirka = new Stats("sirka diry pricne      ");
            var diraKde = new Stats("vzdalenost diry        ");

            // Co je v miste, kde se paprsek zastavil: jak silny je vzorek v BUNCE a v nejblizsich
            // sousedech pricne. Rozhoduje to o lecbe - kdyz jsou sousede slabi, interpolace z nich
            // bunku nikdy nerozhodne, at se dela cokoliv.
            var vzorekBunky = new Stats("log-odds bunky         ");
            var vzorekVlevo = new Stats("log-odds souseda vlevo ");
            var vzorekVpravo = new Stats("log-odds souseda vprav ");
            var dosahVlevo = new Stats("vzdalenost souseda vlev");
            var dosahVpravo = new Stats("vzdalenost souseda vpra");
            int sousedRozhodnuty = 0, sousedSlaby = 0, sousedZadny = 0;
            int bezPozy = 0, pouzito = 0;

            var cfg = new LocalPlannerConfig();

            foreach (var e in entries)
            {
                if (!(rec.Read(e) is OccupancyGridMsg g) || g.Occ == null || g.Size <= 0) continue;
                var poza = poses.Nearest(g.TimeStamp);
                if (poza == null) { bezPozy++; continue; }
                pouzito++;

                Sesbirej(g, poza.X, poza.Y, poza.Theta, free, blocked, chybiSem, chybiGeom, chybiOboji,
                         klinFree, klinSem, klinCelkem);

                double fDnes = FreeAhead(g, poza.X, poza.Y, poza.Theta, geometrieStaci: false, duvody);
                ZmerDiru(g, poza.X, poza.Y, poza.Theta, fDnes, diraSirka, diraKde);
                ZmerVzorky(g, poza.X, poza.Y, poza.Theta, fDnes, vzorekBunky,
                           vzorekVlevo, vzorekVpravo, dosahVlevo, dosahVpravo,
                           ref sousedRozhodnuty, ref sousedSlaby, ref sousedZadny);
                double fGeom = FreeAhead(g, poza.X, poza.Y, poza.Theta, geometrieStaci: true);
                freeAheadDnes.Add(fDnes);
                freeAheadGeom.Add(fGeom);
                vBrakeDnes.Add(cfg.VBrake(fDnes));
                vBrakeGeom.Add(cfg.VBrake(fGeom));

                // Simulace NAVRZENE lecby (WedgeFiller) nad timze gridem - tedy ne "co by bylo,
                // kdyby semantika nebyla potreba", ale "co udela to, co se opravdu zapne".
                if (wedgeDeg > 0)
                {
                    var kopie = Rekonstruuj(g);
                    var filler = new WedgeFiller(kopie, wedgeDeg * Math.PI / 180.0 / 2.0,
                                                 confidence: wedgeConf);
                    doplneno.Add(filler.Fill(poza.X, poza.Y, poza.Theta));
                    kandidatu.Add(filler.LastStats.Kandidatu);
                    bezSouseda.Add(filler.LastStats.BezSouseda);
                    double fFill = FreeAheadGrid(kopie, g, poza.X, poza.Y, poza.Theta, duvodyPoDoplneni);
                    ProcNedoplneno(g, kopie, poza.X, poza.Y, poza.Theta, fDnes, wedgeDeg, procNe);
                    freeAheadFill.Add(fFill);
                    vBrakeFill.Add(cfg.VBrake(fFill));
                }
            }

            Console.WriteLine($"pouzito {pouzito} gridu, bez pozy {bezPozy}");
            Console.WriteLine();

            Profil(free, blocked, chybiSem, chybiGeom, chybiOboji);
            PodleVzdalenosti(klinFree, klinSem, klinCelkem);
            Duvody(duvody);
            if (wedgeDeg > 0) ProcNe(procNe);
            if (wedgeDeg > 0) Duvody(duvodyPoDoplneni, " PO DOPLNENI KLINU");
            if (diraSirka.Count > 0)
            {
                Console.WriteLine("--- Dira v semantice, na ktere se paprsek zastavil ---");
                Console.WriteLine("  " + diraKde.Line("m"));
                Console.WriteLine("  " + diraSirka.Line("m"));
                Console.WriteLine("  (klin mezi kamerami je 0,19 m ve 3 m a 0,38 m v 6 m - sirsi dira"
                                  + " ma jinou pricinu a interpolaci z okoli se vylecit neda)");
                Console.WriteLine();
            }
            if (vzorekBunky.Count > 0)
            {
                Console.WriteLine("--- Co je v miste, kde se paprsek zastavil (sila vzorku semantiky) ---");
                Console.WriteLine("  prahy: Free <= " + (-1.0).ToString("F2", CultureInfo.InvariantCulture)
                                  + ", Blocked >= 1,00; nula = zadny vzorek");
                Console.WriteLine("  " + vzorekBunky.Line(""));
                Console.WriteLine("  " + vzorekVlevo.Line(""));
                Console.WriteLine("  " + vzorekVpravo.Line(""));
                Console.WriteLine("  " + dosahVlevo.Line("m"));
                Console.WriteLine("  " + dosahVpravo.Line("m"));
                int n = sousedRozhodnuty + sousedSlaby + sousedZadny;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  sousede pricne: ROZHODNUTI z obou stran {0:F1} %, aspon jeden jen SLABY {1:F1} %, "
                    + "aspon jeden CHYBI {2:F1} %",
                    100.0 * sousedRozhodnuty / n, 100.0 * sousedSlaby / n, 100.0 * sousedZadny / n));
                Console.WriteLine();
            }
            Rychlost(cfg, freeAheadDnes, freeAheadGeom, vBrakeDnes, vBrakeGeom);
            if (wedgeDeg > 0)
            {
                Doplneni(wedgeDeg, wedgeConf, doplneno, kandidatu, bezSouseda,
                         freeAheadFill, vBrakeFill, freeAheadDnes, vBrakeDnes);
                Plazeni(cfg, vBrakeDnes, vBrakeFill, vBrakeGeom);
            }
        }

        /// <summary>
        /// Rozpad buněk v dosahu podle azimutu v <b>tělesovém</b> rámci (0 = směr jízdy, + vlevo).
        /// Vzdálenější pásmo obsahuje víc buněk, proto se profil tiskne v PROCENTECH pásma.
        /// </summary>
        private static void Sesbirej(OccupancyGridMsg g, double rx, double ry, double theta,
                                     long[] free, long[] blocked,
                                     long[] chybiSem, long[] chybiGeom, long[] chybiOboji,
                                     long[] klinFree, long[] klinSem, long[] klinCelkem)
        {
            double cosH = Math.Cos(theta), sinH = Math.Sin(theta);
            int span = (int)Math.Ceiling(RangeM / g.Resolution) + 1;
            int cx0 = (int)Math.Floor(rx / g.Resolution) - g.OriginX;
            int cy0 = (int)Math.Floor(ry / g.Resolution) - g.OriginY;

            for (int j = Math.Max(0, cy0 - span); j <= Math.Min(g.Size - 1, cy0 + span); j++)
                for (int i = Math.Max(0, cx0 - span); i <= Math.Min(g.Size - 1, cx0 + span); i++)
                {
                    double dx = g.CenterX(i) - rx, dy = g.CenterY(j) - ry;
                    double r2 = dx * dx + dy * dy;
                    if (r2 > RangeM * RangeM || r2 < 0.25) continue;   // pod 0,5 m je robot sám

                    double bx = dx * cosH + dy * sinH;      // vpřed
                    double by = -dx * sinH + dy * cosH;     // vlevo
                    double azDeg = Math.Atan2(by, bx) * 180.0 / Math.PI;
                    int bin = (int)Math.Floor(azDeg / BinDeg) + Bins;
                    if (bin < 0 || bin >= 2 * Bins) continue;

                    int idx = i + j * g.Size;
                    float o = g.Occ[idx] * g.Scale;
                    float r = g.Road != null ? g.Road[idx] * g.Scale : 0f;

                    double range = Math.Sqrt(r2);
                    if (o >= g.BlockedThreshold || r >= g.BlockedThreshold)
                    {
                        blocked[bin]++;
                        ZapisKlin(bx, by, range, false, false, klinFree, klinSem, klinCelkem);
                        continue;
                    }
                    if (o <= g.FreeThreshold && r <= g.FreeThreshold)
                    {
                        free[bin]++;
                        ZapisKlin(bx, by, range, true, false, klinFree, klinSem, klinCelkem);
                        continue;
                    }

                    bool geomOk = o <= g.FreeThreshold;
                    bool semOk = r <= g.FreeThreshold;
                    if (geomOk && !semOk) chybiSem[bin]++;
                    else if (!geomOk && semOk) chybiGeom[bin]++;
                    else chybiOboji[bin]++;
                    ZapisKlin(bx, by, range, false, geomOk && !semOk, klinFree, klinSem, klinCelkem);
                }
        }

        /// <summary>
        /// Zapis do profilu podle VZDALENOSTI, jen v pasmu <b>|azimut| &lt; 5 stupnu</b>. Rozlisi dve
        /// vysvetleni, ktera azimutovy profil splacne dohromady: <b>klin mezi zornymi poli</b> se
        /// s dalkou rozsiruje (v metrech), kdezto <b>slepa zona pod kamerami</b> je jen u robota.
        /// </summary>
        private static void ZapisKlin(double bx, double by, double range, bool free, bool jenSemantika,
                                      long[] klinFree, long[] klinSem, long[] klinCelkem)
        {
            if (bx <= 0) return;
            double azDeg = Math.Atan2(by, bx) * 180.0 / Math.PI;
            if (Math.Abs(azDeg) >= 5) return;
            int k = (int)(range * 2);
            if (k < 0 || k >= klinCelkem.Length) return;
            klinCelkem[k]++;
            if (free) klinFree[k]++;
            if (jenSemantika) klinSem[k]++;
        }

        private static void PodleVzdalenosti(long[] klinFree, long[] klinSem, long[] klinCelkem)
        {
            Console.WriteLine("--- Pasmo |azimut| < 5 stupnu podle VZDALENOSTI ---");
            Console.WriteLine("  (klin mezi zornymi poli se s dalkou ROZSIRUJE, slepa zona pod kamerami je jen u robota)");
            Console.WriteLine("  vzdalenost      bunek      Free   chybi_jen_SEM");
            for (int k = 0; k < klinCelkem.Length; k++)
            {
                if (klinCelkem[k] == 0) continue;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4:F1}-{1,4:F1} m  {2,9}  {3,6:F1} %  {4,10:F1} %",
                    k / 2.0, (k + 1) / 2.0, klinCelkem[k],
                    100.0 * klinFree[k] / klinCelkem[k], 100.0 * klinSem[k] / klinCelkem[k]));
            }
            Console.WriteLine();
        }

        /// <summary>
        /// <b>Zorna pole z INTRINSIK v zaznamu</b> - tedy z toho, co kamera opravdu hlasila, ne
        /// z katalogoveho udaje. HFOV = 2*atan(W / 2*fx); montazni yaw se cte z robot-centricke
        /// transformace, kterou <c>CameraFrameProcessor</c> do snimku ulozil.
        ///
        /// <para>Z toho se spocita, jestli mezi barevnymi zornymi poli <b>je</b> mezera - to je
        /// pricina, kterou azimutovy profil umi jen naznacit.</para>
        /// </summary>
        private static void Geometrie(RecordFile rec)
        {
            var videno = new Dictionary<string, ARBot.Common.Devices.CameraFrame>();
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "CameraFrame") continue;
                if (!(rec.Read(e) is ARBot.Common.Devices.CameraFrame f)) continue;
                string jmeno = f.Name ?? string.Empty;
                if (f.Projection == null || videno.ContainsKey(jmeno)) continue;
                videno[jmeno] = f;
                if (videno.Count >= 3) break;
            }

            Console.WriteLine("--- Zorna pole z INTRINSIK v zaznamu ---");
            if (videno.Count == 0)
            {
                Console.WriteLine("  zadny snimek s popisem projekce (CameraFrame verze < 4?)");
                Console.WriteLine();
                return;
            }

            var barva = new List<Tuple<string, double, double>>();
            foreach (var kv in videno.OrderBy(k => k.Key))
            {
                var pr = kv.Value.Projection;
                double yawDeg = YawDeg(pr.Transformation);
                string radek = "  " + kv.Key.PadRight(6) + string.Format(CultureInfo.InvariantCulture,
                    " montazni yaw {0,6:F1} st.", yawDeg);

                if (pr.Intrinsics != null)
                    radek += string.Format(CultureInfo.InvariantCulture,
                        "   hloubka HFOV {0,5:F1} st. ({1}x{2})",
                        Hfov(pr.Intrinsics), pr.Intrinsics.Width, pr.Intrinsics.Height);
                if (pr.ColorIntrinsics != null)
                {
                    radek += string.Format(CultureInfo.InvariantCulture,
                        "   barva HFOV {0,5:F1} st. ({1}x{2})",
                        Hfov(pr.ColorIntrinsics), pr.ColorIntrinsics.Width, pr.ColorIntrinsics.Height);
                    barva.Add(Tuple.Create(kv.Key, yawDeg, Hfov(pr.ColorIntrinsics) / 2));
                }
                else radek += "   barva: intrinsika v zazname NENI (CameraFrame verze < 5)";
                Console.WriteLine(radek);
            }

            if (barva.Count >= 2)
            {
                // Kamery jsou pootocene na obe strany; klin je mezera mezi vnitrnimi okraji.
                var vlevo = barva.OrderByDescending(b => b.Item2).First();
                var vpravo = barva.OrderBy(b => b.Item2).First();
                double vnitrniL = vlevo.Item2 - vlevo.Item3;     // nejpravejsi okraj leve kamery
                double vnitrniP = vpravo.Item2 + vpravo.Item3;   // nejlevejsi okraj prave kamery
                double mezera = vnitrniL - vnitrniP;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  barva pokryva {0:F1}..{1:F1} st. a {2:F1}..{3:F1} st. -> {4} {5:F1} st.",
                    vpravo.Item2 - vpravo.Item3, vnitrniP, vnitrniL, vlevo.Item2 + vlevo.Item3,
                    mezera > 0 ? "MEZERA (klin)" : "prekryv", Math.Abs(mezera)));
                if (mezera > 0)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  sirka klinu: {0:F2} m ve 3 m, {1:F2} m v 6 m pred robotem",
                        2 * 3.0 * Math.Tan(mezera / 2 * Math.PI / 180),
                        2 * 6.0 * Math.Tan(mezera / 2 * Math.PI / 180)));
            }
            Console.WriteLine();
        }

        private static double Hfov(ARBot.Common.Coordinates.Intrinsics i)
            => 2 * Math.Atan(i.Width / (2.0 * i.Fx)) * 180.0 / Math.PI;

        /// <summary>
        /// Montazni yaw [st.] z robot-centricke transformace (kladny doleva).
        ///
        /// <para>Cte se OTOCENIM OSY POHLEDU, ne vytazenim uhlu z matice: opticka osa kamery je
        /// v jejim ramci +Z, takze staci ji transformovat a vzit azimut vysledku. Vytazeni uhlu
        /// (atan2 z prvku matice) by muselo znat konvenci skladani v
        /// <c>Conversions.CameraToWorldTransform</c> vcetne posunu o -90 stupnu - a kdyz se v tom
        /// spletete, vyjdou hodnoty, ktere vypadaji verohodne (-1,9 a 177,7 misto +-29).</para>
        /// </summary>
        private static double YawDeg(System.Numerics.Matrix4x4 m)
        {
            var osa = System.Numerics.Vector3.TransformNormal(
                          new System.Numerics.Vector3(0, 0, 1), m);
            return Math.Atan2(osa.Y, osa.X) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Volno po paprsku přímo vpřed [m] — přesně to, čím <c>LocalPathPlanner</c> krmí
        /// <see cref="LocalPlannerConfig.VBrake"/>. Při <paramref name="geometrieStaci"/> se za volnou
        /// bere i buňka, kterou potvrdila jen hloubka (tedy hypotéza, kterou měříme).
        /// </summary>
        private static double FreeAhead(OccupancyGridMsg g, double rx, double ry, double theta,
                                        bool geometrieStaci, long[] duvody = null)
        {
            double cosH = Math.Cos(theta), sinH = Math.Sin(theta);
            double krok = g.Resolution * 0.5;
            for (double s = 0; s <= RangeM; s += krok)
            {
                double wx = rx + s * cosH, wy = ry + s * sinH;
                int i = (int)Math.Floor(wx / g.Resolution) - g.OriginX;
                int j = (int)Math.Floor(wy / g.Resolution) - g.OriginY;
                if (i < 0 || j < 0 || i >= g.Size || j >= g.Size)
                {
                    if (duvody != null) duvody[5]++;
                    return s;
                }

                int idx = i + j * g.Size;
                float o = g.Occ[idx] * g.Scale;
                float r = g.Road != null ? g.Road[idx] * g.Scale : 0f;

                if (o >= g.BlockedThreshold || r >= g.BlockedThreshold)
                {
                    if (duvody != null) duvody[o >= g.BlockedThreshold ? 0 : 1]++;
                    return s;
                }
                // ⚠️ PUDORYS ROBOTU se bere jako sjizdny - presne to dela LocalPathPlanner
                // (LocalPlannerConfig.FootprintRadiusM, 3. 9. 2026): pod robotem je sjizdno,
                // protoze tam stoji. Bez toho merilo meridlo artefakt a vychazelo, ze robot leze
                // ve 40 % casu kvuli bunce, kterou planovac ve skutecnosti preskakuje.
                if (s < Pudorys) continue;
                bool ok = geometrieStaci ? o <= g.FreeThreshold
                                         : (o <= g.FreeThreshold && r <= g.FreeThreshold);
                if (!ok)
                {
                    if (duvody != null)
                    {
                        bool geomOk = o <= g.FreeThreshold, semOk = r <= g.FreeThreshold;
                        duvody[geomOk && !semOk ? 3 : (!geomOk && semOk ? 2 : 4)]++;
                    }
                    return s;
                }
            }
            if (duvody != null) duvody[5]++;
            return RangeM;
        }

        private static void Profil(long[] free, long[] blocked,
                                   long[] chybiSem, long[] chybiGeom, long[] chybiOboji)
        {
            Console.WriteLine("--- Bunky podle azimutu v telesovem ramci (0 = smer jizdy, + vlevo) ---");
            Console.WriteLine("  azimut       bunek      Free   Blocked   chybi_SEM  chybi_GEOM  chybi_OBOJI");
            for (int b = 0; b < 2 * Bins; b++)
            {
                long n = free[b] + blocked[b] + chybiSem[b] + chybiGeom[b] + chybiOboji[b];
                if (n == 0) continue;
                double od = (b - Bins) * BinDeg;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4:F0}..{1,4:F0}  {2,10}  {3,6:F1} %  {4,6:F1} %  {5,8:F1} %  {6,8:F1} %  {7,9:F1} %",
                    od, od + BinDeg, n,
                    100.0 * free[b] / n, 100.0 * blocked[b] / n,
                    100.0 * chybiSem[b] / n, 100.0 * chybiGeom[b] / n, 100.0 * chybiOboji[b] / n));
            }
            Console.WriteLine();

            double PodilSem(int b)
            {
                long n = free[b] + blocked[b] + chybiSem[b] + chybiGeom[b] + chybiOboji[b];
                return n == 0 ? double.NaN : 100.0 * chybiSem[b] / n;
            }
            double uStredu = 0.5 * (PodilSem(Bins) + PodilSem(Bins - 1));
            var boky = new List<double>();
            for (int b = 0; b < 2 * Bins; b++)
            {
                double az = Math.Abs((b - Bins) * BinDeg + BinDeg / 2);
                if (az >= 20 && az <= 50) { double p = PodilSem(b); if (!double.IsNaN(p)) boky.Add(p); }
            }
            double naBocich = boky.Count > 0 ? boky.Average() : double.NaN;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "\"chybi jen semantika\": ve smeru jizdy (+-5 st.) {0:F1} %, po stranach (20-50 st.) {1:F1} %",
                uStredu, naBocich));
            Console.WriteLine(uStredu > naBocich * 1.5
                ? "  => PROFIL SEDI NA KLIN: pred robotem chybi barva vyrazne vic nez po stranach."
                : "  => profil klin NEUKAZUJE - diry v semantice nejsou soustredene do smeru jizdy.");
            Console.WriteLine();
        }

        /// <summary>
        /// Grid ze zpravy zpatky do <see cref="OccupancyGrid"/>, aby se na nej dal pustit
        /// <see cref="WedgeFiller"/> - tedy PRAVE TEN kod, ktery pobezi na robotu (a ne jeho
        /// replika, ktera se muze rozejit).
        /// </summary>
        private static OccupancyGrid Rekonstruuj(OccupancyGridMsg m)
        {
            var g = new OccupancyGrid(new OccupancyGridConfig
            {
                Size = m.Size,
                Resolution = m.Resolution,
                Scale = m.Scale,
                BlockedThreshold = m.BlockedThreshold,
                FreeThreshold = m.FreeThreshold,
            });
            g.MoveOrigin(m.OriginX, m.OriginY);
            for (int j = 0; j < m.Size; j++)
            {
                int src = j * m.Size;
                for (int i = 0; i < m.Size; i++)
                {
                    int dst = g.LocalIndex(i, j);
                    g.Occ[dst] = m.Occ[src + i];
                    g.Road[dst] = m.Road != null ? m.Road[src + i] : (sbyte)0;
                }
            }
            return g;
        }

        /// <summary>Totez co <see cref="FreeAhead"/>, jen nad zivym gridem (po doplneni klinu).</summary>
        private static double FreeAheadGrid(OccupancyGrid g, OccupancyGridMsg m,
                                            double rx, double ry, double theta, long[] duvody = null)
        {
            double cosH = Math.Cos(theta), sinH = Math.Sin(theta);
            double krok = m.Resolution * 0.5;
            for (double s = 0; s <= RangeM; s += krok)
            {
                int cx = g.CellX(rx + s * cosH), cy = g.CellY(ry + s * sinH);
                if (!g.Contains(cx, cy)) { if (duvody != null) duvody[5]++; return s; }
                var stav = g.State(cx, cy);
                if (stav == CellState.Free) continue;
                if (stav != CellState.Blocked && s < Pudorys) continue;   // pudorys robotu

                if (duvody != null)
                {
                    float o = g.LogOddsOcc(cx, cy), r = g.LogOddsRoad(cx, cy);
                    if (o >= g.Config.BlockedThreshold) duvody[0]++;
                    else if (r >= g.Config.BlockedThreshold) duvody[1]++;
                    else
                    {
                        bool geomOk = o <= g.Config.FreeThreshold, semOk = r <= g.Config.FreeThreshold;
                        duvody[geomOk && !semOk ? 3 : (!geomOk && semOk ? 2 : 4)]++;
                    }
                }
                return s;
            }
            if (duvody != null) duvody[5]++;
            return RangeM;
        }

        /// <summary>
        /// <b>Kolik casu robot leze</b> - podil vzorku pod danou rychlosti, pred lecbou a po ni.
        /// Prumer VBrake tuhle otazku nezodpovi: rozdil se v nem rozpusti, protoze vetsinu casu je
        /// VBrake stejne na stropu. V terenu je ale videt prave tohle - jak casto robot leze.
        /// </summary>
        private static void Plazeni(LocalPlannerConfig cfg, Stats dnes, Stats poDoplneni, Stats geom)
        {
            Console.WriteLine("--- Jak casto by robot lezl (podil vzorku pod danou rychlosti) ---");
            Console.WriteLine("  prah      dnes   s doplnenim   (strop: bez semantiky)");
            foreach (double prah in new[] { 0.2, 0.4, 0.6, 0.8, 1.0, cfg.MaxSpeed })
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  < {0:F2} m/s  {1,5:F1} %      {2,5:F1} %        {3,5:F1} %",
                    prah, 100.0 * Pod(dnes, prah), 100.0 * Pod(poDoplneni, prah),
                    100.0 * Pod(geom, prah)));
            Console.WriteLine();
        }

        /// <summary>Podil vzorku pod prahem (0..1). Stats nedava histogram, tak se hleda pres percentily.</summary>
        private static double Pod(Stats s, double prah)
        {
            if (s.Count == 0) return 0;
            double lo = 0, hi = 100;
            for (int k = 0; k < 40; k++)
            {
                double mid = 0.5 * (lo + hi);
                if (s.Percentile(mid) < prah) lo = mid; else hi = mid;
            }
            return 0.5 * (lo + hi) / 100.0;
        }

        private static void Doplneni(double wedgeDeg, double wedgeConf, Stats doplneno,
                                     Stats kandidatu, Stats bezSouseda,
                                     Stats fFill, Stats vFill, Stats fDnes, Stats vDnes)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "--- Simulace lecby: WedgeFiller nad timze gridem (wedgefill={0:F1} st., duvera {1:F2}) ---",
                wedgeDeg, wedgeConf));
            Console.WriteLine("  " + kandidatu.Line(""));
            Console.WriteLine("  " + bezSouseda.Line(""));
            Console.WriteLine("  " + doplneno.Line(""));
            Console.WriteLine("  " + fFill.Line("m"));
            Console.WriteLine("  " + vFill.Line("m/s"));
            if (vFill.Count > 0 && vDnes.Count > 0)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  freeAhead p50 {0:F2} -> {1:F2} m,  prumer VBrake {2:F3} -> {3:F3} m/s ({4:+0.0;-0.0} %)",
                    fDnes.Median, fFill.Median, vDnes.Mean, vFill.Mean,
                    vDnes.Mean > 0 ? 100.0 * (vFill.Mean - vDnes.Mean) / vDnes.Mean : 0));
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Kdyz se paprsek zastavil na bunce, ktere chybi JEN semantika, zmeri <b>pricnou sirku</b>
        /// te diry (doleva + doprava po prvni bunku se semantikou, nejvys 2 m) a jeji vzdalenost.
        /// Uzka dira = klin mezi kamerami; siroka = neco jineho.
        /// </summary>
        private static void ZmerDiru(OccupancyGridMsg g, double rx, double ry, double theta,
                                     double vzdalenost, Stats sirka, Stats kde)
        {
            if (vzdalenost <= 0 || vzdalenost >= RangeM) return;
            double cosH = Math.Cos(theta), sinH = Math.Sin(theta);
            double wx = rx + vzdalenost * cosH, wy = ry + vzdalenost * sinH;
            int i0 = (int)Math.Floor(wx / g.Resolution) - g.OriginX;
            int j0 = (int)Math.Floor(wy / g.Resolution) - g.OriginY;
            if (i0 < 0 || j0 < 0 || i0 >= g.Size || j0 >= g.Size || g.Road == null) return;

            int idx0 = i0 + j0 * g.Size;
            float o0 = g.Occ[idx0] * g.Scale, r0 = g.Road[idx0] * g.Scale;
            bool jenSemantika = o0 <= g.FreeThreshold && r0 > g.FreeThreshold
                                && o0 < g.BlockedThreshold && r0 < g.BlockedThreshold;
            if (!jenSemantika) return;

            double Strana(int smer)
            {
                int max = (int)(2.0 / g.Resolution);
                for (int k = 1; k <= max; k++)
                {
                    double x = wx + smer * (-sinH) * k * g.Resolution;
                    double y = wy + smer * (cosH) * k * g.Resolution;
                    int i = (int)Math.Floor(x / g.Resolution) - g.OriginX;
                    int j = (int)Math.Floor(y / g.Resolution) - g.OriginY;
                    if (i < 0 || j < 0 || i >= g.Size || j >= g.Size) return k * g.Resolution;
                    if (g.Road[i + j * g.Size] != 0) return k * g.Resolution;
                }
                return 2.0;
            }

            sirka.Add(Strana(+1) + Strana(-1));
            kde.Add(vzdalenost);
        }

        /// <summary>
        /// <b>Proc se nedoplnila prave ta bunka, na ktere se paprsek zastavil.</b> Krok za krokem
        /// stejnymi podminkami jako <see cref="WedgeFiller"/> - bez toho se „doplnilo se 89 bunek,
        /// ale paprsek stoji dal" neda vylozit.
        /// </summary>
        private static void ProcNedoplneno(OccupancyGridMsg m, OccupancyGrid po,
                                           double rx, double ry, double theta, double vzdalenost,
                                           double wedgeDeg, long[] procNe)
        {
            if (vzdalenost <= 0 || vzdalenost >= RangeM || m.Road == null) return;
            double cosH = Math.Cos(theta), sinH = Math.Sin(theta);
            double wx = rx + vzdalenost * cosH, wy = ry + vzdalenost * sinH;
            int i0 = (int)Math.Floor(wx / m.Resolution) - m.OriginX;
            int j0 = (int)Math.Floor(wy / m.Resolution) - m.OriginY;
            if (i0 < 0 || j0 < 0 || i0 >= m.Size || j0 >= m.Size) return;

            int idx0 = i0 + j0 * m.Size;
            float o0 = m.Occ[idx0] * m.Scale, r0 = m.Road[idx0] * m.Scale;
            bool jenSemantika = o0 <= m.FreeThreshold && r0 > m.FreeThreshold
                                && o0 < m.BlockedThreshold && r0 < m.BlockedThreshold;
            if (!jenSemantika) return;

            // Stejne podminky jako ve WedgeFiller.Fill.
            double dx = wx - rx, dy = wy - ry;
            double fwd = dx * cosH + dy * sinH, side = -dx * sinH + dy * cosH;
            if (fwd < new WedgeFiller(po, 1).MinRangeM) { procNe[1]++; return; }
            if (Math.Abs(side) > fwd * Math.Tan(wedgeDeg * Math.PI / 180.0 / 2.0)) { procNe[0]++; return; }
            if (o0 > m.FreeThreshold) { procNe[2]++; return; }

            // Vysledek po doplneni cteme z gridu, na ktery WedgeFiller opravdu sahal.
            int cx = po.CellX(wx), cy = po.CellY(wy);
            float rPo = po.LogOddsRoad(cx, cy);
            if (rPo == r0) { procNe[3]++; return; }                    // nic se nezapsalo
            procNe[po.State(cx, cy) == CellState.Free ? 5 : 4]++;
        }

        private static void ProcNe(long[] d)
        {
            long celkem = 0;
            foreach (long x in d) celkem += x;
            if (celkem == 0) return;
            string[] popis =
            {
                "mimo klin (azimut)",
                "blize nez MinRangeM",
                "geometrie nepotvrzena",
                "nezapsalo se nic (bez pricneho souseda)",
                "zapsalo se, ale NESTACILO na prah",
                "zapsalo se a bunka je Free",
            };
            Console.WriteLine("--- Proc se NEDOPLNILA bunka, na ktere se paprsek zastavil ---");
            for (int k = 0; k < d.Length; k++)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-42} {1,6} ({2,5:F1} %)", popis[k], d[k], 100.0 * d[k] / celkem));
            Console.WriteLine();
        }

        /// <summary>
        /// Sila vzorku semantiky v miste, kde se paprsek zastavil, a v jeho pricnych sousedech.
        /// Odpovida na otazku, proc doplneni z okoli nepomaha: kdyz jsou sousede jen <b>slabi</b>
        /// (maji vzorek, ale nerozhoduji), nemuze z nich interpolace bunku rozhodnout nikdy.
        /// </summary>
        private static void ZmerVzorky(OccupancyGridMsg g, double rx, double ry, double theta,
                                       double vzdalenost, Stats bunka, Stats vlevo, Stats vpravo,
                                       Stats dVlevo, Stats dVpravo,
                                       ref int rozhodnuti, ref int slabi, ref int zadny)
        {
            if (vzdalenost <= 0 || vzdalenost >= RangeM || g.Road == null) return;
            double cosH = Math.Cos(theta), sinH = Math.Sin(theta);
            double wx = rx + vzdalenost * cosH, wy = ry + vzdalenost * sinH;
            int i0 = (int)Math.Floor(wx / g.Resolution) - g.OriginX;
            int j0 = (int)Math.Floor(wy / g.Resolution) - g.OriginY;
            if (i0 < 0 || j0 < 0 || i0 >= g.Size || j0 >= g.Size) return;

            int idx0 = i0 + j0 * g.Size;
            float o0 = g.Occ[idx0] * g.Scale, r0 = g.Road[idx0] * g.Scale;
            bool jenSemantika = o0 <= g.FreeThreshold && r0 > g.FreeThreshold
                                && o0 < g.BlockedThreshold && r0 < g.BlockedThreshold;
            if (!jenSemantika) return;
            bunka.Add(r0);

            // Nejblizsi soused s JAKYMKOLI vzorkem (nenulovym) do 1 m.
            bool Soused(int smer, out float hodnota, out double kde)
            {
                hodnota = 0; kde = double.NaN;
                int max = (int)(1.0 / g.Resolution);
                for (int k = 1; k <= max; k++)
                {
                    double x = wx + smer * (-sinH) * k * g.Resolution;
                    double y = wy + smer * (cosH) * k * g.Resolution;
                    int i = (int)Math.Floor(x / g.Resolution) - g.OriginX;
                    int j = (int)Math.Floor(y / g.Resolution) - g.OriginY;
                    if (i < 0 || j < 0 || i >= g.Size || j >= g.Size) return false;
                    float r = g.Road[i + j * g.Size] * g.Scale;
                    if (r != 0f) { hodnota = r; kde = k * g.Resolution; return true; }
                }
                return false;
            }

            bool jeL = Soused(+1, out float rl, out double dl);
            bool jeP = Soused(-1, out float rp, out double dp);
            if (jeL) { vlevo.Add(rl); dVlevo.Add(dl); }
            if (jeP) { vpravo.Add(rp); dVpravo.Add(dp); }

            if (!jeL || !jeP) zadny++;
            else if (rl <= g.FreeThreshold && rp <= g.FreeThreshold) rozhodnuti++;
            else slabi++;
        }

        /// <summary>
        /// <b>Na cem se paprsek vpred zastavil.</b> Tohle je cislo, ktere rozhoduje o lecbe: klin
        /// stoji za zpomalenim jen do te miry, do jake se na nem paprsek opravdu zastavuje. Kdyz
        /// vyhrava „chybi geometrie" nebo „chybi oboji", je doplnovani semantiky mimo.
        /// </summary>
        private static void Duvody(long[] d, string popisek = "")
        {
            long celkem = 0;
            foreach (long x in d) celkem += x;
            if (celkem == 0) return;

            string[] popis =
            {
                "prekazka z GEOMETRIE (hloubka)",
                "prekazka ze SEMANTIKY (barva)",
                "chybi GEOMETRIE (semantika je)",
                "chybi SEMANTIKA (geometrie je)  <- klin mezi kamerami",
                "chybi OBOJI",
                "az na dosah / okraj gridu",
            };
            Console.WriteLine("--- Na cem se zastavil paprsek vpred (co opravdu srazi VBrake)"
                              + popisek + " ---");
            for (int k = 0; k < d.Length; k++)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-34} {1,6} ({2,5:F1} %)", popis[k], d[k], 100.0 * d[k] / celkem));
            Console.WriteLine();
        }

        private static void Rychlost(LocalPlannerConfig cfg, Stats fDnes, Stats fGeom,
                                     Stats vDnes, Stats vGeom)
        {
            Console.WriteLine("--- Dopad na dopREDNOU rychlost (paprsek primo vpred) ---");
            Console.WriteLine("  " + fDnes.Line("m"));
            Console.WriteLine("  " + fGeom.Line("m"));
            Console.WriteLine("  " + vDnes.Line("m/s"));
            Console.WriteLine("  " + vGeom.Line("m/s"));
            if (vDnes.Count > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  median VBrake by se zmenil z {0:F2} na {1:F2} m/s (strop MaxSpeed {2:F2})",
                    vDnes.Median, vGeom.Median, cfg.MaxSpeed));
            Console.WriteLine();
        }
    }
}
