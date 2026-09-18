#:package YamlDotNet@16.3.0

// Generátor registru úkolů: doc/ukoly.yaml -> doc/ukoly.md + web/pages/historie.html.
// Spouští se z kořene repozitáře:  dotnet run tools/ukoly.cs
// Návrh a pravidla: doc/plan-ukoly.md. Validace je přísná (chyba = návratový kód 1 a
// žádný výstup se nepřepíše) — jediný test generátoru je CI krok, který výstupy
// přegeneruje a porovná, takže se tu nesmí nic tiše "opravit".
//
// Výstup je ZÁMĚRNĚ bez časového razítka: CI porovnává vygenerované soubory s commitem
// a razítko by je rozhodilo při každém běhu.
//
// HLAVIČKA WEBU (menu) se tu NEVYRÁBÍ — vypíše se jen prázdné <header class="sitehead"></header>
// a naplní ho tools/menu.cs, který drží menu pro celý web na jednom místě. Do 18. 9. 2026 tu
// menu bylo opsané a byla to jedna z 22 kopií. DŮSLEDEK: po tomhle generátoru se MUSÍ pustit
// i `dotnet run tools/menu.cs`, jinak zůstane web/pages/historie.html bez menu.

using System.Globalization;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

const string ZdrojCesta = "doc/ukoly.yaml";
const string MdCesta = "doc/ukoly.md";
const string HtmlCesta = "web/pages/historie.html";


if (!File.Exists(ZdrojCesta))
{
    Console.Error.WriteLine($"Nenalezen {ZdrojCesta} — spouštěj z kořene repozitáře.");
    return 1;
}

Registr registr;
try
{
    var deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();
    registr = deserializer.Deserialize<Registr>(File.ReadAllText(ZdrojCesta, Encoding.UTF8));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"{ZdrojCesta}: nejde načíst: {ex.Message}");
    return 1;
}

var chyby = Validace.Zkontroluj(registr);
if (chyby.Count > 0)
{
    foreach (var ch in chyby) Console.Error.WriteLine($"{ZdrojCesta}: {ch}");
    Console.Error.WriteLine($"{chyby.Count} chyb, výstupy nedotčené.");
    return 1;
}

var md = MdVystup.Vyrob(registr);
var html = HtmlVystup.Vyrob(registr);
Zapis(MdCesta, md);
Zapis(HtmlCesta, html);
Console.WriteLine($"OK: {registr.Temata.Count} témat v {registr.Oblasti.Count} oblastech -> {MdCesta}, {HtmlCesta}");
return 0;

static void Zapis(string cesta, string obsah)
{
    // UTF-8 bez BOM, LF — git má text=auto, takže se to normalizuje stejně jako ruční soubory.
    File.WriteAllText(cesta, obsah.Replace("\r\n", "\n"), new UTF8Encoding(false));
}

// ---------------------------------------------------------------- model zdroje

public sealed class Registr
{
    public List<Oblast> Oblasti { get; set; } = new();
    public List<Tema> Temata { get; set; } = new();
}

public sealed class Oblast
{
    public string Id { get; set; } = "";
    public string Nazev { get; set; } = "";
}

public sealed class Tema
{
    public string Id { get; set; } = "";
    public string Oblast { get; set; } = "";
    public string Nazev { get; set; } = "";
    public string Druh { get; set; } = "";
    public string Stav { get; set; } = "";
    public string? Nalezeno { get; set; }
    public string? Vyreseno { get; set; }
    public string Popis { get; set; } = "";
    public List<Krok> Kroky { get; set; } = new();
    public List<string> CekaNa { get; set; } = new();
    public List<Odkaz> Odkazy { get; set; } = new();
    public List<string> Devlog { get; set; } = new();

    [YamlIgnore] public DateOnly NalezenoD { get; set; }
    [YamlIgnore] public DateOnly? VyresenoD { get; set; }
}

public sealed class Krok
{
    public string Co { get; set; } = "";
    public string Stav { get; set; } = "";
    public string? Kdy { get; set; }
    [YamlIgnore] public DateOnly? KdyD { get; set; }
}

public sealed class Odkaz
{
    public string Text { get; set; } = "";
    public string Cesta { get; set; } = "";
}

