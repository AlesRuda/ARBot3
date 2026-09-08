using System;
using System.Diagnostics;
using System.Globalization;
using ARBot.Common.Calibration;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// <b>Mise magcal</b> — robot STOJI a meri si vlastni magnetickou kalibraci, kdyz s nim
    /// obsluha otaci rukou. Sourozenec <see cref="FreeRunMission"/>, ale nejjednodussi z misi:
    /// nema cil, nema mapu a <b>neprodukuje mrkev</b>.
    ///
    /// <para><b>Proc mise a ne prepinac.</b> Mise se vylucuji (viz CLAUDE.md), takze se
    /// nevybiraji booleovskymi prepinaci. A jako mise to navic <b>ubira</b> praci:
    /// <see cref="PhaseText"/> JE ten zivy ukazatel pokryti (stranka stav mise uz kresli),
    /// zaznam se rozjede volbou mise, a <see cref="IRegulatorHolder"/> da konstrukcni zaruku,
    /// ze se robot nerozjede.</para>
    ///
    /// <para>⚠️ <b>Bezpecnostni invariant:</b> po <see cref="StartMission"/> je
    /// <c>holder.Regulator == null</c> a mise ho <b>nikdy</b> nenastavi. Hlida to test
    /// <c>PriStartu_JeRegulatorZahozeny</c>.</para>
    ///
    /// <para>Pozor na jmena: <see cref="StartMission"/>, ne <c>Start()</c> — to by kolidovalo se
    /// zdedenou metodou <c>MessageTarget</c>, ktera spousti vlakno stupne (past uz zapsana
    /// u Robotouru).</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public sealed class MagCalMission : MessageProcessor, IMissionStatus
    {
        private readonly IMagCalControl control;
        private readonly IRegulatorHolder holder;
        private readonly TimeSpan? fitPeriod;
        private MagCalCollector collector;
        private DateTime firstSampleAt, lastSampleAt;

        public MagCalMission(IMagCalControl control, IRegulatorHolder holder,
                             TimeSpan? fitPeriod = null, int queueCapacity = 4)
            : base(OverflowPolicy.DropOldest, queueCapacity)
        {
            this.control = control ?? throw new ArgumentNullException(nameof(control));
            this.holder = holder ?? throw new ArgumentNullException(nameof(holder));
            this.fitPeriod = fitPeriod;
        }

        /// <summary>Faze automatu.</summary>
        public MagCalPhase Phase { get; private set; } = MagCalPhase.Idle;

        /// <summary>
        /// Referencni <c>|B|</c> [G] z registru 21; nula, dokud mise nezacala. <b>Cte se ze
        /// senzoru</b>, nikde se nepise natvrdo — na tuhle hodnotu se normuje prolozeni.
        /// </summary>
        public double BRefG { get; private set; }

        /// <summary>Registr 23 pri zacatku mise (dvanact cisel), nebo prazdne.</summary>
        public string Reg23Before { get; private set; } = string.Empty;

        /// <summary>Pokryti mericich smeru; <c>null</c>, dokud mise nezacala.</summary>
        public MagCalCoverage Coverage => collector?.Coverage;

        /// <summary>Posledni prolozeni; <c>null</c>, dokud soustava neni urcena.</summary>
        public MagCalResult LastResult => collector?.LastResult;

        /// <summary>Je vysledek pouzitelny k zapisu?</summary>
        public bool Usable => collector?.Usable ?? false;

        /// <inheritdoc/>
        public string MissionName => "magcal";

        /// <inheritdoc/>
        public string PhaseText => Phase switch
        {
            MagCalPhase.Idle => "Necinna",
            MagCalPhase.NoReference => "NEZACALA: registr 21 (referencni pole) se nepodarilo"
                                       + " precist. Bez nej by se prokladalo proti dohadu.",
            MagCalPhase.Written => "Kalibrace zapsana do senzoru a ulozena."
                                   + " Pockej ~2 minuty, nez se kurz srovna.",
            _ => collector?.Verdict ?? "POKRACUJ: jeste zadna data",
        };

        /// <inheritdoc/>
        public MissionWait WaitingFor
            => Phase == MagCalPhase.Collecting || Phase == MagCalPhase.Ready
               ? MissionWait.MagCoverage
               : MissionWait.None;

        /// <inheritdoc/>
        public TimeSpan Elapsed
            => firstSampleAt == default || lastSampleAt <= firstSampleAt
               ? TimeSpan.Zero
               : lastSampleAt - firstSampleAt;

        /// <summary>
        /// Zacatek mise: <b>zahodi regulator</b>, precte registry a zapne palubni HSI jako
        /// nezavislou kontrolu.
        /// </summary>
        public void StartMission()
        {
            // Bezpecnost PRVNI: kdyby cokoli niz vyhodilo vyjimku, robot uz stoji.
            holder.Regulator = null;

            var reg21 = control.ReadRegister(IMagCalControl.RegReference);
            if (reg21 == null || reg21.Length < 3)
            {
                // ⚠️ Radeji stat a rict to, nez merit proti dohadu: bez referencniho |B| by
                // prolozeni normovalo na nahodne cislo a VPE by kalibraci stejne neuveril.
                Phase = MagCalPhase.NoReference;
                Trace.WriteLine("MagCal: registr 21 se nepodarilo precist -> mise NEZACINA."
                                + " Bez referencniho |B| by prolozeni normovalo proti dohadu.");
                return;
            }

            // Prvni tri slozky registru 21 jsou referencni vektor magnetickeho pole.
            BRefG = Math.Sqrt(reg21[0] * reg21[0] + reg21[1] * reg21[1] + reg21[2] * reg21[2]);
            if (!(BRefG > 0))
            {
                Phase = MagCalPhase.NoReference;
                Trace.WriteLine("MagCal: registr 21 hlasi nulove referencni pole -> mise NEZACINA.");
                return;
            }
            Trace.WriteLine($"MagCal: referencni |B| z registru 21 = {BRefG:F4} G.");

            var reg23 = control.ReadRegister(IMagCalControl.RegCompensation);
            Reg23Before = reg23 == null
                ? string.Empty
                : string.Join(",", Array.ConvertAll(reg23,
                    v => v.ToString("F6", CultureInfo.InvariantCulture)));
            if (reg23 == null)
                Trace.WriteLine("MagCal: registr 23 se nepodarilo precist - stav 'pred' nebude"
                                + " v zaznamu. Mereni to ale neblokuje (jede se ze suroveho pole).");

            if (!control.SetOnboardHsi(true))
                Trace.WriteLine("MagCal: registr 44 se nepodarilo zapnout - nezavisla kontrola"
                                + " z registru 47 nebude k dispozici.");

            collector = new MagCalCollector(BRefG, fitPeriod) { Reg23Before = Reg23Before };
            Phase = MagCalPhase.Collecting;
            Trace.WriteLine("MagCal: sber zapnut. Robot STOJI - otacej s nim rukou podle pokynu"
                            + " na strance.");
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (collector == null) return;
            if (!(msg is IMUState imu)) return;
            if (!collector.Add(imu)) return;

            if (firstSampleAt == default) firstSampleAt = imu.TimeStamp;
            lastSampleAt = imu.TimeStamp;

            // Faze se prepina obema smery: kdyz obsluha robotem hne tak, ze se pokryti nebo
            // prolozeni zhorsi, nesmi stranka dal tvrdit HOTOVO.
            if (Phase == MagCalPhase.Collecting && collector.Usable) Phase = MagCalPhase.Ready;
            else if (Phase == MagCalPhase.Ready && !collector.Usable) Phase = MagCalPhase.Collecting;

            var zprava = collector.ToLogMessage();
            zprava.Phase = (int)Phase;
            EmitDerived(zprava);
        }

        /// <summary>
        /// <b>Zapis kalibrace do senzoru</b> — jen na pokyn cloveka (tlacitko na strance pod
        /// drzenym nouzovym zastavenim). Vraci <c>false</c>, kdyz zapsat nelze, a <b>rika proc</b>
        /// do <see cref="Trace"/>.
        ///
        /// <para>⚠️ Gate na pokryti a verdikt je <b>tady</b>, ne jen v UI: skryte tlacitko neni
        /// pojistka.</para>
        /// </summary>
        public bool WriteToSensor()
        {
            if (collector == null)
            {
                Trace.WriteLine("MagCal: zapis odmitnut - mise nezacala.");
                return false;
            }
            if (!collector.Usable)
            {
                Trace.WriteLine("MagCal: zapis odmitnut - " + collector.Verdict);
                return false;
            }

            string cisla = collector.LastResult.ToVnwrg23();
            if (!control.WriteMagCompensation(cisla))
            {
                Trace.WriteLine("MagCal: zapis registru 23 SELHAL - do flash se neuklada.");
                return false;
            }
            if (!control.SaveToFlash())
            {
                Trace.WriteLine("MagCal: registr 23 zapsan, ale ULOZENI DO FLASH SELHALO -"
                                + " po vypnuti senzoru bude kalibrace pryc.");
                return false;
            }

            // Palubni HSI uz nema co delat - vypnout, at nebezi zbytecne.
            control.SetOnboardHsi(false);

            Phase = MagCalPhase.Written;
            Trace.WriteLine("MagCal: kalibrace zapsana a ulozena: " + cisla);
            Trace.WriteLine("MagCal: ⚠️ trvalost overi JEN vypnuti a zapnuti robota.");
            return true;
        }
    }
}
