using System;
using System.Diagnostics;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Runtime;
using ARBot.HAL;

namespace ARBot.Robot
{
    /// <summary>
    /// <b>Nastavi VN100 model magnetickeho pole podle skutecne polohy robota</b> — jednorazove,
    /// po prvnim fixu GPS, ktery projde branou kvality.
    ///
    /// <para><b>Nacpak.</b> Bez modelu drzi VN referencni pole natvrdo v registru 21 a na robotu
    /// tam je <c>(0,234; 0; 0,4212)</c>, tedy <b>vychodni slozka nula</b> (bez deklinace) a sklon
    /// <b>60,9°</b>, ackoli pro CR je ~<b>65,7°</b>. Ma to dva nasledky:</para>
    /// <list type="number">
    /// <item><b>Kurz je magneticky, ne k pravemu severu</b> — u nas o ~5° jinak, kdezto kurz
    ///   z GPS je k pravemu severu. Fuze tedy michala dve referencie, ktere se systematicky
    ///   nesouhlasi.</item>
    /// <item><b>VPE porovnava mereny sklon proti te referenci</b> a pri nesouhlasu magnetometr
    ///   adaptivne utlumi. Kalibrace udela <c>|B|</c> a sklon KONSTANTNI, ale nezmeni, ze ta
    ///   konstanta je o 5° mimo referenci — takze i po perfektni kalibraci muze VPE magnetometr
    ///   dal castecne dusit. ⚠️ <b>Tohle je hypoteza, ne zjisteni:</b> jak silne VPE na
    ///   KONSTANTNI odchylku sklonu reaguje, z dokumentace vycist nejde a zmerit se to da jedine
    ///   na senzoru.</item>
    /// </list>
    ///
    /// <para><b>Zamerne BEZ <c>VNWNV</c>.</b> Zapis do registru je nestaly, ale to je tady
    /// vyhoda: nastavi se pri kazdem behu podle <b>aktualni</b> polohy a data (pole se s casem
    /// meni), takze neni co udrzovat ve flash a nesaha se na pravidlo „ulozeni do flash je
    /// vedomy rucni krok" (viz <c>deploy/vnrestore.sh</c>).</para>
    ///
    /// <para>⚠️ <b>Po nastaveni se kurz skokem zmeni o deklinaci</b> a VPE se na nove referencni
    /// pole dotahuje ~100–170 s (zmereno 6. 9. 2026, viz doc/imu-and-frames.md). Proto se to dela
    /// co nejdriv — pri prvnim dobrem fixu, ne az za jizdy.</para>
    ///
    /// <para>Kvalita fixu se posuzuje <see cref="DefaultMeasurementMapper.PositionRejectReason"/>,
    /// tedy <b>tymz</b> verdiktem jako fuze a webovy nahled. Druha brana by se driv nebo pozdeji
    /// rozesla s tou prvni.</para>
    ///
    /// <para>Viz doc/imu-and-frames.md a doc/plan-vn100-kalibrace.md (faze 2, kandidat a).</para>
    /// </summary>
    public sealed class MagModelInit : IMessageSink
    {
        private readonly IMagneticModel imu;
        private readonly FusionConfig cfg;
        private readonly object gate = new object();
        private bool hotovo;
        private int zamitnuto;
        private DateTime posledniHlaska = DateTime.MinValue;

        /// <summary>Jak casto nejvys se hlasi, ze se na dobry fix jeste ceka [s].</summary>
        private const double HlaskaPeriodaSec = 30;

        /// <param name="imu">VN100 (ASCII i binarni driver); <c>null</c> = nic se nedeje.</param>
        /// <param name="cfg">Prahy brany kvality fixu (tytez, jake pouziva fuze).</param>
        public MagModelInit(IMagneticModel imu, FusionConfig cfg)
        {
            this.imu = imu;
            this.cfg = cfg ?? new FusionConfig();
        }

        /// <summary>Uz se model nastavil? (Diagnostika a testy.)</summary>
        public bool Done { get { lock (gate) return hotovo; } }

        /// <summary>Kolik fixu brana zamitla, nez prosel prvni dobry. (Diagnostika.)</summary>
        public int Rejected { get { lock (gate) return zamitnuto; } }

        /// <inheritdoc/>
        public void Post(Message msg)
        {
            if (imu == null) return;
            if (!(msg is GPSState gps)) return;

            lock (gate)
            {
                if (hotovo) return;

                string duvod = DefaultMeasurementMapper.PositionRejectReason(gps, cfg);
                if (duvod != null)
                {
                    zamitnuto++;
                    // Nehlasit kazdy zamitnuty fix - GPS jde 5 Hz a log by se zaplnil. Ale
                    // hlasit OBCAS je nutne: „model pole se nenastavil" je jinak neviditelne.
                    var nyni = TimeBase.Now;
                    if ((nyni - posledniHlaska).TotalSeconds >= HlaskaPeriodaSec)
                    {
                        posledniHlaska = nyni;
                        Trace.WriteLine($"MagModel: cekam na kvalitni fix ({duvod}); zamitnuto"
                                        + $" {zamitnuto} fixu. Do te doby je kurz MAGNETICKY.");
                    }
                    return;
                }

                // Zeměpisne souradnice jsou v projektu VSUDE v radianech; prevod na stupne resi
                // az VnCommands.ReferenceVectorConfig, tedy okraj u driveru.
                var lla = new LLA(gps.Latitude, gps.Longitude, gps.Altitude);
                try
                {
                    imu.SetModelParams(lla);
                    hotovo = true;
                    Trace.WriteLine("MagModel: registr 83 nastaven podle polohy "
                                    + $"{Conversions.Rad2Deg(gps.Latitude):F6}, "
                                    + $"{Conversions.Rad2Deg(gps.Longitude):F6} "
                                    + $"(zamitnuto {zamitnuto} fixu pred tim). Kurz je od teď "
                                    + "k PRAVEMU severu; VPE se na novou referenci dotahuje "
                                    + "~100-170 s.");
                }
                catch (Exception ex)
                {
                    // Nenastavit model je vada diagnosticka, ne fatalni - kurz zustane magneticky.
                    // Proto se jen zapise a zkousi se dal pri dalsim fixu.
                    Trace.WriteLine("MagModel: nastaveni registru 83 SELHALO: " + ex.Message
                                    + " Kurz zustava magneticky, zkusim to pri dalsim fixu.");
                }
            }
        }
    }
}
