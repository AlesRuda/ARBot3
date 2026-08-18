using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Models;
using ARBot.Common.Occupancy;
using ARBot.Common.Telemetry;

namespace ARBot.Telemetry
{
    /// <summary>
    /// Registr sloupcu telemetrickeho pohledu - <b>pridat udaj = jeden zaznam v <see cref="All"/></b>.
    ///
    /// <para>Zamerne v UI vrstve: jednotky, format a "co ma smysl kreslit" jsou prezentacni vec,
    /// takze se kvuli nim nesaha do <c>ARBot.Common</c> a domena nedostane starosti UI.
    /// Viz doc/telemetry-view.md.</para>
    ///
    /// <para>Registr zaroven urcuje, ktere zpravy sken ze zaznamu vubec cte - typ, ktery tu nema
    /// sloupec, se preskoci bez cteni.</para>
    /// </summary>
    public static class TelemetryColumns
    {
        /// <summary>Vsechny sloupce v poradi, v jakem se maji zobrazit.</summary>
        public static IReadOnlyList<ColumnSpec> All { get; } = Build();

        private static List<ColumnSpec> Build()
        {
            var list = new List<ColumnSpec>
        {
            // --- fuzovana poza ---
            Num<RobotStateMsg>("X [m]", m => m.X,
                "Poloha robotu v ose X (world ENU, východ) z fúze EKF. Počátek = místo startu."),
            Num<RobotStateMsg>("Y [m]", m => m.Y,
                "Poloha robotu v ose Y (world ENU, sever) z fúze EKF. Počátek = místo startu."),
            Num<RobotStateMsg>("theta [°]", m => Conversions.Rad2Deg(m.Theta),
                "Kurz robotu z fúze: matematická orientace ve world ENU (0° = východ, kladně proti "
                + "směru hodinových ručiček). Tlačítkem Azimut v liště se přepne na světovou konvenci "
                + "(0° = sever, po směru hodinových ručiček).", "F1"),
            Num<RobotStateMsg>("v [m/s]", m => m.V,
                "Skutečná dopředná rychlost robotu z fúze (odometrie + IMU + GPS), ne požadovaná."),
            Num<RobotStateMsg>("omega [°/s]", m => Conversions.Rad2Deg(m.Omega),
                "Skutečná úhlová rychlost otáčení z fúze; kladně = doleva (proti hodinovým "
                + "ručičkám); s přepínačem Azimut je kladně doprava.", "F1"),

            // --- ridici zasah ---
            Num<DriveCommandMsg>("cmd v [m/s]", m => m.Speed,
                "Požadovaná dopředná rychlost poslaná do motorů. Rozdíl proti „v“ ukazuje, jak "
                + "robot na příkaz reaguje."),
            Num<DriveCommandMsg>("cmd omega [°/s]", m => Conversions.Rad2Deg(m.RotationSpeed),
                "Požadovaná úhlová rychlost poslaná do motorů; kladně = doleva (s přepínačem Azimut doprava).", "F1"),
            Num<DriveCommandMsg>("cmd dif [m/s]", m => m.Dif,
                "Rozdíl rychlostí levého a pravého pásu odpovídající požadovanému zatáčení "
                + "(diferenciální řízení)."),
            Flag<DriveCommandMsg>("STOP", m => m.EmergencyStop,
                "Nouzové zastavení v tomto příkazu: „STOP“ = motory zastaveny bezpečnostní "
                + "podmínkou, „-“ = jede se normálně."),

            // --- lokalni plan ---
            Enum<LocalPlanMsg, LocalPlanStatus>("plan stav", m => m.Status,
                "Výsledek lokálního plánovače nad occupancy gridem (nalezena cesta / cíl "
                + "nedosažitelný / bez cíle…). Viz doc/occupancy-and-local-planning.md."),
            Num<LocalPlanMsg>("plan delka [m]", m => m.LengthM,
                "Délka naplánované lokální cesty od robotu k lokálnímu cíli („mrkvi“)."),
            Num<LocalPlanMsg>("plan odstup [m]", m => m.MinClearanceM,
                "Nejmenší odstup naplánované cesty od překážky. Malá hodnota = plán vede těsně "
                + "kolem překážky, plánovač tam sráží rychlost."),
            Num<LocalPlanMsg>("plan bodu", m => m.WayPoints?.Length ?? 0,
                "Počet waypointů, které plánovač předal regulátoru.", "F0"),
            Num<LocalPlanMsg>("plan vypocet [ms]", m => m.ComputeMs,
                "Doba výpočtu jednoho lokálního plánu (A*). Diagnostika zátěže řídicí smyčky.", "F1"),

            // --- globalni navigace ---
            Enum<GlobalNavMsg, GlobalNavStatus>("nav stav", m => m.Status,
                "Stav globální navigace nad OSM sítí (bez cíle / jede k cíli / cíl dosažen / "
                + "bloudění / zásek…). Viz doc/global-navigation-runtime.md."),
            Num<GlobalNavMsg>("do cile [m]", m => m.RouteLengthM,
                "Zbývající délka trasy do cíle měřená po síti cest (ne vzdušnou čarou).", "F0"),
            Num<GlobalNavMsg>("hran trasy", m => m.RouteEdgeCount,
                "Počet hran grafu, které do cíle ještě zbývají projet.", "F0"),
            Num<GlobalNavMsg>("od site [m]", m => m.OffRouteDist,
                "Kolmá vzdálenost robotu od hrany, po které má jet. Rostoucí hodnota = robot "
                + "sjíždí z trasy."),
            // Tri desetinna mista: phi se mezi takty meni o zlomky sekundy a na F1 vypadalo
            // zamrzle (dva sousedni radky mely tutez hodnotu, i kdyz robot popojel).
            Num<GlobalNavMsg>("fi [s]", m => m.Phi,
                "Cost-to-goal aktuální pozice v poli globální navigace (odhad zbývajícího času "
                + "jízdy do cíle podle dopravního profilu).", "F3"),
            Num<GlobalNavMsg>("uzavreno hran", m => m.ClosureCount,
                "Kolik hran navigace zatím uzavřela jako neprůjezdné (přehrazená cesta). Skok "
                + "znamená, že se právě přeplánovávalo.", "F0"),

            // --- surove GPS (bez fuze) ---
            Num<GPSState>("GPS lat [°]", m => m.Latitude,
                "Zeměpisná šířka přímo z GPS přijímače (WGS84), tedy bez fúze.", "F6"),
            Num<GPSState>("GPS lon [°]", m => m.Longitude,
                "Zeměpisná délka přímo z GPS přijímače (WGS84), tedy bez fúze.", "F6"),
            Enum<GPSState, GPSState.FixQuality>("GPS fix", m => (int)m.Quality,
                "Kvalita fixu hlášená přijímačem (žádný fix / GPS / DGPS / RTK…). Určuje, jakou "
                + "váhu má GPS ve fúzi."),
            Num<GPSState>("GPS satelitu", m => m.NumberOfSatellites,
                "Počet družic použitých k výpočtu polohy. Propad bývá první příznak zákrytu.", "F0"),
            Num<GPSState>("GPS HDOP", m => m.Hdop,
                "Horizontální rozptyl přesnosti — čím menší, tím lepší geometrie družic "
                + "(pod 1 výborné, nad 5 nepoužitelné)."),

            // --- surove GPS: dopocitane udaje (kurz a rychlost z pohybu nebo ze dvou anten) ---
            Num<GPSState>("GPS alt [m]", m => m.Altitude,
                "Výška nad elipsoidem z GPS. Bývá výrazně horší než poloha, do fúze nevstupuje.", "F1"),
            Num<GPSState>("GPS v [m/s]", m => m.Speed ?? m.DynamicSpeed,
                "Rychlost z přijímače (dvouanténní, jinak dopočtená z pohybu). Nezávislá kontrola "
                + "rychlosti z fúze i z kol."),
            Num<GPSState>("GPS kurz [°]", m => Deg(m.Orientation ?? m.DynamicOrientation),
                "Kurz z GPS: přednost má dvouanténní Orientation, jinak se dopočítá z pohybu "
                + "(v klidu je nesmyslný). Zobrazuje se v konvenci zvolené v liště.", "F0"),

            // --- motory (MotorStateBase) - to, co robot skutecne udelal, proti tomu co dostal prikazem ---
            Num<MotorStateBase>("kolo L [m/s]", m => m.LeftWheelSpeed,
                "Skutečná rychlost levého kola. Driver ji měří ze svého vzorkovacího intervalu, "
                + "takže nezávisí na tom, kdo a kdy měření vyzvedl."),
            Num<MotorStateBase>("kolo R [m/s]", m => m.RightWheelSpeed,
                "Skutečná rychlost pravého kola. Rozdíl proti levému ukazuje reálné zatáčení."),
            // Odometrie prepoctena na pohyb robotu - tataz dvojice velicin, jakou zadava ridici
            // smycka (cmd v / cmd omega) a jakou vydava fuze (v / omega), takze jdou srovnat
            // vedle sebe: prikaz -> skutecnost -> co si o tom mysli fuze.
            Num<MotorStateBase>("odo v [m/s]", m => (m.LeftWheelSpeed + m.RightWheelSpeed) / 2,
                "Dopředná rychlost robotu spočtená z rychlostí kol ((vL + vR) / 2). Měřená "
                + "skutečnost, bez fúze a bez GPS."),
            Num<MotorStateBase>("odo omega [°/s]", m => Conversions.Rad2Deg(
                    (m.RightWheelSpeed - m.LeftWheelSpeed) / Profile.Rozchod),
                "Rychlost otáčení z odometrie ((vR − vL) / rozchod, rozchod " + Profile.Rozchod
                + " m) — tentýž vzorec, jakým ji počítá fúze. Rozdíl proti „cmd omega“ ukazuje, "
                + "jestli robot zatáčku vůbec provedl.", "F1"),
            Num<MotorStateBase>("enc L [m]", m => m.LeftEncoder,
                "Kumulativní ujetá dráha levého kola od startu (od verze 2 zprávy kumulativní, "
                + "ne přírůstek).", "F1"),
            Num<MotorStateBase>("enc R [m]", m => m.RightEncoder,
                "Kumulativní ujetá dráha pravého kola od startu.", "F1"),
            Num<MotorStateBase>("bat [V]", m => m.Voltage,
                "Napětí baterie. Propad při rozjezdu = zatížení, trvalý pokles = vybíjení."),
            Num<MotorStateBase>("I L [A]", m => m.LeftMotorCurrent,
                "Proud levého motoru. Vysoký proud při malé rychlosti = zablokované kolo nebo "
                + "těžký terén."),
            Num<MotorStateBase>("I R [A]", m => m.RightMotorCurrent,
                "Proud pravého motoru."),
            Flag<MotorStateBase>("HW STOP", m => m.IsEmergencyStop,
                "Nouzové zastavení hlášené HARDWAREM motorů. Proti sloupci „STOP“ (co si o stopu "
                + "myslela řídicí smyčka) ukáže, jestli se smyčka a hardware shodnou."),
            Num<MotorStateBase>("mot drop", m => m.DropedOutNum,
                "Kolik vzorků driver motorů zahodil před tímto (nestíhané čtení). Nenulové = "
                + "měření chybí a odometrie ve fúzi je řidší, než by měla být.", "F0"),

            // --- IMU (IMUState) - orientace a dynamika v telovem ramci FLU (X vpred, Y vlevo, Z nahoru) ---
            Num<IMUState>("IMU yaw [°]", m => Deg(m.YPR()?.Yaw),
                "Kurz z IMU dopočtený z kvaternionu, ve stejné konvenci jako „theta“ (řídí ji přepínač Azimut). Proti "
                + "„theta“ z fúze ukáže, jak moc fúze IMU koriguje.", "F1"),
            Num<IMUState>("IMU pitch [°]", m => Deg(m.YPR()?.Pitch),
                "Naklonění dopředu/dozadu. Ve svahu vysvětluje, proč robot zpomalil nebo prokluzuje.", "F1"),
            Num<IMUState>("IMU roll [°]", m => Deg(m.YPR()?.Roll),
                "Naklonění do stran. Velká hodnota = riziko převrácení.", "F1"),
            Num<IMUState>("gyro z [°/s]", m => Deg(m.AngularVelocity?.Z),
                "Rychlost otáčení kolem svislé osy těla (Z nahoru), tedy měřená rychlost zatáčení; znaménko řídí přepínač Azimut. "
                + "Srovnej s „omega“ z fúze a „cmd omega“ z příkazu.", "F1"),
            Num<IMUState>("acc x [m/s²]", m => m.Acceleration?.X,
                "Zrychlení v podélné ose těla (X vpřed). Skoky = rázy z terénu nebo tvrdé brzdění."),
            Num<IMUState>("acc z [m/s²]", m => m.Acceleration?.Z,
                "Zrychlení ve svislé ose těla (Z nahoru). Obsahuje i tíhové zrychlení, pokud ho "
                + "driver neodečítá — sleduj spíš rozkmit než absolutní hodnotu."),
            Num<IMUState>("IMU conf", m => m.Confidence,
                "Důvěra hlášená IMU k tomuto vzorku (0–1). Nízká = orientaci neber vážně."),
            Num<IMUState>("IMU drop", m => m.DropedOutNum,
                "Kolik vzorků IMU se zahodilo před tímto. Nenulové = fúze má ve vstupu díry.", "F0"),
            };

            // Uhlove udaje na JEDNOM miste - jinak se konvence rozejdou (drive mel GPS kurz
            // azimut, kdezto theta a IMU yaw matematickou orientaci). Ulozeno je vzdy matematicky
            // (kurz 0 = vychod a +CCW, kladna rychlost = doleva); prepnuti zobrazeni resi lista
            // pohledu pres TelemetryTable.AngleMode. Viz doc/telemetry-view.md.
            Mark(list, AngleKind.Heading, "theta [°]", "IMU yaw [°]", "GPS kurz [°]");
            Mark(list, AngleKind.Rate, "omega [°/s]", "cmd omega [°/s]", "gyro z [°/s]",
                 "odo omega [°/s]");
            // POZN.: IMU pitch/roll jsou naklony, ne kurzy - konvence se jich netyka.

            return list;
        }

