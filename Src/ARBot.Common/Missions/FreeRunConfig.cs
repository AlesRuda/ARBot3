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
        /// zatacky „rezne". Puvodni vychozi 3 m byl odhad k premereni; 3. 9. 2026 na pokyn autora
        /// zkracen na POLOVINU (1,5 m) - i pro kratke testovaci useky (OSM/SyntetickyRovny2m.osm),
        /// kde robot ma pred sebou jen 1 m cesty. Porad je to odhad, ne merena pravda.</para>
        /// </summary>
        public double LookaheadM = 1.5;

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
        /// Nejmensi odstup pozadovane cary od PRAVEHO kraje cesty [m]. Prava polovina jen tehdy,
        /// kdyz od kraje zbyde aspon tolik; jinak se cara posune k ose, nejdal na stred cesty
        /// (pravidlo autora 26. 9. 2026, viz <see cref="FreeRunMission.RightOffsetFromAxis"/>).
        ///
        /// <para><b>Hodnota:</b> <c>SafeDist + EdgeMarginM</c> lokalniho planovace (0,40 + 0,15 m) —
        /// pod <c>SafeDist</c> planovac nejde vubec a v pasmu <c>EdgeMarginM</c> nad nim podel
        /// prekazky zpomaluje; kraj cesty (trava) je pro nej neprujezdny. Runtime ho bere ze
        /// skutecne konfigurace planovace, takze sleduje i <c>safedist=</c>.</para>
        /// </summary>
        public double MinRightEdgeClearanceM = DefaultMinRightEdgeClearance();

        private static double DefaultMinRightEdgeClearance()
        {
            var p = new Occupancy.LocalPlannerConfig();
            return p.SafeDist + p.EdgeMarginM;
        }

        /// <summary>
        /// Klade se mrkev i podle <b>JEDINE</b> viditelne hrany cesty? (<c>freerunsingle=</c>)
        ///
        /// <para><b>Proc (26. 9. 2026, pokyn autora):</b> ve FreeRun <c>20260925-144658.rec</c> vznikl
        /// oboustranny koridor jen ve 2,7 % snimku, jedna hrana ale v 86 % (prava 65 %, leva 21 %)
        /// a jeji smer sedel na GPS kurz (p50 0,24°, p90 5°). Mise pritom brala jen oboustranny
        /// koridor, takze 97 % casu jela „rovne podle kurzu".</para>
        ///
        /// <para><b>Jak:</b> odstup hrany od robotu je zmereny, takze pricna poloha vuci hrane je
        /// znama. Se sirkou z mapy (<see cref="FreeRunMission.MapWidthAt"/>) z ni vznikne osa cesty
        /// a mrkev jde doprostred prave poloviny jako u oboustranneho koridoru; bez sirky jde mrkev
        /// ve smeru hrany se <b>zachovanym zmerenym odstupem</b>. <c>false</c> = chovani do 26. 9.</para>
        /// </summary>
        public bool UseSingleEdge = true;

        /// <summary>
        /// Nejvetsi odstup pozy od mapove cesty [m], pri kterem se jeji sirka jeste bere. Dal uz
        /// nejspis nejde o cestu, po ktere robot jede, a mrkev pujde podle odstupu od hrany.
        /// </summary>
        public double MapWidthMaxDistanceM = 8.0;

        /// <summary>
        /// Nejvetsi rozdil smeru viditelne hrany a mapove cesty [stupne], pri kterem se sirka z mapy
        /// bere. Chrani pred sirkou PRICNE ulice u krizovatky.
        /// </summary>
        public double MapWidthMaxAngleDeg = 20.0;

        // POZN.: bývalo tu pole MaxSpeedMps ("strop rychlosti mise"). Bylo to MRTVÉ - nikdo ho
        // nečetl, a číst ho ani nešlo: šev do lokální vrstvy je
        // ILocalGoalSink.SetGoal(worldX, worldY, corridorWidthM) a kanál pro rychlost tam není.
        // Vypadalo to jako hotová funkce, přitom nastavit ho nic nedělalo. Odstraněno 1. 9. 2026.
        // Strop rychlosti se dnes zadává parametrem maxspeed= (nastaví Profile.MaxAllowedSpeed,
        // tedy platí pro CELÉ řízení, ne jen pro misi) - viz doc/configuration.md.

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
            if (!(MinRightEdgeClearanceM >= 0))
                throw new ArgumentException(
                    $"FreeRunConfig.MinRightEdgeClearanceM ({MinRightEdgeClearanceM}) musi byt >= 0.");
            if (!(MapWidthMaxDistanceM > 0) || !(MapWidthMaxAngleDeg > 0) || MapWidthMaxAngleDeg > 90)
                throw new ArgumentException(
                    $"FreeRunConfig.MapWidthMaxDistanceM ({MapWidthMaxDistanceM}) musi byt > 0 "
                    + $"a MapWidthMaxAngleDeg ({MapWidthMaxAngleDeg}) v (0; 90].");
        }
    }
}
