using System;
using System.IO;

namespace ARBot.Common.Configuration
{
    /// <summary>
    /// Reseni cest relativne ke KORENI REPA (slozka s <c>.git</c>), ne k pracovnimu adresari
    /// procesu. Pracovni adresar se lisi podle toho, jak se app spusti (z VS je to build output
    /// <c>bin\...</c>, z <c>dotnet run</c> slozka projektu), takze <c>map=OSM/Neco.osm</c> by
    /// jednou nasel a jindy ne. Proti koreni repa to plati vzdy - diky tomu mohou byt cesty
    /// v <c>launchSettings.json</c> i v profilech relativni, a tedy prenositelne mezi pracovnimi
    /// kopiemi.
    ///
    /// <para>Bez repa (nasazeni na zarizeni) je zakladem <see cref="AppContext.BaseDirectory"/>;
    /// tam se stejne pouzivaji absolutni cesty.</para>
    ///
    /// <para><b>Proc to bydli tady, a ne v <c>Program</c>.</b> Potrebuje to
    /// <see cref="ParamStore"/> (rozreseni <c>config=</c> a parametru typu cesta) i strazny test
    /// registru - a testovaci projekt na <c>ARBot</c> referenci nema.
    /// <c>Program.RepoRootOrBase</c> sem deleguje. Viz doc/configuration.md.</para>
    /// </summary>
    public static class RepoPaths
    {
        /// <summary>Koren repa hledany smerem nahoru od build outputu; fallback na base directory.</summary>
        public static string RootOrBase()
        {
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    string git = Path.Combine(dir.FullName, ".git");
                    if (Directory.Exists(git) || File.Exists(git))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch { }
            return AppContext.BaseDirectory;
        }

        /// <summary>
        /// Absolutni cestu necha, relativni spoji s <see cref="RootOrBase"/>. Prazdny vstup
        /// prochazi beze zmeny; vadnou cestu vraci tak, jak prisla - at ji resi volajici
        /// (File.Exists + hlaska), aby se start aplikace neshodil na formatu retezce.
        /// </summary>
        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;
            try
            {
                if (Path.IsPathRooted(path))
                    return path;
                return Path.GetFullPath(Path.Combine(RootOrBase(), path));
            }
            catch
            {
                return path;
            }
        }
    }
}
