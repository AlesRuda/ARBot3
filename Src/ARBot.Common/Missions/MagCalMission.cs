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
        private bool hsiBezi;

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

        /// <summary>Da se zapsat aspon tvrde zelezo? Slabsi brana nez <see cref="Usable"/>.</summary>
        public bool CanWriteHardIron => collector?.CanWriteHardIron ?? false;

        /// <summary>
        /// Byl posledni zapis jen <b>castecny</b> (tvrde zelezo)? Rozhoduje o tom, co se rekne
        /// obsluze — „hotovo" a „hotovo z poloviny" nesmi vypadat stejne.
        /// </summary>
        public bool WrittenHardIronOnly { get; private set; }

        /// <inheritdoc/>
        public string MissionName => "magcal";

        /// <inheritdoc/>
        public string PhaseText => Phase switch
        {
            MagCalPhase.Idle => "Necinna",
            MagCalPhase.NoReference => "NEZACALA: registr 21 (referencni pole) se nepodarilo"
                                       + " precist. Bez nej by se prokladalo proti dohadu.",
            MagCalPhase.Written when WrittenHardIronOnly
                => "Zapsano JEN TVRDE ZELEZO - mekke zustava neopravene, takze se chyba kurzu"
                   + " zmensi, ale nezmizi. Pockej ~2 minuty, nez se kurz srovna.",
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

            // ⚠️ RESET PRVNI, teprve pak Run (TN002 kap. 4.1, krok 1). Podle ICD registru 44 se
            // pri prechodu Run -> Off reseni NEMAZE a dalsi Run pokracuje ze stareho - takze bez
            // resetu by registr 47 nesl vysledek z minule mise a jako "nezavisla kontrola"
            // naseho prolozeni by nefungoval.
            if (!control.ResetOnboardHsi())
                Trace.WriteLine("MagCal: registr 44 se nepodarilo vyresetovat - v registru 47"
                                + " muze zustat reseni z minule mise, neber ho jako nezavislou"
                                + " kontrolu.");
            if (!control.SetOnboardHsi(true))
                Trace.WriteLine("MagCal: registr 44 se nepodarilo zapnout - nezavisla kontrola"
                                + " z registru 47 nebude k dispozici.");
            hsiBezi = true;

            collector = new MagCalCollector(BRefG, fitPeriod) { Reg23Before = Reg23Before };
            Phase = MagCalPhase.Collecting;
            Trace.WriteLine("MagCal: sber zapnut. Robot STOJI - otacej s nim rukou podle pokynu"
                            + " na strance.");
        }

        /// <summary>
        /// Ukonceni mise — <b>vzdy vypne palubni HSI</b>, i kdyz se nic nezapsalo.
        ///
        /// <para>⚠️ Tohle NENI kosmetika. TN002 kap. 5.2 uvadi „Mode field in Register 44 is set
        /// to Run" primo mezi <b>pricinami ujizdejiciho kurzu</b> a rika, ze mimo kalibraci ma
        /// byt registr 44 vzdy vypnuty. Do 10. 9. 2026 se vypinal jen na ceste po uspesnem
        /// zapisu, takze <b>nedokoncena mise nechala senzor v Run</b> — a nedokoncena mise je
        /// prave to, co se v poli stalo.</para>
        ///
        /// <para>Vypina se PRED zastavenim stupne: kdyby <c>base.Stop()</c> vyhodilo vyjimku,
        /// senzor uz je v poradku.</para>
        /// </summary>
        public override void Stop()
        {
            VypniHsi();
            base.Stop();
        }

        /// <summary>Vypne palubni HSI, jen kdyz jsme ho sami zapnuli. Idempotentni.</summary>
        private void VypniHsi()
        {
            if (!hsiBezi) return;
            hsiBezi = false;
            if (!control.SetOnboardHsi(false))
                Trace.WriteLine("MagCal: registr 44 se nepodarilo VYPNOUT - senzor zustava"
                                + " v rezimu Run, coz podle TN002 zhorsuje kurz. Vypni ho rucne"
                                + " ($VNWRG,44,0,1,5) nebo restartuj senzor.");
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

            return Zapis(collector.LastResult.ToVnwrg23(), castecna: false);
        }

        /// <summary>
        /// <b>Zapis kalibrace JEN TVRDEHO ZELEZA</b> (z prolozeni koule) — pro pripad, kdy na
        /// plnou kalibraci nedoslo, ale obsluha si z pole chce neco odvezt.
        ///
        /// <para>⚠️ <b>Neni to nahrada plne kalibrace.</b> Odstrani prvni harmonickou chyby
        /// kurzu, druhou (mekke zelezo) ne — a <b>i ten odhad tvrdeho zeleza je vychyleny</b>
        /// neopravenym mekkym zelezem. Zmereno 10. 9. 2026: odstrani se 100 % bez mekkeho
        /// zeleza, ~92 % pri mirnem, ~80 % pri tom z referencniho exportu senzoru a jen
        /// <b>38 % pri patologickem</b> (1,5/1,0/0,8). Tomu musi odpovidat i text na strance:
        /// nabizi se to jako lepsi nez nic, ne jako hotovo.</para>
        ///
        /// <para>Brana je slabsi nez u <see cref="WriteToSensor"/> (nezada pokryti naklonu ani
        /// urcenou elipsoidu), ale <b>porad to brana je</b>: data musi lezet na kouli, jinak by
        /// se zapsal nesmysl z rovinne rotace.</para>
        /// </summary>
        public bool WriteHardIronOnly()
        {
            if (collector == null)
            {
                Trace.WriteLine("MagCal: zapis tvrdeho zeleza odmitnut - mise nezacala.");
                return false;
            }
            if (!collector.CanWriteHardIron)
            {
                Trace.WriteLine("MagCal: zapis tvrdeho zeleza odmitnut - " + collector.Verdict);
                return false;
            }

            return Zapis(collector.HardIronOnly.ToVnwrg23(), castecna: true);
        }

        /// <summary>
        /// Samotny zapis do registru 23 a do flash — spolecny pro plnou i castecnou kalibraci.
        ///
        /// <para>Poradi je podstatne: <b>nejdriv registr, pak flash</b>, a kdyz flash selze,
        /// rekne se to nahlas — kalibrace je pak v senzoru jen do vypnuti.</para>
        /// </summary>
        private bool Zapis(string cisla, bool castecna)
        {
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
            VypniHsi();

            Phase = MagCalPhase.Written;
            WrittenHardIronOnly = castecna;
            Trace.WriteLine((castecna ? "MagCal: zapsano JEN TVRDE ZELEZO: "
                                      : "MagCal: kalibrace zapsana a ulozena: ") + cisla);
            if (castecna)
                Trace.WriteLine("MagCal: ⚠️ mekke zelezo zustava neopravene - chyba kurzu"
                                + " se zmensi, ale nezmizi.");
            Trace.WriteLine("MagCal: ⚠️ trvalost overi JEN vypnuti a zapnuti robota.");
            return true;
        }
    }
}
