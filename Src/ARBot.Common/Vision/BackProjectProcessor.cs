using System;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;

namespace ARBot.Common.Vision
{
    /// <summary>
    /// Vypocetni stupen pipeline: na RGB snimek kamery (<see cref="CameraFrame.ImageRGB"/>)
    /// aplikuje <see cref="IBackProject"/> (barva -&gt; pravdepodobnost sjizdnosti) a vysledny
    /// obraz posle jako <see cref="Blob"/> (Type=Probability) pres <see cref="MessageProcessor.Output"/>.
    /// Volitelne posle i zdrojovy RGB snimek jako JPEG Blob (aby byl v zaznamu i vstup).
    /// </summary>
    public sealed class BackProjectProcessor : MessageProcessor
    {
        private readonly IBackProject backProject;
        private readonly bool includeSourceRgb;

        /// <summary>Nazev vysledneho (pravdepodobnostniho) blobu.</summary>
        public string ResultName { get; set; } = "backproject";
        /// <summary>Nazev zdrojoveho (RGB) blobu.</summary>
        public string SourceName { get; set; } = "rgb";

        /// <param name="backProject">Zpetna projekce barev na pravdepodobnost.</param>
        /// <param name="includeSourceRgb">Zda take poslat zdrojovy RGB snimek (JPEG) do proudu.</param>
        /// <param name="policy">Politika vstupni fronty.</param>
        public BackProjectProcessor(IBackProject backProject, bool includeSourceRgb = true,
                                    OverflowPolicy policy = OverflowPolicy.Block)
            : base(policy)
        {
            this.backProject = backProject ?? throw new ArgumentNullException(nameof(backProject));
            this.includeSourceRgb = includeSourceRgb;
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (msg is not CameraFrame frame || frame.ImageRGB == null)
                return;

            var rgb = frame.ImageRGB;

            // Volitelne zdrojovy RGB snimek (JPEG pri zaznamu) - aby replay videl vstup i vystup.
            if (includeSourceRgb)
            {
                var srcMsg = new ImageMsg(rgb, SourceName, ImageMsg.Compression.Jpeg) { TimeStamp = frame.TimeStamp };
                EmitDerived(srcMsg);
            }

            // BackProject: barva -> pravdepodobnost. Pripadne resize na velikost dle Size().
            var size = backProject.Size(rgb.Width, rgb.Height);
            Image<BGR32> src = rgb;
            if (size.Width != rgb.Width || size.Height != rgb.Height)
            {
                src = new Image<BGR32>(size.Width, size.Height);
                src.Resize(rgb);
            }

            var prob = new Image<Gray>(size.Width, size.Height);
            backProject.Process(src, prob);

            // Pravdepodobnost (Gray) - bezztratove (None).
            var probMsg = new ImageMsg(prob, ResultName) { TimeStamp = frame.TimeStamp };
            EmitDerived(probMsg);
        }
    }
}