public static class Stavy
{
    public const string Otevreno = "otevreno", VKodu = "v-kodu", Hotovo = "hotovo",
        Odlozeno = "odlozeno", Zamitnuto = "zamitnuto";
    public static readonly string[] Vse = { Otevreno, VKodu, Hotovo, Odlozeno, Zamitnuto };
    // Pořadí v tabulce otevřených a v bloku oblasti.
    public static int Poradi(string s) => Array.IndexOf(Vse, s);
    public static string Popisek(string s) => s switch
    {
        Otevreno => "otevřeno", VKodu => "v kódu, na HW neověřeno", Hotovo => "hotovo",
        Odlozeno => "odloženo", Zamitnuto => "zamítnuto", _ => s
    };
    public static bool JeUzavreno(string s) => s is Hotovo or Zamitnuto;
}

// ---------------------------------------------------------------- validace

public static class Validace
{
    public static List<string> Zkontroluj(Registr r)
    {
        var chyby = new List<string>();
        var oblasti = new HashSet<string>();
        foreach (var o in r.Oblasti)
        {
            if (string.IsNullOrWhiteSpace(o.Id) || string.IsNullOrWhiteSpace(o.Nazev))
                chyby.Add("oblast bez id nebo názvu");
            else if (!oblasti.Add(o.Id)) chyby.Add($"oblast `{o.Id}` je dvakrát");
        }

        var ids = new HashSet<string>();
        foreach (var t in r.Temata)
        {
            if (string.IsNullOrWhiteSpace(t.Id)) { chyby.Add("téma bez id"); continue; }
            if (!ids.Add(t.Id)) chyby.Add($"{t.Id}: id je dvakrát");
        }

        foreach (var t in r.Temata)
        {
            string P(string pole) => $"{t.Id}: {pole}";
            if (!oblasti.Contains(t.Oblast)) chyby.Add(P($"neznámá oblast `{t.Oblast}`"));
            if (string.IsNullOrWhiteSpace(t.Nazev)) chyby.Add(P("chybí nazev"));
            if (t.Druh is not ("zamer" or "vada")) chyby.Add(P($"druh musí být zamer|vada, je `{t.Druh}`"));
            if (!Stavy.Vse.Contains(t.Stav)) chyby.Add(P($"neznámý stav `{t.Stav}`"));
            if (string.IsNullOrWhiteSpace(t.Popis)) chyby.Add(P("chybí popis"));

            if (!Datum(t.Nalezeno, out var nal)) chyby.Add(P($"nalezeno musí být RRRR-MM-DD, je `{t.Nalezeno}`"));
            t.NalezenoD = nal;
            if (t.Vyreseno is not null)
            {
                if (!Datum(t.Vyreseno, out var vyr)) chyby.Add(P($"vyreseno musí být RRRR-MM-DD, je `{t.Vyreseno}`"));
                else
                {
                    t.VyresenoD = vyr;
                    if (vyr < nal) chyby.Add(P("vyreseno je dřív než nalezeno"));
                }
                if (t.Stav is Stavy.Otevreno or Stavy.Odlozeno)
                    chyby.Add(P($"stav `{t.Stav}` nemůže mít vyreseno"));
            }
            else if (t.Stav is Stavy.VKodu or Stavy.Hotovo or Stavy.Zamitnuto)
                chyby.Add(P($"stav `{t.Stav}` musí mít vyreseno"));

            foreach (var k in t.Kroky)
            {
                if (string.IsNullOrWhiteSpace(k.Co)) chyby.Add(P("krok bez `co`"));
                if (k.Stav is not ("hotovo" or "otevreno")) chyby.Add(P($"krok `{k.Co}`: stav musí být hotovo|otevreno"));
                if (k.Kdy is not null)
                {
                    if (!Datum(k.Kdy, out var kd)) chyby.Add(P($"krok `{k.Co}`: kdy musí být RRRR-MM-DD"));
                    else k.KdyD = kd;
                }
                else if (k.Stav == "hotovo") chyby.Add(P($"krok `{k.Co}` je hotovo bez data"));
            }
            if (t.Stav == Stavy.Hotovo && t.Kroky.Any(k => k.Stav != "hotovo"))
                chyby.Add(P("stav hotovo, ale má otevřený krok"));

            foreach (var d in t.CekaNa)
            {
                if (!ids.Contains(d)) chyby.Add(P($"ceka_na odkazuje na neznámé id `{d}`"));
                if (d == t.Id) chyby.Add(P("ceka_na na sebe sama"));
            }
            foreach (var o in t.Odkazy)
            {
                if (string.IsNullOrWhiteSpace(o.Text) || string.IsNullOrWhiteSpace(o.Cesta)) chyby.Add(P("odkaz bez textu nebo cesty"));
                else if (!File.Exists(o.Cesta) && !Directory.Exists(o.Cesta)) chyby.Add(P($"odkaz `{o.Cesta}` v repozitáři neexistuje"));
            }
            foreach (var d in t.Devlog)
                if (!Datum(d, out _)) chyby.Add(P($"devlog `{d}` není RRRR-MM-DD"));
        }

        // Cyklus v závislostech (DFS s barvením).
        var mapa = r.Temata.Where(t => ids.Contains(t.Id)).DistinctBy(t => t.Id).ToDictionary(t => t.Id);
        var barva = new Dictionary<string, int>();
        foreach (var t in mapa.Values)
            if (Cyklus(t.Id, mapa, barva, out var kde)) { chyby.Add($"cyklus v ceka_na přes `{kde}`"); break; }

        return chyby;
    }

