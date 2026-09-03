using System;
using ARBot.Common.Configuration;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Model rychlostniho stropu z odstupu od prekazek (viz <see cref="LocalPlannerConfig.VEnvelope"/>).
    /// </summary>
    public enum SpeedEnvelopeMode
    {
        /// <summary>
        /// Puvodni model: jedina linearni rampa v_max * (d - SafeDist) / (PrefDist - SafeDist) bez ohledu
        /// na smer jizdy. Trestá BLIZKOST okraje, ne priblizovani k nemu - robot jedouci podel okraje
        /// 0,5 m od travy jel trvale 0,3 m/s. Ponechano pro A/B (parametr envelope=radial).
        /// </summary>
        Radial = 0,

        /// <summary>
        /// Smerovy model (od 3. 9. 2026): PODELNY strop je uzka rampa pres <see cref="LocalPlannerConfig.EdgeMarginM"/>
        /// nad SafeDist (rezerva na chybu sledovani drahy), KOLMY strop je brzdna draha k hranici
        /// SafeDist delena rychlosti priblizovani (zaporny prumet smeru drahy do gradientu pole odstupu).
        /// Podel okraje se jede rychle, kolmo na nej se brzdi tak, aby se dalo zastavit pred SafeDist.
        /// </summary>
        Directional = 1,
    }

    /// <summary>
    /// Konfigurace lokalniho planovace (<see cref="LocalPathPlanner"/>) - odstupy, rychlostni stropy
    /// a ceny. Vychozi hodnoty se berou z <see cref="Profile"/>.
    /// Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    public sealed class LocalPlannerConfig
    {
        /// <summary>TVRDY minimalni odstup od neprujezdneho [m]. Bliz planovac nikdy nejde.</summary>
        public double SafeDist = Profile.SafeDist;

        /// <summary>Odstup, od ktereho uz se rychlost neomezuje [m] - jen v rezimu <see cref="SpeedEnvelopeMode.Radial"/>.</summary>
        public double PrefDist = Profile.PrefDist;

        /// <summary>Model rychlostniho stropu z odstupu. Vychozi smerovy; <c>envelope=radial</c> vrati puvodni.</summary>
        public SpeedEnvelopeMode Envelope = SpeedEnvelopeMode.Directional;

        /// <summary>
        /// Sirka PODELNE rampy nad <see cref="SafeDist"/> [m] (smerovy model): pri odstupu
        /// SafeDist + EdgeMarginM uz se podel prekazky jede plnou rychlosti. Je to rezerva na pricnou
        /// chybu sledovani drahy (regulator, kvantovani gridu 0,05 m, lokalizace) - kdyz robot jedouci
        /// tesne nad SafeDist o kus ujede, nesmi spadnout pod SafeDist a do uniku. V simulaci je pricna
        /// chyba sledovani p50 ~0,01-0,05 m; 0,15 m je trojnasobna rezerva, na HW premerit.
        /// </summary>
        public double EdgeMarginM = 0.15;

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

        /// <summary>
        /// Polomer PUDORYSU robota [m] - co je pod robotem, je sjizdne, protoze tam stoji. Pouziva
        /// se JEN pri vypoctu brzdne obalky (vzdalenost k prvni nepotvrzene bunce po draze): vzorky
        /// drahy blize nez tento polomer se berou jako sjizdne, pokud nejsou <see cref="CellState.Blocked"/>.
        /// Do gridu se nic nezapisuje.
        ///
        /// <para><b>Proc (3. 9. 2026):</b> kamery se sklonem ~20 stupnu zem tesne pred robotem nevidi
        /// (slepa zona ~0,5 m) a <c>Free</c> vyzaduje potvrzeni obema kanaly, takze bunka pod robotem
        /// je po startu <c>Unknown</c>, "volno" po draze je 0 a robot leze <see cref="MinCostSpeed"/>,
        /// dokud slepou zonu neprejede - namereno 10 s pri 0,05 m/s. Pudorys je fakt, ne domnenka:
        /// nepredpoklada nic o prostoru, kam robot nevidi. Alternativy (zapis pudorysu do gridu jako
        /// Free/cesta, presumpce cele slepe zony, vyssi podlaha) zamitnuty - viz doc/devlog.md.</para>
        ///
        /// <para>Mensi nez <see cref="SafeDist"/> (ten je nafouknuty polomer s rezervou); pri pouziti se
        /// na SafeDist orizne. Rozchod kol je 0,41 m, takze 0,3 m kryje kola i s rezervou.</para>
        /// </summary>
        public double FootprintRadiusM = 0.3;

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

        /// <summary>
        /// PODELNY strop u okraje [m/s] (smerovy model): rampa 0 -> <see cref="MaxSpeed"/> pres
        /// <see cref="EdgeMarginM"/> nad <see cref="SafeDist"/>. Jizda PODEL prekazky ji nepriblizuje,
        /// takze jedine, pred cim rampa chrani, je pricna chyba sledovani - odtud uzke pasmo.
        /// </summary>
        public double VAlong(double clearance)
        {
            double t = (clearance - SafeDist) / EdgeMarginM;
            if (t <= 0) return 0.0;
            if (t >= 1) return MaxSpeed;
            return MaxSpeed * t;
        }

        /// <summary>
        /// KOLMY strop [m/s] (smerovy model): dopredna rychlost, pri ktere se slozka priblizovani
        /// k prekazce jeste stihne ubrzdit pred <see cref="SafeDist"/>:
        /// <c>v * closing &lt;= sqrt(2 * a * (d - SafeDist))</c>.
        /// </summary>
        /// <param name="clearance">Odstup od nejblizsi neprujezdne bunky [m].</param>
        /// <param name="closing">Rychlost priblizovani na jednotku dopredne rychlosti, 0..1:
        /// zaporny prumet smeru drahy do gradientu pole odstupu (<c>max(0, -t . grad d)</c>).
        /// 0 = jede podel nebo od prekazky (bez omezeni), 1 = primo na ni.</param>
        public double VClosing(double clearance, double closing)
        {
            if (!(closing > 1e-6)) return MaxSpeed;
            double margin = clearance - SafeDist;
            if (margin <= 0) return 0.0;
            return Math.Min(MaxSpeed, Math.Sqrt(2.0 * MaxDeceleration * margin) / closing);
        }

        /// <summary>
        /// Rychlostni strop z odstupu podle zvoleneho modelu (<see cref="Envelope"/>). V radialnim
        /// rezimu se <paramref name="closing"/> ignoruje a vraci se <see cref="VClear"/>.
        /// </summary>
        public double VEnvelope(double clearance, double closing)
            => Envelope == SpeedEnvelopeMode.Radial
                ? VClear(clearance)
                : Math.Min(VAlong(clearance), VClosing(clearance, closing));

        /// <summary>Rychlost pouzita v CENE planovani (s podlahou <see cref="MinCostSpeed"/>), radialne (bez smeru).</summary>
        public double VCost(double clearance) => Math.Max(MinCostSpeed, VClear(clearance));

        /// <summary>Rychlost pouzita v CENE planovani (s podlahou <see cref="MinCostSpeed"/>) podle zvoleneho modelu.</summary>
        public double VCost(double clearance, double closing) => Math.Max(MinCostSpeed, VEnvelope(clearance, closing));

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
            if (FootprintRadiusM < 0)
                throw new ArgumentException("LocalPlannerConfig.FootprintRadiusM musi byt >= 0.");
            if (EdgeMarginM <= 0)
                throw new ArgumentException("LocalPlannerConfig.EdgeMarginM musi byt > 0 (sirka podelne rampy).");
        }
    }
}
