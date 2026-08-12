namespace ARBot.Common.Vision.Synthetic
{
    /// <summary>
    /// Parametry vzhledu a sumu simulovane sceny (viz doc/virtual-hw.md).
    /// Kazda slozka sumu se vypina nulou - to je rezim pro deterministicke testy.
    /// </summary>
    public sealed class SyntheticSceneOptions
    {
        /// <summary>Vyska travy nad rovinou vozovky [m]. 0 = trava lezi v rovine vozovky.</summary>
        public double GrassHeightM = 0.10;

        /// <summary>Rozptyl vysky travy [m] (drsnost povrchu). 0 = hladka rovina.</summary>
        public double GrassRoughnessM = 0.03;

        /// <summary>Smerodatna odchylka sumu hloubky [m]. 0 = presna geometrie.</summary>
        public double DepthNoiseM = 0.003;

        /// <summary>Dosah kamery [m]; dal je pixel neplatny (hloubka 0), jako u realne D435.</summary>
        public double MaxRangeM = 10.0;

        /// <summary>Barva vozovky (seda).</summary>
        public byte RoadR = 128, RoadG = 128, RoadB = 128;

        /// <summary>Barva travy (zelena) - i nad horizontem, viz doc/virtual-hw.md.</summary>
        public byte GrassR = 60, GrassG = 140, GrassB = 60;

        /// <summary>Amplituda sumu barvy [0..255 na slozku]. 0 = ciste barvy.</summary>
        public double ColorNoise = 6.0;

        /// <summary>Seed sumu - se stejnym seedem a pozou vyjde bitove tentyz snimek.</summary>
        public int Seed = 1;
    }
}
