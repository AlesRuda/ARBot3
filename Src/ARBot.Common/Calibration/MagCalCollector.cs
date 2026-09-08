using System;
using System.Diagnostics;
using System.Globalization;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Calibration
{
    /// <summary>
    /// <b>Sberac kalibrace magnetometru.</b> Vzor <c>PerfCollector</c> → <c>PerfMsg</c>: sbira ze
    /// streamu a jednou za interval vyrobi zpravu metodou <see cref="ToLogMessage"/> (konvenci
    /// projektu vlastni konverzi domena, <c>Message</c> zustava pasivni DTO).
    ///
    /// <para><b>Cas z hodin DAT, ne stroje</b> — integrace gyra i kadence prolozeni se ridi
    /// razitky zprav, takze pri prehravani zaznamu a v testech znamena totez jako za behu.
    /// Stejna zasada jako <c>IMissionStatus.Elapsed</c>.</para>
    ///
    /// <para>⚠️ <b>Prolozeni se NEPOCITA pro kazdy vzorek</b> — je to SVD nad matici <c>n×10</c>.
    /// Jednou za <see cref="FitPeriod"/> a jen kdyz od posledne pribyla data.</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public sealed class MagCalCollector
    {
        /// <summary>Mezera v datech, pres kterou se uz uhlova rychlost neintegruje [s].</summary>
        private const double MaxGapSec = 0.5;

        private readonly MagCalCoverage coverage = new MagCalCoverage();
        private double yaw;
        private DateTime? tPrev;
        private DateTime tNextFit = DateTime.MinValue;
        private int samplesAtLastFit;

        public MagCalCollector(double bRefG, TimeSpan? fitPeriod = null)
        {
            if (!(bRefG > 0)) throw new ArgumentOutOfRangeException(nameof(bRefG));
            BRefG = bRefG;
            FitPeriod = fitPeriod ?? TimeSpan.FromSeconds(1);
        }

        /// <summary>Referencni <c>|B|</c> [G] z registru 21.</summary>
        public double BRefG { get; }

        /// <summary>Jak casto se prepocitava prolozeni.</summary>
        public TimeSpan FitPeriod { get; }

        /// <summary>Stav registru 23 pri zacatku mise (dvanact cisel) — jen se nese do zpravy.</summary>
        public string Reg23Before { get; set; }

        public MagCalCoverage Coverage => coverage;

        /// <summary>Posledni uspesne prolozeni; <c>null</c>, dokud soustava neni urcena.</summary>
        public MagCalResult LastResult { get; private set; }

        /// <summary>
        /// Podminenost z posledniho pokusu o prolozeni — <b>vyplnena i kdyz se neprolozilo</b>,
        /// protoze prave to je cislo, kterym se obsluze rika, jak daleko od hotova je.
        /// </summary>
        public double LastCondition { get; private set; } = double.PositiveInfinity;

        /// <summary>Otoceni nasbirane integraci gyra [rad] — diagnostika.</summary>
        public double IntegratedYawRad => yaw;

        /// <summary>
        /// Je vysledek pouzitelny k zapisu do senzoru? <b>Vsechny</b> podminky zaroven: pokryti
        /// uplne (vcetne naklonu na obe strany), prolozeni urcene, zbytky pod prahy.
        ///
        /// <para>Shodu prvni a druhe poloviny dat kontroluje <b>offline</b> report — za behu by
        /// to znamenalo tri SVD misto jednoho a je to jen doplnkove kriterium.</para>
        /// </summary>
        public bool Usable
            => LastResult != null
               && coverage.Complete
               && LastResult.Condition <= MagCalThresholds.MaxCondition
               && LastResult.SdMagnitudeG <= MagCalThresholds.MaxSdMagnitudeG
               // NaN (bez akcelerometru) se nepocita jako prekroceni prahu, ale bez gravitace
               // se sem stejne nedostaneme - kose ji vyzaduji.
               && !(LastResult.SdInclinationDeg > MagCalThresholds.MaxSdInclinationDeg);

        /// <summary>
        /// <b>Verdikt pro cloveka</b>: co udelat dal, nebo ze je hotovo.
        ///
        /// <para>Poradi je zamerne — nejdriv <b>pokyn</b> (co chybi v pokryti), pak diagnoza
        /// (podminenost, zbytky). Obsluha stoji u robota a potrebuje vedet, co ma delat.</para>
        /// </summary>
        public string Verdict
        {
            get
            {
                string chybi = coverage.MissingText();
                if (chybi.Length > 0) return "POKRACUJ: " + chybi;
                if (LastResult == null)
                    return coverage.Mag.Count < MagCalFit.MinSamples
                        ? "POKRACUJ: jeste malo vzorku"
                        : string.Format(CultureInfo.InvariantCulture,
                            "POKRACUJ: podminenost {0:G4} (prah {1:G4}) - otacej dal a pridej naklon",
                            LastCondition, MagCalThresholds.MaxCondition);
                if (LastResult.SdMagnitudeG > MagCalThresholds.MaxSdMagnitudeG)
                    return string.Format(CultureInfo.InvariantCulture,
                        "NEPOUZITELNE: sd(|B|) {0:F4} G nad prahem {1:F3} - pole je porad nekonzistentni",
                        LastResult.SdMagnitudeG, MagCalThresholds.MaxSdMagnitudeG);
                if (LastResult.SdInclinationDeg > MagCalThresholds.MaxSdInclinationDeg)
                    return string.Format(CultureInfo.InvariantCulture,
                        "NEPOUZITELNE: sd(sklonu) {0:F2}° nad prahem {1:F1}",
                        LastResult.SdInclinationDeg, MagCalThresholds.MaxSdInclinationDeg);
                return "HOTOVO";
            }
        }

        /// <summary>
        /// Prida vzorek. Vraci <c>true</c>, kdyz se prave prepocitalo prolozeni — tedy kdy ma
        /// smysl poslat zpravu.
        /// </summary>
        public bool Add(IMUState imu)
        {
            if (imu == null) return false;

            // ⚠️ V robotu je IMU vic a T265 posila RELATIVNI yaw. Michat dve ruzne nuly by dalo
            // nesmysl - stejny duvod jako ve Vn100Report.
            if (!imu.HasAbsoluteHeading) return false;

            var pole = imu.MagnetometerRaw ?? imu.Magnetometer;
            if (pole == null || imu.Acceleration == null || imu.AngularVelocity == null) return false;

            if (tPrev.HasValue)
            {
                double dt = (imu.TimeStamp - tPrev.Value).TotalSeconds;
                // Mezera v datech: integrace by pres ni nasbirala otoceni, ktere se nestalo.
                if (dt > 0 && dt < MaxGapSec) yaw += imu.AngularVelocity.Value.Z * dt;
            }
            tPrev = imu.TimeStamp;

            coverage.Add(yaw, pole.Value, imu.Acceleration.Value);

            if (imu.TimeStamp < tNextFit || coverage.Mag.Count == samplesAtLastFit) return false;
            tNextFit = imu.TimeStamp + FitPeriod;
            samplesAtLastFit = coverage.Mag.Count;

            if (coverage.Mag.Count < MagCalFit.MinSamples) return false;
            try
            {
                bool ok = MagCalFit.TryFit(coverage.Mag, BRefG, out var r, out double cond,
                                           coverage.Acc);
                LastCondition = cond;
                LastResult = ok ? r : null;
                return true;
            }
            catch (ArgumentException ex)
            {
                // Diagnostika do Trace, ne Debug: v Release na zarizeni by po poruse nezustala
                // zadna stopa (pravidlo projektu).
                Trace.WriteLine("MagCal: prolozeni selhalo - " + ex.Message);
                return false;
            }
        }

        /// <inheritdoc cref="MagCalMsg"/>
        public MagCalMsg ToLogMessage() => new MagCalMsg
        {
            Condition = LastResult?.Condition ?? LastCondition,
            SdMagnitudeG = LastResult?.SdMagnitudeG ?? 0,
            SdInclinationDeg = LastResult?.SdInclinationDeg ?? double.NaN,
            Vnwrg23 = LastResult?.ToVnwrg23() ?? string.Empty,
            Verdict = Verdict,
            MissingText = coverage.MissingText(),
            FilledAzimuthBins = coverage.FilledAzimuthBins,
            TiltGroups = coverage.TiltGroups,
            TiltedGroups = coverage.TiltedGroups,
            HasOppositeTilts = coverage.HasOppositeTilts,
            Samples = coverage.Mag.Count,
            BRefG = BRefG,
            Reg23Before = Reg23Before ?? string.Empty,
            TimeStamp = tPrev ?? default,
        };
    }
}
