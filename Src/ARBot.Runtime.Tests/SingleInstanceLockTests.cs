using System.IO;
using ARBot;
using ARBot.Robot;

namespace ARBot.Runtime.Tests
{
    /// <summary>
    /// <see cref="SingleInstanceLock"/> a <see cref="CrashLog.LogDirectory"/>: dvě věci, které dělají
    /// nasazení stínovou kopií pod systemd bezpečným.
    ///
    /// <para>Zámek řeší tu zákeřnou situaci, kdy vedle běžící jednotky pustí člověk přes ssh druhou
    /// instanci: port náhledu se ošetří sám, takže <b>zvenčí to vypadá, že vše běží</b> — jen
    /// stránka ukazuje první proces, zatímco druhý sahá na tytéž UARTy a kamery.</para>
    /// </summary>
    [NonParallelizable]
    public class SingleInstanceLockTests
    {
        private string dir;

        [SetUp]
        public void Priprav()
        {
            dir = Path.Combine(Path.GetTempPath(), "arbot-zamek-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void Uklid()
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }

        [Test]
        public void PrvniZamekProjde_DruhyNe()
        {
            using var prvni = SingleInstanceLock.TryAcquire(dir, out string chybaPrvni);

            var druhy = SingleInstanceLock.TryAcquire(dir, out string chybaDruhy);

            Assert.Multiple(() =>
            {
                Assert.That(prvni, Is.Not.Null, chybaPrvni);
                Assert.That(druhy, Is.Null, "druha instance se nesmi pustit k hardwaru");
                // Hlaska musi rict, KDE se podivat - jinak clovek u robota jen vidi, ze to nejde.
                Assert.That(chybaDruhy, Does.Contain("systemctl"));
            });
        }

        [Test]
        public void PoUvolneniJdeZamekVzitZnovu()
        {
            SingleInstanceLock.TryAcquire(dir, out _).Dispose();

            using var druhy = SingleInstanceLock.TryAcquire(dir, out string chyba);

            Assert.That(druhy, Is.Not.Null, chyba);
        }

        [Test]
        public void DrzenyZamekNejdeOtevritAniKeCteni()
        {
            using var zamek = SingleInstanceLock.TryAcquire(dir, out _);

            // FileShare.None znamena NIC jineho, ani ctenare. Je to zamerne prisne: kdyby sel
            // soubor otevrit, hrozilo by, ze si nekdo zamek "overi" ctenim a usoudi, ze je volny.
            Assert.Throws<IOException>(
                () => new FileStream(zamek.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite).Dispose());
        }

        [Test]
        public void PoUvolneniJeVZamkuVidet_KdoHoDrzel()
        {
            string cesta;
            using (var zamek = SingleInstanceLock.TryAcquire(dir, out _)) cesta = zamek.Path;

            // Obsah je forenzni udaj: za behu ho precist nejde (viz test vyse), ale po padu nebo
            // ukonceni rekne, ktery proces tu byl posledni.
            Assert.That(File.ReadAllText(cesta), Does.Contain("pid="));
        }

        [Test]
        public void CrashLogPiseDoZadanehoAdresare()
        {
            // Pri nasazeni stinovou kopii je adresar aplikace ten, ktery se pri pristim startu
            // prepise - crash log tam nesmi zustat.
            string puvodni = CrashLog.LogDirectory;
            try
            {
                CrashLog.LogDirectory = dir;

                string cesta = CrashLog.Write("test zamku", new System.Exception("zkouska"), terminating: false);

                Assert.That(cesta, Is.Not.Null);
                Assert.That(Path.GetFullPath(cesta), Does.StartWith(Path.GetFullPath(dir)));
            }
            finally { CrashLog.LogDirectory = puvodni; }
        }
    }
}
