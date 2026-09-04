using System;
using System.Diagnostics;
using System.Globalization;
using ARBot.Common.Configuration;

namespace ARBot.Robot
{
    /// <summary>
    /// <b>Bootstrap konfigurace runtime</b> - společný začátek obou aplikací nad <c>ARBot.Runtime</c>
    /// (Avalonia UI <c>ARBot</c> i konzolový <c>ARBot.Headless</c>): složí <see cref="ParamStore"/>
    /// z příkazové řádky, vypíše účinnou konfiguraci a přenese bezpečnostní stropy
    /// (<c>maxspeed=</c>, <c>safedist=</c>) do <see cref="Profile"/>.
    ///
    /// <para><b>Pořadí volání</b> je v obou aplikacích stejné a záměrné:
    /// <c>CrashLog.Install()</c> → <see cref="TryConfigure"/> → aplikace (UI / Run). Konfigurace se
    /// skládá <b>jako první</b> - dřív, než cokoliv sáhne na <c>ParamRegistry.X.Value</c>. Vadná
    /// konfigurace má skončit hláškou, ne výjimkou v půlce startu: aplikace by pak běžela s něčím
    /// jiným, než co je v profilu napsáno, a nikdo by se to nedozvěděl. Viz doc/configuration.md.</para>
    ///
    /// <para>Do 4. 9. 2026 to všechno dělal <c>Program.Main</c> UI aplikace; sem se to přesunulo
    /// <b>beze změny logiky</b>, aby to headless nemusel opisovat. O ukončení procesu při chybě
    /// rozhoduje volající (UI <c>Environment.Exit(2)</c>, headless <c>return 2</c>), proto metoda
    /// vrací hlášku a sama nic nezavírá.</para>
    /// </summary>
    public static class RuntimeBootstrap
    {
        /// <summary>Návratový kód procesu při vadné konfiguraci - stejný v UI i headless.</summary>
        public const int ExitCodeBadConfig = 2;

