using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Chodí z kamer opravdu nové snímky?</b> Pro každou kameru spočítá, kolik snímků přišlo, jak
    /// rychle za sebou — a hlavně <b>kolik z nich je různých obrazů</b> (otisk pixelů) a jak dlouhá
    /// je nejdelší série po sobě jdoucích <b>totožných</b> snímků.
    ///
    /// <para><b>Nač to je:</b> kamera, která hlásí <c>OK</c> a přitom posílá pořád tentýž obraz, je
    /// ta nejhorší porucha — zvenčí vypadá všechno v pořádku (snímky chodí, stáří je nízké, panel
    /// svítí zeleně), ale robot se řídí podle nehybné fotky. Ani počet snímků, ani jejich stáří to
    /// neodhalí; musí se porovnat <b>obsah</b>. Vzniklo 6. 9. 2026 z pozorování autora, že z pravé
    /// kamery „chodí pořád stejný snímek".</para>
    ///
    /// <para><b>Porovnává se otisk, ne obraz.</b> Držet předchozí snímek by znamenalo držet
    /// megabajty; FNV-1a přes bajty obrazu stačí — hledá se shoda, ne podobnost. Počítá se zvlášť
    /// pro <b>barvu</b> a pro <b>hloubku</b>, protože se můžou zaseknout nezávisle (u D435 jsou to
    /// dva streamy) a rozlišit to je půlka diagnózy.</para>
    ///
    /// <para><b>Čte celé snímky</b> (obrazy), takže na gigabajtovém záznamu to trvá — proto
    /// <c>--limit</c> (výchozí 400 snímků celkem, 0 = vše).</para>
    /// </summary>
    public static class CameraFramesReport
    {
        /// <param name="skip">Kolik snimku na zacatku preskocit — s <paramref name="limit"/> se tim
        /// da podivat i na KONEC dlouheho zaznamu ("zamrzlo to hned, nebo az za pul hodiny?").</param>
        /// <param name="png">Prefix cesty pro ulozeni prvniho snimku kazde kamery jako PNG;
        /// <c>null</c> = neukladat.</param>
        public static void Run(RecordFile rec, int limit, int skip = 0, string png = null)
        {
            var entries = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            int celkem = entries.Count;
            if (skip > 0) entries = entries.Skip(skip).ToList();
            if (limit > 0) entries = entries.Take(limit).ToList();

            Console.WriteLine($"CameraFrame v indexu: {celkem}"
                              + (entries.Count < celkem
                                 ? $" (cte se {entries.Count} od poradi {skip})" : string.Empty));
            if (entries.Count == 0) return;

            var kamery = new Dictionary<string, Kamera>(StringComparer.Ordinal);
            int precteno = 0;

            foreach (var e in entries)
            {
                if (!(rec.Read(e) is CameraFrame f)) continue;
                precteno++;

                string jmeno = f.Name ?? "(bez jmena)";
                if (!kamery.TryGetValue(jmeno, out var k)) kamery[jmeno] = k = new Kamera(jmeno);
                k.Pridej(f);
            }

            Console.WriteLine($"precteno {precteno} snimku ze {kamery.Count} kamer");
            Console.WriteLine();

            foreach (var k in kamery.Values.OrderBy(x => x.Jmeno, StringComparer.Ordinal))
            {
                k.Vypis();
                if (!string.IsNullOrWhiteSpace(png)) k.UlozPng(png);
            }
        }

        /// <summary>
        /// <b>Časová osa výpadků kamer přes celý záznam</b> (<c>cameras --vypadky</c>). Pro každou
        /// kameru vypíše epizody, kdy (a) nepřišel žádný snímek déle než <paramref name="gapSec"/>,
        /// nebo (b) barva / hloubka / razítko barvy z driveru stály (totožný obsah) déle než
        /// <paramref name="freezeSec"/>. Čas se tiskne i jako odstup od prvního snímku záznamu, aby
        /// šel přímo dohledat ve videu z jízdy. Vzniklo 26. 9. 2026 pro popis videa z Robotouru.
        ///
        /// <para>Čte <b>všechny</b> snímky — nad desítkami GB to trvá minuty.</para>
        /// </summary>
        public static void RunEpisodes(RecordFile rec, double gapSec, double freezeSec)
        {
            var entries = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            Console.WriteLine($"CameraFrame v indexu: {entries.Count}; mezera > {gapSec:F1} s, zamrznuti > {freezeSec:F1} s");
            if (entries.Count == 0) return;

            var kamery = new Dictionary<string, Osa>(StringComparer.Ordinal);
            DateTime? zacatek = null;
            foreach (var e in entries)
            {
                if (!(rec.Read(e) is CameraFrame f)) continue;
                zacatek ??= f.TimeStamp;
                string jmeno = f.Name ?? "(bez jmena)";
                if (!kamery.TryGetValue(jmeno, out var k)) kamery[jmeno] = k = new Osa(jmeno);
                k.Pridej(f);
            }

            foreach (var k in kamery.Values.OrderBy(x => x.Jmeno, StringComparer.Ordinal))
                k.Vypis(zacatek.Value, gapSec, freezeSec);
        }

        /// <summary>Epizody výpadků jedné kamery (mezery ve snímcích a stojící obsah).</summary>
        private sealed class Osa
        {
            public readonly string Jmeno;
            private readonly List<(DateTime Od, DateTime Do, string Druh)> epizody = new();
            private DateTime prvni, posledni;
            private int snimku;
            private readonly Serie barva = new("barva stoji");
            private readonly Serie hloubka = new("hloubka stoji");
            private readonly Serie razitko = new("razitko barvy z driveru stoji");
            private readonly Serie cesta = new("cesta z RGB stoji");

            public Osa(string jmeno) { Jmeno = jmeno; }

            public void Pridej(CameraFrame f)
            {
                if (snimku == 0) prvni = f.TimeStamp;
                else if ((f.TimeStamp - posledni).TotalSeconds > 0)
                    epizody.Add((posledni, f.TimeStamp, "mezera bez snimku"));
                posledni = f.TimeStamp;
                snimku++;

                barva.Pridej(Kamera.Otisk(f.ImageRGB?.Data), f.TimeStamp, epizody);
                hloubka.Pridej(Kamera.Otisk(f.ImageDepth?.Data), f.TimeStamp, epizody);
                razitko.Pridej((ulong)f.RGBTimeStamp.Ticks, f.TimeStamp, epizody);
                cesta.Pridej(Kamera.Otisk(f.ImageProbability?.Data), f.TimeStamp, epizody);
            }

            public void Vypis(DateTime zacatek, double gapSec, double freezeSec)
            {
                barva.Uzavri(epizody); hloubka.Uzavri(epizody); razitko.Uzavri(epizody); cesta.Uzavri(epizody);

                var vybrane = epizody
                    .Where(x => (x.Do - x.Od).TotalSeconds > (x.Druh.StartsWith("mezera") ? gapSec : freezeSec))
                    .OrderBy(x => x.Od).ToList();

                double sekund = (posledni - prvni).TotalSeconds;
                Console.WriteLine();
                Console.WriteLine($"=== {Jmeno}: {snimku} snimku za {sekund:F0} s ({(sekund > 0 ? snimku / sekund : 0):F1} Hz), epizod {vybrane.Count}");
                Console.WriteLine("  od            do            trvani   od startu  druh");
                foreach (var x in vybrane)
                {
                    var t = x.Od - zacatek;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0:HH:mm:ss.f}  {1:HH:mm:ss.f}  {2,6:F1} s  {3,3}:{4:00}     {5}",
                        x.Od, x.Do, (x.Do - x.Od).TotalSeconds, (int)t.TotalMinutes, t.Seconds, x.Druh));
                }
                foreach (var g in vybrane.GroupBy(x => x.Druh))
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  souhrn {0}: {1}x, celkem {2:F1} s", g.Key, g.Count(), g.Sum(x => (x.Do - x.Od).TotalSeconds)));
            }
        }

        /// <summary>Série po sobě jdoucích snímků se shodným otiskem → epizoda „stojí".</summary>
        private sealed class Serie
        {
            private readonly string druh;
            private ulong? predchozi;
            private DateTime od, naposledy;
            private int delka;

            public Serie(string druh) { this.druh = druh; }

            public void Pridej(ulong? otisk, DateTime t, List<(DateTime, DateTime, string)> ven)
            {
                if (otisk == null) return;
                if (predchozi == otisk) { delka++; naposledy = t; return; }
                Uzavri(ven);
                predchozi = otisk; od = t; naposledy = t; delka = 1;
            }

            public void Uzavri(List<(DateTime, DateTime, string)> ven)
            {
                if (delka > 1) ven.Add((od, naposledy, druh));
                delka = 0;
            }
        }

        /// <summary>Statistika jedné kamery.</summary>
        private sealed class Kamera
        {
            public readonly string Jmeno;

            private readonly Stopa barva = new Stopa("barva (RGB)");
            private readonly Stopa hloubka = new Stopa("hloubka");
            private readonly Stopa cesta = new Stopa("cesta z RGB");

            private int snimku;
            private DateTime prvni, posledni;
            private DateTime predchoziCas;
            private readonly List<double> rozestupy = new List<double>();
            private readonly HashSet<DateTime> razitkaRGB = new HashSet<DateTime>();
            private readonly HashSet<DateTime> razitkaHloubky = new HashSet<DateTime>();

            public Kamera(string jmeno) { Jmeno = jmeno; }

            /// <summary>Prvni snimek kamery — na pozadani se ulozi jako PNG (viz <c>--png=</c>).</summary>
            private CameraFrame prvniSnimek;

            public void Pridej(CameraFrame f)
            {
                prvniSnimek ??= f;
                if (snimku == 0) prvni = f.TimeStamp;
                posledni = f.TimeStamp;
                if (snimku > 0) rozestupy.Add((f.TimeStamp - predchoziCas).TotalMilliseconds);
                predchoziCas = f.TimeStamp;
                snimku++;

                barva.Pridej(Otisk(f.ImageRGB?.Data));
                hloubka.Pridej(Otisk(f.ImageDepth?.Data));
                cesta.Pridej(Otisk(f.ImageProbability?.Data));

                // Razitka JEDNOTLIVYCH streamu z driveru (CameraFrame verze 6). Rozhoduji o tom,
                // KDE je vada: kdyz se razitko barvy nehybe, nedodava snimky librealsense (nebo
                // senzor); kdyby se hybalo a pixely ne, byla by chyba v nasem kopirovani.
                razitkaRGB.Add(f.RGBTimeStamp);
                razitkaHloubky.Add(f.DepthTimeStamp);
            }

            /// <summary>
            /// Ulozi prvni snimek kamery jako PNG. Zamrzly obraz je potreba VIDET: jinak nejde
            /// rozliseit „stream se nikdy nerozjel" (cerno, sum) od „jeden skutecny snimek a pak uz
            /// nic". Jmeno souboru nese jmeno kamery, aby sly kamery porovnat vedle sebe.
            /// </summary>
            public void UlozPng(string prefix)
            {
                var img = prvniSnimek?.ImageRGB;
                if (img == null) { Console.WriteLine($"  {Jmeno}: barva ve snimku neni, PNG se neuklada"); return; }

                string cesta = $"{prefix}-{Jmeno.Replace(' ', '_')}.png";
                try
                {
                    System.IO.File.WriteAllBytes(cesta, ARBot.Common.Logs.ImageMsg.EncodePng(img));
                    Console.WriteLine($"  ulozeno {cesta}");
                }
                catch (Exception ex) { Console.WriteLine($"  PNG {cesta} se neulozilo: {ex.Message}"); }
            }

            public void Vypis()
            {
                double sekund = (posledni - prvni).TotalSeconds;
                Console.WriteLine($"=== {Jmeno}");
                Console.WriteLine($"  snimku: {snimku} za {sekund:F1} s"
                                  + (sekund > 0 ? $" ({snimku / sekund:F1} Hz)" : string.Empty));

                if (rozestupy.Count > 0)
                {
                    var s = rozestupy.OrderBy(x => x).ToList();
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  rozestup [ms]: p50 {0:F0}, p90 {1:F0}, max {2:F0}",
                        s[s.Count / 2], s[(int)(s.Count * 0.9)], s[s.Count - 1]));
                }

                barva.Vypis(snimku);
                hloubka.Vypis(snimku);
                cesta.Vypis(snimku);
                Console.WriteLine($"  razitka       : barva {razitkaRGB.Count} ruznych, "
                                  + $"hloubka {razitkaHloubky.Count} ruznych (z {snimku})"
                                  + (razitkaRGB.Count == 1 && snimku > 1
                                     ? "  <-- razitko barvy stoji: nechodi z driveru, neni to nase kopie"
                                     : string.Empty));
                Console.WriteLine();
            }

            /// <summary>
            /// FNV-1a přes bajty obrazu. <c>null</c> = vrstva ve snímku není (pak se nehodnotí).
            /// Otisk stačí: hledá se <b>shoda</b>, ne podobnost, a držet předchozí obraz by znamenalo
            /// držet megabajty.
            /// </summary>
            internal static ulong? Otisk(byte[] data)
            {
                if (data == null || data.Length == 0) return null;

                ulong h = 14695981039346656037UL;
                for (int i = 0; i < data.Length; i++)
                {
                    h ^= data[i];
                    h *= 1099511628211UL;
                }
                return h;
            }
        }

        /// <summary>Kolik různých obrazů a jak dlouhá nejdelší série totožných po sobě.</summary>
        private sealed class Stopa
        {
            private readonly string popis;
            private readonly HashSet<ulong> ruzne = new HashSet<ulong>();
            private ulong? predchozi;
            private int mam, serie, nejdelsiSerie;

            public Stopa(string popis) { this.popis = popis; }

            public void Pridej(ulong? otisk)
            {
                if (otisk == null) return;
                mam++;
                ruzne.Add(otisk.Value);

                if (predchozi.HasValue && predchozi.Value == otisk.Value) serie++;
                else serie = 1;
                if (serie > nejdelsiSerie) nejdelsiSerie = serie;
                predchozi = otisk;
            }

            public void Vypis(int snimku)
            {
                if (mam == 0)
                {
                    Console.WriteLine($"  {popis,-14}: neni v zaznamu");
                    return;
                }

                string verdikt;
                if (ruzne.Count == 1 && mam > 1)
                    // Otisk se tiskne schvalne: dva behy nad ruznymi useky zaznamu se pak daji
                    // porovnat, tedy poznat, jestli je to porad TENTYZ obraz, nebo jiny zamrzly.
                    verdikt = $"  <-- VSECHNY SNIMKY JSOU TOTOZNE (otisk {predchozi:x16})";
                else if (nejdelsiSerie > 2)
                    verdikt = $"  <-- nejdelsi serie totoznych: {nejdelsiSerie}";
                else
                    verdikt = string.Empty;

                Console.WriteLine($"  {popis,-14}: {ruzne.Count} ruznych obrazu z {mam}{verdikt}");
            }
        }
    }
}
