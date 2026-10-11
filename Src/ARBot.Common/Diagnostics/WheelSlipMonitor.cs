using System;
using System.Collections.Generic;
using System.Globalization;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using ARBot.Common.Configuration;

namespace ARBot.Common.Diagnostics
{
    /// <summary>Uroven hlidace prokluzu (<see cref="WheelSlipDetector"/>).</summary>
    public enum WheelSlipLevel : byte
    {
        /// <summary>Kola a gyro se shoduji (nebo neni z ceho rozhodnout).</summary>
        None = 0,
        /// <summary>Otaceni z kol nesouhlasi s gyrem nad prahem — prokluz, smyk nebo otaceni rukou.</summary>
        Slip = 1,
    }

    /// <summary>Jak kola s gyrem nesouhlasi.</summary>
    public enum WheelSlipKind : byte
    {
        /// <summary>Nevi se (zadna epizoda).</summary>
        Unknown = 0,
        /// <summary>Kola stoji, robot se otaci — otaceni rukou, tlaceni, smyk stojiciho robotu.</summary>
        WheelsStill = 1,
        /// <summary>Kola se toci proti otaceni robotu.</summary>
        Opposite = 2,
        /// <summary>Kola hlasi vic otoceni nez robot - prokluz kol (hrabou na miste, v trave, ve sterku).</summary>
        WheelsMore = 3,
        /// <summary>Robot se otaci vic, nez hlasi kola - smyk tela.</summary>
        BodyMore = 4,
    }

    /// <summary>Okamzity stav hlidace pro stranku nahledu.</summary>
    public readonly struct WheelSlipReading
    {
        public WheelSlipReading(WheelSlipLevel level, WheelSlipKind kind, string text, bool recent)
        {
            Level = level;
            Kind = kind;
            Text = text ?? string.Empty;
            Recent = recent;
        }

        /// <summary>Probiha prokluz?</summary>
        public WheelSlipLevel Level { get; }
        /// <summary>Druh probihajici (nebo posledni) epizody.</summary>
        public WheelSlipKind Kind { get; }
        /// <summary>Lidsky popis (s diakritikou); prazdny, kdyz neni co hlasit.</summary>
        public string Text { get; }
        /// <summary>Text popisuje nedavno SKONCENOU epizodu (ne probihajici).</summary>
        public bool Recent { get; }
    }

    /// <summary>Nastaveni <see cref="WheelSlipDetector"/>. Prahy z dat, viz doc/ekf-fusion.md.</summary>
    public sealed class WheelSlipConfig
    {
        /// <summary>Okno, pres ktere se prumeruje uhlova rychlost z kol i z gyra [s].</summary>
        public double WindowSec = 0.5;

        /// <summary>Pevna cast prahu na rozdil prumeru kola − gyro [rad/s].</summary>
        public double AbsThresholdRadS = 0.15;

        /// <summary>
        /// Pomerna cast prahu: nasobi se vetsi z obou prumernych rychlosti. Podvozek se v zatackach
        /// systematicky smyka — kola hlasi o 3–7 % vic otoceni nez telo (Robotour, 1. 10. 2026) —
        /// a to prokluz neni.
        /// </summary>
        public double RelThreshold = 0.25;

        /// <summary>Jak dlouho musi rozdil nad prahem trvat, nez se ohlasi [s].</summary>
        public double MinOnSec = 0.3;

        /// <summary>
        /// O kolik se musi otoceni z kol a z gyra rozejit, nez se epizoda ohlasi [rad] (vychozi 10°).
        /// <para>Nad 50 zaznamy (3,4 h jizdy) byly epizody pod 10° nejasne: rozdil kratce nad prahem,
        /// ktery se vyrusil (oba integraly 2°), nebo raz o 6–8° pri 1 m/s. Pri <c>odoratestd=0.3</c>
        /// nesou kola 5,9 % ω, takze 10° rozdilu pohne kurzem fuze o ~0,6°.</para>
        /// </summary>
        public double MinAngleRad = 10 * Math.PI / 180;

        /// <summary>Jak dlouho musi byt rozdil pod polovinou prahu, nez epizoda skonci [s].</summary>
        public double MinOffSec = 0.5;

        /// <summary>Nejvetsi mezera v datech jednoho zdroje uvnitr okna [s]; vetsi = nerozhoduje se.</summary>
        public double MaxGapSec = 0.1;

        /// <summary>Jak casto se okno vyhodnocuje [s].</summary>
        public double EvalPeriodSec = 0.05;

        /// <summary>Jak dlouho bez platnych dat smi trvat probihajici epizoda, nez se ukonci [s].</summary>
        public double DataTimeoutSec = 1.0;

        /// <summary>Rozchod kol [m] - jako <see cref="FusionConfig.WheelBase"/>.</summary>
        public double WheelBase = Profile.Rozchod;

        /// <summary>Znamenko uhlove rychlosti z kol - jako <see cref="FusionConfig.OdoOmegaSign"/>.</summary>
        public double OdoOmegaSign = 1.0;

