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
        public RouteProbeResult(bool reachable, double lengthM,
                                LLA snappedTarget = null, double offRoadM = 0)
        {
            Reachable = reachable;
            LengthM = lengthM;
            SnappedTarget = snappedTarget;
            OffRoadM = offRoadM;
        }

        /// <summary>Vede na cil po siti trasa?</summary>
        public bool Reachable { get; }

        /// <summary>Delka nalezene trasy [m]; ukazuje se obsluze pred potvrzenim cile.</summary>
        public double LengthM { get; }

        /// <summary>
        /// Cil <b>prichyceny na sit</b> — kolmy prumet na nejblizsi hranu. <c>null</c>, kdyz se
        /// prichytit nepodarilo (zadna hrana, nebo zkouska bez site).
        ///
        /// <para><b>Proc se cil prichycuje:</b> souradnice z QR kodu je misto, kde stoji <i>clovek
        /// s krabici</i>, ne bod na ceste. Robot tam nemuze dojet — jede po siti — a
        /// <c>Navigator</c> pritom meri dojezd proti <c>GoalField.GoalPoint</c>, coz je
        /// <b>surovy</b> cil. Pri odsazeni vetsim nez <c>ArrivalRadiusMeters</c> (3 m) by tedy
        /// <c>Arrived</c> nenastalo NIKDY: robot by dojel na cestu, zastavil se u prumetu a cekal
        /// — a protoze jizda k cili nema timeout, cekal by porad. Mise proto jezdi na
        /// <b>prichyceny</b> cil.</para>
        /// </summary>
        public LLA SnappedTarget { get; }

        /// <summary>
        /// Jak daleko byl <b>surovy</b> cil od site [m] (vzdalenost k prumetu).
        ///
        /// <para>Zkouska sama tenhle udaj <b>neposuzuje</b> — limit je vec mise
        /// (<c>RobotourConfig.MaxTargetOffRoadM</c>), stejne jako vzdalenost od depa. Sit tu jen
        /// meri; „co je jeste prijatelne" je pravidlo ulohy, ne vlastnost grafu.</para>
        /// </summary>
        public double OffRoadM { get; }
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

    /// <summary>
    /// <b>Uzky sev pro cteni a zapis kalibrace VN100</b> (v aplikaci nad driverem).
    ///
    /// <para>⚠️ <b>Je uzky ZAMERNE.</b> Projekt ma vedome nakreslenou caru — „konfigurace senzoru
    /// se meni vedome a rucne, ne vedlejsim ucinkem nejakeho mereni" (hlavicky
    /// <c>deploy/vnprobe.sh</c> a <c>vnrestore.sh</c>). <b>Zamer te cary byl „zadny zapis bez
    /// rozhodnuti cloveka" a ten plati dal:</b> zapis se deje jen na tuknuti na tlacitko pod
    /// DRZENYM nouzovym zastavenim, coz je silnejsi gate nez ssh session. Meni se mechanismus,
    /// ne pravidlo.</para>
    ///
    /// <para>Pojistka proti erozi: sev umi <b>jen</b> registry 23 a 44 a cteni 21/23/44/47,
    /// a vola ho <b>jen</b> <see cref="MagCalMission"/>. Obecne „zapis jakykoli registr" by tu
    /// caru smazalo — <b>nezakladej ho.</b></para>
    ///
    /// <para><b>Proc jsou cisla registru tady a ne ve <c>VnCommands</c>:</b> mise zije
    /// v <c>ARBot.Common</c> a smer zavislosti je <c>Common ← HAL</c>, takze na <c>VnCommands</c>
    /// nevidi. Znalost protokolu zustava v HAL; sem patri jen ta cisla, ktera mise potrebuje
    /// pojmenovat.</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md a doc/decisions.md.</para>
    /// </summary>
    public interface IMagCalControl
    {
        /// <summary>Registr 21: referencni vektory pole a gravitace (odtud se bere <c>|B|</c>).</summary>
        public const int RegReference = 21;

        /// <summary>Registr 23: kompenzace magnetometru.</summary>
        public const int RegCompensation = 23;

        /// <summary>Registr 44: rizeni palubni HSI kalibrace.</summary>
        public const int RegCalControl = 44;

        /// <summary>Registr 47: kalibrace, kterou spocital SAM senzor (nezavisla kontrola).</summary>
        public const int RegCalculatedHsi = 47;

        /// <summary>Precte registr; <c>null</c> = nepodarilo se.</summary>
        double[] ReadRegister(int reg);

        /// <summary>Zapise kompenzaci do registru 23 (dvanact cisel s desetinnou teckou).</summary>
        bool WriteMagCompensation(string dvanactCisel);

        /// <summary>
        /// Zapne/vypne palubni HSI (registr 44) — <b>bez</b> aplikace, jen do registru 47.
        /// Je to nezavisla kontrola naseho prolozeni, ne druha kalibrace.
        ///
        /// <para>⚠️ <b>Vypnout se MUSI i pri nedokoncene misi.</b> TN002 kap. 5.2 uvadi
        /// „Mode = Run" primo mezi pricinami ujizdejiciho kurzu a rika, ze mimo kalibraci ma
        /// byt registr 44 vzdy vypnuty.</para>
        /// </summary>
        bool SetOnboardHsi(bool run);

        /// <summary>
        /// <b>Smaze</b> reseni palubni HSI (registr 44, Mode = Reset).
        ///
        /// <para>⚠️ Bez tohohle kroku nese registr 47 reseni z MINULE mise: podle ICD registru 44
        /// se pri prechodu Run → Off reseni <b>nemaze</b> a dalsi Run pokracuje ze stareho.
        /// Registr 47 pritom pouzivame jako <b>nezavislou kontrolu</b> naseho prolozeni — takze
        /// bez resetu by to nezavisla kontrola nebyla. TN002 kap. 4.1 to ma jako krok 1.</para>
        /// </summary>
        bool ResetOnboardHsi();

        /// <summary>
        /// Ulozi sadu registru do flash.
        /// <para>⚠️ Uspech NENI overitelny zpetnym ctenim (to cte z RAM) — skutecny test je az
        /// vypnuti a zapnuti robota.</para>
        /// </summary>
        bool SaveToFlash();
    }
}
