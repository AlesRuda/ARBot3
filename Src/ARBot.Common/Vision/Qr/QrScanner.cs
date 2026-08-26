using System;
using System.Collections.Generic;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;

namespace ARBot.Common.Vision.Qr
{
    /// <summary>
    /// <b>Cteni QR kodu z kamery.</b> Samostatny <see cref="MessageProcessor"/> vedle mise — mise
    /// o kamerach nic nevi, jen odebira <see cref="QrCodeMsg"/>. Viz doc/robotour-mission.md.
    ///
    /// <para><b>Vypnuty, dokud ho mise nezapne</b> (<see cref="Enabled"/>), a mise ho zapina jen pod
    /// drzenym nouzovym zastavenim. Dve veci z toho plynou: za jizdy je dekodovani cista rezie a
    /// nikoho nezajima (takze vykonova otazka je z velke casti mimo hru), a hlavne — <b>robot nikdy
    /// neskenuje, kdyz muze jet</b>. Obsluha stojici u robotu s krabici v ruce tak ma fyzickou
    /// garanci, ne jen softwarovou.</para>
    ///
    /// <para><b>Vlastni vlakno</b> (fronta <see cref="OverflowPolicy.DropOldest"/>, kapacita 1):
    /// dekodovani nesmi zdrzet ani vlakno kamery, ani misi, ani ridici tik. Kapacita 1 je spravna —
    /// kdyz scanner nestiha, je nejlepsi pracovat s NEJNOVEJSIM snimkem.</para>
    ///
    /// <para><b>Cte se jen ve stoje, zamerne.</b> Pri 0,8 m/s a expozici par ms je rolling-shutter
    /// rozmazani takove, ze se dekodovani stane loterii. Vsechna cteni v automatu proto probihaji
    /// ve stoje — a shodou okolnosti presne tam, kde je robot navic pod nouzovym zastavenim.</para>
    /// </summary>
    public sealed class QrScanner : MessageProcessor, Missions.IQrScannerControl
    {
        private readonly IQrDecoder decoder;
        private readonly QrScannerConfig config;

        /// <summary>Posledni videny text a kolikrat po sobe (pro <see cref="QrScannerConfig.Confirmations"/>).</summary>
        private string lastText;
        private int lastTextStreak;

        /// <param name="decoder">Dekoder kodu; testy si dodaji vlastni.</param>
        /// <param name="config">Konfigurace; null = vychozi.</param>
        public QrScanner(IQrDecoder decoder, QrScannerConfig config = null)
            : base(OverflowPolicy.DropOldest, capacity: 1)
        {
            this.decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
            this.config = config ?? new QrScannerConfig();
            this.config.Validate();
        }

        /// <summary>Nastaveni, se kterym scanner pracuje.</summary>
        public QrScannerConfig Config => config;

        /// <summary>
        /// Skenuje se? <b>Vychozi je <c>false</c></b> — zapina to mise, a jen pod drzenym nouzovym
        /// zastavenim.
        ///
        /// <para><c>volatile</c>: prehazuje to vlakno mise, cte vlakno scanneru.</para>
        /// </summary>
        public bool Enabled
        {
            get => enabled;
            set
            {
                if (enabled == value) return;
                enabled = value;
                // Vypnuti je zaroven RESET pocitani shodnych cteni: pri dalsim zapnuti (jine
                // servisni okno, jiny kod) nesmi stara cteni pocitat.
                lastText = null;
                lastTextStreak = 0;
            }
        }
        private volatile bool enabled;

        /// <summary>DIAGNOSTIKA: kolik snimku scanner skutecne dekodoval.</summary>
        public long FramesDecoded { get; private set; }

        /// <summary>DIAGNOSTIKA: kolik kodu ohlasil.</summary>
        public long CodesFound { get; private set; }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (!(msg is CameraFrame frame)) return;
            try
            {
                foreach (var r in Process(frame))
                    EmitDerived(r.ToLogMessage());
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"QrScanner: {ex}"); }
        }

        /// <summary>
        /// Jeden snimek: vraci nalezene kody (prazdne pole, kdyz nic — to je normalni stav).
        ///
        /// <para>Verejne schvalne: takhle jde scanner prohnat zaznamem i z testu BEZ vlakna.</para>
        /// </summary>
        public QrResult[] Process(CameraFrame frame)
        {
            if (!enabled || frame == null || frame.ImageRGB == null) return Array.Empty<QrResult>();
            if (!Matches(frame.Name)) return Array.Empty<QrResult>();

            var gray = frame.ImageRGB.ToGray(config.Downscale);
            FramesDecoded++;

            var found = decoder.Decode(gray) ?? Array.Empty<QrResult>();
            if (found.Length == 0)
            {
                // Prazdny snimek prerusi serii shodnych cteni: "trikrat totez" ma znamenat
                // tri snimky po sobe, ne tri kdykoli za odpoledne.
                lastText = null;
                lastTextStreak = 0;
                return Array.Empty<QrResult>();
            }

            var accepted = new List<QrResult>(found.Length);
            foreach (var r in found)
            {
                r.CameraName = frame.Name;
                r.TimeStamp = frame.TimeStamp;

                if (!Confirmed(r.Text)) continue;
                accepted.Add(r);
                CodesFound++;
            }

            return accepted.ToArray();
        }

        /// <summary>
        /// Je snimek z te kamery, ze ktere se cte? Prazdne jmeno v konfiguraci = <b>vsechny</b>.
        /// </summary>
        private bool Matches(string frameName)
            => string.IsNullOrWhiteSpace(config.CameraName)
               || string.Equals(frameName, config.CameraName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Precetl se tentyz text uz dost krat po sobe? Pri <c>Confirmations = 1</c> (vychozi) je to
        /// vzdy hned true.
        /// </summary>
        private bool Confirmed(string text)
        {
            if (text == lastText) lastTextStreak++;
            else { lastText = text; lastTextStreak = 1; }

            return lastTextStreak >= config.Confirmations;
        }
    }
}