        /// <summary>Jak casto posilat <see cref="WheelSlipMsg"/>, dokud prokluz trva [s].</summary>
        public double MessagePeriodSec = 1.0;

        /// <summary>
        /// Jak casto smi do Trace radek o skoncene epizode [s]; mezitim skoncene se spocitaji
        /// a ohlasi souhrnem (v trave nebo pri otaceni rukou jich muze byt hodne za sebou).
        /// </summary>
        public double TracePeriodSec = 5.0;

        /// <summary>Jak dlouho po konci ukazovat posledni epizodu na strance nahledu [s].</summary>
        public double RecentSec = 30.0;

        /// <summary>Konfigurace se stejnym rozchodem a znamenkem jako fuze.</summary>
        public static WheelSlipConfig From(FusionConfig fusion)
        {
            var c = new WheelSlipConfig();
            if (fusion != null)
            {
                c.WheelBase = fusion.WheelBase;
                c.OdoOmegaSign = fusion.OdoOmegaSign;
            }
            return c;
        }

        /// <summary>Zkontroluje konzistenci; vyhodi <see cref="ArgumentException"/>.</summary>
        public void Validate()
        {
            if (!(WindowSec > 0) || !(EvalPeriodSec > 0) || !(MaxGapSec > 0) || !(WheelBase > 0))
                throw new ArgumentException("WheelSlipConfig: WindowSec, EvalPeriodSec, MaxGapSec a WheelBase musi byt kladne.");
            if (!(AbsThresholdRadS > 0) || !(RelThreshold >= 0) || !(RelThreshold < 1))
                throw new ArgumentException("WheelSlipConfig: musi platit AbsThresholdRadS > 0 a 0 <= RelThreshold < 1.");
            if (!(MinOnSec >= 0) || !(MinOffSec >= 0) || !(MinAngleRad >= 0) || !(DataTimeoutSec > 0) || !(MessagePeriodSec > 0)
                || !(TracePeriodSec >= 0) || !(RecentSec >= 0))
                throw new ArgumentException("WheelSlipConfig: casy nesmi byt zaporne.");
            if (OdoOmegaSign != 1.0 && OdoOmegaSign != -1.0)
                throw new ArgumentException("WheelSlipConfig: OdoOmegaSign musi byt +1 nebo -1.");
        }
    }

    /// <summary>
    /// <b>Hlidac prokluzu kol</b> — cista logika bez vlaken (<see cref="WheelSlipMonitor"/> ji obaluje
    /// stupnem). Porovnava uhlovou rychlost z kol (<c>OdoOmegaSign·(vR − vL)/rozchod</c>, tentyz vzorec
    /// jako <c>Odo/rate</c> ve fuzi) s gyrem (<c>IMU/gyro</c> z IMU s absolutnim kurzem) a rozdil nad
    /// prahem hlasi jako prokluz. Tema <c>lok-fuze-rucni-otoceni</c>, doc/ekf-fusion.md.
    ///
    /// <para><b>Nacpak:</b> mericich kol se nikdy nepozna prokluz z kol samotnych (jsou hnana a ve
    /// zpetne vazbe, sleduji rampu prikazu, at maji trakci, nebo ne — proto se nepouziva stary
    /// <see cref="SlipDetector"/>). Pozna se jen proti nezavisle referenci, a pro otaceni je to gyro.
    /// Fuze to delala ticho: do 11. 10. 2026 nesla kola 24 % integralu ω a otoceni rukou nebo
    /// prokluz sly do kurzu (1. 10. −19°). Od te doby nesou 5,9 % (<c>odoratestd=0.3</c>), ale ze
    /// k prokluzu doslo, nehlasil nikdo.</para>
    ///
    /// <para><b>Jen hlasi</b> (autor 11. 10. 2026): fuzi nemeni, merenie kol nezahazuje.</para>
    ///
    /// <para><b>Rozhoduje se z prumeru pres okno</b> <see cref="WheelSlipConfig.WindowSec"/>, ne ze
    /// vzorku: rozdil vzorek po vzorku ma i bez prokluzu sd 0,10–0,29 rad/s (vibrace tela, ktere
    /// kola nevidi). Prah je <c>abs + rel·max(|ω̄kola|, |ω̄gyro|)</c>, pomerna cast odfiltruje
    /// systematicky smyk podvozku v zatackach. Epizoda zacne po <see cref="WheelSlipConfig.MinOnSec"/>
    /// nad prahem a skonci po <see cref="WheelSlipConfig.MinOffSec"/> pod jeho polovinou.</para>
    ///
    /// <para><b>Cas je cas DAT</b> (razitka vzorku), takze hlidac dava totez v behu, v prehravani
    /// i v testech a jde pustit nad starym zaznamem (<c>ARBot.Analyze prokluz</c>). Okno konci
    /// u starsiho z poslednich vzorku obou zdroju, aby ho pokryvaly oba; s mezerou nad
    /// <see cref="WheelSlipConfig.MaxGapSec"/> se nerozhoduje (zasek procesu 1. 10. 2026 by jinak
    /// vypadal jako prokluz).</para>
    /// </summary>
    public sealed class WheelSlipDetector
    {
        private readonly WheelSlipConfig cfg;

