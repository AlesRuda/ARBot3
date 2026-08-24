using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.LocalMaps;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Vision;

namespace ARBot.HAL
{
    /// <summary>
    /// Abstrakce kamery (barevny + volitelne hloubkovy stream) a jeji projekce do sveta.
    /// </summary>
    public interface ICamera : ISensor
    {
        /// <summary>
        /// Vyvolano po prichodu noveho snimku (poskytuje ho SensorBase).
        /// </summary>
        event EventHandler<CameraFrame> MeasurementArived;

        /// <summary>Aktualni nastaveni barevneho (RGB) streamu.</summary>
        CameraSettings RGBSettings { get;  }

        /// <summary>Aktualni nastaveni hloubkoveho streamu.</summary>
        CameraSettings DepthSettings { get;}

        /// <summary>
        /// Volitelny synchronni procesor snimku: vola se na vlakne kamery hned po nasnimani a dopocte
        /// odvozene vlastnosti primo do <see cref="CameraFrame"/> (pravdepodobnost, polarni grid).
        /// null = kamera jen snima (chovani jako drive). Viz doc/plan-camera-vision-refactor.md.
        /// </summary>
        ICameraFrameProcessor FrameProcessor { get; set; }

        /// <summary>
        /// Zdroj <b>odhadu pozy</b> (fuze) k danemu casu. Kamera z nej plni
        /// <see cref="CameraFrame.PoseAtCaptureX"/> — poza v okamziku porizeni snimku.
        /// <c>null</c> = poza se nestampuje (<see cref="CameraFrame.HasPose"/> zustane <c>false</c>).
        ///
        /// <para><b>Vyhradne diagnostika a vizualizace</b> — viz
        /// <see cref="CameraFrame.PoseAtCaptureX"/>. Kdo pozu potrebuje pro vypocet, sahne si do
        /// fuze sam; kamera ji nese jen proto, aby se hranice cesty daly nakreslit spravnou pozou
        /// i po seeku v zaznamu.</para>
        ///
        /// <para><b>Nesmi se splest s renderovaci pozou virtualni kamery.</b> Ta se ridi parametrem
        /// <c>camerapose=</c> a ve vychozim stavu je to <b>ground truth</b> (aby byla chyba odhadu
        /// meritelna). Tady musi byt vzdy <b>odhad z fuze</b> — jinak by se na virtualnim HW
        /// stampovala skutecnost, na realnem odhad, a obe vetve by se chovaly jinak. Viz
        /// doc/virtual-hw.md.</para>
        /// </summary>
        Func<DateTime, ARBot.Common.Fusion.RobotState> EstimatedPoseAt { get; set; }

        /// <summary>
        /// (Re)konfiguruje kameru na zadana rozliseni a (znovu)spusti snimani.
        /// </summary>
        /// <param name="rgbSettings">Nastaveni barevneho streamu.</param>
        /// <param name="depthSettings">Nastaveni hloubkoveho streamu.</param>
        /// <returns>true pri uspesne konfiguraci.</returns>
        bool Init(CameraSettings rgbSettings, CameraSettings depthSettings);

        /// <summary>
        /// Vraci posledni zachyceny snimek. Opakovane volani bez prichodu noveho snimku vraci null.
        /// </summary>
        CameraFrame GetLastMeasurement();
/*        /// <summary>
        /// Projekce do lokalni mapy
        /// </summary>
        /// <param name="lm"></param>
        /// <param name="bp"></param>
        /// <returns></returns>
        ICameraProjection<Image<BGR>> CreateColorProjector(ILocalMap lm, BackProject bp);
        */
        /// <summary>
        /// Projekce barevne kamery do roviny po ktere jede robot.
        /// </summary>
        /// <returns>Projekce barevneho obrazu.</returns>
        ICameraProjection CreateProjector();

        /// <summary>
        /// Projekce s vyuzitim hloubkove kamery (3D rekonstrukce bodu).
        /// </summary>
        /// <returns>Hloubkova projekce.</returns>
        IDepthCameraProjection CreateDepthProjector();

        //        Matrix3D WorldToCameraTransform(double roll, double pitch, double yaw, Vector3D offset);
        //      void ProjectColor(ILocalMap localMap, Image<BGR> image, Matrix3D transform, BackProject bp);
    }
}
