using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Devices
{
    /// <summary>
    /// Hlídá, že <b>hlášení výjimky</b> na cestách poruch jde do <c>Trace</c>, ne do <c>Debug</c>.
    ///
    /// <para><b>Proč vedle <see cref="DiagnostikaSenzoruTests"/>.</b> Ten kryje jen ovladače senzorů
    /// a jen vzorek <c>Debug.WriteLine($"{Name}: …</c>. Audit 15. 9. 2026 ukázal, že pravidlo
    /// z CLAUDE.md („diagnostika poruch jde do <c>Trace</c>") neplatilo právě na těch nejdražších
    /// místech, která ten test nevidí: výjimka v <c>Consume</c> <b>kteréhokoli</b> stupně
    /// (<c>MessageTarget</c>), selhání celého cyklu lokální navigace, hláška
    /// „NOUZOVE ZASTAVENI - kolize" a jediné místo, kde se hlásí, že nejde otevřít UART.
    /// V Release buildu — a právě ten běží na zařízení — po nich nezůstala <b>žádná</b> stopa.</para>
    ///
    /// <para><b>Co se hlídá:</b> <c>Debug.WriteLine</c>, které vypisuje <b>výjimku</b>. Vývojářské
    /// dumpy, které výjimku nenesou (výpis statistik occupancy gridu, intrinsiky kamery), zůstávají
    /// v <c>Debug</c> schválně — CLAUDE.md je výslovně povoluje.</para>
    ///
    /// <para>⚠️ <b>Seznam souborů je vědomě výčet, ne celý strom.</b> Zbylých ~25 míst v repu
    /// (hlavně UI projekt <c>ARBot</c>, kde vývojář panel <i>Debug output</i> vidí) se zatím
    /// nepřevádělo; rozšiřovat ten výčet je správný směr.</para>
    /// </summary>
    public class DiagnostikaPoruchTests
    {
        /// <summary>Soubory na cestách poruch, které už převedené jsou a nesmí se vrátit.</summary>
        private static readonly string[] Soubory =
        {
            Path.Combine("Src", "ARBot.Common", "Runtime", "ControlLoop.cs"),
            Path.Combine("Src", "ARBot.Common", "Communication", "MessageTarget.cs"),
            Path.Combine("Src", "ARBot.Common", "Occupancy", "LocalNavigator.cs"),
            Path.Combine("Src", "ARBot.Common", "Missions", "TrackMission.cs"),
            Path.Combine("Src", "ARBot.Common", "Missions", "RobotourMission.cs"),
            Path.Combine("Src", "ARBot.Common", "Missions", "FreeRunMission.cs"),
            Path.Combine("Src", "ARBot.Common", "Maps", "OsmNav", "Navigation", "GlobalNavigator.cs"),
            Path.Combine("Src", "ARBot.Common", "Vision", "Qr", "QrScanner.cs"),
            Path.Combine("Src", "ARBot.Common", "Vision", "Qr", "ZXingQrDecoder.cs"),
            Path.Combine("Src", "ARBot.HAL", "Devices", "Uart", "Uart.cs"),
            Path.Combine("Src", "ARBot.HAL", "Devices", "GPS", "uBlox", "uBloxGps.cs"),
            Path.Combine("Src", "ARBot.HAL", "Devices", "AHRS", "VN100IMUBinary.cs"),
            Path.Combine("Src", "ARBot.Runtime", "Robot", "ARBotRuntime.cs"),
        };

        /// <summary>
        /// <c>Debug.WriteLine</c>, které vypisuje výjimku — tedy hlášení poruchy, ne dump.
        /// </summary>
        private static readonly Regex VyjimkaDoDebugu = new Regex(
            @"Debug\.WriteLine\s*\(\s*[^)]*\b(ex\b|ex\.Message|ex\.ToString|\{ex[\.\}])",
            RegexOptions.Compiled);

        /// <summary>Řádek, který je celý komentář (v dokumentaci se slovo „Debug.WriteLine" cituje).</summary>
        private static bool JeKomentar(string radek)
        {
            string t = radek.TrimStart();
            return t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*");
        }

        [Test]
        public void PoruchyNaCestachRizeniJdouDoTrace_NeDoDebug()
        {
            string koren = RepoPaths.RootOrBase();
            var existujici = Soubory.Select(r => Path.Combine(koren, r)).Where(File.Exists).ToList();
            if (existujici.Count == 0)
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            // Kdyby se soubory přejmenovaly, test by prošel naprázdno - to musí být vidět.
            Assert.That(existujici, Has.Count.EqualTo(Soubory.Length),
                        "nektery hlidany soubor neexistuje - seznam je potreba opravit, ne nechat prochazet");

            var vady = new List<string>();
            foreach (string f in existujici)
            {
                string[] radky = File.ReadAllLines(f);
                for (int i = 0; i < radky.Length; i++)
                    if (!JeKomentar(radky[i]) && VyjimkaDoDebugu.IsMatch(radky[i]))
                        vady.Add($"{Path.GetFileName(f)}:{i + 1}");
            }

            Assert.That(vady, Is.Empty,
                "Hlaseni vyjimky na ceste poruchy musi jit do Trace, ne do Debug - Debug.WriteLine je "
                + "[Conditional(\"DEBUG\")], takze v Release (a ten bezi na zarizeni) po poruche "
                + "nezustane zadna stopa. Nalezeno v: " + string.Join(", ", vady));
        }
    }
}
