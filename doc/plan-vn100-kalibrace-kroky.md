# Kalibrace magnetometru VN100 — implementační kroky

> **Pro agentní pracovníky:** plán se plní **task po tasku**, kroky mají checkboxy (`- [ ]`).
> Každý task končí zeleným buildem a testy pod `x64`. **Nekomitovat bez pokynu autora**
> (viz [CLAUDE.md](../CLAUDE.md)) — commity v krocích níž jsou připravené texty, ne pokyn.

**Cíl:** Robot si otáčením na místě změří vlastní magnetickou kalibraci, ukáže ji na webové
stránce a po ťuknutí obsluhy si ji zapíše do senzoru — bez notebooku v poli.

**Návrh:** Proložení elipsoidy (`MagCalFit`) je čistá funkce v `ARBot.Common/Calibration`,
testovatelná proti syntetickým datům se známou odpovědí. Sběr a verdikt drží `MagCalCollector`
(vzor `PerfCollector`), řídí ho `MagCalMission` (vzor `FreeRunMission`, ale bez cíle a bez mapy),
zobrazuje webová stránka a **tentýž `MagCalFit`** běží offline v `ARBot.Analyze magcal`. Zápis do
senzoru jde úzkým švem `IMagCalControl` nad rozšířeným `VnCommands`.

**Technologie:** .NET 10, C#, NUnit 4, **MathNet.Numerics 5.0.0** (už je závislost
`ARBot.Common`, vzor použití `Src/ARBot.Common/Algorithms/Kabsch.cs`), `VectorNav.dll`
z `vndotnetlib-0.4` (mimo repo).

**Spec:** [plan-vn100-kalibrace.md](plan-vn100-kalibrace.md) — plán argumentuje ze specifikace,
čti obojí. Čísla dnešního stavu, akceptační kritéria, runbook a fáze 2 jsou tam.

## Globální omezení

- **Jazyk čeština** — komentáře, dokumentace i výpisy. Jména testů bez diakritiky
  (`Prolozeni_VratiZnameParametry`), vzor `Src/ARBot.Common.Tests/Diagnostics/PerfCollectorTests.cs`.
- **Build a testy vždy pro konkrétní platformu, NE `AnyCPU`:**
  `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`.
- **Diagnostika poruch do `Trace`**, ne `Debug` (`Debug.WriteLine` je `[Conditional("DEBUG")]`
  a na zařízení běží Release).
- **Čas přes `TimeBase.Now`**, ne `DateTime.Now`/`UtcNow`.
- **Doména si vyrábí zprávu `ToLogMessage()`**; `Message` je pasivní DTO. Nezakládat
  `MagCalMsg.FromDomain(...)`.
- **Zeměpisné souřadnice všude v radiánech**; převod na stupně jen na okrajích.
- **Nemazat starou implementaci**, dokud novou nepotvrdí testy.
- **Pole je v gaussech [G]**, jak je posílá VN100.

## Mapa souborů

| Soubor | Odpovědnost |
|---|---|
| `Src/ARBot.Common/Calibration/MagCalResult.cs` | výsledek proložení + `ToVnwrg23()` |
| `Src/ARBot.Common/Calibration/MagCalFit.cs` | proložení elipsoidy, symetrický rozklad, měřítko, podmíněnost |
| `Src/ARBot.Common/Calibration/MagCalCoverage.cs` | koše azimutů a náklonů, `MissingText()` |
| `Src/ARBot.Common/Calibration/MagCalThresholds.cs` | prahy verdiktu (prozatímní, na jednom místě) |
| `Src/ARBot.Common/Calibration/MagCalCollector.cs` | sběr ze streamu, integrace gyra, verdikt, `ToLogMessage()` |
| `Src/ARBot.Common/Logs/MagCalMsg.cs` | pasivní DTO do streamu a záznamu |
| `Src/ARBot.Common/Missions/MagCalMission.cs` | automat `Collecting → Done`, `Regulator = null` |
| `Src/ARBot.Common/Missions/MagCalPhase.cs` | výčet fází |
| `Src/ARBot.Common/Missions/MissionSeams.cs` | **modifikace:** přidat `IMagCalControl` |
| `Src/ARBot.Common/Missions/IMissionStatus.cs` | **modifikace:** `MissionWait.MagCoverage = 7` |
| `Src/ARBot.Common/Models/IMUState.cs` | **modifikace:** `MagnetometerRaw`, `FormatVersion` 4 |
| `Src/ARBot.HAL/Devices/AHRS/VnCommands.cs` | **modifikace:** registry 23/44/47, `VNWNV`, parsování odpovědí |
| `Src/ARBot.HAL/Devices/AHRS/VN100IMUBinary.cs` | **modifikace:** `UncompMag` do výstupu, čtecí cesta |
| `Src/ARBot.HAL/Devices/AHRS/VnMagCalControl.cs` | implementace `IMagCalControl` nad driverem |
| `Src/ARBot.Runtime/Robot/ARBotRuntime.cs` | **modifikace:** `case "magcal"` v switchi misí |
| `Src/ARBot.Runtime/Web/WebStatus.cs` | **modifikace:** blok kalibrace do `ToJson`/`ToHtml` |
| `Src/ARBot.Runtime/Web/WebPreviewServer.cs` | **modifikace:** koncový bod zápisu, gate na stopu |
| `Src/ARBot.Analyze/MagCalReport.cs` | offline report |
| `Src/ARBot.Common/Configuration/ParamRegistry.cs` | **modifikace:** `magcal` do výčtu `mission` |

Testy zrcadlí strukturu: `Src/ARBot.Common.Tests/Calibration/`, `.../Missions/`, `.../Devices/`.

---

### Task 1: `MagCalFit` — proložení elipsoidy

Jádro. Bez HW, bez streamu, bez mise.

**Files:**
- Create: `Src/ARBot.Common/Calibration/MagCalResult.cs`
- Create: `Src/ARBot.Common/Calibration/MagCalFit.cs`
- Test: `Src/ARBot.Common.Tests/Calibration/MagCalFitTests.cs`

**Interfaces:**
- Consumes: `System.Numerics.Vector3` (pole v [G]), `MathNet.Numerics.LinearAlgebra`
- Produces:
  - `MagCalResult` — `Matrix<double> C`, `Vector<double> B`, `double Condition`,
    `double SdMagnitudeG`, `double SdInclinationDeg`, `int Samples`, `string ToVnwrg23()`,
    `Vector3 Apply(Vector3 raw)`
  - `MagCalFit.Fit(IReadOnlyList<Vector3> mag, double bRefG) → MagCalResult`
  - `MagCalFit.HeadingDiffDeg(MagCalResult a, MagCalResult b, int bins = 24) → double`

- [x] **Krok 1: Pomůcka pro syntetická data v testu**

Konvence modelu senzoru je `m_comp = C · (m_raw − b)`, takže test si vyrobí surová data
**inverzí**: `m_raw = C⁻¹ · m_ideal + b`.

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using ARBot.Common.Calibration;
using MathNet.Numerics.LinearAlgebra;
using NUnit.Framework;

namespace ARBot.Common.Tests.Calibration
{
    /// <summary>
    /// Prolozeni elipsoidy magnetometru. Testy meri proti ZNAME odpovedi: vyrobi se ideal,
    /// pokrivi znamymi (C, b) a fit ma parametry vratit. Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public class MagCalFitTests
    {
        /// <summary>Velikost pole [G] a sklon [rad] podle registru 21 dnesniho senzoru.</summary>
        private const double Bref = 0.4818;
        private const double SklonRad = 1.0638;   // 60,9 stupne

        /// <summary>Idealni pole v telese pri danem kurzu a naklonu (jednotky G).</summary>
        private static Vector3 Ideal(double yaw, double naklon)
        {
            // Pole v ENU-like ramci: vodorovna slozka na sever, svisla dolu.
            double h = Bref * Math.Cos(SklonRad), v = -Bref * Math.Sin(SklonRad);
            var m = new Vector3((float)h, 0f, (float)v);
            // Otoceni telesa: nejdriv kurz kolem z, pak naklon kolem x.
            var q = Quaternion.CreateFromYawPitchRoll(0f, 0f, (float)naklon)
                  * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)-yaw);
            return Vector3.Transform(m, q);
        }

        /// <summary>Surova mereni pro zadane (C, b): m_raw = C^-1 * m_ideal + b.</summary>
        private static List<Vector3> Vzorky(Matrix<double> C, Vector<double> b,
                                            double[] naklony, int naAzimut = 60)
        {
            var Ci = C.Inverse();
            var res = new List<Vector3>();
            foreach (double n in naklony)
                for (int i = 0; i < naAzimut; i++)
                {
                    var id = Ideal(2 * Math.PI * i / naAzimut, n);
                    var x = Vector<double>.Build.DenseOfArray(new double[] { id.X, id.Y, id.Z });
                    var r = Ci * x + b;
                    res.Add(new Vector3((float)r[0], (float)r[1], (float)r[2]));
                }
            return res;
        }

        private static Matrix<double> Mat(double xx, double yy, double zz,
                                         double xy, double xz, double yz)
            => Matrix<double>.Build.DenseOfArray(new[,] {
                   { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } });

        private static Vector<double> Vec(double x, double y, double z)
            => Vector<double>.Build.DenseOfArray(new[] { x, y, z });
    }
}
```

- [x] **Krok 2: Napsat padající test na známou odpověď**

Hodnoty `C` a `b` jsou z referenčního exportu `vn100-2026-7-8-nastavei z arbot2.sencfg`, tedy
realistický řád (měkké železo ~1,1–1,2, tvrdé ~0,27 G).

```csharp
        [Test]
        public void Prolozeni_VratiZnameParametry()
        {
            var C = Mat(1.222, 1.175, 1.081, 0.005, 0.010, -0.012);
            var b = Vec(-0.274, -0.058, 0.076);
            var vz = Vzorky(C, b, new[] { 0.0, 0.35, -0.35 });

            var r = MagCalFit.Fit(vz, Bref);

            for (int i = 0; i < 3; i++)
            {
                Assert.That(r.B[i], Is.EqualTo(b[i]).Within(0.002), $"bias slozka {i}");
                for (int j = 0; j < 3; j++)
                    Assert.That(r.C[i, j], Is.EqualTo(C[i, j]).Within(0.01), $"C[{i},{j}]");
            }
        }
```

- [x] **Krok 3: Spustit a ověřit, že padá**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter MagCalFitTests
```

Očekávané: chyba překladu — `MagCalFit` ani `MagCalResult` neexistují.

- [x] **Krok 4: `MagCalResult`**

```csharp
using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Calibration
{
    /// <summary>
    /// <b>Vysledek prolozeni</b> — kompenzace magnetometru ve tvaru, v jakem ji ceka registr 23
    /// VN100: <c>m_comp = C · (m_raw − B)</c>.
    ///
    /// <para><c>C</c> je <b>symetricka</b> zamerne (viz <see cref="MagCalFit"/>): rozklad
    /// elipsoidy je jednoznacny jen na rotaci a fyzikalni volba je symetricke reseni. Kdyby
    /// symetricka nebyla, zbyl by po kalibraci konstantni posun kurzu, ktery se neda odlisit
    /// od deklinace.</para>
    /// </summary>
    public sealed class MagCalResult
    {
        public MagCalResult(Matrix<double> c, Vector<double> b, double condition,
                            double sdMagnitudeG, double sdInclinationDeg, int samples)
        {
            C = c ?? throw new ArgumentNullException(nameof(c));
            B = b ?? throw new ArgumentNullException(nameof(b));
            Condition = condition;
            SdMagnitudeG = sdMagnitudeG;
            SdInclinationDeg = sdInclinationDeg;
            Samples = samples;
        }

        /// <summary>Matice mekkeho zeleza 3×3 (symetricka, bezrozmerna).</summary>
        public Matrix<double> C { get; }

        /// <summary>Bias tvrdeho zeleza [G] — odecita se od surového mereni.</summary>
        public Vector<double> B { get; }

        /// <summary>Podminenost navrhove matice: <b>urcenost</b> soustavy, nutna podminka.</summary>
        public double Condition { get; }

        /// <summary>Rozptyl <c>|B|</c> po korekci [G] — kvalita, ne urcenost.</summary>
        public double SdMagnitudeG { get; }

        /// <summary>Rozptyl sklonu po korekci [deg].</summary>
        public double SdInclinationDeg { get; }

        public int Samples { get; }

        /// <summary>Kompenzace jednoho mereni: <c>C · (m − B)</c>.</summary>
        public Vector3 Apply(Vector3 raw)
        {
            double x = raw.X - B[0], y = raw.Y - B[1], z = raw.Z - B[2];
            return new Vector3(
                (float)(C[0, 0] * x + C[0, 1] * y + C[0, 2] * z),
                (float)(C[1, 0] * x + C[1, 1] * y + C[1, 2] * z),
                (float)(C[2, 0] * x + C[2, 1] * y + C[2, 2] * z));
        }

        /// <summary>
        /// Dvanact cisel v poradi, v jakem je cte registr 23 (radky <c>C</c>, pak <c>B</c>) —
        /// ke zkopirovani za <c>VNWRG,23,</c>. Invariantni kultura: desetinna TECKA, jinak by
        /// se do prikazu oddelenych carkami dostala carka.
        /// </summary>
        public string ToVnwrg23()
        {
            var c = new[] { C[0,0], C[0,1], C[0,2], C[1,0], C[1,1], C[1,2],
                            C[2,0], C[2,1], C[2,2], B[0], B[1], B[2] };
            return string.Join(",", c.Select(v => v.ToString("F6", CultureInfo.InvariantCulture)));
        }
    }
}
```

- [x] **Krok 5: `MagCalFit`**

⚠️ **Test z kroku 2 je autorita na konvence měřítka a indexů.** Když neprojde, hledej chybu
nejdřív tady, ne v testu.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;

namespace ARBot.Common.Calibration
{
    /// <summary>
    /// <b>Prolozeni elipsoidy</b> mereni magnetometru → kompenzace registru 23.
    ///
    /// <para><b>Princip.</b> Neporusene pole ma konstantni velikost, takze surova mereni maji
    /// lezet na <b>kulove plose</b>. Tvrde zelezo ji posune (stred mimo pocatek), mekke ji
    /// zdeformuje na elipsoidu. Hleda se tedy elipsoida a z ni transformace zpet na kouli.</para>
    ///
    /// <para>⚠️ <b>Rozklad NENI jednoznacny.</b> <c>A = CᵀC</c> ma nekonecne mnoho reseni
    /// lisicich se rotaci — konstantni <c>|B|</c> splni i otocene reseni. Bere se <b>symetricka
    /// pozitivne definitni odmocnina</b>, protoze mekke zelezo <i>je</i> symetricka deformace.
    /// Referencni export to potvrzuje: mimo diagonalu ma jednotky tisicin.</para>
    ///
    /// <para>⚠️ <b>Meritko se vaze na registr 21</b> (<paramref name="bRefG"/>), ne na prumer
    /// dat: VPE porovnava merene <c>|B|</c> proti referencnimu vektoru a pri nesouhlasu
    /// magnetometr adaptivne utlumi. Koule o spatnem polomeru tedy VPE neuspokoji. Viz
    /// doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public static class MagCalFit
    {
        /// <summary>Pod timhle poctem vzorku se neproklada — 10 neznamych a sum.</summary>
        public const int MinSamples = 50;

        public static MagCalResult Fit(IReadOnlyList<Vector3> mag, double bRefG)
        {
            if (mag == null) throw new ArgumentNullException(nameof(mag));
            if (mag.Count < MinSamples)
                throw new ArgumentException($"Malo vzorku ({mag.Count} < {MinSamples}).", nameof(mag));
            if (!(bRefG > 0)) throw new ArgumentOutOfRangeException(nameof(bRefG));

            // ⚠️ Normalizace vstupu je NUTNA. Bez ni jsou sloupce navrhove matice v jednotkach
            // G², G a 1, tedy o rady jinde — a podminenost by pak merila volbu jednotek, ne
            // geometrii dat, tedy presne to, co ma merit.
            double s = mag.Average(v => v.Length());
            if (!(s > 0)) throw new ArgumentException("Nulove pole.", nameof(mag));

            var d = Matrix<double>.Build.Dense(mag.Count, 10);
            for (int i = 0; i < mag.Count; i++)
            {
                double x = mag[i].X / s, y = mag[i].Y / s, z = mag[i].Z / s;
                d[i, 0] = x * x;  d[i, 1] = y * y;  d[i, 2] = z * z;
                d[i, 3] = 2 * x * y; d[i, 4] = 2 * x * z; d[i, 5] = 2 * y * z;
                d[i, 6] = 2 * x;  d[i, 7] = 2 * y;  d[i, 8] = 2 * z;  d[i, 9] = 1;
            }

            // Homogenni soustava D·u = 0 → nejmensi singularni vektor.
            Svd<double> svd = d.Svd(true);
            var u = svd.VT.Row(9);

            // Podminenost pres NENULOVE smery (0..8). S[9] je ta, ktera ma byt nulova — kdyby
            // se delilo ji, vyslo by vzdy "spatne" i u perfektnich dat. Kdyz je i S[8] male,
            // soustava neni urcena a prave to chceme videt.
            double cond = svd.S[8] > 0 ? svd.S[0] / svd.S[8] : double.PositiveInfinity;

            var A = Matrix<double>.Build.DenseOfArray(new[,] {
                { u[0], u[3], u[4] },
                { u[3], u[1], u[5] },
                { u[4], u[5], u[2] } });
            var v = Vector<double>.Build.DenseOfArray(new[] { u[6], u[7], u[8] });

            // Znak u je libovolny; pro odmocninu je potreba pozitivne definitni A.
            if (A.Evd(Symmetricity.Symmetric).EigenValues.Real().Minimum() < 0)
            {
                A = A.Multiply(-1.0);
                v = v.Multiply(-1.0);
            }

            // Stred elipsoidy z ∂/∂m [mᵀAm + 2vᵀm + c] = 0  →  A·b = −v   (v normalizovanych jednotkach)
            var bn = A.Solve(v.Multiply(-1.0));

            // Symetricka pozitivne definitni odmocnina: C0 = V·diag(√λ)·Vᵀ
            var evd = A.Evd(Symmetricity.Symmetric);
            var lam = evd.EigenValues.Real();
            var V = evd.EigenVectors;
            var sqrtL = Matrix<double>.Build.DenseDiagonal(3, 3, i => Math.Sqrt(lam[i]));
            var C0 = V * sqrtL * V.Transpose();

            // Meritko: prumerna velikost po korekci ma byt bRefG.
            double k = mag.Average(m =>
            {
                var x = Vector<double>.Build.DenseOfArray(
                    new double[] { m.X / s - bn[0], m.Y / s - bn[1], m.Z / s - bn[2] });
                return (C0 * x).L2Norm();
            });

            // Zpet do G: prolozeni bezelo na m/s, takze C se deli s a bias nasobi s.
            var C = C0.Multiply(bRefG / k / s);
            var b = bn.Multiply(s);

            var res = new MagCalResult(C, b, cond, 0, 0, mag.Count);
            var vel = new List<double>(mag.Count);
            var skl = new List<double>(mag.Count);
            foreach (var m in mag)
            {
                var c = res.Apply(m);
                vel.Add(c.Length());
                skl.Add(Math.Atan2(-c.Z, Math.Sqrt(c.X * c.X + c.Y * c.Y)) * 180.0 / Math.PI);
            }
            return new MagCalResult(C, b, cond, Sd(vel), Sd(skl), mag.Count);
        }

        /// <summary>
        /// <b>O kolik stupnu se dve kalibrace lisi v OPRAVE KURZU</b> — maximum pres azimuty.
        ///
        /// <para>Proc ne rozdil dvanacti parametru: ten se da vylozit jen s jejich kovarianci,
        /// kdezto „o kolik jinak by mi vysel kurz" je veliCina, ktera zajima robota. Pouziva se
        /// na kontrolu shody prvni a druhe poloviny dat.</para>
        /// </summary>
        public static double HeadingDiffDeg(MagCalResult a, MagCalResult b, int bins = 24)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            double max = 0;
            for (int i = 0; i < bins; i++)
            {
                // Zkusebni surove mereni: kruh o polomeru 1 G ve vodorovne rovine.
                double f = 2 * Math.PI * i / bins;
                var m = new Vector3((float)Math.Cos(f), (float)Math.Sin(f), 0f);
                var ca = a.Apply(m); var cb = b.Apply(m);
                double d = Math.Atan2(ca.Y, ca.X) - Math.Atan2(cb.Y, cb.X);
                while (d > Math.PI) d -= 2 * Math.PI;
                while (d < -Math.PI) d += 2 * Math.PI;
                max = Math.Max(max, Math.Abs(d) * 180.0 / Math.PI);
            }
            return max;
        }

