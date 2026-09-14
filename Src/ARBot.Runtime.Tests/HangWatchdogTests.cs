using System;
using System.Diagnostics;
using System.Threading;
using ARBot;

namespace ARBot.Runtime.Tests
{
    /// <summary>
    /// <see cref="HangWatchdog"/> — hlídač zatuhnutí. Vzniklo po 14. 9. 2026, kdy se runtime při
    /// volbě mise ze stránky zasekl uvnitř <c>Start()</c> a jediné, co po tom zbylo, byla mezera
    /// v journalu.
    ///
    /// <para><b>Co se dá testovat a co ne:</b> že hlídač vystřelí a mlčí ve správnou chvíli, ano.
    /// Vlastní <c>createdump</c> ne — je jen na Linuxu, sáhne si na celý proces a na testovacím
    /// běhu by vyrobil stomegabajtový soubor. Ta část je krytá tím, že se dump vůbec nepokouší
    /// jinde než na Linuxu, a jinak ověřením na zařízení.</para>
    /// </summary>
    [NonParallelizable]
    public class HangWatchdogTests
    {
        /// <summary>Posluchač, který si <see cref="Trace"/> uloží — hlásit se má do něj.</summary>
        private sealed class Zachytavac : TraceListener
        {
            private readonly System.Text.StringBuilder sb = new System.Text.StringBuilder();
            public override void Write(string message) { lock (sb) sb.Append(message); }
            public override void WriteLine(string message) { lock (sb) sb.AppendLine(message); }
            public string Text { get { lock (sb) return sb.ToString(); } }
        }

        private Zachytavac posluchac;

        [SetUp]
        public void Priprav()
        {
            posluchac = new Zachytavac();
            Trace.Listeners.Add(posluchac);
        }

        [TearDown]
        public void Uklid()
        {
            Trace.Listeners.Remove(posluchac);
            posluchac.Dispose();
        }

        /// <summary>
        /// Práce, která se vrátí včas, nesmí po sobě nechat ani řádek. Falešný poplach je tady
        /// dražší než mlčení: příště podle něj někdo bude hledat zatuhnutí, které nebylo.
        /// </summary>
        [Test]
        public void KdyzPraceDobehneVcas_Mlci()
        {
            using (HangWatchdog.Guard("test-rychly", 30)) { /* hotovo hned */ }

            Assert.That(posluchac.Text, Does.Not.Contain("HangWatchdog"));
        }

        /// <summary>Po vypršení limitu se to musí ozvat — a jmenovat operaci, aby šlo poznat co.</summary>
        [Test]
        public void KdyzPraceNedobehne_Hlasi()
        {
            using (HangWatchdog.Guard("test-pomaly", 0.2))
            {
                Thread.Sleep(1500);
            }

            Assert.That(posluchac.Text, Does.Contain("test-pomaly").And.Contain("ZATUHNUTI"));
        }

        /// <summary>
        /// Když operace nakonec dojede, musí to hlídač dopsat. Bez toho by v journalu zůstalo
        /// viset obvinění ze zatuhnutí u něčeho, co bylo jen pomalé.
        /// </summary>
        [Test]
        public void KdyzPraceDobehneAzPoVystrelu_ReknePozdejiDobehlo()
        {
            using (HangWatchdog.Guard("test-pozdni", 0.2))
            {
                Thread.Sleep(1500);
            }

            Assert.That(posluchac.Text, Does.Contain("nakonec dobehlo"));
        }

        /// <summary>
        /// <c>hangwatch=0</c> musí vrátit přesně dosavadní chování — token, který nic nedělá.
        /// Je to úniková cesta pro A/B a pro případ, že by sám hlídač škodil.
        /// </summary>
        [Test]
        public void NulaNeboZaporneVypneHlidac()
        {
            using (HangWatchdog.Guard("test-vypnuty", 0)) { Thread.Sleep(600); }
            using (HangWatchdog.Guard("test-zaporny", -5)) { Thread.Sleep(600); }

            Assert.That(posluchac.Text, Does.Not.Contain("HangWatchdog"));
        }

        /// <summary>Druhý Dispose (using + ruční zahození) nesmí nic zdvojit ani spadnout.</summary>
        [Test]
        public void DvojiDisposeNevadi()
        {
            var t = HangWatchdog.Guard("test-dvakrat", 30);
            t.Dispose();

            Assert.DoesNotThrow(() => t.Dispose());
        }

        /// <summary>
        /// Jméno operace jde do jména souboru, takže z něj musí zmizet závorky a mezery — jinak by
        /// se <c>hang-Start(Run)-….dmp</c> na zařízení nedalo napsat bez escapování.
        /// </summary>
        [TestCase("Start(Run)", "Start-Run")]
        [TestCase("ARBotRuntime.Start", "ARBotRuntime.Start")]
        [TestCase("a b  c", "a-b-c")]
        [TestCase("", "?")]
        public void JmenoSouboruJeBezpecne(string vstup, string cekam)
            => Assert.That(HangWatchdog.Jmeno(vstup), Is.EqualTo(cekam));

        /// <summary>Chybějící jméno nesmí skončit výjimkou v diagnostice — samostatně, protože
        /// <c>null</c> v <c>TestCase</c> neprojde analyzátorem NUnitu.</summary>
        [Test]
        public void JmenoSouboruSnesiNull()
            => Assert.That(HangWatchdog.Jmeno(null), Is.EqualTo("?"));
    }
}
