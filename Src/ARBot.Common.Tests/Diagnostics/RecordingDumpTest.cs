using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Tests.Runtime;   // TestHelpers, DelegateTarget
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// NASTROJ (ne test): vypise textovy log ze zaznamu behu. Diky
    /// <see cref="TraceInfoBridge"/> je debugovaci vystup soucasti nahravky, takze se da
    /// zpetne precist i beh, u ktereho nikdo nesedÄ›l u okna Debug output - vcetne behu
    /// na zarizeni. Viz doc/record-replay.md.
    ///
    /// <para><b>Pouziti:</b> cesta k zaznamu se bere z promenne prostredi <c>ARBOT_RECORD</c>:</para>
    /// <code>
    /// $env:ARBOT_RECORD = "D:\Work\ARBot3\logs\beh.rec"
    /// dotnet test Src\ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~RecordingDumpTest"
    /// </code>
    /// <para>Vystup jde do konzole testu a soubeznemu souboru <c>&lt;zaznam&gt;.log.txt</c>.
    /// Volitelne filtry: <c>ARBOT_RECORD_AREA</c> (podretezec oblasti, napr. <c>App</c> pro vlastni
    /// hlasky bez Avalonie) a <c>ARBOT_RECORD_GREP</c> (podretezec textu).</para>
    ///
    /// <para>Nezname typy zprav (CameraFrame a dalsi z HAL/app) <c>MessageReader</c> preskoci,
    /// takze staci <see cref="MessageCatalog.CommonDefaults"/> a test nepotrebuje referenci na HAL.</para>
    /// </summary>
    [Explicit("Nastroj pro cteni zaznamu - cesta se predava pres ARBOT_RECORD.")]
    public class RecordingDumpTest
    {
        [Test]
        public void VypisLogZeZaznamu()
        {
            string path = Environment.GetEnvironmentVariable("ARBOT_RECORD");
            Assert.That(path, Is.Not.Null.And.Not.Empty,
                        "nastav ARBOT_RECORD na cestu k zaznamu");
            Assert.That(File.Exists(path), Is.True, $"zaznam neexistuje: {path}");

            string areaFilter = Environment.GetEnvironmentVariable("ARBOT_RECORD_AREA");
            string grep = Environment.GetEnvironmentVariable("ARBOT_RECORD_GREP");

            var lines = new List<string>();
            int total = 0, shown = 0;

            var sink = new DelegateTarget(m =>
            {
                if (!(m is Info info)) return;
                total++;

                if (!string.IsNullOrEmpty(areaFilter) &&
                    (info.Area == null || info.Area.IndexOf(areaFilter, StringComparison.OrdinalIgnoreCase) < 0))
                    return;
                if (!string.IsNullOrEmpty(grep) &&
                    (info.Message == null || info.Message.IndexOf(grep, StringComparison.OrdinalIgnoreCase) < 0))
                    return;

                shown++;
                // Cas je od verze 2; u starsich zaznamu zustane default - vypise se pomlckou.
                string t = info.TimeStamp == default ? "        -    " : info.TimeStamp.ToString("HH:mm:ss.fff");
                lines.Add($"{t}  {info.Level,-9} {info.Area,-22} {info.Message}");
            });

            sink.Start();
            using (var fs = File.OpenRead(path))
            {
                var src = new FileMessageSource(fs, TestHelpers.Enc, MessageCatalog.CommonDefaults());
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            string outPath = path + ".log.txt";
            File.WriteAllLines(outPath, lines);

            TestContext.Out.WriteLine($"zaznam: {path}");
            TestContext.Out.WriteLine($"Info zprav: {total}, po filtru: {shown}");
            TestContext.Out.WriteLine($"vypsano do: {outPath}");
            TestContext.Out.WriteLine(new string('-', 100));
            foreach (var l in lines.Take(500)) TestContext.Out.WriteLine(l);
            if (lines.Count > 500)
                TestContext.Out.WriteLine($"... a dalsich {lines.Count - 500} radku (cele v {outPath})");

            Assert.That(total, Is.GreaterThan(0),
                        "v zaznamu nejsou zadne Info zpravy - bezel zaznam s napojenym TraceInfoBridge?");
        }
    }
}