        private static double Sd(List<double> x)
        {
            if (x.Count < 2) return 0;
            double m = x.Average();
            return Math.Sqrt(x.Sum(v => (v - m) * (v - m)) / (x.Count - 1));
        }
    }
}
```

- [x] **Krok 6: Spustit test, musí projít**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter MagCalFitTests
```

Očekávané: PASS. Když bias sedí a `C` ne, je chyba v měřítku (`bRefG / k / s`); když nesedí ani
bias, v indexech návrhové matice nebo ve znaku `A.Solve(−v)`.

- [x] **Krok 7: Test degenerace — rotace bez náklonů musí být ODMÍTNUTA**

Tohle je nejdůležitější test celého tasku: bez něj by fit nad rovinnou rotací tiše odpověděl
a nikdo by nevěděl, že složka `z` je vymyšlená.

```csharp
        [Test]
        public void JenYaw_BezNaklonu_JeSpatnePodminene()
        {
            var C = Mat(1.222, 1.175, 1.081, 0.005, 0.010, -0.012);
            var b = Vec(-0.274, -0.058, 0.076);

            var rovina = MagCalFit.Fit(Vzorky(C, b, new[] { 0.0 }, 200), Bref);
            var snaklony = MagCalFit.Fit(Vzorky(C, b, new[] { 0.0, 0.35, -0.35 }), Bref);

            Assert.That(rovina.Condition, Is.GreaterThan(MagCalThresholds.MaxCondition),
                "rotace na rovine musi byt odmitnuta podminenosti - z neni merene");
            Assert.That(snaklony.Condition, Is.LessThan(MagCalThresholds.MaxCondition),
                "s naklony ma byt soustava urcena");
        }
```

- [x] **Krok 8: `MagCalThresholds`**

```csharp
namespace ARBot.Common.Calibration
{
    /// <summary>
    /// <b>Prahy verdiktu</b> na jednom miste.
    ///
    /// <para>⚠️ <b>Vsechna cisla jsou ODHAD</b> a naostro se nastavi az podle prvniho skutecneho
    /// mereni na zarizeni — stejna zasada jako u <c>perfwarn=70</c>. Nestavej na nich zavery,
    /// dokud v doc/plan-vn100-kalibrace.md neni napsano, ze jsou zmerene.</para>
    /// </summary>
    public static class MagCalThresholds
    {
        /// <summary>Podminenost, nad kterou je soustava neurcena.</summary>
        public const double MaxCondition = 30.0;

        /// <summary>Pocet azimutovych kosu (po 15 stupnich).</summary>
        public const int AzimuthBins = 24;

        /// <summary>Kolik vzorku musi byt v kazdem azimutovem kosi.</summary>
        public const int MinPerAzimuthBin = 20;

        /// <summary>Kolik naklonovych skupin celkem.</summary>
        public const int MinTiltGroups = 3;

        /// <summary>Z toho kolik odklonenych aspon <see cref="MinTiltDeg"/>.</summary>
        public const int MinTiltedGroups = 2;

        /// <summary>Co se jeste pocita jako naklon [deg].</summary>
        public const double MinTiltDeg = 15.0;

        /// <summary>Rozptyl <c>|B|</c> po korekci [G].</summary>
        public const double MaxSdMagnitudeG = 0.005;

        /// <summary>Rozptyl sklonu po korekci [deg].</summary>
        public const double MaxSdInclinationDeg = 0.5;

        /// <summary>Rozdil v oprave kurzu mezi prvni a druhou polovinou dat [deg].</summary>
        public const double MaxHalfSplitDeg = 2.0;
    }
}
```

- [x] **Krok 9: Spustit, oba testy musí projít**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter MagCalFitTests
```

- [x] **Krok 10: Test symetrie — nesymetrický vstup dá symetrický výstup**

```csharp
        [Test]
        public void NesymetrickyVstup_VratiSymetrickouMatici()
        {
            // Data vyrobena ROTOVANOU (tedy nesymetrickou) matici: R · C_sym.
            var sym = Mat(1.20, 1.15, 1.08, 0.0, 0.0, 0.0);
            var rot = Matrix<double>.Build.DenseOfArray(new[,] {
                { 0.9962, -0.0872, 0.0 }, { 0.0872, 0.9962, 0.0 }, { 0.0, 0.0, 1.0 } });
            var b = Vec(-0.10, 0.05, -0.02);

            var r = MagCalFit.Fit(Vzorky(rot * sym, b, new[] { 0.0, 0.35, -0.35 }), Bref);

            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    Assert.That(r.C[i, j], Is.EqualTo(r.C[j, i]).Within(1e-9),
                        $"C musi byt symetricka: [{i},{j}] vs [{j},{i}]");
        }
```

- [x] **Krok 11: Test normalizace — `|B|` po korekci sedí na referenci, ne na průměr dat**

```csharp
        [Test]
        public void PoKorekci_SediVelikostNaReferenci_NeNaPrumerDat()
        {
            // Mekke zelezo se stredni hodnotou VYRAZNE nad 1 → prumer surovych dat je jiny
            // nez Bref, takze test odlisi normalizaci na referenci od normalizace na data.
            var C = Mat(1.60, 1.55, 1.50, 0.0, 0.0, 0.0);
            var vz = Vzorky(C, Vec(-0.20, 0.10, 0.05), new[] { 0.0, 0.35, -0.35 });

            var r = MagCalFit.Fit(vz, Bref);

            double prumer = 0;
            foreach (var m in vz) prumer += r.Apply(m).Length();
            prumer /= vz.Count;
            Assert.That(prumer, Is.EqualTo(Bref).Within(0.001));
            Assert.That(r.SdMagnitudeG, Is.LessThan(MagCalThresholds.MaxSdMagnitudeG));
            Assert.That(r.SdInclinationDeg, Is.LessThan(MagCalThresholds.MaxSdInclinationDeg));
        }
```

- [x] **Krok 12: Test `ToVnwrg23` — desetinná tečka, dvanáct čísel**

```csharp
        [Test]
        public void ToVnwrg23_MaDvanactCisel_SDesetinnouTeckou()
        {
            var r = new MagCalResult(Mat(1.2, 1.1, 1.0, 0.01, 0.02, 0.03),
                                     Vec(-0.274, -0.058, 0.076), 12.0, 0.001, 0.1, 500);
            string s = r.ToVnwrg23();

            Assert.That(s.Split(',').Length, Is.EqualTo(12));
            Assert.That(s, Does.Contain("."), "desetinna TECKA - carka by rozbila prikaz");
            Assert.That(s, Does.StartWith("1.200000"));
            Assert.That(s, Does.EndWith("0.076000"));
        }
```

- [x] **Krok 13: Spustit celou sadu**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter MagCalFitTests
```

Očekávané: 5 testů PASS.

- [x] **Krok 14: Build celého řešení**

```bash
dotnet build Src/ARBot.slnx -p:Platform=x64
```

- [ ] **Krok 15: Commit** (jen na pokyn autora)

```bash
git add Src/ARBot.Common/Calibration Src/ARBot.Common.Tests/Calibration
git commit -m "Prolozeni elipsoidy magnetometru VN100 (MagCalFit) a prahy verdiktu"
```

---

### Task 2: `MagCalCoverage` — pokrytí azimutů a náklonů

Odpověď na otázku „jak poznám, že mám dost dat", v podobě, která obsluze řekne **co udělat dál**.

**Files:**
- Create: `Src/ARBot.Common/Calibration/MagCalCoverage.cs`
- Test: `Src/ARBot.Common.Tests/Calibration/MagCalCoverageTests.cs`

**Interfaces:**
- Consumes: `MagCalThresholds` (Task 1)
- Produces:
  - `MagCalCoverage.Add(double yawRad, Vector3 mag, Vector3 acc)`
  - `int[] AzimuthCounts`, `int FilledAzimuthBins`, `int TiltGroups`, `int TiltedGroups`
  - `IReadOnlyList<Vector3> Mag`, `bool Complete`, `string MissingText()`

- [x] **Krok 1: Napsat padající testy**

⚠️ `yawRad` je **integrované gyro**, ne `yaw` ze senzoru — `yaw` je právě ta vada, kterou měříme.
Integrace se dělá v `MagCalCollector` (Task 4); sem přichází hotová.

```csharp
using System;
using System.Numerics;
using ARBot.Common.Calibration;
using NUnit.Framework;

namespace ARBot.Common.Tests.Calibration
{
    /// <summary>
    /// Kose pokryti: co jeste chybi, aby slo prolozit. Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public class MagCalCoverageTests
    {
        /// <summary>Gravitace pri danem naklonu [rad] — akcelerometr meri -g v telese.</summary>
        private static Vector3 Acc(double naklon)
            => new Vector3(0f, (float)(-9.81 * Math.Sin(naklon)), (float)(-9.81 * Math.Cos(naklon)));

        private static void Obrat(MagCalCoverage c, double naklon, int n = 240)
        {
            for (int i = 0; i < n; i++)
                c.Add(2 * Math.PI * i / n, new Vector3(0.4f, 0.1f, -0.3f), Acc(naklon));
        }

        [Test]
        public void PrazdnePokryti_NeniHotove_AHlasiChybejiciAzimuty()
        {
            var c = new MagCalCoverage();
            Assert.That(c.Complete, Is.False);
            Assert.That(c.MissingText(), Does.Contain("azimut"));
        }

        [Test]
        public void JedenObratNaRovine_MaAzimuty_AleNeNaklony()
        {
            var c = new MagCalCoverage();
            Obrat(c, 0.0);

            Assert.That(c.FilledAzimuthBins, Is.EqualTo(MagCalThresholds.AzimuthBins));
            Assert.That(c.TiltedGroups, Is.EqualTo(0));
            Assert.That(c.Complete, Is.False, "bez naklonu nesmi byt hotovo - z by bylo vymyslene");
            Assert.That(c.MissingText(), Does.Contain("naklon"));
        }

        [Test]
        public void TriObratySeDvemaNaklony_JeHotovo()
        {
            var c = new MagCalCoverage();
            Obrat(c, 0.0);
            Obrat(c, 0.40);    // ~23 stupne
            Obrat(c, -0.40);

            Assert.That(c.TiltGroups, Is.GreaterThanOrEqualTo(MagCalThresholds.MinTiltGroups));
            Assert.That(c.TiltedGroups, Is.GreaterThanOrEqualTo(MagCalThresholds.MinTiltedGroups));
            Assert.That(c.Complete, Is.True);
            Assert.That(c.MissingText(), Is.Empty);
        }

        [Test]
        public void MalyNaklon_SeNepocitaJakoNaklon()
        {
            var c = new MagCalCoverage();
            Obrat(c, 0.0);
            Obrat(c, 0.10);    // ~5,7 stupne, pod MinTiltDeg
            Obrat(c, -0.10);

            Assert.That(c.TiltedGroups, Is.EqualTo(0));
            Assert.That(c.Complete, Is.False);
        }
    }
}
```

- [x] **Krok 2: Spustit, ověřit chybu překladu**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter MagCalCoverageTests
```

- [x] **Krok 3: Implementovat `MagCalCoverage`**

Náklonové skupiny se počítají koši po 10° v odklonu gravitace od svislice; skupina = koš,
ve kterém je aspoň `MinPerAzimuthBin` vzorků aspoň v polovině azimutů.

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace ARBot.Common.Calibration
{
    /// <summary>
    /// <b>Pokryti mericich smeru</b> — kolik azimutu a naklonu uz robot pri otaceni prosel.
    ///
    /// <para><b>Nacpak.</b> Podminenost prolozeni (<see cref="MagCalFit"/>) rekne, ze soustava
    /// jeste neni urcena, ale nerekne <b>co s tim</b>. Kose to rekly: „chybi azimuty 120–165°",
    /// „chybi naklon". Obsluha stoji u robota a potrebuje pokyn, ne diagnozu.</para>
    ///
    /// <para>⚠️ <b><paramref name="yawRad"/> je INTEGROVANE GYRO</b>, ne yaw ze senzoru — yaw je
    /// prave ta vada, kterou merime. Gyro je ciste (klidovy bias −4,6 °/h, tedy ~0,15° za dve
    /// minuty otaceni) a na pokryti staci relativni uhel: nepotrebujeme vedet, kde je sever,
    /// jen ze jsme se otocili dokola.</para>
    /// </summary>
    public sealed class MagCalCoverage
    {
        /// <summary>Sirka naklonoveho kose [deg].</summary>
        private const double TiltBinDeg = 10.0;

        private readonly int[] azimuth = new int[MagCalThresholds.AzimuthBins];
        // Klic = naklonovy kos, hodnota = pocty po azimutech v tom kosi.
        private readonly Dictionary<int, int[]> tilt = new();
        private readonly List<Vector3> mag = new();
        private readonly List<Vector3> acc = new();

        public IReadOnlyList<Vector3> Mag => mag;
        public IReadOnlyList<Vector3> Acc => acc;
        public int[] AzimuthCounts => (int[])azimuth.Clone();

        public void Add(double yawRad, Vector3 magSample, Vector3 accSample)
        {
            int ai = AzimuthBin(yawRad);
            azimuth[ai]++;

            double odklon = TiltDeg(accSample);
            int ti = (int)Math.Round(odklon / TiltBinDeg);
            if (!tilt.TryGetValue(ti, out var po)) tilt[ti] = po = new int[MagCalThresholds.AzimuthBins];
            po[ai]++;

            mag.Add(magSample);
            acc.Add(accSample);
        }

        /// <summary>Kolik azimutovych kosu ma dost vzorku.</summary>
        public int FilledAzimuthBins => azimuth.Count(c => c >= MagCalThresholds.MinPerAzimuthBin);

        /// <summary>Naklonove skupiny s dostatecnym azimutovym pokrytim.</summary>
        public int TiltGroups => tilt.Count(kv => Dostatecna(kv.Value));

        /// <summary>Z nich ty odklonene aspon <see cref="MagCalThresholds.MinTiltDeg"/>.</summary>
        public int TiltedGroups
            => tilt.Count(kv => Dostatecna(kv.Value)
                                && kv.Key * TiltBinDeg >= MagCalThresholds.MinTiltDeg);

        public bool Complete
            => FilledAzimuthBins == MagCalThresholds.AzimuthBins
               && TiltGroups >= MagCalThresholds.MinTiltGroups
               && TiltedGroups >= MagCalThresholds.MinTiltedGroups;

        /// <summary>Co jeste chybi, pro cloveka. Prazdny retezec = nic.</summary>
        public string MissingText()
        {
            var s = new List<string>();
            var chybi = ChybejiciAzimuty();
            if (chybi.Count > 0) s.Add("chybi azimuty " + PopisRozsahu(chybi));
            if (TiltedGroups < MagCalThresholds.MinTiltedGroups)
                s.Add($"chybi naklon (mam {TiltedGroups} z {MagCalThresholds.MinTiltedGroups}, "
                      + $"podloz robota aspon o {MagCalThresholds.MinTiltDeg:F0} stupnu)");
            else if (TiltGroups < MagCalThresholds.MinTiltGroups)
                s.Add($"chybi naklonova skupina ({TiltGroups} z {MagCalThresholds.MinTiltGroups})");
            return string.Join("; ", s);
        }

        private static bool Dostatecna(int[] po)
            => po.Count(c => c >= MagCalThresholds.MinPerAzimuthBin)
               >= MagCalThresholds.AzimuthBins / 2;

        private List<int> ChybejiciAzimuty()
        {
            var r = new List<int>();
            for (int i = 0; i < azimuth.Length; i++)
                if (azimuth[i] < MagCalThresholds.MinPerAzimuthBin) r.Add(i);
            return r;
        }

        /// <summary>Souvisle useky kosu jako rozsahy ve stupnich (140–180°), ne vypis cisel.</summary>
        private static string PopisRozsahu(List<int> kose)
        {
            double sirka = 360.0 / MagCalThresholds.AzimuthBins;
            var casti = new List<string>();
            int i = 0;
            while (i < kose.Count)
            {
                int j = i;
                while (j + 1 < kose.Count && kose[j + 1] == kose[j] + 1) j++;
                casti.Add(string.Format(CultureInfo.InvariantCulture, "{0:F0}-{1:F0}°",
                                        kose[i] * sirka, (kose[j] + 1) * sirka));
                i = j + 1;
            }
            return string.Join(", ", casti);
        }

        private static int AzimuthBin(double yawRad)
        {
            double f = yawRad % (2 * Math.PI);
            if (f < 0) f += 2 * Math.PI;
            int i = (int)(f / (2 * Math.PI) * MagCalThresholds.AzimuthBins);
            return Math.Min(i, MagCalThresholds.AzimuthBins - 1);
        }

        /// <summary>Odklon gravitace od svislice [deg]; 0 = robot na rovine.</summary>
        private static double TiltDeg(Vector3 acc)
        {
            double n = acc.Length();
            if (!(n > 0)) return 0;
            double vodorovne = Math.Sqrt(acc.X * acc.X + acc.Y * acc.Y);
            return Math.Atan2(vodorovne, Math.Abs(acc.Z)) * 180.0 / Math.PI;
        }
    }
}
```

