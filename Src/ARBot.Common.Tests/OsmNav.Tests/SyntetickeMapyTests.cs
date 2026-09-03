using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARBot.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Osm;

namespace ARBot.Common.Tests.OsmNav;

/// <summary>
/// Syntetické testovaci mapy v <c>OSM/</c> jsou <b>merici pristroje</b> — cisla namerena nad nimi
/// se citujou v dokumentaci a rozhoduje se podle nich, takze jejich geometrie musi byt hlidana
/// stejne jako kod. Tenhle test overuje, ze rovne mapy (<c>SyntetickyRovny.osm</c> 160 m × 2 m
/// a <c>SyntetickyRovny2m.osm</c> 2 m × 1,5 m) jsou skutecne rovne a konstantni sirky a ze
/// <c>SyntetickyRovnyPosunuty.osm</c> je od 160m originalu <b>tuhou translaci</b>.
///
/// <para><b>Nacpak.</b> Obe vlastnosti se uz jednou nedodrzely a stalo to praci:
/// <c>SyntetickyKoridor.osm</c> ma nalevku, ktera zamitne koridor na 20 % cyklu (a vypada to jako
/// vada algoritmu — viz doc/map-correlation-localization.md), a
/// <c>SyntetickyKoridorPosunuty.osm</c> ma posun <b>nahodny per uzel</b>, takze korelace nad nim
/// nema jednu spravnou odpoved a nejde z ni udelat falsifikovatelnou predpoved.</para>
///
/// <para><b>Souradnice se cti tak, jak je cte aplikace</b> — pres <see cref="OsmXmlReader"/>
/// a <see cref="GeoReference"/>, ne rucnim prepoctem. Test tedy chyta i to, kdyby se zmenil
/// prevod, ne jen kdyby nekdo prepsal soubor.</para>
///
/// <para>Testy rovnych map jsou <b>parametrizovane</b> (soubor, delka, sirka, rozestup uzlu):
/// kazda dalsi rovna mapa se prida jednim radkem v <see cref="RovneMapy"/>, ne kopii testu.</para>
/// </summary>
public class SyntetickeMapyTests
{
    /// <summary>Pocatek lokalni ENU roviny — uzel 1 originalu; tentyz jako u SyntetickyKoridor.osm.</summary>
    private static GeoReference Origin() => GeoReference.FromDegrees(50.0290000, 14.5200000);

    /// <summary>Delka dlouhe rovne mapy [m] podle zadani v jeji hlavicce.</summary>
    private const double LengthM = 160.0;

    /// <summary>Sirka dlouhe rovne mapy [m] podle zadani v jeji hlavicce.</summary>
    private const double WidthM = 2.0;

    /// <summary>Tuha translace posunute dvojnice [m] podle zadani v jeji hlavicce.</summary>
    private const double ShiftEastM = 0.60, ShiftNorthM = -0.40;

    /// <summary>
    /// Rovne mapy a jejich zadani z hlavicek: soubor, delka [m], sirka [m], rozestup uzlu [m].
    /// Pocet uzlu plyne z delky a rozestupu (delka / rozestup + 1).
    /// </summary>
    private static readonly object[] RovneMapy =
    {
        new object[] { "SyntetickyRovny.osm",   LengthM, WidthM, 20.0 },
        new object[] { "SyntetickyRovny2m.osm", 2.0,     1.5,    0.5 },
    };

