namespace ARBot.Common.Missions
{
    /// <summary>
    /// <b>Prevod stavu mise na text pro cloveka</b> — jedno misto pro webovy nahled, UI i rozbor
    /// zaznamu (<c>ARBot.Analyze</c>), aby tatáz situace nebyla pojmenovana trikrat jinak.
    ///
    /// <para><b>Proc to nejde do zpravy</b> (a <c>MissionMsg</c> se proto nemusela verzovat):
    /// „na co se ceka" je u Robotour <b>ciste funkce faze</b>, a tu zprava uz nese
    /// (<c>MissionMsg.Phase</c>). Ulozit vedle ni jeste odvozenou hodnotu by znamenalo dva zdroje
    /// pravdy v zaznamu — a hlavne by to platilo jen pro nove nahravky, zatimco takhle se ten radek
    /// da dopocitat i pro vsechny <b>starsi</b> zaznamy. Proto se prevod dela az u ctenare a bere
    /// <c>int</c>: stejne se vola nad zivou misi i nad prectenou zpravou.</para>
    /// </summary>
    public static class MissionStatusText
    {
        /// <summary>Jmeno mise Robotour ve tvaru parametru <c>mission=</c>.</summary>
        public const string Robotour = "robotour";

        /// <summary>Jmeno mise FreeRun ve tvaru parametru <c>mission=</c>.</summary>
        public const string FreeRun = "freerun";

        /// <summary>
        /// Na co ceka mise Robotour v dane fazi. <b>Kazda faze ma odpoved</b> — „na nic"
        /// (<see cref="MissionWait.None"/>) je taky odpoved, ale zadna faze nesmi propadnout
        /// nedopatrenim (hlida test nad vsemi hodnotami vyctu).
        /// </summary>
        public static MissionWait WaitFor(RobotourPhase phase) => phase switch
        {
            RobotourPhase.Idle => MissionWait.MissionStart,
            RobotourPhase.ArmingAtDepot => MissionWait.GpsFix,
            RobotourPhase.AwaitingEStop => MissionWait.EmergencyStopPressed,
            // Do Servicing se mise dostane VYHRADNE tam, kde se kod cte: kde se necte (vykladka),
            // jde AwaitingEStop rovnou na AwaitingEStopRelease (viz RobotourMission.OnMotors).
            RobotourPhase.Servicing => MissionWait.QrCode,
            RobotourPhase.AwaitingEStopRelease => MissionWait.EmergencyStopReleased,
            RobotourPhase.DrivingToPickup => MissionWait.Arrival,
            RobotourPhase.DrivingToDrop => MissionWait.Arrival,
            RobotourPhase.DrivingToDepot => MissionWait.Arrival,
            RobotourPhase.Finished => MissionWait.None,
            RobotourPhase.Aborted => MissionWait.None,
            _ => MissionWait.None,
        };

        /// <summary>Jako <see cref="WaitFor(RobotourPhase)"/>, ale z cisla ve zprave (<c>MissionMsg.Phase</c>).</summary>
        public static MissionWait WaitFor(int phase) => WaitFor((RobotourPhase)phase);

        /// <summary>
        /// Co mise Robotour v dane fazi dela — kratky nazev do hlavicky, ne veta.
        /// Neznama hodnota (novejsi zaznam, starsi ctenar) se prizna cislem, netvari se jako zname.
        /// </summary>
        public static string PhaseText(RobotourPhase phase) => phase switch
        {
            RobotourPhase.Idle => "necinna",
            RobotourPhase.ArmingAtDepot => "ukotvuje depo",
            RobotourPhase.AwaitingEStop => "stoji na stanovisti",
            RobotourPhase.Servicing => "servisni okno",
            RobotourPhase.AwaitingEStopRelease => "pripravena k odjezdu",
            RobotourPhase.DrivingToPickup => "jede na nakladku",
            RobotourPhase.DrivingToDrop => "jede na vykladku",
            RobotourPhase.DrivingToDepot => "jede do depa",
            RobotourPhase.Finished => "hotovo",
            RobotourPhase.Aborted => "preruseno",
            _ => "faze " + (int)phase,
        };

        /// <summary>Jako <see cref="PhaseText(RobotourPhase)"/>, ale z cisla ve zprave.</summary>
        public static string PhaseText(int phase) => PhaseText((RobotourPhase)phase);

        /// <summary>
        /// Text „na co se ceka" pro obsluhu. Prazdny retezec u <see cref="MissionWait.None"/> —
        /// stranka pak radek vubec neukaze, misto aby psala „na nic".
        /// </summary>
        public static string WaitText(MissionWait wait) => wait switch
        {
            MissionWait.None => string.Empty,
            MissionWait.MissionStart => "pokyn ke startu mise",
            MissionWait.GpsFix => "kvalitni fix GPS",
            MissionWait.EmergencyStopPressed => "stisknuti nouzoveho zastaveni",
            MissionWait.QrCode => "QR kod",
            MissionWait.EmergencyStopReleased => "uvolneni nouzoveho zastaveni",
            MissionWait.Arrival => "dojezd k cili",
            _ => "neznamy stav " + (int)wait,
        };
    }
}
