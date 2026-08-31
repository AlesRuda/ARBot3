# Konfigurace aplikace — implementační plán

> **Pro agentní pracovníky:** Plán se plní **task po tasku**, kroky mají checkboxy (`- [ ]`).
> Každý task končí zeleným buildem a testy.

**Spec:** [doc/configuration.md](configuration.md) — plán z ní vychází, čti obojí.

**Cíl:** Parametry, které se dnes zadávají jen z příkazové řádky, dostanou registr, dají se načíst
z profilu (`config=cesta`), prohlédnout a upravit v panelu a uložit zpět do profilu.

**Architektura:** Registr, parser souboru a vrstva účinných hodnot jsou v `ARBot.Common/Configuration`
(bez závislosti na UI, takže se dají pokrýt testy). `Program.GetParam*` si **nechá signaturu** a jen
uvnitř přestane sahat na `Environment.GetCommandLineArgs()` — žádné z ~50 míst čtení se nemění.
Panel je Avalonia dokument v `ARBot`.

**Technologie:** .NET 10, C#, NUnit 4 (`Assert.That`), Avalonia 12 + Dock + CommunityToolkit.Mvvm.

## Globální omezení (platí pro každý krok)

- **Build i testy vždy pro konkrétní platformu:** `dotnet build <proj> -p:Platform=x64`,
  `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`. Nikdy `AnyCPU`.
- **Jazyk:** čeština. Komentáře v `Src/**` **bez diakritiky** (konvence okolních souborů),
  `doc/**` s diakritikou.
- **Commity nejsou součástí kroků.** Commituje autor na vlastní pokyn ([CLAUDE.md](../CLAUDE.md)).
  Kroky typu „commit" se v tomhle plánu záměrně nevyskytují.
- **Nemazat starou implementaci, dokud novou nepotvrdí testy** (CLAUDE.md).
- **Směr závislostí:** `ARBot.Common` nesmí vidět UI ani projekt `ARBot`.
- **Kultura při parsování čísel je vždy `CultureInfo.InvariantCulture`** — profil musí být čitelný
  stejně na stroji s českým i anglickým locale. Dnešní `GetParamDouble` používá `double.TryParse`
  **bez** kultury, což je latentní vada (na českém locale by `0.05` neprošlo); tenhle plán ji
  opravuje mimochodem v Tasku 4.
- **DevLog:** na konci celku doplnit záznam do [devlog.md](devlog.md).

---

## Rozvržení souborů

| Soubor | Odpovědnost |
|---|---|
| `Src/ARBot.Common/Configuration/RepoPaths.cs` | hledání kořene repa (přesun z `Program`) |
| `Src/ARBot.Common/Configuration/ParamDef.cs` | popis jednoho parametru + `ParamType` |
| `Src/ARBot.Common/Configuration/ParamRegistry.cs` | seznam **všech** parametrů |
| `Src/ARBot.Common/Configuration/ParamFile.cs` | čtení a zápis `klíč=hodnota` |
| `Src/ARBot.Common/Configuration/ParamStore.cs` | účinné hodnoty + původ + precedence |
| `Src/ARBot/Program.cs` | `GetParam*` deleguje na `ParamStore` (signatura beze změny) |
| `Src/ARBot/ViewModels/ConfigurationDocument.cs` | ViewModel panelu |
| `Src/ARBot/Views/ConfigurationDocumentView.axaml` (+ `.cs`) | View panelu |
| `Src/ARBot.Common.Tests/Configuration/*` | testy |
| `config/*.cfg` | profily (vzniknou za běhu, jeden vzorový v Tasku 7) |

---

## Task 1: Kořen repa v `Common`

**Proč první:** potřebují ho `ParamStore` (rozřešení cest) i strážný test, a `ARBot.Common.Tests`
na projekt `ARBot` referenci nemá.

**Files:**
- Create: `Src/ARBot.Common/Configuration/RepoPaths.cs`
- Modify: `Src/ARBot/Program.cs` (`RepoRootOrBase`, `GetParamPath`)
- Test: `Src/ARBot.Common.Tests/Configuration/RepoPathsTests.cs`

**Interfaces:**
- Produces: `ARBot.Common.Configuration.RepoPaths.RootOrBase()` → `string`;
  `RepoPaths.Resolve(string path)` → `string` (absolutní nechá, relativní spojí s kořenem;
  `null`/prázdné vrátí beze změny)

- [ ] **Krok 1: Napiš padající test**

`Src/ARBot.Common.Tests/Configuration/RepoPathsTests.cs`:

```csharp
using System.IO;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    public class RepoPathsTests
    {
        [Test]
        public void RootOrBase_ExistujiciSlozka()
        {
            Assert.That(Directory.Exists(RepoPaths.RootOrBase()), Is.True);
        }

        [Test]
        public void Resolve_AbsolutniCestuNechava()
        {
            string abs = Path.GetFullPath(Path.Combine(RepoPaths.RootOrBase(), "OSM"));
            Assert.That(RepoPaths.Resolve(abs), Is.EqualTo(abs));
        }

        [Test]
        public void Resolve_RelativniSpojiSKorenem()
        {
            Assert.That(RepoPaths.Resolve("OSM/x.osm"),
                        Is.EqualTo(Path.GetFullPath(Path.Combine(RepoPaths.RootOrBase(), "OSM/x.osm"))));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Resolve_PrazdneNechava(string vstup)
        {
            Assert.That(RepoPaths.Resolve(vstup), Is.EqualTo(vstup));
        }
    }
}
```

- [ ] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter RepoPathsTests`
Čekej: chyba překladu — `RepoPaths` neexistuje.

- [ ] **Krok 3: Napiš implementaci**

`Src/ARBot.Common/Configuration/RepoPaths.cs`:

```csharp
using System;
using System.IO;

namespace ARBot.Common.Configuration
{
    /// <summary>
    /// Reseni cest relativne ke KORENI REPA (slozka s <c>.git</c>), ne k pracovnimu adresari
    /// procesu. Pracovni adresar se lisi podle toho, jak se app spusti (z VS je to build output,
    /// z <c>dotnet run</c> slozka projektu), takze <c>map=OSM/Neco.osm</c> by jednou nasel a jindy
    /// ne. Proti koreni repa to plati vzdy - diky tomu mohou byt cesty v launchSettings.json
    /// i v profilech relativni, a tedy prenositelne mezi pracovnimi kopiemi.
    ///
    /// <para>Bez repa (nasazeni na zarizeni) je zakladem <see cref="AppContext.BaseDirectory"/>;
    /// tam se stejne pouzivaji absolutni cesty.</para>
    ///
    /// <para>Bydli v <c>ARBot.Common</c>, ne v <c>Program</c>, protoze to potrebuje
    /// <see cref="ParamStore"/> i strazny test registru - a testovaci projekt na <c>ARBot</c>
    /// referenci nema. <c>Program.RepoRootOrBase</c> sem deleguje.</para>
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
```

- [ ] **Krok 4: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter RepoPathsTests`
Čekej: PASS (6 testů).

- [ ] **Krok 5: Přepoj `Program` na `RepoPaths`**

V `Src/ARBot/Program.cs` nahraď těla obou metod delegací. **Nemaž komentáře** — přesuň jejich
obsah do `RepoPaths` (už je tam z Kroku 3) a v `Program` nech krátký odkaz:

```csharp
        /// <summary>
        /// Vraci hodnotu parametru z prikazove radky jako **cestu k souboru/slozce**: relativni
        /// cesta se resi proti korenu repa. Logiku drzi
        /// <see cref="ARBot.Common.Configuration.RepoPaths"/> - tam je i duvod, proc ne proti
        /// pracovnimu adresari.
        /// </summary>
        public static string GetParamPath(string param, string def = null)
        {
            return ARBot.Common.Configuration.RepoPaths.Resolve(GetParam(param, def));
        }

        /// <summary>
        /// Koren git repa. Zachovano kvuli volajicim v projektu ARBot; implementace je
        /// v <see cref="ARBot.Common.Configuration.RepoPaths.RootOrBase"/>.
        /// </summary>
        public static string RepoRootOrBase()
        {
            return ARBot.Common.Configuration.RepoPaths.RootOrBase();
        }
```

- [ ] **Krok 6: Ověř build aplikace**

Spusť: `dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64`
Čekej: build prochází bez chyb.

---

## Task 2: Model parametru a prázdný registr

**Files:**
- Create: `Src/ARBot.Common/Configuration/ParamDef.cs`
- Create: `Src/ARBot.Common/Configuration/ParamRegistry.cs`
- Test: `Src/ARBot.Common.Tests/Configuration/ParamRegistryTests.cs`

**Interfaces:**
- Produces: `ParamType` (`Bool | Double | String | Path`); `ParamDef` s poli
  `Name`, `Type`, `Default`, `DefaultFromCode`, `Description`, `Category`;
  `ParamRegistry.All` → `IReadOnlyList<ParamDef>`;
  `ParamRegistry.TryGet(string name, out ParamDef def)` → `bool` (case-insensitive);
  `ParamDef.IsValidValue(string raw)` → `bool`

- [ ] **Krok 1: Napiš padající test**

`Src/ARBot.Common.Tests/Configuration/ParamRegistryTests.cs`:

```csharp
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    public class ParamRegistryTests
    {
        // Pozn.: case-insensitivitu TryGet overuje az Task 5 - registr je do te doby prazdny
        // a Add je private, takze zvenci se do nej polozka nepridava.

        [Test]
        public void Jmena_JsouUnikatni_BezOhleduNaVelikost()
        {
            var videna = new System.Collections.Generic.HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var d in ParamRegistry.All)
                Assert.That(videna.Add(d.Name), Is.True, $"duplicitni parametr: {d.Name}");
        }

        [Test]
        public void KazdyParametrMaPopisAKategorii()
        {
            foreach (var d in ParamRegistry.All)
            {
                Assert.That(d.Description, Is.Not.Null.And.Not.Empty, $"{d.Name}: chybi popis");
                Assert.That(d.Category, Is.Not.Null.And.Not.Empty, $"{d.Name}: chybi kategorie");
            }
        }

        [Test]
        public void KonstantniDefault_JeSamPlatnouHodnotou()
        {
            foreach (var d in ParamRegistry.All)
            {
                if (d.DefaultFromCode || d.Default == null) continue;
                Assert.That(d.IsValidValue(d.Default), Is.True,
                            $"{d.Name}: vychozi hodnota '{d.Default}' neprojde vlastni validaci");
            }
        }

        [TestCase(ParamType.Bool, "true", true)]
        [TestCase(ParamType.Bool, "TRUE", true)]
        [TestCase(ParamType.Bool, "ano", false)]
        [TestCase(ParamType.Double, "0.05", true)]
        [TestCase(ParamType.Double, "-1", true)]
        [TestCase(ParamType.Double, "0,05", false)]   // desetinna carka neni InvariantCulture
        [TestCase(ParamType.Double, "x", false)]
        [TestCase(ParamType.String, "cokoliv", true)]
        [TestCase(ParamType.String, "", true)]
        [TestCase(ParamType.Path, "OSM/a.osm", true)]
        public void IsValidValue_PodleTypu(ParamType typ, string hodnota, bool ceka)
        {
            var def = new ParamDef { Name = "x", Type = typ, Description = "d", Category = "k" };
            Assert.That(def.IsValidValue(hodnota), Is.EqualTo(ceka));
        }
    }
}
```

- [ ] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter ParamRegistryTests`
Čekej: chyba překladu — `ParamDef`/`ParamRegistry` neexistují.

- [ ] **Krok 3: Napiš `ParamDef`**

`Src/ARBot.Common/Configuration/ParamDef.cs`:

```csharp
using System.Globalization;

namespace ARBot.Common.Configuration
{
    /// <summary>Typ hodnoty parametru. Urcuje validaci a to, jak se hodnota cte.</summary>
    public enum ParamType
    {
        /// <summary>true / false (case-insensitive).</summary>
        Bool,
        /// <summary>Desetinne cislo v InvariantCulture (tecka, ne carka).</summary>
        Double,
        /// <summary>Libovolny retezec - vcetne slozenych tvaru jako "vpred,vlevo,stupne".</summary>
        String,
        /// <summary>Cesta k souboru nebo slozce; relativni se resi proti koreni repa.</summary>
        Path,
    }

