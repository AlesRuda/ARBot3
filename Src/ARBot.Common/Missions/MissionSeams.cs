using System;
using ARBot.Common.Coordinates;
using ARBot.Common.Regulators;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// Inicializace polohove casti filtru. Uzke rozhrani nad <c>AsyncFusionEngine</c>, aby byl
    /// automat testovatelny bez fuze. Viz doc/robotour-mission.md → <c>ArmingAtDepot</c>.
    ///
    /// <para>Ze to dela mise a ne filtr, je zamer: „tomuhle fixu uz verim tak, ze podle nej postavim
    /// pocatek" je rozhodnuti te vrstvy, ktera vi, ze robot stoji v depu — ne vlastnost merici
    /// cesty.</para>
    /// </summary>
    public interface IPositionInitializer
    {
        /// <summary>Nastavi polohu stavu na (<paramref name="x"/>, <paramref name="y"/>) [m, ENU]
        /// s nejistotou <paramref name="std"/> [m] v case <paramref name="t"/>.</summary>
        void InitializePosition(double x, double y, double std, DateTime t);
    }

    /// <summary>
    /// Drzitel regulatoru (v aplikaci <c>ControlLoop</c>). Mise ho pouziva jen k <b>zahozeni</b>
    /// regulatoru, aby se pri stani nemohlo nic rozjet (<c>null</c> = stat, bezpecny stav).
    /// </summary>
    public interface IRegulatorHolder
    {
        /// <summary>Regulator, ktery nizsi smycka jede; <c>null</c> = stat.</summary>
        IRegulator Regulator { get; set; }
    }

    /// <summary>
    /// Vypinac scanneru QR (v aplikaci <c>QrScanner</c>). Mise ho zapina <b>vyhradne</b> ve stavu
    /// <see cref="RobotourPhase.Servicing"/>, tedy pod drzenym nouzovym zastavenim — robot tedy
    /// nikdy neskenuje, kdyz muze jet.
    /// </summary>
    public interface IQrScannerControl
    {
        /// <summary>Skenuje se?</summary>
        bool Enabled { get; set; }
    }

    /// <summary>Vysledek zkousky, jestli na cil vede po siti trasa.</summary>
    public readonly struct RouteProbeResult
    {
        public RouteProbeResult(bool reachable, double lengthM)
        {
            Reachable = reachable;
            LengthM = lengthM;
        }

        /// <summary>Vede na cil po siti trasa?</summary>
        public bool Reachable { get; }

        /// <summary>Delka nalezene trasy [m]; ukazuje se obsluze pred potvrzenim cile.</summary>
        public double LengthM { get; }
    }

    /// <summary>
    /// Zkouska dosazitelnosti cile v grafu cest, <b>bez</b> zmeny aktivniho cile navigace.
    ///
    /// <para>Delam se uz pri prijeti kodu, protoze jinak by se <c>NoRoute</c> zjistilo az za jizdy —
    /// a soucasne to da obsluze delku trasy, tedy udaj, podle ktereho lze cil zkontrolovat.</para>
    /// </summary>
    public interface IRouteProbe
    {
        /// <summary>Zkusi najit trasu na cil.</summary>
        RouteProbeResult Probe(LLA target);
    }
}
