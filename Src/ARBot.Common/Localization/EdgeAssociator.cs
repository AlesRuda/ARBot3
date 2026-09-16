using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Fusion;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Localization
{
    /// <summary>Jak dopadlo prirazeni koridoru k hrane site.</summary>
    public enum EdgeAssocResult : byte
    {
        /// <summary>Vitez je jednoznacny.</summary>
        Ok = 0,

        /// <summary>Mapa v okoli pozy zadnou cestu nema.</summary>
        NoEdge = 1,

        /// <summary>Vsichni kandidati jsou dal nez strop, nebo mimo veto azimutu / stropu chi2.</summary>
        NoCandidate = 2,

        /// <summary>Dva kandidati vysli podobne — nevime, po ktere ceste jedeme.</summary>
        Ambiguous = 3,
    }

    /// <summary>Vysledek prirazeni: vybrana osa, skore viteze a odstup od druheho.</summary>
    public readonly struct EdgeAssociation
    {
        public EdgeAssociation(EdgeAssocResult result, RoadAxisMatch axis, double chi2,
                               double chi2Second, int candidates)
        {
            Result = result; Axis = axis; Chi2 = chi2; Chi2Second = chi2Second;
            Candidates = candidates;
        }

        public EdgeAssocResult Result { get; }

        /// <summary>Vztah pozy k vybrane hrane (u neuspechu nejlepsi posuzovany, jinak prazdny).</summary>
        public RoadAxisMatch Axis { get; }

        /// <summary>Chi-kvadrat viteze; <see cref="double.NaN"/>, kdyz zadny nebyl.</summary>
        public double Chi2 { get; }

        /// <summary>Chi-kvadrat druheho kandidata; <see cref="double.NaN"/>, kdyz zadny nebyl.</summary>
        public double Chi2Second { get; }

        /// <summary>Kolik kandidatu se posuzovalo (po vetu azimutu).</summary>
        public int Candidates { get; }

        public bool Ok => Result == EdgeAssocResult.Ok;
    }

    /// <summary>
    /// Rozhodne, <b>po ktere ceste robot jede</b>: z nekolika nejblizsich hran vybere tu, ktera
    /// nejlip sedne na to, co vidi kamera.
    ///
    /// <para><b>Proc to neni „nejblizsi hrana".</b> Pri chybe polohy nekolika metru vyhraje
    /// u krizovatky <b>pricna ulice</b> — a nad <c>20260916-164926.rec</c> se to tyka
    /// <b>poloviny</b> cyklu (1 114 z 2 256 ma nesouhlas kurzu 60–90°). Pricnou vzdalenosti se to
    /// rozhodnout NEDA: to je prave ta velicina, kterou neznáme, takze by se rozhodovalo kruhem.
    /// Rozhoduje proto <b>azimut</b>, ktery na poloze nezavisi (<c>HeadingRelRad</c> je vztazeny
    /// ke KURZU robotu).</para>
    ///
    /// <para><b>Skore je Mahalanobisova vzdalenost</b>, ne linearni kombinace: metry a stupne se
    /// scitat nedaji, ale kdyz se kazda odchylka vydeli svoji sigmou, vyjde bezrozmerny
    /// chi-kvadrat se dvema stupni volnosti. Vahy tim zmizi a prah ma zname rozdeleni. Sigmy
    /// vstupuji ctyri: dve z kovariance pozy (<see cref="RobotState.Covariance"/> promitnuta do
    /// normaly hrany, resp. prvek kurzu) a dve z prolozeni koridoru — obe s <b>podlahou</b>, viz
    /// <see cref="EdgeAssociationConfig"/>.</para>
    ///
    /// <para>Viz doc/map-correlation-localization.md.</para>
    /// </summary>
    public static class EdgeAssociator
    {
        /// <summary>
        /// Vybere hranu pro dany koridor a pozu.
        /// </summary>
        /// <param name="network">Silnicni sit.</param>
        /// <param name="origin">Pocatek lokalni ENU roviny.</param>
        /// <param name="pose">Poza z fuze (kovariance smi byt <c>null</c> — pak plati podlahy).</param>
        /// <param name="corridor">Koridor videny kamerami.</param>
        /// <param name="cfg">Nastaveni prirazeni.</param>
        /// <param name="maxEdgeDistanceM">Nad timhle odstupem se hrana nebere za „tu nasi" [m].</param>
        public static EdgeAssociation Associate(RoadNetwork network, GeoReference origin,
                                                RobotState pose, RoadCorridor corridor,
                                                EdgeAssociationConfig cfg, double maxEdgeDistanceM)
        {
            var none = new EdgeAssociation(EdgeAssocResult.NoEdge, default, double.NaN, double.NaN, 0);
            if (network == null || origin == null || pose == null || corridor == null || cfg == null)
                return none;

            var candidates = network.NearestEdges(origin.ToLLA(pose.X, pose.Y), cfg.Candidates,
                                                  maxEdgeDistanceM);
            if (candidates.Count == 0) return none;

            double varThPose = Variance(pose, EKFModel.ITh, EKFModel.ITh);
            var fallbackAxis = default(RoadAxisMatch);
            bool haveFallback = false;

            // Sbiraji se HYPOTEZY, ne hrany: kolinearni sousedni segment tehoz kusu asfaltu je
            // v siti samostatna hrana, ale vede na tutéž osu, tedy na totez merenie. Kdyby
            // soutezil jako druhy kandidat, byla by remiza sam se sebou a test nejednoznacnosti
            // by zamitl kazdou rovnou cestu. Viz EdgeAssociationConfig.SameHypothesisLateralM.
            var hypotheses = new List<(RoadAxisMatch Axis, double Chi2)>(candidates.Count);

            foreach (var c in candidates)
            {
                var axis = RoadAxis.Relate(origin, c.Edge, c.T, c.DistanceM, pose.X, pose.Y, pose.Theta);
                if (!axis.Found) continue;
                if (!haveFallback) { fallbackAxis = axis; haveFallback = true; }

                // Rozdil smeru dvou PRIMEK - slozit na +-90 stupnu. Bez toho vyjde u cesty kolme
                // na kurz jedno cislo u +89 a druhe u -89, tedy rozdil 178 tam, kde je 2.
                double dHdg = Conversions.NormalizeHalfOrientation(corridor.DirectionRad - axis.HeadingRelRad);
                if (Math.Abs(dHdg) > cfg.VetoRad) continue;

                double dLat = corridor.Lateral - axis.Lateral;

                // Sigma pricne = kovariance pozy promitnuta do NORMALY hrany (ne sqrt(P_xx)).
                double varLatPose = Variance(pose, axis.NormalX, axis.NormalY);
                double varLat = Math.Max(varLatPose, Sq(cfg.SigmaLateralFloorM))
                                + Sq(corridor.SigmaLateral);
                double varHdg = Math.Max(varThPose, Sq(cfg.SigmaHeadingFloorRad))
                                + Sq(corridor.SigmaDirectionRad);
                if (varLat <= 0 || varHdg <= 0) continue;

                double chi2 = Sq(dLat) / varLat + Sq(dHdg) / varHdg;
                if (double.IsNaN(chi2)) continue;

                int same = hypotheses.FindIndex(h => SameHypothesis(h.Axis, axis, cfg));
                if (same >= 0)
                {
                    // Tatáz osa: nechat lepsi z nich (kandidati chodi od nejblizsiho, takze
                    // obvykle uz ten prvni).
                    if (chi2 < hypotheses[same].Chi2) hypotheses[same] = (axis, chi2);
                    continue;
                }
                hypotheses.Add((axis, chi2));
            }

            int judged = hypotheses.Count;
            hypotheses.Sort((h1, h2) => h1.Chi2.CompareTo(h2.Chi2));
            double bestChi = judged > 0 ? hypotheses[0].Chi2 : double.NaN;
            double secondChi = judged > 1 ? hypotheses[1].Chi2 : double.NaN;
            var bestAxis = judged > 0 ? hypotheses[0].Axis : default;

            if (double.IsNaN(bestChi) || bestChi > cfg.Chi2Max)
                return new EdgeAssociation(EdgeAssocResult.NoCandidate,
                                           haveFallback ? fallbackAxis : default,
                                           bestChi, secondChi, judged);

            // Nejednoznacnost: druhy kandidat je skoro stejne dobry. Vybrat toho o chlup lepsiho
            // by znamenalo hadat - radsi se neposle nic.
            if (!double.IsNaN(secondChi) && secondChi - bestChi < cfg.Chi2Margin)
                return new EdgeAssociation(EdgeAssocResult.Ambiguous, bestAxis, bestChi, secondChi, judged);

            return new EdgeAssociation(EdgeAssocResult.Ok, bestAxis, bestChi, secondChi, judged);
        }

        /// <summary>
        /// Vedou oba kandidati na <b>totez merenie</b>? Pak spolu nesoutezi — viz
        /// <see cref="EdgeAssociationConfig.SameHypothesisLateralM"/>.
        /// </summary>
        private static bool SameHypothesis(RoadAxisMatch a, RoadAxisMatch b, EdgeAssociationConfig cfg)
            => Math.Abs(a.Lateral - b.Lateral) <= cfg.SameHypothesisLateralM
            && Math.Abs(Conversions.NormalizeHalfOrientation(a.HeadingRelRad - b.HeadingRelRad))
               <= cfg.SameHypothesisHeadingRad;

        /// <summary>Rozptyl polohy ve smeru (ux, uy): <c>uᵀ P u</c>. Bez kovariance vraci 0.</summary>
        private static double Variance(RobotState pose, double ux, double uy)
        {
            var p = pose.Covariance;
            if (p == null || p.RowCount <= EKFModel.IY) return 0;
            return ux * ux * p[EKFModel.IX, EKFModel.IX]
                 + 2 * ux * uy * p[EKFModel.IX, EKFModel.IY]
                 + uy * uy * p[EKFModel.IY, EKFModel.IY];
        }

        /// <summary>Prvek kovariance, nebo 0, kdyz ji poza nenese.</summary>
        private static double Variance(RobotState pose, int i, int j)
        {
            var p = pose.Covariance;
            if (p == null || p.RowCount <= i || p.ColumnCount <= j) return 0;
            return p[i, j];
        }

        private static double Sq(double a) => a * a;
    }
}
