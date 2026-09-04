using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Rendering;

namespace ARBot.Robot.Web
{
    /// <summary>
    /// <b>Stav pro webovy nahled</b> - odberatel <see cref="ARBotRuntime.Stream"/> s politikou
    /// „latest-wins" a <b>liznym renderem</b>: <see cref="Post"/> jen ulozi posledni zpravu daneho
    /// druhu, obrazky se kresli teprve v obsluze pozadavku. Kdyz se nikdo nekouka, nahled nestoji nic.
    ///
    /// <para><b>Kamera je vyjimka.</b> <see cref="CameraFrame"/> nese <b>poolovane</b> capture buffery,
    /// ktere kamera recykluje, takze si na nej nejde drzet referenci - musi se poridit kopie
    /// z <see cref="CameraFramePool"/> (tak to dela i <c>ImageDocument</c> v UI). Kopie se ale dela
    /// <b>jen kdyz o snimek nekdo v poslednich <see cref="CameraInterestSec"/> sekundach stal</b>
    /// (<see cref="NoteCameraInterest"/>); jinak se snimek zahodi bez kopirovani. Tim nahled bez
    /// publika nestoji ani memcpy - a to je podstatne, protoze rozpocet CPU na zarizeni neni znamy.</para>
    ///
    /// <para>Ostatni zpravy (<see cref="OccupancyGridMsg"/>, <see cref="RobotStateMsg"/>, …) si svoje
    /// pole alokuji samy (viz <c>OccupancyGrid.ToLogMessage</c>), takze u nich staci reference.</para>
    /// </summary>
    public sealed class WebStatus : IMessageSink
    {
        /// <summary>Jak dlouho po pozadavku na snimek se snimky jeste kopiruji [s].</summary>
        public const double CameraInterestSec = 10;
        /// <summary>Kolik bodu ujete drahy se pamatuje.</summary>
        private const int TrailCapacity = 600;
        /// <summary>Kratsi posun se do drahy nezapisuje [m].</summary>
        private const double TrailMinStepM = 0.1;

        private readonly object gate = new object();

        /// <summary>
        /// Kopie snimku v obehu. <b>Musi pokryt pocet kamer + jeden slot na vymenu:</b> drzi se
        /// posledni snimek z KAZDE kamery a novou kopii je potreba poridit driv, nez se stara vrati.
        ///
        /// <para>⚠️ Puvodni kapacita 2 byla vada: se dvema kamerami (Left, Right) po prvnim snimku
        /// z kazde uz nebyl volny slot, <see cref="CameraFramePool.Acquire"/> vracel <c>null</c>
        /// a vsechny dalsi snimky se ticho zahazovaly - obraz na strance zamrzl na prvnim snimku.
        /// Hlida to <c>DveKamery_SeObeAktualizuji</c>. Ctyri slavi tri kamery.</para>
        /// </summary>
        private readonly CameraFramePool framePool = new CameraFramePool(4);

        /// <summary>Kdy se naposled hlasilo vycerpani poolu - hlaska nejvys jednou za minutu.</summary>
        private DateTime lastPoolWarning = DateTime.MinValue;
        private readonly Dictionary<string, CameraFrame> cameras = new Dictionary<string, CameraFrame>();
        private readonly List<PlanViewPoint> trail = new List<PlanViewPoint>(TrailCapacity);

        /// <summary>
        /// Kdy naposled <b>vysla do streamu</b> zprava daneho druhu (klic = druh zpravy,
        /// u pojmenovanych zdroju i jmeno). Fan-out je synchronni na vlakne producenta, takze je to
        /// prakticky okamzik publikovani - ne cas porizeni (ten je o dobu zpracovani v pipeline starsi).
        ///
        /// <para>⚠️ <b>Cas se bere z <see cref="TimeBase.Now"/></b>, ne z <c>DateTime.Now</c> ani
        /// <c>UtcNow</c>: cela aplikace meri tou zakladnou (cas startu plus monotonni stopky, ktera
        /// <b>zamerne nesleduje skoky NTP</b>), takze razitka jsou srovnatelna a timeouty se
        /// nerozbiji pri synchronizaci hodin.</para>
        /// </summary>
        private readonly Dictionary<string, DateTime> lastMeasurement = new Dictionary<string, DateTime>();

        private OccupancyGridMsg grid;
        private RobotStateMsg state;
        private GlobalNavMsg nav;
        private MissionMsg mission;
        private FreeRunMsg freeRun;
        private LocalPlanMsg plan;
        private PerfMsg perf;
        private DateTime cameraInterest = DateTime.MinValue;

