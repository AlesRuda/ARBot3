using System;
using System.IO;
using System.Numerics;

namespace ARBot.Common.Coordinates
{
    /// <summary>
    /// Serializovatelny popis projekce kamery - vse, z ceho lze <see cref="CameraProjection"/>
    /// znovu postavit. Uklada se do <see cref="ARBot.Common.Devices.CameraFrame.Projection"/>
    /// (od FormatVersion 4), aby sla vizualni cesta prepocitat offline ze zaznamu
    /// (viz doc/occupancy-and-local-planning.md, rezim Simulate).
    ///
    /// <para><b>Cache se NEUKLADA.</b> <c>toDistortCache</c> i <c>camera2DToCamera3DCache</c> jsou
    /// cista funkce intrinsics a jsou velke (640x480 -&gt; ~5 MB); staveji se az pri
    /// <see cref="CreateProjection"/> a drzi se <b>per kamera</b>, ne per snimek.</para>
    ///
    /// <para>Typ je zamerne bez zavislosti na Intel.RealSense - <c>ARBot.Common</c> ho nesmi
    /// referencovat. Nativni cesta <c>ColorPixel23D</c> (ktera RealSense struktury potrebuje) tim
    /// pokryta neni; pro managed vypocty (occupancy, projekce bodu zeme do obrazu) staci tohle.</para>
    /// </summary>
    public sealed class CameraProjectionInfo
    {
        /// <summary>Intrinsics streamu, jehoz zkresleni se modeluje.</summary>
        public Intrinsics Intrinsics;

        /// <summary>Inverzni intrinsics (rozmery tabulek cache se beru z nich).</summary>
        public Intrinsics InverseIntrinsics;

        /// <summary>Transformace <c>from</c> z konstruktoru <see cref="CameraProjection"/>.</summary>
        public Matrix4x4 From = Matrix4x4.Identity;

        /// <summary>Transformace <c>to</c> z konstruktoru <see cref="CameraProjection"/>.</summary>
        public Matrix4x4 To = Matrix4x4.Identity;

        /// <summary>Orientace/pozice kamery nastavena pres <c>SetOrientation</c> (robot-centricka).</summary>
        public Matrix4x4 Transformation = Matrix4x4.Identity;

        /// <summary>
        /// Postavi <see cref="CameraProjection"/> z tohoto popisu (vcetne <c>SetOrientation</c>).
        /// POZOR: konstruktor projekce staví cache pres cele W*H - vysledek si nacachuj per kamera.
        /// </summary>
        public CameraProjection CreateProjection()
        {
            if (Intrinsics == null || InverseIntrinsics == null)
                throw new InvalidOperationException("CameraProjectionInfo: chybi intrinsics.");

            var p = new CameraProjection(Intrinsics, InverseIntrinsics, From, To);
            p.SetOrientation(Transformation);
            return p;
        }

        /// <summary>
        /// Intrinsika <b>barevneho</b> streamu (jina nez <see cref="Intrinsics"/>, ktera patri
        /// hloubkovemu). <c>null</c> = neznama.
        ///
        /// <para><b>Proc tady</b> (21. 8. 2026): prepocet pixelu barevneho obrazu na metricky bod
        /// (<see cref="ARBot.Common.Vision.ColorPixelTo3D"/>) ji potrebuje, a dopocitat ji
        /// z hloubkove nejde - streamy maji jine FOV (u D435 69,4° vs 87°). Tim, ze je v popisu,
        /// je i v zaznamu, takze offline prepocet nepotrebuje zivou kameru.</para>
        /// </summary>
        public Intrinsics ColorIntrinsics;

        /// <summary>
        /// Extrinsika barevna → hloubkova kamera. Identita = zarovnane streamy (virtualni kamera).
        ///
        /// <para><b>Proc tady:</b> u realne D435 jsou senzory ~15 mm od sebe a bez teto transformace
        /// se barevny pixel priradi na spatny hloubkovy (chyba radu 1,5 cm pri 3 m). Do 21. 8. 2026
        /// tyto hodnoty <b>nemely kudy vylezt</b>: <c>D435CameraProjection</c> je drzel v privatnich
        /// polich jen pro nativni <c>ColorPixel23D</c> a na ARM je konstruktor zahazoval.</para>
        /// </summary>
        public Matrix4x4 ColorToDepth = Matrix4x4.Identity;

        /// <summary>Extrinsika hloubkova → barevna kamera. Identita = zarovnane streamy.</summary>
        public Matrix4x4 DepthToColor = Matrix4x4.Identity;

