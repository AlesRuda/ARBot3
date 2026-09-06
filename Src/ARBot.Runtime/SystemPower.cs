using System;
using System.Diagnostics;

namespace ARBot.Robot
{
    /// <summary>
    /// <b>Vypnutí celého zařízení</b> (nejen aplikace). Spustí příkaz z parametru
    /// <c>poweroffcmd=</c> a počká, jestli se hned nevrátil s chybou.
    ///
    /// <para><b>Nač to je:</b> robot na soutěži nemá klávesnici ani displej a vytáhnout mu napájení
    /// za běhu znamená useknutý záznam a nedopsaný souborový systém. Ze stránky náhledu jde proto
    /// robota <b>bezpečně vypnout</b>: aplikace nejdřív zastaví runtime (dojede fronty, uzavře
    /// záznam, zastaví motory) a teprve pak dá systému pokyn k vypnutí.</para>
    ///
    /// <para><b>Příkaz je parametr, ne konstanta</b>, protože se liší stroj od stroje: na Armbianu
    /// s bezheslovým sudo stačí <c>sudo /sbin/poweroff</c>, jinde <c>systemctl poweroff</c>
    /// (polkit) nebo nic, když se to zakázat má. Prázdná hodnota funkci <b>vypíná</b> a stránka
    /// tlačítko vůbec neukáže.</para>
    ///
    /// <para><b>Selhání se musí dozvědět obsluha.</b> Když příkaz chybí, není povolený nebo skončí
    /// nenulovým kódem, vrací se důvod volajícímu — stránka ho ukáže. Robot, který na „vypnout"
    /// mlčky nic neudělá, je horší než ten, který řekne proč.</para>
    /// </summary>
    public static class SystemPower
    {
        /// <summary>Jak dlouho se čeká, jestli příkaz nespadne hned [ms].</summary>
        private const int ExitWaitMs = 3000;

        /// <summary>
        /// Spustí příkaz vypnutí. Vrací <c>null</c> při úspěchu (nebo když se příkaz do limitu
        /// nevrátil — to je normální, systém se vypíná), jinak důvod selhání.
        /// </summary>
        /// <param name="command">Celý příkaz i s argumenty, např. <c>sudo /sbin/poweroff</c>.
        /// Prázdný = funkce je vypnutá.</param>
        public static string TryPowerOff(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return "vypnuti neni povolene (poweroffcmd= je prazdny)";

            command = command.Trim();
            int mezera = command.IndexOf(' ');
            string exe = mezera < 0 ? command : command.Substring(0, mezera);
            string args = mezera < 0 ? string.Empty : command.Substring(mezera + 1);

            try
            {
                Trace.WriteLine($"Vypinam zarizeni: {command}");
                var p = Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                });
                if (p == null) return $"prikaz '{command}' se nepodarilo spustit";

                // Kdyz se prikaz do limitu nevrati, je to dobre znameni: system se vypina a nas
                // proces uz nema komu odpovedet. Chyba (chybejici sudo, zakazany polkit) se naopak
                // vraci hned.
                if (!p.WaitForExit(ExitWaitMs)) return null;
                if (p.ExitCode == 0) return null;

                string err = string.Empty;
                try { err = p.StandardError.ReadToEnd().Trim(); } catch { }
                return $"'{command}' skoncil s kodem {p.ExitCode}"
                       + (err.Length > 0 ? ": " + err : string.Empty);
            }
            catch (Exception ex)
            {
                return $"'{command}' selhal: {ex.Message}";
            }
        }
    }
}