- [x] **Krok 4: Spustit testy, musí projít**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter MagCalCoverageTests
```

Očekávané: 4 testy PASS.

- [ ] **Krok 5: Commit** (jen na pokyn autora)

```bash
git add Src/ARBot.Common/Calibration/MagCalCoverage.cs Src/ARBot.Common.Tests/Calibration/MagCalCoverageTests.cs
git commit -m "Pokryti azimutu a naklonu pro kalibraci magnetometru (MagCalCoverage)"
```

---

### Task 3: `ARBot.Analyze magcal` — offline report

Dá se pustit hned a **rovnou odpoví na otázku, kterou jsme si položili v návrhu**: jaká je
podmíněnost nad běžnými jízdními daty. To je předpověď z dat, ne dohad.

**Files:**
- Create: `Src/ARBot.Analyze/MagCalReport.cs`
- Modify: `Src/ARBot.Analyze/Program.cs` (dispatch, u ostatních `case` na řádcích 57–130)
- Test: ruční spuštění nad záznamem (report je tiskárna, jádro má testy z Tasku 1–2)

**Interfaces:**
- Consumes: `MagCalFit.Fit`, `MagCalCoverage`, `MagCalThresholds` (Task 1–2),
  `RecordFile` (`rec.Index`, `rec.Read(e)`), `IMUState.Magnetometer`
- Produces: `MagCalReport.Run(RecordFile rec, double bRefG, string reg47)`

⚠️ **Tenhle task použije jen `IMUState.Magnetometer`** (kompenzované pole). Surové
`MagnetometerRaw` vzniká až v Tasku 4 a `MagCalMsg` až v Tasku 6 — kdyby na ně report sahal už
teď, **nepřeložil by se**. Není to omezení: **existující záznamy surové pole ani neobsahují**,
takže je to jediné, co dnes jde přečíst. Rozšíření dodají kroky v Tasku 4 a 6.

- [x] **Krok 1: `MagCalReport`**

Vzor je `Vn100Report.Run`: projít index, vzít jen `IMUState` s absolutním kurzem (T265 posílá
relativní yaw a smíchat dvě různé nuly by dalo nesmysl), integrovat gyro, naplnit pokrytí.

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ARBot.Common.Calibration;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Kalibrace magnetometru ze zaznamu</b> — tentyz <see cref="MagCalFit"/>, ktery bezi na
    /// robotu. To je zamer: verdikt v poli a verdikt u stolu musi byt TOTEZ cislo, ne dve
    /// implementace, ktere se pak rozchazeji.
    ///
    /// <para>Tiskne 12 cisel ve tvaru ke zkopirovani za <c>VNWRG,23,</c>, verdikt, pokryti
    /// a kontrolu rozpulenim. (Blok „co spocital robot v poli" ze zpravy <c>MagCalMsg</c>
    /// pribyde pozdeji — viz doc/plan-vn100-kalibrace-kroky.md, Task 6.)</para>
    /// </summary>
    public static class MagCalReport
    {
        public static void Run(RecordFile rec, double bRefG, string reg47)
        {
            var cov = new MagCalCoverage();
            var raw = new List<Vector3>();
            double yaw = 0;
            double? tPred = null;
            DateTime t0 = DateTime.MinValue;
            int bezPole = 0, bezGyra = 0, relativnich = 0;

            foreach (var e in rec.Index)
            {
                if (e.MsgName != "IMUState") continue;
                var i = (IMUState)rec.Read(e);

                // Stejny duvod jako ve Vn100Report: v robotu je IMU vic a T265 posila
                // RELATIVNI yaw. Zdroje bez absolutniho kurzu se vynechavaji.
                if (!i.HasAbsoluteHeading) { relativnich++; continue; }

                // Task 4 to zmeni na `i.MagnetometerRaw ?? i.Magnetometer`.
                var pole = i.Magnetometer;
                if (pole == null) { bezPole++; continue; }
                if (i.AngularVelocity == null) { bezGyra++; continue; }
                if (i.Acceleration == null) continue;

                double t = Sec(i.TimeStamp, ref t0);
                if (tPred.HasValue)
                {
                    double dt = t - tPred.Value;
                    // Mezera v datech: integrace by pres ni nasbirala nesmysl.
                    if (dt > 0 && dt < 0.5) yaw += i.AngularVelocity.Value.Z * dt;
                }
                tPred = t;

                cov.Add(yaw, pole.Value, i.Acceleration.Value);
                raw.Add(pole.Value);
            }

            Console.WriteLine("=== KALIBRACE MAGNETOMETRU ZE ZAZNAMU ===");
            Console.WriteLine($"  vzorku: {raw.Count}, referencni |B| = {bRefG:F4} G");
            Console.WriteLine("  ⚠️ pole: KOMPENZOVANE (Magnetometer) - vysledek plati JEN kdyz byl"
                              + " registr 23 pri nahravani JEDNOTKOVY. Ze zaznamu se to NEPOZNA.");
            if (relativnich > 0)
                Console.WriteLine($"  vynechano {relativnich} vzorku z IMU bez absolutniho kurzu (T265).");
            if (bezPole > 0 || bezGyra > 0)
                Console.WriteLine($"  vynechano: bez pole {bezPole}, bez gyra {bezGyra}.");

            Console.WriteLine();
            Console.WriteLine("1) POKRYTI");
            Console.WriteLine($"  azimutove kose: {cov.FilledAzimuthBins} z {MagCalThresholds.AzimuthBins}");
            Console.WriteLine($"  naklonove skupiny: {cov.TiltGroups} (z toho odklonenych {cov.TiltedGroups})");
            Console.WriteLine($"  otoceni gyrem celkem: {yaw * 180.0 / Math.PI:F0} stupnu");
            string chybi = cov.MissingText();
            Console.WriteLine(chybi.Length == 0 ? "  pokryti je uplne." : "  " + chybi);

            if (raw.Count < MagCalFit.MinSamples)
            {
                Console.WriteLine();
                Console.WriteLine($"  Malo vzorku ({raw.Count} < {MagCalFit.MinSamples}) - neprokladam.");
                return;
            }

            var r = MagCalFit.Fit(raw, bRefG);
            Console.WriteLine();
            Console.WriteLine("2) PROLOZENI");
            Console.WriteLine($"  podminenost:       {r.Condition:F1}  (prah {MagCalThresholds.MaxCondition:F0})");
            Console.WriteLine($"  sd(|B|) po korekci: {r.SdMagnitudeG:F5} G  (prah {MagCalThresholds.MaxSdMagnitudeG:F3})");
            Console.WriteLine($"  sd(sklonu):        {r.SdInclinationDeg:F3}°  (prah {MagCalThresholds.MaxSdInclinationDeg:F1})");

            // Rozpuleni: dve nezavisle poloviny se musi shodnout v OPRAVE KURZU.
            int p = raw.Count / 2;
            if (p >= MagCalFit.MinSamples)
            {
                try
                {
                    var a = MagCalFit.Fit(raw.Take(p).ToList(), bRefG);
                    var b = MagCalFit.Fit(raw.Skip(p).ToList(), bRefG);
                    double d = MagCalFit.HeadingDiffDeg(a, b);
                    Console.WriteLine($"  rozpuleni dat:     {d:F2}° rozdilu v oprave kurzu"
                                      + $"  (prah {MagCalThresholds.MaxHalfSplitDeg:F0})");
                    Console.WriteLine("  ⚠️ rozpuleni SAMO NESTACI - dve stejne degenerovana data se shodnou taky."
                                      + " Musi platit i podminenost.");
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine("  rozpuleni: nelze - " + ex.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine("3) VYSLEDEK — ke zkopirovani za VNWRG,23,");
            Console.WriteLine("  " + r.ToVnwrg23());

            bool ok = r.Condition <= MagCalThresholds.MaxCondition
                      && r.SdMagnitudeG <= MagCalThresholds.MaxSdMagnitudeG
                      && r.SdInclinationDeg <= MagCalThresholds.MaxSdInclinationDeg
                      && cov.Complete;
            Console.WriteLine("  verdikt: " + (ok ? "POUZITELNE" : "NEPOUZITELNE - viz cisla vyse"));

            if (reg47 != null)
            {
                Console.WriteLine();
                Console.WriteLine("4) POROVNANI S REGISTREM 47 (co spocital sam senzor)");
                Console.WriteLine("  senzor: " + reg47);
                Console.WriteLine("  my:     " + r.ToVnwrg23());
                Console.WriteLine("  ⚠️ Dve nezavisle metody, ktere se shodnou, jsou dukaz."
                                  + " Kdyz se rozejdou, NEZAPISUJ nic a premysli.");
            }

            // Blok 5 (co spocital robot v poli, tedy MagCalMsg ze zaznamu) doplni Task 6 —
            // ta zprava jeste neexistuje.
        }

        private static double Sec(DateTime t, ref DateTime t0)
        {
            if (t0 == DateTime.MinValue) t0 = t;
            return (t - t0).TotalSeconds;
        }
    }
}
```

- [x] **Krok 2: Dispatch v `Program.cs`**

Vlož mezi existující `case` (vzor `case "vn100"` na řádku 113); `Arg` a `Text` jsou už hotové
pomůcky v tom souboru.

```csharp
                    case "magcal":
                        MagCalReport.Run(rec, Arg(args, "--bref", 0.4818), Text(args, "--reg47"));
                        return 0;
```

- [x] **Krok 3: Build**

```bash
dotnet build Src/ARBot.Analyze/ARBot.Analyze.csproj -p:Platform=x64
```

- [x] **Krok 4: Pustit nad existujícím záznamem**

```bash
dotnet run --project Src/ARBot.Analyze -p:Platform=x64 -- magcal records/20260903-153947.rec
```

⚠️ **BLOKOVÁNO nedostatkem dat, ne chybou (zjištěno 8. 9. 2026).** Report funguje a správně
odmítne, ale **žádný z lokálních záznamů magnetometr nenese**: všechny jsou ze simulace
s virtuálním IMU, které pole neposílá (`Magnetometer == null`, jméno zdroje prázdné). Prověřeno
na `20260814-132817` (13 475 vzorků IMU), `20260818-084247`, `20260819-225917`,
`20260820-065657`, `20260822-104827`, `20260824-113019`, `20260901-122322`,
`20260903-153947` — u všech „bez pole" = 100 %.

Výstup nad `20260903-153947.rec`: 2501 vzorků IMU, z toho 0 použitelných, 0 z 24 azimutových
košů, otočení gyrem 0°. Tedy report se chová správně (ohlásí, co chybí, a neproloží).

- [x] **Krok 5: Změřeno nad skutečnými jízdními daty 8. 9. 2026** (`records/test/20260907-170728.rec`,
      452 s FreeRunu venku, 45 185 vzorků VN100 při 100 Hz)

```
azimutove kose:      24 z 24              otoceni gyrem celkem: 556 stupnu
naklonove skupiny:   2 (z toho odklonenych 0)   naklony na obe strany: NE
podminenost:         352.2   (prah 1e4)
sd(|B|) po korekci:  0.13436 G  (prah 0.005)   -> 27x nad
sd(sklonu):          35.156 deg (prah 0.5)     -> 70x nad
rozpuleni dat:       47.76 deg rozdilu v oprave kurzu (prah 2)
verdikt: NEPOUZITELNE
```

⚠️ **Odpověď je jiná, než jakou plán čekal — a je to nález, ne detail: na reálných datech je
podmíněnost jako brána NEÚČINNÁ.** Syntetická rovinná rotace má 2,0 × 10⁸, ale skutečná jízda
**352**, tedy **pět řádů níž a hluboko pod prahem** 10⁴ — soustava se „určila" a vyšel z ní
nesmysl: `C[2,2] = 38,0` místo ~1,1 (podpis nezměřené osy `z`, kterou fit natáhne) a pole po
korekci **není ani zdaleka konstantní**. Varování ve specifikaci („na reálných datech se mezera
zúží, šum vyplní degenerovaný směr") se tedy naplnilo **v plné síle**: jízda po nerovném terénu
dá náklony do ~15°, které degenerovaný směr vyplní — ale **šumem**, takže je soustava numericky
řešitelná a statisticky pořád podurčená.

✅ **Brána přitom drží — jen ji nedrží podmíněnost.** Verdikt `NEPOUZITELNE` vyšel z **pokrytí**
(0 z 2 odkloněných skupin, náklony jen na jednu stranu) a ze **zbytků** (27× a 70× nad prahem).
Praktický důsledek: primární kritérium pro obsluhu jsou **koše a zbytky**, ne podmíněnost —
což specifikace tušila („kritérium geometrické, tedy na šumu nezávislé"), ale teď je to změřené.
Prahy podmíněnosti proto **naostro nastaví až rotační test** (Task 10 krok 10); snižovat ji
naslepo podle jednoho jízdního záznamu by znamenalo hádat.

**Odpověď na „jak moc je rotační test potřeba":** velmi. Běžná jízda naplní **všechny azimuty**
(24/24, otočení 556°), takže vodorovná složka pokrytá je — ale **náklony nedá vůbec** a jen ta
druhá polovina rozhoduje. Dobrá zpráva pro terénní výjezd je ta azimutová část: obsluha bude
muset dodat hlavně **podložení na obě strany**, ne otáčení dokola.

⚠️ **Platí to jen tehdy, když byl registr 23 při nahrávání jednotkový.** Záznam je **formátu 3**,
tedy surové pole nenese (`MagnetometerRaw` přibylo 8. 9. 2026), takže se prokládalo pole
**kompenzované**. Podle [imu-and-frames.md](imu-and-frames.md) byla kalibrace 6. 9. 2026 vymazána
a uložena do flash, takže 7. 9. jednotkový **byl** — je to ale doložené z dokumentace, **ze dat
se to zjistit nedá**. Potvrdit `deploy/vnprobe.sh` (registr 23), až bude robot.
⚠️ **Na `20260906-153657.rec` se to dělat nesmí** — tam byla aktivní stará kalibrace z ARBot2,
takže by proložení dělalo *korekci korekce* a vyšla by věrohodně vypadající hloupost.
Použitelné jsou jen `20260907-170728` a `20260906-082403`.

- [x] **Krok 5b: Vyšla přitom vada výkonu v jádře — proložení bylo O(m²)** (opraveno 8. 9. 2026)

Report nad tím záznamem **vůbec nedoběhl**: `MagCalFit` počítal `Svd` nad maticí *m*×10 a MathNet
k tomu tvoří **plnou matici `U` (*m*×*m*)**, tedy pro 45 185 vzorků 16 GB a čas v hodinách.
Změřeno na syntetice: 1 200 vzorků 69 ms, 3 000 462 ms, 6 000 1 929 ms, 12 000 **7 729 ms**.

⚠️ **Není to problém reportu, je to vada mise.** `MagCalCollector` prokládá **1× za sekundu nad
vším, co dosud nasbíral**, a VN100 posílá 100–200 Hz — takže **po minutě otáčení by se
`mission=magcal` zadusila vlastním proložením**, a to na Orange Pi ještě dřív než na PC. Na HW
by se to našlo až v poli.

**Léčba: `MagCalFit.MaxFitSamples = 1500` a rovnoměrné ředění** (každý k-tý) uvnitř `TryFit`.
Smí se to proto, že **podmíněnost je na počtu vzorků invariantní** — změřeno na tomtéž
syntetickém vstupu **434,4 pro 60, 300, 1 200, 3 000, 6 000 i 12 000 vzorků** (rozdíl pod
desetinu), takže se ředí veličina, na které počet řádků nezávisí. Hlídá to test
`Podminenost_JeInvariantni_NaPoctuVzorku`, výsledek pak `NadStropem_SeRedi_AVysledekZustane`.
**Zbytky a měřítko se dál počítají ze VŠECH vzorků** — ředí se jen soustava; kvalita se má
posuzovat na všech datech. Ředit se musí **rovnoměrně**, ne „prvních N": později naměřené
náklony jsou právě ta část dat, která soustavu určuje.

- [x] **Krok 5c: Rozbor sklonu a průběh v poli** (10. 9. 2026, nad `20260910-170809.rec`)

Verdikt v poli padl na `sd(sklonu)` a z jednoho čísla se nedalo poznat, jestli je to kalibrace,
nebo akcelerometr. Report má proto blok **7) ROZBOR SKLONU** — `sd(sklonu)` nad klidnými vzorky
(`|acc|` do ±1 % **klidové hodnoty z dat**, `|ω|` < 20 °/s), nad akcelerometrem hlazeným 1 s,
po řádcích mřížky (`MagCalCoverage.RowOf`, kvůli tomu zveřejněný), po azimutech na rovině
(1./2. harmonická), podle odchylky `|acc|` od klidu, a **akcelerometr proložený jako koule**
(bias a měřítko) — a blok **8) PRŮBĚH V POLI** (řádek `MagCalMsg` při každé změně verdiktu,
min/max podmíněnosti), aby šlo číslo, které si obsluha pamatuje, přiřadit k okamžiku.
`MagCalFit.TryFit` dostal přetížení s `out string duvod`, které při neurčeném proložení řekne,
která ze tří bran za podmíněností spadla (vlastní čísla `A`). Výsledky: plán, fáze 1c.