        /// <summary>
        /// Oznaci sloupce podle zahlavi jako uhlove. <b>Neznamé zahlavi je chyba</b> - kdyby se
        /// sloupec prejmenoval, priznak by se jinak tise ztratil a tabulka by zase michala dve
        /// konvence.
        /// </summary>
        private static void Mark(List<ColumnSpec> columns, AngleKind kind, params string[] headers)
        {
            foreach (var header in headers)
            {
                var spec = columns.Find(c => c.Header == header);
                if (spec == null)
                    throw new InvalidOperationException(
                        $"TelemetryColumns: sloupec \"{header}\" neexistuje, nelze mu nastavit {kind}.");
                spec.Angle = kind;
            }
        }

        /// <summary>
        /// Ciselny sloupec z jedne zpravy. <c>MsgName</c> se bere z prototypu, aby se nazev typu
        /// nepsal jako retezec (a nerozesel se pri prejmenovani).
        /// </summary>
        /// <param name="description">Vysvetleni udaje do tooltipu (zahlavi je jen zkratka).</param>
        private static ColumnSpec Num<T>(string header, Func<T, double?> value, string description,
                                         string format = "F2")
            where T : Message, new()
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Description = description,
                Format = format,
                Value = m => m is T typed ? value(typed) : (double?)null,
            };

