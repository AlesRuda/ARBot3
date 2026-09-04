using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ARBot.Common.Common;

namespace ARBot.Robot.Web
{
    /// <summary>
    /// <b>Webovy nahled headless runtime.</b> Jedno vlakno prijima spojeni a obsluhuje je po jednom;
    /// odpovida na <c>/</c>, <c>/world.png</c>, <c>/camera.jpg</c>, <c>/status.json</c>
    /// a <c>POST /stop</c>. Viz doc/headless.md.
    ///
    /// <para><b>Nesmi ublizit rizeni:</b> vlakno je <c>IsBackground</c> a ma
    /// <see cref="ThreadPriority.BelowNormal"/>, spojeni se serializuji a obrazky se kresli teprve
    /// na pozadavek (<see cref="WebStatus"/>). Kdyz bind selze, <see cref="Start"/> vrati
    /// <c>false</c> a <b>robot jede dal bez nahledu</b> - stejna zasada, jakou ma zaznam
    /// (chybejici nahled je horsi diagnostika, ale nespusteny robot je horsi vysledek).</para>
    ///
    /// <para><b>Bez autentizace, na vsech rozhranich</b> (rozhodnuti autora 4. 9. 2026): robot je na
    /// uzavrene siti a jediny zasah je zastaveni, tedy ta bezpecnejsi strana. Rozjet robota z webu
    /// nejde a nikdy nesmi jit.</para>
    /// </summary>
    public sealed class WebPreviewServer : IDisposable
    {
        /// <summary>Casovy limit na cteni i zapis jednoho spojeni [ms] - zaseknute nesmi drzet server.</summary>
        private const int IoTimeoutMs = 5000;

        private readonly WebStatus status;
        private readonly Action onStop;
        private TcpListener? listener;
        private Thread? thread;
        private volatile bool running;
        private DateTime lastErrorLog = DateTime.MinValue;

        /// <param name="status">Odberatel streamu, ze ktereho se cte stav a kresli obrazky.</param>
        /// <param name="onStop">Co udelat na <c>POST /stop</c> - v headless nastavi udalost ukonceni.
        /// Server sam <c>ARBotRuntime.Stop()</c> nevola: o ukonceni procesu rozhoduje aplikace.</param>
        public WebPreviewServer(WebStatus status, Action onStop)
        {
            this.status = status ?? throw new ArgumentNullException(nameof(status));
            this.onStop = onStop;
        }

        /// <summary>Skutecny port, na kterem server posloucha (u portu 0 ten pridelený OS).</summary>
        public int Port { get; private set; }