- [ ] **Krok 6: Commit** (jen na pokyn autora)

```bash
git add Src/ARBot.Analyze/MagCalReport.cs Src/ARBot.Analyze/Program.cs doc/devlog.md
git commit -m "Offline kalibrace magnetometru ze zaznamu (ARBot.Analyze magcal)"
```

---

### Task 4: Surové pole (`UncompMag`)

**Nejdřív se ověří, jestli to vůbec jde** — a podle odpovědi se jde jednou z dvou cest. Bez
tohohle tasku má celý řetěz předpoklad „registr 23 byl při nahrávání jednotkový", který **z dat
zkontrolovat nelze**.

**Files:**
- Modify: `Src/ARBot.Common/Models/IMUState.cs` (pole `Magnetometer` je na řádku 37,
  `FormatVersion` na 115, `ToData` na 326, `FromData` na 343)
- Modify: `Src/ARBot.HAL/Devices/AHRS/VN100IMUBinary.cs:57-64` (`BinaryOutputConfig`)
- Test: `Src/ARBot.Common.Tests/Devices/IMUStateSerializationTests.cs` (nový nebo doplnit
  existující sadu ve `.../Devices/`)

**Interfaces:**
- Produces: `IMUState.MagnetometerRaw` (`Vector3?`), `IMUState.FormatVersion == 4`

- [x] **Krok 1: Ověřit, že `UncompMag` v DLL je**

```bash
dotnet build Src/ARBot.HAL/ARBot.HAL.csproj -p:Platform=x64
```

Do `VN100IMUBinary.cs` dočasně přidej k `ImuGroup` člen `| BinaryOutputConfig.ImuGroupOptions.UncompMag`
a přelož. **Přeloží se → jdi krokem 2. Nepřeloží se → jdi krokem 8 (záložní cesta).**
Odpověď zapiš do [plan-vn100-kalibrace.md](plan-vn100-kalibrace.md) do „Otevřené otázky".

- [x] **Krok 2: Napsat padající serializační test**

```csharp
        [Test]
        public void IMUState_SurovePole_ProjdeSerializaci()
        {
            var a = new IMUState
            {
                Magnetometer = new Vector3(0.40f, 0.10f, -0.30f),
                MagnetometerRaw = new Vector3(0.12f, 0.05f, -0.22f),
            };

            var b = (IMUState)Kolecko(a);   // pomucka sady: ToData -> FromData

            Assert.That(b.MagnetometerRaw, Is.Not.Null);
            Assert.That(b.MagnetometerRaw.Value.X, Is.EqualTo(0.12f).Within(1e-6f));
            Assert.That(b.Magnetometer.Value.X, Is.EqualTo(0.40f).Within(1e-6f));
        }

        [Test]
        public void IMUState_BezSurovehoPole_ZustaneNull()
        {
            var b = (IMUState)Kolecko(new IMUState { Magnetometer = new Vector3(0.4f, 0f, 0f) });
            Assert.That(b.MagnetometerRaw, Is.Null);
        }
```

- [x] **Krok 3: Spustit, ověřit, že padá**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter IMUState
```

- [x] **Krok 4: Přidat pole do `IMUState`**

```csharp
        /// <summary>
        /// <b>NEKOMPENZOVANE</b> magneticke pole [G] — tak, jak ho cidlo meri, PRED kompenzaci
        /// registrem 23. <c>null</c> u zdroju, ktere ho neposilaji (T265, virtualni IMU).
        ///
        /// <para><b>Nacpak.</b> <see cref="Magnetometer"/> je pole PO kompenzaci, takze prolozeni
        /// kalibrace nad nim by dalo <b>korekci korekce</b> — a ze zaznamu se to NEPOZNA (elipsoida
        /// uz kompenzovanych dat je priblizne vycentrovana, coz je od dobreho zeleza
        /// nerozeznatelne). Se surovym polem je ten predpoklad odstranen konstrukcne a offline
        /// prolozeni jde udelat z KTERÉHOKOLI zaznamu. Viz doc/plan-vn100-kalibrace.md.</para>
        /// </summary>
        public Vector3? MagnetometerRaw;
```

- [x] **Krok 5: `FormatVersion` na 4 a serializace**

⚠️ **Starší záznamy se musí dál čítat.** `FromData` čte nové pole jen když je verze ≥ 4;
`Version` je ve `Message` dostupná z hlavičky. Vzor podmíněného čtení hledej v `MissionMsg`
(je na verzi 6 a starší verze čte).

```csharp
        public const int FormatVersion = 4;   // 4: MagnetometerRaw (surove pole pro kalibraci)
```

V `ToData` zapiš `MagnetometerRaw` stejným způsobem jako `Magnetometer`; v `FromData` ho čti
**pod podmínkou verze**, aby záznamy s verzí 3 nespadly.

- [x] **Krok 6: Spustit testy**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter IMUState
```

- [x] **Krok 7: Ověřit, že starší záznam se pořád čte**

```bash
dotnet run --project Src/ARBot.Analyze -p:Platform=x64 -- types records/20260903-153947.rec
```

Očekávané: vypíše typy bez výjimky. **Kdyby spadlo, je rozbité čtení verze 3** — to je regrese
na všech existujících záznamech, ne detail.

- [x] **Krok 7b: Rozšířit `MagCalReport` na surové pole**

Task 3 čte jen `Magnetometer`, protože `MagnetometerRaw` tehdy neexistoval. Teď existuje:

```csharp
                var pole = i.MagnetometerRaw ?? i.Magnetometer;
                if (i.MagnetometerRaw.HasValue) surove = true;
                if (pole == null) { bezPole++; continue; }
```

a hlášku o zdroji pole nahraď rozlišením (`bool surove = false;` k ostatním počítadlům):

```csharp
            Console.WriteLine(surove
                ? "  pole: SUROVE (MagnetometerRaw) - vysledek je ABSOLUTNI kalibrace"
                : "  ⚠️ pole: KOMPENZOVANE (Magnetometer) - vysledek plati JEN kdyz byl registr 23"
                  + " pri nahravani JEDNOTKOVY. Ze zaznamu se to NEPOZNA.");
```

Ověř na **starém** záznamu, že se pořád tiskne varování (surové pole v něm není):

```bash
dotnet run --project Src/ARBot.Analyze -p:Platform=x64 -- magcal records/20260903-153947.rec
```

- [~] **Krok 8: ZÁLOŽNÍ CESTA — NEPOUŽITA.** ✅ `UncompMag` v `vndotnetlib-0.4/VectorNav.dll`
      **je** (ověřeno překladem 8. 9. 2026; v enumu jsou i `UncompAccel` a `UncompGyro`).
      Předpoklad „registr 23 = identita" je tím odstraněn **konstrukčně** a mise registr 23 před
      měřením mazat **nesmí**. Záložní cesta se neprovádí.

