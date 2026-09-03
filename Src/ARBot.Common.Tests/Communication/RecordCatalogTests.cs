using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Tests.Communication
{
    /// <summary>
    /// Hlídá katalog, kterým se čte <b>záznam</b> (<see cref="MessageCatalog.RecordDefaults"/>).
    ///
    /// <para><b>Nač to je:</b> když v katalogu typ chybí, index ho v záznamu ukazuje, ale
    /// <c>Read</c> vrátí <c>null</c> — takže se záznam tváří, jako by ten senzor <b>vůbec
    /// neexistoval</b>. Je to zákeřné, protože to nevypadá na chybu nástroje, ale na chybějící
    /// data. Stalo se to <b>dvakrát</b>: u <c>GPSState</c> (25. 8. 2026, stálo hodinu) a u
    /// <c>MotorStateBase</c> (2. 9. 2026, kdy `ARBot.Analyze` hlásil 8381 motorových zpráv
    /// v indexu a nulu přečtených — a málem z toho vznikl závěr „motor nedodával měření").</para>
    /// </summary>
    public class RecordCatalogTests
    {
        /// <summary>
        /// Zprávy, které v katalogu záznamu <b>schválně nejsou</b>. Každá s důvodem — bez něj
        /// by se sem dalo přidat cokoli a test by přestal chránit.
        /// </summary>
        private static readonly Dictionary<string, string> Vyjimky = new()
        {
            // sem patří jen typy s doloženým důvodem, proč se ze záznamu nečtou
        };

        private static Dictionary<string, Message> Katalog()
            => MessageCatalog.RecordDefaults().ToPrototypeMap();

        [Test]
        public void RecordDefaults_ZnaStavyZarizeni()
        {
            var k = Katalog();
            Assert.Multiple(() =>
            {
                Assert.That(k.ContainsKey(new GPSState().MsgName), Is.True, "GPSState");
                Assert.That(k.ContainsKey(new MotorStateBase().MsgName), Is.True, "MotorStateBase");
                Assert.That(k.ContainsKey(new CameraFrame().MsgName), Is.True, "CameraFrame");
                Assert.That(k.ContainsKey(new IMUState().MsgName), Is.True, "IMUState");
            });
        }

        /// <summary>
        /// Silná verze: <b>každá</b> zpráva z <c>ARBot.Common</c>, kterou lze vyrobit bez
        /// parametrů, musí být v katalogu záznamu. Tenhle test by chytil oba dosavadní případy
        /// sám, bez toho aby si někdo vzpomněl doplnit jméno výš.
        /// </summary>
        [Test]
        public void RecordDefaults_ZnaVsechnyZpravyZCommon()
        {
            var k = Katalog();

            var typy = typeof(Message).Assembly.GetTypes()
                .Where(t => typeof(Message).IsAssignableFrom(t))
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition)
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.Name)
                .ToList();

            Assert.That(typy, Is.Not.Empty, "Reflexe nenasla zadne zpravy - test by nic nehlidal.");

            var chybi = new List<string>();
            foreach (var t in typy)
            {
                var prototyp = (Message)Activator.CreateInstance(t);
                string jmeno = prototyp.MsgName;
                if (string.IsNullOrEmpty(jmeno)) continue;          // bez jmena se serializovat neda
                if (Vyjimky.ContainsKey(t.Name)) continue;
                if (!k.ContainsKey(jmeno))
                    chybi.Add($"{t.Name} (MsgName '{jmeno}')");
            }

            Assert.That(chybi, Is.Empty,
                "Tyhle zpravy katalog zaznamu nezna, takze by se ze zaznamu NEPRECETLY a tvarilo "
                + "by se to jako chybejici data: " + string.Join(", ", chybi)
                + ". Bud je doregistruj do MessageCatalog.RecordDefaults, nebo je zapis do "
                + "seznamu Vyjimky s duvodem.");
        }
    }
}