    /// <summary>
    /// Najde adresar <c>OSM/</c> — testy bezi z vlastniho bin adresare, takze se hleda o par
    /// urovni vys (stejny pristup jako <c>ARBot.Analyze</c> u <c>Records/</c>).
    /// </summary>
    private static string MapPath(string fileName)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "OSM", fileName);
            if (File.Exists(candidate)) return candidate;
        }
        Assert.Fail($"Mapa {fileName} se nenasla (hledano v OSM/ nad {Directory.GetCurrentDirectory()}).");
        return null;
    }

    /// <summary>Sit sestavena z mapy tak, jak ji staví aplikace (chodecky profil).</summary>
    private static RoadNetwork Network(string fileName)
    {
        using var stream = File.OpenRead(MapPath(fileName));
        return GraphBuilder.BuildNetwork(OsmXmlReader.Read(stream), TravelProfile.Pedestrian());
    }

    /// <summary>Uzly mapy v lokalnich metrech, v poradí podle id.</summary>
    private static List<(long Id, Point2D P, double Width)> Nodes(string fileName)
    {
        var geo = Origin();
        var network = Network(fileName);

        // Uzly se berou ze site (ne z surovych dat), takze se overuje i to, co z mapy vznikne
        // po sestaveni grafu — vcetne sirky, ktera se dopocitava v GraphBuilder.
        var seen = new Dictionary<long, (Point2D P, double W)>();
        foreach (var e in network.Edges)
        {
            seen[e.From.Id] = (geo.ToLocal(e.From.Location), e.From.Width);
            seen[e.To.Id] = (geo.ToLocal(e.To.Location), e.To.Width);
        }
        return seen.OrderBy(kv => kv.Key)
                   .Select(kv => (kv.Key, kv.Value.P, kv.Value.W)).ToList();
    }

    [TestCaseSource(nameof(RovneMapy))]
    public void Rovna_jePresneRovna(string file, double lengthM, double widthM, double stepM)
    {
        var nodes = Nodes(file);
        int expectedCount = (int)Math.Round(lengthM / stepM) + 1;

        Assert.That(nodes, Has.Count.EqualTo(expectedCount),
                    $"{file}: mapa ma mit {expectedCount} uzlu po {stepM} m");
        Assert.Multiple(() =>
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                Assert.That(nodes[i].P.X, Is.EqualTo(i * stepM).Within(0.001),
                            $"{file}: uzel {nodes[i].Id} ma byt na x = {i * stepM} m");
                Assert.That(nodes[i].P.Y, Is.EqualTo(0.0).Within(0.001),
                            $"{file}: uzel {nodes[i].Id} se odchyluje od osy — cesta uz neni rovna");
            }
        });
    }

    [TestCaseSource(nameof(RovneMapy))]
    public void Rovna_maKonstantniSirku(string file, double lengthM, double widthM, double stepM)
    {
        var nodes = Nodes(file);

        // Sirka MUSI byt na kazdem uzlu, ne jen na ceste: RoadScene interpoluje polosirku mezi
        // uzly, takze jediny uzel s jinou sirkou z cesty udela nalevku — presne to, cim je
        // SyntetickyKoridor.osm nepouzitelny pro mereni rovnobeznosti.
        Assert.Multiple(() =>
        {
            foreach (var n in nodes)
                Assert.That(n.Width, Is.EqualTo(widthM).Within(1e-9),
                            $"{file}: uzel {n.Id} ma jinou sirku — vznikla by nalevka");
        });
    }

    [TestCaseSource(nameof(RovneMapy))]
    public void Rovna_maZadanouDelku(string file, double lengthM, double widthM, double stepM)
    {
        var nodes = Nodes(file);

        double length = nodes[nodes.Count - 1].P.X - nodes[0].P.X;
        Assert.That(length, Is.EqualTo(lengthM).Within(0.001), file);
    }

    /// <summary>
    /// Sirka je konstantni i MEZI uzly, ne jen na nich. Overuje se pres
    /// <see cref="RoadScene.IsRoad"/>, tedy pres tuze cestu, kterou vidi virtualni kamera —
    /// kdyby se interpolace chovala jinak, nez se ceka, projevilo by se to prave tady.
    /// </summary>
    [TestCaseSource(nameof(RovneMapy))]
    public void Rovna_sirkaDrziIMeziUzly(string file, double lengthM, double widthM, double stepM)
    {
        var scene = new RoadScene(Network(file), Origin());

        double half = widthM / 2;
        // Vzorkuje se po ctvrtine rozestupu uzlu, tedy i uprostred segmentu (u 160m mapy
        // x = 10, 30, 50, ...; u 2m mapy x = 0,25, 0,75, ...) — tam by pripadna interpolace
        // mezi ruznymi sirkami udelala nejvetsi rozdil. Krajni ctvrtina se vynechava, aby se
        // nemerilo tesne u zakonceni cesty.
        double sample = stepM / 4;
        Assert.Multiple(() =>
        {
            for (double x = sample; x <= lengthM - sample + 1e-9; x += sample)
            {
                Assert.That(scene.IsRoad(x, 0), Is.True, $"{file} x={x}: osa ma byt cesta");
                Assert.That(scene.IsRoad(x, half - 0.05), Is.True,
                            $"{file} x={x}: tesne uvnitr leve hranice ma byt cesta");
                Assert.That(scene.IsRoad(x, -(half - 0.05)), Is.True,
                            $"{file} x={x}: tesne uvnitr prave hranice ma byt cesta");
                Assert.That(scene.IsRoad(x, half + 0.05), Is.False,
                            $"{file} x={x}: tesne za levou hranici uz cesta byt nema");
                Assert.That(scene.IsRoad(x, -(half + 0.05)), Is.False,
                            $"{file} x={x}: tesne za pravou hranici uz cesta byt nema");
            }
        });
    }

    /// <summary>
    /// Rovna mapa nema mit <b>zadnou krizovatku ani odbocku</b> — to je cely jeji smysl. Kdyby
    /// pribyla, statistika nad ni prestane byt cista a nikdo by si toho nemusel vsimnout.
    /// </summary>
    [TestCaseSource(nameof(RovneMapy))]
    public void Rovna_neniTamKrizovatkaAniOdbocka(string file, double lengthM, double widthM, double stepM)
    {
        var network = Network(file);

        // Jedna cesta -> hrany maji vsechny tentyz wayId.
        var ways = network.Edges.Select(e => e.WayId).Distinct().ToList();
        Assert.That(ways, Has.Count.EqualTo(1), $"{file}: rovna mapa ma mit presne jednu cestu");

        // Zadny uzel nesmi mit vic nez dva sousedy (jinak je to krizovatka).
        var degree = new Dictionary<long, int>();
        foreach (var e in network.Edges)
        {
            degree[e.From.Id] = degree.TryGetValue(e.From.Id, out int a) ? a + 1 : 1;
            degree[e.To.Id] = degree.TryGetValue(e.To.Id, out int b) ? b + 1 : 1;
        }
        // Obousmerna cesta je dve hrany, takze vnitrni uzel ma stupen 4, krajni 2.
        Assert.That(degree.Values.Max(), Is.LessThanOrEqualTo(4),
                    $"{file}: uzel s vyssim stupnem znamena krizovatku");
    }

    /// <summary>
    /// Stred obalky uzlu je tam, kde robot startuje (<c>ARBotRuntime.BuildOriginFromMap</c>).
    /// U kratke mapy na tom zalezi: hlavicka slibuje, ze start pada presne na uzel (x = 1 m)
    /// a ze pred robotem je prave polovina delky. Kdyby nekdo pridal uzel jen na jedne strane,
    /// stred by se posunul a slib v hlavicce by prestal platit.
    /// </summary>
    [TestCaseSource(nameof(RovneMapy))]
    public void Rovna_stredObalkyJeVPoloviceDelkyANaUzlu(string file, double lengthM, double widthM, double stepM)
    {
        var nodes = Nodes(file);
        double center = (nodes.Min(n => n.P.X) + nodes.Max(n => n.P.X)) / 2;

        Assert.That(center, Is.EqualTo(lengthM / 2).Within(0.001), $"{file}: stred obalky uzlu");
        Assert.That(nodes.Any(n => Math.Abs(n.P.X - center) < 0.001), Is.True,
                    $"{file}: ve stredu obalky (start robota) ma lezet uzel");
    }

    /// <summary>
    /// <b>Tohle je ten podstatny test posunute dvojnice:</b> posun musi byt u vsech uzlu TENTYZ
    /// vektor. Nahodny posun per uzel (jako v <c>SyntetickyKoridorPosunuty.osm</c>) je pro mereni
    /// korelace nepouzitelny — <c>MapCorrelator</c> hleda jedno 3-DOF <c>(dx, dy, fi)</c> na cely
    /// grid, takze nad deformovanou mapou nema jednu spravnou odpoved.
    /// </summary>
    [Test]
    public void SyntetickyRovnyPosunuty_jeTUHOUtranslaci()
    {
        var orig = Nodes("SyntetickyRovny.osm");
        var moved = Nodes("SyntetickyRovnyPosunuty.osm");

        Assert.That(moved, Has.Count.EqualTo(orig.Count), "posunuta mapa ma mit tytez uzly");

        Assert.Multiple(() =>
        {
            for (int i = 0; i < orig.Count; i++)
            {
                double dx = moved[i].P.X - orig[i].P.X;
                double dy = moved[i].P.Y - orig[i].P.Y;
                Assert.That(dx, Is.EqualTo(ShiftEastM).Within(0.001),
                            $"uzel {orig[i].Id}: posun na vychod neni {ShiftEastM} m");
                Assert.That(dy, Is.EqualTo(ShiftNorthM).Within(0.001),
                            $"uzel {orig[i].Id}: posun na sever neni {ShiftNorthM} m");
            }
        });
    }

    [Test]
    public void SyntetickyRovnyPosunuty_maStejnouGeometriiJakoOriginal()
    {
        var moved = Nodes("SyntetickyRovnyPosunuty.osm");

        // Posun nesmi zmenit tvar: porad rovna, porad 2 m, porad 160 m.
        Assert.Multiple(() =>
        {
            foreach (var n in moved)
                Assert.That(n.Width, Is.EqualTo(WidthM).Within(1e-9), $"uzel {n.Id}");
            for (int i = 0; i < moved.Count; i++)
                Assert.That(moved[i].P.Y, Is.EqualTo(ShiftNorthM).Within(0.001),
                            $"uzel {moved[i].Id}: posunuta cesta uz neni rovna");
            Assert.That(moved[moved.Count - 1].P.X - moved[0].P.X, Is.EqualTo(LengthM).Within(0.001));
        });
    }
}