        /// <summary>Vzorek uhlove rychlosti [rad/s] (u kol i dopredna rychlost [m/s]).</summary>
        private readonly struct Vzorek
        {
            public Vzorek(DateTime t, double w, double v) { T = t; W = w; V = v; }
            public DateTime T { get; }
            public double W { get; }
            public double V { get; }
        }

        private readonly List<Vzorek> gyro = new List<Vzorek>();
        private readonly List<Vzorek> odo = new List<Vzorek>();
        private DateTime now;           // nejnovejsi razitko dat
        private DateTime lastEval;      // konec posledniho vyhodnoceneho okna
        private DateTime lastData;      // konec posledniho useku, ktery oba zdroje pokryly daty
        private long outOfOrder;        // zahozene vzorky s casem pozadu

        // posledni okno
        private double winOdo = double.NaN, winGyro = double.NaN, winV = double.NaN, winThr = double.NaN;

        // rozbehajici se / probihajici epizoda
        private WheelSlipLevel level;
        private DateTime pendingSince;  // od kdy je rozdil nad prahem (default = neni)
        private double pendOdo, pendGyro; // integraly [rad] od zacatku okna pri prvnim prekroceni
        private DateTime quietSince;    // od kdy je rozdil pod polovinou prahu (jen v epizode)
        // Casovy profil rozdilu ∫(kola − gyro) SE ZNAMENKEM po useccich ~EvalPeriodSec od zacatku okna
        // pri prvnim prekroceni - z nej delka a zacatek epizody (viz Rozpeti).
        private readonly List<(DateTime Od, DateTime Do, double D)> profil = new List<(DateTime, DateTime, double)>();
        private double epOdo, epGyro;   // integraly [rad] od zacatku epizody (vcetne okna pred detekci)
        private double epPeak;          // nejvetsi |rozdil prumeru| [rad/s]
        private WheelSlipKind epKind;
        private DateTime lastMsg;

        // posledni skoncena epizoda (stranka)
        private DateTime lastEnd;
        private double lastDur, lastOdo, lastGyro;
        private string lastReason;
        private WheelSlipKind lastKind;

        // skrceni Trace
        private DateTime lastTrace;
        private int suppressed;
        private double suppressedSum, suppressedMax;

        public WheelSlipDetector(WheelSlipConfig config = null)
        {
            cfg = config ?? new WheelSlipConfig();
            cfg.Validate();
        }

        /// <summary>Nastaveni.</summary>
        public WheelSlipConfig Config => cfg;

        /// <summary>Aktualni uroven.</summary>
        public WheelSlipLevel Level => level;

        /// <summary>Cas konce posledniho vyhodnoceneho okna; <c>default</c> = jeste zadne.</summary>
        public DateTime LastEval => lastEval;

        /// <summary>Kolik vzorku prislo s casem pozadu a zahodilo se.</summary>
        public long OutOfOrder => outOfOrder;

        /// <summary>Prumerna uhlova rychlost z kol v poslednim okne [rad/s]; NaN = nevi se.</summary>
        public double WindowOdoRate => winOdo;

        /// <summary>Prumerna uhlova rychlost z gyra v poslednim okne [rad/s]; NaN = nevi se.</summary>
        public double WindowGyroRate => winGyro;

        /// <summary>
        /// Zpracuje zpravu. Pouziva <see cref="IMUState"/> s absolutnim kurzem (gyro) a
        /// <see cref="MotorStateBase"/> s merenim (kola); ostatni ignoruje. Do <paramref name="lines"/>
        /// pripise radky pro Trace, vraci zpravu do zaznamu, nebo <c>null</c>.
        /// </summary>
        public WheelSlipMsg Feed(Message msg, List<string> lines)
        {
            switch (msg)
            {
                // Jako DefaultMeasurementMapper: IMU/gyro jde jen z IMU s absolutnim kurzem (VN100);
                // relativni (T265) nese do fuze jinou velicinu.
                case IMUState imu when imu.HasAbsoluteHeading && imu.AngularVelocity.HasValue:
                {
                    double w = imu.AngularVelocity.Value.Z;
                    if (!double.IsFinite(w)) return null;
                    if (gyro.Count > 0 && imu.TimeStamp <= gyro[gyro.Count - 1].T) { outOfOrder++; return null; }
                    gyro.Add(new Vzorek(imu.TimeStamp, w, 0));
                    return Advance(imu.TimeStamp, lines);
                }
                case MotorStateBase m when m.HasMeasurement && m.TimeStamp != default:
                {
                    double w = cfg.OdoOmegaSign * (m.RightWheelSpeed - m.LeftWheelSpeed) / cfg.WheelBase;
                    double v = 0.5 * (m.LeftWheelSpeed + m.RightWheelSpeed);
                    if (!double.IsFinite(w) || !double.IsFinite(v)) return null;
                    if (odo.Count > 0 && m.TimeStamp <= odo[odo.Count - 1].T) { outOfOrder++; return null; }
                    odo.Add(new Vzorek(m.TimeStamp, w, v));
                    return Advance(m.TimeStamp, lines);
                }
                default:
                    return null;
            }
        }