    /// <summary>
    /// Popis jednoho konfiguracniho parametru. Do 31. 8. 2026 zadny takovy popis neexistoval -
    /// klic byl jen string literal na miste cteni, takze neslo vypsat, co lze nastavit, a preklep
    /// tise propadl na vychozi hodnotu. Viz doc/configuration.md.
    /// </summary>
    public sealed class ParamDef
    {
        /// <summary>Jmeno klice, jak se pise na prikazovou radku i do profilu. Porovnava se
        /// case-insensitive (stejne jako dosud v <c>Program.GetParam</c>).</summary>
        public string Name;

        /// <summary>Typ hodnoty - urcuje validaci.</summary>
        public ParamType Type;

        /// <summary>
        /// Vychozi hodnota v TEXTOVE podobe, presne jak by stala v profilu. Textove proto, aby
        /// zapis profilu i vypis v panelu sly jednou cestou a nemohly se rozejit o formatovani
        /// cisla. <c>null</c> znamena "nenastaveno".
        /// </summary>
        public string Default;

        /// <summary>
        /// Vychozi hodnotu urcuje az kod za behu, takze ji registr nezna. Priklad:
        /// <c>UartAHRS</c> ma default z detekce portu. U takovych parametru se v panelu misto
        /// hodnoty ukaze <see cref="DefaultDescription"/>, do profilu se nezapisuji, dokud je
        /// nekdo vyslovne nenastavi, a kontrola shody defaultu se preskoci.
        /// </summary>
        public bool DefaultFromCode;

        /// <summary>Cim je default urcen, kdyz <see cref="DefaultFromCode"/> - napr.
        /// "podle detekce portu". Jen pro zobrazeni.</summary>
        public string DefaultDescription;

        /// <summary>Veta do panelu i do komentare v profilu. Povinna.</summary>
        public string Description;

        /// <summary>Kategorie pro razeni a nadpisy ("Fuze", "Mise", ...). Povinna.</summary>
        public string Category;

        /// <summary>Projde hodnota validaci pro <see cref="Type"/>?</summary>
        public bool IsValidValue(string raw)
        {
            if (raw == null) return false;
            switch (Type)
            {
                case ParamType.Bool:
                    return bool.TryParse(raw.Trim(), out _);
                case ParamType.Double:
                    return double.TryParse(raw.Trim(), NumberStyles.Float,
                                           CultureInfo.InvariantCulture, out _);
                default:
                    return true;    // String i Path prijmou cokoliv vcetne prazdneho
            }
        }
    }
}
```

- [ ] **Krok 4: Napiš prázdný `ParamRegistry`**

`Src/ARBot.Common/Configuration/ParamRegistry.cs`:

```csharp
using System.Collections.Generic;

namespace ARBot.Common.Configuration
{
    /// <summary>
    /// Seznam VSECH konfiguracnich parametru aplikace - jedine misto, kde parametr vznika.
    /// Je to centralni deklarace, ne samoregistrace pri cteni: panel musi umet vypsat i parametry
    /// vetvi, ktere v tomhle behu nebezi (pri <c>mission=robotour</c> i klice FreeRunu). Shodu se
    /// zdrojovym kodem hlida <c>ParamRegistryGuardTests</c>. Viz doc/configuration.md.
    /// </summary>
    public static class ParamRegistry
    {
        private static readonly List<ParamDef> all = new List<ParamDef>();

        /// <summary>Vsechny parametry v poradi deklarace (kategorie po kategoriich).</summary>
        public static IReadOnlyList<ParamDef> All => all;

