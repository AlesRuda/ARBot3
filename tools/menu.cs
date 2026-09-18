// Generátor hlavičky webu: JEDINÝ zdroj hlavního menu -> <header class="sitehead"> ve všech
// stránkách pod web/. Spouští se z kořene repozitáře:  dotnet run tools/menu.cs
//
// PROČ: hlavička byla opsaná v každé stránce zvlášť. Do 18. 9. 2026 to bylo 21 souborů
// + generátor tools/ukoly.cs, tedy 22 míst, a rostlo to s každou novou stránkou; README to
// vedlo jako vědomý dluh ("generátor celého menu"). Změna menu se tak dělala mechanicky a
// mlčky se při ní dalo zapomenout na jeden soubor — nejhůř na ten generovaný.
//
// POŘADÍ SPOUŠTĚNÍ: nejdřív tools/ukoly.cs (vyrobí web/pages/historie.html s PRÁZDNOU
// hlavičkou <header class="sitehead"></header>), teprve pak tenhle nástroj, který ji doplní.
// Kdo pustí jen ukoly.cs, nechá historie.html bez menu — hlídá to CI (build-and-test.yml,
// job "generovane-soubory"), které pustí obojí a porovná výsledek s commitem.
//
// Validace je PŘÍSNÁ (chyba = návratový kód 1 a žádný soubor se nepřepíše), ze stejného
// důvodu jako u tools/ukoly.cs: jediný test generátoru je ten CI krok, takže se tu nesmí
// nic tiše "opravit".
//
// Menu je schválně v tomhle souboru a ne ve vlastním datovém souboru (jako doc/ukoly.yaml):
// je to sedm řádků, mění se jednou za pár měsíců a takhle je zdroj i pravidla na jednom místě.

using System.Text;
using System.Text.RegularExpressions;

const string WebRoot = "web";

// ---------------------------------------------------------------- hlavní menu
// Cesta je relativní k web/ (prefix ../ si nástroj doplní podle umístění stránky).
var menu = new (string Cesta, string Popisek)[]
{
    ("index.html",                        "Úvod"),
    ("pages/prezentace.html",             "Jak to funguje"),
    ("pages/historie.html",               "Historie"),
    ("pages/umisteni-v-soutezich.html",   "Umístění v soutěžích"),
    ("pages/verze.html",                  "Verze"),
    ("pages/technicke-clanky.html",       "Technické články"),
    ("pages/kontakt.html",                "Kontakt"),
};

// ---------------------------------------------------------------- skupiny
// Stránky, které vlastní položku v menu NEMAJÍ: která položka se na nich zvýrazní
// (class="on"). Bez toho by na článku nebylo zvýrazněné nic a vypadalo by to, že
// návštěvník vypadl ze struktury webu.
var skupiny = new (string Polozka, string[] Stranky)[]
{
    ("pages/technicke-clanky.html", new[]
    {
        "pages/model-diferencialniho-podvozku.html",
        "pages/detekce-kraje-vozovky.html",
        "pages/regulator-sledovani-drahy.html",
    }),
    ("pages/umisteni-v-soutezich.html", new[]
    {
        "pages/robotour-2009.html",
        "pages/robotour-2011.html",
        "pages/robotour-2012.html",
        "pages/robotour-2013.html",
        "pages/robotem-rovne-2010.html",
        "pages/robotem-rovne-2011.html",
        "pages/robotem-rovne-2012.html",
        "pages/robotem-rovne-2013.html",
        "pages/robotem-rovne-2017.html",
        "pages/roboorienteering-2010.html",
        "pages/roboorienteering-2011.html",
    }),
};

// ---------------------------------------------------------------- běh

if (!Directory.Exists(WebRoot))
{
    Console.Error.WriteLine($"Nenalezena složka {WebRoot}/ — spouštěj z kořene repozitáře.");
    return 1;
}

var chyby = new List<string>();

