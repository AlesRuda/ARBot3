using System;
using System.Diagnostics;
using System.IO;

namespace ARBot.Robot
{
    /// <summary>
    /// <b>Zámek jedné instance</b> — soubor <c>arbot.lock</c> v datovém adresáři, držený otevřený
    /// po celou dobu běhu s <see cref="FileShare.None"/>.
    ///
    /// <para><b>Proč to existuje (5. 9. 2026):</b> na zařízení pouští aplikaci systemd jednotka.
    /// Když se pak člověk připojí přes ssh a spustí ji ručně, sáhne <b>druhá instance na tytéž
    /// UARTy a kamery</b>. Port webového náhledu se ošetří sám („bez nahledu" a jede se dál) —
    /// a právě to je ta zákeřná varianta: stránka ukazuje první instanci, zatímco ovládat můžeš
    /// druhou. Viz doc/plan-headless-provoz.md, návrh G.</para>
    ///
    /// <para><b>Bere se PŘED hardwarem</b>, aby druhá instance nesáhla na porty ani na chvíli.</para>
    ///
    /// <para><b>Zámek souboru, ne pidfile:</b> .NET mapuje <see cref="FileShare.None"/> na Unixu na
    /// <c>flock</c>, takže zámek <b>padá s procesem</b> — po tvrdém zabití nebo pádu nezůstane
    /// viset mrtvý pidfile, který by příští start blokoval a nikdo by nevěděl proč.</para>
    /// </summary>
    public sealed class SingleInstanceLock : IDisposable
    {
        /// <summary>Návratový kód procesu, když už jiná instance běží.</summary>
        public const int ExitCodeAlreadyRunning = 3;

        /// <summary>Jméno zámkového souboru v datovém adresáři.</summary>
        public const string FileName = "arbot.lock";

        private FileStream stream;

        private SingleInstanceLock(FileStream stream, string path)
        {
            this.stream = stream;
            Path = path;
        }

        /// <summary>Cesta k zámkovému souboru.</summary>
        public string Path { get; }

        /// <summary>
        /// Zkusí zámek získat. Vrací <c>null</c>, když už jiná instance běží (nebo když adresář
        /// není zapisovatelný) — důvod je v <paramref name="error"/> a patří na stderr.
        /// </summary>
        /// <param name="directory">Adresář zámku; obvykle datový adresář (<c>dataroot=</c>).</param>
        public static SingleInstanceLock TryAcquire(string directory, out string error)
        {
            error = null;
            string path = null;
            try
            {
                path = System.IO.Path.Combine(directory, FileName);
                Directory.CreateDirectory(directory);

                // DeleteOnClose se ZAMERNE nepouziva: na Windows by zamek nesel vzit, dokud se
                // soubor po padu neuvolni, a na Unixu by smazani inodu maskovalo, ze zamek drzi
                // nekdo jiny. Prazdny soubor, ktery zustane lezet, nikomu nevadi.
                var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    // Do zamku PID a cas. Je to FORENZNI udaj: za behu soubor otevrit nejde
                    // (FileShare.None nepusti ani ctenare), ale po ukonceni nebo padu rekne, ktery
                    // proces tu byl posledni. Systemovy cas schvalne - kalendarni udaj pro cloveka.
                    var w = new StreamWriter(fs) { AutoFlush = true };
                    w.Write($"pid={Environment.ProcessId} start={DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
                }
                catch (Exception ex)
                {
                    // Zapis obsahu je jen pohodli; kdyz selze, zamek uz drzime a to je to podstatne.
                    Trace.WriteLine("Zamek instance: obsah se nepodarilo zapsat: " + ex.Message);
                }

                return new SingleInstanceLock(fs, path);
            }
            catch (IOException ex)
            {
                error = $"Uz bezi jina instance ARBota (zamek {path}). "
                        + $"Pod systemd: 'systemctl status arbot', pripadne 'systemctl stop arbot'. "
                        + $"[{ex.Message}]";
                return null;
            }
            catch (Exception ex)
            {
                error = $"Zamek {path} nejde vzit: {ex.Message}";
                return null;
            }
        }

        public void Dispose()
        {
            try { stream?.Dispose(); }
            catch (Exception ex) { Trace.WriteLine("Zamek instance: uvolneni selhalo: " + ex.Message); }
            stream = null;
        }
    }
}