        private WheelSlipMsg Advance(DateTime t, List<string> lines)
        {
            if (t > now) now = t;
            if (gyro.Count == 0 || odo.Count == 0) return Tick(lines);

            // Okno konci u starsiho z poslednich vzorku obou zdroju - tam jsou data od obou.
            DateTime b = gyro[gyro.Count - 1].T < odo[odo.Count - 1].T ? gyro[gyro.Count - 1].T : odo[odo.Count - 1].T;
            if (lastEval != default && (b - lastEval).TotalSeconds < cfg.EvalPeriodSec)
            {
                // Konec okna stoji: jeden zdroj nechodi vubec (vypadek VN100, motory jen se zastupnymi
                // ramci). Probihajici epizoda by jinak neskoncila nikdy a nezustala by po ni stopa
                // v Trace - po DataTimeoutSec od poslednich dat se ukonci (nalez kontroly 11. 10. 2026).
                if (level == WheelSlipLevel.Slip && lastData != default
                    && (now - lastData).TotalSeconds > cfg.DataTimeoutSec)
                    return End(lines, lastData, "chybí data z kol nebo z gyra");
                return Tick(lines);
            }

            WheelSlipMsg outMsg = Evaluate(b, lines);
            Trim(b);
            return outMsg;
        }

        /// <summary>Zahodi vzorky starsi, nez je potreba pro okno a pro pricitani prirustku.</summary>
        private void Trim(DateTime b)
        {
            // Start epizody pocita integral od okna, kde rozdil poprve presahl prah, tedy az
            // WindowSec + MinOnSec zpatky.
            var hranice = b.AddSeconds(-(cfg.WindowSec + cfg.MinOnSec + cfg.MaxGapSec + 1.0));
            int kg = 0; while (kg + 1 < gyro.Count && gyro[kg + 1].T < hranice) kg++;
            if (kg > 0) gyro.RemoveRange(0, kg);
            int ko = 0; while (ko + 1 < odo.Count && odo[ko + 1].T < hranice) ko++;
            if (ko > 0) odo.RemoveRange(0, ko);
        }

        private WheelSlipMsg Evaluate(DateTime b, List<string> lines)
        {
            DateTime a = b.AddSeconds(-cfg.WindowSec);
            bool valid = Mean(gyro, a, b, s => s.W, out double mg) & MeanOdo(a, b, out double mo, out double mv);
            DateTime prev = lastEval;
            lastEval = b;
            WheelSlipMsg outMsg = null;

            // Prirustky integralu od posledniho vyhodnoceni (presne, ze vzorku) - do epizody i do
            // rozbehajici se epizody. Rozhoduje pokryti TOHO useku, ne celeho okna: po mezere je okno
            // jeste WindowSec neplatne, ale data uz chodi a otoceni se pocita dal (do 11. 10. 2026 se
            // ztratila i ta pulsekunda - 24 % uhlu po jedine 0,2s mezere, nalez kontroly). Usek, ve
            // kterem mezera je, se vynecha cely - drzena hodnota by pricitala nesmysl.
            double incOdo = 0, incGyro = 0;
            bool incOk = prev != default && Pokryti(gyro, prev, b) && Pokryti(odo, prev, b);
            if (incOk)
            {
                incOdo = Integral(odo, prev, b, s => s.W);
                incGyro = Integral(gyro, prev, b, s => s.W);
                lastData = b;
            }
            else if (valid) lastData = b;
            if (incOk && level == WheelSlipLevel.Slip)
            {
                epOdo += incOdo; epGyro += incGyro;
                profil.Add((prev, b, incOdo - incGyro));
            }

            if (!valid)
            {
                // Okno se nerozhoduje; rozbeh epizody se rusi, probihajici trva (zobrazuji se posledni
                // platne prumery) a skonci, az data nechodi dele nez DataTimeoutSec.
                pendingSince = default;
                if (level == WheelSlipLevel.Slip && lastData != default
                    && (b - lastData).TotalSeconds > cfg.DataTimeoutSec)
                    outMsg = End(lines, lastData, "chybí data z kol nebo z gyra");
                return outMsg ?? Tick(lines);
            }

            winOdo = mo; winGyro = mg; winV = mv;
            double diff = mo - mg;
            double thr = cfg.AbsThresholdRadS + cfg.RelThreshold * Math.Max(Math.Abs(mo), Math.Abs(mg));
            winThr = thr;

            if (level == WheelSlipLevel.None)
            {
                if (Math.Abs(diff) > thr)
                {
                    if (pendingSince == default)
                    {
                        // Integral od zacatku okna, ve kterem rozdil poprve presahl prah (tam uz byl);
                        // mimo rozdil pricita obema stranam totez, takze rozdil integralu sedi.
                        pendingSince = b;
                        pendOdo = Integral(odo, a, b, s => s.W);
                        pendGyro = Integral(gyro, a, b, s => s.W);
                        profil.Clear();
                        for (DateTime u = a; u < b; u = u.AddSeconds(cfg.EvalPeriodSec))
                        {
                            DateTime v = u.AddSeconds(cfg.EvalPeriodSec);
                            if (v > b) v = b;
                            profil.Add((u, v, Integral(odo, u, v, s => s.W) - Integral(gyro, u, v, s => s.W)));
                        }
                    }
                    else if (incOk)
                    {
                        pendOdo += incOdo; pendGyro += incGyro;
                        profil.Add((prev, b, incOdo - incGyro));
                    }
                    if ((b - pendingSince).TotalSeconds >= cfg.MinOnSec
                        && Math.Abs(pendOdo - pendGyro) >= cfg.MinAngleRad)
                        outMsg = Start(lines, b, diff);
                }
                else pendingSince = default;
            }
            else
            {
                if (Math.Abs(diff) > epPeak) { epPeak = Math.Abs(diff); epKind = KindOf(mo, mg); }
                if (Math.Abs(diff) < 0.5 * thr)
                {
                    if (quietSince == default) quietSince = b;
                    if ((b - quietSince).TotalSeconds >= cfg.MinOffSec)
                        outMsg = End(lines, quietSince, "rozdíl kol a gyra zmizel");
                }
                else quietSince = default;
                if (outMsg == null && (b - lastMsg).TotalSeconds >= cfg.MessagePeriodSec)
                    outMsg = Message();
            }
            return outMsg ?? Tick(lines);
        }

