using System;
using System.Diagnostics;
using System.IO;

namespace ARBot.Diagnostics
{
    /// <summary>
    /// Otevření souboru / složky v prostředí operačního systému (přidružená aplikace, správce souborů).
    /// Používá toolbar u pořízených snímků a záznamů (viz doc/screen-capture.md).
    /// Windows = <c>explorer.exe</c> (umí i označit soubor ve složce), Linux = <c>xdg-open</c>.
    /// </summary>
    public static class ShellOpen
    {
        /// <summary>Otevře soubor v přidružené aplikaci (prohlížeč obrázků / přehrávač).</summary>
        public static bool File(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return false;
            return Launch(path);
        }

        /// <summary>Otevře složku se souborem a soubor v ní označí (kde to jde); jinak jen složku.</summary>
        public static bool Reveal(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (OperatingSystem.IsWindows() && System.IO.File.Exists(path))
                {
                    // Uvozovky kolem cesty jsou nutné (mezery); /select, bez mezery za čárkou.
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                    {
                        UseShellExecute = true
                    });
                    return true;
                }
            }
            catch (Exception ex) { Debug.WriteLine("ShellOpen.Reveal: " + ex.Message); }

            // Fallback: aspoň otevřít složku, ve které soubor leží.
            return Folder(Path.GetDirectoryName(path));
        }

        /// <summary>Otevře složku ve správci souborů.</summary>
        public static bool Folder(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try { Directory.CreateDirectory(dir); } catch { }
            return Launch(dir);
        }

        private static bool Launch(string target)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // UseShellExecute = přidružená aplikace podle přípony (u složky správce souborů).
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("xdg-open", $"\"{target}\"") { UseShellExecute = false });
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ShellOpen: " + ex.Message);
                return false;
            }
        }
    }
}
