using System;
using ARBot.Common.Common;

namespace ARBot.Common.Vision
{
    /// <summary>Druh vrstvy - urcuje, jak se renderuje.</summary>
    public enum LayerKind
    {
        /// <summary>Barevny obraz (BGR32).</summary>
        Color,
        /// <summary>Pravdepodobnostni / sedy obraz (1 bajt/pixel).</summary>
        Probability,
        /// <summary>Hloubka (16 bit).</summary>
        Depth
    }

    /// <summary>
    /// Pojmenovana obrazova vrstva - spolecny model pro <see cref="ARBot.Common.Logs.Blob"/> i
    /// <see cref="ARBot.Common.Devices.CameraFrame"/>. Podle <see cref="Kind"/> je vyplneno prave
    /// jedno z pol <see cref="Color"/> / <see cref="Gray"/> / <see cref="Depth"/>.
    /// </summary>
    public sealed class ImageLayer
    {
        public string Name;
        public LayerKind Kind;
        public DateTime TimeStamp;

        public Image<BGR32> Color;
        public Image<Gray> Gray;
        public Image<Gray16> Depth;

        public int Width =>
            Color?.Width ?? Gray?.Width ?? Depth?.Width ?? 0;
        public int Height =>
            Color?.Height ?? Gray?.Height ?? Depth?.Height ?? 0;

        /// <summary>
        /// <b>Rozliseni obrazu, ktery vrstva POKRYVA</b> [px]; <c>0</c> = totez jako
        /// <see cref="Width"/>/<see cref="Height"/>.
        ///
        /// <para><b>Nacpak.</b> Pravdepodobnost ze site je <b>128×128</b>, ale pokryva cely
        /// barevny snimek <b>640×480</b> — squash 4:3 → 1:1 je replika treninku, ne chyba (viz
        /// doc/semantic-segmentation.md). Bez tehle informace se s tim neda spravne zachazet:
        /// zobrazovac vrstvu vykresli jako CTVEREC vedle podkladu a odecet hodnoty pod kurzorem
        /// mine. Presne to se stalo (nalez 10. 9. 2026, doc/devlog.md): overlay pokryval jen
        /// prostrednich 75 % sirky obrazu a hodnota <c>p</c> se hlasila jen v levem hornim rohu
        /// 128×128 px, a jeste pro jiny bod scény.</para>
        ///
        /// <para>Nastavuje <see cref="MessageImageLayers"/> tam, kde je zdroj znam. U samostatne
        /// prisle vrstvy (<c>Blob</c> „backproject" bez barevneho snimku) znam neni a zustava
        /// <c>0</c> — pak se scéna rovna rozliseni vrstvy, tedy dnesni chovani.</para>
        /// </summary>
        public int SceneWidth;
        /// <inheritdoc cref="SceneWidth"/>
        public int SceneHeight;

        /// <summary>Scéna, kterou vrstva pokryva [px] — <see cref="SceneWidth"/> s dopadem na vlastni rozmer.</summary>
        public int EffectiveSceneWidth => SceneWidth > 0 ? SceneWidth : Width;
        /// <inheritdoc cref="EffectiveSceneWidth"/>
        public int EffectiveSceneHeight => SceneHeight > 0 ? SceneHeight : Height;

        /// <summary>
        /// <b>Pixel vrstvy pro bod ve SCENOVYCH souradnicich.</b> <c>false</c> = bod je mimo.
        ///
        /// <para>Meritko se pocita v obou osach ZVLAST — stejna konvence jako
        /// <c>probScaleX</c>/<c>probScaleY</c> v <c>OccupancyIntegrator</c> a
        /// <c>PathEdgeFinderItem.Scale*</c>. Jedno spolecne meritko by u 128×128 proti 640×480
        /// minulo svisle o tretinu.</para>
        /// </summary>
        /// <param name="sceneX">Bod ve scéne [px].</param>
        /// <param name="sceneY">Bod ve scéne [px].</param>
        /// <param name="sceneW">Sirka scény, ve ktere <paramref name="sceneX"/> plati; ≤ 0 = rozmer vrstvy.</param>
        /// <param name="sceneH">Vyska scény; ≤ 0 = rozmer vrstvy.</param>
        /// <param name="x">Pixel vrstvy.</param>
        /// <param name="y">Pixel vrstvy.</param>
        public bool TryPixel(int sceneX, int sceneY, int sceneW, int sceneH, out int x, out int y)
        {
            x = y = -1;
            int w = Width, h = Height;
            if (w <= 0 || h <= 0) return false;
            if (sceneW <= 0) sceneW = EffectiveSceneWidth;
            if (sceneH <= 0) sceneH = EffectiveSceneHeight;
            if (sceneW <= 0 || sceneH <= 0) return false;
            if (sceneX < 0 || sceneY < 0 || sceneX >= sceneW || sceneY >= sceneH) return false;

            // long: 640 * 128 se do int vejde, ale u vetsich obrazu uz je to na hrane.
            x = (int)((long)sceneX * w / sceneW);
            y = (int)((long)sceneY * h / sceneH);
            if (x >= w) x = w - 1;      // sceneX == sceneW-1 pri deleni beze zbytku
            if (y >= h) y = h - 1;
            return true;
        }
    }
}
