using System;

namespace ARBot.Common.Occupancy
{
    /// <summary>Okluzni pravidlo barvy, viz <see cref="OccupancyIntegratorConfig.ColorShadow"/>.</summary>
    public enum ColorShadowMode
    {
        /// <summary>
        /// Puvodni pravidlo: v kazdem azimutu se od nabezne hrany PRVNI prekazky dal barva nevzorkuje
        /// vubec. Zmereno 9. 10. 2026 nad 46 jizdami: zahodi 45,7 % barevnych vzorku, ktere by se
        /// jinak zapsaly - stin vrha hlavne nizka drsna plocha (trava) blizko robotu.
        /// </summary>
        FirstObstacle,

        /// <summary>
        /// <b>Stin podle vysky vrhace</b> (vychozi od 10. 10. 2026, rozhodnuti autora): prekazka
        /// s vrcholem <c>zT</c> zakryje zem jen do vzdalenosti <c>e·Cz/(Cz − zT)</c>, kde <c>e</c> je
        /// vnejsi hrana jejiho prstence a <c>Cz</c> vyska kamery - obe vysky ABSOLUTNE v ramci robotu,
        /// protoze barva se vzorkuje v bode <c>(x, y, 0)</c> toho ramce (<c>zT ≥ Cz</c> = az do
        /// nekonecna, <c>zT ≤ 0</c> = nic). Vlastni prstenec prekazky se NEstini - bod v prstenci
        /// <c>rb</c> zakryvaji jen prstence pred nim; ten prstenec se stejne potvrdi jako prekazka
        /// z hloubky.
        ///
        /// <para><b>Vrchol je <c>MeanZ + √3·StdZ</c>, ne <c>MaxZ</c>:</b> u svisle plochy s rovnomerne
        /// rozlozenymi body (stred <c>h/2</c>, smerodatna odchylka <c>h/√12</c>) je to presne jeji
        /// vrchol, a jeden uletly bod ho posune jen o <c>(z − prumer)·√(3/n)</c> misto cele vysky.
        /// Prumer sam (<c>MeanZ</c>) je zamitnuty merenim: body svisle plochy lezi v jednom prstenci,
        /// takze prumer je polovina vysky a stin by vysel kratky (zem „zachranena" prumerem navic lezi
        /// z 63 % za prekazkou nad 30 cm, tedy zakryta).</para>
        ///
        /// <para>Poloha kamery se bere z hloubkove projekce (<c>IDepthCameraProjection.Transformation</c>);
        /// projekce bez ni nebo kamera nize nez <c>MinCameraHeightM</c> = <see cref="FirstObstacle"/>.
        /// Viz <c>vid-inshadow-zahazuje-vzorky</c> a doc/occupancy-and-local-planning.md, „Okluze".</para>
        /// </summary>
        Height,
    }

