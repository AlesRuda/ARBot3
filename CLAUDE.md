# ARBot3 — rozcestník a pravidla projektu

Autonomní mobilní robot. .NET 10, C#. Aplikace `ARBot` (Avalonia UI + Dock), sdílená
knihovna `ARBot.Common` (modely, fúze, algoritmy), hardwarová vrstva `ARBot.HAL`
(+ platformové `ARBot.HALWindows` / `ARBot.HALArmbian`). Zdroj je v `Src/`.

Tento soubor je rozcestník; **detailní doménová dokumentace je v `doc/`** a u konkrétních
komponent (viz odkazy níže). Při práci na dané oblasti si přečti příslušný dokument.

## Pravidla / konvence (dodržovat)

- **Vše v repozitáři** — všechny poznatky, poznámky a dokumentace musí být uloženy v rámci
  projektu (`doc/`, README, komentáře v kódu). **Žádné ukládání mimo repozitář** (žádná externí
  ani soukromá úložiště mimo git).
- **Jazyk: čeština** — komunikace, komentáře v kódu i dokumentace jsou česky.
- **Build vždy pro konkrétní platformu — NE `AnyCPU`.** Windows/vývoj/testy = `x64`,
  cílové zařízení (Armbian/ARM64) = `OrangePI`. Např.
  `dotnet test <proj> -p:Platform=x64`. Podrobnosti: [doc/build-and-platforms.md](doc/build-and-platforms.md).
- **Při migracích/přepisech nemazat starou ani zakomentovanou implementaci, dokud
  novou nepotvrdí unit testy.**
- **Souřadnicové konvence:** world **ENU** + matematická orientace (0 = východ, +CCW),
  body **FLU** (X vpřed, Y vlevo, Z nahoru). Viz [doc/imu-and-frames.md](doc/imu-and-frames.md).
- **Ověřuj změny buildem a testy** (`dotnet build` / `dotnet test` pod `x64`); u kódu
  s dopadem na HW napiš, co je odsimulované vs. co je nutné ověřit na zařízení.

## Doménová dokumentace

- [doc/architecture.md](doc/architecture.md) — struktura projektů, směr závislostí
  (`Common ← HAL ← app`), kam patří fúze / adaptéry / řídicí smyčka.
- [doc/build-and-platforms.md](doc/build-and-platforms.md) — platformy, HAL (Windows/Armbian),
  nativní knihovna, RealSense verze, externí (ne-NuGet) reference.
- [doc/ekf-fusion.md](doc/ekf-fusion.md) — EKF senzorická fúze (`ARBot.Common/Fusion`);
  hloubkově [doc/EKF_fuze_dokumentace.docx](doc/EKF_fuze_dokumentace.docx).
- [doc/imu-and-frames.md](doc/imu-and-frames.md) — IMU, souřadnicové systémy, VN100
  (drivery, montáž, reference frame rotation).
- [doc/hardware.md](doc/hardware.md) — senzory a připojení (per-zařízení, orientační).
- [doc/record-replay.md](doc/record-replay.md) — pipeline zpráv, záznam/přehrávání běhu,
  vize (BackProject), režimy Run/View/Simulace + otevřené úkoly.
- [Src/ARBot/ARBot/Views/README.md](Src/ARBot/ARBot/Views/README.md) — dokovatelné
  dokumenty a nástroje UI (DocumentBase/ToolBase + ViewType, design-time náhled).

Když vznikne nová netriviální doménová oblast, přidej k ní `doc/*.md` a odkaz sem.
