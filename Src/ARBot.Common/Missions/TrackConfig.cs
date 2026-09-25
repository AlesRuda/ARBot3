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
        /// Timeout jizdy k jednomu mistu [s]; <c>0</c> = neomezovat (vychozi).
        ///
        /// <para><b>Vypnuto 26. 9. 2026 na pokyn autora</b> (driv 600 s, stejne jako u Robotouru je
        /// 0). Pevny strop nesedi na useky, ktere se mezi seznamy lisi radove: v Modranech
        /// (<c>20260925-142428.rec</c>) mela trasa k prvnimu mistu 1 118 m, tedy ~670 s pri
        /// 1,66 m/s, a mise se prerusila, i kdyz robot jel. Puvodni duvod („nikdy tiche
        /// zaseknuti" - robot zaklineny navzdy a mise hlasi „jede") plati dal, jen ho uz nekryje
        /// tenhle strop: zaseknuti hlidaji detektory v <c>GlobalNavigator</c> (bez pohybu, bez
        /// postupu, prehrazeno) a je videt na strance nahledu. Registr
        /// <c>mise-track-timeout-delka-useku</c>.</para>
        /// </summary>
        public double DrivingTimeoutSec = 0;

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
