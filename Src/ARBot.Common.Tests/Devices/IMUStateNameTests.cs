using System.IO;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Tests.Devices
{
    /// <summary>
    /// <see cref="IMUState.Name"/> - jmeno zdroje mereni. Pridano 4. 9. 2026, protoze IMU muze byt
    /// v robotovi <b>vic</b> (VN100 i T265 jsou <c>IIMU</c>) a ze zpravy nebylo poznat, ze ktereho
    /// je: diagnostika „ktery senzor mlci" pak mluvila o „IMUState" bez puvodce. Zprava tim dostala
    /// verzi formatu 2 a musi umet precist i verzi 1 (starsi zaznamy jmeno nenesou).
    /// </summary>
    public class IMUStateNameTests
    {
        private static IMUState Vzorek() => new IMUState
        {
            Name = "VN100 IMU",
            TimeStamp = new System.DateTime(2026, 9, 4, 12, 34, 56),
            FrameNum = 42,
            Confidence = 0.75,
            Magnetometer = new System.Numerics.Vector3(1, 2, 3),
            AngularVelocity = new System.Numerics.Vector3(0.1f, 0.2f, 0.3f),
        };

        [Test]
        public void JeToPojmenovanaZprava()
        {
            Assert.That(Vzorek(), Is.InstanceOf<INamedMessage>(),
                        "podle jmena se pary zprava<->senzor v diagnostice (napr. webovy nahled)");
            Assert.That(((INamedMessage)Vzorek()).Name, Is.EqualTo("VN100 IMU"));
        }

        [Test]
        public void VerzeFormatuJe3()
            => Assert.That(IMUState.FormatVersion, Is.EqualTo(3),
                           "pridani pole = zvednuta verze, jinak by se stare zaznamy cetly spatne");

        [Test]
        public void RoundTrip_ZachovaPriznakAbsolutnihoKurzu()
        {
            // Verze 3 (6. 9. 2026): bez tohohle priznaku by se relativni yaw z T265 dostal do fuze
            // jako ABSOLUTNI kurz a vnutil by filtru libovolne otoceny svet.
            var vzorek = Vzorek();
            vzorek.HasAbsoluteHeading = false;

            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                vzorek.ToData(bw);

            ms.Position = 0;
            var zpet = new IMUState();
            using (var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                zpet.FromData(br);

            Assert.That(zpet.HasAbsoluteHeading, Is.False);
        }

        [Test]
        public void VychoziJeAbsolutniKurz()
            => Assert.That(new IMUState().HasAbsoluteHeading, Is.True,
                           "tak se chovaly vsechny zdroje do 6. 9. 2026; kdo ma yaw relativni, musi to rict");

        [Test]
        public void Clone_PreneseAbsolutnostKurzu()
        {
            var vzorek = Vzorek();
            vzorek.HasAbsoluteHeading = false;

            Assert.That(vzorek.Clone().HasAbsoluteHeading, Is.False,
                        "fuze a historie klonuji stavy - klonovanim by se z relativniho yaw stal absolutni");
        }

        [Test]
        public void RoundTrip_ZachovaJmenoIZbytek()
        {
            var vzorek = Vzorek();

            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                vzorek.ToData(bw);

            ms.Position = 0;
            var zpet = new IMUState();
            using (var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                zpet.FromData(br);

            Assert.Multiple(() =>
            {
                Assert.That(zpet.Name, Is.EqualTo("VN100 IMU"));
                Assert.That(zpet.TimeStamp, Is.EqualTo(vzorek.TimeStamp));
                Assert.That(zpet.FrameNum, Is.EqualTo(42u));
                Assert.That(zpet.Confidence, Is.EqualTo(0.75));
                Assert.That(zpet.Magnetometer, Is.EqualTo(vzorek.Magnetometer));
                Assert.That(zpet.AngularVelocity, Is.EqualTo(vzorek.AngularVelocity));
            });
        }

        [Test]
        public void PrazdneJmeno_ProjdeRoundTripem()
        {
            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                new IMUState().ToData(bw);   // Name = null

            ms.Position = 0;
            var zpet = new IMUState();
            using (var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                zpet.FromData(br);

            Assert.That(zpet.Name, Is.Empty, "null jmeno se uklada jako prazdny retezec");
        }

        [Test]
        public void Clone_PreneseJmeno()
        {
            Assert.That(Vzorek().Clone().Name, Is.EqualTo("VN100 IMU"),
                        "fuze a historie klonuji stavy - bez tohohle by se puvodce cestou ztratil");
        }

        [Test]
        public void JeVReplayKatalogu()
        {
            // Katalog mapuje jmeno zpravy na prototyp; bez toho by se zaznam neprehral.
            var katalog = MessageCatalog.RecordDefaults();
            Assert.That(katalog.Contains(nameof(IMUState)), Is.True);
            Assert.That(katalog.ToPrototypeMap()[nameof(IMUState)], Is.InstanceOf<IMUState>());
        }
    }
}
