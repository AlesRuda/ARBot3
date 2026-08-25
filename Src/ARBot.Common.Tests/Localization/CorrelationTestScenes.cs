using System;
using ARBot.Common.Coordinates;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Spolecne synteticke sceny a generator gridu pro testy korelace s mapou
/// (viz doc/map-correlation-localization.md).
///
/// <para><b>Jak se testuje:</b> grid se naplni podle mapy, ale POSUNUTE a OTOCENE o znamou chybu.
/// Korelator pak musi tu chybu najit. Zadna vize, zadny HW.</para>
/// </summary>
internal static class CorrelationTestScenes
{
    /// <summary>Bunek na stranu testovaciho gridu (9,6 m pri 10 cm - drzi testy rychle).</summary>
    public const int GridSize = 96;

    /// <summary>Velikost bunky testovaciho gridu [m].</summary>
    public const double Resolution = 0.1;

    public static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>Jedna prima cesta podel osy X (na vychod), delka 60 m, stred v y = 0.</summary>
    public static RoadNetwork StraightEastRoad(GeoReference o, double width = 4.0)
    {
        var a = new Node(1, o.ToLLA(-30, 0), width);
        var b = new Node(2, o.ToLLA(30, 0), width);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 60.0, wayId: 1, traversalCost: 60.0);
        return builder.Build();
    }

    /// <summary>
    /// Prima cesta pod 45 stupni. Na ceste podel osy je vazba kurz-translace presne nulova; sikma
    /// cesta ji vyrobi, takze se da otestovat marginalizace v degradovane ceste kovariance.
    /// </summary>
    public static RoadNetwork DiagonalRoad(GeoReference o, double width = 4.0)
    {
        var a = new Node(30, o.ToLLA(-21, -21), width);
        var b = new Node(31, o.ToLLA(21, 21), width);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 59.4, wayId: 300, traversalCost: 59.4);
        return builder.Build();
    }

    /// <summary>
    /// T-krizovatka: prima cesta podel X a odbocka na SEVER z bodu (0,0). Odbocka lame podelnou
    /// symetrii - bez ni je poloha "podel cesty" nepodminena.
    /// </summary>
    public static RoadNetwork TJunction(GeoReference o, double width = 4.0)
    {
        var a = new Node(1, o.ToLLA(-30, 0), width);
        var b = new Node(2, o.ToLLA(30, 0), width);
        var c = new Node(3, o.ToLLA(0, 0), width);
        var d = new Node(4, o.ToLLA(0, 20), width);

        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, c, 30.0, wayId: 1, traversalCost: 30.0);
        builder.AddEdge(c, b, 30.0, wayId: 1, traversalCost: 30.0);
        builder.AddEdge(c, d, 20.0, wayId: 2, traversalCost: 20.0);
        return builder.Build();
    }

    /// <summary>
    /// Cesta s ohybem: lomena linie, ktera u pocatku zatoci k severovychodu. Ohyb lame podelnou
    /// symetrii mirneji nez odbocka - test, ze korelace zvlada i zakrivenou cestu.
    /// </summary>
    public static RoadNetwork CurvedRoad(GeoReference o, double width = 4.0)
    {
        var pts = new[] { (e: -30.0, n: 0.0), (e: -10.0, n: 0.0), (e: 0.0, n: 2.0), (e: 10.0, n: 8.0), (e: 25.0, n: 20.0) };

        // Uzly se vyrobi JEDNOU a mezi useky se sdileji (spolecny uzel = navazujici pas).
        var nodes = new Node[pts.Length];
        for (int k = 0; k < pts.Length; k++)
            nodes[k] = new Node(20 + k, o.ToLLA(pts[k].e, pts[k].n), width);

        var builder = new RoadNetwork.Builder();
        for (int k = 0; k + 1 < pts.Length; k++)
        {
            double de = pts[k + 1].e - pts[k].e, dn = pts[k + 1].n - pts[k].n;
            double len = Math.Sqrt(de * de + dn * dn);
            builder.AddEdge(nodes[k], nodes[k + 1], len, wayId: 200, traversalCost: len);
        }
        return builder.Build();
    }

    /// <summary>
    /// Soubezne cesty s rozestupem osy 2 m - vzor se OPAKUJE, takze posun o 2 m da konkurencni
    /// maximum skore skoro stejneho. Slouzi k testu nejednoznacnosti.
    ///
    /// <para><b>Proc jich je devet a ne tri:</b> pri trech se po posunu o rozestup namapuje vnejsi
    /// cesta do prazdna, shoda vyrazne klesne (mereno: konkurent jen 0,29 proti maximu 1,0) a scena
    /// nejednoznacnost NEVYROBI. Aby byl vzor v ramci gridu skutecne periodicky, musi cesty
    /// presahovat grid na obe strany - grid je 9,6 m, takze +-4 rozestupy staci s rezervou.
    /// Zjisteno integracnim testem 2026-08-19.</para>
    /// </summary>
    public static RoadNetwork ParallelRoads(GeoReference o, double width = 1.5, double spacing = 2.0,
                                            int halfCount = 4)
    {
        var builder = new RoadNetwork.Builder();
        for (int k = -halfCount; k <= halfCount; k++)
        {
            double y = k * spacing;
            var a = new Node(100 + 2 * (k + halfCount), o.ToLLA(-30, y), width);
            var b = new Node(101 + 2 * (k + halfCount), o.ToLLA(30, y), width);
            builder.AddEdge(a, b, 60.0, wayId: 200 + k, traversalCost: 60.0);
        }
        return builder.Build();
    }

    /// <summary>
    /// Naplni kanal Road podle mapy tak, jako by robot mel chybu pozy
    /// <paramref name="dx0"/>, <paramref name="dy0"/>, <paramref name="phi0"/>.
    ///
    /// <para>Model korelatoru: bunka, kterou robot vidi na ODHADOVANE pozici q, lezi ve skutecnosti
    /// na q' = R(phi0)*(q - p) + p + (dx0, dy0). Do gridu se tedy zapise, co mapa rika o q'.
    /// Spravna odpoved korelace je pak presne (dx0, dy0, phi0).</para>
    /// </summary>
    /// <param name="size">Bunek na stranu; vychozi <see cref="GridSize"/>.</param>
    /// <param name="resolution">Velikost bunky [m]; vychozi <see cref="Resolution"/>.
    /// <b>Nacpak parametr:</b> testy nezavislosti na rozliseni potrebuji TENTYZ vyrez sveta
    /// pokryty jinak hustou mrizi (napr. 96 bunek po 10 cm proti 192 po 5 cm), aby se dalo
    /// odlisit "vic informace" od "jen vic bunek". Viz honestni sigma
    /// v doc/map-correlation-localization.md.</param>
    public static OccupancyGridMsg GridFromScene(RoadScene scene, double robotX, double robotY,
                                                 double dx0, double dy0, double phi0,
                                                 int size = GridSize, double resolution = Resolution)
    {
        int originX = (int)Math.Floor(robotX / resolution) - size / 2;
        int originY = (int)Math.Floor(robotY / resolution) - size / 2;

        var msg = new OccupancyGridMsg
        {
            Size = size,
            Resolution = resolution,
            OriginX = originX,
            OriginY = originY,
            Scale = 0.05f,
            BlockedThreshold = 1.0f,
            FreeThreshold = -1.0f,
            Occ = new sbyte[size * size],
            Road = new sbyte[size * size],
            TimeStamp = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc),
        };

        double c = Math.Cos(phi0), s = Math.Sin(phi0);
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                double qx = msg.CenterX(i), qy = msg.CenterY(j);
                double rx = qx - robotX, ry = qy - robotY;
                double tx = robotX + dx0 + (c * rx - s * ry);
                double ty = robotY + dy0 + (s * rx + c * ry);

                // -1 = cesta, +1 = mimo cestu (log-odds NEPRUJEZDNOSTI).
                float logOdds = scene.IsRoad(tx, ty) ? -1.0f : 1.0f;
                msg.Road[i + j * size] = (sbyte)Math.Round(logOdds / msg.Scale);
            }
        }
        return msg;
    }

    /// <summary>
    /// Konfigurace pro testy: dve urovne skenovani misto tri. Nejjemnejsi krok 10 cm odpovida
    /// rozliseni testovaciho gridu - treti uroven by uz merila kvantizaci a testy jen zpomalila.
    ///
    /// <para><b>Honestni sigmu NEVYPINA</b>, takze testy jedou s tim, co se skutecne nasazuje
    /// (<see cref="MapCorrelatorConfig.ReferenceInformativeEvidence"/> = 37,5 od 25. 8. 2026 vecer).
    /// Dusledek: ABSOLUTNI sigmy uz nejsou tytez jako historicka cisla zapsana
    /// v doc/map-correlation-localization.md (napr. <c>SigmaLoose</c> 0,1848 m na sikme ceste) —
    /// ta se merila s konstantni <c>Alpha</c>. Kdo je chce zreprodukovat, nastavi
    /// <c>ReferenceInformativeEvidence = 0</c>. Testy same o absolutni sigmu neopiraji nic,
    /// tvrdi jen POMERY a smery — proto tahle zmena zadny z nich nerozbila.</para>
    /// </summary>
    public static MapCorrelatorConfig TestConfig()
        => new MapCorrelatorConfig
        {
            Levels = new[]
            {
                new ScanLevel { StepM = 0.40, StepHeadingRad = 4.0 * Math.PI / 180.0,
                                HalfRangeM = 2.0, HalfRangeHeadingRad = 8.0 * Math.PI / 180.0, Stride = 4 },
                new ScanLevel { StepM = 0.10, StepHeadingRad = 1.0 * Math.PI / 180.0,
                                HalfRangeM = 0.4, HalfRangeHeadingRad = 4.0 * Math.PI / 180.0, Stride = 1 },
            },
            MapRasterMarginM = 3.0,
        };

    /// <summary>
    /// Rastr mapy zarovnany s danou zpravou gridu.
    ///
    /// <para><b>Pozor - schvalne pouziva SUROVOU nakonfigurovanou marzi</b>, ne dopoctenou
    /// <c>MapCorrelatorConfig.RequiredRasterMarginM</c>, kterou od finalni review pouziva
    /// <c>MapCorrelator.Process</c>. Duvod: jednotkove testy skore a kovariance maji porovnatelna
    /// cisla s tim, co je zaznamenane v doc/map-correlation-localization.md (napr. SigmaLoose
    /// 0,1848 m na sikme ceste). Cena: regrese v RequiredRasterMarginM by tyhle testy nepocitily -
    /// produkcni cestu pokryva az MapCorrelatorTests. Viz doc/map-correlation-localization.md,
    /// sekce o marzi rastru.</para>
    /// </summary>
    public static RoadRaster RasterFor(RoadScene scene, OccupancyGridMsg msg, MapCorrelatorConfig cfg)
        => RoadRaster.Build(scene, msg.OriginX, msg.OriginY, msg.Size, msg.Resolution, cfg.MapRasterMarginM);
}
