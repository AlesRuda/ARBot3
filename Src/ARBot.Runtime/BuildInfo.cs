using System;
using System.Globalization;
using System.Reflection;

namespace ARBot.Robot
{
    /// <summary>
    /// <b>Verze běžící binárky.</b> Rozebere <see cref="AssemblyInformationalVersionAttribute"/>
    /// vstupní assembly na verzi, git hash, příznak rozpracované kopie a čas buildu.
    ///
    /// <para><b>Proč to existuje (5. 9. 2026):</b> na zařízení nešlo poznat, která binárka běží —
    /// repo nemá tagy a <c>AssemblyVersion</c> byla natvrdo <c>1.0.0.0</c>. Verze teď jde do
    /// hlavičky crash logu, do úvodního řádku aplikace (a tím přes <c>TraceInfoBridge</c>
    /// i do záznamu) a do webového náhledu. Viz doc/plan-headless-provoz.md, návrh E.</para>
    ///
    /// <para><b>Tvar atributu</b> (skládá ho <c>Src/Directory.Build.props</c>):
    /// <c>1.0.247.11448+2316de12-dirty (2026-09-05 06:21 UTC)</c> u nasazované binárky,
    /// <c>0.0.0.0-dev</c> u běžného vývojového buildu (razítkuje se jen s <c>-p:ArbotStamp=true</c>,
    /// jinak by se při každém buildu překládalo celé řešení znovu).</para>
    ///
    /// <para><b>Nikdy nevyhazuje výjimku.</b> Je to diagnostický údaj: chybějící nebo nesmyslný
    /// atribut znamená „neznámá verze", ne pád aplikace — a už vůbec ne pád <see cref="CrashLog"/>,
    /// který ji volá při zápisu pádu.</para>
    /// </summary>
    public sealed class BuildInfo
    {
        /// <summary>Přípona informational version u nerazítkovaného (vývojového) buildu.</summary>
        private const string DevSuffix = "-dev";

        /// <summary>Přípona za git hashem, když se stavělo z rozpracované kopie.</summary>
        private const string DirtySuffix = "-dirty";

        /// <summary>Formát času buildu v závorce (vždy UTC, vždy invariantní kultura).</summary>
        private const string TimeFormat = "yyyy-MM-dd HH:mm";

        private static BuildInfo? current;

        private BuildInfo(string raw, string version, string gitHash, bool isDirty, bool isDev, DateTime? buildTimeUtc)
        {
            Raw = raw;
            Version = version;
            GitHash = gitHash;
            IsDirty = isDirty;
            IsDev = isDev;
            BuildTimeUtc = buildTimeUtc;
        }

        /// <summary>Verze vstupní assembly procesu (<c>ARBot.exe</c> / <c>ARBot.Headless.dll</c>).</summary>
        public static BuildInfo Current => current ??= FromEntryAssembly();

        /// <summary>Celý atribut tak, jak byl — ať se rozborem nic neztratí.</summary>
        public string Raw { get; }

        /// <summary>Čtyřdílné číslo verze, např. <c>1.0.247.11448</c>; <c>0.0.0.0</c> u vývojového buildu.</summary>
        public string Version { get; }

        /// <summary>Krátký git hash, nebo prázdný řetězec (stavělo se bez gitu).</summary>
        public string GitHash { get; }

        /// <summary>Stavělo se z kopie, která se lišila od <c>HEAD</c> (včetně nesledovaných souborů).</summary>
        public bool IsDirty { get; }

        /// <summary>Nerazítkovaný build, tedy vývojový — číslo verze nic neříká.</summary>
        public bool IsDev { get; }

        /// <summary>Čas buildu v UTC, nebo <c>null</c> (vývojový build, nebo nesmyslný atribut).</summary>
        public DateTime? BuildTimeUtc { get; }

        /// <summary>
        /// Řádek pro člověka: <c>1.0.247.11448 (2316de12-dirty, build 2026-09-05 06:21 UTC)</c>.
        /// U vývojového buildu <c>0.0.0.0-dev (nerazítkovaný build)</c>.
        /// </summary>
        public string Popis()
        {
            if (IsDev) return Version + DevSuffix + " (nerazítkovaný build)";

            string zavorka = string.Empty;
            if (GitHash.Length > 0) zavorka = GitHash + (IsDirty ? DirtySuffix : string.Empty);
            if (BuildTimeUtc.HasValue)
            {
                string cas = "build " + BuildTimeUtc.Value.ToString(TimeFormat, CultureInfo.InvariantCulture) + " UTC";
                zavorka = zavorka.Length > 0 ? zavorka + ", " + cas : cas;
            }

            return zavorka.Length > 0 ? Version + " (" + zavorka + ")" : Version;
        }

        /// <summary>
        /// Rozebere informational version. Cokoliv nečekaného skončí jako verze
        /// <c>neznámá</c> — rozbor je diagnostika, ne validace.
        /// </summary>
        public static BuildInfo Parse(string? informationalVersion)
        {
            string raw = (informationalVersion ?? string.Empty).Trim();
            if (raw.Length == 0)
                return new BuildInfo(string.Empty, "neznámá", string.Empty, false, false, null);

            // Čas buildu je v závorce na konci; zbytek je "verze[+hash[-dirty]]".
            DateTime? cas = null;
            string hlava = raw;
            int zavorka = raw.IndexOf('(');
            if (zavorka >= 0)
            {
                int konec = raw.IndexOf(')', zavorka + 1);
                string uvnitr = (konec > zavorka ? raw.Substring(zavorka + 1, konec - zavorka - 1) : raw.Substring(zavorka + 1)).Trim();
                if (uvnitr.EndsWith("UTC", StringComparison.OrdinalIgnoreCase))
                    uvnitr = uvnitr.Substring(0, uvnitr.Length - 3).Trim();
                if (DateTime.TryParseExact(uvnitr, TimeFormat, CultureInfo.InvariantCulture,
                                           DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
                    cas = t;
                hlava = raw.Substring(0, zavorka).Trim();
            }

            string verze = hlava;
            string hash = string.Empty;
            bool dirty = false;

            int plus = hlava.IndexOf('+');
            if (plus >= 0)
            {
                verze = hlava.Substring(0, plus).Trim();
                hash = hlava.Substring(plus + 1).Trim();
                if (hash.EndsWith(DirtySuffix, StringComparison.OrdinalIgnoreCase))
                {
                    dirty = true;
                    hash = hash.Substring(0, hash.Length - DirtySuffix.Length);
                }
            }

            bool dev = verze.EndsWith(DevSuffix, StringComparison.OrdinalIgnoreCase);
            if (dev) verze = verze.Substring(0, verze.Length - DevSuffix.Length);

            if (verze.Length == 0) verze = "neznámá";
            return new BuildInfo(raw, verze, hash, dirty, dev, cas);
        }

        /// <summary>
        /// Vstupní assembly procesu; když žádná není (nehostovaný běh), tak ta, ve které je
        /// tahle třída. Rozdíl je vidět: knihovny si drží svou verzi, razítkuje se každá.
        /// </summary>
        private static BuildInfo FromEntryAssembly()
        {
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly;
                string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrWhiteSpace(info)) info = asm.GetName().Version?.ToString();
                return Parse(info);
            }
            catch
            {
                return Parse(null);
            }
        }
    }
}
