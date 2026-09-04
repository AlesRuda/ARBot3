using System;
using System.Diagnostics;
using ARBot.Common.Configuration;
using Avalonia.Threading;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Otevre pohledy vyjmenovane v parametru <c>open=</c> hned po startu aplikace
    /// (napr. <c>open=world,telemetry</c>), v uvedenem poradi - posledni je aktivni zalozka.
    ///
    /// <para><b>Nac to je:</b> na zarizeni se aplikace pousti pres SSH profilem a dohlizi se na ni
    /// pres vzdalenou plochu z mobilu, kde je menu prakticky neovladatelne. Self-test umel okna
    /// otevrit (<c>st_world</c>, <c>st_robot</c>, <c>st_images</c>), ale jen jako soucast mereni,
    /// ktere aplikaci po case ukonci. Tohle je totez pro bezny beh a pro vsechny pohledy z menu
    /// Tools. Doplnuje <c>autorun=</c>: profil tak popise i to, co ma obsluha videt.</para>
    ///
    /// <para>Otevira se pres TYTEZ metody jako menu Tools, takze plati jejich deduplikace (uz otevreny
    /// pohled se jen aktivuje) a napojeni na runtime. Jmena pohledu drzi
    /// <see cref="ParamParsers.ViewNames"/> v <c>Common</c>, aby registr odmitl neznamy pohled uz pri
    /// startu (viz doc/configuration.md); pridani pohledu = jmeno tam a vetev v <see cref="OpenView"/>.</para>
    ///
    /// <para>Se self-testem se to sklada: kdyz self-test pozdeji otevre World jako posledni (kvuli
    /// snimku), uz otevreny pohled se jen aktivuje.</para>
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>Otevre pohledy z <c>open=</c>, je-li zadan. Bezi z konstruktoru.</summary>
        private void OpenViewsIfRequested()
        {
            string raw = ParamRegistry.Open.Value;
            if (string.IsNullOrWhiteSpace(raw))
                return;

            // Registr hodnotu overil uz pri startu (neznamy pohled = chyba konfigurace); tady jen
            // pro jistotu, kdyby se ke ctenari dostala jinou cestou.
            if (!ParamParsers.TryViews(raw, out string[] views, out string unknown))
            {
                Trace.WriteLine($"open={raw}: neznamy pohled '{unknown}', nic se neotevira.");
                return;
            }

            // Az po vykresleni okna (Background priorita): dok uz v konstruktoru existuje, ale
            // aktivace/focus zalozky pred prvnim layoutem se nechyti - stejny duvod, proc self-test
            // otevira okna az z Dispatcheru.
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var v in views)
                    OpenView(v);
            }, DispatcherPriority.Background);
        }

        /// <summary>Otevre jeden pohled podle jmena z <see cref="ParamParsers.ViewNames"/>.</summary>
        private void OpenView(string name)
        {
            try
            {
                switch (name)
                {
                    case "sensors": OpenSensors(); break;
                    case "images": OpenImages(); break;
                    case "robot": OpenRobotCentric(); break;
                    case "world": OpenWorldView(); break;
                    case "telemetry": OpenTelemetry(); break;
                    case "debug": OpenDebugOutput(); break;
                    case "virtual": OpenVirtualSensors(); break;
                    case "robotour": OpenRobotourMission(); break;
                    case "config": OpenConfiguration(); break;
                    case "perf": OpenPerformance(); break;
                    default:
                        // ViewNames a tenhle switch se musi shodovat - kdyz ne, at je to videt v logu.
                        Trace.WriteLine($"open={name}: jmeno je v ParamParsers.ViewNames, ale OpenView ho nezna.");
                        return;
                }
                Trace.WriteLine($"open: pohled '{name}' otevren.");
            }
            catch (Exception ex)
            {
                // Jeden pohled nesmi shodit otevirani ostatnich (ani start aplikace).
                Trace.WriteLine($"open={name}: otevreni selhalo: {ex.Message}");
            }
        }
    }
}
