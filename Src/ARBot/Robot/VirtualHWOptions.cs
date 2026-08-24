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
        /// <summary>
        /// Silnicni sit, ze ktere se rendruje scena.
        /// <para>Je to sit <b>pro obraz</b>, ne pro navigaci - runtime sem dava
        /// <c>ARBotRuntime.CameraRoadNetwork</c>, tedy mapu z <c>visionmap=</c>, kdyz je zadana,
        /// jinak navigacni <c>map=</c>. Kamery tak mohou videt jinou mapu, nez podle ktere se robot
        /// naviguje (viz doc/virtual-hw.md).</para>
        /// </summary>
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

        /// <summary>
        /// Parametry vzhledu a sumu sceny. <b>null = pouzij sdilenou instanci
        /// <see cref="ARBotHW.VirtualScene"/></b> — jen tak jde scenu menit za behu z UI
        /// a z prikazove radky. Test si smi predat vlastni instanci.
        ///
        /// <para><b>Nesmi to byt <c>new SyntheticSceneOptions()</c>.</b> Bylo — a tim byla
        /// scena z prikazove radky i z panelu <b>uplne mrtva</b> (nalezeno 24. 8. 2026):
        /// <c>ARBotHW.SetVirtualHW</c> dela <c>options.Scene ?? VirtualScene</c>, takze pri
        /// nenulove vychozi hodnote se <c>??</c> nikdy neuplatnil a kamery vzdy renderovaly
        /// s vychozi scenou. <c>grassheight=</c>, <c>grassrough=</c> ani <c>depthnoise=</c>
        /// tedy nedelaly nic — tise, protoze parser hodnotu prijal a zapsal ji do
        /// <see cref="ARBotHW.VirtualScene"/>, ze ktereho pak nikdo nerenderoval. Porovnej
        /// <c>Sensors</c>, ktere vychozi hodnotu nema a proto fungovalo.</para>
        /// </summary>
        public SyntheticSceneOptions Scene = null;

        /// <summary>Rozliseni, zorne pole a takt kamer.</summary>
        public VirtualCameraOptions Camera = new VirtualCameraOptions();

        /// <summary>Montazni transformace leve kamery (vychozi ze skutecneho profilu robota).</summary>
        public Matrix4x4 LeftCameraTransform = Profile.LeftCameraTransform;

        /// <summary>Montazni transformace prave kamery (vychozi ze skutecneho profilu robota).</summary>
        public Matrix4x4 RightCameraTransform = Profile.RightCameraTransform;

        /// <summary>
        /// Sum, biasy a prokluz kol simulovane GPS, IMU a odometrie.
        /// <para><b>null (vychozi) = sdilena instance</b> <c>ARBotHW.VirtualSensors</c>. Jen tak
        /// jde sum a chyby menit za behu z UI - kdyby si tu kazdy beh zalozil vlastni objekt,
        /// nastroj by prepisoval neco, co uz nikdo necte. Vlastni instanci ma smysl predat jen
        /// v testech.</para>
        /// </summary>
        public VirtualSensorOptions Sensors;

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