        /// <summary>Najde parametr podle jmena, case-insensitive.</summary>
        public static bool TryGet(string name, out ParamDef def)
        {
            def = null;
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (var d in all)
            {
                if (string.Equals(d.Name, name.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    def = d;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Prida parametr do seznamu; vraci ho, aby sel deklarovat v jednom vyrazu.</summary>
        private static ParamDef Add(ParamDef d)
        {
            all.Add(d);
            return d;
        }

        static ParamRegistry()
        {
            // Naplneni prijde v Tasku 5. Do te doby je seznam prazdny a Add nepouzita - to je
            // zamer, ne opomenuti.
        }
    }
}
```

- [ ] **Krok 5: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter ParamRegistryTests`
Čekej: PASS (13 testů). Testy nad prázdným seznamem projdou triviálně — naplní ho Task 5.

---

## Task 3: Čtení a zápis profilu

**Files:**
- Create: `Src/ARBot.Common/Configuration/ParamFile.cs`
- Test: `Src/ARBot.Common.Tests/Configuration/ParamFileTests.cs`

**Interfaces:**
- Consumes: `ParamDef`, `ParamRegistry` (Task 2)
- Produces:
  `ParamFile.Parse(IEnumerable<string> lines)` → `List<KeyValuePair<string,string>>` (v pořadí souboru)
  `ParamFile.Read(string path)` → totéž (vyhodí `FileNotFoundException`, když soubor není)
  `ParamFile.Format(IReadOnlyDictionary<string,string> values)` → `string` (celý obsah profilu)

- [ ] **Krok 1: Napiš padající test**

`Src/ARBot.Common.Tests/Configuration/ParamFileTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    public class ParamFileTests
    {
        private static List<KeyValuePair<string, string>> Parse(params string[] lines)
            => ParamFile.Parse(lines);

        [Test]
        public void Parse_ZakladniDvojice()
        {
            var v = Parse("mapcorr=true");
            Assert.That(v, Has.Count.EqualTo(1));
            Assert.That(v[0].Key, Is.EqualTo("mapcorr"));
            Assert.That(v[0].Value, Is.EqualTo("true"));
        }

        [Test]
        public void Parse_IgnorujeKomentareAPrazdneRadky()
        {
            var v = Parse("# komentar", "", "   ", "mission=freerun", "# dalsi");
            Assert.That(v.Select(p => p.Key), Is.EqualTo(new[] { "mission" }));
        }

        [Test]
        public void Parse_OrezavaMezeryKolemRovnitka()
        {
            var v = Parse("  mission  =  freerun  ");
            Assert.That(v[0].Key, Is.EqualTo("mission"));
            Assert.That(v[0].Value, Is.EqualTo("freerun"));
        }

        [Test]
        public void Parse_HodnotaSmiObsahovatRovnitko()
        {
            // Deli se na PRVNIM rovnitku - slozene hodnoty typu poseerror=1,2,3 ho sice nemaji,
            // ale budouci format hodnoty by na tom nemel ztroskotat.
            var v = Parse("st_out=a=b");
            Assert.That(v[0].Value, Is.EqualTo("a=b"));
        }

        [Test]
        public void Parse_PrazdnaHodnotaJePlatna()
        {
            // qrcamera= (prazdne) ma v ARBotRuntime vlastni vyznam: skenuji se VSECHNY kamery.
            var v = Parse("qrcamera=");
            Assert.That(v, Has.Count.EqualTo(1));
            Assert.That(v[0].Value, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Parse_RadekBezRovnitkaJeChyba()
        {
            var ex = Assert.Throws<ParamFileException>(() => Parse("mapcorr"));
            Assert.That(ex.Message, Does.Contain("mapcorr"));
            Assert.That(ex.Message, Does.Contain("1"));      // cislo radku
        }

        [Test]
        public void Parse_DuplicitniKlicJeChyba()
        {
            var ex = Assert.Throws<ParamFileException>(() => Parse("mapcorr=true", "MAPCORR=false"));
            Assert.That(ex.Message, Does.Contain("mapcorr").IgnoreCase);
        }

        [Test]
        public void Format_ZapisSeDaPrecistZpatky()
        {
            var hodnoty = new Dictionary<string, string> { { "mapcorr", "true" }, { "mission", "freerun" } };
            string text = ParamFile.Format(hodnoty);
            var zpet = ParamFile.Parse(text.Split('\n'));
            Assert.That(zpet.ToDictionary(p => p.Key, p => p.Value),
                        Is.EquivalentTo(hodnoty));
        }

        [Test]
        public void Format_PiseKomentarSPopisemZRegistru()
        {
            // Popis se bere z registru; u neznameho klice se komentar vynecha (nespadne to).
            string text = ParamFile.Format(new Dictionary<string, string> { { "nezname_x", "1" } });
            Assert.That(text, Does.Contain("nezname_x=1"));
        }
    }
}
```

- [ ] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter ParamFileTests`
Čekej: chyba překladu — `ParamFile` a `ParamFileException` neexistují.

- [ ] **Krok 3: Napiš implementaci**

`Src/ARBot.Common/Configuration/ParamFile.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ARBot.Common.Configuration
{
    /// <summary>Vada v profilu - hlasi se s cislem radku, at se da opravit.</summary>
    public sealed class ParamFileException : Exception
    {
        public ParamFileException(string message) : base(message) { }
    }

    /// <summary>
    /// Cteni a zapis profilu ve tvaru <c>klic=hodnota</c>, radek na klic, <c>#</c> uvozuje
    /// komentar. Je to zamerne PRESNE to, co by se jinak napsalo na prikazovou radku, jen po
    /// radcich - jedna semantika, zadne mapovani, edituje se v nano pres SSH a diff v gitu je
    /// citelny. Viz doc/configuration.md.
    /// </summary>
    public static class ParamFile
    {
        /// <summary>
        /// Rozebere radky profilu. Poradi zachovava (kvuli hlaskam a kvuli tomu, ze pozdejsi klic
        /// by jinak nesel dohledat). Duplicitni klic je CHYBA, ne tiche prepsani - v souboru,
        /// ktery clovek edituje rucne, je to skoro jiste omyl.
        /// </summary>
        public static List<KeyValuePair<string, string>> Parse(IEnumerable<string> lines)
        {
            var result = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int lineNo = 0;

            foreach (var rawLine in lines)
            {
                lineNo++;
                string line = (rawLine ?? string.Empty).Trim().TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int eq = line.IndexOf('=');
                if (eq < 0)
                    throw new ParamFileException(
                        $"Radek {lineNo}: '{line}' neni ve tvaru klic=hodnota.");

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                if (key.Length == 0)
                    throw new ParamFileException($"Radek {lineNo}: prazdny klic.");
                if (!seen.Add(key))
                    throw new ParamFileException(
                        $"Radek {lineNo}: klic '{key}' je v profilu uz podruhe.");

                result.Add(new KeyValuePair<string, string>(key, value));
            }

            return result;
        }

        /// <summary>Precte profil ze souboru. Chybejici soubor je chyba - viz doc/configuration.md.</summary>
        public static List<KeyValuePair<string, string>> Read(string path)
        {
            if (!File.Exists(path))
                throw new ParamFileException($"Konfiguracni soubor '{path}' neexistuje.");
            return Parse(File.ReadAllLines(path));
        }

        /// <summary>
        /// Slozi obsah profilu: poradi a nadpisy kategorii bere z <see cref="ParamRegistry"/> a ke
        /// kazdemu klici pise popis jako komentar. Profil je tim sam o sobe dokumentaci parametru.
        /// Klice, ktere registr nezna, se pripoji na konec bez komentare (nemelo by nastat, ale
        /// zapis nesmi spadnout).
        /// </summary>
        public static string Format(IReadOnlyDictionary<string, string> values)
        {
            var sb = new StringBuilder();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string category = null;

            foreach (var def in ParamRegistry.All)
            {
                if (!values.TryGetValue(def.Name, out string value))
                    continue;

                if (!string.Equals(category, def.Category, StringComparison.Ordinal))
                {
                    category = def.Category;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append("# --- ").Append(category).Append(" ---\n");
                }

                if (!string.IsNullOrWhiteSpace(def.Description))
                    sb.Append("# ").Append(def.Description).Append('\n');
                sb.Append(def.Name).Append('=').Append(value).Append('\n');
                written.Add(def.Name);
            }

            foreach (var pair in values)
            {
                if (written.Contains(pair.Key)) continue;
                sb.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
            }

            return sb.ToString();
        }
    }
}
```

- [ ] **Krok 4: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter ParamFileTests`
Čekej: PASS (9 testů).

---

## Task 4: Účinné hodnoty a precedence

**Files:**
- Create: `Src/ARBot.Common/Configuration/ParamStore.cs`
- Test: `Src/ARBot.Common.Tests/Configuration/ParamStoreTests.cs`

**Interfaces:**
- Consumes: `ParamDef`, `ParamRegistry` (Task 2), `ParamFile` (Task 3), `RepoPaths` (Task 1)
- Produces:
  `enum ParamOrigin { Default, File, CommandLine }`
  `ParamStore.Build(IEnumerable<string> commandLineArgs)` → `ParamStore` (vyhodí `ParamFileException` při vadné konfiguraci)
  instanční: `string Get(string name)`, `bool GetBool(string name, bool def)`,
  `double GetDouble(string name, double def)`, `string GetString(string name, string def)`,
  `string GetPath(string name, string def)`,
  `ParamOrigin OriginOf(string name)`, `IReadOnlyList<string> Warnings`, `string ConfigPath`
  statické: `ParamStore Current` (nastaví `Build`; před ním prázdný store)

- [ ] **Krok 1: Napiš padající test**

`Src/ARBot.Common.Tests/Configuration/ParamStoreTests.cs`:

```csharp
using System.IO;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    public class ParamStoreTests
    {
        // Registr je naplneny az v Tasku 5; testy proto pouzivaji klice, ktere v nem urcite jsou
        // (mapcorr, mission, roadwidth) - do te doby se tento soubor prekladat bude, ale testy
        // padnou na "neznamy klic". To je v poradku a Task 5 je rozsviti.

        private static string TempProfil(string obsah)
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".cfg");
            File.WriteAllText(path, obsah);
            return path;
        }

        [Test]
        public void BezZadaniPlatiDefaultZRegistru()
        {
            var s = ParamStore.Build(new string[0]);
            Assert.That(s.GetBool("mapcorr", false), Is.False);
            Assert.That(s.OriginOf("mapcorr"), Is.EqualTo(ParamOrigin.Default));
        }

        [Test]
        public void SouborPrebijeDefault()
        {
            string p = TempProfil("mapcorr=true\n");
            try
            {
                var s = ParamStore.Build(new[] { "config=" + p });
                Assert.That(s.GetBool("mapcorr", false), Is.True);
                Assert.That(s.OriginOf("mapcorr"), Is.EqualTo(ParamOrigin.File));
            }
            finally { File.Delete(p); }
        }

        [Test]
        public void PrikazovaRadkaPrebijeSoubor()
        {
            string p = TempProfil("mapcorr=true\n");
            try
            {
                var s = ParamStore.Build(new[] { "config=" + p, "mapcorr=false" });
                Assert.That(s.GetBool("mapcorr", true), Is.False);
                Assert.That(s.OriginOf("mapcorr"), Is.EqualTo(ParamOrigin.CommandLine));
            }
            finally { File.Delete(p); }
        }

        [Test]
        public void NeznamyKlicVSouboruJeChyba()
        {
            string p = TempProfil("mapcor=true\n");   // preklep
            try
            {
                var ex = Assert.Throws<ParamFileException>(
                    () => ParamStore.Build(new[] { "config=" + p }));
                Assert.That(ex.Message, Does.Contain("mapcor"));
            }
            finally { File.Delete(p); }
        }

        [Test]
        public void NeplatnaHodnotaVSouboruJeChyba()
        {
            string p = TempProfil("mapcorr=ano\n");
            try
            {
                var ex = Assert.Throws<ParamFileException>(
                    () => ParamStore.Build(new[] { "config=" + p }));
                Assert.That(ex.Message, Does.Contain("ano"));
            }
            finally { File.Delete(p); }
        }

        [Test]
        public void ChybejiciSouborJeChyba()
        {
            Assert.Throws<ParamFileException>(
                () => ParamStore.Build(new[] { "config=" + Path.Combine(Path.GetTempPath(), "neni.cfg") }));
        }

        [Test]
        public void NeznamyKlicNaPrikazoveRadceJeJenVarovani()
        {
            // Mezi args jsou i ciziAvalonia argumenty a cesta k exe - tvrda chyba by aplikaci
            // znemoznila spustit.
            var s = ParamStore.Build(new[] { "C:\\app\\ARBot.exe", "--nejaky-avalonia-prepinac", "mapcor=true" });
            Assert.That(s.Warnings, Has.Some.Contains("mapcor"));
            Assert.That(s.GetBool("mapcorr", false), Is.False);
        }

        [Test]
        public void NeplatnaHodnotaNaPrikazoveRadceJeChyba()
        {
            var ex = Assert.Throws<ParamFileException>(
                () => ParamStore.Build(new[] { "mapcorr=ano" }));
            Assert.That(ex.Message, Does.Contain("ano"));
        }

        [Test]
        public void GetDouble_CteInvariantCulture()
        {
            var s = ParamStore.Build(new[] { "roadwidth=2.5" });
            Assert.That(s.GetDouble("roadwidth", 3.0), Is.EqualTo(2.5).Within(1e-9));
        }

        [Test]
        public void GetPath_ResiRelativniProtiKoreniRepa()
        {
            var s = ParamStore.Build(new[] { "map=OSM/x.osm" });
            Assert.That(s.GetPath("map", null), Is.EqualTo(RepoPaths.Resolve("OSM/x.osm")));
        }

        [Test]
        public void NesouladDefaultuSeOzve()
        {
            // Volani predava default, ktery se lisi od registru -> ma se to ozvat. V Debug buildu
            // vyjimkou (at si toho nekdo vsimne hned), v Release varovanim.
            var s = ParamStore.Build(new string[0]);
#if DEBUG
            Assert.Throws<ParamFileException>(() => s.GetBool("mapcorr", true));
#else
            s.GetBool("mapcorr", true);          // v registru je false
            Assert.That(s.Warnings, Has.Some.Contains("mapcorr"));
#endif
        }

        [Test]
        public void VolaniBezDefaultuNesoulad_NEhlasi()
        {
            // Program.GetParam("mission") default nepredava vubec (null), takze registrovanou
            // vychozi hodnotu "none" nema s cim porovnavat. Kdyby se to hlasilo, neslo by
            // aplikaci v Debug buildu vubec spustit.
            var s = ParamStore.Build(new string[0]);
            Assert.DoesNotThrow(() => s.GetString("mission", null));
            Assert.That(s.Warnings, Has.None.Contains("mission"));
        }
    }
}
```

- [ ] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter ParamStoreTests`
Čekej: chyba překladu — `ParamStore` a `ParamOrigin` neexistují.

- [ ] **Krok 3: Napiš implementaci**

`Src/ARBot.Common/Configuration/ParamStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ARBot.Common.Configuration
{
    /// <summary>Odkud pochazi ucinna hodnota parametru.</summary>
    public enum ParamOrigin
    {
        /// <summary>Vychozi hodnota z registru (nebo z kodu u DefaultFromCode).</summary>
        Default,
        /// <summary>Z profilu zadaneho pres <c>config=</c>.</summary>
        File,
        /// <summary>Z prikazove radky - prebiji vse.</summary>
        CommandLine,
    }

    /// <summary>
    /// Ucinne hodnoty parametru a jejich puvod. Sklada se jednou pri startu podle poradi
    /// <c>default z registru -> soubor (config=) -> prikazova radka</c>.
    ///
    /// <para><b>Proc prikazova radka prebiji soubor.</b> Jinak by prestalo platit skriptovane A/B
    /// mereni (behy se lisi jednim prepinacem) a vznikla past "proc mi mapcorr=true nic nedela"
    /// by byla ticha. Viz doc/configuration.md.</para>
    /// </summary>
    public sealed class ParamStore
    {
        private readonly Dictionary<string, string> values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ParamOrigin> origins =
            new Dictionary<string, ParamOrigin>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> warnings = new List<string>();

        /// <summary>Hlasky, ktere nezastavily start (neznamy klic na prikazove radce, nesoulad
        /// defaultu). Volajici je vypise do Trace, at skonci i v zaznamu.</summary>
        public IReadOnlyList<string> Warnings => warnings;

        /// <summary>Cesta k profilu z <c>config=</c>, nebo <c>null</c>.</summary>
        public string ConfigPath { get; private set; }

        /// <summary>
        /// Store platny pro tenhle beh. Do zavolani <see cref="Build"/> je prazdny, takze cteni
        /// pred inicializaci vraci defaulty misto pádu.
        /// </summary>
        public static ParamStore Current { get; private set; } = new ParamStore();

        /// <summary>
        /// Slozi store z argumentu prikazove radky. Vyhodi <see cref="ParamFileException"/>, kdyz
        /// je konfigurace vadna - tedy jeste pred tim, nez se cokoliv zalozi.
        /// </summary>
        public static ParamStore Build(IEnumerable<string> commandLineArgs)
        {
            var store = new ParamStore();
            var cmdline = new List<KeyValuePair<string, string>>();

            // 1) Rozebrat prikazovou radku. Ciziho argumentu (cesta k exe, prepinac Avalonie)
            //    si nevsimame - poznamena se az u neznameho klice ve tvaru klic=hodnota.
            foreach (var arg in commandLineArgs ?? new string[0])
            {
                if (arg == null) continue;
                int eq = arg.IndexOf('=');
                if (eq <= 0) continue;
                cmdline.Add(new KeyValuePair<string, string>(
                    arg.Substring(0, eq).Trim(), arg.Substring(eq + 1).Trim()));
            }

            // 2) config= se cte z prikazove radky drive nez cokoliv jineho.
            foreach (var pair in cmdline)
            {
                if (!string.Equals(pair.Key, "config", StringComparison.OrdinalIgnoreCase))
                    continue;
                store.ConfigPath = RepoPaths.Resolve(pair.Value);
                break;
            }

            // 3) Profil. Neznamy klic i neplatna hodnota jsou CHYBA - v souboru, ktery clovek
            //    edituje rucne, zadne cizi klice byt nemaji a tise propadly preklep je presne to,
            //    cemu registr ma zabranit.
            if (store.ConfigPath != null)
            {
                foreach (var pair in ParamFile.Read(store.ConfigPath))
                {
                    if (!ParamRegistry.TryGet(pair.Key, out var def))
                        throw new ParamFileException(
                            $"Profil '{store.ConfigPath}': neznamy parametr '{pair.Key}'.");
                    if (!def.IsValidValue(pair.Value))
                        throw new ParamFileException(
                            $"Profil '{store.ConfigPath}': '{pair.Key}={pair.Value}' neni platna "
                            + $"hodnota typu {def.Type}.");
                    store.Set(def.Name, pair.Value, ParamOrigin.File);
                }
            }

            // 4) Prikazova radka. Neznamy klic je jen varovani (cizi argumenty), ale NEPLATNA
            //    hodnota u znameho klice je chyba - tise propadnout na default je tataz past.
            foreach (var pair in cmdline)
            {
                if (string.Equals(pair.Key, "config", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!ParamRegistry.TryGet(pair.Key, out var def))
                {
                    store.warnings.Add(
                        $"Prikazova radka: '{pair.Key}' neni znamy parametr -> ignoruje se.");
                    continue;
                }
                if (!def.IsValidValue(pair.Value))
                    throw new ParamFileException(
                        $"Prikazova radka: '{pair.Key}={pair.Value}' neni platna hodnota "
                        + $"typu {def.Type}.");
                store.Set(def.Name, pair.Value, ParamOrigin.CommandLine);
            }

            Current = store;
            return store;
        }

        private void Set(string name, string value, ParamOrigin origin)
        {
            values[name] = value;
            origins[name] = origin;
        }

        /// <summary>Surova hodnota, nebo default z registru; <c>null</c>, kdyz neni ani ten.</summary>
        public string Get(string name)
        {
            if (values.TryGetValue(name ?? string.Empty, out string v))
                return v;
            if (ParamRegistry.TryGet(name, out var def) && !def.DefaultFromCode)
                return def.Default;
            return null;
        }

        /// <summary>Odkud pochazi ucinna hodnota.</summary>
        public ParamOrigin OriginOf(string name)
        {
            return origins.TryGetValue(name ?? string.Empty, out var o) ? o : ParamOrigin.Default;
        }

        public bool GetBool(string name, bool fallback)
        {
            CheckDefault(name, fallback ? "true" : "false");
            string raw = Get(name);
            return bool.TryParse((raw ?? string.Empty).Trim(), out bool v) ? v : fallback;
        }

        public double GetDouble(string name, double fallback)
        {
            CheckDefault(name, fallback.ToString(CultureInfo.InvariantCulture));
            string raw = Get(name);
            return double.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        public string GetString(string name, string fallback)
        {
            CheckDefault(name, fallback);
            return Get(name) ?? fallback;
        }

        public string GetPath(string name, string fallback)
        {
            return RepoPaths.Resolve(GetString(name, fallback));
        }

        /// <summary>
        /// Default je zapsany dvakrat - v registru a dal i ve volani (GetParamBool("mapcorr",
        /// false)). Neshodu je potreba slyset, jinak by panel ukazoval jinou vychozi hodnotu, nez
        /// jaka realne plati.
        ///
        /// <para><b>Dve vyjimky, bez kterych by to bylo k nepouziti.</b> (1) Volani, ktere default
        /// nepredava vubec - <c>Program.GetParam("mission")</c> posle null a registrovanou hodnotu
        /// "none" nema s cim porovnavat; hlasit to by v Debug buildu znemoznilo start.
        /// (2) <see cref="ParamDef.DefaultFromCode"/> - tam registr default zamerne nezna.</para>
        /// </summary>
        private void CheckDefault(string name, string callerDefault)
        {
            if (callerDefault == null) return;
            if (!ParamRegistry.TryGet(name, out var def)) return;
            if (def.DefaultFromCode) return;
            if (string.Equals(def.Default ?? string.Empty, callerDefault ?? string.Empty,
                              StringComparison.OrdinalIgnoreCase))
                return;

            string hlaska = $"Parametr '{name}': volani predava vychozi hodnotu "
                            + $"'{callerDefault}', ale registr ma '{def.Default}'.";
            if (!warnings.Contains(hlaska))
                warnings.Add(hlaska);
#if DEBUG
            throw new ParamFileException(hlaska);
#endif
        }
    }
}
```

- [ ] **Krok 4: Spusť test — část ještě padat MÁ**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter ParamStoreTests`
Čekej: většina testů **padne** na „neznamy parametr 'mapcorr'" — registr je prázdný až do Tasku 5.
Projít musí `ChybejiciSouborJeChyba` a `NeznamyKlicNaPrikazoveRadceJeJenVarovani`.
**To je očekávaný mezistav.** Zbytek rozsvítí Task 5; nepokoušej se to obejít.

---

## Task 5: Naplnění registru + strážný test

**Files:**
- Modify: `Src/ARBot.Common/Configuration/ParamRegistry.cs` (statický konstruktor)
- Test: `Src/ARBot.Common.Tests/Configuration/ParamRegistryGuardTests.cs`

**Interfaces:**
- Consumes: `ParamDef`, `ParamRegistry.Add` (Task 2), `RepoPaths` (Task 1)
- Produces: naplněný `ParamRegistry.All` (51 položek; přesný počet potvrdí strážný test v kroku 4)

- [ ] **Krok 1: Naplň registr**

Nahraď statický konstruktor v `ParamRegistry.cs` tímto. Defaulty jsou opsané **z míst čtení nebo
z konfiguračních tříd, které tu hodnotu drží** — neměň je, i kdyby ti přišly divné; jakákoli změna
hodnoty je změna chování, ne migrace.

⚠️ **Čtyři defaulty nejsou u volání, ale v konfiguračních třídách** a byly by snadno odhadnuté
špatně (ověřeno při psaní plánu):
`freerunlook` = `FreeRunConfig.LookaheadM` = **3.0**,
`depotfix` = `RobotourConfig.DepotFixSec` = **5.0**,
`grassrough` = `SyntheticSceneOptions.GrassRoughnessM` = **0.03**,
`depthnoise` = `SyntheticSceneOptions.DepthNoiseM` = **0.003** (ne nula!).
Když se některá z nich změní v té třídě, musí se změnit i tady — jinak bude panel ukazovat lež.

```csharp
        private static ParamDef Konst(string name, ParamType type, string def,
                                      string category, string description)
            => Add(new ParamDef { Name = name, Type = type, Default = def,
                                  Category = category, Description = description });

        private static ParamDef ZKodu(string name, ParamType type, string defDescription,
                                      string category, string description)
            => Add(new ParamDef { Name = name, Type = type, DefaultFromCode = true,
                                  DefaultDescription = defDescription,
                                  Category = category, Description = description });

        static ParamRegistry()
        {
            const string K_HW = "Hardware";
            const string K_MAPA = "Mapy a svet";
            const string K_FUZE = "Fuze a lokalizace";
            const string K_MISE = "Mise";
            const string K_SIM = "Virtualni HW a simulace";
            const string K_DIAG = "Diagnostika";
            const string K_TEST = "Self-test a snimky";

            // --- Hardware -----------------------------------------------------------------
            Konst("no_uart", ParamType.Bool, "false", K_HW,
                  "Preskoci UART senzory (IMU/GPS/motor). Odpojene drivery haze vyjimky v tesne "
                  + "smycce, coz zkresluje mereni vykonu vizualni cesty.");
            ZKodu("UartAHRS", ParamType.String, "podle detekce portu", K_HW,
                  "Serioovy port IMU (VN100). Bez zadani se pouzije port zjisteny pri startu.");
            ZKodu("UartMotor", ParamType.String, "podle detekce portu", K_HW,
                  "Serioovy port ridici jednotky motoru (SDC2160).");
            ZKodu("UartGPS", ParamType.String, "podle detekce portu", K_HW,
                  "Serioovy port GPS (uBlox).");

            // --- Mapy a svet --------------------------------------------------------------
            Konst("map", ParamType.Path, null, K_MAPA,
                  "OSM mapa, podle ktere robot jede (silnicni sit pro globalni navigaci).");
            Konst("visionmap", ParamType.Path, null, K_MAPA,
                  "OSM mapa, ze ktere renderuji VIRTUALNI KAMERY - kdyz se lisi od map=, je "
                  + "vnucena chyba v datech, ne v pozorovateli. Viz doc/virtual-hw.md.");
            Konst("roadwidth", ParamType.Double, "3", K_MAPA,
                  "Vychozi sirka cesty [m] pro useky, ktere ji v mape nemaji.");
            Konst("start", ParamType.String, null, K_MAPA,
                  "Vychozi poloha: 'lat,lon[,kurzDeg]' ve stupnich, nebo 'gps' (pocka na prvni "
                  + "pouzitelny fix a vypne hadani polohy z mapy).");
            Konst("goal", ParamType.String, null, K_MAPA,
                  "Cil jizdy 'lat,lon' ve stupnich - protejsek ke start=. Bez nej robot pri "
                  + "bezobsluznem behu stoji (regulator je null, coz je bezpecny stav).");

            // --- Fuze a lokalizace --------------------------------------------------------
            Konst("mapcorr", ParamType.Bool, "false", K_FUZE,
                  "Zapina korelaci occupancy gridu s mapou (odhad chyby polohy a kurzu). Ve "
                  + "vychozim stavu vypnuta - stoji cele jadro. Viz doc/map-correlation-localization.md.");
            Konst("mapcorrsend", ParamType.Bool, "true", K_FUZE,
                  "Posilat korekce z korelace do fuze, nebo je jen merit.");
            Konst("mapcorrgate", ParamType.String, "soft", K_FUZE,
                  "Hradlovani korekci: 'soft' (vychozi) nebo 'reject'. Tvrde hradlo zahazuje "
                  + "prave ty velke korekce, ktere jsou potreba - zmereno, ze delalo vysledek horsi.");
            Konst("mapcorrref", ParamType.Double, "37.5", K_FUZE,
                  "Referencni informativni dukaz [m^2 * log-odds] pro skalovani sigma korelace. "
                  + "0 vrati konstantni alfa pro A/B srovnani.");
            Konst("corridor", ParamType.Bool, "false", K_FUZE,
                  "Zapina hranovou lokalizaci (poloha a kurz z okraju koridoru proti mape).");
            Konst("corridorsend", ParamType.Bool, "true", K_FUZE,
                  "Posilat mereni z hranove lokalizace do fuze, nebo je jen merit.");
            Konst("corridortol", ParamType.String, null, K_FUZE,
                  "Prah inlieru RANSACu ve tvaru 'konstanta,prirustekNaMetr' [m]. Vzdalena hranice "
                  + "je radove nejistejsi nez blizka, takze jeden prah pro vsechny body je spatne.");
            Konst("measdiag", ParamType.String, null, K_FUZE,
                  "Diagnostika mereni ve fuzi: 'true' nebo '*' pro vsechna mereni (stovky za "
                  + "sekundu), jinak filtr na zdroj mereni.");

            // --- Mise ----------------------------------------------------------------------
            Konst("mission", ParamType.String, "none", K_MISE,
                  "Vyber mise: none | freerun | robotour. Mise se vylucuji, proto selektor a ne "
                  + "booleovske prepinace - dve zaroven by si prepisovaly mrkev.");
            Konst("freerunlook", ParamType.Double, "3", K_MISE,
                  "Lookahead mrkve mise FreeRun [m] - jedina skutecna ladici konstanta te mise.");
            Konst("depotfix", ParamType.Double, "5", K_MISE,
                  "Jak dlouho [s] musi fix v depu neprerusene vyhovovat, nez se mise Robotour zarmuje.");
            Konst("qrcamera", ParamType.String, null, K_MISE,
                  "Kamera, ze ktere se cte QR kod. Prazdna hodnota znamena VSECHNY kamery.");

            // --- Virtualni HW a simulace ---------------------------------------------------
            Konst("virtualhw", ParamType.Bool, "false", K_SIM,
                  "Misto skutecneho HW zalozi simulovane senzory (kamery renderovane z mapy).");
            Konst("camerapose", ParamType.String, "truth", K_SIM,
                  "Z ceho renderuji virtualni kamery: 'truth' (ground truth - chyba odhadu je pak "
                  + "meritelna) nebo 'fusion' (kamera prisroubovana k odhadu chybu strukturalne skryva).");
            Konst("poseerror", ParamType.String, null, K_SIM,
                  "Umela chyba pozy 'vpred,vlevo[,stupne]' - vnuti do renderu znamy posun, takze "
                  + "korelace s mapou ma proti cemu merit.");
            Konst("wheelslip", ParamType.String, null, K_SIM,
                  "Systematicky prokluz kol (neprumeruje se pryc, na rozdil od bileho sumu).");
            Konst("imubias", ParamType.String, null, K_SIM,
                  "Systematicky bias IMU - pomalu rostouci chyba kurzu.");
            Konst("imunoise", ParamType.String, null, K_SIM,
                  "Sum simulovaneho IMU.");
            Konst("gpsnoise", ParamType.String, null, K_SIM,
                  "Sum simulovane GPS.");
            Konst("depthnoise", ParamType.Double, "0.003", K_SIM,
                  "Sum hloubky syntetickeho obrazu [m]. 0 = exaktni zpetna projekce hranic.");
            Konst("grassrough", ParamType.Double, "0.03", K_SIM,
                  "Drsnost travy [m]. Ridi rezidua proloziti koridoru - je to podlaha presnosti, "
                  + "danou tvarem okraje travy, ne hloubkovym senzorem.");
            Konst("grassheight", ParamType.Double, "0", K_SIM,
                  "Vyska travy nad vozovkou [m]. Nenulova ruzi exaktnost zpetne projekce hranic.");

            // --- Diagnostika ---------------------------------------------------------------
            Konst("diag", ParamType.Bool, "true", K_DIAG,
                  "Diagnosticke stupne v pipeline (vetsi objem zprav ve streamu i v zaznamu).");

            // --- Self-test a snimky --------------------------------------------------------
            Konst("selftest", ParamType.Bool, "false", K_TEST,
                  "Bezobsluzny self-test: otevre okna, spusti Run, pocka, ulozi souhrn a skonci. "
                  + "Viz doc/selftest.md.");
            Konst("st_name", ParamType.String, "baseline", K_TEST,
                  "Jmeno mereni v souhrnnem CSV - odlisuje vetve A/B.");
            Konst("st_seconds", ParamType.Double, "30", K_TEST, "Delka mereni [s].");
            Konst("st_record", ParamType.Bool, "false", K_TEST, "Zaznamenavat beh do .rec souboru.");
            Konst("st_images", ParamType.Bool, "false", K_TEST, "Otevrit okno Images.");
            Konst("st_images_active", ParamType.Bool, "false", K_TEST,
                  "Nechat okno Images aktivni (vykresluje se, tedy zatezuje).");
            Konst("st_robot", ParamType.Bool, "true", K_TEST, "Otevrit robot-centricky pohled.");
            Konst("st_world", ParamType.Bool, "false", K_TEST, "Otevrit World pohled.");
            Konst("st_out", ParamType.Path, null, K_TEST, "Soubor se souhrnem mereni (CSV).");
            Konst("st_shot", ParamType.Bool, "false", K_TEST, "Ulozit snimek okna na konci mereni.");
            Konst("st_video", ParamType.Bool, "false", K_TEST, "Poridit videozaznam okna.");
            Konst("st_video_seconds", ParamType.Double, "5", K_TEST, "Delka videozaznamu [s].");
            Konst("st_video_fps", ParamType.Double, "8", K_TEST, "Snimkova frekvence videozaznamu.");
            Konst("st_video_scale", ParamType.Double, "3", K_TEST,
                  "Delitel rozliseni videozaznamu (3 = tretinova sirka i vyska).");
            Konst("st_video_format", ParamType.String, null, K_TEST, "Format videa: mp4 nebo gif.");
            Konst("ffmpeg", ParamType.Path, null, K_TEST,
                  "Cesta k ffmpeg. Bez nej se pouzije nahradni cesta bez roury.");
            Konst("telemetryshot", ParamType.Bool, "false", K_TEST,
                  "Bezobsluzny snimek telemetrickeho pohledu nad zaznamem.");
            Konst("ts_rec", ParamType.Path, null, K_TEST,
                  "Zaznam pro telemetryshot. Bez nej se vezme nejnovejsi indexovany zaznam.");
            Konst("worldshot", ParamType.Bool, "false", K_TEST,
                  "Bezobsluzny snimek World pohledu.");

            // config= sam do registru NEPATRI - neni to nastaveni aplikace, ale volba, ODKUD se
            // nastaveni bere. Kdyby v registru byl, sel by zapsat do profilu a profil by mohl
            // ukazat na jiny profil.
        }
```

- [ ] **Krok 2: Doplň test case-insensitivity**

Do `ParamRegistryTests.cs` (teď už má registr co hledat):

```csharp
        [Test]
        public void TryGet_JeCaseInsensitive()
        {
            Assert.That(ParamRegistry.TryGet("MAPCORR", out var def), Is.True);
            Assert.That(def.Name, Is.EqualTo("mapcorr"));
        }
```

- [ ] **Krok 3: Ověř počet a spusť testy registru a store**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter "ParamRegistryTests|ParamStoreTests"`
Čekej: PASS. Pokud `KonstantniDefault_JeSamPlatnouHodnotou` padne, je v seznamu překlep v hodnotě
(např. `Double` s desetinnou čárkou).

- [ ] **Krok 4: Napiš strážný test**

`Src/ARBot.Common.Tests/Configuration/ParamRegistryGuardTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Hlida, ze registr a zdrojovy kod se nerozejdou. Centralni deklarace ma jednu vadu -
    /// da se na ni zapomenout - a tenhle test je ta vada zalatana. Viz doc/configuration.md.
    /// </summary>
    public class ParamRegistryGuardTests
    {
        /// <summary>Sest vzoru, ne jen GetParam*: ARBotRuntime ma dva vlastni pomocniky
        /// (ReadDouble, TryReadMeters), ktere GetParam volaji s PROMENNOU - literal je az na
        /// miste volani toho pomocnika.</summary>
        private static readonly Regex Volani = new Regex(
            @"(?:GetParamBool|GetParamDouble|GetParamPath|GetParam|ReadDouble|TryReadMeters)\s*\(\s*""([^""]+)""",
            RegexOptions.Compiled);

        /// <summary>Volani GetParam* s necim jinym nez retezcovym literalem.</summary>
        private static readonly Regex NeprimeVolani = new Regex(
            @"Program\.GetParam(?:Bool|Double|Path)?\s*\(\s*(?!"")",
            RegexOptions.Compiled);

        private static string AppDir()
            => Path.Combine(RepoPaths.RootOrBase(), "Src", "ARBot");

        private static IEnumerable<string> Zdrojaky()
            => Directory.EnumerateFiles(AppDir(), "*.cs", SearchOption.AllDirectories)
                        .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj"
                                                + Path.DirectorySeparatorChar)
                                 && !p.Contains(Path.DirectorySeparatorChar + "bin"
                                                + Path.DirectorySeparatorChar));

        private static HashSet<string> KliceVeZdroji()
        {
            var klice = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Zdrojaky())
                foreach (Match m in Volani.Matches(File.ReadAllText(file)))
                    klice.Add(m.Groups[1].Value);
            // "config" je volba, ODKUD se nastaveni bere - do registru zamerne nepatri.
            klice.Remove("config");
            return klice;
        }

        [Test]
        public void KazdyKlicZeZdrojeJeVRegistru()
        {
            if (!Directory.Exists(AppDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            var chybi = KliceVeZdroji()
                .Where(k => !ParamRegistry.TryGet(k, out _))
                .OrderBy(k => k).ToList();

            Assert.That(chybi, Is.Empty,
                        "Tyhle klice se v kodu ctou, ale nejsou v ParamRegistry: "
                        + string.Join(", ", chybi));
        }

        [Test]
        public void KazdyKlicZRegistruSeNekdeCte()
        {
            if (!Directory.Exists(AppDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            var veZdroji = KliceVeZdroji();
            var mrtve = ParamRegistry.All.Select(d => d.Name)
                                         .Where(n => !veZdroji.Contains(n))
                                         .OrderBy(n => n).ToList();

            Assert.That(mrtve, Is.Empty,
                        "Tyhle klice jsou v ParamRegistry, ale nikdo je necte: "
                        + string.Join(", ", mrtve));
        }

        [Test]
        public void NeprimeVolaniGetParamJenVeDvouZnamychPomocnicich()
        {
            if (!Directory.Exists(AppDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            // Povolena jsou jen tela ReadDouble a TryReadMeters v ARBotRuntime.cs. Kdyby vznikl
            // dalsi pomocnik, test padne a resenim je pridat ho do vzoru Volani - ne vypnout test.
            var nalezy = new List<string>();
            foreach (var file in Zdrojaky())
            {
                if (Path.GetFileName(file) == "ARBotRuntime.cs") continue;
                if (NeprimeVolani.IsMatch(File.ReadAllText(file)))
                    nalezy.Add(Path.GetFileName(file));
            }

            Assert.That(nalezy, Is.Empty,
                        "Volani Program.GetParam* s ne-literalnim klicem mimo zname pomocniky: "
                        + string.Join(", ", nalezy));
        }
    }
}
```

- [ ] **Krok 5: Spusť strážný test a doplň, co najde**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter ParamRegistryGuardTests`
Čekej: PASS. Když padne s výčtem klíčů, **doplň je do registru** podle vzoru výš (defaulty opiš
z místa čtení) — neupravuj test tak, aby prošel.

- [ ] **Krok 6: Spusť celou sadu testů**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`
Čekej: vše zelené.

---

## Task 6: Napojení `Program` a `config=`

**Files:**
- Modify: `Src/ARBot/Program.cs` (`GetParam`, `GetParamDouble`, `GetParamBool`, `Main`)
- Modify: `Src/ARBot/Robot/ARBotRuntime.cs:1096-1130` (`ReadDouble`, `TryReadMeters`)

**Interfaces:**
- Consumes: `ParamStore.Build`, `ParamStore.Current`, `ParamOrigin` (Task 4); naplněný registr (Task 5)
- Produces: nic nového — `Program.GetParam*` si nechává signaturu

- [ ] **Krok 1: Přepiš těla `GetParam*`**

V `Src/ARBot/Program.cs` nahraď těla tří metod. **Komentáře nad metodami nech** — popisují smysl,
který se nemění. `Debug.WriteLine` zůstává: je to jediná dnešní stopa konfigurace v záznamu
(přes `Info`), a tu si nechceme vzít.

```csharp
        public static string GetParam(string param, string def = null)
        {
            var store = ARBot.Common.Configuration.ParamStore.Current;
            string val = store.GetString(param, def);
            Debug.WriteLine(string.Format("{0}={1}", param, val));
            return val;
        }

        public static double GetParamDouble(string param, double def)
        {
            var store = ARBot.Common.Configuration.ParamStore.Current;
            double val = store.GetDouble(param, def);
            Debug.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                          "{0}={1}", param, val));
            return val;
        }

        public static bool GetParamBool(string param, bool def)
        {
            var store = ARBot.Common.Configuration.ParamStore.Current;
            bool val = store.GetBool(param, def);
            Debug.WriteLine(string.Format("{0}={1}", param, val));
            return val;
        }
```

- [ ] **Krok 2: Postav store v `Main` před startem Avalonie**

`ParamStore.Build` musí proběhnout dřív, než cokoli sáhne na `GetParam` — a vadná konfigurace
má skončit hláškou, ne výjimkou v půlce startu.

```csharp
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                var store = ARBot.Common.Configuration.ParamStore.Build(
                    Environment.GetCommandLineArgs());
                foreach (var w in store.Warnings)
                    Debug.WriteLine("Konfigurace: " + w);
                if (store.ConfigPath != null)
                    Debug.WriteLine("Konfigurace: profil " + store.ConfigPath);
            }
            catch (ARBot.Common.Configuration.ParamFileException ex)
            {
                // Vadna konfigurace nema smysl obchazet - aplikace by bezela s necim jinym, nez
                // co je v profilu napsano, a nikdo by se to nedozvedel.
                Console.Error.WriteLine("Chyba konfigurace: " + ex.Message);
                Debug.WriteLine("Chyba konfigurace: " + ex.Message);
                Environment.Exit(2);
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
```

- [ ] **Krok 3: Přepiš `ReadDouble` a `TryReadMeters` v `ARBotRuntime`**

Obě dnes volají `Program.GetParam(name)` s proměnnou a samy parsují. Nech jim signaturu i hlášky,
jen parsování předej store — tím se sjednotí kultura i validace:

```csharp
        /// <summary>Precte cislo z parametru; chybejici i nesmysl da <paramref name="fallback"/>.</summary>
        private static double ReadDouble(string name, double fallback)
        {
            double v = ARBot.Common.Configuration.ParamStore.Current.GetDouble(name, fallback);
            if (v != fallback)
                Trace.WriteLine($"{name}={v.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            return v;
        }

        /// <summary>
        /// Precte nezaporny rozmer [m] z parametru; nesmysl ohlasi a ignoruje.
        ///
        /// <para>Cte pres <c>Get</c>, ne pres <c>GetDouble</c>: tahle metoda zadny vlastni default
        /// nema (vychozi hodnotu drzi SyntheticSceneOptions a nastavuje se jen pri zadani), takze
        /// by kontrola shody defaultu ve <c>ParamStore</c> hlasila neshodu, ktera zadna neni.</para>
        /// </summary>
        private static bool TryReadMeters(string name, out double meters)
        {
            meters = 0;
            string raw = ARBot.Common.Configuration.ParamStore.Current.Get(name);
            if (string.IsNullOrWhiteSpace(raw)) return false;

            if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out meters)
                || meters < 0 || double.IsNaN(meters))
            {
                Trace.WriteLine($"{name}={raw} neni nezaporne cislo v metrech -> ignoruje se.");
                meters = 0;
                return false;
            }

            Trace.WriteLine($"{name}={meters.ToString(System.Globalization.CultureInfo.InvariantCulture)} m");
            return true;
        }
```

- [ ] **Krok 4: Ověř build**

Spusť: `dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64`
Čekej: build prochází.

- [ ] **Krok 5: Ověř, že aplikace nastartuje bez parametrů i s profilem**

Založ `config/test.cfg`:

```
mapcorr=true
mission=freerun
```

Spusť: `dotnet run --project Src/ARBot/ARBot.csproj -p:Platform=x64 -- config=config/test.cfg`
Čekej: aplikace nastartuje; v Debug outputu je `Konfigurace: profil …` a `mapcorr=True`.

Pak zkus překlep — `mapcor=true` v souboru. Čekej: aplikace **se nespustí** a na chybovém výstupu
je `Chyba konfigurace: … neznamy parametr 'mapcor'`.

- [ ] **Krok 6: Ověř, že příkazová řádka přebíjí profil**

Spusť: `dotnet run --project Src/ARBot/ARBot.csproj -p:Platform=x64 -- config=config/test.cfg mapcorr=false`
Čekej: v Debug outputu `mapcorr=False`.

- [ ] **Krok 7: Spusť celou sadu testů**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`
Čekej: vše zelené.

---

## Task 7: Panel *Tools → Konfigurace*

**Files:**
- Create: `Src/ARBot/ViewModels/ConfigurationDocument.cs`
- Create: `Src/ARBot/Views/ConfigurationDocumentView.axaml` (+ `.axaml.cs`)
- Modify: `Src/ARBot/ViewModels/MainWindowViewModel.cs` (nový `[RelayCommand] OpenConfiguration`)
- Modify: `Src/ARBot/Views/MainWindow.axaml` (položka v menu Tools)

**Interfaces:**
- Consumes: `ParamRegistry.All`, `ParamStore.Current`, `ParamOrigin`, `ParamFile.Format` (Tasky 2–4)
- Produces: `ConfigurationDocument` s `Id = "Configuration"`

- [ ] **Krok 1: Napiš ViewModel**

`Src/ARBot/ViewModels/ConfigurationDocument.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ARBot.Common.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ARBot.ViewModels
{
    /// <summary>Jeden radek tabulky parametru.</summary>
    public partial class ParamRow : ObservableObject
    {
        /// <summary>Deklarace, ze ktere radek vznikl - drzi se kvuli validaci hodnoty.</summary>
        public ParamDef Def { get; init; }

        public string Name => Def?.Name;
        public string Category => Def?.Category;
        public string Description => Def?.Description;
        public ParamType Type => Def?.Type ?? ParamType.String;

        /// <summary>Vychozi hodnota k zobrazeni; u DefaultFromCode je to popis, ne hodnota.</summary>
        public string DefaultText => Def == null
            ? null
            : (Def.DefaultFromCode ? Def.DefaultDescription : (Def.Default ?? "(nenastaveno)"));

        /// <summary>Odkud pochazi hodnota, ktera plati - "vychozi" / "profil" / "prikazova radka".
        /// Je to pulka objevitelnosti: "proc to ma tuhle hodnotu" je stejne casta otazka jako
        /// "co to vubec je".</summary>
        [ObservableProperty] private string origin;

        [ObservableProperty] private string value;

        /// <summary>Neprazdne, kdyz hodnota neprojde validaci podle typu - chybu ma videt hned,
        /// ne az pri startu.</summary>
        [ObservableProperty] private string error;

        partial void OnValueChanged(string value)
        {
            if (Def == null) return;        // Value se muze nastavit driv nez Def
            Error = string.IsNullOrEmpty(value) || Def.IsValidValue(value)
                    ? null
                    : $"neni platna hodnota typu {Def.Type}";
        }
    }

    /// <summary>
    /// Dokument „Konfigurace": vypis VSECH parametru z <see cref="ParamRegistry"/> s popisem,
    /// ucinnou hodnotou a jejim puvodem; editace a ulozeni profilu.
    ///
    /// <para><b>Proc je videt cely registr, a ne jen to, co se v tomhle behu precetlo.</b> Pri
    /// <c>mission=robotour</c> se klice FreeRunu vubec neprectou - a prave je clovek hleda, kdyz
    /// chce prepnout misi. Viz doc/configuration.md.</para>
    ///
    /// <para><b>Zmena plati az po restartu.</b> Skoro vsechny parametry se ctou pri konstrukci
    /// runtimu, takze editor je ulozistem hodnot pro pristi start - proto tlacitko
    /// „Ulozit a restartovat".</para>
    /// </summary>
    public partial class ConfigurationDocument : DocumentBase
    {
        public override Type ViewType => typeof(ARBot.Views.ConfigurationDocumentView);

        private readonly List<ParamRow> allRows = new List<ParamRow>();

        /// <summary>Radky po filtru.</summary>
        public ObservableCollection<ParamRow> Rows { get; } = new ObservableCollection<ParamRow>();

        [ObservableProperty] private string filter = string.Empty;

        /// <summary>Cesta, kam se naposledy ukladalo (predvyplnena pro dalsi ulozeni).</summary>
        [ObservableProperty] private string profilePath;

        /// <summary>Posledni hlaska pro uzivatele (ulozeno / chyba).</summary>
        [ObservableProperty] private string status;

        public ConfigurationDocument()
        {
            Id = "Configuration";
            Title = "Konfigurace";

            // Design-time: navrhar nesmi sahat na runtime store ani na soubory.
            if (Avalonia.Controls.Design.IsDesignMode)
            {
                allRows.Add(new ParamRow
                {
                    Def = new ParamDef
                    {
                        Name = "mapcorr", Category = "Fuze a lokalizace", Type = ParamType.Bool,
                        Default = "false",
                        Description = "Zapina korelaci occupancy gridu s mapou.",
                    },
                    Origin = "vychozi",
                    Value = "false",
                });
                ApplyFilter();
                return;
            }

            var store = ParamStore.Current;
            ProfilePath = store.ConfigPath;

            foreach (var def in ParamRegistry.All)
            {
                allRows.Add(new ParamRow
                {
                    Def = def,
                    Origin = OriginText(store.OriginOf(def.Name)),
                    Value = store.Get(def.Name) ?? string.Empty,
                });
            }

            ApplyFilter();
        }

        private static string OriginText(ParamOrigin o) => o switch
        {
            ParamOrigin.File => "profil",
            ParamOrigin.CommandLine => "prikazova radka",
            _ => "vychozi",
        };

        partial void OnFilterChanged(string value) => ApplyFilter();

        private void ApplyFilter()
        {
            Rows.Clear();
            string f = (Filter ?? string.Empty).Trim();
            foreach (var r in allRows)
            {
                if (f.Length > 0
                    && r.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                    && (r.Description ?? string.Empty).IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Rows.Add(r);
            }
        }

        /// <summary>
        /// Hodnoty, ktere se maji zapsat: jen ty odlisne od vychoziho stavu. Kratky soubor, ze
        /// ktereho je videt, co se na tomhle behu meni; uplny vycet je uloha panelu, ne profilu.
        /// </summary>
        private Dictionary<string, string> ValuesToWrite()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in allRows)
            {
                if (r.Def == null || string.IsNullOrEmpty(r.Value)) continue;
                if (!r.Def.DefaultFromCode
                    && string.Equals(r.Def.Default ?? string.Empty, r.Value,
                                     StringComparison.OrdinalIgnoreCase))
                    continue;
                result[r.Name] = r.Value;
            }
            return result;
        }

        /// <summary>Ulozi profil na <see cref="ProfilePath"/>. Vadny radek ulozeni zastavi.</summary>
        [RelayCommand]
        private void Save()
        {
            var vadne = allRows.Where(r => !string.IsNullOrEmpty(r.Error)).Select(r => r.Name).ToList();
            if (vadne.Count > 0)
            {
                Status = "Neplatne hodnoty: " + string.Join(", ", vadne);
                return;
            }

            string path = ProfilePath;
            if (string.IsNullOrWhiteSpace(path))
                path = RepoPaths.Resolve(Path.Combine("config", "profil.cfg"));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, ParamFile.Format(ValuesToWrite()));
                ProfilePath = path;
                Status = "Ulozeno do " + path;
            }
            catch (Exception ex)
            {
                Status = "Ulozeni selhalo: " + ex.Message;
            }
        }
    }
}
```

- [ ] **Krok 2: Napiš View**

`Src/ARBot/Views/ConfigurationDocumentView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ARBot.ViewModels"
             x:Class="ARBot.Views.ConfigurationDocumentView"
             x:DataType="vm:ConfigurationDocument">
    <Design.DataContext><vm:ConfigurationDocument/></Design.DataContext>

    <DockPanel Margin="8">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,8">
            <TextBox Width="240" Watermark="Filtr (jméno nebo popis)"
                     Text="{Binding Filter, Mode=TwoWay}"/>
            <TextBox Width="320" Watermark="Cesta k profilu"
                     Text="{Binding ProfilePath, Mode=TwoWay}"/>
            <Button Classes="btn action accent" Content="Uložit profil"
                    Command="{Binding SaveCommand}"/>
        </StackPanel>

        <TextBlock DockPanel.Dock="Bottom" Margin="0,8,0,0"
                   Text="{Binding Status}" TextWrapping="Wrap"/>

        <DataGrid ItemsSource="{Binding Rows}" IsReadOnly="False"
                  AutoGenerateColumns="False" CanUserSortColumns="True"
                  GridLinesVisibility="Horizontal">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Kategorie" Binding="{Binding Category}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Klíč" Binding="{Binding Name}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Hodnota" Binding="{Binding Value, Mode=TwoWay}" Width="140"/>
                <DataGridTextColumn Header="Výchozí" Binding="{Binding DefaultText}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Původ" Binding="{Binding Origin}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Chyba" Binding="{Binding Error}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Popis" Binding="{Binding Description}" IsReadOnly="True"
                                    Width="*"/>
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</UserControl>
```

`Src/ARBot/Views/ConfigurationDocumentView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace ARBot.Views
{
    public partial class ConfigurationDocumentView : UserControl
    {
        public ConfigurationDocumentView()
        {
            InitializeComponent();
        }
    }
}
```

- [ ] **Krok 3: Přidej příkaz do `MainWindowViewModel`**

Za `OpenRobotourMission` v `Src/ARBot/ViewModels/MainWindowViewModel.cs` (stejný vzor):

```csharp
        /// <summary>
        /// Otevre (nebo aktivuje) panel „Konfigurace": vypis vsech parametru s popisem, ucinnou
        /// hodnotou a jejim puvodem, editace a ulozeni profilu. Viz doc/configuration.md.
        /// </summary>
        [RelayCommand]
        private void OpenConfiguration()
        {
            var dock = _factory.DocumentDock;
            if (dock == null)
                return;

            var existing = dock.VisibleDockables?.FirstOrDefault(d => d.Id == "Configuration");
            if (existing != null)
            {
                _factory.SetActiveDockable(existing);
                if (Layout is not null) _factory.SetFocusedDockable(Layout, existing);
                return;
            }

            var doc = new ConfigurationDocument();
            _factory.AddDockable(dock, doc);
            _factory.SetActiveDockable(doc);
            if (Layout is not null)
                _factory.SetFocusedDockable(Layout, doc);
        }
```

- [ ] **Krok 4: Přidej položku do menu Tools**

V `Src/ARBot/Views/MainWindow.axaml` za `Mise Robotour`:

```xml
                <MenuItem Header="Konfigurace" Command="{Binding OpenConfigurationCommand}"/>
```

- [ ] **Krok 5: Ověř build a otevři panel**

Spusť: `dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64`
Čekej: build prochází.

Spusť: `dotnet run --project Src/ARBot/ARBot.csproj -p:Platform=x64 -- mapcorr=true`
Otevři *Tools → Konfigurace*. Ověř: tabulka vypisuje všechny parametry, `mapcorr` má hodnotu
`true` a původ `prikazova radka`, ostatní mají původ `vychozi`. Filtr „corr" zúží seznam.

- [ ] **Krok 6: Ověř uložení profilu**

V panelu změň `mission` na `freerun`, do pole cesty napiš `config/rucni.cfg`, klikni **Uložit profil**.
Ověř, že vznikl `config/rucni.cfg`, obsahuje komentáře s popisy, `mapcorr=true` i `mission=freerun`
a **neobsahuje** parametry ponechané na výchozí hodnotě.

Pak aplikaci ukonči a spusť ji s tím profilem:
`dotnet run --project Src/ARBot/ARBot.csproj -p:Platform=x64 -- config=config/rucni.cfg`
Ověř v panelu, že obě hodnoty platí a mají původ `profil`.

---

## Task 8: Uložit a restartovat

**Files:**
- Modify: `Src/ARBot/ViewModels/ConfigurationDocument.cs` (příkaz `SaveAndRestart`)
- Modify: `Src/ARBot/Views/ConfigurationDocumentView.axaml` (tlačítko)
- Modify: `OrangePi5Ultra/setup-orangepi.sh` (poznámka k `systemd`)

**Interfaces:**
- Consumes: `Save` (Task 7)
- Produces: nic dalšího

- [ ] **Krok 1: Přidej restart do ViewModelu**

Do `ConfigurationDocument.cs`:

```csharp
        /// <summary>
        /// Ulozi profil a restartuje aplikaci s nim.
        ///
        /// <para><b>Predava se JEN config=, puvodni argumenty se zahodi.</b> Kdyby se prenesly,
        /// prebily by podle precedence prave ulozenou hodnotu a tlacitko by nedelalo, co slibuje.
        /// Bezpecne to je proto, ze se do profilu ukladaji UCINNE hodnoty (tedy i to, co prislo
        /// z prikazove radky) - nic se ztratit nemuze.</para>
        ///
        /// <para><b>Past se systemd:</b> pod sluzbou s <c>Restart=always</c> by spusteni vlastni
        /// kopie vyrobilo DVE instance. Promennou <c>INVOCATION_ID</c> nastavuje systemd kazde
        /// jednotce, takze podle ni se pozna, ze staci skoncit a restart nechat na nem.</para>
        /// </summary>
        [RelayCommand]
        private void SaveAndRestart()
        {
            Save();
            if (Status != null && Status.StartsWith("Ulozeno", StringComparison.Ordinal) == false)
                return;                     // ulozeni selhalo -> nerestartovat

            string exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                Status = "Restart neni mozny: neznam cestu k vlastnimu procesu.";
                return;
            }

            bool podSystemd = !string.IsNullOrEmpty(
                Environment.GetEnvironmentVariable("INVOCATION_ID"));

            try
            {
                if (!podSystemd)
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(exe)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(exe),
                    };
                    psi.ArgumentList.Add("config=" + ProfilePath);
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Status = "Restart selhal: " + ex.Message;
                return;
            }

            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
                life.Shutdown();
        }
```

- [ ] **Krok 2: Přidej tlačítko do View**

Do `StackPanel` v horní liště `ConfigurationDocumentView.axaml`, za tlačítko *Uložit profil*:

```xml
            <Button Classes="btn action danger" Content="Uložit a restartovat"
                    Command="{Binding SaveAndRestartCommand}"
                    ToolTip.Tip="Uloží profil a spustí aplikaci znovu jen s ním. Běžící Run i mise skončí."/>
```

- [ ] **Krok 3: Ověř build**

Spusť: `dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64`
Čekej: build prochází.

- [ ] **Krok 4: Ověř restart za běhu**

Spusť aplikaci, otevři *Tools → Konfigurace*, změň `mission` na `freerun`, cesta
`config/restart-test.cfg`, klikni **Uložit a restartovat**.
Čekej: okno se zavře a otevře znovu; v novém okně má `mission` hodnotu `freerun` a původ `profil`.

- [ ] **Krok 5: Doplň poznámku o `systemd`**

Do `OrangePi5Ultra/setup-orangepi.sh` k jednotce aplikace (nebo do její sekce v komentáři) přidej:

```sh
# Pozn.: panel Konfigurace umi "Ulozit a restartovat". Pod systemd se aplikace SAMA nespousti -
# pozna se podle promenne INVOCATION_ID a jen skonci; restart musi zajistit jednotka. Proto tu
# MUSI byt Restart=always, jinak by tlacitko aplikaci vyplo a uz ji nezaplo.
```

Ověř, že jednotka `Restart=always` skutečně má; když ne, doplň ji.

---

## Task 9: Dokumentace

**Files:**
- Modify: `doc/configuration.md` (stav)
- Modify: `CLAUDE.md` (odkaz v rozcestníku)
- Modify: `doc/devlog.md` (záznam dne)
- Modify: `Src/ARBot/Robot/ARBotHW.cs:343` (oprava komentáře — viz níž)

- [ ] **Krok 1: Aktualizuj hlavičku specu**

V `doc/configuration.md` nahraď blok „Stav 2026-08-31" skutečným stavem: co je hotové, kolik testů,
co je ověřeno za běhu a co ne.

- [ ] **Krok 2: Přidej odkaz do rozcestníku**

Do `CLAUDE.md` do sekce „Doménová dokumentace":

```markdown
- [doc/configuration.md](doc/configuration.md) — **konfigurace aplikace**: registr parametrů
  (`ARBot.Common/Configuration`), profily `klíč=hodnota` (`config=cesta`), panel *Tools →
  Konfigurace* s výpisem všech parametrů, jejich původu a uložením profilu. Precedence
  **default → soubor → příkazová řádka**; neznámý klíč nebo neplatná hodnota v profilu je
  **chyba při startu**, ne tichý pád na default. Změna platí **až po restartu** (panel ho umí).
```

- [ ] **Krok 3: Oprav zavádějící komentář o `hw=`**

`Src/ARBot/Robot/ARBotHW.cs:343` slibuje parametr `hw=real`, který **v kódu neexistuje** (žádné
`GetParam("hw")` nikde není) — režim určuje `virtualhw=` a volba v menu. Oprav komentář:

```csharp
        /// <para>Nevola se automaticky - po startu aplikace bezi <see cref="HwMode.None"/> a rezim
        /// urcuje parametr <c>virtualhw=</c> nebo volba v menu. Viz doc/virtual-hw.md.</para>
```

- [ ] **Krok 4: Doplň DevLog**

Do `doc/devlog.md` nahoru přidej záznam dne podle pravidel v hlavičce toho souboru. Zmiň:
co vzniklo, že `GetParam*` si nechalo signaturu (a proč), tvrdé chyby místo tichého defaultu,
past se `systemd` a nález, že `hw=` v komentáři nikdy neexistoval.

- [ ] **Krok 5: Spusť celou sadu testů a build obou platforem**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`
Spusť: `dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64`
Čekej: vše zelené.

---

## Co zůstává neověřené

- **Chování na zařízení** (Armbian/OrangePI): restart pod `systemd`, cesty bez repa. Task 8 to
  ověřuje jen na Windows.
- **Editace hodnot v `DataGrid`u** je ověřená ručně (Task 7, krok 6), ne testem.
- **Kontrola shody defaultů** ohlásí jen parametry, které se v daném běhu skutečně přečtou —
  u větví, kterými se nešlo, neshodu neodhalí. Registr jako celek hlídá strážný test, ale ten
  porovnává jména, ne hodnoty. **Čtyři defaulty navíc žijí v konfiguračních třídách**
  (`FreeRunConfig.LookaheadM`, `RobotourConfig.DepotFixSec`, `SyntheticSceneOptions.GrassRoughnessM`
  a `.DepthNoiseM`) a jejich volání registru default nepředává — tam kontrola nezabere vůbec
  a rozejít se můžou tiše.
- **`ParamStore.Current` je globální mutovatelný stav**, který `ParamStore.Build` přepisuje.
  Testy ho tím ovlivňují navzájem; funguje to, protože NUnit třídy ve výchozím stavu neběží
  paralelně. Kdyby se v projektu zapnula paralelizace (`[Parallelizable]`), tyhle testy se
  rozbijí a bude to vypadat jako náhodné selhání.