        /// <summary>Jmena kamer, ze kterych uz snimek prisel (diagnostika a vyber vrstvy).</summary>
        public string[] CameraNames
        {
            get { lock (gate) { var k = new string[cameras.Count]; cameras.Keys.CopyTo(k, 0); return k; } }
        }

        /// <summary>
        /// Rekni, ze o snimky kamery ma nekdo zajem (vola server pri kazdem <c>/camera.jpg</c>,
        /// i kdyz snimek jeste neni - jinak by se prvni snimek nikdy nezkopiroval).
        /// </summary>
        public void NoteCameraInterest()
        {
            lock (gate) cameraInterest = TimeBase.Now;
        }

        // --- IMessageSink: bezi na vlakne producenta, MUSI byt neblokujici a skoupe na alokace. ---
        public void Post(Message msg)
        {
            if (msg == null) return;

            // Vek mereni: kazda zprava od senzoru (IMUState, GPSState, MotorStateBase, CameraFrame)
            // dedi ze SensorStateBase. Senzor, ktery hlasi OK a pritom uz nic neposila, je ta horsi
            // porucha - a bez tohohle by nebyla videt.
            if (msg is SensorStateBase)
            {
                string druh = msg is INamedMessage nm && !string.IsNullOrEmpty(nm.Name)
                    ? msg.GetType().Name + ":" + nm.Name
                    : msg.GetType().Name;

                // TimeBase, ne DateTime.Now - tou zakladnou meri cela aplikace (viz lastMeasurement).
                var kdy = TimeBase.Now;
                lock (gate) lastMeasurement[druh] = kdy;
            }

            switch (msg)
            {
                case CameraFrame cf: PostCamera(cf); return;
                case OccupancyGridMsg og: lock (gate) { grid = og; } return;
                case RobotStateMsg rs: PostState(rs); return;
                case GlobalNavMsg gn: lock (gate) { nav = gn; } return;
                case MissionMsg mm: lock (gate) { mission = mm; } return;
                case FreeRunMsg fr: lock (gate) { freeRun = fr; } return;
                case LocalPlanMsg lp: lock (gate) { plan = lp; } return;
                case PerfMsg pm: lock (gate) { perf = pm; } return;
            }
        }

        private void PostCamera(CameraFrame cf)
        {
            // Bez zajmu se snimek ani nekopiruje - to je cely trik, jak nahled bez publika nic nestoji.
            lock (gate)
            {
                if ((TimeBase.Now - cameraInterest).TotalSeconds > CameraInterestSec) return;
            }

            var copy = framePool.Acquire(cf);
            if (copy == null)
            {
                // Drop je best-effort, ale NE ticho: presne tenhle stav (vycerpany pool) drzel
                // obraz zamrzly na prvnim snimku a hodinu se hledalo, proc se stranka neaktualizuje.
                lock (gate)
                {
                    if ((TimeBase.Now - lastPoolWarning).TotalSeconds >= 60)
                    {
                        lastPoolWarning = TimeBase.Now;
                        System.Diagnostics.Trace.WriteLine(
                            $"WebStatus: pool kopii snimku je vycerpany ({framePool.Capacity} slotu, "
                            + $"{framePool.InUseCount} obsazenych) -> snimek zahozen a NAHLED ZAMRZNE. "
                            + "Kapacita musi byt pocet kamer + 1.");
                    }
                }
                return;
            }

            string key = cf.Name ?? string.Empty;
            CameraFrame old = null;
            lock (gate)
            {
                cameras.TryGetValue(key, out old);
                cameras[key] = copy;
            }
            if (old != null) framePool.Release(old);   // vraceni do poolu mimo zamek
        }

        private void PostState(RobotStateMsg rs)
        {
            lock (gate)
            {
                state = rs;
                if (trail.Count == 0)
                {
                    trail.Add(new PlanViewPoint(rs.X, rs.Y));
                    return;
                }
                var last = trail[trail.Count - 1];
                double dx = rs.X - last.X, dy = rs.Y - last.Y;
                if (dx * dx + dy * dy < TrailMinStepM * TrailMinStepM) return;

                if (trail.Count >= TrailCapacity) trail.RemoveAt(0);
                trail.Add(new PlanViewPoint(rs.X, rs.Y));
            }
        }