        /// <summary>Pravidelne: odlozeny souhrn potlacenych radku do Trace.</summary>
        private WheelSlipMsg Tick(List<string> lines)
        {
            if (suppressed > 0 && (now - lastTrace).TotalSeconds >= cfg.TracePeriodSec)
                Souhrn(lines);
            return null;
        }

        /// <summary>Radek se souhrnem potlacenych epizod do Trace (a vynulovani).</summary>
        private void Souhrn(List<string> lines)
        {
            lines?.Add(string.Format(CultureInfo.InvariantCulture,
                "PROKLUZ kol: dalsich {0} epizod od posledniho hlaseni (soucet rozdilu {1:F0} deg, nejvetsi {2:F0} deg).",
                suppressed, suppressedSum * 180 / Math.PI, suppressedMax * 180 / Math.PI));
            lastTrace = now;
            suppressed = 0; suppressedSum = 0; suppressedMax = 0;
        }

        /// <summary>
        /// Konec behu (zastaveni runtime): probihajici epizodu ukonci s duvodem
        /// <paramref name="reason"/> a odlozeny souhrn vypise hned. Jinak by po prokluzu, ktery
        /// trval pri zastaveni, nezustala v Trace zadna stopa (radky jdou jen pri konci epizody).
        /// </summary>
        public WheelSlipMsg Flush(List<string> lines, string reason)
        {
            WheelSlipMsg m = null;
            if (level == WheelSlipLevel.Slip)
                m = End(lines, lastData != default ? lastData : lastEval, reason);
            if (suppressed > 0) Souhrn(lines);
            return m;
        }

        private WheelSlipMsg Start(List<string> lines, DateTime b, double diff)
        {
            level = WheelSlipLevel.Slip;
            epOdo = pendOdo;
            epGyro = pendGyro;
            epPeak = Math.Abs(diff);
            epKind = KindOf(winOdo, winGyro);
            quietSince = default;
            pendingSince = default;
            return Message();
        }

        /// <summary>
        /// Zacatek a delka epizody: kdy se rozdil kola − gyro skutecne nascital. Z kumulativniho
        /// rozdilu SE ZNAMENKEM epizody po useccich ~<see cref="WheelSlipConfig.EvalPeriodSec"/> se
        /// najdou casy, kdy dosahl 10 % a 90 % celku (s interpolaci uvnitr useku), a delka je jejich
        /// rozdil / 0,8 (pri stalem rozdilu presne), zacatek = cas 10 % − 0,1·delka.
        /// <para><b>Ne |rozdil| po useccich</b> (tak to bylo pred kontrolou 11. 10. 2026): vibrace
        /// gyra za jizdy maji nulovy prumer, ale jejich absolutni hodnota se scitala, takze z 0,15s
        /// prokluzu vyslo 1,5 s. Se znamenkem se vyrusi. <b>Ne doba „prumer okna nad prahem"</b>:
        /// ta je o ~okno delsi nez rozdil sam a u kratkych epizod by vysla nula.</para>
        /// </summary>
        private (DateTime Zacatek, double Delka) Rozpeti()
        {
            if (profil.Count == 0) return (lastEval, 0);
            double celkem = 0;
            foreach (var u in profil) celkem += u.D;
            int sgn = Math.Sign(celkem);
            if (sgn == 0) return (profil[0].Od, 0);
            double cil = Math.Abs(celkem);
            DateTime t10 = default, t90 = default;
            double c = 0;
            foreach (var u in profil)
            {
                double c0 = c;
                c += sgn * u.D;
                foreach (var (podil, nastav) in new[] { (0.1, 0), (0.9, 1) })
                {
                    double hranice = podil * cil;
                    bool hotovo = nastav == 0 ? t10 != default : t90 != default;
                    if (hotovo || c < hranice) continue;
                    double f = c > c0 ? (hranice - c0) / (c - c0) : 1;
                    if (f < 0) f = 0;
                    var t = u.Od + TimeSpan.FromTicks((long)((u.Do - u.Od).Ticks * f));
                    if (nastav == 0) t10 = t; else t90 = t;
                }
                if (t90 != default) break;
            }
            if (t10 == default) t10 = profil[0].Od;
            if (t90 == default) t90 = profil[profil.Count - 1].Do;
            double delka = Math.Max(0, (t90 - t10).TotalSeconds / 0.8);
            return (t10 - TimeSpan.FromSeconds(0.1 * delka), delka);
        }