// Odkud se zvýrazní která položka. Klíč = stránka, hodnota = cesta položky v menu.
var zvyrazni = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var (cesta, _) in menu)
{
    if (!File.Exists(Path.Combine(WebRoot, cesta)))
        chyby.Add($"položka menu „{cesta}“ neexistuje jako soubor");
    zvyrazni[cesta] = cesta;
}
foreach (var (polozka, stranky) in skupiny)
{
    if (!menu.Any(m => m.Cesta == polozka))
        chyby.Add($"skupina míří na „{polozka}“, což není položka menu");
    foreach (var s in stranky)
    {
        if (!File.Exists(Path.Combine(WebRoot, s)))
            chyby.Add($"skupina „{polozka}“ uvádí stránku „{s}“, která neexistuje");
        if (zvyrazni.TryGetValue(s, out var uz))
            chyby.Add($"stránka „{s}“ je uvedená dvakrát (už ji vlastní „{uz}“)");
        else
            zvyrazni[s] = polozka;
    }
}

var hlavicka = new Regex("<header class=\"sitehead\">.*?</header>", RegexOptions.Singleline);

var soubory = Directory.EnumerateFiles(WebRoot, "*.html", SearchOption.AllDirectories)
                       .Select(p => Path.GetRelativePath(WebRoot, p).Replace('\\', '/'))
                       .OrderBy(p => p, StringComparer.Ordinal)
                       .ToList();

var plan = new List<(string Cesta, string Novy)>();
foreach (var rel in soubory)
{
    var plna = Path.Combine(WebRoot, rel);
    var txt = File.ReadAllText(plna, new UTF8Encoding(false));
    if (!hlavicka.IsMatch(txt))
    {
        chyby.Add($"{rel}: chybí blok <header class=\"sitehead\">…</header>");
        continue;
    }
    if (!zvyrazni.TryGetValue(rel, out var aktivni))
    {
        chyby.Add($"{rel}: stránka není ani položkou menu, ani v žádné skupině — "
                + "doplň ji do tools/menu.cs (menu, nebo skupiny)");
        continue;
    }

    // "pages/kontakt.html" -> "../", "index.html" -> ""
    var prefix = string.Concat(Enumerable.Repeat("../", rel.Count(c => c == '/')));
    // Konce řádků se přebírají ze souboru: .gitattributes má `* text=auto`, takže na Windows
    // je pracovní kopie CRLF a na CI LF. Pevné "\n" by na Windows vyrobilo míchané konce
    // řádků uvnitř jinak CRLF souboru.
    var eol = txt.Contains("\r\n") ? "\r\n" : "\n";
    var blok = Vyrob(prefix, aktivni, eol);
    // MatchEvaluator, ne řetězec: v náhradě by se `$` bral jako odkaz na skupinu.
    plan.Add((rel, hlavicka.Replace(txt, _ => blok, 1)));
}

if (chyby.Count > 0)
{
    foreach (var ch in chyby) Console.Error.WriteLine($"{WebRoot}/: {ch}");
    Console.Error.WriteLine($"{chyby.Count} chyb, výstupy nedotčené.");
    return 1;
}

int zmeneno = 0;
foreach (var (rel, novy) in plan)
{
    var plna = Path.Combine(WebRoot, rel);
    if (File.ReadAllText(plna, new UTF8Encoding(false)) == novy) continue;
    File.WriteAllText(plna, novy, new UTF8Encoding(false));
    Console.WriteLine($"  přepsáno: {rel}");
    zmeneno++;
}
Console.WriteLine($"OK: {menu.Length} položek menu v {plan.Count} stránkách "
                + $"({(zmeneno == 0 ? "beze změny" : zmeneno + " přepsáno")}).");
return 0;

// ---------------------------------------------------------------- sazba

string Vyrob(string prefix, string aktivni, string eol)
{
    var sb = new StringBuilder();
    sb.Append("<header class=\"sitehead\">").Append(eol);
    sb.Append($"  <a class=\"brand\" href=\"{prefix}index.html\">"
            + $"<img src=\"{prefix}assets/img/arbot-logo.png\" width=\"106\" height=\"106\" alt=\"\">"
            + "<span>ARBot</span></a>").Append(eol);
    sb.Append("  <nav>").Append(eol);
    foreach (var (cesta, popisek) in menu)
    {
        var on = cesta == aktivni ? " class=\"on\"" : "";
        sb.Append($"      <a href=\"{prefix}{cesta}\"{on}>{popisek}</a>").Append(eol);
    }
    sb.Append("  </nav>").Append(eol);
    sb.Append("</header>");
    return sb.ToString();
}
