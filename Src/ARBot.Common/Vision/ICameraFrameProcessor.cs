using ARBot.Common.Devices;

namespace ARBot.Common.Vision
{
    /// <summary>
    /// Synchronni dopocet odvozenych vlastnosti snimku kamery primo do <see cref="CameraFrame"/>
    /// (pravdepodobnost sjizdnosti <see cref="CameraFrame.ImageProbability"/> a polarni grid
    /// <see cref="CameraFrame.Grid"/>). Vola se na vlakne kamery hned po nasnimani (viz
    /// doc/plan-camera-vision-refactor.md, krok 1) - misto asynchronniho fan-outu do pipeline.
    /// </summary>
    public interface ICameraFrameProcessor
    {
        /// <summary>Dopocte odvozene vlastnosti (Probability, Grid) primo do <paramref name="frame"/>.
        /// Vola se SYNCHRONNE (na vlakne kamery).</summary>
        void Process(CameraFrame frame);
    }
}