    static bool Cyklus(string id, Dictionary<string, Tema> mapa, Dictionary<string, int> barva, out string kde)
    {
        kde = "";
        if (barva.TryGetValue(id, out var b)) { if (b == 1) { kde = id; return true; } return false; }
        barva[id] = 1;
        foreach (var d in mapa[id].CekaNa)
            if (mapa.ContainsKey(d) && Cyklus(d, mapa, barva, out kde)) return true;
        barva[id] = 2;
        return false;
    }

    public static bool Datum(string? s, out DateOnly d) =>
        DateOnly.TryParseExact(s ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
}

// ---------------------------------------------------------------- společné

public static class Text
{
    public static string Cz(DateOnly d) => $"{d.Day}. {d.Month}. {d.Year}";
    public static string Cz(DateOnly? d) => d is null ? "—" : Cz(d.Value);
    static readonly string[] Mesice = { "leden", "únor", "březen", "duben", "květen", "červen",
        "červenec", "srpen", "září", "říjen", "listopad", "prosinec" };
    public static string Mesic(int rok, int mesic) => $"{Mesice[mesic - 1]} {rok}";

    public static string Html(string s)
    {
        var e = System.Net.WebUtility.HtmlEncode(s.Trim());
        // `kód` -> <code>; nic dalšího se neinterpretuje.
        var sb = new StringBuilder();
        bool v = false;
        foreach (var ch in e)
        {
            if (ch == '`') { sb.Append(v ? "</code>" : "<code>"); v = !v; }
            else sb.Append(ch);
        }
        if (v) sb.Append("</code>");
        return sb.ToString();
    }
}

// ---------------------------------------------------------------- doc/ukoly.md

public static class MdVystup
{
    public static string Vyrob(Registr r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Úkoly projektu — registr");
        sb.AppendLine();
        sb.AppendLine("<!-- GENEROVÁNO z doc/ukoly.yaml nástrojem tools/ukoly.cs — needitovat ručně. -->");
        sb.AppendLine();
        sb.AppendLine("**Generováno** z [ukoly.yaml](ukoly.yaml) příkazem `dotnet run tools/ukoly.cs`; ruční úpravy");
        sb.AppendLine("přepíše další běh. Pravidla a schéma: [plan-ukoly.md](plan-ukoly.md). Totéž pro web:");
        sb.AppendLine("[web/pages/historie.html](../web/pages/historie.html).");
        sb.AppendLine();

        var pocty = Stavy.Vse.Select(s => $"{Stavy.Popisek(s)} **{r.Temata.Count(t => t.Stav == s)}**");
        sb.AppendLine($"Témat celkem **{r.Temata.Count}**: {string.Join(" · ", pocty)}.");
        sb.AppendLine();

        var oblasti = r.Oblasti.ToDictionary(o => o.Id, o => o.Nazev);

        sb.AppendLine("## Otevřené a v kódu (kde jsme)");
        sb.AppendLine();
        sb.AppendLine("| stav | oblast | téma | nalezeno | čeká na |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var t in r.Temata.Where(t => !Stavy.JeUzavreno(t.Stav))
                     .OrderBy(t => Stavy.Poradi(t.Stav)).ThenBy(t => t.NalezenoD).ThenBy(t => t.Id))
        {
            var ceka = t.CekaNa.Count == 0 ? "" : string.Join(", ", t.CekaNa.Select(d => $"[{d}](#{d})"));
            sb.AppendLine($"| {Stavy.Popisek(t.Stav)} | {oblasti[t.Oblast]} | [{t.Nazev}](#{t.Id}) | {Text.Cz(t.NalezenoD)} | {ceka} |");
        }
        sb.AppendLine();

        foreach (var o in r.Oblasti)
        {
            var temata = r.Temata.Where(t => t.Oblast == o.Id)
                .OrderBy(t => Stavy.JeUzavreno(t.Stav) ? 1 : 0).ThenBy(t => Stavy.Poradi(t.Stav))
                .ThenBy(t => t.NalezenoD).ThenBy(t => t.Id).ToList();
            if (temata.Count == 0) continue;
            sb.AppendLine($"## {o.Nazev}");
            sb.AppendLine();
            foreach (var t in temata)
            {
                sb.AppendLine($"<a id=\"{t.Id}\"></a>");
                sb.AppendLine($"### {Znacka(t.Stav)} {t.Nazev}");
                sb.AppendLine();
                var radek = $"`{t.Id}` · {(t.Druh == "vada" ? "vada" : "záměr")} · **{Stavy.Popisek(t.Stav)}** · nalezeno {Text.Cz(t.NalezenoD)}";
                if (t.VyresenoD is not null) radek += $" · vyřešeno {Text.Cz(t.VyresenoD)}";
                sb.AppendLine(radek);
                sb.AppendLine();
                sb.AppendLine(t.Popis.Trim());
                sb.AppendLine();
                if (t.Kroky.Count > 0)
                {
                    foreach (var k in t.Kroky)
                        sb.AppendLine($"- [{(k.Stav == "hotovo" ? "x" : " ")}] {k.Co}{(k.KdyD is null ? "" : $" ({Text.Cz(k.KdyD)})")}");
                    sb.AppendLine();
                }
                var meta = new List<string>();
                if (t.CekaNa.Count > 0) meta.Add("čeká na " + string.Join(", ", t.CekaNa.Select(d => $"[{d}](#{d})")));
                if (t.Odkazy.Count > 0) meta.Add(string.Join(", ", t.Odkazy.Select(l => $"[{l.Text}]({(l.Cesta.StartsWith("doc/") ? l.Cesta[4..] : "../" + l.Cesta)})")));
                if (t.Devlog.Count > 0) meta.Add("DevLog " + string.Join(", ", t.Devlog.Select(d => $"[{d}](devlog.md#{d})")));
                if (meta.Count > 0) { sb.AppendLine(string.Join(" · ", meta)); sb.AppendLine(); }
            }
        }
        return sb.ToString();
    }

