using System.Collections.Generic;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Parametr <c>open=</c> — pohledy otevřené hned po startu. Hlídá se, že registr odmítne neznámé
    /// jméno už při načtení profilu (jinak by se na zařízení nic neotevřelo a nikdo by nevěděl proč)
    /// a že rozbor seznamu je shovívavý k mezerám, velikosti písmen a duplicitám.
    /// Viz doc/configuration.md.
    /// </summary>
    public class OpenParamTests
    {
        private static List<KeyValuePair<string, string>> Dvojice(string hodnota)
            => new List<KeyValuePair<string, string>> { new("open", hodnota) };

        [TestCase("world")]
        [TestCase("world,telemetry")]
        [TestCase(" World ; Robot ,images")]
        [TestCase("")]
        public void ZnamePohledyProjdou(string hodnota)
        {
            Assert.That(ParamRegistry.Validate(Dvojice(hodnota)), Is.Empty);
        }

        [Test]
        public void NeznamyPohledJeChybaAHlaskaRekneKtereZna()
        {
            var vady = ParamRegistry.Validate(Dvojice("world,mapa"));
            Assert.That(vady, Is.Not.Empty);
            string text = string.Join("; ", vady);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("mapa"), "hláška má jmenovat neznámé jméno");
                Assert.That(text, Does.Contain("world"), "hláška má vyjmenovat známá jména");
            });
        }

        [Test]
        public void SeznamSeNormalizuje_MezeryVelikostDuplicity()
        {
            bool ok = ParamParsers.TryViews(" World ,telemetry; world ,ROBOT", out var views, out _);
            Assert.That(ok, Is.True);
            Assert.That(views, Is.EqualTo(new[] { "world", "telemetry", "robot" }), "poradi = poradi prvniho vyskytu");
        }

        [Test]
        public void PrazdnyTextJePrazdnySeznam()
        {
            Assert.That(ParamParsers.TryViews("", out var views, out _), Is.True);
            Assert.That(views, Is.Empty);
            Assert.That(ParamParsers.TryViews(null, out views, out _), Is.True);
            Assert.That(views, Is.Empty);
        }

        [Test]
        public void JeVRegistruJakoSlozenyText()
        {
            Assert.That(ParamRegistry.TryGet("open", out var def), Is.True);
            Assert.That(def.Type, Is.EqualTo(ParamType.String));
            Assert.That(def.Parse, Is.Not.Null, "hodnotu má ověřovat parser, ne jen typ");
        }
    }
}
