using System;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// Konfigurace mise <see cref="FreeRunMission"/>. Viz doc/mission-freerun.md.
    ///
    /// <para><b>Jedina skutecna ladici konstanta je <see cref="LookaheadM"/></b> — zbytek je bud
    /// dany geometrii (odsazeni je ctvrtina sirky koridoru), nebo je to strop kvuli bezpecnosti.</para>
    /// </summary>
    public sealed class FreeRunConfig
    {
        /// <summary>
        /// Jak daleko pred robota se klada mrkev [m].
        ///
        /// <para><b>Tohle je ta konstanta, ktera se bude ladit.</b> Kratky lookahead dava ostre
        /// srovnavani na pozadovanou caru (a s nim kmitani), dlouhy plynulou jizdu, ktera ale
        /// zatacky „rezne". Vychozi 3 m je odhad k premereni, ne merena pravda.</para>
        /// </summary>
        public double LookaheadM = 3.0;

        /// <summary>
        /// Podil sirky koridoru, o ktery je mrkev odsazena vpravo od osy. <c>0,25</c> = stred prave
        /// poloviny.
        ///
        /// <para><b>Proc podil a ne pevny odstup od hrany:</b> proporcionalni odsazeni degraduje
        /// rozumne na obou koncich — na 2m ceste 0,5 m, na 4m 1,0 m, na 1m 0,25 m — a nepridava
        /// konstantu, protoze sirka uz z koridoru je. Pevnych „0,5 m od prave hrany" by na 1m ceste
        /// poslalo robota VLEVO od osy. Rozhodnuti autora, viz doc/mission-freerun.md.</para>
        /// </summary>
        public double RightOffsetFraction = 0.25;

        /// <summary>
        /// Strop rychlosti mise [m/s]; <c>0</c> = neomezovat a nechat to na rychlostni obalce
        /// lokalniho planovace.
        ///
        /// <para>Je to strop NAD existujici obalkou, ne jeji nahrada — planovac uz umi zpomalit
        /// u prekazek a v zatackach. Tohle jen rika „a nikdy ne rychleji nez tolik".</para>
        /// </summary>
        public double MaxSpeedMps = 0.0;

        /// <summary>Zkontroluje konzistenci; vyhodi <see cref="ArgumentException"/> pri chybe.</summary>
        public void Validate()
        {
            if (!(LookaheadM > 0.5))
                throw new ArgumentException(
                    $"FreeRunConfig.LookaheadM ({LookaheadM}) musi byt > 0,5 m. Kratsi lookahead "
                    + "poklada mrkev prakticky na robota a planovac nema kam jet.");
            if (!(RightOffsetFraction > 0) || RightOffsetFraction >= 0.5)
                throw new ArgumentException(
                    $"FreeRunConfig.RightOffsetFraction ({RightOffsetFraction}) musi byt v (0; 0,5). "
                    + "Polovina sirky uz lezi NA prave hranici koridoru, takze to neni "
                    + "'prava polovina', ale 'prave po okraji'; nula je stred cesty.");
            if (MaxSpeedMps < 0)
                throw new ArgumentException(
                    $"FreeRunConfig.MaxSpeedMps ({MaxSpeedMps}) nesmi byt zaporna; nula = neomezovat.");
        }
    }
}
