using System.Collections.Generic;
using ARBot.Common.Configuration;
using ARBot.Common.Occupancy;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Parametr <c>maxspeed=</c> — strop rychlosti jízdy.
    ///
    /// <para>Je to <b>bezpečnostní</b> omezení, takže se hlídají dvě věci, které by ho tiše
    /// vyřadily: (a) že registr odmítne nekladnou hodnotu už při načtení profilu, a (b) že
    /// hodnota z <c>Profile.MaxAllowedSpeed</c> opravdu dorazí ke konzumentům. Viz
    /// doc/configuration.md.</para>
    /// </summary>
    /// <remarks>Mění statické <c>Profile.MaxAllowedSpeed</c> (a zase vrací) - kdyby se testy
    /// jednou pustily paralelně, ovlivnilo by to ostatní.</remarks>
    [NonParallelizable]
    public class MaxSpeedParamTests
    {
        private static List<KeyValuePair<string, string>> Dvojice(string hodnota)
            => new List<KeyValuePair<string, string>> { new("maxspeed", hodnota) };

        [TestCase("0.1")]
        [TestCase("1.2")]
        [TestCase("0.001")]
        public void KladnaHodnotaProjde(string hodnota)
        {
            Assert.That(ParamRegistry.Validate(Dvojice(hodnota)), Is.Empty);
        }

        /// <summary>
        /// Nula ani záporná hodnota projít nesmí. Nula by znamenala „nikdy se nerozjeď" a nikdo
        /// by nepoznal, proč robot stojí; záporná je nesmysl, který by se protáhl do plánovače.
        /// </summary>
        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("-0.5")]
        public void NekladnaHodnotaJeChyba(string hodnota)
        {
            var vady = ParamRegistry.Validate(Dvojice(hodnota));
            Assert.That(vady, Is.Not.Empty, $"maxspeed={hodnota} mělo být odmítnuto.");
            Assert.That(string.Join("; ", vady), Does.Contain("vetsi nez 0"));
        }

        [TestCase("rychle")]
        [TestCase("0,1")]      // desetinna CARKA - registr chce tecku
        public void NecisloJeChyba(string hodnota)
        {
            Assert.That(ParamRegistry.Validate(Dvojice(hodnota)), Is.Not.Empty);
        }

        /// <summary>
        /// Klíč musí být v registru jako číslo — jinak by ho panel *Konfigurace* nabídl jako text
        /// a šlo by do něj napsat cokoli.
        /// </summary>
        [Test]
        public void JeVRegistruJakoCislo()
        {
            Assert.That(ParamRegistry.TryGet("maxspeed", out var def), Is.True);
            Assert.That(def.Type, Is.EqualTo(ParamType.Double));
            Assert.That(def.DefaultFromCode, Is.True, "výchozí hodnota je z kódu (Profile).");
        }

        /// <summary>
        /// Strop se čte z <c>Profile.MaxAllowedSpeed</c> až při VZNIKU objektu — proto ho
        /// <c>Program</c> nastavuje před složením runtime. Tenhle test to dokládá na
        /// <see cref="LocalPlannerConfig"/>: instance vzniklá po změně vidí novou hodnotu,
        /// dřív vzniklá drží starou. Kdyby se to změnilo, přestane pořadí v <c>Program.Main</c>
        /// stačit a strop by platil jen zčásti.
        /// </summary>
        [Test]
        public void LocalPlannerCteStropAzPriVznikuInstance()
        {
            double puvodni = Profile.MaxAllowedSpeed;
            try
            {
                var pred = new LocalPlannerConfig();

                Profile.MaxAllowedSpeed = 0.1;
                var po = new LocalPlannerConfig();

                Assert.That(po.MaxSpeed, Is.EqualTo(0.1).Within(1e-9),
                            "instance vzniklá PO nastavení musí strop vidět");
                Assert.That(pred.MaxSpeed, Is.EqualTo(puvodni).Within(1e-9),
                            "instance vzniklá PŘED nastavením drží starou hodnotu - proto se "
                            + "maxspeed aplikuje v Program.Main, ne později");
            }
            finally
            {
                Profile.MaxAllowedSpeed = puvodni;
            }
        }

        /// <summary>
        /// Z <c>MaxAllowedSpeed</c> nesmí nic derivovat — to je celý důvod, proč se tenhle jeden
        /// údaj dá zpřístupnit, i když `Profile` jako celek v registru není (odvozená statická
        /// pole by po změně zůstala stará). <c>MaxTheoreticalSpeed</c> se počítá z obvodu kola
        /// a otáček motoru.
        /// </summary>
        [Test]
        public void ZeStropuNicNederivuje()
        {
            double puvodni = Profile.MaxAllowedSpeed;
            double teoretickaPred = Profile.MaxTheoreticalSpeed;
            try
            {
                Profile.MaxAllowedSpeed = 0.1;
                Assert.That(Profile.MaxTheoreticalSpeed, Is.EqualTo(teoretickaPred).Within(1e-12));
            }
            finally
            {
                Profile.MaxAllowedSpeed = puvodni;
            }
        }
    }
}
