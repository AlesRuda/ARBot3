using System;
using System.Numerics;
using ARBot.Common.Configuration;
using ARBot.Common.Coordinates;
using ARBot.Common.Fusion;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Vision.Synthetic;
using ARBot.HAL.Devices;
using ARBot.HAL.Devices.Camera;

namespace ARBot.Robot
{
    /// <summary>
    /// Vse, co potrebuje <see cref="ARBotHW.SetVirtualHW"/> k zalozeni simulovanych senzoru
    /// (viz doc/virtual-hw.md).
    /// <para>
    /// Zatim jde jen o kamery; virtualni GPS a IMU sem pribydou pozdeji a zalozi se
    /// stejnym volanim.
    /// </para>
    /// </summary>
    public sealed class VirtualHWOptions
    {
        /// <summary>Silnicni sit, ze ktere se rendruje scena.</summary>
        public RoadNetwork Network;

        /// <summary>
        /// Pocatek lokalni ENU roviny. Virtualni HW si ji NEZAKLADA - dostane ji hotovou,
        /// aby cely system pocital od jednoho pocatku (viz doc/virtual-hw.md).
        /// </summary>
        public GeoReference Origin;

        /// <summary>
        /// Zdroj pozy robota k danemu casu. V aplikaci fuze (<c>t =&gt; engine.GetStateAt(t)</c>),
        /// v testech konstanta. Smi vratit null, dokud poza neni k dispozici - snimek se preskoci.
        /// </summary>
        public Func<DateTime, RobotState> PoseAt;

        /// <summary>Parametry vzhledu a sumu sceny.</summary>
        public SyntheticSceneOptions Scene = new SyntheticSceneOptions();

        /// <summary>Rozliseni, zorne pole a takt kamer.</summary>
        public VirtualCameraOptions Camera = new VirtualCameraOptions();

        /// <summary>Montazni transformace leve kamery (vychozi ze skutecneho profilu robota).</summary>
        public Matrix4x4 LeftCameraTransform = Profile.LeftCameraTransform;

        /// <summary>Montazni transformace prave kamery (vychozi ze skutecneho profilu robota).</summary>
        public Matrix4x4 RightCameraTransform = Profile.RightCameraTransform;

        /// <summary>Sum a frekvence simulovane GPS a IMU.</summary>
        public VirtualSensorOptions Sensors = new VirtualSensorOptions();

        /// <summary>Rozchod kol simulovaneho robota [m].</summary>
        public double WheelBase = Profile.Rozchod;

        /// <summary>Omezeni zrychleni kol [m/s^2].</summary>
        public double Acceleration = Profile.MaxAcceleration;

        /// <summary>
        /// Nejvyssi mozna rychlost jednoho kola [m/s]. Pri jejim dosazeni ustupuje dopredna
        /// rychlost, aby se zachovala rotace - stejne jako u skutecneho driveru, ktery dostava
        /// tutez hodnotu jako <c>maxPossibleSpeed</c>. Viz doc/virtual-hw.md.
        /// </summary>
        public double MaxWheelSpeed = Profile.MaxTheoreticalSpeed;

        /// <summary>Pocatecni poloha robota v lokalni ENU rovine [m] (na vychod).</summary>
        public double StartX;

        /// <summary>Pocatecni poloha robota v lokalni ENU rovine [m] (na sever).</summary>
        public double StartY;

        /// <summary>Pocatecni kurz [rad], matematicky (0 = vychod, +CCW).</summary>
        public double StartTheta;

        /// <summary>Overi, ze jsou vyplnene povinne polozky.</summary>
        public void Validate()
        {
            if (Network == null) throw new InvalidOperationException("VirtualHWOptions: chybi Network.");
            if (Origin == null) throw new InvalidOperationException("VirtualHWOptions: chybi Origin.");
            if (PoseAt == null) throw new InvalidOperationException("VirtualHWOptions: chybi PoseAt.");
        }
    }
}
