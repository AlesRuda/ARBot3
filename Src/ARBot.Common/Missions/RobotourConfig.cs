using System;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// Konfigurace mise <see cref="RobotourMission"/>. Viz doc/robotour-mission.md, sekce Parametry.
    /// </summary>
    public sealed class RobotourConfig
    {
        // ---------------- ArmingAtDepot: kvalita fixu ----------------

        /// <summary>
        /// Jak dlouho musi fix <b>neprerusene</b> vyhovovat, nez se jeho prumerem inicializuje fuze [s].
        ///
        /// <para>Start mise je jedine misto, kde robot stoji, ma cas a nikam nespecha — proto se
        /// tady dela nejdulezitejsi mereni cele jizdy.</para>
        /// </summary>
        public double DepotFixSec = 5.0;

        /// <summary>Nejmensi pocet satelitu, aby fix vyhovoval.</summary>
        public int MinSatellites = 6;

        /// <summary>Nejvyssi pripustny <c>Hdop</c>.</summary>
        public double MaxHdop = 2.0;

        /// <summary>
        /// Nejvyssi pripustny rozptyl fixu v okne [m] — <b>efektivni (RMS) odchylka od prumeru</b>,
        /// ne maximalni.
        ///
        /// <para>Robot stoji, takze rozptyl je <b>zdarma</b> dostupna kontrola kvality — a soucasne
        /// realisticka <c>std</c> pro filtr. Velky rozptyl znamena „cekej dal".</para>
        ///
        /// <para><b>Proc RMS a ne maximum:</b> maximum s rostoucim <c>n</c> roste i u dokonale
        /// gaussovskeho sumu, takze delsi cekani by kriterium <b>pritvrzovalo</b> — presne naopak,
        /// nez ma. RMS konverguje k sigma senzoru, takze prah je fyzikalne cteny udaj.</para>
        ///
        /// <para><b>2,5 m je nad nominalnim sumem, ne pod nim.</b> Spotrebni GPS ma ve stoje sigma
        /// radu metru (virtualni GPS simulace 1,5 m), takze prah musi normalni sum <b>propustit</b> —
        /// zamitat se maji jen abnormalni fixy (multipath skace o desitky metru). Prvni navrh mel
        /// 1,0 m a mise by se nezarmovala nikdy: ani v simulaci, ani na zarizeni.</para>
        /// </summary>
        public double MaxSpreadM = 2.5;

        /// <summary>
        /// Podlaha nejistoty, se kterou se inicializuje filtr [m].
        ///
        /// <para>Je potreba ze dvou duvodu: <c>InitializePosition</c> vyhodi na <c>std &lt;= 0</c>
        /// (a v simulaci muze byt rozptyl presne nulovy), a hlavne — samotny rozptyl okna
        /// <b>nezahrnuje systematickou chybu GPS</b>, takze by tvrdil vic jistoty, nez je pravda.</para>
        /// </summary>
        public double MinInitStdM = 0.3;

        // ---------------- Cteni kodu ----------------

        /// <summary>
        /// Po jake dobe ve servisnim okne se v UI hlasi „kod nevidim" [s]. <b>Skenuje se dal</b> —
        /// resenim je obsluha (posunout kod, prisunout robota), ne otocka robota, ktery ma pod
        /// rukama cloveka.
        /// </summary>
        public double QrSearchSec = 10.0;

        /// <summary>
        /// Nejvetsi pripustna vzdalenost cile z QR kodu od depa [m]. Sanity check: jedno chybne
        /// dekodovani muze poslat robota o stovky metru jinam.
        /// </summary>
        public double MaxTargetDistanceM = 2000.0;

        // ---------------- Timeouty ----------------

        /// <summary>
        /// Timeout stavu <see cref="RobotourPhase.ArmingAtDepot"/> [s]; <c>0</c> = neomezovat.
        /// Timeouty maji <b>jen stavy bez cloveka v cyklu</b>.
        /// </summary>
        public double ArmingTimeoutSec = 0;

        /// <summary>Timeout jizdy k cili [s]; <c>0</c> = neomezovat.</summary>
        public double DrivingTimeoutSec = 0;

        /// <summary>Perioda periodicke <c>MissionMsg</c> [s].</summary>
        public double MissionMessagePeriodSec = 1.0;

        /// <summary>Zkontroluje konzistenci; vyhodi <see cref="ArgumentException"/> pri chybe.</summary>
        public void Validate()
        {
            if (!(DepotFixSec > 0))
                throw new ArgumentException(
                    $"RobotourConfig.DepotFixSec ({DepotFixSec}) musi byt > 0; nula by znamenala "
                    + "'postav pocatek podle prvniho vzorku', a to je presne to, cemu se okno vyhyba.");
            if (MinSatellites < 4)
                throw new ArgumentException(
                    $"RobotourConfig.MinSatellites ({MinSatellites}) musi byt >= 4 — pod ctyrmi "
                    + "satelity neni fix urcen.");
            if (!(MaxHdop > 0))
                throw new ArgumentException($"RobotourConfig.MaxHdop ({MaxHdop}) musi byt > 0.");
            if (!(MaxSpreadM > 0))
                throw new ArgumentException($"RobotourConfig.MaxSpreadM ({MaxSpreadM}) musi byt > 0.");
            if (!(MinInitStdM > 0))
                throw new ArgumentException(
                    $"RobotourConfig.MinInitStdM ({MinInitStdM}) musi byt > 0 — filtr na nulove "
                    + "std vyhodi vyjimku.");
            if (!(MaxTargetDistanceM > 0))
                throw new ArgumentException(
                    $"RobotourConfig.MaxTargetDistanceM ({MaxTargetDistanceM}) musi byt > 0.");
            if (ArmingTimeoutSec < 0 || DrivingTimeoutSec < 0)
                throw new ArgumentException("Timeouty nesmi byt zaporne; nula = neomezovat.");
            if (!(MissionMessagePeriodSec > 0))
                throw new ArgumentException(
                    $"RobotourConfig.MissionMessagePeriodSec ({MissionMessagePeriodSec}) musi byt > 0.");
        }
    }
}
