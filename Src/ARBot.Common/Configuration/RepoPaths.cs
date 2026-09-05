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
        /// <summary>
        /// <b>Datovy adresar</b> (parametr <c>dataroot=</c>), proti kteremu se resi relativni cesty
        /// misto <see cref="RootOrBase"/>. <c>null</c> = vypnuto, plati dosavadni chovani.
        ///
        /// <para><b>Nacpak to je:</b> na zarizeni se aplikace nasazuje <b>stinovou kopii</b> - skript
        /// odkopiruje binarky bokem a spusti je odtamtud, aby sel puvodni adresar prepisovat i za
        /// behu (bezici .NET binarku prepsat nejde, assembly jsou memory-mapped). Data ale musi
        /// zustat v tom PUVODNIM adresari, jinak by se zaznamy a logy ztracely s kazdou novou kopii.
        /// Viz doc/plan-headless-provoz.md, navrh F.</para>
        ///
        /// <para>Nastavuje se <b>jednou pri startu</b> z <c>RuntimeBootstrap</c>, respektive uz
        /// v <c>ParamStore.Build</c> (drive nez se resi <c>config=</c>). Proto se <c>dataroot=</c>
        /// bere jen z prikazove radky - profil se hleda az podle nej.</para>
        /// </summary>
        public static string DataRoot { get; private set; }

        /// <summary>
        /// Nastavi datovy adresar. Prazdna hodnota ho vypina. Relativni cesta se resi proti
        /// <see cref="RootOrBase"/>, aby slo psat <c>dataroot=data</c> i pri vyvoji.
        /// </summary>
        public static void SetDataRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) { DataRoot = null; return; }
            try
            {
                DataRoot = Path.IsPathRooted(path)
                    ? Path.GetFullPath(path)
                    : Path.GetFullPath(Path.Combine(RootOrBase(), path));
            }
            catch
            {
                // Nesmyslna cesta nesmi shodit start driv, nez se stihne vypsat konfigurace;
                // relativni cesty pak zustanou u dosavadniho chovani.
                DataRoot = null;
            }
        }

        /// <summary>
        /// Zaklad pro relativni cesty: datovy adresar, kdyz je zadany, jinak koren repa nebo
        /// adresar aplikace. <b>Tudy chodi vsechno, co se cte i zapisuje</b> - zaznamy, logy,
        /// profily, mapy.
        /// </summary>
        public static string DataRootOrBase() => DataRoot ?? RootOrBase();

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
                return Path.GetFullPath(Path.Combine(DataRootOrBase(), path));
            }
            catch
            {
                return path;
            }
        }
    }
}
