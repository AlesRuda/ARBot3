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
        private readonly Action<string>? onMission;
        private readonly Func<string>? onPowerOff;
        private TcpListener? listener;
        private Thread? thread;
        private volatile bool running;
        private DateTime lastErrorLog = DateTime.MinValue;

        /// <param name="status">Odberatel streamu, ze ktereho se cte stav a kresli obrazky.</param>
        /// <param name="onStop">Co udelat na <c>POST /stop</c> - v headless nastavi udalost ukonceni.
        /// Server sam <c>ARBotRuntime.Stop()</c> nevola: o ukonceni procesu rozhoduje aplikace.</param>
        /// <param name="onMission">Co udelat na <c>POST /mission?m=…</c> - v headless spusti Run
        /// s vybranou misi. <c>null</c> = vyber mise se neposkytuje (odpoved 404).</param>
        /// <param name="onPowerOff">Co udelat na <c>POST /poweroff</c> - zastavit runtime a dat
        /// systemu pokyn k vypnuti. Vraci <c>null</c> pri uspechu, jinak duvod selhani (ten se
        /// ukaze na strance). <c>null</c> callback = vypinani se nenabizi (odpoved 404).</param>
        public WebPreviewServer(WebStatus status, Action onStop, Action<string>? onMission = null,
                                Func<string>? onPowerOff = null)
        {
            this.status = status ?? throw new ArgumentNullException(nameof(status));
            this.onStop = onStop;
            this.onMission = onMission;
            this.onPowerOff = onPowerOff;
        }

        /// <summary>Nabizi tenhle server vypnuti zarizeni? Stranka podle toho ukaze tlacitko.</summary>
        public bool PowerOffAvailable => onPowerOff != null;

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

                case "/mission":
                    HandleMission(s, req);
                    return;

                case "/poweroff":
                    HandlePowerOff(s, req);
                    return;

                case "/virtualestop":
                    HandleVirtualEStop(s, req);
                    return;

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

        /// <summary>
        /// <b>Vyber mise</b> (<c>POST /mission?m=freerun</c>). Mise se jmenem <c>none</c> ani
        /// neznama neprojde (400), a hlavne: <b>gate na nouzove zastaveni se vyhodnocuje TADY</b>
        /// (409), ne jen v prohlizeci. Klientska kontrola je pohodli, tahle je pojistka - vyber
        /// mise robota nakonec rozjede, takze to musi projit jen clovek stojici u nej.
        ///
        /// <para>Hodnota jde <b>query stringem</b>, ne telem: <see cref="HttpMini"/> cte jen
        /// hlavicku a kvuli jednomu retezci nema smysl do nej pridavat cteni tela.</para>
        /// </summary>
        private void HandleMission(System.IO.Stream s, HttpRequestLine req)
        {
            if (onMission == null) { HttpMini.WriteText(s, 404, "vyber mise tahle aplikace nenabizi"); return; }
            if (!string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                HttpMini.WriteText(s, 405, "misi lze vybrat jen pres POST");
                return;
            }

            string mise = QueryValue(req.Query, "m");
            if (string.IsNullOrWhiteSpace(mise)) { HttpMini.WriteText(s, 400, "chybi parametr m"); return; }
            if (string.Equals(mise, "none", StringComparison.OrdinalIgnoreCase))
            {
                // Bez mise se nejezdi - "none" neni volba, je to stav pred volbou.
                HttpMini.WriteText(s, 400, "'none' neni mise");
                return;
            }

            string duvod = status.MissionBlockedReason();
            if (duvod != null) { HttpMini.WriteText(s, 409, duvod); return; }

            try
            {
                Trace.WriteLine($"web: prisel POST /mission?m={mise}");
                onMission(mise);
                HttpMini.WriteText(s, 200, "mise " + mise + " spustena");
            }
            catch (Exception ex)
            {
                // Neplatnou hodnotu odmitne az ParamStore (jeden zdroj pravdy o tom, co je platna
                // mise), takze se sem dostane jako vyjimka - a je to chyba VSTUPU, tedy 400.
                Trace.WriteLine("web: vyber mise selhal: " + ex.Message);
                HttpMini.WriteText(s, 400, ex.Message);
            }
        }

        /// <summary>
        /// <b>Vypnuti cele desky</b> (<c>POST /poweroff</c>) — zastavi runtime a da systemu pokyn
        /// k vypnuti.
        ///
        /// <para>Neni to totez co <c>/stop</c>: ten ukonci proces a systemd ho za par sekund vrati.
        /// Tohle vypina zarizeni, aby slo robotovi bezpecne odpojit napajeni bez useknuteho zaznamu
        /// a nedopsaneho souboroveho systemu.</para>
        ///
        /// <para><b>Odpoved se posila az po pokusu</b>, ne pred nim (na rozdil od <c>/stop</c>):
        /// selhani vypinani je presne to, co obsluha potrebuje vedet — robot, ktery na „vypnout"
        /// mlcky nic neudela, je horsi nez ten, ktery rekne proc. Kdyz se vypnuti podari, spojeni
        /// bud jeste stihne odpoved, nebo umre se systemem; obojim je odpoved bezcenna.</para>
        /// </summary>
        private void HandlePowerOff(System.IO.Stream s, HttpRequestLine req)
        {
            if (onPowerOff == null) { HttpMini.WriteText(s, 404, "vypinani tahle aplikace nenabizi"); return; }
            if (!string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                HttpMini.WriteText(s, 405, "vypnout jde jen pres POST");
                return;
            }

            Trace.WriteLine("web: prislo POST /poweroff -> zastavuji runtime a vypinam zarizeni.");
            try
            {
                string chyba = onPowerOff();
                if (chyba == null) HttpMini.WriteText(s, 200, "zarizeni se vypina");
                else
                {
                    Trace.WriteLine("web: vypnuti selhalo: " + chyba);
                    HttpMini.WriteText(s, 500, chyba);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("web: vypnuti selhalo: " + ex.Message);
                HttpMini.WriteText(s, 500, ex.Message);
            }
        }

        /// <summary>
        /// <b>Virtualni nouzove zastaveni</b> (<c>POST /virtualestop?on=true</c>) - jen pri
        /// <c>virtualhw=true</c>.
        ///
        /// <para>Bez nej by se cely handshake (stisk stopu -> vyber mise -> uvolneni stopu) nedal
        /// na Windows vubec vyzkouset: panel <i>Tools → Virtualni senzory</i> je v UI aplikaci,
        /// kterou headless nema. <b>Se skutecnym hardwarem se to odmita</b> - dalkove ovladani
        /// nouzoveho zastaveni na skutecnem robotu tu nikdy nesmi byt.</para>
        /// </summary>
        private void HandleVirtualEStop(System.IO.Stream s, HttpRequestLine req)
        {
            if (!string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                HttpMini.WriteText(s, 405, "jen POST");
                return;
            }
            if (!ARBot.Common.Configuration.ParamRegistry.VirtualHw.Value || !ARBotHW.HasCurrent)
            {
                HttpMini.WriteText(s, 404, "virtualni nouzove zastaveni je jen pri virtualhw=true");
                return;
            }

            bool on = !string.Equals(QueryValue(req.Query, "on"), "false", StringComparison.OrdinalIgnoreCase);
            try
            {
                ARBotHW.Current.VirtualSensors.EmergencyStop = on;
                Trace.WriteLine($"web: virtualni nouzove zastaveni -> {(on ? "STISKNUTO" : "uvolneno")}");
                HttpMini.WriteText(s, 200, on ? "stisknuto" : "uvolneno");
            }
            catch (Exception ex)
            {
                Trace.WriteLine("web: virtualni stop selhal: " + ex.Message);
                HttpMini.WriteText(s, 500, ex.Message);
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
