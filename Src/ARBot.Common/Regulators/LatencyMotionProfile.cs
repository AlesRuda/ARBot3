using System;

namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Spojitý kinematický profil <b>se zpožděním řídicí smyčky</b> <c>L</c>: zásah je nejvyšší
    /// rychlost, ze které se robot ještě stihne zastavit (resp. zpomalit na <c>v_e</c>), když se
    /// brzdit začne až za <c>L</c>. Viz <c>doc/path-following.md</c>, „Profil se zpožděním smyčky".
    ///
    /// <para><b>Zákon.</b> Podmínka „nejdřív ujedu <c>v·L</c>, pak brzdím konstantní decelerací"
    /// <c>v·L + (v² − v_e²)/(2a) ≤ x</c> dává</para>
    /// <code>v = max(v_e, −a·L + √((a·L)² + 2a·x + v_e²))</code>
    /// <para>oříznuto na <see cref="MaxSpeed"/>. Daleko od cíle je to <c>√(2a·x)</c> (časově
    /// optimální brzdná křivka), u cíle <c>v ≈ x/L</c>, tedy <b>konečné zesílení <c>1/L</c></b>:
    /// časově optimální zákon bez zpoždění má u nuly zesílení nekonečné a se skutečným zpožděním
    /// akčního členu kmitá. <c>max(v_e, …)</c> je tam proto, že robot jedoucí nejvýš <c>v_e</c>
    /// brzdit nemusí, takže člen <c>v·L</c> se ho netýká; obě větve se potkají v <c>x = v_e·L</c>.</para>
    ///
    /// <para><b>Proč ne <see cref="TrapezoidMotionProfile"/>.</b> Jeho diskrétní vzorec plánuje
    /// trojúhelník „zrychli z aktuální rychlosti, pak zabrzdi" a vrací jeho vrchol, ačkoli rampu
    /// dělá motorová jednotka sama. Důsledky naměřené a spočtené 25. 9. 2026: pevný bod při stálé
    /// vzdálenosti mrkve hluboko pod bezpečnou rychlostí (1,4 m → 0,855 m/s, 3 m → 1,305 m/s),
    /// kvantování po 0,045 m/s resp. 0,22 rad/s a hlavně <b>žádné plné brzdění</b>, když už robot
    /// zastavit nestihne — u rotace z toho je mezní cyklus (změna znaménka ω ~3× za sekundu).
    /// Rozbor a simulace: doc/ukoly.md <c>lp-regulator-kmitani-rotace</c>.</para>
    ///
    /// <para><b><c>startSpeed</c> zákon nepotřebuje</b> — příkaz je strop, rampu k němu z aktuální
    /// rychlosti (i plné brzdění, když je strop pod ní) dělá motorová jednotka. Rozhraní to
    /// připouští stejně jako u <see cref="SqrtMotionProfile"/>.</para>
    ///
    /// <para><b>Doba dorovnání rotace</b> (<see cref="RegulatorResult.RegulationTime"/> z
    /// <see cref="Rot2RotSpeed"/>, ze které <see cref="SpeedLimit"/> srazí dopřednou rychlost) je doba
    /// časově optimálního natočení o <c>β</c> <b>z klidu</b>: u nuly klesá k nule a nezávisí na
    /// měřené ω. Dnešní profil ji počítal z aktuální ω, takže každý zákmit rotace prodloužil
    /// <c>T_rot</c> a trhl dopřednou rychlostí (84 % skoků rychlosti ve FreeRun 25. 9.).</para>
    /// </summary>
    public sealed class LatencyMotionProfile : IMotionProfile
    {
        /// <summary>Výchozí zpoždění smyčky [s] (<c>motionlatency=</c>). Naměřené zpoždění je
        /// ~0,2–0,25 s (takt 0,1 s + mrtvá doba ~0,05 s + náběh ~0,08 s); 0,4 s je volba autora
        /// ze simulace 25. 9. 2026 — rezerva na zpoždění fúze, které simulace nezná.</summary>
        public const double DefaultLatency = 0.4;

        private readonly double maxSpeed;
        private readonly double maxOrientationSpeed;
        private readonly double acceleration;
        private readonly double rozchod2;
        private readonly double latency;
        private readonly double stability;

        public double MaxSpeed => maxSpeed;
        public double MaxRotationSpeed => maxOrientationSpeed;
        public double Acceleration => acceleration;

        /// <summary>Zpoždění smyčky <c>L</c> [s], se kterým zákon počítá.</summary>
        public double Latency => latency;

        /// <param name="maxSpeed">maximální dopředná rychlost [m/s]</param>
        /// <param name="maxOrientationSpeed">maximální rychlost otáčení [rad/s]</param>
        /// <param name="acceleration">zrychlení i decelerace [m/s²]; pro rotaci na kole (rameno rozchod/2)</param>
        /// <param name="rozchod">rozchod kol [m]</param>
        /// <param name="latency">zpoždění smyčky <c>L</c> [s], &gt; 0</param>
        /// <param name="stability">koeficient vazby dopředné rychlosti na dobu rotace (viz <see cref="SpeedLimit"/>)</param>
        public LatencyMotionProfile(double maxSpeed, double maxOrientationSpeed, double acceleration,
                                    double rozchod, double latency = DefaultLatency, double stability = 4)
        {
            if (!(latency > 0))
                throw new ArgumentOutOfRangeException(nameof(latency), latency,
                    "Zpozdeni smycky musi byt > 0 - s nulou je to casove optimalni zakon s nekonecnym "
                    + "zesilenim u cile, tedy prave to, co kmita.");
            if (!(acceleration > 0))
                throw new ArgumentOutOfRangeException(nameof(acceleration), acceleration, "Zrychleni musi byt > 0.");
            this.maxSpeed = maxSpeed;
            this.maxOrientationSpeed = maxOrientationSpeed;
            this.acceleration = acceleration;
            this.rozchod2 = rozchod / 2.0;
            this.latency = latency;
            this.stability = stability;
        }

        /// <inheritdoc/>
        /// <remarks>Konstantní zrychlení: <c>|v_s² − v_e²|/(2a)</c> — přesná inverze
        /// <see cref="Dist2MaxSpeed"/>.</remarks>
        public double Speed2Dist(double startSpeed, double endSpeed)
            => Math.Abs(startSpeed * startSpeed - endSpeed * endSpeed) / (2 * acceleration);

        /// <inheritdoc/>
        /// <remarks>Brzdná obálka BEZ zpoždění (<c>√(v_e² + 2a·d)</c>), stejně jako
        /// <see cref="TrapezoidMotionProfile"/>: je to horní mez příkazu <see cref="Dist2Speed"/>,
        /// o kterou se opírá plánovač (<c>PathPlanner</c>, vyhlazování v <c>LocalPathPlanner</c>).</remarks>
        public double Dist2MaxSpeed(double dist, double endSpeed)
        {
            if (dist <= 0) return Math.Min(maxSpeed, endSpeed);
            return Math.Min(maxSpeed, Math.Sqrt(endSpeed * endSpeed + 2.0 * acceleration * dist));
        }

        /// <inheritdoc/>
        public RegulatorResult Dist2Speed(double dist, double startSpeed, double endSpeed)
        {
            double x = Math.Abs(dist);
            double ve = Math.Min(Math.Abs(endSpeed), maxSpeed);
            double v = Law(x, ve, acceleration, latency, maxSpeed);
            double vsAlong = dist < 0 ? -startSpeed : startSpeed;   // rychlost ve smeru k cili
            return new RegulatorResult
            {
                // Ne Math.Sign: v nule by dal nulu i tam, kde ma byt ve (prujezd uzlem).
                Speed = dist < 0 ? -v : v,
                RegulationTime = Duration(x, Math.Max(0, vsAlong), ve, maxSpeed, acceleration),
            };
        }

        /// <inheritdoc/>
        /// <remarks>Tentýž zákon v úhlu: zrychlení kola <c>a</c> na rameni <c>rozchod/2</c> je úhlové
        /// zrychlení <c>a/(rozchod/2)</c>. <paramref name="startRotSpeed"/> se nepoužívá (viz popis třídy).</remarks>
        public RegulatorResult Rot2RotSpeed(double beta, double startRotSpeed, double endRotSpeed)
        {
            double alpha = acceleration / rozchod2;
            double x = Math.Abs(beta);
            double we = Math.Min(Math.Abs(endRotSpeed), maxOrientationSpeed);
            double w = Law(x, we, alpha, latency, maxOrientationSpeed);
            return new RegulatorResult
            {
                RotationSpeed = Math.Sign(beta) * w,
                RegulationTime = Duration(x, 0, 0, maxOrientationSpeed, alpha),
            };
        }

        /// <inheritdoc/>
        /// <remarks>Stejná vazba jako u <see cref="TrapezoidMotionProfile"/>:
        /// <c>min(v, d/(stability·T_rot))</c>, při <c>T_rot = 0</c> bez omezení.</remarks>
        public double SpeedLimit(double speed, double d, RegulatorResult rotationResul)
        {
            if (rotationResul.RegulationTime > 0)
                return Math.Min(speed, d / (stability * rotationResul.RegulationTime));
            return speed;
        }

        /// <summary>
        /// Zákon profilu: nejvyšší rychlost, ze které se po zpoždění <paramref name="L"/> ještě
        /// stihne zpomalit na <paramref name="ve"/> na dráze <paramref name="x"/> (vše kladné).
        /// </summary>
        public static double Law(double x, double ve, double acc, double L, double cap)
        {
            double aL = acc * L;
            double v = -aL + Math.Sqrt(aL * aL + 2 * acc * x + ve * ve);
            return Math.Min(cap, Math.Max(ve, v));
        }

        /// <summary>
        /// Doba časově optimálního přejezdu dráhy <paramref name="x"/> z <paramref name="vs"/> na
        /// <paramref name="ve"/> (spojitý lichoběžník se stropem <paramref name="vmax"/>). Když se
        /// na dráze zpomalit nestihne, vrací dobu rovnoměrného zpomalení <c>2x/(v_s + v_e)</c>.
        /// </summary>
        public static double Duration(double x, double vs, double ve, double vmax, double acc)
        {
            if (x <= 0) return 0;
            vs = Math.Min(vs, vmax);
            double vp2 = (2 * acc * x + vs * vs + ve * ve) / 2;   // vrchol trojúhelníku, na
            double vp = Math.Sqrt(vp2);                           // kterém se potká zrychlení s brzděním
            if (vp < Math.Max(vs, ve))
                return vs + ve > 0 ? 2 * x / (vs + ve) : 0;
            if (vp <= vmax)
                return (vp - vs) / acc + (vp - ve) / acc;
            double xUp = (vmax * vmax - vs * vs) / (2 * acc);
            double xDown = (vmax * vmax - ve * ve) / (2 * acc);
            return (vmax - vs) / acc + (vmax - ve) / acc + (x - xUp - xDown) / vmax;
        }
    }
}