        private double Trvani() => Rozpeti().Delka;

        private WheelSlipMsg End(List<string> lines, DateTime end, string reason)
        {
            var (zacatek, dur) = Rozpeti();
            var kind = KindOfIntegrals(epOdo, epGyro);
            level = WheelSlipLevel.None;
            pendingSince = default;
            quietSince = default;
            lastEnd = end; lastDur = dur; lastOdo = epOdo; lastGyro = epGyro; lastKind = kind; lastReason = reason;
            double rozdil = epOdo - epGyro;

            string radek = string.Format(CultureInfo.InvariantCulture,
                "PROKLUZ kol {0:F1} s: kola {1:F0} deg, robot (gyro) {2:F0} deg, rozdil {3:F0} deg - {4} ({5}).",
                dur, epOdo * 180 / Math.PI, epGyro * 180 / Math.PI, rozdil * 180 / Math.PI,
                StuckDetector.Ascii(KindText(kind)), StuckDetector.Ascii(reason));
            if (lastTrace == default || (now - lastTrace).TotalSeconds >= cfg.TracePeriodSec)
            {
                if (suppressed > 0)
                    radek += string.Format(CultureInfo.InvariantCulture,
                        " Predtim dalsich {0} epizod (soucet rozdilu {1:F0} deg, nejvetsi {2:F0} deg).",
                        suppressed, suppressedSum * 180 / Math.PI, suppressedMax * 180 / Math.PI);
                lines?.Add(radek);
                lastTrace = now;
                suppressed = 0; suppressedSum = 0; suppressedMax = 0;
            }
            else
            {
                suppressed++;
                suppressedSum += Math.Abs(rozdil);
                suppressedMax = Math.Max(suppressedMax, Math.Abs(rozdil));
            }

            var m = Message();
            m.Level = (byte)WheelSlipLevel.None;
            m.Kind = (byte)kind;
            m.EpisodeSec = dur;
            m.EpisodeStart = zacatek;
            m.Text = "prokluz skončil: " + reason;
            return m;
        }

        private WheelSlipMsg Message()
        {
            lastMsg = lastEval;
            return new WheelSlipMsg
            {
                Level = (byte)level,
                Kind = (byte)(level == WheelSlipLevel.Slip ? epKind : WheelSlipKind.Unknown),
                OdoRate = winOdo,
                GyroRate = winGyro,
                Speed = winV,
                Threshold = winThr,
                EpisodeSec = level == WheelSlipLevel.Slip ? Trvani() : 0,
                EpisodeStart = level == WheelSlipLevel.Slip ? Rozpeti().Zacatek : default,
                OdoAngle = epOdo,
                GyroAngle = epGyro,
                Text = level == WheelSlipLevel.Slip ? Describe() : string.Empty,
                TimeStamp = lastEval,
            };
        }

        /// <summary>
        /// Druh podle prumernych rychlosti [rad/s] z kol a z gyra. Ktera strana „stoji", se rozhoduje
        /// pomerem, ne jen absolutne: 1. 10. 2026 se pri uvaznuti kola otocila o −49°, zatimco telo
        /// o +6° — to je prokluz (kola hrabou), ne „proti", ackoli znamenka nesouhlasi.
        /// </summary>
        public static WheelSlipKind KindOf(double odoRate, double gyroRate) => Druh(odoRate, gyroRate, 0.05);

        /// <summary>Druh skoncene epizody z integralu [rad] (prah „stoji" 2°).</summary>
        public static WheelSlipKind KindOfIntegrals(double odoAngle, double gyroAngle) => Druh(odoAngle, gyroAngle, 2 * Math.PI / 180);

        /// <summary>Spolecne pravidlo: „stoji" = pod <paramref name="malo"/> nebo pod petinou druhe strany.</summary>
        private static WheelSlipKind Druh(double o, double g, double malo)
        {
            double ao = Math.Abs(o), ag = Math.Abs(g);
            if (ao < Math.Max(malo, 0.2 * ag)) return WheelSlipKind.WheelsStill;
            if (ag < Math.Max(malo, 0.2 * ao)) return WheelSlipKind.WheelsMore;
            if (Math.Sign(o) != Math.Sign(g)) return WheelSlipKind.Opposite;
            return ao > ag ? WheelSlipKind.WheelsMore : WheelSlipKind.BodyMore;
        }

