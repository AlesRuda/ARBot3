using System;
using ARBot.Common.Common;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Pozna, ze se poza zmenila VIC, nez vysvetli rychlost - tedy ze nekdo lokalizaci skokem
    /// prepsal (korekce z korelace s mapou, znovuzachyceni GPS, konvergence kurzu po startu).
    /// Grid je world-kotveny, takze po takovem skoku je jeho obsah na spatnem miste a je lepsi
    /// ho zahodit. Viz doc/map-correlation-localization.md ("Zpetna vazba na grid").
    ///
    /// <para>Hlida <b>posun i rotaci</b>. Rotace neni detail: grid je kotveny ve svete, takze
    /// otoceni pozy o <c>dTheta</c> posune jeho obsah az o <c>R * dTheta</c> - pri dohledu ~6 m
    /// staci par stupnu, aby se obsah posunul vic, nez povoluje translacni tolerance.</para>
    ///
    /// <para><b>Proc rotace pribyla</b> (19. 8. 2026): puvodne se hlidal jen posun. Po startu ale
    /// neni kurz v EKF inicializovany (<c>InitializePosition</c> nastavuje jen X/Y), takze jde od
    /// nuly ke skutecne hodnote. Robot pritom stoji, takze <c>moved = 0</c> a skok se nehlasil -
    /// grid si nechal snimky ulozene s obracenym kurzem a prvni korelace s mapou z nich vysla se
    /// spatnym znamenkem. Namereno na zaznamu 20260819-233057.rec.</para>
    ///
    /// <para>Male korekce (jednotky cm za cyklus) se schvalne NEHLASI - ty se vyperou samy diky
    /// clampu a kratke pameti gridu; resamplovat grid by je jen rozmazalo. Totez plati pro sum
    /// kurzu z filtru (namereno ~0,7 deg za 100 ms u stojiciho robotu).</para>
    /// </summary>
    public sealed class PoseJumpDetector
    {
        private bool hasPrevious;
        private double prevX;
        private double prevY;
        private double prevTheta;
        private DateTime prevTime;

        /// <summary>O kolik smi poza "pretect" nad to, co vysvetli rychlost, nez je to skok [m].</summary>
        public double ToleranceM { get; set; } = 0.5;

        /// <summary>
        /// O kolik smi kurz "pretect" nad to, co vysvetli <c>omega</c>, nez je to skok [rad].
        /// <para>Vychozich 5 deg je zvoleno tak, aby odpovidalo <see cref="ToleranceM"/>: pri dohledu
        /// ~6 m posune 5 deg obsah gridu prave o ~0,5 m. Zaroven je to ~7x nad namerenym sumem
        /// kurzu, takze grid se nezahazuje bezduvodne.</para>
        /// </summary>
        public double ToleranceRad { get; set; } = 5.0 * Math.PI / 180.0;

        /// <summary>Zapomene predchozi pozu (dalsi <see cref="Check"/> skok nehlasi).</summary>
        public void Reset() => hasPrevious = false;

        /// <summary>
        /// Zaznamena pozu a vrati <c>true</c>, kdyz je to skok.
        /// </summary>
        /// <param name="x">Poloha na vychod [m].</param>
        /// <param name="y">Poloha na sever [m].</param>
        /// <param name="theta">Orientace [rad], matematicky.</param>
        /// <param name="v">Rychlost ve smeru orientace [m/s] (znamenko nehraje roli).</param>
        /// <param name="omega">Uhlova rychlost [rad/s] (znamenko nehraje roli).</param>
        /// <param name="t">Cas, ke kteremu poza plati.</param>
        public bool Check(double x, double y, double theta, double v, double omega, DateTime t)
        {
            if (!hasPrevious)
            {
                Remember(x, y, theta, t);
                return false;
            }

            double dt = (t - prevTime).TotalSeconds;

            // Cas pozadu: snimky dvou kamer maji jine casy grabu a mohou prijit prehozene.
            // To neni skok pozy - jen se stav prepise a jede se dal.
            if (dt <= 0)
            {
                Remember(x, y, theta, t);
                return false;
            }

            double moved = Math.Sqrt((x - prevX) * (x - prevX) + (y - prevY) * (y - prevY));
            double explained = Math.Abs(v) * dt;

            // Normalizace je nutna: prechod pres +-180 deg je zmena o jednotky stupnu, ne o 360.
            // Bez ni by se skok hlasil pokazde, kdyz robot miri na zapad.
            double turned = Math.Abs(Conversions.NormalizeOrientation(theta - prevTheta));
            double explainedTurn = Math.Abs(omega) * dt;

            Remember(x, y, theta, t);

            return moved > explained + ToleranceM
                || turned > explainedTurn + ToleranceRad;
        }

        private void Remember(double x, double y, double theta, DateTime t)
        {
            prevX = x; prevY = y; prevTheta = theta; prevTime = t;
            hasPrevious = true;
        }
    }
}
