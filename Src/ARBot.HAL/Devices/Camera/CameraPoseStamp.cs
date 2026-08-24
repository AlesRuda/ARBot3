using System;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// Doplni do snimku <b>odhad pozy v okamziku porizeni</b>
    /// (<see cref="CameraFrame.PoseAtCaptureX"/>). Volaji to vsechny kamery na svem vlakne hned po
    /// sestaveni snimku, aby realna i virtualni vetev delaly totez.
    ///
    /// <para><b>Proc to je sdilena funkce a ne kod ve trech kamerach.</b> Implementace jsou tri
    /// (<c>VirtualCamera</c>, <c>D435Camera</c> pro Windows a pro Armbian) a jde o kontrakt, ktery
    /// se musi chovat stejne — vcetne toho, ze <b>chybejici poza NESMI zahodit snimek</b>.</para>
    ///
    /// <para><b>Zahazovat snimek kvuli poze je vada.</b> U virtualni kamery je bez pozy skutecne
    /// co renderovat nelze, takze tam se snimek preskocit musi — ale to je jeji RENDEROVACI poza.
    /// Tahle poza je jen metadatum: kdyz ji fuze nezna, snimek je porad platne senzoricke mereni
    /// a musi projit s <see cref="CameraFrame.HasPose"/> = <c>false</c>.</para>
    ///
    /// <para>Viz doc/record-replay.md a doc/virtual-hw.md.</para>
    /// </summary>
    public static class CameraPoseStamp
    {
        /// <summary>
        /// Vyzvedne pozu k casu snimku a zapise ji do ramce. Bezpecne pri <c>null</c> lambde
        /// i <c>null</c> ramci; vyjimka ze zdroje pozy se spolkne (metadatum nesmi shodit kameru).
        /// </summary>
        public static void Apply(CameraFrame frame, Func<DateTime, RobotState> estimatedPoseAt)
        {
            if (frame == null) return;

            frame.HasPose = false;
            if (estimatedPoseAt == null) return;

            try
            {
                // Cas snimku, ne "teď": fuze umi extrapolovat dopredu z posledniho uzlu, takze
                // dotaz v okamziku porizeni projde (null vraci jen pro cas STARSI, nez je okno
                // historie). Viz AsyncFusionEngine.GetStateAt.
                var pose = estimatedPoseAt(frame.TimeStamp);
                if (pose == null) return;

                frame.PoseAtCaptureX = pose.X;
                frame.PoseAtCaptureY = pose.Y;
                frame.PoseAtCaptureTheta = pose.Theta;
                frame.HasPose = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraPoseStamp: {ex.Message}");
            }
        }
    }
}