        /// <summary>Lidsky popis druhu (s diakritikou).</summary>
        public static string KindText(WheelSlipKind k)
        {
            switch (k)
            {
                case WheelSlipKind.WheelsStill: return "kola stojí, robot se otáčí (rukou? tlačení?)";
                case WheelSlipKind.Opposite: return "kola se točí proti otáčení robotu";
                case WheelSlipKind.WheelsMore: return "kola se točí víc než robot (prokluz, kola hrabou)";
                case WheelSlipKind.BodyMore: return "robot se otáčí víc, než hlásí kola (smyk)";
                default: return "kola nesouhlasí s gyrem";
            }
        }

        /// <summary>Text probihajici epizody pro stranku (s diakritikou), nebo prazdny.</summary>
        public string Describe()
        {
            if (level != WheelSlipLevel.Slip) return string.Empty;
            // Prumery jsou posledni PLATNE okno (po mezere v datech se drzi, NaN se nezobrazuje).
            return double.IsFinite(winOdo) && double.IsFinite(winGyro)
                ? string.Format(CultureInfo.InvariantCulture,
                    "PROKLUZ kol {0:F1} s: kola {1:F0} °/s, robot {2:F0} °/s — {3}",
                    Trvani(), winOdo * 180 / Math.PI, winGyro * 180 / Math.PI, KindText(epKind))
                : string.Format(CultureInfo.InvariantCulture, "PROKLUZ kol {0:F1} s — {1}", Trvani(), KindText(epKind));
        }

        /// <summary>
        /// Stav pro stranku v case <paramref name="at"/>: probihajici epizoda, nebo posledni skoncena
        /// v poslednich <see cref="WheelSlipConfig.RecentSec"/> sekundach, jinak prazdny.
        /// </summary>
        public WheelSlipReading Reading(DateTime at)
        {
            if (level == WheelSlipLevel.Slip)
                return new WheelSlipReading(level, epKind, Describe(), false);
            if (lastEnd != default && (at - lastEnd).TotalSeconds <= cfg.RecentSec)
            {
                string t = string.Format(CultureInfo.InvariantCulture,
                    "prokluz kol před {0:F0} s ({1:F1} s, rozdíl {2:F0}°) — {3}{4}",
                    Math.Max(0, (at - lastEnd).TotalSeconds), lastDur, (lastOdo - lastGyro) * 180 / Math.PI,
                    KindText(lastKind), lastReason != null && lastReason.StartsWith("rozdíl") ? string.Empty : " (" + lastReason + ")");
                return new WheelSlipReading(WheelSlipLevel.None, lastKind, t, true);
            }
            return new WheelSlipReading(WheelSlipLevel.None, WheelSlipKind.Unknown, string.Empty, false);
        }

        // ------------------------------------------------------------------ okna

        /// <summary>
        /// Prumer pres [a, b] s hodnotou drzenou do dalsiho vzorku (tak ji drzi i fuze). Neplatny,
        /// kdyz okno nepokryva vzorek s casem do <see cref="WheelSlipConfig.MaxGapSec"/> pred
        /// zacatkem nebo kdyz je uvnitr mezera vetsi nez MaxGapSec.
        /// </summary>
        private bool Mean(List<Vzorek> l, DateTime a, DateTime b, Func<Vzorek, double> val, out double mean)
        {
            mean = double.NaN;
            if (!Pokryti(l, a, b)) return false;
            double s = Integral(l, a, b, val);
            double dt = (b - a).TotalSeconds;
            if (!(dt > 0)) return false;
            mean = s / dt;
            return true;
        }

        private bool MeanOdo(DateTime a, DateTime b, out double w, out double v)
        {
            v = double.NaN;
            if (!Mean(odo, a, b, s => s.W, out w)) return false;
            v = Integral(odo, a, b, s => s.V) / (b - a).TotalSeconds;
            return true;
        }

        private bool Pokryti(List<Vzorek> l, DateTime a, DateTime b)
        {
            int k = Index(l, a);          // prvni vzorek s casem >= a
            // vzorek drzeny pres zacatek okna: posledni pred a (nebo prvni do MaxGap za a)
            DateTime prevT;
            if (k > 0) prevT = l[k - 1].T;
            else if (k < l.Count) prevT = l[k].T;
            else return false;
            if ((a - prevT).TotalSeconds > cfg.MaxGapSec) return false;   // drzeny vzorek je moc stary
            if (k == 0 && (prevT - a).TotalSeconds > cfg.MaxGapSec) return false;
            DateTime posledni = prevT;
            for (int i = k; i < l.Count && l[i].T <= b; i++)
            {
                DateTime p = i > 0 ? l[i - 1].T : l[i].T;
                if ((l[i].T - p).TotalSeconds > cfg.MaxGapSec) return false;
                posledni = l[i].T;
            }
            // Posledni vzorek do b musi byt cerstvy - mezera pres konec okna se jinak neprojevi.
            return (b - posledni).TotalSeconds <= cfg.MaxGapSec;
        }