Vrať změnu v `VN100IMUBinary` i `MagnetometerRaw` (kroky 2–7 přeskoč) a do
[plan-vn100-kalibrace.md](plan-vn100-kalibrace.md) zapiš, že platí záložní varianta: **mise
vynuluje registr 23 před sběrem** (doplní se v Tasku 6) a offline re-fit vyžaduje znalost
registru 23 z doby nahrávání. `MagCalReport` už to hlásí sám (varování „pole: KOMPENZOVANE").

- [ ] **Krok 9: Commit** (jen na pokyn autora)

```bash
git add Src/ARBot.Common/Models/IMUState.cs Src/ARBot.HAL/Devices/AHRS/VN100IMUBinary.cs Src/ARBot.Common.Tests doc/plan-vn100-kalibrace.md
git commit -m "Surove magneticke pole z VN100 (IMUState.MagnetometerRaw, format 4)"
```

---

### Task 5: `VnCommands` — registry 23/44/47 a čtecí cesta v driveru

**Files:**
- Modify: `Src/ARBot.HAL/Devices/AHRS/VnCommands.cs` (vzor `ReferenceVectorConfig` na řádku 62)
- Modify: `Src/ARBot.HAL/Devices/AHRS/VN100IMUBinary.cs` (čtecí cesta)
- Test: `Src/ARBot.HAL.Tests/Devices/VnCommandsTests.cs`

**Interfaces:**
- Produces:
  - `VnCommands.RegMagCompensation = 23`, `RegMagCalControl = 44`, `RegCalculatedHsi = 47`,
    `RegMagGravityReference = 21`
  - `VnCommands.MagnetometerCompensation(string dvanactCisel) → string`
  - `VnCommands.MagCalControl(bool run) → string`
  - `VnCommands.ReadRegister(int reg) → string`
  - `VnCommands.SaveToFlash() → string`
  - `VnCommands.TryParseResponse(byte[] buffer, int reg, out double[] values) → bool`

- [x] **Krok 1: Napsat padající testy**

⚠️ Druhý test je ten podstatný: driver jede **binárně**, takže ASCII odpovědi přicházejí
**utopené v binárním toku**. `deploy/vnprobe.sh` si na to naběhl a má to v hlavičce — hledá se
rámec `$VN…*XX` **v bajtovém proudu, ne po řádcích**.

```csharp
using System;
using System.Linq;
using System.Text;
using ARBot.HAL.Devices.AHRS;
using NUnit.Framework;

namespace ARBot.HAL.Tests.Devices
{
    /// <summary>
    /// Prikazy VN100 pro kalibraci magnetometru a parsovani odpovedi z BINARNIHO toku.
    /// Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public class VnCommandsTests
    {
        [Test]
        public void KompenzaceMagnetometru_MaSpravnyTvar()
        {
            string s = VnCommands.MagnetometerCompensation(
                "1.222000,0.005000,0.010000,0.002000,1.175000,-0.012000,"
                + "-0.004000,-0.017000,1.081000,-0.274000,-0.058000,0.076000");

            Assert.That(s, Does.StartWith("VNWRG,23,"), "musi to byt ZAPIS, ne VNRRG");
            Assert.That(s.Split(',').Length, Is.EqualTo(14), "VNWRG + 23 + 12 cisel");
            Assert.That(s, Does.Not.Contain(" "));
        }

        [Test]
        public void OdpovedVBinarnimToku_SePrecte()
        {
            // Binarni smeti pred i za ASCII odpovedi - presne tak to prijde z linky.
            var telo = "VNRRG,23,1.000,0.000,0.000,0.000,1.000,0.000,0.000,0.000,1.000,0.000,0.000,0.000";
            var ascii = Encoding.ASCII.GetBytes(VnCommands.Frame(telo));
            var smeti = new byte[] { 0xFA, 0x01, 0x28, 0x00, 0x7F, 0xE3, 0xFA, 0x01 };
            var buf = smeti.Concat(ascii).Concat(smeti).ToArray();

            Assert.That(VnCommands.TryParseResponse(buf, 23, out var v), Is.True);
            Assert.That(v.Length, Is.EqualTo(12));
            Assert.That(v[0], Is.EqualTo(1.0).Within(1e-9));
            Assert.That(v[9], Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void OdpovedNaJinyRegistr_SeNevezme()
        {
            var ascii = Encoding.ASCII.GetBytes(VnCommands.Frame("VNRRG,35,1,0,1,1"));
            Assert.That(VnCommands.TryParseResponse(ascii, 23, out _), Is.False);
        }

        [Test]
        public void MagCalControl_ZapneRunBezAplikace()
        {
            // HSIMode=Run, HSIOutput=NoOnboard: senzor pocita, ale NEAPLIKUJE - je to nezavisla
            // kontrola naseho prolozeni, ne druha kalibrace.
            Assert.That(VnCommands.MagCalControl(true), Is.EqualTo("VNWRG,44,1,1,5"));
            Assert.That(VnCommands.MagCalControl(false), Is.EqualTo("VNWRG,44,0,1,5"));
        }

        [Test]
        public void UlozeniDoFlash_JeVNWNV()
        {
            Assert.That(VnCommands.SaveToFlash(), Is.EqualTo("VNWNV"));
        }
    }
}
```

- [x] **Krok 2: Spustit, ověřit, že padá**

```bash
dotnet test Src/ARBot.HAL.Tests/ARBot.HAL.Tests.csproj -p:Platform=x64 --filter VnCommandsTests
```

- [x] **Krok 3: Doplnit `VnCommands`**

```csharp
        /// <summary>Registr 21: referencni vektory pole a gravitace (odtud se bere <c>|B|</c>).</summary>
        public const int RegMagGravityReference = 21;

        /// <summary>Registr 23: kompenzace magnetometru — <c>m_comp = C · (m_raw − B)</c>.</summary>
        public const int RegMagCompensation = 23;

        /// <summary>Registr 44: rizeni palubni HSI kalibrace.</summary>
        public const int RegMagCalControl = 44;

        /// <summary>Registr 47: kalibrace, kterou spocital SAM senzor (nezavisla kontrola).</summary>
        public const int RegCalculatedHsi = 47;

        /// <summary>
        /// Telo prikazu pro <b>registr 23</b>. Cisla se predavaji uz zformatovana
        /// (<see cref="ARBot.Common.Calibration.MagCalResult.ToVnwrg23"/>), protoze prave tam je
        /// zaruceno, ze maji desetinnou TECKU — carka by rozbila prikaz oddeleny carkami.
        /// </summary>
        public static string MagnetometerCompensation(string dvanactCisel)
        {
            if (string.IsNullOrWhiteSpace(dvanactCisel))
                throw new ArgumentNullException(nameof(dvanactCisel));
            int n = dvanactCisel.Split(',').Length;
            if (n != 12) throw new ArgumentException($"Ceka se 12 cisel, prislo {n}.", nameof(dvanactCisel));
            return $"VNWRG,{RegMagCompensation},{dvanactCisel}";
        }

        /// <summary>
        /// Telo prikazu pro <b>registr 44</b>: <c>HSIMode</c>, <c>HSIOutput</c>, <c>ConvergeRate</c>.
        ///
        /// <para><c>HSIOutput</c> zustava <b>1 = NoOnboard</b> i pri zapnutem <c>Run</c>: senzor
        /// vysledek spocita do registru 47, ale <b>neaplikuje</b>. Je to nezavisla kontrola naseho
        /// prolozeni, ne druha kalibrace, ktera by se s nasi michala.</para>
        /// </summary>
        public static string MagCalControl(bool run)
            => $"VNWRG,{RegMagCalControl},{(run ? 1 : 0)},1,5";

        /// <summary>Telo cteciho prikazu.</summary>
        public static string ReadRegister(int reg) => $"VNRRG,{reg}";

        /// <summary>
        /// Ulozeni cele sady registru do flash.
        ///
        /// <para>⚠️ Uklada <b>vsechno, jak to je prave v RAM</b> — tedy i to, co tam zapsal driver
        /// pri startu (ADOR a binarni vystup). Je to neskodne (driver si je pise pri kazdem
        /// startu), ale flash se v tech polozkach rozejde s referencnim exportem.</para>
        ///
        /// <para>⚠️ Zapis do flash jde overit jen zpetnym ctenim, a to cte z RAM. <b>Skutecny test
        /// je az vypnuti a zapnuti robota.</b></para>
        /// </summary>
        public static string SaveToFlash() => "VNWNV";

        /// <summary>
        /// <b>Vytahne odpoved na <c>VNRRG,&lt;reg&gt;</c> z BAJTOVEHO PROUDU.</b>
        ///
        /// <para>⚠️ <b>Proc ne po radcich.</b> Driver prepne senzor do binarniho rezimu, takze po
        /// lince tece ~9 kB/s binarnich dat a ASCII odpovedi jsou v nich <b>utopene</b> — bajt
        /// 0x0A se v binarnich datech vyskytuje bezne, takze deleni na radky rozseka odpoved
        /// uprostred. Hleda se proto rámec <c>$VN…*XX</c> v celem vzorku.
        /// <c>deploy/vnprobe.sh</c> na tuhle past naslapl a ma ji v hlavicce.</para>
        /// </summary>
        public static bool TryParseResponse(byte[] buffer, int reg, out double[] values)
        {
            values = null;
            if (buffer == null || buffer.Length < 8) return false;
            string ocekavano = $"VNRRG,{reg},";

            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != (byte)'$') continue;
                int hvezda = -1;
                for (int j = i + 1; j < buffer.Length && j - i < 512; j++)
                {
                    if (buffer[j] == (byte)'*') { hvezda = j; break; }
                    // Bajt mimo tisknutelne ASCII = tohle nebyl zacatek ramce.
                    if (buffer[j] < 0x20 || buffer[j] > 0x7E) break;
                }
                if (hvezda < 0 || hvezda + 2 >= buffer.Length) continue;

                string telo = Encoding.ASCII.GetString(buffer, i + 1, hvezda - i - 1);
                if (!telo.StartsWith(ocekavano, StringComparison.Ordinal)) continue;

                string souctem = Encoding.ASCII.GetString(buffer, hvezda + 1, 2);
                if (!byte.TryParse(souctem, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                   out byte ocekavanySoucet)
                    || Checksum(telo) != ocekavanySoucet)
                {
                    Trace.WriteLine($"VN100: odpoved na registr {reg} ma vadny kontrolni soucet - zahozeno.");
                    continue;
                }

                var casti = telo.Substring(ocekavano.Length).Split(',');
                var v = new double[casti.Length];
                for (int k = 0; k < casti.Length; k++)
                    if (!double.TryParse(casti[k], NumberStyles.Float, CultureInfo.InvariantCulture, out v[k]))
                        return false;
                values = v;
                return true;
            }
            return false;
        }
```

Doplň `using System.Diagnostics;`, `System.Globalization;` a `System.Text;`. **`Trace`, ne
`Debug`** — v Release na zařízení by `Debug` nezanechal po poruše žádnou stopu.

- [x] **Krok 4: Spustit testy**

```bash
dotnet test Src/ARBot.HAL.Tests/ARBot.HAL.Tests.csproj -p:Platform=x64 --filter VnCommandsTests
```

Očekávané: 5 testů PASS.

- [x] **Krok 5: Čtecí cesta v `VN100IMUBinary`**

Driver dostane metodu, která pošle `VNRRG` a chvíli sbírá bajty, dokud v nich `TryParseResponse`
nenajde odpověď. Timeout **1 s**, tři pokusy; při selhání `Trace` a `null`.

```csharp
        /// <summary>
        /// <b>Precte registr</b> — posle <c>VNRRG</c> a hleda odpoved v prichozim BINARNIM toku.
        /// <c>null</c> = nepodarilo se (zapsano do <see cref="Trace"/>).
        ///
        /// <para>⚠️ Neni to rychla operace: odpoved se ceka az 1 s a zkousi se trikrat. Volat jen
        /// pri zakladani a ukonceni mise, ne v ridici smycce.</para>
        /// </summary>
        public double[] ReadRegister(int reg, int pokusu = 3)
        {
            for (int p = 0; p < pokusu; p++)
            {
                var buf = ZacniSbirat();      // odchyt prichozich bajtu (kruhovy buffer driveru)
                uart.WriteLine(VnCommands.Frame(VnCommands.ReadRegister(reg)));
                var az = TimeBase.Now.AddSeconds(1);
                while (TimeBase.Now < az)
                {
                    if (VnCommands.TryParseResponse(buf.Snapshot(), reg, out var v)) return v;
                    Thread.Sleep(20);
                }
                Trace.WriteLine($"VN100: registr {reg} neodpovedel (pokus {p + 1}/{pokusu}).");
            }
            return null;
        }

        /// <summary>Zapise registr; <c>true</c> = zpetne cteni potvrdilo zapis.</summary>
        public bool WriteRegister(string telo, int reg, double[] ocekavano = null)
        {
            uart.WriteLine(VnCommands.Frame(telo));
            Thread.Sleep(400);
            var zpet = ReadRegister(reg);
            if (zpet == null) { Trace.WriteLine($"VN100: zapis registru {reg} nelze overit."); return false; }
            if (ocekavano == null) return true;
            for (int i = 0; i < Math.Min(zpet.Length, ocekavano.Length); i++)
                if (Math.Abs(zpet[i] - ocekavano[i]) > 1e-3)
                {
                    Trace.WriteLine($"VN100: registr {reg} po zapisu nesouhlasi na pozici {i}: "
                                    + $"{zpet[i]} != {ocekavano[i]}.");
                    return false;
                }
            return true;
        }
```

⚠️ **`ZacniSbirat()` / `Snapshot()` nejsou hotové — je to jediné místo tasku, kde se musí sáhnout
do stávajícího driveru.** Nezakládej **druhý odběr z portu**: dva čtenáři jednoho UARTu si data
rozeberou a binární rámce se začnou ztrácet. Postup:

1. Najdi v `VN100IMUBinary` (a jeho předku `VN100IMU`) místo, kde se čtou bajty z `uart` a skládají
   binární rámce — tam už nějaký buffer je.
2. Přidej k němu **odbočku**: `byte[] odposlech` (kruhový, 4 kB) plněný **týmiž** bajty, které
   procházejí do skladače rámců. Zápis pod `lock`.
3. `ZacniSbirat()` = vynulovat pozici odposlechu a vrátit jeho úchyt; `Snapshot()` = kopie
   obsahu pod `lock`.

```csharp
        private readonly object odposlechLock = new object();
        private readonly byte[] odposlech = new byte[4096];
        private int odposlechPos;

        /// <summary>
        /// <b>Odbocka bajtoveho toku pro ASCII odpovedi.</b> Vola se z TEHOZ misto, kde se bajty
        /// predavaji skladaci binarnich ramcu — ne z druheho ctenim portu (dva ctenari jednoho
        /// UARTu si data rozeberou a ramce se zacnou ztracet).
        /// </summary>
        private void Odposlechni(byte[] data, int offset, int count)
        {
            lock (odposlechLock)
                for (int i = 0; i < count; i++)
                    odposlech[odposlechPos++ % odposlech.Length] = data[offset + i];
        }

        private void ZacniSbirat()
        {
            lock (odposlechLock) { Array.Clear(odposlech, 0, odposlech.Length); odposlechPos = 0; }
        }

        private byte[] Snapshot()
        {
            lock (odposlechLock)
            {
                int n = Math.Min(odposlechPos, odposlech.Length);
                var kopie = new byte[n];
                Array.Copy(odposlech, kopie, n);
                return kopie;
            }
        }
```

⚠️ Kruhový buffer se po 4 kB přepíše — při 9 kB/s je to **necelá půlsekunda**. `ReadRegister`
proto kontroluje `Snapshot()` každých 20 ms, ne až na konci timeoutu; jinak by odpověď stihla
odejít.

- [x] **Krok 6: Build HAL**

```bash
dotnet build Src/ARBot.HAL/ARBot.HAL.csproj -p:Platform=x64
```

- [ ] **Krok 7: Commit** (jen na pokyn autora)

```bash
git add Src/ARBot.HAL/Devices/AHRS Src/ARBot.HAL.Tests/Devices/VnCommandsTests.cs
git commit -m "Prikazy a cteni registru VN100 pro kalibraci magnetometru (23/44/47)"
```

---

### Task 6: `MagCalMsg` a `MagCalCollector`

Sběr ze streamu, integrace gyra, verdikt a zpráva do záznamu. Ještě bez mise.

**Files:**
- Create: `Src/ARBot.Common/Logs/MagCalMsg.cs`
- Create: `Src/ARBot.Common/Calibration/MagCalCollector.cs`
- Test: `Src/ARBot.Common.Tests/Calibration/MagCalCollectorTests.cs`

**Interfaces:**
- Consumes: `MagCalFit.Fit`, `MagCalResult`, `MagCalCoverage`, `MagCalThresholds` (Task 1–2),
  `IMUState.MagnetometerRaw` (Task 4), `Message`, `IMessageSink`
- Produces:
  - `MagCalCollector(double bRefG, TimeSpan? fitPeriod = null)` s `Add(IMUState) → bool`
    (`true` = právě se přepočítalo proložení), `Coverage`, `LastResult`, `Verdict`, `Usable`,
    `BRefG`, `Reg23Before` (settable), `ToLogMessage() → MagCalMsg`
  - `MagCalMsg` s poli `Phase`, `Condition`, `SdMagnitudeG`, `SdInclinationDeg`, `Vnwrg23`
    (string), `Verdict` (string), `MissingText` (string), `FilledAzimuthBins`, `TiltGroups`,
    `TiltedGroups`, `Samples`, `BRefG`, `Reg23Before` (string), `TimeStamp`

- [x] **Krok 1: Napsat padající testy kolektoru**

Syntetická data jako v `MagCalFitTests`, ale skrz kolektor — tedy **včetně integrace gyra**.
Žádné výpustky: data se vyrábějí celá.

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using ARBot.Common.Calibration;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using MathNet.Numerics.LinearAlgebra;
using NUnit.Framework;

namespace ARBot.Common.Tests.Calibration
{
    /// <summary>
    /// Sberac kalibrace: integruje gyro, plni kose, obcas prolozi a vyrobi MagCalMsg.
    /// Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public class MagCalCollectorTests
    {
        private const double Bref = 0.4818;
        private const double SklonRad = 1.0638;
        private static readonly DateTime T0 = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

        private static readonly Matrix<double> C = Matrix<double>.Build.DenseOfArray(new[,] {
            { 1.222, 0.005, 0.010 }, { 0.005, 1.175, -0.012 }, { 0.010, -0.012, 1.081 } });
        private static readonly Vector<double> Bias =
            Vector<double>.Build.DenseOfArray(new[] { -0.274, -0.058, 0.076 });

        /// <summary>Surove pole pri danem kurzu a naklonu: m_raw = C^-1 * m_ideal + b.</summary>
        private static Vector3 Pole(double yaw, double naklon)
        {
            double h = Bref * Math.Cos(SklonRad), v = -Bref * Math.Sin(SklonRad);
            var q = Quaternion.CreateFromYawPitchRoll(0f, 0f, (float)naklon)
                  * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)-yaw);
            var id = Vector3.Transform(new Vector3((float)h, 0f, (float)v), q);
            var x = Vector<double>.Build.DenseOfArray(new double[] { id.X, id.Y, id.Z });
            var r = C.Inverse() * x + Bias;
            return new Vector3((float)r[0], (float)r[1], (float)r[2]);
        }

        private static Vector3 Gravitace(double naklon)
            => new Vector3(0f, (float)(-9.81 * Math.Sin(naklon)), (float)(-9.81 * Math.Cos(naklon)));

        /// <summary>Jeden obrat o 360° pri danem naklonu, 100 Hz, 0,5 rad/s.</summary>
        private static void Obrat(MagCalCollector c, double naklon, ref double t)
        {
            const double omega = 0.5, dt = 0.01;
            int n = (int)(2 * Math.PI / omega / dt);
            for (int i = 0; i < n; i++)
            {
                double yaw = omega * dt * i;
                c.Add(new IMUState
                {
                    Name = "VN100 IMU",
                    HasAbsoluteHeading = true,
                    MagnetometerRaw = Pole(yaw, naklon),
                    Acceleration = Gravitace(naklon),
                    AngularVelocity = new Vector3(0f, 0f, (float)omega),
                    TimeStamp = T0.AddSeconds(t),
                });
                t += dt;
            }
        }

        [Test]
        public void JenObratNaRovine_NeniPouzitelne()
        {
            var c = new MagCalCollector(Bref);
            double t = 0;
            Obrat(c, 0.0, ref t);

            Assert.That(c.Coverage.Complete, Is.False);
            Assert.That(c.Usable, Is.False, "bez naklonu je z vymyslene - nesmi to byt pouzitelne");
            Assert.That(c.Verdict, Does.Contain("naklon"));
        }

        [Test]
        public void TriObratySNaklony_JePouzitelne_ASediParametry()
        {
            var c = new MagCalCollector(Bref);
            double t = 0;
            Obrat(c, 0.0, ref t);
            Obrat(c, 0.40, ref t);
            Obrat(c, -0.40, ref t);

            Assert.That(c.Coverage.Complete, Is.True);
            Assert.That(c.LastResult, Is.Not.Null);
            Assert.That(c.LastResult.Condition, Is.LessThan(MagCalThresholds.MaxCondition));
            Assert.That(c.Usable, Is.True);
            for (int i = 0; i < 3; i++)
                Assert.That(c.LastResult.B[i], Is.EqualTo(Bias[i]).Within(0.005));
        }

        [Test]
        public void IMUBezAbsolutnihoKurzu_SeIgnoruje()
        {
            // V robotu je IMU vic a T265 posila RELATIVNI yaw. Michat dve ruzne nuly by dalo
            // nesmysl - stejny duvod jako ve Vn100Report.
            var c = new MagCalCollector(Bref);
            c.Add(new IMUState
            {
                Name = "T265",
                HasAbsoluteHeading = false,
                MagnetometerRaw = new Vector3(0.4f, 0.1f, -0.3f),
                Acceleration = Gravitace(0),
                AngularVelocity = Vector3.Zero,
                TimeStamp = T0,
            });

            Assert.That(c.Coverage.Mag.Count, Is.EqualTo(0));
        }

        [Test]
        public void MezeraVDatech_SeNeintegruje()
        {
            // Po dlouhe mezere se uhlova rychlost nesmi integrovat - nasbiralo by se otoceni,
            // ktere se nestalo, a kose by se naplnily vedle.
            var c = new MagCalCollector(Bref);
            var a = new IMUState
            {
                Name = "VN100 IMU", HasAbsoluteHeading = true,
                MagnetometerRaw = Pole(0, 0), Acceleration = Gravitace(0),
                AngularVelocity = new Vector3(0f, 0f, 1.0f), TimeStamp = T0,
            };
            var b = (IMUState)a.Clone();
            b.TimeStamp = T0.AddSeconds(30);   // mezera 30 s

            c.Add(a);
            c.Add(b);

            // Dva vzorky, oba do TEHOZ azimutoveho kose (yaw se nezmenil).
            Assert.That(c.Coverage.AzimuthCounts[0], Is.EqualTo(2));
        }

        [Test]
        public void ToLogMessage_NeseVerdiktIPokryti()
        {
            var c = new MagCalCollector(Bref) { Reg23Before = "1,0,0,0,1,0,0,0,1,0,0,0" };
            double t = 0;
            Obrat(c, 0.0, ref t);
            Obrat(c, 0.40, ref t);
            Obrat(c, -0.40, ref t);

            var m = c.ToLogMessage();

            Assert.That(m.Vnwrg23.Split(',').Length, Is.EqualTo(12));
            Assert.That(m.FilledAzimuthBins, Is.EqualTo(MagCalThresholds.AzimuthBins));
            Assert.That(m.TiltedGroups, Is.GreaterThanOrEqualTo(MagCalThresholds.MinTiltedGroups));
            Assert.That(m.BRefG, Is.EqualTo(Bref).Within(1e-9));
            Assert.That(m.Reg23Before, Is.EqualTo("1,0,0,0,1,0,0,0,1,0,0,0"));
        }
    }
}
```

- [x] **Krok 2: Spustit, ověřit chybu překladu**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter MagCalCollectorTests
```

- [x] **Krok 3: `MagCalMsg`**

Vzor je `Src/ARBot.Common/Logs/PerfMsg.cs`: `base("MagCalMsg", 1)`, `Write(bw, …)` a
`ReadDateTime(br)` jsou dědené pomůcky, `Build()` vrací novou instanci.

```csharp
using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// <b>Stav kalibrace magnetometru</b> za jeden interval sberu (~1 s).
    ///
    /// <para><b>Proc zprava a ne jen text na strance.</b> Ve streamu jde soucasne do webu (zivy
    /// ukazatel pro obsluhu, ktera stoji u robota) i do <b>zaznamu</b> — takze verdikt z pole je
    /// pozdeji dohledatelny a <c>ARBot.Analyze magcal</c> ho umi postavit vedle vlastniho
    /// prepoctu ze surovych <c>IMUState</c>. Kdyz se rozejdou, je chyba v KODU, ne v senzoru,
    /// a pozna se to.</para>
    ///
    /// <para><see cref="Reg23Before"/> je stav registru 23 pri zacatku mise. Nese se proto, ze
    /// bez neho se u starsich zaznamu (bez suroveho pole) neda rict, jestli je vysledek absolutni
    /// kalibrace, nebo korekce korekce — a ze samotnych dat se to nepozna.</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    [Serializable()]
    public class MagCalMsg : Message, IHasCaptureTime
    {
        public const int FormatVersion = 1;

        /// <summary>Faze mise (<c>MagCalPhase</c> jako int, aby zprava prezila doplneni hodnot).</summary>
        public int Phase;

        /// <summary>Podminenost navrhove matice — urcenost soustavy.</summary>
        public double Condition;

        /// <summary>Rozptyl <c>|B|</c> po korekci [G].</summary>
        public double SdMagnitudeG;

        /// <summary>Rozptyl sklonu po korekci [deg].</summary>
        public double SdInclinationDeg;

        /// <summary>Dvanact cisel ke zkopirovani za <c>VNWRG,23,</c>; prazdne, dokud se neprolozilo.</summary>
        public string Vnwrg23;

        /// <summary>Verdikt pro cloveka (co udelat dal, nebo ze je hotovo).</summary>
        public string Verdict;

        /// <summary>Co jeste chybi v pokryti; prazdne = nic.</summary>
        public string MissingText;

        public int FilledAzimuthBins, TiltGroups, TiltedGroups, Samples;

        /// <summary>Referencni <c>|B|</c> [G] z registru 21 — <b>ctene ze senzoru</b>, ne konstanta.</summary>
        public double BRefG;

        /// <summary>Registr 23 pri zacatku mise (dvanact cisel), nebo prazdne.</summary>
        public string Reg23Before;

        public DateTime TimeStamp;

        /// <inheritdoc/>
        public DateTime CaptureTime => TimeStamp;

        public MagCalMsg() : base("MagCalMsg", FormatVersion) { }

        public override Message Build() => new MagCalMsg();

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Phase);
            bw.Write(Condition);
            bw.Write(SdMagnitudeG);
            bw.Write(SdInclinationDeg);
            bw.Write(Vnwrg23 ?? string.Empty);
            bw.Write(Verdict ?? string.Empty);
            bw.Write(MissingText ?? string.Empty);
            bw.Write(FilledAzimuthBins);
            bw.Write(TiltGroups);
            bw.Write(TiltedGroups);
            bw.Write(Samples);
            bw.Write(BRefG);
            bw.Write(Reg23Before ?? string.Empty);
            Write(bw, TimeStamp);
        }

        public override void FromData(BinaryReader br)
        {
            Phase = br.ReadInt32();
            Condition = br.ReadDouble();
            SdMagnitudeG = br.ReadDouble();
            SdInclinationDeg = br.ReadDouble();
            Vnwrg23 = br.ReadString();
            Verdict = br.ReadString();
            MissingText = br.ReadString();
            FilledAzimuthBins = br.ReadInt32();
            TiltGroups = br.ReadInt32();
            TiltedGroups = br.ReadInt32();
            Samples = br.ReadInt32();
            BRefG = br.ReadDouble();
            Reg23Before = br.ReadString();
            TimeStamp = ReadDateTime(br);
        }
    }
}
```

