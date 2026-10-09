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
    /// <para>Male korekce (jednotky cm za cyklus) se schvalne NEHLASI - v zornem poli je nova
    /// pozorovani prepisi (clamp log-odds); resamplovat grid by je jen rozmazalo. Totez plati pro sum
    /// kurzu z filtru (namereno ~0,7 deg za 100 ms u stojiciho robotu).</para>
    ///
    /// <para>⚠️ <b>Mimo zorne pole se nevyperou</b> - grid nema casovy rozpad. Rada malych korekci pod
    /// toleranci (limit kroku koridoru <c>corridorslew=</c> je drzi pod ni zamerne) tak muze posunout
    /// pozu o metry proti obsahu gridu, aniz by detektor cokoli hlasil. Stalo se 1. 10. 2026
    /// (Track, <c>20261001-144638.rec</c>): 5-7 mereni koridoru po 0,17-0,24 m posunulo pozu za 5-9 s
    /// o 1,5-1,9 m do krajnice zapsane chvili predtim a robot stal v <c>RobotBlocked</c> 132 + 42 s.
    /// Lecbou je lokalni vrstva v odometricke soustave (<c>localframe=odom</c>), ne nizsi tolerance.</para>
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

        /// <summary>
        /// Kontrolovat skok i u pozy s casem POZADU (<c>dt &lt;= 0</c>)? Vychozi <c>true</c>.
        /// <para>Snimky dvou kamer maji jine casy grabu a chodi prehozene bezne. Do 1. 10. 2026 se
        /// u takoveho snimku poza jen zapamatovala a skok se nekontroloval - skok, ktery prisel
        /// prave na nej, se tim SPOLKL (dalsi snimek se uz porovnal s pozou po skoku) a grid se
        /// nesmazal. Pozorovano na Robotouru 19. 9. 2026: robot se skokem ocitl mimo sjizdnou
        /// oblast stare mapy a presel do uniku (<c>lok-skok-pozy-nedetekce</c>).</para>
        /// <para>Poza v case pozadu se posuzuje stejne jako dopredu, jen s <c>|dt|</c>: poza o
        /// <c>|dt|</c> drive se smi lisit o tolik, kolik vysvetli rychlost za <c>|dt|</c>.
        /// <c>false</c> = stare chovani (jen pro A/B v <c>ARBot.Analyze fusionreplay</c>).</para>
        /// </summary>
        public bool CheckBackwardTime { get; set; } = true;

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
            // Samotne prehozeni skok neni, ale skok, ktery na takovy snimek pripadne, se musi
            // poznat taky - jinak se spolkne (viz CheckBackwardTime). Posuzuje se s |dt|.
            if (dt <= 0)
            {
                if (!CheckBackwardTime)
                {
                    Remember(x, y, theta, t);
                    return false;
                }
                dt = -dt;
            }

            double moved = Math.Sqrt((x - prevX) * (x - prevX) + (y - prevY) * (y - prevY));
            double explained = Math.Abs(v) * dt;

            // Normalizace je nutna: prechod pres +-180 deg je zmena o jednotky stupnu, ne o 360.
            // Bez ni by se skok hlasil pokazde, kdyz robot miri na zapad.
            double turned = Math.Abs(Conversions.NormalizeOrientation(theta - prevTheta));
            double explainedTurn = Math.Abs(omega) * dt;

            bool backward = (t - prevTime).TotalSeconds <= 0;
            Remember(x, y, theta, t);

            bool posun = moved > explained + ToleranceM;
            bool kurz = turned > explainedTurn + ToleranceRad;
            if (posun || kurz)
                Describe(posun, kurz, moved, explained, turned, explainedTurn, dt, backward);
            return posun || kurz;
        }

        /// <summary>
        /// Druh posledniho skoku: <c>posun</c>, <c>kurz</c> nebo <c>posun+kurz</c>; <c>null</c> pred
        /// prvnim skokem. Slouzi jako klic skrceni hlaseni (jiny druh jde ven hned).
        /// </summary>
        public string LastJumpKind { get; private set; }

        /// <summary>
        /// Popis posledniho skoku pro Trace (lp-mazani-gridu-bez-stopy): velikost posunu a otoceni,
        /// kolik z nich vysvetli rychlost a za jak dlouho. Cisla s teckou (invariantni kultura),
        /// stejne jako ostatni hlasky v Trace.
        /// </summary>
        public string LastJumpDescription { get; private set; }

        private void Describe(bool posun, bool kurz, double moved, double explained,
                              double turned, double explainedTurn, double dt, bool backward)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            LastJumpKind = posun && kurz ? "posun+kurz" : posun ? "posun" : "kurz";
            var casti = new System.Collections.Generic.List<string>(2);
            if (posun)
                casti.Add(string.Format(ci, "posun {0:F2} m (rychlost vysvetli {1:F2} m)", moved, explained));
            if (kurz)
                casti.Add(string.Format(ci, "kurz {0:F1} deg (omega vysvetli {1:F1} deg)",
                                        Conversions.Rad2Deg(turned), Conversions.Rad2Deg(explainedTurn)));
            LastJumpDescription = "skok pozy - " + string.Join(", ", casti)
                                + string.Format(ci, " za {0:F2} s", dt)
                                + (backward ? " (cas pozadu - prehozene snimky)" : string.Empty);
        }

        private void Remember(double x, double y, double theta, DateTime t)
        {
            prevX = x; prevY = y; prevTheta = theta; prevTime = t;
            hasPrevious = true;
        }
    }
}