    /// <summary>
    /// Konfigurace zapisu z <see cref="ARBot.Common.Devices.CameraFrame"/> do occupancy gridu
    /// (<see cref="OccupancyIntegrator"/>). Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    public sealed class OccupancyIntegratorConfig
    {
        /// <summary>Kolik sloupcu bylo oriznuto z kazde strany hloubkoveho obrazu
        /// (musi odpovidat <c>PolarGridConfig.EdgeColumnTrim</c>, jinak se azimut posune).</summary>
        public int EdgeColumnTrim = 0;

        /// <summary>
        /// Prah probability (0..255), pod kterym je pixel povazovan za NEsjizdny.
        /// Konvence stejna jako v <c>PathEdgeFinder</c>: vyssi hodnota = sjizdnejsi, 128 = neutralni.
        /// </summary>
        public byte RoadNeutral = 128;

        /// <summary>Do teto vzdalenosti [m] ma barevny vzorek plnou duveru.</summary>
        public double RoadFullRangeM = 3.0;

        /// <summary>Za touto vzdalenosti [m] se barva uz nepouziva (duvera 0). Barva dohledne dal nez
        /// pouzitelna hloubka, ale roste chyba rovinneho predpokladu i velikost pudorysu pixelu.</summary>
        public double RoadMaxRangeM = 8.0;

        /// <summary>
        /// Zapisovat semanticky kanal i tam, kde hloubka nic nevi (za dosahem polarniho gridu)?
        /// Ano je zamer: barva dohledne dal a je to jediny zdroj informace o ceste pred robotem.
        /// Okluze se pritom porad respektuje (<see cref="ColorShadow"/>).
        /// </summary>
        public bool RoadBeyondDepthRange = true;

        /// <summary>
        /// <b>Okluzni pravidlo barvy</b> - kde se semanticky kanal NEvzorkuje, protoze barva v tom miste
        /// obrazu patri prekazce, ne zemi za ni. Vychozi <see cref="ColorShadowMode.Height"/>;
        /// <see cref="ColorShadowMode.FirstObstacle"/> je puvodni pravidlo pro A/B
        /// (<c>colorshadow=first</c>). Viz doc/occupancy-and-local-planning.md, „Okluze".
        /// </summary>
        public ColorShadowMode ColorShadow = ColorShadowMode.Height;

        /// <summary>
        /// Maximalni vzdalenost [m], do ktere se vubec prochazi okoli robotu. 0 = odvodit z gridu
        /// a z dosahu polarniho gridu (default).
        /// </summary>
        public double MaxRangeM = 0;

        /// <summary>
        /// <b>Sirka klinu mezi zornymi poli barevnych streamu [stupne]</b>, ve kterem se dopisuje
        /// semantika interpolovana z okoli (<see cref="WedgeFiller"/>); <b>0 = vypnuto</b>.
        ///
        /// <para>Zmereno 12. 9. 2026 z intrinsik v zaznamu: barva ma pri 640x480 HFOV 55,0 stupnu
        /// a kamery jsou pootocene o +-29,3 stupne, takze mezi nimi zbyva <b>3,7 stupne</b>.
        /// Vychozich 6 je ta mezera s rezervou na nepresnost montaze; sirsi nastaveni uz doplnuje
        /// tam, kde kamera opravdu vidi, takze by jen prekryvalo skutecna mereni (ta se ale
        /// neprepisuji, viz pojistka 2 v <see cref="WedgeFiller"/>).</para>
        /// </summary>
        public double WedgeFillDeg = 6.0;

        /// <summary>Do jake vzdalenosti se klin doplnuje [m].</summary>
        public double WedgeFillRangeM = 6.0;

        /// <summary>Nejdelsi pricna mezera, pres kterou se interpoluje [m].</summary>
        public double WedgeFillMaxGapM = 0.6;

        /// <summary>
        /// Duvera doplneneho vzorku proti skutecnemu pozorovani (0..1). ⚠️ <b>Vychozi 1,0 je
        /// zmerena volba</b>, ne nedbalost: pri 0,5 se 13,3 % doplnenych bunek k prahu nedostalo,
        /// protoze sousede u klinu jsou sami tesne pod prahem (p50 -0,95 az -1,10 proti prahu
        /// -1,00). Pulena interpolace tedy bunku nerozhodne skoro nikdy.
        /// </summary>
        public double WedgeFillConfidence = 1.0;

        /// <summary>Duvera barevneho vzorku podle vzdalenosti (linearni pokles za
        /// <see cref="RoadFullRangeM"/> na 0 v <see cref="RoadMaxRangeM"/>).</summary>
        public float RoadConfidence(double range)
        {
            if (range <= RoadFullRangeM) return 1f;
            if (range >= RoadMaxRangeM) return 0f;
            return (float)((RoadMaxRangeM - range) / (RoadMaxRangeM - RoadFullRangeM));
        }

        /// <summary>Prevede hodnotu probability (0..255) na pravdepodobnost sjizdnosti 0..1
        /// (<see cref="RoadNeutral"/> -&gt; 0,5).</summary>
        public float ProbabilityToTraversable(byte value)
        {
            if (value >= RoadNeutral)
            {
                int span = 255 - RoadNeutral;
                return span <= 0 ? 1f : 0.5f + 0.5f * (value - RoadNeutral) / span;
            }
            return RoadNeutral <= 0 ? 0f : 0.5f * value / RoadNeutral;
        }

        /// <summary>Zkontroluje konzistenci; vyhodi <see cref="ArgumentException"/> pri chybe.</summary>
        public void Validate()
        {
            if (EdgeColumnTrim < 0) throw new ArgumentException("OccupancyIntegratorConfig.EdgeColumnTrim musi byt >= 0.");
            if (RoadMaxRangeM <= RoadFullRangeM)
                throw new ArgumentException(
                    $"OccupancyIntegratorConfig: RoadMaxRangeM ({RoadMaxRangeM}) musi byt > RoadFullRangeM ({RoadFullRangeM}).");
            if (WedgeFillDeg < 0 || WedgeFillDeg > 30)
                throw new ArgumentException(
                    $"OccupancyIntegratorConfig.WedgeFillDeg ({WedgeFillDeg}) musi byt 0..30 stupnu "
                    + "(0 = vypnuto). Sirsi klin uz neni mezera mezi kamerami, ale plocha, kterou "
                    + "kamera vidi - doplnovat ji by znamenalo hadat misto mereni.");
            if (WedgeFillConfidence < 0 || WedgeFillConfidence > 1)
                throw new ArgumentException(
                    $"OccupancyIntegratorConfig.WedgeFillConfidence ({WedgeFillConfidence}) musi byt 0..1.");
        }
    }
}