⚠️ Ověř proti `PerfMsg`, jestli `IHasCaptureTime` a `ReadDateTime` mají přesně tyhle názvy —
`MissionMsg` je implementuje taky, tak podle nich.

- [x] **Krok 4: `MagCalCollector`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Calibration
{
    /// <summary>
    /// <b>Sberac kalibrace magnetometru.</b> Vzor <c>PerfCollector</c> → <c>PerfMsg</c>:
    /// sbira ze streamu a jednou za interval vyrobi zpravu metodou
    /// <see cref="ToLogMessage"/> (konvenci projektu vlastni konverzi domena,
    /// <c>Message</c> zustava pasivni DTO).
    ///
    /// <para><b>Cas z hodin DAT, ne stroje</b> — integrace gyra i kadence prolozeni se ridi
    /// razitky zprav, takze pri prehravani zaznamu a v testech znamena totez jako za behu.
    /// Stejna zasada jako <c>IMissionStatus.Elapsed</c>.</para>
    ///
    /// <para>⚠️ <b>Prolozeni se NEPOCITA pro kazdy vzorek</b> — je to SVD nad matici <c>n×10</c>.
    /// Jednou za <see cref="FitPeriod"/> a jen kdyz od posledne pribyla data.</para>
    /// </summary>
    public sealed class MagCalCollector
    {
        /// <summary>Mezera v datech, pres kterou se uz uhlova rychlost neintegruje [s].</summary>
        private const double MaxGapSec = 0.5;

        private readonly MagCalCoverage coverage = new MagCalCoverage();
        private double yaw;
        private DateTime? tPrev;
        private DateTime tNextFit = DateTime.MinValue;
        private int samplesAtLastFit;

        public MagCalCollector(double bRefG, TimeSpan? fitPeriod = null)
        {
            if (!(bRefG > 0)) throw new ArgumentOutOfRangeException(nameof(bRefG));
            BRefG = bRefG;
            FitPeriod = fitPeriod ?? TimeSpan.FromSeconds(1);
        }

        /// <summary>Referencni <c>|B|</c> [G] z registru 21.</summary>
        public double BRefG { get; }

        /// <summary>Jak casto se prepocitava prolozeni.</summary>
        public TimeSpan FitPeriod { get; }

        /// <summary>Stav registru 23 pri zacatku mise (dvanact cisel) — jen se nese do zpravy.</summary>
        public string Reg23Before { get; set; }

        public MagCalCoverage Coverage => coverage;

        /// <summary>Posledni prolozeni; <c>null</c>, dokud neni dost vzorku.</summary>
        public MagCalResult LastResult { get; private set; }

        /// <summary>Otoceni nasbirane integraci gyra [rad] — diagnostika.</summary>
        public double IntegratedYawRad => yaw;

        /// <summary>
        /// Je vysledek pouzitelny k zapisu? <b>Vsechny</b> podminky zaroven: pokryti uplne,
        /// podminenost pod prahem, zbytky pod prahy. Rozpuleni dat kontroluje offline report —
        /// za behu by to znamenalo tri SVD misto jednoho.
        /// </summary>
        public bool Usable
            => LastResult != null
               && coverage.Complete
               && LastResult.Condition <= MagCalThresholds.MaxCondition
               && LastResult.SdMagnitudeG <= MagCalThresholds.MaxSdMagnitudeG
               && LastResult.SdInclinationDeg <= MagCalThresholds.MaxSdInclinationDeg;

        /// <summary>Verdikt pro cloveka: co udelat dal, nebo ze je hotovo.</summary>
        public string Verdict
        {
            get
            {
                string chybi = coverage.MissingText();
                if (chybi.Length > 0) return "POKRACUJ: " + chybi;
                if (LastResult == null) return "POKRACUJ: jeste malo vzorku";
                if (LastResult.Condition > MagCalThresholds.MaxCondition)
                    return string.Format(CultureInfo.InvariantCulture,
                        "POKRACUJ: podminenost {0:F1} (prah {1:F0}) - otacej dal a pridej naklon",
                        LastResult.Condition, MagCalThresholds.MaxCondition);
                if (LastResult.SdMagnitudeG > MagCalThresholds.MaxSdMagnitudeG)
                    return string.Format(CultureInfo.InvariantCulture,
                        "NEPOUZITELNE: sd(|B|) {0:F4} G nad prahem {1:F3} - pole je porad nekonzistentni",
                        LastResult.SdMagnitudeG, MagCalThresholds.MaxSdMagnitudeG);
                if (LastResult.SdInclinationDeg > MagCalThresholds.MaxSdInclinationDeg)
                    return string.Format(CultureInfo.InvariantCulture,
                        "NEPOUZITELNE: sd(sklonu) {0:F2}° nad prahem {1:F1}",
                        LastResult.SdInclinationDeg, MagCalThresholds.MaxSdInclinationDeg);
                return "HOTOVO";
            }
        }

        /// <summary>
        /// Prida vzorek. Vraci <c>true</c>, kdyz se prave prepocitalo prolozeni (tedy je smysl
        /// poslat zpravu).
        /// </summary>
        public bool Add(IMUState imu)
        {
            if (imu == null) return false;

            // V robotu je IMU vic a T265 posila RELATIVNI yaw. Michat dve ruzne nuly by dalo
            // nesmysl - stejny duvod jako ve Vn100Report.
            if (!imu.HasAbsoluteHeading) return false;

            var pole = imu.MagnetometerRaw ?? imu.Magnetometer;
            if (pole == null || imu.Acceleration == null || imu.AngularVelocity == null) return false;

            if (tPrev.HasValue)
            {
                double dt = (imu.TimeStamp - tPrev.Value).TotalSeconds;
                // Mezera v datech: integrace by pres ni nasbirala otoceni, ktere se nestalo.
                if (dt > 0 && dt < MaxGapSec) yaw += imu.AngularVelocity.Value.Z * dt;
            }
            tPrev = imu.TimeStamp;

            coverage.Add(yaw, pole.Value, imu.Acceleration.Value);

            if (imu.TimeStamp < tNextFit || coverage.Mag.Count == samplesAtLastFit) return false;
            tNextFit = imu.TimeStamp + FitPeriod;
            samplesAtLastFit = coverage.Mag.Count;

            if (coverage.Mag.Count < MagCalFit.MinSamples) return false;
            try
            {
                LastResult = MagCalFit.Fit(coverage.Mag, BRefG);
                return true;
            }
            catch (ArgumentException ex)
            {
                // Diagnostika do Trace, ne Debug: v Release na zarizeni by po poruse nezustala stopa.
                System.Diagnostics.Trace.WriteLine("MagCal: prolozeni selhalo - " + ex.Message);
                return false;
            }
        }

        /// <inheritdoc cref="MagCalMsg"/>
        public MagCalMsg ToLogMessage() => new MagCalMsg
        {
            Condition = LastResult?.Condition ?? 0,
            SdMagnitudeG = LastResult?.SdMagnitudeG ?? 0,
            SdInclinationDeg = LastResult?.SdInclinationDeg ?? 0,
            Vnwrg23 = LastResult?.ToVnwrg23() ?? string.Empty,
            Verdict = Verdict,
            MissingText = coverage.MissingText(),
            FilledAzimuthBins = coverage.FilledAzimuthBins,
            TiltGroups = coverage.TiltGroups,
            TiltedGroups = coverage.TiltedGroups,
            Samples = coverage.Mag.Count,
            BRefG = BRefG,
            Reg23Before = Reg23Before ?? string.Empty,
            TimeStamp = tPrev ?? default,
        };
    }
}
```

- [x] **Krok 5: Spustit testy kolektoru**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter MagCalCollectorTests
```

Očekávané: 5 testů PASS.

- [x] **Krok 6: Serializační test zprávy**

Vzor `Src/ARBot.Common.Tests/Diagnostics/PerfMsgSerializationTests.cs`.

```csharp
        [Test]
        public void MagCalMsg_ProjdeSerializaci()
        {
            var a = new MagCalMsg
            {
                Phase = 2, Condition = 12.5, SdMagnitudeG = 0.0012, SdInclinationDeg = 0.21,
                Vnwrg23 = "1.2,0.0,0.0,0.0,1.1,0.0,0.0,0.0,1.0,-0.274,-0.058,0.076",
                Verdict = "HOTOVO", MissingText = "",
                FilledAzimuthBins = 24, TiltGroups = 3, TiltedGroups = 2, Samples = 3770,
                BRefG = 0.4818, Reg23Before = "1,0,0,0,1,0,0,0,1,0,0,0",
                TimeStamp = new DateTime(2026, 9, 8, 12, 5, 0, DateTimeKind.Utc),
            };

            var b = (MagCalMsg)Kolecko(a);

            Assert.That(b.Condition, Is.EqualTo(12.5).Within(1e-9));
            Assert.That(b.Vnwrg23, Is.EqualTo(a.Vnwrg23));
            Assert.That(b.Verdict, Is.EqualTo("HOTOVO"));
            Assert.That(b.Reg23Before, Is.EqualTo(a.Reg23Before));
            Assert.That(b.TimeStamp, Is.EqualTo(a.TimeStamp));
        }
```

- [x] **Krok 7: Spustit**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter MagCal
```

- [x] **Krok 8: Doplnit do `MagCalReport` blok 5 — co spočítal robot v poli**

Task 3 ho vynechal, protože `MagCalMsg` neexistoval. Teď existuje, a je to ta kontrola, kvůli
které zpráva vůbec teče do záznamu: **verdikt v poli a verdikt u stolu musí být totéž číslo.**

Do smyčky nad indexem přidej `if (e.MsgName == "MagCalMsg") { posledni = (MagCalMsg)rec.Read(e); continue; }`
(a `MagCalMsg posledni = null;` k proměnným), na konec `Run`:

```csharp
            if (posledni != null)
            {
                Console.WriteLine();
                Console.WriteLine("5) CO SPOCITAL ROBOT V POLI (MagCalMsg ze zaznamu)");
                Console.WriteLine($"  podminenost {posledni.Condition:F1}, verdikt: {posledni.Verdict}");
                Console.WriteLine($"  referencni |B| tehdy: {posledni.BRefG:F4} G");
                Console.WriteLine($"  registr 23 pred merenim: {posledni.Reg23Before}");
                Console.WriteLine("  " + posledni.Vnwrg23);
                Console.WriteLine("  ⚠️ Rozdil proti bodu 3 znamena chybu v KODU, ne v senzoru.");
            }
```

⚠️ Report běží s `--bref` (výchozí 0,4818), kdežto robot měl `BRefG` **přečtené ze senzoru**.
Když se ta dvě čísla liší, liší se i výsledky **oprávněně** — proto se `BRefG` ze zprávy tiskne.

- [ ] **Krok 9: Commit** (jen na pokyn autora)

```bash
git add Src/ARBot.Common/Logs/MagCalMsg.cs Src/ARBot.Common/Calibration/MagCalCollector.cs Src/ARBot.Analyze/MagCalReport.cs Src/ARBot.Common.Tests/Calibration
git commit -m "Sberac kalibrace magnetometru, zprava MagCalMsg a jeji kontrola v reportu"
```

---

### Task 7: `IMagCalControl`, `MagCalPhase` a `MagCalMission`

**Files:**
- Modify: `Src/ARBot.Common/Missions/IMissionStatus.cs` (`MissionWait`, přidat na konec výčtu)
- Modify: `Src/ARBot.Common/Missions/MissionSeams.cs` (přidat `IMagCalControl`)
- Create: `Src/ARBot.Common/Missions/MagCalPhase.cs`, `MagCalMission.cs`
- Create: `Src/ARBot.HAL/Devices/AHRS/VnMagCalControl.cs`
- Test: `Src/ARBot.Common.Tests/Missions/MagCalMissionTests.cs`

**Interfaces:**
- Consumes: `MagCalCollector` (Task 6), `VnCommands` + `VN100IMUBinary.ReadRegister/WriteRegister`
  (Task 5), `IRegulatorHolder` a `IMissionStatus` (existující), `MessageProcessor`
  (`OverflowPolicy.DropOldest`, `Consume`, `EmitDerived`)
- Produces:
  - `MissionWait.MagCoverage = 7`
  - `IMagCalControl` — `double[] ReadRegister(int reg)`, `bool WriteMagCompensation(string)`,
    `bool SetOnboardHsi(bool run)`, `bool SaveToFlash()`
  - `MagCalPhase` — `Idle = 0`, `Collecting = 1`, `Ready = 2`, `Written = 3`
  - `MagCalMission(IMagCalControl control, IRegulatorHolder holder, TimeSpan? fitPeriod = null,
    int queueCapacity = 4)` s `StartMission()`, `WriteToSensor() → bool`, `Phase`, `Coverage`,
    `LastResult`, `BRefG`, `Reg23Before`, `MissionName`, `PhaseText`, `WaitingFor`, `Elapsed`
  - `VnMagCalControl(VN100IMUBinary imu)`

- [x] **Krok 1: `MissionWait.MagCoverage`**

Na **konec** výčtu — jeho vlastní dokumentace to žádá („nepřečíslovat, nové hodnoty přidávat
na konec"), protože číslo je součástí formátu zprávy.

```csharp
        /// <summary>Ceka, az obsluha pokryje otacenim azimuty a naklony (mise magcal).</summary>
        MagCoverage = 7,
```

- [x] **Krok 2: `IMagCalControl` do `MissionSeams.cs`**

```csharp
    /// <summary>
    /// <b>Uzky sev pro cteni a zapis kalibrace VN100</b> (v aplikaci nad driverem).
    ///
    /// <para>⚠️ <b>Je uzky ZAMERNE.</b> Projekt ma vedome nakreslenou caru — „konfigurace senzoru
    /// se meni vedome a rucne, ne vedlejsim ucinkem nejakeho mereni" (hlavicky
    /// <c>deploy/vnprobe.sh</c> a <c>vnrestore.sh</c>). Zamer te cary byl <b>zadny zapis bez
    /// rozhodnuti cloveka</b> a ten plati dal: zapis se deje jen na tuknuti na tlacitko pod
    /// <b>drzenym nouzovym zastavenim</b>, coz je silnejsi gate nez ssh session. Meni se
    /// mechanismus, ne pravidlo.</para>
    ///
    /// <para>Pojistka proti erozi: umi <b>jen</b> registry 23 a 44 a cteni 21/23/44/47, a vola ho
    /// <b>jen</b> <see cref="MagCalMission"/>. Obecne „zapis jakykoli registr" by tu caru smazalo
    /// — <b>nezakladej ho.</b> Viz doc/decisions.md a doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public interface IMagCalControl
    {
        /// <summary>Precte registr (21, 23, 44, 47); <c>null</c> = nepodarilo se.</summary>
        double[] ReadRegister(int reg);

        /// <summary>Zapise kompenzaci do registru 23 (dvanact cisel s desetinnou teckou).</summary>
        bool WriteMagCompensation(string dvanactCisel);

        /// <summary>Zapne/vypne palubni HSI (registr 44) — <b>bez</b> aplikace, jen do registru 47.</summary>
        bool SetOnboardHsi(bool run);

        /// <summary>Ulozi sadu registru do flash.</summary>
        bool SaveToFlash();
    }
```

- [x] **Krok 3: Napsat padající testy mise**

První test je **bezpečnostní**, ne kosmetický.

```csharp
using System;
using System.Numerics;
using ARBot.Common.Calibration;
using ARBot.Common.Missions;
using ARBot.Common.Models;
using ARBot.Common.Regulators;
using ARBot.HAL.Devices.AHRS;
using NUnit.Framework;

