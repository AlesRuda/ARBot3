using System;
using ARBot.Common.Configuration;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Konfigurace lokalniho planovace (<see cref="LocalPathPlanner"/>) - odstupy, rychlostni stropy
    /// a ceny. Vychozi hodnoty se berou z <see cref="Profile"/>.
    /// Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    public sealed class LocalPlannerConfig
    {
        /// <summary>TVRDY minimalni odstup od neprujezdneho [m]. Bliz planovac nikdy nejde.</summary>
        public double SafeDist = Profile.SafeDist;

        /// <summary>Odstup, od ktereho uz se rychlost neomezuje [m].</summary>
        public double PrefDist = Profile.PrefDist;

        /// <summary>Maximalni dovolena rychlost [m/s].</summary>
        public double MaxSpeed = Profile.MaxAllowedSpeed;

        /// <summary>Decelerace pouzita v brzdne obalce [m/s^2].</summary>
        public double MaxDeceleration = Profile.MaxDecceleration;

        /// <summary>Maximalni rychlost otaceni [rad/s] - pro cenu otoceni z aktualniho kurzu.</summary>
        public double MaxRotationSpeed = Profile.MaxAllowedRotationSpeed;

        /// <summary>
        /// Spodni mez rychlosti pouzita JEN v cene planovani [m/s]. Bez ni by bunka presne na hranici
        /// <see cref="SafeDist"/> mela nekonecnou cenu, i kdyz je jeste prujezdna. Nizka hodnota =
        /// takova bunka je velmi draha, ale pouzitelna, kdyz nic lepsiho neni.
        /// </summary>
        public double MinCostSpeed = 0.05;

        /// <summary>
        /// Nasobek ceny pruchodu bunkou <see cref="CellState.Unknown"/>. Planovat se skrz neznamo SMI
        /// (jinak by robot nikdy nevyjel - dopredu vidi 5 m a do stran skoro nic), jen je to drazsi.
        /// Ze do neznama nesmi VJET se resi brzdna obalka, ne tato cena.
        /// </summary>
        public double UnknownCostFactor = 3.0;

        /// <summary>Minimalni tolerance pruchodu waypointem (epsilon) [m].</summary>
        public double EpsMin = 0.03;

        /// <summary>Maximalni tolerance pruchodu waypointem (epsilon) [m].</summary>
        public double EpsMax = 0.15;

        /// <summary>Maximalni delka planovane drahy [m] (horizont lokalniho planu).</summary>
        /// <remarks>
        /// NENI to radius, ale <b>maximalni delka planovane drahy</b> - planovac preruší expanzi,
        /// jakmile <c>lenFromStart >= HorizonM</c>. Globalni navigace pokláda mrkev az na okraj
        /// lokalni mapy (~6,4 m vzdusne), ale cesta k ni pres bludiste muze mit 20 i 30 m; s puvodnimi
        /// 6 m by se plan utnul v polovine a mrkev by byl nedosazitelny pokazde, kdyz cesta neni skoro
        /// prima. Cena je jen vypocetni (A* smi v nejhorsim expandovat cely grid).
        /// Viz doc/global-navigation-runtime.md.
        /// </remarks>
        public double HorizonM = 25.0;

        // POZN.: bývalo tu pole EscapeRadius (0,5 m) - "eskapovací zóna", ve které se kolem
        // aktuální buňky slevoval odstup pod SafeDist, aby robot, který zastavil těsně u překážky,
        // měl odkud vyjet. Zrušeno 3. 9. 2026: zóna byla symetrická (pustila robota i BLÍŽ
        // k překážce) a posouvala se s ním, takže se k okraji cesty dalo doplížit po buňce
        // s libovolně velkým SafeDist. Těsný start dnes řeší únikový režim (EscapingBlocked), tj.
        // totéž, co blokovaná buňka; hystereze půl buňky je odvozená z rozlišení gridu v
        // LocalPathPlanner.Plan, ne parametr. Viz doc/occupancy-and-local-planning.md a decisions.md.

        /// <summary>
        /// Nejdelsi pripustna draha UNIKU z blokovane nebo tesne bunky [m]. Kdyz je nejblizsi
        /// legalni bunka dal, unik se nezkousi (<see cref="LocalPlanStatus.RobotBlocked"/>) - bloudit
        /// metry mimo cestu je horsi nez stat a nechat to na vyssi vrstve.
        /// </summary>
        public double EscapeMaxLength = 1.5;

        /// <summary>
        /// Nasobek ceny za prujezd bunkou, kterou blokuje semantika (pri uniku). Nezakazuje ji -
        /// jen davá prednost uniku, ktery mimo cestu stravi co nejmene.
        /// </summary>
        public double EscapeBlockedCostFactor = 4.0;

        /// <summary>
        /// Bocni rychlostni strop z odstupu od prekazky [m/s]: linearni rampa mezi
        /// <see cref="SafeDist"/> (0) a <see cref="PrefDist"/> (<see cref="MaxSpeed"/>).
        /// <para>Zamerne NE odmocnina - u bocniho odstupu nejde o brzdnou drahu, ta patri
        /// vyhradne do <see cref="VBrake"/>.</para>
        /// </summary>
        public double VClear(double clearance)
        {
            if (clearance <= SafeDist) return 0.0;
            if (clearance >= PrefDist) return MaxSpeed;
            double t = (clearance - SafeDist) / (PrefDist - SafeDist);
            return MaxSpeed * t;
        }

        /// <summary>
        /// Brzdna obalka [m/s]: nejvyssi rychlost, ze ktere se jeste stihnu zastavit po ujeti
        /// <paramref name="freeAhead"/> metru. Tim se resi "skrz neznamo smim planovat, ale nesmim
        /// do nej vjet" - <paramref name="freeAhead"/> je vzdalenost k prvni bunce, ktera neni
        /// <see cref="CellState.Free"/>.
        /// </summary>
        public double VBrake(double freeAhead)
        {
            if (freeAhead <= 0) return 0.0;
            return Math.Min(MaxSpeed, Math.Sqrt(2.0 * MaxDeceleration * freeAhead));
        }

        /// <summary>Rychlost pouzita v CENE planovani (s podlahou <see cref="MinCostSpeed"/>).</summary>
        public double VCost(double clearance) => Math.Max(MinCostSpeed, VClear(clearance));

        /// <summary>Zkontroluje konzistenci; vyhodi <see cref="ArgumentException"/> pri chybe.</summary>
        public void Validate()
        {
            if (SafeDist < 0) throw new ArgumentException("LocalPlannerConfig.SafeDist musi byt >= 0.");
            if (PrefDist <= SafeDist)
                throw new ArgumentException(
                    $"LocalPlannerConfig: PrefDist ({PrefDist}) musi byt > SafeDist ({SafeDist}).");
            if (MaxSpeed <= 0) throw new ArgumentException("LocalPlannerConfig.MaxSpeed musi byt > 0.");
            if (MaxDeceleration <= 0) throw new ArgumentException("LocalPlannerConfig.MaxDeceleration musi byt > 0.");
            if (MaxRotationSpeed <= 0) throw new ArgumentException("LocalPlannerConfig.MaxRotationSpeed musi byt > 0.");
            if (MinCostSpeed <= 0) throw new ArgumentException("LocalPlannerConfig.MinCostSpeed musi byt > 0.");
            if (EpsMin <= 0 || EpsMax < EpsMin)
                throw new ArgumentException("LocalPlannerConfig: musi platit 0 < EpsMin <= EpsMax.");
        }
    }
}
