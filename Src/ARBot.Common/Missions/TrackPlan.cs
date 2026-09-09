using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// <b>Seznam mist ze souboru <c>*.track</c></b> — vstup mise <see cref="TrackMission"/>.
    /// Viz doc/track-mission.md.
    ///
    /// <para><b>Format</b> je zamerne co nejhlouposti: jeden bod na radek jako
    /// <c>sirka,delka</c> ve <b>STUPNICH</b>, a volitelne posledni radek <c>repeat</c>, ktery
    /// znamena „po poslednim bode zacni znovu od prvniho". Prazdne radky a radky zacinajici
    /// <c>#</c> se preskakuji.</para>
    ///
    /// <para><b>⚠️ Stupne jsou tady zamer, ne nedbalost.</b> Projekt drzi zemepisne souradnice
    /// VSUDE v radianech (viz CLAUDE.md) a prevod patri jen na <b>okraje</b> — a tenhle soubor
    /// okraj je: pise ho clovek, ktery si souradnice zkopiroval z mapy. Prevod se proto dela
    /// hned pri cteni a dal uz se nese <see cref="LLA"/> v radianech.</para>
    ///
    /// <para><b>⚠️ Nesrozumitelny radek je CHYBA, ne tiche preskoceni.</b> Tatáz zasada jako
    /// u konfiguracnich profilu (doc/configuration.md): kdyby se vadny radek preskocil, robot by
    /// objel <b>jinou</b> trasu, nez clovek zadal, a nikdo by to nepoznal — trasa se od zadani
    /// lisi jen tim, co v ni NENI.</para>
    /// </summary>
    public sealed class TrackPlan
    {
        /// <summary>Klicove slovo, ktere znamena „zacni znovu od prvniho bodu".</summary>
        public const string RepeatKeyword = "repeat";

        private readonly List<LLA> points;

        private TrackPlan(List<LLA> points, bool repeat, IReadOnlyList<string> sourceLines)
        {
            this.points = points;
            Repeat = repeat;
            SourceLines = sourceLines;
        }

        /// <summary>Mista v poradi, jak se maji objet; <b>v RADIANECH</b>.</summary>
        public IReadOnlyList<LLA> Points => points;

        /// <summary>Ma se po poslednim bode pokracovat znovu od prvniho?</summary>
        public bool Repeat { get; }

        /// <summary>
        /// Radky souboru tak, jak byly nacteny (bez komentaru a prazdnych) — jdou do zpravy
        /// a do logu, aby slo ze zaznamu overit, <b>co robot vlastne dostal zadano</b>.
        /// </summary>
        public IReadOnlyList<string> SourceLines { get; }

        /// <summary>Kolik mist seznam obsahuje.</summary>
        public int Count => points.Count;

        /// <summary>
        /// Nacte seznam ze souboru. Vyhodi <see cref="FileNotFoundException"/>, kdyz soubor neni,
        /// a <see cref="FormatException"/> pri nesrozumitelnem obsahu.
        /// </summary>
        public static TrackPlan Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Cesta k souboru .track je prazdna.", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException($"Soubor se seznamem mist neexistuje: {path}", path);

            return Parse(File.ReadAllLines(path), path);
        }

        /// <summary>
        /// Rozebere radky (testovatelne bez souboru). <paramref name="source"/> se objevi jen
        /// v hlaskach chyb, aby clovek vedel, KTERY soubor je vadny.
        /// </summary>
        public static TrackPlan Parse(IEnumerable<string> lines, string source = "(bez jmena)")
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));

            var points = new List<LLA>();
            var kept = new List<string>();
            bool repeat = false;
            int lineNumber = 0;

            foreach (string raw in lines)
            {
                lineNumber++;
                string line = (raw ?? string.Empty).Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                // ⚠️ `repeat` MUSI byt posledni. Kdyby smel byt uprostred, zbytek souboru by byl
                // NEDOSAZITELNY a soubor by tvrdil neco jineho, nez robot dela - presne ten druh
                // rozdilu, ktery se v poli nehleda, protoze na nej nic neupozorni.
                if (repeat)
                    throw new FormatException(
                        $"{source}, radek {lineNumber}: za '{RepeatKeyword}' uz nesmi nic byt "
                        + $"('{line}'). Slovo '{RepeatKeyword}' znamena konec seznamu a skok na "
                        + "prvni bod, takze radky za nim by se nikdy neobjely.");

                if (string.Equals(line, RepeatKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    repeat = true;
                    kept.Add(RepeatKeyword);
                    continue;
                }

                points.Add(ParsePoint(line, source, lineNumber));
                kept.Add(line);
            }

            if (points.Count == 0)
                throw new FormatException(
                    $"{source}: seznam neobsahuje ani jedno misto. Ocekava se aspon jeden radek "
                    + "'sirka,delka' ve stupnich.");
            if (repeat && points.Count < 2)
                throw new FormatException(
                    $"{source}: '{RepeatKeyword}' s jedinym bodem nema smysl - robot by pak "
                    + "dojel na to jedine misto a hlasil dojezd porad znovu.");

            return new TrackPlan(points, repeat, kept);
        }

        /// <summary>Jeden radek <c>sirka,delka</c> ve stupnich → <see cref="LLA"/> v radianech.</summary>
        private static LLA ParsePoint(string line, string source, int lineNumber)
        {
            // Strednik i mezera se toleruji: soubor pise clovek a kopiruje ho z mapy, kde je
            // oddelovac podle nastroje jiny. Desetinny oddelovac je ale VZDY tecka - carka je
            // oddelovac slozek, takze "50,03;14,52" by bylo dvojznacne.
            string[] parts = line.Split(new[] { ',', ';', ' ', '\t' },
                                        StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new FormatException(
                    $"{source}, radek {lineNumber}: necekany format '{line}'. Ocekava se "
                    + $"'sirka,delka' ve stupnich, nebo slovo '{RepeatKeyword}'.");

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture,
                                 out double latDeg)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out double lonDeg))
                throw new FormatException(
                    $"{source}, radek {lineNumber}: '{line}' nejsou dve desetinna cisla "
                    + "(desetinny oddelovac je TECKA, carka oddeluje sirku od delky).");

            // Rozsah se kontroluje, protoze zamena sirky a delky je nejcastejsi omyl a v Cechach
            // se NEPOZNA podle padu: 14,5 je platna sirka (Nigerie) a robot by nasel "nejblizsi
            // misto na mape" nekde, kde zadna mapa neni, a hlasil jen NoRoute.
            if (latDeg < -90 || latDeg > 90)
                throw new FormatException(
                    $"{source}, radek {lineNumber}: sirka {latDeg} je mimo rozsah -90..90. "
                    + "Nejsou sirka a delka prohozene?");
            if (lonDeg < -180 || lonDeg > 180)
                throw new FormatException(
                    $"{source}, radek {lineNumber}: delka {lonDeg} je mimo rozsah -180..180.");

            return new LLA(latDeg * Math.PI / 180.0, lonDeg * Math.PI / 180.0);
        }

        /// <summary>Popis pro cloveka do logu a na stranku.</summary>
        public override string ToString()
            => $"{Count} mist" + (Repeat ? " (dokola)" : " (jednou)");

        /// <summary>Souradnice bodu ve stupnich, pro log a diagnostiku.</summary>
        public string PointText(int index)
        {
            if (index < 0 || index >= points.Count) return "(mimo seznam)";
            var p = points[index];
            return string.Format(CultureInfo.InvariantCulture, "{0:F7},{1:F7}",
                                 p.Latitude * 180.0 / Math.PI, p.Longitude * 180.0 / Math.PI);
        }
    }
}