namespace ARBot.Common.Tests.Missions
{
    /// <summary>
    /// Mise magcal: STOJI, sbira pole pri otaceni rukou, hlasi pokryti a na pokyn zapise
    /// kalibraci. Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public class MagCalMissionTests
    {
        private sealed class Drzitel : IRegulatorHolder
        {
            public IRegulator Regulator { get; set; } = new PointRegulator();
        }

        private sealed class Senzor : IMagCalControl
        {
            public string Zapsano;
            public bool Flash, Hsi;
            public bool ZapisSelze;

            public double[] ReadRegister(int reg) => reg switch
            {
                // Registr 21: (0,234; 0; 0,4212) pole a (0; 0; -9,79375) gravitace.
                21 => new double[] { 0.234, 0, 0.4212, 0, 0, -9.79375 },
                23 => new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 },
                44 => new double[] { 0, 1, 5 },
                _ => null,
            };

            public bool WriteMagCompensation(string s)
            {
                if (ZapisSelze) return false;
                Zapsano = s; return true;
            }

            public bool SetOnboardHsi(bool run) { Hsi = run; return true; }
            public bool SaveToFlash() { Flash = true; return true; }
        }

        private static MagCalMission Mise(Senzor s, Drzitel d) => new MagCalMission(s, d);

        [Test]
        public void PriStartu_JeRegulatorZahozeny()
        {
            var d = new Drzitel();
            using var m = Mise(new Senzor(), d);

            m.StartMission();

            Assert.That(d.Regulator, Is.Null,
                "mise magcal nesmi NIKDY nechat robota rozjet - je to bezpecnostni invariant");
        }

        [Test]
        public void PriStartu_PrecteRegistr21_AZapneHsi()
        {
            var s = new Senzor();
            using var m = Mise(s, new Drzitel());

            m.StartMission();

            Assert.That(m.BRefG, Is.EqualTo(0.4818).Within(0.001),
                "referencni |B| se CTE z registru 21, nepise natvrdo");
            Assert.That(s.Hsi, Is.True, "registr 44 na Run/NoOnboard - nezavisla kontrola");
            Assert.That(m.Reg23Before, Is.Not.Empty, "stav registru 23 patri do zaznamu");
        }

        [Test]
        public void PoStartu_CekaNaObsluhu()
        {
            using var m = Mise(new Senzor(), new Drzitel());
            m.StartMission();

            Assert.That(m.MissionName, Is.EqualTo("magcal"));
            Assert.That(m.Phase, Is.EqualTo(MagCalPhase.Collecting));
            Assert.That(m.WaitingFor, Is.EqualTo(MissionWait.MagCoverage));
            Assert.That(m.PhaseText, Does.Contain("POKRACUJ"));
        }

        [Test]
        public void ZapisBezHotovehoPokryti_SeNEPROVEDE()
        {
            var s = new Senzor();
            using var m = Mise(s, new Drzitel());
            m.StartMission();

            Assert.That(m.WriteToSensor(), Is.False);
            Assert.That(s.Zapsano, Is.Null, "nehotova kalibrace se do senzoru zapsat NESMI");
            Assert.That(s.Flash, Is.False);
            Assert.That(m.Phase, Is.EqualTo(MagCalPhase.Collecting));
        }

        [Test]
        public void ZapisBezStartuMise_SeNEPROVEDE()
        {
            var s = new Senzor();
            using var m = Mise(s, new Drzitel());

            Assert.That(m.WriteToSensor(), Is.False);
            Assert.That(s.Zapsano, Is.Null);
        }

        [Test]
        public void SelhanyZapis_NeprejdeDoWritten()
        {
            var s = new Senzor { ZapisSelze = true };
            using var m = Mise(s, new Drzitel());
            m.StartMission();

            Assert.That(m.WriteToSensor(), Is.False);
            Assert.That(m.Phase, Is.Not.EqualTo(MagCalPhase.Written),
                "kdyz zapis selze, mise nesmi tvrdit, ze je zapsano");
            Assert.That(s.Flash, Is.False, "do flash se uklada JEN po uspesnem zapisu");
        }
    }
}
```

- [x] **Krok 4: Spustit, ověřit, že padá**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter MagCalMissionTests
```

- [x] **Krok 5: `MagCalPhase`**

```csharp
namespace ARBot.Common.Missions
{
    /// <summary>
    /// Faze mise magcal. Cisla jdou do <c>MagCalMsg</c> — <b>neprecislovat</b>, nove hodnoty
    /// pridavat na konec.
    /// </summary>
    public enum MagCalPhase
    {
        /// <summary>Jeste nezacala.</summary>
        Idle = 0,

        /// <summary>Sbira pole a ceka, az obsluha otacenim pokryje azimuty a naklony.</summary>
        Collecting = 1,

        /// <summary>Pokryti uplne a prolozeni pouzitelne — ceka se na pokyn k zapisu.</summary>
        Ready = 2,

        /// <summary>Kalibrace zapsana do registru 23 a ulozena do flash.</summary>
        Written = 3,
    }
}
```

- [x] **Krok 6: `MagCalMission`**

```csharp
using System;
using System.Diagnostics;
using ARBot.Common.Calibration;
using ARBot.Common.Communication;
using ARBot.Common.Models;
using ARBot.HAL.Devices.AHRS;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// <b>Mise magcal</b> — robot STOJI a meri si vlastni magnetickou kalibraci, kdyz s nim
    /// obsluha otaci rukou. Sourozenec <see cref="FreeRunMission"/>, ale nejjednodussi: nema cil,
    /// nema mapu a <b>neprodukuje mrkev</b>.
    ///
    /// <para><b>Proc mise a ne prepinac.</b> Mise se vylucuji (viz CLAUDE.md), takze se nevybiraji
    /// booleovskymi prepinaci. A jako mise to ubira praci: <see cref="PhaseText"/> je ten zivy
    /// ukazatel pokryti (stranka stav mise uz kresli), zaznam se rozjede volbou mise, a
    /// <see cref="IRegulatorHolder"/> da konstrukcni zaruku, ze se robot nerozjede.</para>
    ///
    /// <para>⚠️ <b>Bezpecnostni invariant:</b> po <see cref="StartMission"/> je
    /// <c>holder.Regulator == null</c> a mise ho nikdy nenastavi. Hlida to test
    /// <c>PriStartu_JeRegulatorZahozeny</c>.</para>
    ///
    /// <para>Pozor na jmena: <see cref="StartMission"/>, ne <c>Start()</c> — to by kolidovalo se
    /// zdedenou metodou <c>MessageTarget</c>, ktera spousti vlakno stupne (past uz zapsana
    /// u Robotouru).</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public sealed class MagCalMission : MessageProcessor, IMissionStatus
    {
        private readonly IMagCalControl control;
        private readonly IRegulatorHolder holder;
        private readonly TimeSpan? fitPeriod;
        private MagCalCollector collector;
        private DateTime firstSampleAt, lastSampleAt;

        public MagCalMission(IMagCalControl control, IRegulatorHolder holder,
                             TimeSpan? fitPeriod = null, int queueCapacity = 4)
            : base(OverflowPolicy.DropOldest, queueCapacity)
        {
            this.control = control ?? throw new ArgumentNullException(nameof(control));
            this.holder = holder ?? throw new ArgumentNullException(nameof(holder));
            this.fitPeriod = fitPeriod;
        }

        public MagCalPhase Phase { get; private set; } = MagCalPhase.Idle;

        /// <summary>Referencni <c>|B|</c> [G] z registru 21; nula, dokud mise nezacala.</summary>
        public double BRefG { get; private set; }

        /// <summary>Registr 23 pri zacatku mise (dvanact cisel), nebo prazdne.</summary>
        public string Reg23Before { get; private set; } = string.Empty;

        public MagCalCoverage Coverage => collector?.Coverage;
        public MagCalResult LastResult => collector?.LastResult;

        /// <inheritdoc/>
        public string MissionName => "magcal";

        /// <inheritdoc/>
        public string PhaseText => Phase switch
        {
            MagCalPhase.Idle => "Necinna",
            MagCalPhase.Written => "Kalibrace zapsana do senzoru a ulozena. "
                                   + "Pockej ~2 minuty, nez se kurz srovna.",
            _ => collector?.Verdict ?? "POKRACUJ: jeste zadna data",
        };

        /// <inheritdoc/>
        public MissionWait WaitingFor
            => Phase == MagCalPhase.Collecting || Phase == MagCalPhase.Ready
               ? MissionWait.MagCoverage
               : MissionWait.None;

        /// <inheritdoc/>
        public TimeSpan Elapsed
            => firstSampleAt == default || lastSampleAt <= firstSampleAt
               ? TimeSpan.Zero
               : lastSampleAt - firstSampleAt;

        /// <summary>
        /// Zacatek mise: precte registry, zapne palubni HSI jako nezavislou kontrolu a
        /// <b>zahodi regulator</b>.
        /// </summary>
        public void StartMission()
        {
            // Bezpecnost PRVNI: kdyby cokoli niz vyhodilo vyjimku, robot uz stoji.
            holder.Regulator = null;

            var reg21 = control.ReadRegister(VnCommands.RegMagGravityReference);
            if (reg21 != null && reg21.Length >= 3)
            {
                // Registr 21 nese referencni vektor pole (prvni tri slozky).
                BRefG = Math.Sqrt(reg21[0] * reg21[0] + reg21[1] * reg21[1] + reg21[2] * reg21[2]);
                Trace.WriteLine($"MagCal: referencni |B| z registru 21 = {BRefG:F4} G.");
            }
            else
            {
                // ⚠️ Bez reference nelze normovat. Radeji stat a rict to, nez merit proti dohadu.
                Trace.WriteLine("MagCal: registr 21 se nepodarilo precist -> mise NEZACINA. "
                                + "Bez referencniho |B| by prolozeni normovalo proti dohadu.");
                return;
            }

            var reg23 = control.ReadRegister(VnCommands.RegMagCompensation);
            Reg23Before = reg23 == null
                ? string.Empty
                : string.Join(",", Array.ConvertAll(reg23,
                    v => v.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)));

            if (!control.SetOnboardHsi(true))
                Trace.WriteLine("MagCal: registr 44 se nepodarilo zapnout - "
                                + "nezavisla kontrola z registru 47 nebude k dispozici.");

            collector = new MagCalCollector(BRefG, fitPeriod) { Reg23Before = Reg23Before };
            Phase = MagCalPhase.Collecting;
            Trace.WriteLine("MagCal: sber zapnut. Robot STOJI - otacej s nim rukou "
                            + "podle pokynu na strance.");
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (collector == null) return;
            if (!(msg is IMUState imu)) return;

            if (!collector.Add(imu)) return;

            if (firstSampleAt == default) firstSampleAt = imu.TimeStamp;
            lastSampleAt = imu.TimeStamp;

            if (Phase == MagCalPhase.Collecting && collector.Usable) Phase = MagCalPhase.Ready;
            else if (Phase == MagCalPhase.Ready && !collector.Usable) Phase = MagCalPhase.Collecting;

            var zprava = collector.ToLogMessage();
            zprava.Phase = (int)Phase;
            EmitDerived(zprava);
        }

        /// <summary>
        /// <b>Zapis kalibrace do senzoru</b> — jen na pokyn cloveka (tlacitko na strance pod
        /// drzenym nouzovym zastavenim). Vraci <c>false</c>, kdyz zapsat nelze, a <b>rika proc</b>
        /// do <see cref="Trace"/>.
        ///
        /// <para>⚠️ Gate na pokryti a verdikt je tady, <b>ne jen v UI</b>: skryte tlacitko neni
        /// pojistka.</para>
        /// </summary>
        public bool WriteToSensor()
        {
            if (collector == null)
            {
                Trace.WriteLine("MagCal: zapis odmitnut - mise nezacala.");
                return false;
            }
            if (!collector.Usable)
            {
                Trace.WriteLine("MagCal: zapis odmitnut - " + collector.Verdict);
                return false;
            }

            string cisla = collector.LastResult.ToVnwrg23();
            if (!control.WriteMagCompensation(cisla))
            {
                Trace.WriteLine("MagCal: zapis registru 23 SELHAL - do flash se neuklada.");
                return false;
            }
            if (!control.SaveToFlash())
            {
                Trace.WriteLine("MagCal: registr 23 zapsan, ale ULOZENI DO FLASH SELHALO - "
                                + "po vypnuti senzoru bude kalibrace pryc.");
                return false;
            }

            // Palubni HSI uz nema co delat - vypnout, at nebezi zbytecne.
            control.SetOnboardHsi(false);

            Phase = MagCalPhase.Written;
            Trace.WriteLine("MagCal: kalibrace zapsana a ulozena: " + cisla);
            Trace.WriteLine("MagCal: ⚠️ trvalost overi JEN vypnuti a zapnuti robota.");
            return true;
        }
    }
}
```

⚠️ **`MagCalMission` je v `ARBot.Common`, ale používá `VnCommands` z `ARBot.HAL`** — a směr
závislostí je `Common ← HAL`, takže to **nepřeloží**. Léčba: čísla registrů, která mise potřebuje
(21 a 23), dej jako konstanty do `IMagCalControl` v `MissionSeams.cs`
(`IMagCalControl.RegReference = 21`, `RegCompensation = 23`) a `using ARBot.HAL…` z mise odstraň.
`VnCommands` pak zůstane jen v `VnMagCalControl` na straně HAL, kam patří.

- [x] **Krok 7: `VnMagCalControl`** — obal bez logiky, jen mapování na driver z Tasku 5.

```csharp
using System;
using System.Diagnostics;
using ARBot.Common.Missions;

namespace ARBot.HAL.Devices.AHRS
{
    /// <summary>
    /// <see cref="IMagCalControl"/> nad binarnim driverem VN100. <b>Zadna logika</b> — jen
    /// mapovani sevu na prikazy a registry, aby rozhodovani zustalo v misi a znalost protokolu
    /// v HAL. Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public sealed class VnMagCalControl : IMagCalControl
    {
        private readonly VN100IMUBinary imu;

        public VnMagCalControl(VN100IMUBinary imu)
            => this.imu = imu ?? throw new ArgumentNullException(nameof(imu));

        public double[] ReadRegister(int reg) => imu.ReadRegister(reg);

        public bool WriteMagCompensation(string dvanactCisel)
            => imu.WriteRegister(VnCommands.MagnetometerCompensation(dvanactCisel),
                                 VnCommands.RegMagCompensation);

        public bool SetOnboardHsi(bool run)
            => imu.WriteRegister(VnCommands.MagCalControl(run), VnCommands.RegMagCalControl);

        public bool SaveToFlash()
        {
            // VNWNV nema co zpetne cist (uklada RAM do flash), takze se jen posle a ceka.
            // ⚠️ Uspech tedy NENI overeny - skutecny test je az vypnuti a zapnuti robota.
            imu.SendCommand(VnCommands.SaveToFlash(), TimeSpan.FromSeconds(3));
            Trace.WriteLine("VN100: VNWNV posláno. Trvalost overi jen vypnuti a zapnuti.");
            return true;
        }
    }
}
```

`SendCommand(telo, cekani)` doplň do `VN100IMUBinary` vedle `ReadRegister`/`WriteRegister`
z Tasku 5: orámuje, pošle a počká (ukládání do flash trvá ~1 s a senzor přitom neodpovídá).

- [x] **Krok 8: Spustit testy**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter MagCal
dotnet build Src/ARBot.slnx -p:Platform=x64
```

Očekávané: 6 testů mise + 5 kolektoru + 5 fitu + 4 pokrytí PASS.

- [ ] **Krok 9: Commit** (jen na pokyn autora)

```bash
git add Src/ARBot.Common/Missions Src/ARBot.HAL/Devices/AHRS/VnMagCalControl.cs Src/ARBot.Common.Tests/Missions
git commit -m "Mise magcal: automat, sev IMagCalControl a zapis kalibrace na pokyn"
```

---

### Task 8: Napojení na runtime a selektor `mission=magcal`

**Files:**
- Modify: `Src/ARBot.Common/Configuration/ParamRegistry.cs:180` (výčet `mission`)
- Modify: `Src/ARBot.Runtime/Robot/ARBotRuntime.cs` (switch misí, vzor `case "freerun"` na 661;
  vlastnost `FreeRunMission` na ~686; `processing` = `RelaySource` na 433)

**Interfaces:**
- Consumes: `MagCalMission`, `VnMagCalControl` (Task 7), `processing` (fan-out primárních zpráv),
  `stream`, `ARBotHW.Current.IMU`
- Produces: `ARBotRuntime.MagCalMission` (vlastnost pro web a UI)

- [x] **Krok 1: Rozšířit výčet parametru**

⚠️ Komentář nad ním říká *„Vycet MUSI odpovidat switchi v ARBotRuntime — kdyz pribude mise,
patri i sem."* Plní se **obojí v jednom tasku**, jinak `mission=magcal` skončí chybou při startu.

```csharp
        public static readonly StringParam Mission = Vycet("mission", "none",
              new[] { "none", "freerun", "robotour", "magcal" }, K_MISE,
              "Vyber mise: none | freerun | robotour | magcal. Mise se vylucuji, proto selektor "
              + "a ne booleovske prepinace - dve zaroven by si prepisovaly mrkev. magcal NEJEZDI: "
              + "robot stoji a meri si kalibraci magnetometru, kdyz s nim clovek otaci rukou "
              + "(doc/plan-vn100-kalibrace.md).");
