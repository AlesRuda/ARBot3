using System;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// <b>Doplneni semantiky v KLINU mezi zornymi poli barevnych streamu.</b>
    ///
    /// <para><b>Proc to je potreba (zmereno 12. 9. 2026, <c>ARBot.Analyze wedge</c> nad
    /// <c>records/test/20260907-170728.rec</c>):</b> barva D435 ma pri 640x480 vodorovne zorne pole
    /// jen <b>55,0 stupnu</b> (katalogovych 69 plati pro 16:9) a kamery jsou pootocene o <b>+-29,3
    /// stupne</b>, takze barva pokryva -56,9..-1,8 a +1,8..+56,8 stupnu — <b>primo pred robotem
    /// zustava mezera 3,7 stupne</b>, tedy pruh siroky 0,19 m ve 3 m a 0,38 m v 6 m. Hloubka tam
    /// vidi (jeji zorne pole je 89,6 stupnu), ale <see cref="CellState.Free"/> vyzaduje OBA kanaly,
    /// takze klin zustava <see cref="CellState.Unknown"/>. Grid to potvrzuje nezavisle: podil bunek
    /// „chybi jen semantika" je ve smeru jizdy <b>4,1 %</b> proti <b>1,4 %</b> po stranach a
    /// <b>roste se vzdalenosti</b> (3,5 % v 1 m, 7,3 % ve 4 m) — presne jak se klin s dalkou
    /// rozsiruje.</para>
    ///
    /// <para><b>Co to dela:</b> bunce v klinu, ktera <b>nema zadny vzorek semantiky</b>, dopise
    /// semantiku <b>INTERPOLOVANOU z nejblizsich bunek pricne vlevo a vpravo</b> — tedy z toho, co
    /// o temze povrchu rika leva a prava kamera tesne vedle klinu.</para>
    ///
    /// <para>⚠️ <b>Neni to „prohlasit neznamo za sjizdne".</b> Ten rozdil je podstatny: konstanta
    /// „sjizdne" by do mapy zapsala cestu i tam, kde je ve skutecnosti trava nebo dira, a ta lez by
    /// se sirila dal (occupancy grid cte i korelace s mapou). Interpolace pres 19 cm mezeru mezi
    /// dvema pozorovanimi TEHOZ povrchu nove tvrzeni nevyraba: kdyz je vlevo i vpravo trava, vyjde
    /// trava.</para>
    ///
    /// <para><b>Ctyri pojistky:</b></para>
    /// <list type="number">
    /// <item><description><b>Geometrie se nedoplnuje NIKDY</b> — jen semanticky kanal. Prekazku vidi
    /// hloubka a ta v klinu funguje; vymyslet „volno" tam, kde muze byt prekazka, by bylo
    /// nebezpecne.</description></item>
    /// <item><description><b>Doplnuje se jen bunka, ktera je dnes Unknown PRAVE KVULI semantice</b>
    /// — geometrie ji ma potvrzenou jako volnou a semantika je mezi prahy. Bunka, kterou uz barva
    /// rozhodla (Free nebo Blocked), se nedotkne, a vzorek se <b>pricita</b> jako dalsi dukaz, ne
    /// prepisuje. ⚠️ Puvodne se doplnovala jen bunka s <c>LogOddsRoad == 0</c> a <b>merenim se
    /// ukazalo, ze to nedela nic</b>: bunky v klinu maji vetsinou slaby vzorek z okraje zorneho
    /// pole (kde <c>RoadConfidence</c> klesa), ne zadny.</description></item>
    /// <item><description><b>Musi byt podpora z OBOU stran</b> do <see cref="MaxGapM"/>. Na okraji
    /// dosahu (kde soused chybi) se nedoplnuje nic.</description></item>
    /// <item><description><b>Doplnenim nemuze vzniknout PREKAZKA</b> — zapis se orizne tak, aby
    /// bunka zustala nejvys <see cref="CellState.Unknown"/>. Doplneni smi rychlost jen povolit,
    /// nikdy ji samo nezakazat; zakazat ji smi jen skutecne mereni.</description></item>
    /// </list>
    ///
    /// <para>Zapina se parametrem <c>wedgefill=</c> (sirka klinu ve stupnich, 0 = vypnuto).
    /// Viz doc/occupancy-and-local-planning.md.</para>
    /// </summary>
    public sealed class WedgeFiller
    {
        private readonly OccupancyGrid grid;

        /// <summary>Polovicni sirka doplnovaneho klinu [rad] od smeru jizdy.</summary>
        public double HalfWidthRad { get; }

        /// <summary>Do jake vzdalenosti se doplnuje [m].</summary>
        public double RangeM { get; }

        /// <summary>
        /// Jak daleko pricne se hleda soused se semantikou [m]. Vetsi nez sirka klinu v nejvetsi
        /// vzdalenosti (0,38 m v 6 m), ale ne o moc: pres metr uz to neni tyz povrch.
        /// </summary>
        public double MaxGapM { get; }

        /// <summary>
        /// Duvera doplneneho vzorku (0..1) proti skutecnemu pozorovani. Mensi nez 1 zamerne:
        /// interpolace je slabsi dukaz nez mereni, takze se na ni <b>pomaleji</b> akumuluje a
        /// prvni skutecne pozorovani ji prebije.
        /// </summary>
        public double Confidence { get; }

        /// <param name="grid">Cilovy grid.</param>
        /// <param name="halfWidthRad">Polovicni sirka klinu [rad]; &lt;= 0 = nedela se nic.</param>
        /// <param name="rangeM">Dosah doplnovani [m].</param>
        /// <param name="maxGapM">Nejdelsi pricna mezera, pres kterou se interpoluje [m].</param>
        /// <param name="confidence">Duvera doplneneho vzorku 0..1.</param>
        /// <param name="minRangeM">Od jake vzdalenosti se doplnuje [m].</param>
        public WedgeFiller(OccupancyGrid grid, double halfWidthRad, double rangeM = 6.0,
                           double maxGapM = 0.6, double confidence = 1.0, double minRangeM = 0.3)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            HalfWidthRad = halfWidthRad;
            RangeM = rangeM;
            MaxGapM = maxGapM;
            Confidence = confidence;
            MinRangeM = minRangeM;
        }

        /// <summary>
        /// Od jake vzdalenosti se klin doplnuje [m]. ⚠️ <b>Zmereno, ze na tom zalezi:</b> puvodnich
        /// 0,5 m nechalo <b>20,4 %</b> zastaveni paprsku bez lecby, protoze bunka blizsi nez pulmetr
        /// zastavi paprsek uplne stejne jako vzdalena - a <c>VBrake</c> je na kratke vzdalenosti
        /// nejcitlivejsi (0,3 m = 0,42 m/s). Vychozich 0,3 m navazuje na
        /// <c>LocalPlannerConfig.FootprintRadiusM</c>: blize uz je pudorys robotu, ktery si
        /// planovac bere jako sjizdny sam.
        /// </summary>
        public double MinRangeM { get; }

        /// <summary>Kolik bunek doplnil posledni <see cref="Fill"/> (diagnostika).</summary>
        public int LastFilled { get; private set; }

        /// <summary>
        /// <b>Kde se doplnovani ztratilo</b> - diagnostika posledniho <see cref="Fill"/>. Bez ni se
        /// „nedoplnilo se nic" neda vylozit: kandidat mohl chybet uplne, nebo jich byly stovky
        /// a vsechny padly na chybejiciho souseda. Prvni nenulove pole zprava rekne, ktery clanek
        /// retezu selhal.
        /// </summary>
        public struct FillStats
        {
            /// <summary>Bunek v klinu, ktere doplneni potrebuji (Unknown kvuli semantice).</summary>
            public int Kandidatu;
            /// <summary>Z toho zamitnuto, protoze chybel rozhodnuty soused vlevo nebo vpravo.</summary>
            public int BezSouseda;
            /// <summary>Z toho zamitnuto stropem (doplneni by vyrobilo prekazku).</summary>
            public int Stropem;
            /// <summary>Zapsanych bunek.</summary>
            public int Zapsano;

            /// <inheritdoc/>
            public override string ToString()
                => $"kandidatu={Kandidatu} bezSouseda={BezSouseda} stropem={Stropem} zapsano={Zapsano}";
        }

        /// <summary>Diagnostika posledniho <see cref="Fill"/> - viz <see cref="FillStats"/>.</summary>
        public FillStats LastStats { get; private set; }

        /// <summary>
        /// Doplni klin pred robotem. Vraci pocet doplnenych bunek.
        /// </summary>
        /// <param name="robotX">Poloha robotu [m, world ENU].</param>
        /// <param name="robotY">Poloha robotu [m, world ENU].</param>
        /// <param name="heading">Kurz robotu [rad] (0 = vychod, +CCW).</param>
        public int Fill(double robotX, double robotY, double heading)
        {
            LastFilled = 0;
            var stats = default(FillStats);
            LastStats = stats;
            if (!(HalfWidthRad > 0) || !(RangeM > 0)) return 0;

            double cosH = Math.Cos(heading), sinH = Math.Sin(heading);
            int span = (int)Math.Ceiling(RangeM / grid.Resolution) + 1;
            int cx0 = grid.CellX(robotX), cy0 = grid.CellY(robotY);

            double minRange = MinRangeM;
            double tanHalf = Math.Tan(HalfWidthRad);

            for (int cy = cy0 - span; cy <= cy0 + span; cy++)
                for (int cx = cx0 - span; cx <= cx0 + span; cx++)
                {
                    if (!grid.Contains(cx, cy)) continue;
                    if (!PotrebujeDoplnit(cx, cy)) continue;           // pojistky 1 a 2

                    double dx = grid.CenterX(cx) - robotX, dy = grid.CenterY(cy) - robotY;
                    double fwd = dx * cosH + dy * sinH;
                    if (fwd < minRange || fwd > RangeM) continue;
                    // ⚠️ Klin ma i MINIMALNI sirku jedne bunky: pri 3 stupnich je ve 0,35 m siroky
                    // 1,8 cm, tedy uzsi nez bunka (5 cm), takze by u robota nepropustil zadnou -
                    // a prave tam boli nejvic, protoze VBrake je na kratke vzdalenosti
                    // nejcitlivejsi. Bunka, kterou klin PROCHAZI, do nej patri.
                    double side = -dx * sinH + dy * cosH;
                    double pulSirky = Math.Max(fwd * tanHalf, grid.Resolution);
                    if (Math.Abs(side) > pulSirky) continue;           // mimo klin

                    stats.Kandidatu++;
                    if (!TryNeighbours(cx, cy, -sinH, cosH, out float vlevo, out float vpravo))
                    {
                        stats.BezSouseda++;
                        continue;                                      // pojistka 3
                    }

                    float cil = 0.5f * (vlevo + vpravo);
                    float zapis = (float)(cil * Confidence);
                    if (zapis == 0f) continue;

                    // Pojistka 4: doplnenim nesmi vzniknout prekazka. Zapis se orizne tak, aby
                    // VYSLEDEK (stav + prirustek) zustal pod prahem neprujezdnosti.
                    float strop = grid.Config.BlockedThreshold - grid.Config.Scale
                                  - grid.LogOddsRoad(cx, cy);
                    if (zapis > strop) { zapis = strop; stats.Stropem++; }
                    if (zapis == 0f) continue;

                    grid.AddRoad(cx, cy, zapis);
                    LastFilled++;
                }

            stats.Zapsano = LastFilled;
            LastStats = stats;
            return LastFilled;
        }

        /// <summary>
        /// Je tahle bunka presne ten pripad, kvuli kteremu doplnovani existuje? Tedy:
        /// <b>geometrie ji ma potvrzenou jako volnou</b> (hloubka klin pokryva, jeji zorne pole je
        /// 89,6 proti 55,0 stupnum barvy) a <b>semantika ji nerozhodla</b> — je mezi prahy.
        ///
        /// <para>Bunka, kterou barva uz rozhodla (Free i Blocked), se nedotkne: doplneni nema
        /// prebijet mereni. A bunka bez potvrzene geometrie se nedoplnuje proto, ze by ji to
        /// stejne Free neudelalo (<see cref="OccupancyGrid.StateAt"/> zada OBA kanaly) — jen by
        /// zanesla do mapy tvrzeni, ktere nikdo nekontroluje.</para>
        /// </summary>
        private bool PotrebujeDoplnit(int cx, int cy)
        {
            if (grid.LogOddsOcc(cx, cy) > grid.Config.FreeThreshold) return false;
            float r = grid.LogOddsRoad(cx, cy);
            return r > grid.Config.FreeThreshold && r < grid.Config.BlockedThreshold;
        }

        /// <summary>
        /// Najde nejblizsi bunku se semantikou pricne vlevo a vpravo (smer <paramref name="ux"/>,
        /// <paramref name="uy"/> je „doleva" v ramci robotu). Vraci <c>false</c>, kdyz na nektere
        /// strane do <see cref="MaxGapM"/> zadna neni.
        /// </summary>
        private bool TryNeighbours(int cx, int cy, double ux, double uy,
                                   out float vlevo, out float vpravo)
        {
            vlevo = 0; vpravo = 0;
            int kroku = (int)Math.Ceiling(MaxGapM / grid.Resolution);
            double x0 = grid.CenterX(cx), y0 = grid.CenterY(cy);

            bool Hledej(double smer, out float hodnota)
            {
                hodnota = 0;
                for (int k = 1; k <= kroku; k++)
                {
                    double x = x0 + smer * ux * k * grid.Resolution;
                    double y = y0 + smer * uy * k * grid.Resolution;
                    int i = grid.CellX(x), j = grid.CellY(y);
                    if (!grid.Contains(i, j)) return false;
                    // Staci JAKYKOLI vzorek, ne rozhodnuty. ⚠️ Puvodne se zadal soused rozhodnuty
                    // (Free nebo Blocked ze semantiky) s odvodnenim „nesirit nejistotu" - a merenim
                    // se ukazalo, ze prave ta podminka doplnovani zabijela: v klinu je soused
                    // rozhodnuty z obou stran jen v 17,3 % pripadu, v 78,6 % je aspon jeden jen
                    // slaby. Je to zakonite: bunky u klinu vidi kamera SIKMO na okraji zorneho
                    // pole, takze maji nizkou duveru - slaby vzorek je tam pravidlo, ne vyjimka.
                    float r = grid.LogOddsRoad(i, j);
                    if (r != 0f) { hodnota = r; return true; }
                }
                return false;
            }

            return Hledej(+1, out vlevo) && Hledej(-1, out vpravo);
        }
    }
}
