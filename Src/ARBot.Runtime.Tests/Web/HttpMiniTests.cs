using System.IO;
using System.Text;
using ARBot.Robot.Web;

namespace ARBot.Runtime.Tests.Web
{
    /// <summary>
    /// Minimalni HTTP nad TcpListener. Vlastni proto, ze HttpListener na Windows bez admin prav
    /// neumi jiny prefix nez localhost (namereno 4. 9. 2026). Viz doc/plan-headless-web.md.
    /// </summary>
    public class HttpMiniTests
    {
        [Test]
        public void RozeberePozadavekAOdstrihneQueryString()
        {
            var r = HttpMini.ParseRequestLine("GET /world.png?t=12345 HTTP/1.1");

            Assert.Multiple(() =>
            {
                Assert.That(r.Ok, Is.True);
                Assert.That(r.Method, Is.EqualTo("GET"));
                Assert.That(r.Path, Is.EqualTo("/world.png"), "query string do routovani nepatri");
                Assert.That(r.Query, Is.EqualTo("t=12345"));
            });
        }

        [Test]
        public void PozadavekBezQuery_MaPrazdnyQuery()
        {
            var r = HttpMini.ParseRequestLine("POST /stop HTTP/1.1");
            Assert.Multiple(() =>
            {
                Assert.That(r.Ok, Is.True);
                Assert.That(r.Method, Is.EqualTo("POST"));
                Assert.That(r.Path, Is.EqualTo("/stop"));
                Assert.That(r.Query, Is.Empty);
            });
        }

        [TestCase("")]
        [TestCase("GET")]
        [TestCase("blabla")]
        [TestCase("GET bezlomitka HTTP/1.1")]
        public void NesmyslnyRadek_NeniOk(string radek)
            => Assert.That(HttpMini.ParseRequestLine(radek).Ok, Is.False);

        [Test]
        public void PrecteHlavickuAzPoPrazdnyRadek()
        {
            var vstup = new MemoryStream(Encoding.ASCII.GetBytes(
                "GET / HTTP/1.1\r\nHost: pi:8080\r\n\r\nTELO"));

            string hlavicka = HttpMini.ReadHeader(vstup);

            Assert.Multiple(() =>
            {
                Assert.That(hlavicka, Does.StartWith("GET / HTTP/1.1"));
                Assert.That(hlavicka, Does.Contain("Host: pi:8080"));
                Assert.That(hlavicka, Does.Not.Contain("TELO"), "telo do hlavicky nepatri");
            });
        }

        [Test]
        public void SnesiHlavickuJenSLf()
        {
            // curl a telnet posilaji LF bez CR; prohlizec CRLF. Oboji musi projit.
            var vstup = new MemoryStream(Encoding.ASCII.GetBytes("GET / HTTP/1.1\nHost: x\n\n"));

            string hlavicka = HttpMini.ReadHeader(vstup);

            Assert.That(hlavicka, Does.StartWith("GET / HTTP/1.1"));
        }

        [Test]
        public void PrilisDlouhaHlavicka_VratiNull()
        {
            var dlouha = "GET / HTTP/1.1\r\nX: " + new string('a', HttpMini.MaxHeaderBytes + 10);
            var vstup = new MemoryStream(Encoding.ASCII.GetBytes(dlouha));

            Assert.That(HttpMini.ReadHeader(vstup, HttpMini.MaxHeaderBytes), Is.Null);
        }

        [Test]
        public void ZavreneSpojeniPredKoncemHlavicky_VratiNull()
        {
            var vstup = new MemoryStream(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n"));

            Assert.That(HttpMini.ReadHeader(vstup), Is.Null);
        }

        [Test]
        public void OdpovedMaStavovyRadekDelkuANoStore()
        {
            var vystup = new MemoryStream();
            HttpMini.WriteResponse(vystup, 200, "image/png", new byte[] { 1, 2, 3 });

            byte[] vse = vystup.ToArray();
            string s = Encoding.ASCII.GetString(vse);
            Assert.Multiple(() =>
            {
                Assert.That(s, Does.StartWith("HTTP/1.1 200 OK\r\n"));
                Assert.That(s, Does.Contain("Content-Type: image/png"));
                Assert.That(s, Does.Contain("Content-Length: 3"));
                Assert.That(s, Does.Contain("Cache-Control: no-store"));
                Assert.That(s, Does.Contain("Connection: close"));
                Assert.That(vse[^3..], Is.EqualTo(new byte[] { 1, 2, 3 }), "telo na konci");
            });
        }

        [Test]
        public void OdpovedBezTela_MaNulovouDelku()
        {
            var vystup = new MemoryStream();
            HttpMini.WriteResponse(vystup, 204, null, null);

            string s = Encoding.ASCII.GetString(vystup.ToArray());
            Assert.Multiple(() =>
            {
                Assert.That(s, Does.StartWith("HTTP/1.1 204 No Content\r\n"));
                Assert.That(s, Does.Contain("Content-Length: 0"));
                Assert.That(s, Does.Not.Contain("Content-Type"));
            });
        }

        [Test]
        public void ChybovyStav_MaSpravnyText()
        {
            var vystup = new MemoryStream();
            HttpMini.WriteText(vystup, 404, "nenalezeno");

            string s = Encoding.ASCII.GetString(vystup.ToArray());
            Assert.Multiple(() =>
            {
                Assert.That(s, Does.StartWith("HTTP/1.1 404 Not Found\r\n"));
                Assert.That(s, Does.EndWith("nenalezeno"));
            });
        }
    }
}