```

- [x] **Krok 2: `case "magcal"` do switche**

Připojuje se na **`processing`** (fan-out primárních zpráv), ne na `loop.Output` — mise
potřebuje `IMUState`, ne snímky kamer.

```csharp
                case "magcal":
                    // Kalibraci umi jen binarni driver: cteni a zapis registru je v nem.
                    var vn = ARBotHW.Current.IMU as ARBot.HAL.Devices.AHRS.VN100IMUBinary;
                    if (vn == null)
                    {
                        Trace.WriteLine("mission=magcal, ale neni binarni VN100 -> mise se nezaklada. "
                                        + "V simulaci a s jinym IMU to nema co merit.");
                        break;
                    }

                    var magcal = new ARBot.Common.Missions.MagCalMission(
                        new ARBot.HAL.Devices.AHRS.VnMagCalControl(vn), regulatorHolder);

                    MagCalMission = magcal;
                    stages.Add(magcal);
                    connections.Add(processing.Connect(magcal));
                    connections.Add(magcal.Output.Connect(stream));
                    magcal.StartMission();
                    Trace.WriteLine("mission=magcal: robot STOJI. Otacej s nim rukou podle pokynu "
                                    + "na strance; zapis kalibrace je tlacitko pod drzenym stopem.");
                    break;
```

⚠️ **`regulatorHolder` dohledej, nevymýšlej.** Najdi, co se v témž switchi předává jako
`IRegulatorHolder` misi Robotour (`grep -n "IRegulatorHolder" Src/ARBot.Runtime/Robot/ARBotRuntime.cs`)
a předej **týž objekt**. Když Robotour šev nedostává, je to `ControlLoop` — ověř, že
`ControlLoop` to rozhraní implementuje, a teprve pak ho předej.

- [x] **Krok 3: Vlastnost `MagCalMission`**

Vedle existující `FreeRunMission` (~686), aby na ni dosáhl web i UI.

```csharp
        /// <summary>
        /// Ziva mise magcal pro prikazy z webu; <c>null</c> pri jine misi a ve View.
        /// Viz doc/plan-vn100-kalibrace.md.
        /// </summary>
        public ARBot.Common.Missions.MagCalMission MagCalMission { get; private set; }
```

- [x] **Krok 4: Build a testy runtime**

```bash
dotnet build Src/ARBot.slnx -p:Platform=x64
dotnet test Src/ARBot.Runtime.Tests/ARBot.Runtime.Tests.csproj -p:Platform=x64
```

- [x] **Krok 5: Ověřit, že neznámá mise je pořád chyba při startu**

```bash
dotnet run --project Src/ARBot.Headless -p:Platform=x64 -- mission=neexistuje
```

Očekávané: návratový kód **2** (vadná konfigurace). Zkontroluj i to, že `mission=magcal`
projde validací:

```bash
dotnet run --project Src/ARBot.Headless -p:Platform=x64 -- mission=magcal web=0
```

Očekávané: nastartuje, v logu je hláška „není binární VN100" (na Windows je to správně)
a proces jde ukončit Ctrl+C.

- [ ] **Krok 6: Commit** (jen na pokyn autora)

```bash
git add Src/ARBot.Common/Configuration/ParamRegistry.cs Src/ARBot.Runtime/Robot/ARBotRuntime.cs
git commit -m "Napojeni mise magcal na runtime a selektor mission="
```

---

### Task 9: Blok na webové stránce a tlačítko zápisu

**Files:**
- Modify: `Src/ARBot.Runtime/Web/WebStatus.cs` (`Post` na 143, `ToJson` na 304, `ToHtml` na 618;
  vzor gate `MissionBlockedReason()` na 115)
- Modify: `Src/ARBot.Runtime/Web/WebPreviewServer.cs` (koncový bod)
- Test: `Src/ARBot.Runtime.Tests/Web/WebStatusMagCalTests.cs`

**Interfaces:**
- Consumes: `MagCalMsg` (Task 6), `MagCalPhase` (Task 7), `WebStatus.MissionBlockedReason()`
- Produces: `WebStatus.MagCal` (poslední `MagCalMsg`), `WebStatus.MagCalWriteBlockedReason()`,
  koncový bod `POST /magcal/write`

- [x] **Krok 1: Napsat padající testy gate**

Gate má **dvě** podmínky a každá je vlastní test — jinak by projedná zamaskovala druhou.

```csharp
using ARBot.Common.Logs;
using ARBot.Common.Missions;
using ARBot.Runtime.Web;
using NUnit.Framework;

namespace ARBot.Runtime.Tests.Web
{
    /// <summary>
    /// Gate zapisu kalibrace: jen pri HOTOVEM pokryti a DRZENEM nouzovem zastaveni.
    /// Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public class WebStatusMagCalTests
    {
        private static WebStatus Stav(bool stopDrzeny, bool pouzitelne)
        {
            var s = new WebStatus();
            s.Post(new MotorStateBase { EmergencyStop = stopDrzeny });   // podle skutecneho typu
            s.Post(new MagCalMsg
            {
                Phase = (int)(pouzitelne ? MagCalPhase.Ready : MagCalPhase.Collecting),
                Verdict = pouzitelne ? "HOTOVO" : "POKRACUJ: chybi naklon",
            });
            return s;
        }

        [Test]
        public void BezDrzenehoStopu_JeZapisZablokovany()
        {
            Assert.That(Stav(stopDrzeny: false, pouzitelne: true).MagCalWriteBlockedReason(),
                Is.Not.Empty, "zapis do senzoru vyzaduje cloveka u robota");
        }

        [Test]
        public void BezHotovehoPokryti_JeZapisZablokovany()
        {
            Assert.That(Stav(stopDrzeny: true, pouzitelne: false).MagCalWriteBlockedReason(),
                Is.Not.Empty, "nehotova kalibrace se zapsat nesmi");
        }

        [Test]
        public void BezMise_JeZapisZablokovany()
        {
            Assert.That(new WebStatus().MagCalWriteBlockedReason(), Is.Not.Empty);
        }

        [Test]
        public void SDrzenymStopemAHotovymPokrytim_Projde()
        {
            Assert.That(Stav(stopDrzeny: true, pouzitelne: true).MagCalWriteBlockedReason(),
                Is.Empty);
        }
    }
}
```

⚠️ `MotorStateBase`/`EmergencyStop` uprav podle toho, jak stav stopu do `WebStatus` skutečně
teče — najdi to v `MissionBlockedReason()` a použij **týž** zdroj, ne druhý.

- [x] **Krok 2: Spustit, ověřit, že padá**

```bash
dotnet test Src/ARBot.Runtime.Tests/ARBot.Runtime.Tests.csproj -p:Platform=x64 --filter MagCal
```

- [x] **Krok 3: `MagCalMsg` do `WebStatus.Post` a gate**

```csharp
        /// <summary>Posledni stav kalibrace magnetometru; <c>null</c> pri jine misi.</summary>
        public MagCalMsg MagCal { get; private set; }
```

V `Post` přidej k ostatním typům `case MagCalMsg m: MagCal = m; break;` (podle stávajícího
tvaru toho switche).

```csharp
        /// <summary>
        /// <b>Proc nelze zapsat kalibraci</b>; prazdny retezec = lze.
        ///
        /// <para>Dve podminky: <b>hotove pokryti</b> (jinak by se zapsala nehotova kalibrace)
        /// a <b>drzene nouzove zastaveni</b> (jinak by se do senzoru zapisovalo, kdyz robot muze
        /// jet). Tataz zasada a tyz mechanismus jako <see cref="MissionBlockedReason"/>.</para>
        ///
        /// <para>⚠️ Gate je <b>na serveru</b>, ne jen skrytim tlacitka — skryte tlacitko neni
        /// pojistka.</para>
        /// </summary>
        public string MagCalWriteBlockedReason()
        {
            if (MagCal == null) return "Mise magcal nebezi.";
            if (MagCal.Phase != (int)MagCalPhase.Ready)
                return "Kalibrace jeste neni hotova: " + (MagCal.Verdict ?? string.Empty);
            // Tyz zdroj stavu stopu jako MissionBlockedReason - ne druhy.
            string stop = MissionBlockedReason();
            if (stop.Length > 0) return stop;
            return string.Empty;
        }
```

- [x] **Krok 4: Spustit testy, musí projít**

```bash
dotnet test Src/ARBot.Runtime.Tests/ARBot.Runtime.Tests.csproj -p:Platform=x64 --filter MagCal
```

- [x] **Krok 5: Blok do `ToJson` a `ToHtml`**

Pořadí je záměrné: **nejdřív co udělat dál**, pak čísla. Obsluha stojí u robota a potřebuje
pokyn, ne diagnózu.

```
Kalibrace magnetometru
  POKRACUJ: chybi azimuty 135-180°; chybi naklon (mam 0 z 2, podloz robota aspon o 15 stupnu)
  azimuty 20/24    naklony 1 (odklonene 0)    vzorku 4210
  podminenost 84,3 (prah 30)   sd|B| 0,0121 G   sd sklonu 1,84°
  [Zapsat do senzoru]     <- aktivni jen kdyz MagCalWriteBlockedReason() je prazdny
```

Když je gate zablokovaný, **ukaž důvod u tlačítka** — „tlačítko je šedé a nikdo neví proč" je
přesně to, co člověka v poli zastaví.

- [x] **Krok 6: Koncový bod `POST /magcal/write`**

Vzor je koncový bod volby mise ve `WebPreviewServer`. Chování:

- `MagCalWriteBlockedReason()` neprázdný → **409** a důvod v těle (ne 200 s tichým nic);
- jinak `ARBotRuntime.Current.MagCalMission?.WriteToSensor()`;
- `false` → **500** a text „zápis selhal, viz log";
- `true` → 200 a zpětné čtení registru 23 v odpovědi.

- [x] **Krok 7: Build a celá sada testů**

```bash
dotnet build Src/ARBot.slnx -p:Platform=x64
dotnet test Src/ARBot.Runtime.Tests/ARBot.Runtime.Tests.csproj -p:Platform=x64
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64
```

- [x] **Krok 8: Proklikat v simulaci, co jde**

```bash
dotnet run --project Src/ARBot.Headless -p:Platform=x64 -- mission=magcal web=8080 webopen=true
```

Bez skutečného VN100 se mise nezaloží (Task 8, krok 2), takže blok bude prázdný a tlačítko
zablokované s důvodem „Mise magcal nebeži" — **to je správné chování**, ne chyba. Celý blok
se proklikává až na zařízení (Task 10).

- [ ] **Krok 9: Commit** (jen na pokyn autora)

```bash
git add Src/ARBot.Runtime/Web Src/ARBot.Runtime.Tests/Web
git commit -m "Blok kalibrace magnetometru na strance a tlacitko zapisu pod drzenym stopem"
```

---

### Task 10: Terénní měření a přeměření (na zařízení)

Převážně ruční task. Runbook a akceptační kritéria jsou ve specifikaci
([plan-vn100-kalibrace.md](plan-vn100-kalibrace.md) → „Runbook v terénu", „Akceptační kritéria").

- [x] **Krok 1: Doplněno 8. 9. 2026** — `deploy/vnprobe.sh` čte i registry **37 a 38** (VPE mag advanced tuning),
      ať je fáze 2 podložená daty — dnes čte 36 a 44. Je to jedna položka ve smyčce `for r in …`.
      Skript zůstává **read-only** (`VNRRG`); zápis do něj nepatří.

```bash
for r in 01 06 07 08 21 23 25 26 27 35 36 37 38 44 47 54 83; do send "VNRRG,$r"; done
```

- [ ] **Krok 2: Nasadit**

```bash
pwsh deploy/nasad.ps1
```

- [ ] **Krok 3: Stav „před"** — `sudo systemctl stop arbot; ./vnprobe.sh /dev/ttyUSB0` a opsat
      registry 21, 23, 26, 35, 36, 37, 38, 44, 83 do [imu-and-frames.md](imu-and-frames.md).
      Pak službu spustit.
- [ ] **Krok 4: Odjet runbook** — na stránce `mission=magcal` pod drženým stopem, uvolnit,
      otáčet robotem rukou podle pokynů (2–3 obraty na rovině → podložit stranu 20–30° → obrat →
      druhá strana), při `HOTOVO` stisknout stop a ťuknout „Zapsat do senzoru".
- [ ] **Krok 5: Čekat ~2 minuty**, než se kurz srovná (`Absolute` se na pole dotahuje 100–170 s).
- [ ] **Krok 6: Projet smyčku** venku a pustit rozbory:

```bash
dotnet run --project Src/ARBot.Analyze -p:Platform=x64 -- magcal records/<novy>.rec
dotnet run --project Src/ARBot.Analyze -p:Platform=x64 -- vn100 records/<novy>.rec
dotnet run --project Src/ARBot.Analyze -p:Platform=x64 -- heading records/<novy>.rec
```

- [ ] **Krok 6b: Zkontrolovat v logu, že se nastavil model pole** (`MagModel: registr 83 nastaven`).
      Když tam místo toho je „cekam na kvalitni fix", kalibrace se normovala na **starou**
      referenci z registru 21 a je potřeba ji přeměřit. `IMU yaw − GPS kurz` se má proti
      7. 9. zlepšit **přesně o deklinaci** (~+5°) — jiné číslo znamená, že se dvě chyby smíchaly.
- [ ] **Krok 7: Porovnat s akceptačními kritérii** a zapsat **naměřené hodnoty** (ne „zlepšilo
      se") do [imu-and-frames.md](imu-and-frames.md) a [devlog.md](devlog.md). Když kritéria
      nejsou splněná, **napiš to tak** — polovičatý výsledek zapsaný jako úspěch je horší než
      žádný.
- [ ] **Krok 8: Vypnout a zapnout robota** a přeměřit. **Jediný skutečný test, že kalibrace
      přežila flash** — zpětné čtení registru čte z RAM, ne z flash.
- [ ] **Krok 9: Přeměřit `K`** (blok 3 `vn100`) a rozhodnout o fázi 2:
      `K` vyskočilo → fáze 2 se **ruší** a zapíše se to jako zamítnutá vada i s číslem;
      `K` nevyskočilo → pokračuje se kandidátem (a), registrem 83.
- [ ] **Krok 10: Nastavit prahy naostro** v `MagCalThresholds` podle naměřeného a **odstranit
      z komentáře varování „jsou to odhady"** — od té chvíle jsou to změřená čísla.
- [ ] **Krok 11: Doplnit odkazy** — [CLAUDE.md](../CLAUDE.md) (u doménového bulletu
      `imu-and-frames.md`), [decisions.md](decisions.md) (rozhodnutí 1 a 2 ze specifikace:
      proč mise a ne parametr, proč se ohnula čára o ručním zápisu do senzoru).
- [ ] **Krok 12: DevLog** — záznam dne podle pravidel v hlavičce [devlog.md](devlog.md).


---

## Fáze 1b — po prvním výjezdu (10. 9. 2026)

První pokus v poli skončil bez výsledku (podrobně
[plan-vn100-kalibrace.md](plan-vn100-kalibrace.md#fáze-1b--co-našel-první-výjezd-10-9-2026)).
Tohle je hotové **v kódu**; na skutečném senzoru neběželo nic z toho.

- [x] **Proložení koule** (`MagCalFit.TryFitSphere`) — jen tvrdé železo, 4 neznámé. Tři brány:
      podmíněnost, zbytek, a **měřítko** (`MaxSphereScale`) — bez té třetí projde rovinná rotace
      a vrátí bias vedle o 476 787 G.
- [x] **Verdikt rozlišuje „chybí náklon" od „měnilo se pole"** a každá větev končí pokynem.
- [x] **Zápis jen tvrdého železa** — `MagCalMission.WriteHardIronOnly`, `POST /magcal/writehardiron`,
      druhé tlačítko na stránce. Bezpečnostní brána (držený stop) je **sdílená** s plným zápisem.
- [x] **Mřížka pokrytí 24 × 5** místo půdorysu, s modrým rámečkem na aktuální buňce.
- [x] **Velikost odklonu pryč z klíče skupin** + posunuté sektory + pokyn pojmenuje stranu.
- [x] **`MagCalMsg` verze 2** (mřížka, aktuální buňka, čísla z koule); verze 1 se čte dál.
- [x] **`ARBot.Analyze magcal`** má blok „2) PROLOZENI KOULE" před elipsoidou.

### Co zbývá

- [ ] **Pustit `ARBot.Analyze magcal` na záznam z výjezdu 10. 9.**, až bude z robota k dispozici,
      a ověřit, že verdikt spadl opravdu na nekladném vlastním čísle (dnes je to odvozeno
      z kódu a z podmíněnosti ~80, ne změřeno).
- [ ] **Zvážit vázané proložení elipsoidy** (Li–Griffiths), které z principu nemůže vyjít jako
      hyperboloid — tím by stav „proložení selhalo přes dobré pokrytí" zmizel úplně. Rozhodnout
      **až podle toho záznamu**; dokud nevíme, že to je náš případ, je to práce naslepo.
- [ ] **Terénní měření celé fáze 1b** — hlavně že mřížka obsluhu opravdu navede a že částečný
      zápis do flash projde.

### Z dokumentace VN (10. 9. 2026)

- [x] **`Reset` registru 44 před `Run`** (TN002 kap. 4.1, krok 1) — bez něj nese registr 47
      řešení z minulé mise a není to nezávislá kontrola.
- [x] **Vypnout registr 44 i při NEDOKONČENÉ misi** (TN002 kap. 5.2: Mode = Run je uvedený mezi
      příčinami ujíždějícího kurzu).
- [ ] **Přeměřit `K` po kalibraci** — tvrzení „VPE 206 s je samostatná vada, kterou kalibrace
      neopraví" je **nepodložené**, viz [imu-and-frames.md](imu-and-frames.md). Když `K`
      zůstane, zkusit `$VNWRG,35,1,0,0,0` (Absolute + Unfiltered + Static) a přeměřit znovu —
      tím se obě adaptivní vrstvy vyloučí naráz.
- [ ] **Rozhodnout o 2D kalibraci** (TN002 kap. 3.2) — z pouhé rotace na rovině, ale platí jen
      do 5–10° náklonu. Náš 3D fit to neumí; byla by to samostatná úloha.