    static string Znacka(string stav) => stav switch
    {
        Stavy.Hotovo => "✅", Stavy.VKodu => "🧪", Stavy.Otevreno => "⬜", Stavy.Odlozeno => "⏸", Stavy.Zamitnuto => "❌", _ => ""
    };
}

// ---------------------------------------------------------------- web/pages/historie.html

public static class HtmlVystup
{
    const string GitHubBlob = "https://github.com/AlesRuda/ARBot3/blob/master/";
    public static string Vyrob(Registr r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
<!-- GENEROVÁNO z doc/ukoly.yaml nástrojem tools/ukoly.cs — needitovat ručně, změny přepíše další běh. -->
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>ARBot – Čím si projekt prošel</title>
<link rel="icon" href="../assets/img/arbot-logo.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Archivo:wght@500;600;700&amp;family=JetBrains+Mono:wght@400;600&amp;family=Newsreader:opsz,wght@6..72,400;6..72,500;6..72,600&amp;display=swap">
<link rel="stylesheet" href="../assets/site.css">

<header class="sitehead"></header>
<main>
  <div class="pagehead">
    <p class="eyebrow">Třetí verze softwaru, od června 2026</p>
    <h1>Čím si projekt prošel</h1>
  </div>
""");
        sb.AppendLine("<section class=\"first\">");
        sb.AppendLine("  <p class=\"lead\">Co se kdy ukázalo jako problém, co se s tím udělalo a co ještě čeká. Každá položka vede");
        sb.AppendLine("  do dokumentace v repozitáři, kde jsou čísla a měření.</p>");
        // Filtr: bez JavaScriptu je to jen legenda s počty; se skriptem (dole) zaškrtávátka
        // filtrují karty i osu a pole hledá v textu karet.
        sb.AppendLine("  <form class=\"filtr\" id=\"filtr\" onsubmit=\"return false\" autocomplete=\"off\">");
        foreach (var s in Stavy.Vse)
            sb.AppendLine($"    <label><input type=\"checkbox\" name=\"stav\" value=\"{s}\" checked> <span class=\"stav stav-{s}\">{Text.Html(Stavy.Popisek(s))}</span> <span class=\"mono\">{r.Temata.Count(t => t.Stav == s)}</span></label>");
        sb.AppendLine("    <input type=\"search\" name=\"hledat\" placeholder=\"hledat v textu…\" aria-label=\"hledat v textu\" hidden>");
        sb.AppendLine("    <span class=\"mono pocet\" id=\"filtr-pocet\"></span>");
        sb.AppendLine("  </form>");
        sb.AppendLine("</section>");

        // --- osa po měsících (vzestupně: vypráví se příběh)
        var udalosti = new List<(DateOnly kdy, string typ, Tema t)>();
        foreach (var t in r.Temata)
        {
            var zacatek = t.Druh == "vada" ? "nalezeno" : "začato";
            var konec = t.Stav == Stavy.Zamitnuto ? "zamítnuto" : "vyřešeno";
            // Nalezeno a vyřešeno v týž den = jedna položka, jinak osa nese každé téma dvakrát.
            if (t.VyresenoD == t.NalezenoD) udalosti.Add((t.NalezenoD, zacatek + " i " + konec, t));
            else
            {
                udalosti.Add((t.NalezenoD, zacatek, t));
                if (t.VyresenoD is not null) udalosti.Add((t.VyresenoD.Value, konec, t));
            }
        }
        sb.AppendLine("<section class=\"osa-sekce\">");
        sb.AppendLine("  <p class=\"eyebrow\">Časová osa</p>");
        sb.AppendLine("  <h2>Měsíc po měsíci</h2>");
        foreach (var m in udalosti.GroupBy(u => (u.kdy.Year, u.kdy.Month)).OrderBy(g => g.Key))
        {
            sb.AppendLine("  <div class=\"osa\">");
            sb.AppendLine($"    <h3>{Text.Html(Text.Mesic(m.Key.Year, m.Key.Month))}</h3>");
            sb.AppendLine("    <ul>");
            foreach (var u in m.OrderBy(u => u.kdy).ThenBy(u => u.t.Id))
                sb.AppendLine($"      <li data-tema=\"{u.t.Id}\"><span class=\"mono\">{u.kdy.Day}. {u.kdy.Month}.</span> <span class=\"typ\">{u.typ}</span> <a href=\"#{u.t.Id}\">{Text.Html(u.t.Nazev)}</a></li>");
            sb.AppendLine("    </ul>");
            sb.AppendLine("  </div>");
        }
        sb.AppendLine("</section>");

        // --- strom po oblastech
        foreach (var o in r.Oblasti)
        {
            var temata = r.Temata.Where(t => t.Oblast == o.Id)
                .OrderBy(t => Stavy.JeUzavreno(t.Stav) ? 1 : 0).ThenBy(t => Stavy.Poradi(t.Stav))
                .ThenBy(t => t.NalezenoD).ThenBy(t => t.Id).ToList();
            if (temata.Count == 0) continue;
            sb.AppendLine("<section class=\"oblast\">");
            sb.AppendLine("  <p class=\"eyebrow\">Oblast</p>");
            sb.AppendLine($"  <h2>{Text.Html(o.Nazev)}</h2>");
            foreach (var t in temata)
            {
                sb.AppendLine($"  <article class=\"tema\" id=\"{t.Id}\" data-stav=\"{t.Stav}\">");
                sb.AppendLine($"    <h3>{Text.Html(t.Nazev)}</h3>");
                sb.Append($"    <p class=\"tema-meta\"><span class=\"stav stav-{t.Stav}\">{Text.Html(Stavy.Popisek(t.Stav))}</span>");
                sb.Append($" <span>{(t.Druh == "vada" ? "vada" : "záměr")}</span> <span>nalezeno {Text.Cz(t.NalezenoD)}</span>");
                if (t.VyresenoD is not null) sb.Append($" <span>vyřešeno {Text.Cz(t.VyresenoD)}</span>");
                sb.AppendLine("</p>");
                sb.AppendLine($"    <p>{Text.Html(t.Popis)}</p>");
                if (t.Kroky.Count > 0)
                {
                    sb.AppendLine("    <ul class=\"kroky\">");
                    foreach (var k in t.Kroky)
                        sb.AppendLine($"      <li class=\"{(k.Stav == "hotovo" ? "hotovo" : "otevreno")}\">{Text.Html(k.Co)}{(k.KdyD is null ? "" : $" <span class=\"mono\">{Text.Cz(k.KdyD)}</span>")}</li>");
                    sb.AppendLine("    </ul>");
                }
                var meta = new List<string>();
                if (t.CekaNa.Count > 0) meta.Add("čeká na " + string.Join(", ", t.CekaNa.Select(d => $"<a href=\"#{d}\">{Text.Html(Nazev(r, d))}</a>")));
                if (t.Odkazy.Count > 0) meta.Add(string.Join(", ", t.Odkazy.Select(l => $"<a href=\"{GitHubBlob}{l.Cesta}\">{Text.Html(l.Text)}</a>")));
                if (t.Devlog.Count > 0) meta.Add("DevLog " + string.Join(", ", t.Devlog.Select(d => $"<a href=\"{GitHubBlob}doc/devlog.md#{d}\">{d}</a>")));
                if (meta.Count > 0) sb.AppendLine($"    <p class=\"tema-odkazy\">{string.Join(" · ", meta)}</p>");
                sb.AppendLine("  </article>");
            }
            sb.AppendLine("</section>");
        }

        // Jediný JavaScript na webu (rozhodnutí autora 17. 9. 2026): filtr podle stavu
        // a hledání v textu. Bez skriptu stránka funguje celá, jen bez filtru.
        sb.AppendLine("""
<script>
(function () {
  var f = document.getElementById('filtr'); if (!f) return;
  var hledat = f.querySelector('input[name=hledat]'); hledat.hidden = false;
  var pocet = document.getElementById('filtr-pocet');
  var karty = Array.prototype.slice.call(document.querySelectorAll('article.tema'));
  var norm = function (s) { return s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''); };
  karty.forEach(function (k) { k._text = norm(k.textContent); });
  function pouzij() {
    var stavy = {}; f.querySelectorAll('input[name=stav]').forEach(function (c) { stavy[c.value] = c.checked; });
    var q = norm(hledat.value.trim());
    var videt = {}; var n = 0;
    karty.forEach(function (k) {
      var ok = stavy[k.dataset.stav] && (q === '' || k._text.indexOf(q) >= 0);
      k.classList.toggle('skryto', !ok); videt[k.id] = ok; if (ok) n++;
    });
    document.querySelectorAll('section.oblast').forEach(function (s) {
      s.classList.toggle('skryto', !s.querySelector('article.tema:not(.skryto)'));
    });
    document.querySelectorAll('.osa li[data-tema]').forEach(function (li) { li.classList.toggle('skryto', !videt[li.dataset.tema]); });
    document.querySelectorAll('.osa').forEach(function (o) { o.classList.toggle('skryto', !o.querySelector('li:not(.skryto)')); });
    var os = document.querySelector('section.osa-sekce'); if (os) os.classList.toggle('skryto', !os.querySelector('.osa:not(.skryto)'));
    pocet.textContent = n === karty.length ? '' : n + ' z ' + karty.length;
  }
  f.addEventListener('change', pouzij); hledat.addEventListener('input', pouzij); pouzij();
})();
</script>
<footer>
  <div class="foot-in">
    <span class="mono">ARBOT &middot; AUTONOMNÍ MOBILNÍ ROBOT</span>
    <span>Stránka je generovaná z registru úkolů v repozitáři:
      <a href="https://github.com/AlesRuda/ARBot3/blob/master/doc/ukoly.yaml">doc/ukoly.yaml</a></span>
  </div>
</footer>
</main>
""");
        return sb.ToString();
    }

    static string Nazev(Registr r, string id) => r.Temata.First(t => t.Id == id).Nazev;
}
