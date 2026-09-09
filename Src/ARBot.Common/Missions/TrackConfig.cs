using System;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// Konfigurace mise <see cref="TrackMission"/>. Viz doc/track-mission.md.
    /// </summary>
    public sealed class TrackConfig
    {
        /// <summary>
        /// Nejvetsi pripustny <b>odstup mista od site cest</b> [m] — jak daleko smi lezet bod ze
        /// souboru od nejblizsi hrany, aby se na ni jeste smel prichytit.
        ///
        /// <para><b>Proc limit vubec je:</b> prichyceni (<c>NearestEdge</c>) zadny limit nema,
        /// takze prichytit jde <b>cokoliv</b> — bod uprostred pole 300 m od silnice se prichyti
        /// k te silnici a vyjde jako dosazitelny. Robot by pak odjel nekam uplne jinam, nez clovek
        /// v souboru zadal, a <b>ohlasil dojezd</b>. Limit je to, co dela z prichyceni kontrolu.
        /// Tatáz uvaha jako u <see cref="RobotourConfig.MaxTargetOffRoadM"/>.</para>
        ///
        /// <para>⚠️ <b>50 m je usudek, ne merena hodnota</b>, a je volnejsi nez u Robotouru (15 m)
        /// zamerne: bod z QR kodu je misto, kde stoji clovek s krabici <i>u cesty</i>, kdezto bod
        /// v souboru <c>.track</c> si clovek klikl na mape — treba do stredu krizovatky nebo na
        /// roh budovy. Odstup se meri do zaznamu, takze se da nastavit z dat.</para>
        /// </summary>
        public double MaxPointOffRoadM = 50.0;

        /// <summary>
        /// Timeout jizdy k jednomu mistu [s]; <c>0</c> = neomezovat.
        ///
        /// <para><b>Nikdy tiche zaseknuti:</b> jizda k cili sama timeout nema, takze bez tohohle
        /// stropu by robot, ktery se nekam zaklinil, stal <b>navzdy</b> a mise by dal hlasila
        /// „jede". Zotavovaci manevr neexistuje, takze jedina bezpecna odpoved je zastavit
        /// a rict to.</para>
        /// </summary>
        public double DrivingTimeoutSec = 600.0;

        /// <summary>Jak casto se posila <c>TrackMsg</c> [s] — do webu i do zaznamu.</summary>
        public double MessagePeriodSec = 1.0;

        /// <summary>Zkontroluje konzistenci; vyhodi <see cref="ArgumentException"/> pri chybe.</summary>
        public void Validate()
        {
            if (!(MaxPointOffRoadM > 0))
                throw new ArgumentException(
                    $"TrackConfig.MaxPointOffRoadM ({MaxPointOffRoadM}) musi byt > 0. Nula by "
                    + "znamenala, ze bod musi lezet PRESNE na ose cesty, coz clovek na mape "
                    + "netrefi nikdy.");
            if (DrivingTimeoutSec < 0)
                throw new ArgumentException(
                    $"TrackConfig.DrivingTimeoutSec ({DrivingTimeoutSec}) nesmi byt zaporny "
                    + "(0 = neomezovat).");
            if (!(MessagePeriodSec > 0))
                throw new ArgumentException(
                    $"TrackConfig.MessagePeriodSec ({MessagePeriodSec}) musi byt > 0.");
        }
    }
}