        /// <summary>
        /// Nakresli pudorys z posledniho stavu. <c>null</c> = nepodarilo se.
        /// </summary>
        /// <param name="scaleBarM">Pozadovana delka meritkove usecky [m] - z ni se pocita vyrez
        /// (usecka je jeho ctvrtina, viz <see cref="PlanViewRenderer.SpanForScaleBar"/>). Stranka
        /// tim prepina priblizeni; nesmyslna hodnota spadne na 10 m.</param>
        public byte[] RenderPlanView(double scaleBarM = 10)
        {
            PlanViewInput input;
            lock (gate)
            {
                input = new PlanViewInput
                {
                    Grid = grid,
                    Network = ARBotRuntime.HasCurrent ? ARBotRuntime.Current.RoadNetwork : null,
                    Origin = ARBotRuntime.HasCurrent ? ARBotRuntime.Current.MapOrigin : null,
                    HasPose = state != null,
                    PoseX = state?.X ?? 0,
                    PoseY = state?.Y ?? 0,
                    PoseTheta = state?.Theta ?? 0,
                    Trail = trail.ToArray(),
                };

                // Mrkev: globalni navigace ji ma jako CarrotX/Y, mise FreeRun jako GoalX/Y.
                if (nav != null && nav.HasCarrot)
                {
                    input.HasCarrot = true; input.CarrotX = nav.CarrotX; input.CarrotY = nav.CarrotY;
                }
                else if (freeRun != null)
                {
                    input.HasCarrot = true; input.CarrotX = freeRun.GoalX; input.CarrotY = freeRun.GoalY;
                }
            }
            return PlanViewRenderer.Render(input, new PlanViewOptions
            {
                SpanM = PlanViewRenderer.SpanForScaleBar(scaleBarM),
            });
        }

        /// <summary>
        /// Zakoduje posledni snimek dane kamery do JPEG. <paramref name="cam"/> null nebo prazdne =
        /// prvni, ktera je k dispozici. <c>null</c> = zadny snimek (server vrati 204).
        ///
        /// <para><paramref name="layer"/> = <c>"prob"</c> posle misto RGB
        /// <see cref="CameraFrame.ImageProbability"/>, tedy <b>pravdepodobnost cesty z RGB</b> - to,
        /// co robot povazuje za cestu jeste pred fuzi do mapy (plni <c>CameraFrameProcessor</c>, cte
        /// <c>OccupancyIntegrator</c>). Je to <c>Image&lt;Gray&gt;</c> (step 1), takze do JPEG jde
        /// tymz kodekem bez prevodu. Cokoliv jineho = RGB.</para>
        /// </summary>
        public byte[] RenderCameraJpeg(string cam, string layer)
        {
            CameraFrame frame = null;
            lock (gate)
            {
                if (!string.IsNullOrEmpty(cam)) cameras.TryGetValue(cam, out frame);
                else foreach (var kv in cameras) { frame = kv.Value; break; }
            }
            if (frame == null) return null;

            bool prob = string.Equals(layer, "prob", StringComparison.OrdinalIgnoreCase);
            ARBot.Common.Common.Image img = prob ? frame.ImageProbability : frame.ImageRGB;
            if (img == null) return null;

            try { return ImageMsg.EncodeJpeg(img); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"WebStatus: kodovani snimku selhalo: {ex.Message}");
                return null;
            }
        }

