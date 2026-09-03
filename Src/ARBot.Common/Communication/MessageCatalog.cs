using System;
using System.Collections.Generic;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Registr prototypu zprav (MsgName -&gt; prototyp) pro <see cref="MessageReader"/>.
    /// Nahrazuje natvrdo zadany seznam v ctoru <see cref="MessageQueue"/>. Kazda vrstva
    /// (Common / HAL / app) si pridava vlastni typy pres <see cref="Register(Message)"/>.
    /// </summary>
    public sealed class MessageCatalog
    {
        private readonly Dictionary<string, Message> prototypes = new Dictionary<string, Message>();

        /// <summary>Zaregistruje prototyp podle jeho <see cref="Message.MsgName"/>.</summary>
        public MessageCatalog Register(Message prototype)
        {
            if (prototype == null) throw new ArgumentNullException(nameof(prototype));
            prototypes[prototype.MsgName] = prototype;
            return this;
        }

        /// <summary>Zaregistruje prototyp typu <typeparamref name="T"/> (nutny bezparametricky ctor).</summary>
        public MessageCatalog Register<T>() where T : Message, new() => Register(new T());

        /// <summary>Je typ zpravy s danym jmenem registrovan?</summary>
        public bool Contains(string msgName) => prototypes.ContainsKey(msgName);

        /// <summary>Kopie mapy prototypu pro <see cref="MessageReader"/>.</summary>
        public Dictionary<string, Message> ToPrototypeMap() => new Dictionary<string, Message>(prototypes);

        /// <summary>
        /// Vychozi katalog typu z <c>ARBot.Common</c> (telemetrie + sjednocena merenia +
        /// odvozene zpravy). HAL/app si doplnuji sve (GPSState, CameraFrame, ...).
        /// </summary>
        public static MessageCatalog CommonDefaults()
        {
            var c = new MessageCatalog();
            // Telemetrie z ARBot2
            c.Register(new State());
            c.Register(new EKFStepMsg());
            c.Register(new Info());
            c.Register(new ImageMsg());
            c.Register(new Marker());
            c.Register(new Module());
            c.Register(new VFH());
            c.Register(new Lidar());
            c.Register(new ICPMsg());
            c.Register(new ColliderMsg());
            c.Register(new PathEdgeMsg());
            c.Register(new GraphNavigationMsg());
            c.Register(new MapMsg());
            // Sjednocena merenia (Common)
            c.Register(new IMUState());
            // Odvozene / debug zpravy
            c.Register(new RobotStateMsg());
            c.Register(new GroundTruthMsg());
            c.Register(new MeasurementDiagMsg());
            c.Register(new DriveCommandMsg());
            c.Register(new OccupancyGridMsg());
            c.Register(new MapCorrelationMsg());
            c.Register(new RoadCorridorMsg());
            c.Register(new FreeRunMsg());
            c.Register(new QrCodeMsg());
            c.Register(new MissionMsg());
            c.Register(new LocalPlanMsg());
            c.Register(new GlobalNavMsg());
            c.Register(new PerfMsg());
            // POZN.: PolarTraversabilityGridMsg zrusen - grid je nyni soucasti CameraFrame
            // (viz doc/plan-camera-vision-refactor.md). Stare zaznamy s touto zpravou se pri replay
            // preskoci (neznamy typ), prehravani se nerozbije.
            return c;
        }

        /// <summary>
        /// <see cref="CommonDefaults"/> + stavy zarizeni (<see cref="GPSState"/>,
        /// <see cref="MotorStateBase"/>, <see cref="CameraFrame"/>). <b>Tohle je katalog, kterym se
        /// cte ZAZNAM</b> - jak v aplikaci (replay, telemetricky sken), tak v offline analyze.
        ///
        /// <para><b>Nacpak samostatna metoda:</b> tyhle tri typy si dlouho doregistroval kazdy
        /// konzument sam a ty seznamy se DVAKRAT rozesly. Dusledek je vzdycky stejny a zakerny:
        /// index zpravu ukazuje, ale <c>Read</c> vrati <c>null</c>, takze se zaznam tvari, jako by
        /// v nem ten senzor VUBEC NEBYL. Poprve to stalo hodinu u <see cref="GPSState"/>
        /// (25. 8. 2026), podruhe u <see cref="MotorStateBase"/> (2. 9. 2026), kde
        /// <c>ARBot.Analyze</c> hlasil 8381 motorovych zprav v indexu a nulu prectenych - a skoro
        /// z toho vznikl zaver "motor nedodaval mereni", ktery by byl uplne vymysleny.
        /// Jeden zdroj pravdy to zavira: kdo cte zaznam, vola tuhle metodu.</para>
        ///
        /// <para>Do <see cref="CommonDefaults"/> se to zamerne neslucuje - ten je zaklad pro
        /// libovolneho konzumenta zprav, kdezto tady jde vyslovne o cteni zaznamu.</para>
        /// </summary>
        public static MessageCatalog RecordDefaults()
            => CommonDefaults()
                .Register(new GPSState())
                .Register(new MotorStateBase())
                .Register(new CameraFrame());
    }
}
