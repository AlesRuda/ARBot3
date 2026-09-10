using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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
        /// Prolozeni <b>samotne koule</b> — jen tvrde zelezo; <c>null</c>, dokud se neurci.
        ///
        /// <para>Ma dvoji ulohu. Za prve <b>diagnostickou</b>: kdyz koule sedi a elipsoida ne,
        /// je pole konzistentni a chybi jen naklon; kdyz nesedi ani koule, menilo se behem
        /// mereni pole a otaceni nepomuze. Za druhe je to <b>pouzitelny vysledek</b>, ktery si
        /// obsluha muze odvezt z pole, kdyz na plnou kalibraci nedoslo.</para>
        /// </summary>
        public MagCalResult HardIronOnly { get; private set; }

        /// <summary>
        /// Podminenost z posledniho pokusu o prolozeni — <b>vyplnena i kdyz se neprolozilo</b>,
        /// protoze prave to je cislo, kterym se obsluze rika, jak daleko od hotova je.
        /// </summary>
        public double LastCondition { get; private set; } = double.PositiveInfinity;

        /// <summary>Otoceni nasbirane integraci gyra [rad] — diagnostika.</summary>
        public double IntegratedYawRad => yaw;

        /// <summary>
        /// Je vysledek pouzitelny k zapisu do senzoru? <b>Vsechny</b> podminky zaroven: pokryti
        /// uplne (vcetne naklonu na obe strany), prolozeni urcene, <c>sd(|B|)</c> pod prahem,
        /// pole se behem mereni nemenilo.
        ///
        /// <para>⚠️ <b>Rozptyl sklonu od 10. 9. 2026 branou NENI</b> — rozhodnuti autora nad
        /// zaznamem <c>20260910-170809.rec</c>, kde shodil jinak bezvadnou kalibraci (2,09°
        /// proti prahu 0,5°). Sklon se sklapi surovym akcelerometrem, a ten pri otaceni robotem
        /// rukou meri dynamiku, ne jen gravitaci: rozptyl rostl s odchylkou <c>|acc|</c> od
        /// klidu (1,1° → 4,2°), podlaha v uplnem klidu byla 0,42–0,46° a akcelerometr ma bias
        /// 0,27 m/s² v ose Z. Merilo se tim tedy neco jineho nez magnetometr. Cislo se dal
        /// pocita a nese do zpravy jako <b>diagnostika</b>. Viz doc/decisions.md.</para>
        ///
        /// <para>Shodu prvni a druhe poloviny dat kontroluje <b>offline</b> report — za behu by
        /// to znamenalo tri SVD misto jednoho a je to jen doplnkove kriterium.</para>
        /// </summary>
        public bool Usable
            => LastResult != null
               && coverage.Complete
               && LastResult.Condition <= MagCalThresholds.MaxCondition
               && LastResult.SdMagnitudeG <= MagCalThresholds.MaxSdMagnitudeG
               && !FieldChanged;

        /// <summary>
        /// <b>Menilo se behem mereni pole?</b> Pozna se na tom, ze data nelezi ani na kouli.
        ///
        /// <para>Je to jedina vada, kterou <b>nespravi zadne otaceni</b> — robot musi stat na
        /// jednom miste dal od kovu. Proto ma ve verdiktu prednost pred pokynem k otaceni.</para>
        ///
        /// <para>⚠️ Nerozhoduje se, dokud koule neni urcena: na zacatku sberu (rovina, malo
        /// vzorku) by to bylo tvrzeni z niceho.</para>
        /// </summary>
        public bool FieldChanged
            => HardIronOnly != null
               && HardIronOnly.SdMagnitudeG > MagCalThresholds.MaxSphereSdMagnitudeG;

        /// <summary>
        /// Da se zapsat aspon kalibrace tvrdeho zeleza? <b>Slabsi brana nez</b>
        /// <see cref="Usable"/> — nezada pokryti naklonu ani urcenost elipsoidy, ale porad
        /// zada, aby data lezela na kouli.
        /// </summary>
        public bool CanWriteHardIron => HardIronOnly != null && !FieldChanged;

        /// <summary>
        /// <b>Verdikt pro cloveka</b>: co udelat dal, nebo ze je hotovo.
        ///
        /// <para>Poradi je zamerne — nejdriv <b>pokyn</b> (co chybi v pokryti), pak diagnoza
        /// (podminenost, zbytky). Obsluha stoji u robota a potrebuje vedet, co ma delat.</para>
        ///
        /// <para>⚠️ <b>Kazda vetev musi koncit tim, co ma clovek UDELAT.</b> Puvodne tu byla
        /// veta „podminenost 80 (prah 10000) — otacej dal a pridej naklon", ktera si protirecila
        /// (80 je hluboko pod prahem) a radila jedinou vec, ktera pomoct nemohla. Stalo to
        /// obsluze cely vyjezd 10. 9. 2026: pokryti bylo kompletni, prolozeni presto selhalo
        /// a stranka porad rikala „otacej". Rozliseni prinesla teprve <b>koule</b>
        /// (<see cref="HardIronOnly"/>) — viz doc/plan-vn100-kalibrace.md.</para>
        /// </summary>
        public string Verdict
        {
            get
            {
                if (coverage.Mag.Count < MagCalFit.MinSamples) return "POKRACUJ: jeste malo vzorku";

                // PRVNI, jeste pred pokrytim: tuhle vadu neopravi zadne otaceni, takze poslat
                // cloveka otacet by byla ztrata casu.
                if (FieldChanged)
                    return string.Format(CultureInfo.InvariantCulture,
                        "ZNOVU: pole se behem mereni menilo (rozptyl {0:F3} G, prah {1:F3})."
                        + " Postav robota na JEDNO misto dal od kovu (auto, plot, armatura)"
                        + " a zacni znovu.",
                        HardIronOnly.SdMagnitudeG, MagCalThresholds.MaxSphereSdMagnitudeG);

                string chybi = coverage.MissingText();
                if (chybi.Length > 0) return "POKRACUJ: " + chybi;

                if (LastResult == null)
                    // Pokryti je kompletni a prolozeni presto neni. Zbyva jedina vec, kterou
                    // ma smysl radit: VIC naklonu - a rict, ze tvrde zelezo uz zmerene je.
                    return HardIronOnly != null
                        ? "POKRACUJ: tvrde zelezo zmereno, mekke jeste ne."
                          + " Podloz robota VYS (25-30 stupnu) nebo na dalsi stranu."
                          + " Muzes taky zapsat jen tvrde zelezo."
                        : "POKRACUJ: data zatim nelezi na kouli - otacej dal a nakloň robota.";
                if (LastResult.SdMagnitudeG > MagCalThresholds.MaxSdMagnitudeG)
                    return string.Format(CultureInfo.InvariantCulture,
                        "NEPOUZITELNE: sd(|B|) {0:F4} G nad prahem {1:F3} - pole je porad nekonzistentni."
                        + " Postav robota na JEDNO misto dal od kovu a zacni znovu.",
                        LastResult.SdMagnitudeG, MagCalThresholds.MaxSdMagnitudeG);
                // Rozptyl sklonu tu vetev NEMA - od 10. 9. 2026 je jen diagnostika (viz Usable).
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

                // Koule se proklada VZDY, ne jen kdyz elipsoida selze: jeji zbytek je to,
                // cim se pozna menici se pole, a to je vada i tehdy, kdyz elipsoida vyjde.
                HardIronOnly = MagCalFit.TryFitSphere(coverage.Mag, BRefG, out var koule, out _,
                                                      coverage.Acc)
                    ? koule : null;
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
            Grid = coverage.Grid().Select(r => r.Counts).ToArray(),
            CurrentRow = coverage.CurrentRow,
            CurrentAzimuthBin = coverage.CurrentAzimuthBin,
            CurrentTiltDeg = coverage.CurrentTiltDeg,
            SphereSdMagnitudeG = HardIronOnly?.SdMagnitudeG ?? double.NaN,
            SphereVnwrg23 = HardIronOnly?.ToVnwrg23() ?? string.Empty,
            CanWriteHardIron = CanWriteHardIron,
            BRefG = BRefG,
            Reg23Before = Reg23Before ?? string.Empty,
            TimeStamp = tPrev ?? default,
        };
    }
}
