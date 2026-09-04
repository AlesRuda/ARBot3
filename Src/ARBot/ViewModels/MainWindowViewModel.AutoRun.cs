using ARBot.Common.Configuration;
using System.Threading.Tasks;
using ARBot.Diagnostics;
using ARBot.Robot;
using Avalonia.Threading;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Automatické spuštění režimu <b>Run</b> po startu aplikace (parametr <c>autorun=true</c>).
    ///
    /// <para><b>Nač to je:</b> na zařízení se aplikace pouští přes SSH profilem a obsluha u ní
    /// nesedí — bez tohohle by se musel Run po každém startu naklikat v UI. Doplňuje
    /// <c>mission=</c> a <c>record=</c>: profil tak popíše celý běh od startu po záznam.</para>
    ///
    /// <para>⚠️ <b>Bezpečnost:</b> se zapnutou misí (<c>mission=freerun</c> / <c>robotour</c>) se
    /// robot po startu aplikace <b>rozjede sám</b>, bez dalšího pokynu. Jediné, co ho zastaví, je
    /// nouzové zastavení nebo <i>Stop</i> v UI. Zapínat jen když se s tím počítá; výchozí je
    /// vypnuto. Prodleva níž je na <b>ustálení</b>, ne bezpečnostní — skutečná pojistka je
    /// fyzické nouzové zastavení.</para>
    ///
    /// <para>Postup je záměrně týž jako u self-testu (<see cref="StartSelfTestIfRequested"/>):
    /// počkat na HW, nechat UI ustálit, teprve pak Run. Bez čekání by Run startoval nad
    /// polovičním HW — kamery i porty se otevírají líně.</para>
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>
        /// Jak dlouho [ms] po připravení HW se čeká, než se spustí Run. Je to na <b>ustálení</b>
        /// UI a senzorů, ne bezpečnostní prodleva.
        /// </summary>
        private const int AutoRunSettleMs = 3000;

        /// <summary>Spustí Run po startu, je-li vyžádán parametrem <c>autorun=true</c>.</summary>
        private void StartAutoRunIfRequested()
        {
            if (!ParamRegistry.AutoRun.Value)
                return;

            // Self-test si Run spousti sam a pak aplikaci ukonci - dva autostarty by se praly
            // (druhy Start() nejdriv zavola Stop() toho prvniho a mereni by bylo k nicemu).
            if (SelfTestConfig.FromArgs().Enabled)
            {
                System.Diagnostics.Trace.WriteLine(
                    "autorun=true se IGNORUJE: bezi selftest, ktery si Run spousti sam.");
                return;
            }

            _ = AutoRunAsync();   // fire-and-forget, stejne jako self-test
        }

        private async Task AutoRunAsync()
        {
            try
            {
                System.Diagnostics.Trace.WriteLine(
                    "autorun=true: rezim Run se spusti SAM, jakmile bude HW pripravene. "
                    + "POZOR: je-li zapnuta mise, robot se rozjede bez dalsiho pokynu.");

                // Kamery a porty se otviraji lene - bez tohohle by Run startoval nad polovicnim HW.
                await Task.Run(() => ARBotHW.Current.WaitReady());
                await Task.Delay(AutoRunSettleMs);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // RunMode() zamerne, ne RunAndLog(): zaznam resi parametr record= uvnitr
                    // ARBotRuntime.Start, takze se obe volby skladaji a nedubluje se logika.
                    RunMode();
                    System.Diagnostics.Trace.WriteLine("autorun: rezim Run spusten.");
                });
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("autorun selhal: " + ex);
            }
        }
    }
}
