using System.Collections.Generic;
using ARBot.Common.Configuration;
using ARBot.Common.Occupancy;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Parametr <c>safedist=</c> — tvrdý minimální odstup od překážek pro lokální plánovač
    /// (<c>Profile.SafeDist</c> → <c>LocalPlannerConfig.SafeDist</c>).
    ///
    /// <para>Je to <b>bezpečnostní</b> omezení jako <c>maxspeed=</c>, takže se hlídá totéž:
    /// (a) registr odmítne nekladnou hodnotu už při načtení profilu, (b) hodnota z
    /// <c>Profile.SafeDist</c> opravdu dorazí do <see cref="LocalPlannerConfig"/>, a to při
    /// vzniku instance — proto ji <c>Program</c> nastavuje před složením runtime.
    /// Přizpůsobení <c>PrefDist</c> (když je <c>safedist</c> nad ním) žije v <c>Program</c>
    /// a testuje se tu jen předpoklad, na kterém stojí: <c>Validate()</c> to bez něj odmítne.
    /// Viz doc/configuration.md.</para>
    /// </summary>
    /// <remarks>Mění statické <c>Profile.SafeDist</c> (a zase vrací) — kdyby se testy jednou
    /// pustily paralelně, ovlivnilo by to ostatní.</remarks>
    [NonParallelizable]
    public class SafeDistParamTests
    {
        private static List<KeyValuePair<string, string>> Dvojice(string hodnota)
            => new List<KeyValuePair<string, string>> { new("safedist", hodnota) };

        [TestCase("0.3")]
        [TestCase("0.7")]
        [TestCase("1.5")]
        public void KladnaHodnotaProjde(string hodnota)
        {
            Assert.That(ParamRegistry.Validate(Dvojice(hodnota)), Is.Empty);
        }

        /// <summary>
        /// Nula ani záporná hodnota projít nesmí. Nula by znamenala „smíš se dotýkat překážek"
        /// a odstup by přestal být bezpečnostní; záporná je nesmysl.
        /// </summary>
        [TestCase("0")]
        [TestCase("-0.5")]
        public void NekladnaHodnotaJeChyba(string hodnota)
        {
            var vady = ParamRegistry.Validate(Dvojice(hodnota));
            Assert.That(vady, Is.Not.Empty, $"safedist={hodnota} mělo být odmítnuto.");
            Assert.That(string.Join("; ", vady), Does.Contain("vetsi nez 0"));
        }

        [TestCase("blizko")]
        [TestCase("0,7")]      // desetinna CARKA - registr chce tecku
        public void NecisloJeChyba(string hodnota)
        {
            Assert.That(ParamRegistry.Validate(Dvojice(hodnota)), Is.Not.Empty);
        }

        [Test]
        public void JeVRegistruJakoCislo()
        {
            Assert.That(ParamRegistry.TryGet("safedist", out var def), Is.True);
            Assert.That(def.Type, Is.EqualTo(ParamType.Double));
            Assert.That(def.DefaultFromCode, Is.True, "výchozí hodnota je z kódu (Profile).");
        }

        /// <summary>
        /// Odstup se čte z <c>Profile.SafeDist</c> při VZNIKU <see cref="LocalPlannerConfig"/>
        /// (inicializátorem pole). Instance vzniklá po změně vidí novou hodnotu, dřív vzniklá drží
        /// starou — proto musí <c>Program</c> parametr přenést před složením runtime.
        /// </summary>
        [Test]
        public void LocalPlannerCteOdstupAzPriVznikuInstance()
        {
            double puvodni = Profile.SafeDist;
            try
            {
                var pred = new LocalPlannerConfig();

                Profile.SafeDist = 0.3;
                var po = new LocalPlannerConfig();

                Assert.That(pred.SafeDist, Is.EqualTo(puvodni).Within(1e-12), "starší instance drží starou hodnotu");
                Assert.That(po.SafeDist, Is.EqualTo(0.3).Within(1e-12), "nová instance vidí novou hodnotu");
            }
            finally
            {
                Profile.SafeDist = puvodni;
            }
        }

        /// <summary>
        /// Předpoklad, na kterém stojí přizpůsobení <c>PrefDist</c> v <c>Program</c>: odstup
        /// na úrovni <c>PrefDist</c> nebo nad ním <c>Validate()</c> odmítne. Kdyby se to pravidlo
        /// v plánovači uvolnilo, přizpůsobení by bylo zbytečné; kdyby se zpřísnilo, nestačilo by.
        /// </summary>
        [Test]
        public void OdstupNadPrefDistBezPrizpusobeniNeprojdeValidaci()
        {
            var cfg = new LocalPlannerConfig { SafeDist = Profile.PrefDist };
            Assert.That(() => cfg.Validate(), Throws.ArgumentException);

            cfg.PrefDist = cfg.SafeDist + 0.1;
            Assert.That(() => cfg.Validate(), Throws.Nothing, "po posunutí PrefDist nad odstup projde");
        }
    }
}