        /// <summary>
        /// Sestaví <see cref="ParamStore"/> z příkazové řádky, vypíše účinnou konfiguraci přes
        /// <paramref name="log"/> a přenese <c>maxspeed=</c> / <c>safedist=</c> do <see cref="Profile"/>.
        /// </summary>
        /// <param name="commandLine">Argumenty včetně cesty k exe (<c>Environment.GetCommandLineArgs()</c>);
        /// cizí argumenty bez <c>=</c> store ignoruje.</param>
        /// <param name="log">Odběratel řádků konfigurace. UI předává <c>Debug.WriteLine</c> (výpis jde
        /// do panelu Debug output a přes Info zprávu do záznamu), headless <c>Trace.WriteLine</c>
        /// (konzole + <c>TraceInfoBridge</c> do záznamu). <c>Debug.WriteLine</c> má <c>[Conditional]</c>,
        /// takže se předává lambdou, ne skupinou metod.</param>
        /// <returns><c>null</c> při úspěchu, jinak chybová hláška (začíná „Chyba konfigurace:").</returns>
        public static string? TryConfigure(string[] commandLine, Action<string> log)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));

            try
            {
                var store = ParamStore.Build(commandLine);
                if (store.ConfigPath != null)
                    log("Konfigurace: profil " + store.ConfigPath);
                foreach (var w in store.Warnings)
                    log("Konfigurace: " + w);

                // Cela ucinna konfigurace s puvodem, jednou - to je stopa konfigurace v zaznamu.
                // Do 4. 9. 2026 se misto toho psal kazdy parametr az pri cteni, takze zaznam nesl
                // jen to, co se nahodou precetlo.
                foreach (var line in store.DescribeAll())
                    log(line);
            }
            catch (ParamFileException ex)
            {
                return "Chyba konfigurace: " + ex.Message;
            }

            // Strop rychlosti z parametru maxspeed=. MUSI to byt tady, pred startem UI / Run.
            ApplyMaxSpeedFromParams();
            // Tvrdy odstup od prekazek z parametru safedist= - tentyz duvod a mechanismus.
            ApplySafeDistFromParams();
            return null;
        }

        /// <summary>
        /// Prenese <c>maxspeed=</c> do <see cref="Profile.MaxAllowedSpeed"/>.
        /// Bez zadaneho parametru nedela nic (plati hodnota z kodu).
        ///
        /// <para><b>Proc uz pri startu a ne az u ctenare:</b> tu hodnotu ctou TRI ruzna mista, a to
        /// pri VZNIKU objektu - driver motoru <c>SDC2160Ex</c> a <c>TrapezoidMotionProfile</c>
        /// v konstruktoru, <c>LocalPlannerConfig.MaxSpeed</c> inicializatorem pole. Kdo by vznikl
        /// driv, nez se hodnota nastavi, drzel by tu starou a strop by platil jen zcasti - coz je
        /// u bezpecnostniho omezeni to nejhorsi, co muze byt.</para>
        ///
        /// <para><b>Past s odvozenymi statickymi poli</b> (kvuli ni <c>Profile</c> v registru
        /// zamerne neni - viz doc/configuration.md) se tady NEOTVIRA: z <c>MaxAllowedSpeed</c>
        /// nic nederivuje. <c>MaxTheoreticalSpeed</c> se pocita z obvodu kola a otacek motoru,
        /// ne z nej.</para>
        /// </summary>
        private static void ApplyMaxSpeedFromParams()
        {
            // Default parametru JE hodnota z Profile, takze bez zadani neni co prenaset.
            if (!ParamRegistry.MaxSpeed.IsSet)
                return;

            double v = ParamRegistry.MaxSpeed.Value;

            // Nad technicky dosazitelnou rychlost to nema smysl: motor tam nedojede a rychlostni
            // profil by planoval s cislem, ktere se nikdy nenaplni. Orezat je lepsi nez odmitnout
            // start - zamer ("jed naplno") je jednoznacny a robot v terenu ma jet. Nekladnou
            // hodnotu odmitne uz registr pri nacteni profilu, sem se nedostane.
            double strop = Profile.MaxTheoreticalSpeed;
            if (v > strop)
            {
                Trace.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "maxspeed={0:F2} je nad technicky dosazitelnou rychlosti {1:F2} m/s -> orezano.",
                    v, strop));
                v = strop;
            }

            Trace.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "maxspeed: strop rychlosti {0:F2} -> {1:F2} m/s (plati pro motor, rychlostni profil "
                + "i obalku planovace).",
                Profile.MaxAllowedSpeed, v));
            Profile.MaxAllowedSpeed = v;
        }

        /// <summary>
        /// Prenese <c>safedist=</c> do <see cref="Profile.SafeDist"/> - tvrdeho minimalniho odstupu
        /// od prekazek lokalniho planovace. Bez zadaneho parametru nedela nic (plati hodnota z kodu).
        ///
        /// <para>Stejny mechanismus a stejne duvody jako <see cref="ApplyMaxSpeedFromParams"/>:
        /// <c>LocalPlannerConfig.SafeDist</c> se inicializuje z <c>Profile</c> pri VZNIKU instance
        /// (hlida <c>SafeDistParamTests</c>), takze hodnota musi byt nastavena pred slozenim runtime.
        /// Z <c>SafeDist</c> nic staticky nederivuje, past s odvozenymi poli se neotvira.</para>
        ///
        /// <para><b>Vazba na <c>PrefDist</c>:</b> <c>LocalPlannerConfig.Validate()</c> vyzaduje
        /// <c>PrefDist &gt; SafeDist</c>. Kdyby odstup skoncil na <c>PrefDist</c> nebo nad nim,
        /// planovac by pri vzniku vyhodil vyjimku a runtime by se nesložil - kvuli parametru, ktery
        /// mel robota udelat opatrnejsim. Proto se v tom pripade <c>PrefDist</c> posune nad novy
        /// odstup se zachovanym puvodnim rozestupem (dnes 0,1 m) a zaloguje se to. Vzniklo 3. 9. 2026
        /// pri ladeni odstupu z rozboru „mrkev v nesjizdne oblasti" (doc/devlog.md).</para>
        /// </summary>
        private static void ApplySafeDistFromParams()
        {
            if (!ParamRegistry.SafeDist.IsSet)
                return;

            double puvodni = Profile.SafeDist;
            double pref = Profile.PrefDist;
            // Nekladnou hodnotu odmitne uz registr pri nacteni profilu, sem se nedostane.
            double v = ParamRegistry.SafeDist.Value;

            if (v >= pref)
            {
                double rozestup = pref - puvodni;
                if (!(rozestup > 0)) rozestup = 0.1;   // kdyby nekdo v kodu nastavil PrefDist <= SafeDist
                double novyPref = v + rozestup;
                Trace.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "safedist={0:F2} je na urovni PrefDist {1:F2} m nebo nad nim -> PrefDist posunut "
                    + "na {2:F2} m (zachovan rozestup {3:F2} m), jinak by planovac odmitl vzniknout.",
                    v, pref, novyPref, rozestup));
                Profile.PrefDist = novyPref;
            }

            Trace.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "safedist: tvrdy odstup od prekazek {0:F2} -> {1:F2} m (lokalni planovac: prujezdnost "
                + "i rychlostni obalka).",
                puvodni, v));
            Profile.SafeDist = v;
        }
    }
}