        /// <summary>
        /// Nastartuje server. <c>false</c> = bind selhal (obsazeny port, chybejici pravo) a jede se
        /// dal bez nahledu; duvod je v <see cref="Trace"/>.
        /// </summary>
        public bool Start(int port)
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"web={port}: nahled se nepodarilo nastartovat ({ex.Message}) -> bez nahledu.");
                listener = null;
                return false;
            }

            running = true;
            thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "ARBot web",
                // Nahled nikdy nesmi soupent s ridici smyckou.
                Priority = ThreadPriority.BelowNormal,
            };
            thread.Start();
            Trace.WriteLine($"web={Port}: nahled bezi na http://<ip>:{Port}/ (bez hesla; /stop zastavi robota).");
            return true;
        }

        private void Loop()
        {
            while (running)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                    client.ReceiveTimeout = IoTimeoutMs;
                    client.SendTimeout = IoTimeoutMs;
                    Handle(client);
                }
                catch (Exception ex)
                {
                    if (!running) break;   // Dispose zavrel listener - normalni konec
                    LogRateLimited($"web: obsluha spojeni selhala: {ex.Message}");
                }
                finally
                {
                    try { client?.Close(); } catch { }
                }
            }
        }

        private void Handle(TcpClient client)
        {
            using var s = client.GetStream();

            string header = HttpMini.ReadHeader(s);
            if (header == null)
            {
                HttpMini.WriteText(s, 413, "hlavicka je prilis dlouha nebo spojeni skoncilo");
                return;
            }

            int nl = header.IndexOf('\n');
            var req = HttpMini.ParseRequestLine(nl > 0 ? header.Substring(0, nl).TrimEnd('\r') : header);
            if (!req.Ok) { HttpMini.WriteText(s, 400, "nesmyslny pozadavek"); return; }

            switch (req.Path)
            {
                case "/":
                case "/index.html":
                    HttpMini.WriteResponse(s, 200, "text/html; charset=utf-8",
                                           Encoding.UTF8.GetBytes(status.ToHtml()));
                    return;

                case "/world.png":
                {
                    // ?scale=<metry> = delka meritkove usecky, tedy priblizeni (stranka posila 2/10/50).
                    var png = status.RenderPlanView(DoubleFromQuery(req.Query, "scale", 10));
                    if (png == null) { HttpMini.WriteText(s, 503, "pudorys se nepodarilo nakreslit"); return; }
                    HttpMini.WriteResponse(s, 200, "image/png", png);
                    return;
                }

                case "/camera.jpg":
                {
                    // Zajem se hlasi VZDY, i kdyz snimek jeste neni - jinak by se prvni snimek
                    // nikdy nezkopiroval a kamera by zustala prazdna nadobro.
                    status.NoteCameraInterest();
                    var jpeg = status.RenderCameraJpeg(QueryValue(req.Query, "cam"),
                                                       QueryValue(req.Query, "layer"));
                    if (jpeg == null) { HttpMini.WriteResponse(s, 204, null, null); return; }
                    HttpMini.WriteResponse(s, 200, "image/jpeg", jpeg);
                    return;
                }

                case "/status.json":
                {
                    bool run = ARBotRuntime.HasCurrent && ARBotRuntime.Current.IsRunning;
                    HttpMini.WriteResponse(s, 200, "application/json; charset=utf-8",
                                           Encoding.UTF8.GetBytes(status.ToJson(run)));
                    return;
                }

                case "/stop":
                    if (!string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        // GET by mohl vyvolat prefetch prohlizece nebo nahled odkazu.
                        HttpMini.WriteText(s, 405, "zastaveni jde jen pres POST");
                        return;
                    }
                    Trace.WriteLine("web: prislo POST /stop -> ukoncuji.");
                    HttpMini.WriteText(s, 200, "zastavuji");
                    try { onStop?.Invoke(); }
                    catch (Exception ex) { Trace.WriteLine("web: stop selhal: " + ex.Message); }
                    return;

                default:
                    HttpMini.WriteText(s, 404, "nenalezeno");
                    return;
            }
        }

        /// <summary>Vytahne hodnotu klice z query stringu (<c>cam</c>, <c>layer</c>); null = nebyl.</summary>
        private static string QueryValue(string query, string key)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(key)) return null;

            string prefix = key + "=";
            foreach (var part in query.Split('&'))
            {
                if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(part.Substring(prefix.Length));
            }
            return null;
        }

        /// <summary>
        /// Cislo z query stringu; pri chybejicim nebo nesmyslnem vraci <paramref name="def"/>.
        /// Vzdy <c>InvariantCulture</c> - v URL je desetinna tecka bez ohledu na narodni nastaveni.
        /// </summary>
        private static double DoubleFromQuery(string query, string key, double def)
        {
            string raw = QueryValue(query, key);
            if (string.IsNullOrEmpty(raw)) return def;
            return double.TryParse(raw, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out double v)
                   ? v : def;
        }

        /// <summary>Hlaska nejvys jednou za minutu - zaplavena konzole je horsi nez zadna.</summary>
        private void LogRateLimited(string text)
        {
            var now = TimeBase.Now;
            if ((now - lastErrorLog).TotalSeconds < 60) return;
            lastErrorLog = now;
            Trace.WriteLine(text);
        }

        public void Dispose()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            listener = null;
            try { thread?.Join(1000); } catch { }
            thread = null;
        }
    }
}