        /// <summary>Zachyti popis z parametru projekce (vc. aktualni <c>Transformation</c>).</summary>
        public static CameraProjectionInfo Capture(Intrinsics intrinsics, Intrinsics inverseIntrinsics,
                                                   Matrix4x4 from, Matrix4x4 to, Matrix4x4 transformation,
                                                   Intrinsics colorIntrinsics = null,
                                                   Matrix4x4? colorToDepth = null,
                                                   Matrix4x4? depthToColor = null)
            => new CameraProjectionInfo
            {
                Intrinsics = intrinsics,
                InverseIntrinsics = inverseIntrinsics,
                From = from,
                To = to,
                Transformation = transformation,
                ColorIntrinsics = colorIntrinsics,
                ColorToDepth = colorToDepth ?? Matrix4x4.Identity,
                DepthToColor = depthToColor ?? Matrix4x4.Identity,
            };

        // ---------------- serializace ----------------

        /// <summary>Zapise popis (flag "je k dispozici" + obsah).</summary>
        public static void Write(BinaryWriter bw, CameraProjectionInfo info)
        {
            bw.Write(info != null);
            if (info == null) return;

            WriteIntrinsics(bw, info.Intrinsics);
            WriteIntrinsics(bw, info.InverseIntrinsics);
            WriteMatrix(bw, info.From);
            WriteMatrix(bw, info.To);
            WriteMatrix(bw, info.Transformation);
            WriteIntrinsics(bw, info.ColorIntrinsics);      // od CameraFrame v5
            WriteMatrix(bw, info.ColorToDepth);             // od CameraFrame v5
            WriteMatrix(bw, info.DepthToColor);             // od CameraFrame v5
        }

        /// <summary>
        /// Nacte popis zapsany <see cref="Write"/>; null, kdyz nebyl k dispozici.
        /// </summary>
        /// <param name="withColorExtras">Nese layout i barevnou intrinsiku a extrinsiky color↔depth
        /// (<c>CameraFrame</c> od verze 5)? U v4 zaznamu <c>false</c> - jinak by se cetlo za konec
        /// popisu.</param>
        public static CameraProjectionInfo Read(BinaryReader br, bool withColorExtras = false)
        {
            if (!br.ReadBoolean()) return null;

            var info = new CameraProjectionInfo
            {
                Intrinsics = ReadIntrinsics(br),
                InverseIntrinsics = ReadIntrinsics(br),
                From = ReadMatrix(br),
                To = ReadMatrix(br),
                Transformation = ReadMatrix(br),
            };
            if (withColorExtras)
            {
                info.ColorIntrinsics = ReadIntrinsics(br);
                info.ColorToDepth = ReadMatrix(br);
                info.DepthToColor = ReadMatrix(br);
            }
            return info;
        }

        private static void WriteIntrinsics(BinaryWriter bw, Intrinsics i)
        {
            bw.Write(i != null);
            if (i == null) return;

            bw.Write(i.Width);
            bw.Write(i.Height);
            bw.Write(i.PPx);
            bw.Write(i.PPy);
            bw.Write(i.Fx);
            bw.Write(i.Fy);
            bw.Write((int)i.Model);

            int n = i.Coeffs?.Length ?? 0;
            bw.Write(n);
            for (int k = 0; k < n; k++) bw.Write(i.Coeffs[k]);
        }

        private static Intrinsics ReadIntrinsics(BinaryReader br)
        {
            if (!br.ReadBoolean()) return null;

            var i = new Intrinsics
            {
                Width = br.ReadInt32(),
                Height = br.ReadInt32(),
                PPx = br.ReadSingle(),
                PPy = br.ReadSingle(),
                Fx = br.ReadSingle(),
                Fy = br.ReadSingle(),
                Model = (Intrinsics.Distortion)br.ReadInt32(),
            };

            int n = br.ReadInt32();
            i.Coeffs = new float[n];
            for (int k = 0; k < n; k++) i.Coeffs[k] = br.ReadSingle();
            return i;
        }

        private static void WriteMatrix(BinaryWriter bw, Matrix4x4 m)
        {
            bw.Write(m.M11); bw.Write(m.M12); bw.Write(m.M13); bw.Write(m.M14);
            bw.Write(m.M21); bw.Write(m.M22); bw.Write(m.M23); bw.Write(m.M24);
            bw.Write(m.M31); bw.Write(m.M32); bw.Write(m.M33); bw.Write(m.M34);
            bw.Write(m.M41); bw.Write(m.M42); bw.Write(m.M43); bw.Write(m.M44);
        }

        private static Matrix4x4 ReadMatrix(BinaryReader br)
            => new Matrix4x4(
                br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
                br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
                br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
                br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
    }
}
