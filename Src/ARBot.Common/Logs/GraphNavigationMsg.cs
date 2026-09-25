using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava: <b>graf navigace k zobrazeni a do zaznamu</b> - vrcholy, hrany mezi nimi
    /// a tri znacky (start / cil / vysledek). Je to zamerne OBECNY kontejner: dnes ho plni jen
    /// globalni navigace nad OsmNav, ale drive i starsi navigatory (Voronoi, grid, Dijkstra nad
    /// <c>Maps.Map</c>), takze <b>vyznam poli zavisi na producentovi</b> - viz tabulka nize.
    /// Ve world pohledu se kresli jako vrstva „Trasa+graf" a kazda hrana ma tooltip
    /// (viz doc/world-view.md); rozhodovaci stav globalni navigace nese zvlast
    /// <see cref="GlobalNavMsg"/> (mala zprava kazdy cyklus), tato je vetsi a chodi ridceji.
    ///
    /// <para><b>Producenti a co ktery plni</b> (co neni uvedeno, zustava na defaultu):</para>
    /// <list type="table">
    ///   <listheader><term>Producent</term><description>Souradnice / hrany / vrcholy</description></listheader>
    ///   <item>
    ///     <term><c>GlobalNavigator.BuildRouteMessage</c> (OsmNav, dnesni runtime)</term>
    ///     <description>Souradnice v <b>lokalnim ENU [m]</b>. Start = poloha robotu, Target = cil,
    ///     Result = mrkev. Hrany trasy: <c>ID</c> = OSM <c>WayId</c>, <c>Length</c> = metricka delka,
    ///     <c>HightLight</c> = <c>Path</c> = true. Uzavrene/penalizovane hrany se pridavaji zvlast
    ///     s <c>Collision</c> = <c>Graph</c> = true. Vrcholy: <c>ID</c> = OSM id uzlu,
    ///     <c>Width</c> = sirka cesty; <c>Distance</c> se NEPOCITA
    ///     (<c>DistanceCalculated</c> = false).</description>
    ///   </item>
    ///   <item>
    ///     <term>Nasledujici producenti byli 25. 9. 2026 smazani (mrtvy kod z ARBot2); konvence
    ///     zustavaji popsane kvuli pripadnym starsim zaznamum.</term>
    ///     <description></description>
    ///   </item>
    ///   <item>
    ///     <term>Konstruktor nad <c>Maps.Map</c> (Dijkstra)</term>
    ///     <description><c>Name</c> = „Map". Souradnice vrcholu jsou <b>slozky ECEF</b>
    ///     (X = <c>Position.Y</c>, Y = <c>Position.Z</c>) - stara konvence, NE lokalni ENU.
    ///     <c>Length</c> hrany je <b>vaha</b> (<c>WeigthDistance</c>), ne metry. Vrcholy nesou
    ///     vysledek Dijkstry: <c>Distance</c> = vzdalenost uzlu k cili, <c>Final</c> = uzel je
    ///     uzavreny, <c>DistanceCalculated</c> = hodnota uz je spoctena.</description>
    ///   </item>
    ///   <item>
    ///     <term><c>VoronoiNavigation.ToGraphMsg</c></term>
    ///     <description>Vrcholy z Voronoi diagramu (<c>Distance</c>/<c>Final</c>/
    ///     <c>DistanceCalculated</c> vyplnene, bez <c>ID</c> a <c>Width</c>), hrany s
    ///     <c>Collision</c> = kolize se segmentem a <c>Path</c> = true.</description>
    ///   </item>
    ///   <item>
    ///     <term><c>GridNavigationBase.ToGrapMsg</c></term>
    ///     <description>Synteticka lomena cara z bodu cesty. POZOR: <c>Distance</c> vrcholu je tu
    ///     kumulativni vzdalenost <b>od zacatku</b>, ne k cili - ale
    ///     <c>DistanceCalculated</c> zustava false, takze se hodnota nikde neinterpretuje.</description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>Verze formatu:</b> <c>2</c> (produkcni konstruktory). Rozdil proti <c>1</c> je jediny -
    /// v1 nema u hrany priznak <see cref="Edge.HightLight"/>, takze se ani nezapisuje, ani necte
    /// (viz vetve <c>if (Verze == 2)</c> v <see cref="ToData"/> / <see cref="FromData"/>). Starsi
    /// zaznamy se tim prehraji, jen v nich neni zvyraznena cesta. <b>Odchylka od pravidla</b> v
    /// doc/record-replay.md: bezparametrovy konstruktor (prototyp pro katalog) hlasi verzi 1, ne
    /// aktualni 2 - pri cteni to nevadi (<c>MessageReader</c> verzi prepise podle ramce), ale
    /// rucne slozena zprava z tohoto konstruktoru by se zapsala ve starem formatu.</para>
    ///
    /// <para><b>Pasti:</b> bezparametrovy konstruktor nechava <see cref="Vertexes"/> i
    /// <see cref="Edges"/> <c>null</c> (slouzi jen jako prototyp pro <see cref="Build"/>) - k
    /// naplneni pouzij konstruktor se seznamy. <see cref="Edge"/> si drzi odkaz na rodicovskou
    /// zpravu kvuli <see cref="Edge.Line"/> a <c>ToString</c>; nekteri producenti predavaji
    /// <c>null</c>, pak obe vraci neuplny vysledek. <see cref="Name"/> NENI
    /// <c>INamedMessage.Name</c>, takze se neobjevi ve sloupci „Jmeno" v indexu zaznamu.</para>
    /// </summary>
    public class GraphNavigationMsg:Message
    {
        /// <summary>Vrchol grafu (uzel site / bod cesty). Souradnicova soustava zavisi na
        /// producentovi - viz tabulka u <see cref="GraphNavigationMsg"/>.</summary>
        public class Vertex
        {
            /// <summary>Identita uzlu (u OsmNav id uzlu z OSM); 0 = producent ji neplni.</summary>
            public long ID { get; set; }
            /// <summary>Poloha uzlu, prvni osa (lokalni ENU „na vychod" [m], nebo ECEF.Y u starsi cesty).</summary>
            public double X { get; set; }
            /// <summary>Poloha uzlu, druha osa (lokalni ENU „na sever" [m], nebo ECEF.Z u starsi cesty).</summary>
            public double Y { get; set; }
            /// <summary>Sirka cesty v uzlu [m] (0 = neznama). Kresli se z ni pas cesty.</summary>
            public double Width { get; set; }
            /// <summary>Vzdalenost uzlu k cili z prohledavani; plati jen pri
            /// <see cref="DistanceCalculated"/>.</summary>
            public double Distance { get; set; }
            /// <summary>Uzel je v prohledavani <b>uzavreny</b> - <see cref="Distance"/> uz se nezmeni.
            /// Bez toho je to jen dosavadni (predbezny) odhad.</summary>
            public bool Final { get; set; }
            /// <summary>Ma <see cref="Distance"/> vubec smysl? Producenti, kteri pole nepocitaji,
            /// ho nechavaji <c>false</c>.</summary>
            public bool DistanceCalculated { get; set; }

            public override string ToString()
            {
                return string.Format("{5}\r\nPos: [{0:N3}, {1:N3}]\r\nDist: {2:N3}\r\nFinal: {3}\r\nDistCalc: {4}", X, Y, Distance, Final, DistanceCalculated, ID);
            }
        }
        /// <summary>
        /// Hrana mezi dvema vrcholy. Priznaky <see cref="HightLight"/> / <see cref="Path"/> /
        /// <see cref="Graph"/> / <see cref="Collision"/> nejsou vylucne - popisuji ROLI hrany
        /// a producent jich muze nastavit vic (napr. trasa = <c>HightLight</c> + <c>Path</c>,
        /// uzavrena hrana = <c>Collision</c> + <c>Graph</c>).
        /// </summary>
        public class Edge
        {
            /// <param name="p">Rodicovska zprava - jen kvuli <see cref="Line"/> a <c>ToString</c>,
            /// ktere potrebuji dohledat vrcholy podle indexu. <c>null</c> je pripustne (nekteri
            /// producenti ho nepredavaji), pak obe vraci neuplny vysledek.</param>
            public Edge(GraphNavigationMsg p)
            {
                parent = p;
            }

            GraphNavigationMsg parent;
            /// <summary>Identita hrany (u OsmNav <c>WayId</c> z OSM - tedy CELA cesta, ne jen tento
            /// usek; jedno <c>WayId</c> proto muze byt na vic hranach); 0 = producent ji neplni.</summary>
            public long ID { get; set; }
            /// <summary>Zvyraznena hrana = cesta, po ktere se prave jede. <b>Jen ve verzi 2</b>
            /// (v zaznamech verze 1 chybi a zustava <c>false</c>).</summary>
            public bool HightLight;
            /// <summary>Index pocatecniho vrcholu do <see cref="Vertexes"/>; -1 = nenalezeno.</summary>
            public int From; //-1 nenalezeno
            /// <summary>Index koncoveho vrcholu do <see cref="Vertexes"/>; -1 = nenalezeno.</summary>
            public int To; //-1 nenalezeno
            /// <summary>Delka hrany - u OsmNav v metrech, u starsi cesty pres <c>Maps.Map</c> je to
            /// <b>vaha</b> (<c>WeigthDistance</c>). Viz tabulka producentu u
            /// <see cref="GraphNavigationMsg"/>.</summary>
            public double Length;
            /// <summary>Hrana je neprujezdna: u starsich navigatoru detekovana kolize, u
            /// <c>GlobalNavigator</c> <b>uzavrena / penalizovana</b> hrana, ktere se robot vyhyba.</summary>
            public bool Collision;
            /// <summary>Hrana patri do navrzene trasy k cili.</summary>
            public bool Path;
            /// <summary>Hrana patri do prohledavaneho grafu (kontext kolem trasy).</summary>
            public bool Graph;
            /// <summary>Geometrie hrany z vrcholu rodicovske zpravy; <c>null</c>, kdyz rodic chybi
            /// nebo indexy <see cref="From"/>/<see cref="To"/> mimo seznam.</summary>
            public Line2D Line
            {
                get
                {
                    if (parent != null)
                    {
                        if (parent.Vertexes.Count > Math.Max(From, To))
                        {
                            Vertex v1 = parent.Vertexes[From];
                            Vertex v2 = parent.Vertexes[To];
                            return new Line2D(new Point2D(v1.X, v1.Y), new Point2D(v2.X, v2.Y));
                        }
                    }
                    return null;
                }
            }
            public override string ToString()
            {
                double? w=null;
                double? a=null;
                if (parent != null)
                {
                    if (parent.Vertexes.Count > Math.Max(From, To))
                    {
                        Vertex v1 = parent.Vertexes[From];
                        Vertex v2 = parent.Vertexes[To];
                        var l = new Line2D(new Point2D(v1.X, v1.Y), new Point2D(v2.X, v2.Y));
                        a = Conversions.Rad2Deg(Conversions.Orientation2Azimut(l.Angle));
                        w = (v1.Width + v2.Width) / 2;
                    }
                }
                return string.Format(@"{3}
Len: {0:N3}
Angle: {1:N1}
Width: {2:N3}", Length, a, w, ID);
            }
        }

        /// <summary>Vrcholy grafu. Hrany na ne odkazuji INDEXEM do tohoto seznamu, takze poradi
        /// je soucast dat. <c>null</c> po bezparametrovem konstruktoru.</summary>
        public List<Vertex> Vertexes { get; private set; }

        /// <summary>Hrany grafu. <c>null</c> po bezparametrovem konstruktoru.</summary>
        public List<Edge> Edges { get; private set; }

        /// <summary>Znacka <b>start</b> - odkud se navigovalo (u <c>GlobalNavigator</c> poloha robotu).</summary>
        public double StartX, StartY;

        /// <summary>Znacka <b>cil</b> - zadany cil navigace.</summary>
        public double TargetX, TargetY;

        /// <summary>Znacka <b>vysledek</b> - bod predany nizsi vrstve (u <c>GlobalNavigator</c>
        /// „mrkev" pro lokalni planovac); <c>null</c>, kdyz zadny nevznikl.</summary>
        public double? ResultX, ResultY;

        /// <summary>
        /// Nazev zaznamu - rozlisuje, ktery producent zpravu poslal (napr. „Map"). Pozor, NENI to
        /// <c>INamedMessage.Name</c>, takze se neobjevi ve sloupci „Jmeno" v indexu zaznamu.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>Prototyp pro katalog zprav a <see cref="Build"/>. Nechava
        /// <see cref="Vertexes"/> i <see cref="Edges"/> <c>null</c> a hlasi verzi 1 - k plneni
        /// pouzij konstruktor se seznamy.</summary>
        public GraphNavigationMsg() : base("GN", 1)
        {
        }

        /// <summary>Zprava slozena volajicim z hotovych vrcholu a hran (aktualni verze formatu).</summary>
        /// <param name="startX">Znacka start, prvni osa.</param>
        /// <param name="startY">Znacka start, druha osa.</param>
        /// <param name="targetX">Znacka cil, prvni osa.</param>
        /// <param name="targetY">Znacka cil, druha osa.</param>
        /// <param name="resultX">Znacka vysledek (mrkev), nebo <c>null</c>.</param>
        /// <param name="resultY">Znacka vysledek (mrkev), nebo <c>null</c>.</param>
        /// <param name="vertexes">Vrcholy; hrany na ne odkazuji indexem.</param>
        /// <param name="edges">Hrany.</param>
        public GraphNavigationMsg(double startX, double startY, double targetX, double targetY,
            double? resultX, double? resultY, List<Vertex> vertexes, List<Edge> edges) : base("GN", 2)
        {
            StartX = startX;
            StartY = startY;
            TargetX = targetX;
            TargetY = targetY;
            ResultX = resultX;
            ResultY = resultY;

            Vertexes = vertexes;
            Edges = edges;
        }

        /// <summary>Zapis do zaznamu. Priznak <see cref="Edge.HightLight"/> se zapisuje jen ve
        /// verzi 2 - viz poznamka o verzich u <see cref="GraphNavigationMsg"/>.</summary>
        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Name ?? "GN");

            bw.Write(StartX);
            bw.Write(StartY);
            bw.Write(TargetX);
            bw.Write(TargetY);
            Write(bw, ResultX);
            Write(bw, ResultY);

            bw.Write(Vertexes.Count);
            for (int i = 0; i < Vertexes.Count; i++)
            {
                bw.Write(Vertexes[i].ID);
                bw.Write(Vertexes[i].X);
                bw.Write(Vertexes[i].Y);
                bw.Write(Vertexes[i].Width);
                bw.Write(Vertexes[i].Distance);
                bw.Write(Vertexes[i].Final);
                bw.Write(Vertexes[i].DistanceCalculated);
            }

            bw.Write(Edges.Count);
            for (int i = 0; i < Edges.Count; i++)
            {
                bw.Write(Edges[i].ID);
                if(Verze==2)
                    bw.Write(Edges[i].HightLight);
                bw.Write(Edges[i].From);
                bw.Write(Edges[i].To);
                bw.Write(Edges[i].Length);
                bw.Write(Edges[i].Collision);
                bw.Write(Edges[i].Path);
                bw.Write(Edges[i].Graph);
            }
        }

        /// <summary>Cteni ze zaznamu. <c>Verze</c> uz je nastavena podle hlavicky ramce, takze v1
        /// zaznam se precte bez <see cref="Edge.HightLight"/> (zustane <c>false</c> = zadna hrana
        /// neni zvyraznena).</summary>
        public override void FromData(BinaryReader br)
        {
            Name = br.ReadString();
            StartX = br.ReadDouble();
            StartY = br.ReadDouble();
            TargetX = br.ReadDouble();
            TargetY = br.ReadDouble();
            ResultX = ReadDouble(br);
            ResultY = ReadDouble(br);

            int cnt = br.ReadInt32();
            Vertexes = new List<Vertex>();

            for (int i = 0; i < cnt; i++)
            {
                double x, y, d, w = 0;
                bool f, dc;
                long id = 0;

                id = br.ReadInt64();

                x = br.ReadDouble();
                y = br.ReadDouble();
                w = br.ReadDouble();
                d = br.ReadDouble();
                f = br.ReadBoolean();
                dc = br.ReadBoolean();

                Vertexes.Add(new Vertex() { X = x, Y = y, Distance = d, Final = f, DistanceCalculated = dc, Width = w, ID = id });
            }

            cnt = br.ReadInt32();
            Edges = new List<Edge>();
            for (int i = 0; i < cnt; i++)
            {
                int f, t;
                double l;
                bool c, p, g;
                long id = 0;
                bool hl = false;

                id = br.ReadInt64();

                if (Verze == 2)
                    hl = br.ReadBoolean();

                f = br.ReadInt32();
                t = br.ReadInt32();
                l = br.ReadDouble();
                c = br.ReadBoolean();
                p = br.ReadBoolean();
                g = br.ReadBoolean();

                Edges.Add(new Edge(this) { From = f, To = t, Length = l, Collision = c, Path = p, Graph = g, ID = id, HightLight=hl });
            }
        }

        public override Message Build()
        {
            return new GraphNavigationMsg();
        }

        public override string ToString()
        {
            return string.Format("GraphNavigation {0}", Name);
        }

    }
}
