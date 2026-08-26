using System;
using ARBot.Common.Common;
using ARBot.Common.Vision.Synthetic;

namespace ARBot.Common.Vision.Qr
{
    /// <summary>
    /// Vyrobi <see cref="SyntheticBillboard"/> s <b>QR kodem</b> — tedy „polozeni kodu do sceny"
    /// virtualni kamery. Viz doc/virtual-hw.md a doc/robotour-mission.md.
    ///
    /// <para><b>Nacpak to je:</b> aby se dal v simulaci projit krok mise, ve kterem robot cte kod.
    /// Do 26. 8. 2026 simulace kod nerenderovala, takze servisni okno se nedalo dokoncit ani rucne.</para>
    ///
    /// <para>Kod se koduje <b>tymz ZXingem</b>, ktery ho pak cte. Je to vedome kruhove: dokazuje to
    /// cestu (scena → render → dekoder), ne uspesnost cteni na skutecnem stanovisti. Ta se musi
    /// zmerit na zarizeni.</para>
    /// </summary>
    public static class QrBillboard
    {
        /// <summary>
        /// Postavi desku s QR kodem daneho textu.
        /// </summary>
        /// <param name="text">Text kodu (v misi typicky <c>geo:sirka,delka</c>).</param>
        /// <param name="centerX">Stred desky ve svete [m, ENU].</param>
        /// <param name="centerY">Stred desky ve svete [m, ENU].</param>
        /// <param name="centerZ">Vyska stredu nad vozovkou [m] — obvykle vyska kamery.</param>
        /// <param name="yawRad">Smer normaly desky [rad] — kam deska kouka.</param>
        /// <param name="sizeM">Strana desky [m]; QR je kvadraticky.</param>
        /// <param name="modulePixels">Kolik pixelu textury na jeden modul kodu (ostrost textury).</param>
        public static SyntheticBillboard Create(string text, double centerX, double centerY,
                                                double centerZ, double yawRad, double sizeM = 0.4,
                                                int modulePixels = 8)
        {
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("Text kodu je prazdny.", nameof(text));
            if (!(sizeM > 0)) throw new ArgumentOutOfRangeException(nameof(sizeM));

            return new SyntheticBillboard
            {
                CenterX = centerX,
                CenterY = centerY,
                CenterZ = centerZ,
                YawRad = yawRad,
                WidthM = sizeM,
                HeightM = sizeM,
                Texture = Render(text, modulePixels),
            };
        }

        /// <summary>
        /// Postavi kod <b>pred danou kameru</b> — v jejim vodorovnem smeru pohledu a <b>kolmo</b>
        /// na nej.
        ///
        /// <para><b>Proc to nejde spocitat „vpravo od robota":</b> kamery nejsou namontovane podel
        /// osy robota. Prava kamera je stocena o <b>29° vpravo</b> a sklonena o 18,6° dolu, takze
        /// deska postavena presne 90° vpravo je <b>mimo jeji vyhled</b> — a kdyz uz se do obrazu
        /// dostane, je o tech 29° zkosena. Smer se proto bere z <b>montazni matice</b>
        /// (<paramref name="cameraToBody"/>), ne z domnenky. Nalezeno v aplikaci 26. 8. 2026.</para>
        ///
        /// <para><b>Deska zustava svisla</b>, i kdyz kamera kouka dolu: QR na stojanu je svisly.
        /// Sklon 18,6° zkrati obraz o <c>1 − cos 18,6° = 5 %</c>, coz je pro dekodovani
        /// zanedbatelne — zkoseni ve VODOROVNEM smeru je to, co skodi (pri 29° uz 13 %).</para>
        /// </summary>
        /// <param name="text">Text kodu.</param>
        /// <param name="cameraToBody">Montazni matice kamery (kamera → ramec robota), napr.
        /// <c>Profile.RightCameraTransform</c>.</param>
        /// <param name="poseX">Poloha robota [m, ENU].</param>
        /// <param name="poseY">Poloha robota [m, ENU].</param>
        /// <param name="poseTheta">Kurz robota [rad].</param>
        /// <param name="distanceM">Vzdalenost desky od kamery podel jejiho vodorovneho smeru [m].</param>
        /// <param name="heightM">Vyska stredu desky nad vozovkou [m].</param>
        /// <param name="sizeM">Strana desky [m].</param>
        public static SyntheticBillboard InFrontOfCamera(string text,
                                                         System.Numerics.Matrix4x4 cameraToBody,
                                                         double poseX, double poseY, double poseTheta,
                                                         double distanceM, double heightM,
                                                         double sizeM = 0.4)
        {
            if (!(distanceM > 0)) throw new ArgumentOutOfRangeException(nameof(distanceM));

            // Opticka osa kamery v ramci robota: v prostoru kamery je to +Z (tataz konvence, jakou
            // pouziva renderer pro paprsky - ray = (x, y, 1)).
            var axis = System.Numerics.Vector3.TransformNormal(
                new System.Numerics.Vector3(0, 0, 1), cameraToBody);

            // Zajima jen VODOROVNY smer - deska je svisla, takze sklon kamery jeji orientaci nemeni.
            double camYaw = Math.Atan2(axis.Y, axis.X);
            var eye = cameraToBody.Translation;

            // Stred desky v ramci robota: od kamery po jejim smeru pohledu.
            double bx = eye.X + distanceM * Math.Cos(camYaw);
            double by = eye.Y + distanceM * Math.Sin(camYaw);

            double cos = Math.Cos(poseTheta), sin = Math.Sin(poseTheta);

            return Create(text,
                          centerX: poseX + bx * cos - by * sin,
                          centerY: poseY + bx * sin + by * cos,
                          centerZ: heightM,
                          // Normala miri ZPET do kamery, tedy proti jejimu smeru pohledu -> deska
                          // je na pohled kolma.
                          yawRad: poseTheta + camYaw + Math.PI,
                          sizeM: sizeM);
        }

        /// <summary>
        /// Vyrenderuje QR kod jako <c>Image&lt;BGR32&gt;</c> (cerny vzor na bilem podkladu).
        ///
        /// <para><b>Tichy okraj (quiet zone) je soucasti kodu</b> — ZXing ho pridava sam a bez nej
        /// se kod cte podstatne horse, protoze dekoder nema jak najit hranici vzoru.</para>
        /// </summary>
        public static Image<BGR32> Render(string text, int modulePixels = 8)
        {
            if (modulePixels < 1) modulePixels = 1;

            var writer = new ZXing.QrCode.QRCodeWriter();
            // Velikost se zada v pixelech; ZXing ji zvetsi na nejblizsi nasobek modulu vcetne
            // tiche zony, takze presny rozmer neni potreba hadat.
            int wanted = 21 * modulePixels;
            var matrix = writer.encode(text, ZXing.BarcodeFormat.QR_CODE, wanted, wanted);

            var img = new Image<BGR32>(matrix.Width, matrix.Height);
            var p = new BGR32 { Data = img.Data };
            for (int y = 0; y < matrix.Height; y++)
                for (int x = 0; x < matrix.Width; x++)
                {
                    byte v = matrix[x, y] ? (byte)0 : (byte)255;
                    p.Index = (y * matrix.Width + x) * 4;
                    p.R = v; p.G = v; p.B = v;
                }
            return img;
        }
    }
}