        /// <summary>
        /// Radiany na stupne pro NEPOVINNOU hodnotu - senzorova pole jsou nullable (IMU nemusi
        /// dodat orientaci ani gyro), a chybejici hodnota musi zustat chybejici, ne nula.
        /// </summary>
        private static double? Deg(double? rad)
            => rad.HasValue ? Conversions.Rad2Deg(rad.Value) : (double?)null;

        /// <summary>Logicky sloupec: zobrazi se jako zkratka (kdyz plati), jinak pomlcka.</summary>
        private static ColumnSpec Flag<T>(string header, Func<T, bool> value, string description)
            where T : Message, new()
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Description = description,
                Format = "F0",
                Value = m => m is T typed ? (value(typed) ? 1.0 : 0.0) : (double?)null,
                Text = v => v != 0 ? header : "-",
            };

        /// <summary>Vyctovy sloupec: v tabulce jmeno hodnoty, v grafu schod.</summary>
        private static ColumnSpec Enum<T, TEnum>(string header, Func<T, int> value, string description)
            where T : Message, new() where TEnum : struct, System.Enum
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Description = description,
                Format = "F0",
                Value = m => m is T typed ? value(typed) : (double?)null,
                Text = v => System.Enum.IsDefined(typeof(TEnum), (int)v)
                            ? ((TEnum)(object)(int)v).ToString()
                            : ((int)v).ToString(),
            };
    }
}
