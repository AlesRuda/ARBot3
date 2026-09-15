using System;
using System.Collections.Generic;

namespace ARBot.Common.Maps.OsmNav.Graph
{
    /// <summary>
    /// Prekryv sirek cest: <c>nodeId → sirka [m]</c>. <b>NEMENNY</b> — predava se konzumentum
    /// (<see cref="RoadScene"/>, <see cref="RoadNetwork.ToLogMessage"/>) misto toho, aby se
    /// prepisoval graf.
    ///
    /// <para><b>Proc ne prepis grafu:</b> <c>Node.Width</c> je <c>get</c>-only a sit drzi krome
    /// korelatoru i <c>GlobalNavigator</c>, <c>RoadAxis</c>, <c>TrackMission</c> a navigacni pole.
    /// Vymena site za behu je zamena identity toho, PODLE CEHO ROBOT JEDE — dosah zmeny je
    /// nesrovnatelny s tim, co se ziska.</para>
    ///
    /// <para>Viz doc/plan-naucena-sirka-do-mapy.md.</para>
    /// </summary>
    public sealed class RoadWidthOverrides
    {
        private readonly Dictionary<long, double> sirky;

        /// <summary>Prazdny prekryv — konzument se s nim chova presne jako bez nej.</summary>
        public static readonly RoadWidthOverrides Prazdny =
            new RoadWidthOverrides(new Dictionary<long, double>());

        private RoadWidthOverrides(Dictionary<long, double> sirky) => this.sirky = sirky;

        /// <summary>Kolik uzlu prekryv nese (tedy u kolika se odhad lisi od mapy).</summary>
        public int Count => sirky.Count;

        /// <summary>Sirka uzlu z prekryvu; <c>false</c> = pouzij mapovou hodnotu.</summary>
        public bool TryGet(long nodeId, out double widthM) => sirky.TryGetValue(nodeId, out widthM);

        /// <summary>
        /// Slozi prekryv ze site a naucenych sirek per OSM way.
        ///
        /// <para><b>Sirka uzlu = maximum pres cesty, ktere jim vedou a MAJI ODHAD.</b> Maximum je
        /// tyz slucovaci vzorec, jaky pouziva <c>GraphBuilder</c> pri stavbe site — ale jen nad
        /// <b>zmerenymi</b> cestami. Cesta bez odhadu se hlasovani <b>neucastni</b>: jeji mapova
        /// sirka je DEFAULT (typicky <c>roadwidth</c>), tedy nepritomnost udaje, ne udaj.</para>
        ///
        /// <para>⚠️ <b>Tady byla vada</b> (naslo se 15. 9. 2026 na dvoumapovem rigu, vizualni mapa
        /// 2 m proti jizdni 3 m): puvodne prispivala i cesta bez odhadu, a to svou mapovou
        /// hodnotou. Naucene ZUZENI se tim na kazdem sdilenem uzlu prehlasilo — a protoze
        /// pulsirky segmentu se berou z jeho dvou KONCOVYCH UZLU, zustala cela naucena cesta
        /// siroka vsude, kde se dotyka jine cesty, tedy prakticky na cele siti. Navenek to
        /// vypadalo, ze se sirka neaktualizuje vubec.</para>
        ///
        /// <para>⚠️ <b>Znama mez, ktera plati dal:</b> sirku nese UZEL, ne cesta (rozhodnuti autora
        /// 15. 9. 2026), takze ve spolecnem uzlu se merena cesta a jeji soused nerozlisi — sirsi
        /// vyhraje. Nove to plati i opacnym smerem: zmerena uzka cesta zuzi uzel i sousedovi, ktery
        /// zmereny neni. Obojim smerem je to tataz vlastnost a lecbou by byla sirka per HRANA
        /// (a s ni <c>MapMsg</c> verze 2), ne uprava tohohle pravidla.</para>
        ///
        /// <para><b>Prekryv nese jen ROZDIL proti mape.</b> Uzly, kde vysla tataz hodnota jako
        /// v mape, se vypusti — diky tomu <see cref="Count"/> rika, kolik uzlu uz se opravdu
        /// naucilo, a prazdny prekryv je odlisitelny od „nic se nezmenilo".</para>
        /// </summary>
        /// <param name="network">Sit; <b>nemeni se</b>.</param>
        /// <param name="naucenaSirkaCesty">Pro <c>wayId</c> vrati duveryhodnou sirku, nebo
        /// <c>null</c>, kdyz zadnou nema.</param>
        public static RoadWidthOverrides Build(RoadNetwork network, Func<long, double?> naucenaSirkaCesty)
        {
            if (network == null) throw new ArgumentNullException(nameof(network));
            if (naucenaSirkaCesty == null) throw new ArgumentNullException(nameof(naucenaSirkaCesty));

            var mapove = new Dictionary<long, double>();
            var vysledek = new Dictionary<long, double>();

            foreach (var e in network.Edges)
            {
                // ⚠️ NEZMERENA cesta se maxima NEUCASTNI. Jeji mapova sirka je DEFAULT, ne dukaz,
                // a default nesmi prehlasit merenie.
                //
                // Do 15. 9. 2026 prispivala svou mapovou hodnotou - a naucene ZUZENI se tim na
                // kazdem sdilenem uzlu ztratilo: pulsirky segmentu se berou z jeho dvou koncovych
                // uzlu, takze cesta naucena jako uzsi zustala siroka vsude, kde se dotyka jine
                // cesty, tedy prakticky na cele siti. Naslo se to na dvoumapovem rigu (vizualni
                // mapa 2 m, jizdni 3 m), kde se neaktualizovalo nic viditelneho.
                // Drzi to RoadWidthOverridesTests.UzsiOdhadNaSdilenemUzlu_seNEZTRATI.
                double? naucena = naucenaSirkaCesty(e.WayId);
                if (!naucena.HasValue) continue;

                Prispej(e.From, naucena.Value, mapove, vysledek);
                Prispej(e.To, naucena.Value, mapove, vysledek);
            }

            if (vysledek.Count == 0) return Prazdny;

            foreach (var kv in mapove)
                if (vysledek.TryGetValue(kv.Key, out double w) && Math.Abs(w - kv.Value) < 1e-9)
                    vysledek.Remove(kv.Key);

            return vysledek.Count == 0 ? Prazdny : new RoadWidthOverrides(vysledek);
        }

        /// <summary>Zapocita prispevek jedne ZMERENE cesty do sirky uzlu (maximum).</summary>
        private static void Prispej(Node n, double naucena,
                                    Dictionary<long, double> mapove, Dictionary<long, double> vysledek)
        {
            if (!vysledek.TryGetValue(n.Id, out double cur) || naucena > cur)
                vysledek[n.Id] = naucena;
            mapove[n.Id] = n.Width;
        }
    }
}