        /// <summary>∫ hodnota dt nad [a, b], hodnota drzena do dalsiho vzorku.</summary>
        private static double Integral(List<Vzorek> l, DateTime a, DateTime b, Func<Vzorek, double> val)
        {
            if (l.Count == 0 || !(b > a)) return 0;
            int k = Index(l, a);
            if (k > 0) k--;
            double s = 0;
            for (; k < l.Count && l[k].T < b; k++)
            {
                DateTime t0 = l[k].T < a ? a : l[k].T;
                DateTime t1 = k + 1 < l.Count && l[k + 1].T < b ? l[k + 1].T : b;
                if (t1 > t0) s += val(l[k]) * (t1 - t0).TotalSeconds;
            }
            return s;
        }

        private static int Index(List<Vzorek> l, DateTime t)
        {
            int lo = 0, hi = l.Count;
            while (lo < hi) { int m = (lo + hi) / 2; if (l[m].T < t) lo = m + 1; else hi = m; }
            return lo;
        }
    }

    /// <summary>
    /// <b>Hlidac prokluzu kol</b> jako stupen na Streamu (vzor <see cref="StuckMonitor"/>): cte
    /// <c>IMUState</c> a <c>MotorStateBase</c>, skoncene epizody hlasi do Trace (skrcene),
    /// posila <see cref="WheelSlipMsg"/> do zaznamu a stranka nahledu cte stav z tehoz objektu
    /// (<see cref="Read"/>). Logika je v <see cref="WheelSlipDetector"/>.
    /// </summary>
    public sealed class WheelSlipMonitor : MessageProcessor
    {
        /// <summary>Po jak dlouhe dobe bez vyhodnoceni se probihajici stav na strance neukazuje [s].</summary>
        public const double StaleSec = 3.0;

        private readonly WheelSlipDetector detector;
        private readonly Action<string> report;
        private readonly object gate = new object();
        private readonly List<string> lines = new List<string>();

        /// <param name="config">Nastaveni; null = vychozi.</param>
        /// <param name="report">Kam hlasit radky; null = <c>Trace.WriteLine</c>.</param>
        public WheelSlipMonitor(WheelSlipConfig config = null, Action<string> report = null)
            // Gyro 100 Hz + kola 91 Hz + vse ostatni ze Streamu: fronta vetsi nez u hlidace uvaznuti.
            // Zahozeny vzorek se projevi jen jako mezera, ve ktere se nerozhoduje.
            : base(OverflowPolicy.DropOldest, 1024)
        {
            detector = new WheelSlipDetector(config);
            this.report = report ?? (s => System.Diagnostics.Trace.WriteLine(s));
        }

        /// <summary>Nastaveni.</summary>
        public WheelSlipConfig Config => detector.Config;

        /// <summary>
        /// Zpracuje zpravu synchronne (pro testy a prehravani bez vlakna); vraci <see cref="WheelSlipMsg"/>,
        /// je-li co poslat. Radky jdou do Trace (mimo zamek).
        /// </summary>
        public WheelSlipMsg Process(Message msg)
        {
            WheelSlipMsg outMsg;
            string[] toReport = null;
            lock (gate)
            {
                lines.Clear();
                outMsg = detector.Feed(msg, lines);
                if (lines.Count > 0) toReport = lines.ToArray();
            }
            // Mimo zamek - hlaseni muze tect do streamu (TraceInfoBridge).
            if (toReport != null) foreach (var l in toReport) report(l);
            return outMsg;
        }

        /// <summary>
        /// Stav pro stranku v case <paramref name="now"/>. Probihajici prokluz se neukazuje, kdyz
        /// hlidac dele nez <see cref="StaleSec"/> nic nevyhodnotil (chybi data - stare cislo by lhalo).
        /// </summary>
        public WheelSlipReading Read(DateTime now)
        {
            lock (gate)
            {
                var r = detector.Reading(now);
                var t = detector.LastEval;
                if (!r.Recent && (t == default || (now - t).TotalSeconds > StaleSec))
                    return new WheelSlipReading(WheelSlipLevel.None, WheelSlipKind.Unknown, string.Empty, false);
                return r;
            }
        }

        /// <summary>Stav ted (<see cref="TimeBase.Now"/>).</summary>
        public WheelSlipReading Read() => Read(TimeBase.Now);

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (msg is WheelSlipMsg) return;   // vlastni vystup tece zpet ze Streamu
            var m = Process(msg);
            if (m != null) EmitDerived(m);
        }

        /// <summary>
        /// Zastaveni: prokluz, ktery prave trva, se ukonci a ohlasi do Trace, i odlozeny souhrn
        /// (koncova zprava do zaznamu uz nejde - Stream se zastavuje).
        /// </summary>
        protected override void OnStopped()
        {
            string[] toReport = null;
            lock (gate)
            {
                lines.Clear();
                detector.Flush(lines, "runtime se zastavuje");
                if (lines.Count > 0) toReport = lines.ToArray();
            }
            if (toReport != null) foreach (var l in toReport) report(l);
        }
    }
}