        /// <summary>Stav jako JSON - tentyz obsah, jaky ma tabulka na strance.</summary>
        public string ToJson(bool running)
        {
            var sb = new StringBuilder(512);
            lock (gate)
            {
                sb.Append('{');
                sb.Append("\"running\":").Append(running ? "true" : "false");
                Num(sb, "x", state?.X); Num(sb, "y", state?.Y); Num(sb, "theta", state?.Theta);
                Num(sb, "v", state?.V); Num(sb, "omega", state?.Omega);
                Num(sb, "planLength", plan?.LengthM); Num(sb, "clearance", plan?.MinClearanceM);
                Num(sb, "offRoute", nav?.OffRouteDist); Num(sb, "routeLength", nav?.RouteLengthM);
                Num(sb, "cpu", perf?.ProcessCpuPct);
                if (perf != null) sb.Append(",\"missedTicks\":").Append(perf.MissedTicks);
                if (mission != null)
                {
                    sb.Append(",\"missionPhase\":").Append(mission.Phase);
                    sb.Append(",\"missionElapsed\":").Append(Fmt(mission.ElapsedSec));
                    Str(sb, "missionCode", mission.AcceptedCodeText);
                    Str(sb, "missionAbort", mission.AbortReason);
                }
                if (freeRun != null)
                {
                    sb.Append(",\"corridor\":").Append(freeRun.FromCorridor ? "true" : "false");
                    Num(sb, "corridorWidth", freeRun.Width);
                    Num(sb, "lateral", freeRun.Lateral);
                }
                // Jmena kamer pod tymz zamkem - property CameraNames by brala zamek znovu.
                if (cameras.Count > 0)
                {
                    var jmena = new string[cameras.Count];
                    cameras.Keys.CopyTo(jmena, 0);
                    Str(sb, "cameras", string.Join(",", jmena));
                }

                AppendSensors(sb);
                sb.Append('}');
            }
            return sb.ToString();

            void Num(StringBuilder b, string name, double? v)
            {
                if (v.HasValue) b.Append(",\"").Append(name).Append("\":").Append(Fmt(v.Value));
            }
            void Str(StringBuilder b, string name, string v)
            {
                if (!string.IsNullOrEmpty(v))
                    b.Append(",\"").Append(name).Append("\":\"")
                     .Append(v.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            }
        }

        /// <summary>
        /// Pole <c>sensors</c> do JSON: pro kazdy senzor z <see cref="ARBotHW"/> jeho jmeno a
        /// <see cref="ARBot.Common.Devices.ISensor.IsError"/>, plus vek posledniho mereni toho druhu,
        /// pokud ho jde priradit. Pod tim pole <c>measurements</c> se vsemi druhy zprav od senzoru
        /// a jejich vekem - senzor, ktery hlasi OK a nic neposila, je jinak nevidet.
        ///
        /// <para><b>Vola se pod zamkem</b> <see cref="gate"/> (kvuli <see cref="lastMeasurement"/>).
        /// Na <c>ARBotHW.Current</c> se saha jen kdyz uz instance existuje - cteni te vlastnosti ji
        /// jinak zaklada a spousti init hardwaru.</para>
        /// </summary>
        private void AppendSensors(StringBuilder sb)
        {
            var now = TimeBase.Now;
            var sparovane = new HashSet<string>(StringComparer.Ordinal);

            sb.Append(",\"sensors\":[");
            bool prvni = true;
            if (ARBotHW.HasCurrent)
            {
                try
                {
                    foreach (var s in ARBotHW.Current.Sensors)
                    {
                        if (s == null) continue;

                        // Vek mereni toho senzoru: klic se odvodi z JEHO ROZHRANI, ne ze jmena
                        // (jmena senzoru a druhy zprav se neshoduji - "VirtualIMU" vs "IMUState").
                        string klic = KlicMereni(s);
                        double? vek = null;
                        if (klic != null && lastMeasurement.TryGetValue(klic, out var kdy))
                        {
                            vek = (now - kdy).TotalSeconds;
                            sparovane.Add(klic);
                        }

                        if (!prvni) sb.Append(',');
                        prvni = false;
                        sb.Append("{\"n\":\"").Append(Escape(s.Name ?? "?")).Append("\",\"e\":")
                          .Append(s.IsError ? "true" : "false").Append(",\"age\":")
                          .Append(vek.HasValue ? Fmt(vek.Value) : "null").Append('}');
                    }
                }
                catch (Exception ex)
                {
                    // Seznam senzoru se meni za behu (SetRealHW/SetVirtualHW) - kolize nesmi shodit stranku.
                    System.Diagnostics.Trace.WriteLine("WebStatus: cteni senzoru selhalo: " + ex.Message);
                }
            }
            sb.Append(']');

            // Mereni, ke kteremu se zadny senzor nenasel (HW jeste nestoji, nebo se senzor odebral) -
            // at se udaj neztrati. Pri slozenem HW je tenhle seznam prazdny.
            sb.Append(",\"measurements\":[");
            prvni = true;
            foreach (var kv in lastMeasurement)
            {
                if (sparovane.Contains(kv.Key)) continue;
                if (!prvni) sb.Append(',');
                prvni = false;
                double vek = (now - kv.Value).TotalSeconds;
                sb.Append("{\"n\":\"").Append(Escape(kv.Key)).Append("\",\"age\":").Append(Fmt(vek)).Append('}');
            }
            sb.Append(']');
        }

        /// <summary>
        /// Klic do <see cref="lastMeasurement"/> pro dany senzor - podle jeho <b>rozhrani</b>, protoze
        /// jmeno senzoru a druh zpravy se neshoduji (<c>VirtualIMU</c> posila <c>IMUState</c>).
        ///
        /// <para>U <b>kamer a IMU</b> se rozlisuje i jmenem, protoze jich muze byt v robotovi vic
        /// (Left/Right, VN100 i T265) - proto <see cref="ARBot.Common.Models.IMUState.Name"/>
        /// (pribylo 4. 9. 2026, verze zpravy 2). GPS a motor jsou po jednom, tam staci druh.
        /// Jmeno musi byt <b>tataz hodnota</b> jako <c>ISensor.Name</c>, jinak se par nenajde
        /// a vek se ukaze zvlast v <c>measurements</c>.</para>
        /// </summary>
        private static string KlicMereni(ARBot.Common.Devices.ISensor s) => s switch
        {
            ARBot.HAL.ICamera c => nameof(CameraFrame) + ":" + (c.Name ?? string.Empty),
            ARBot.HAL.IIMU i => nameof(ARBot.Common.Models.IMUState) + ":" + (i.Name ?? string.Empty),
            ARBot.HAL.IGPS => nameof(ARBot.Common.Devices.GPSState),
            ARBot.Common.Devices.IMotorControl => nameof(ARBot.Common.Devices.MotorStateBase),
            _ => null,
        };

        private static string Escape(string v)
            => v.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string Fmt(double v)
            => double.IsFinite(v) ? v.ToString("0.###", CultureInfo.InvariantCulture) : "null";

        /// <summary>Stranka nahledu. Zadne externi zdroje - Pi je offline.</summary>
        public string ToHtml() => Html;

        private const string Html = @"<!doctype html>
<html lang=""cs""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>ARBot - náhled</title>
<style>
 body{background:#14181c;color:#e6e9ec;font:14px system-ui,sans-serif;margin:0;padding:12px}
 h1{font-size:16px;margin:0 0 10px}
 /* Lista nad obrázkem: přepínače vlevo, zastavení vpravo. Šířka jako obrázek, aby to
    lícovalo; na mobilu se zlomí jen skupina přepínačů (lista sama nowrap), takže Stop
    zůstane vpravo. */
 .lista{display:flex;justify-content:space-between;align-items:flex-start;gap:8px;
        flex-wrap:nowrap;max-width:520px;margin-bottom:8px}
 .prepinace{display:flex;gap:5px;align-items:center;flex-wrap:wrap}
 .mezera{width:6px}
 button{border:0;border-radius:4px;color:#fff;font:inherit;white-space:nowrap}
 button.prep{background:#37474f;padding:5px 10px;font-size:13px}
 button.prep.akt{background:#1565c0}
 /* Meritko ma smysl jen u pudorysu - u kamery se skryva. */
 button.mer.skryto{display:none}
 button.stop{background:#c62828;padding:6px 12px;font-size:13px;font-weight:600;flex-shrink:0}
 img{width:100%;max-width:520px;display:block;background:#0b0e11;border:1px solid #2a2f35;
     margin-bottom:10px}
 table{border-collapse:collapse;font-variant-numeric:tabular-nums}
 td{padding:2px 10px 2px 0}
 td:first-child{color:#9aa0a6}
 #stav{color:#9aa0a6;margin-top:8px}
 h2{font-size:13px;color:#9aa0a6;margin:14px 0 6px;font-weight:600}
 /* Senzory: stitek za jmenem - OK zelene, chyba cervene, ticho oranzove. */
 .sen{display:inline-block;padding:3px 9px;border-radius:3px;margin:0 6px 6px 0;
      background:#1f272e;font-size:13px}
 .sen b{font-weight:600;color:#e6e9ec}
 .sen.ok{color:#4caf50}
 .sen.err{background:#4a1d1d;color:#ff6b6b}
 .sen.ticho{background:#4a3a1d;color:#ffb74d}
</style></head><body>
<h1>ARBot - náhled</h1>
<div class=""lista"">
 <div class=""prepinace"">
  <button class=""prep akt"" id=""b-world"" onclick=""vrstva('world')"">půdorys</button>
  <button class=""prep"" id=""b-rgb"" onclick=""vrstva('rgb')"">kamera</button>
  <button class=""prep"" id=""b-prob"" onclick=""vrstva('prob')"">cesta</button>
  <span class=""mezera""></span>
  <button class=""prep mer"" id=""m-2"" onclick=""meritko(2)"">2 m</button>
  <button class=""prep mer akt"" id=""m-10"" onclick=""meritko(10)"">10 m</button>
  <button class=""prep mer"" id=""m-50"" onclick=""meritko(50)"">50 m</button>
 </div>
 <button class=""stop"" onclick=""zastavit()"">Zastavit robota</button>
</div>
<img id=""obraz"" alt=""náhled"">
<h2>senzory</h2>
<div id=""senzory"">—</div>
<h2>stav</h2>
<table id=""tab""></table>
<div id=""stav"">spojuji se...</div>
<script>
var popisky={running:'běží',x:'X [m]',y:'Y [m]',theta:'kurz [rad]',v:'rychlost [m/s]',omega:'omega [rad/s]',
 planLength:'plán [m]',clearance:'odstup [m]',offRoute:'mimo trasu [m]',routeLength:'trasa [m]',
 cpu:'CPU procesu [%]',missedTicks:'zameškané takty',missionPhase:'fáze mise',missionElapsed:'mise [s]',
 missionCode:'kód',missionAbort:'přerušeno',corridor:'koridor',corridorWidth:'šířka koridoru [m]',
 lateral:'odchylka [m]',cameras:'kamery'};
// Jeden obrazek, tri vrstvy: pudorys | kamera (RGB) | cesta z RGB (ImageProbability).
// Kdyz se kouka na pudorys, o snimky kamery se vubec nezada - a server je proto ani nekopiruje.
var vrstvaObrazu='world';
// Meritko = delka usecky v pudorysu [m]; vyrez je jeji ctyrnasobek (resi server).
var meritkoM=10;
function vrstva(v){
 vrstvaObrazu=v;
 ['world','rgb','prob'].forEach(function(k){
  document.getElementById('b-'+k).className='prep'+(k===v?' akt':'');
 });
 var jePudorys = v==='world';
 [2,10,50].forEach(function(m){
  var b=document.getElementById('m-'+m);
  b.className='prep mer'+(m===meritkoM?' akt':'')+(jePudorys?'':' skryto');
 });
 tik();
}
function meritko(m){
 meritkoM=m;
 [2,10,50].forEach(function(k){
  document.getElementById('m-'+k).className='prep mer'+(k===m?' akt':'');
 });
 tik();
}
function tik(){
 var t=Date.now();
 document.getElementById('obraz').src = vrstvaObrazu==='world'
   ? '/world.png?scale='+meritkoM+'&t='+t
   : '/camera.jpg?layer='+vrstvaObrazu+'&t='+t;
 fetch('/status.json').then(function(r){return r.json()}).then(function(d){
  var h='';
  for(var k in d){
   if(k==='sensors'||k==='measurements')continue;   // vykresluji se zvlast, nize
   h+='<tr><td>'+(popisky[k]||k)+'</td><td>'+d[k]+'</td></tr>';
  }
  document.getElementById('tab').innerHTML=h;
  document.getElementById('senzory').innerHTML=senzoryHtml(d);
  document.getElementById('stav').textContent=d.running?'runtime běží':'runtime zastaven';
 }).catch(function(){ document.getElementById('stav').textContent='server neodpovídá'; });
}
// Senzory: jeden udaj = jmeno, stav z HW (ISensor.IsError) a vek posledni jeho zpravy,
// tedy ve tvaru Left: OK/432ms. Chyba je cervene, ticho oranzove; prah 3 s je volny
// zamerne - GPS jde 5 Hz a kamery pod 30, takze kratsi by plane strasil.
function vekText(a){
 if(a===null||a===undefined) return '—';
 return a<1 ? Math.round(a*1000)+'ms' : a.toFixed(1)+'s';
}
function senzoryHtml(d){
 var h='';
 (d.sensors||[]).forEach(function(s){
  var ticho = s.age===null || s.age===undefined || s.age>3;
  var cls = s.e ? 'err' : (ticho ? 'ticho' : 'ok');
  h+='<span class=""sen '+cls+'""><b>'+s.n+'</b>: '+(s.e?'CHYBA':'OK')+'/'+vekText(s.age)+'</span>';
 });
 // Mereni bez senzoru (HW jeste nestoji nebo se senzor odebral) - stav neznamy.
 (d.measurements||[]).forEach(function(m){
  h+='<span class=""sen '+(m.age>3?'ticho':'ok')+'""><b>'+m.n+'</b>: —/'+vekText(m.age)+'</span>';
 });
 return h||'žádné senzory ani měření';
}
function zastavit(){
 if(!confirm('Zastavit robota a ukončit proces?'))return;
 fetch('/stop',{method:'POST'}).then(function(){
  document.getElementById('stav').textContent='zastaveno';
 });
}
tik(); setInterval(tik,1000);
</script></body></html>";
    }
}
