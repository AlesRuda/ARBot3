using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava: jeden cyklus mise FreeRun (viz doc/mission-freerun.md).
    ///
    /// <para><b>Nacpak zprava:</b> bez ni je mise v zaznamu neviditelna a neda se zmerit, jak jela —
    /// hlavne jestli koridor sledovala, nebo jen drzela kurz. Tenhle projekt meri vsechno nad
    /// zaznamem (viz doc/record-replay.md).</para>
    /// </summary>
    [Serializable()]
    public class FreeRunMsg : Message, IHasCaptureTime
    {
        /// <summary>Mrkev poslana do lokalni vrstvy [m, world ENU].</summary>
        public double GoalX, GoalY;

        /// <summary>
        /// Polozila se mrkev podle KORIDORU? <c>false</c> = koridor nebyl a robot drzel kurz.
        /// <b>Klicovy udaj celeho zaznamu mise</b> — podil <c>true</c> rika, jak casto mela mise
        /// vubec co sledovat.
        /// </summary>
        public bool FromCorridor;

        /// <summary>Sirka koridoru [m]; 0 kdyz koridor nebyl.</summary>
        public double Width;

        /// <summary>
        /// Pricna poloha robotu vuci ose koridoru [m], <b>kladne = robot vlevo</b> od osy.
        /// Pozadovana hodnota je <c>−Width/4</c>, takze rozdil proti ni je regulacni odchylka mise.
        /// </summary>
        public double Lateral;

        /// <summary>Smer cesty v ramci robotu [rad]; 0 = cesta vede rovne vpred.</summary>
        public double DirectionRad;

        /// <summary>Duvod (<c>ARBot.Common.Localization.CorridorFixReason</c> jako byte).</summary>
        public byte Reason;

        /// <summary>
        /// Poza, se kterou se mrkev pokladala [m, m, rad].
        ///
        /// <para>Je to poza <b>POŘÍZENÍ</b> snimku, ne „posledni znama" — a cestuje ve zprave, aby
        /// se mrkev dala nakreslit i po seeku v zaznamu. Tataz konvence i tyz duvod jako
        /// u <see cref="RoadCorridorMsg.PoseX"/>.</para>
        /// </summary>
        public double PoseX, PoseY, PoseTheta;

        /// <summary>Je <see cref="PoseX"/> vyplnena? (Nula je legitimni poloha, proto vlastni priznak.)</summary>
        public bool HasPose;

        /// <summary>Cas, ke kteremu vysledek plati (cas snimku).</summary>
        public DateTime TimeStamp;

        /// <summary>
        /// <b>Verze 2</b> (26. 9. 2026): mrkev podle JEDINE hrany - ktera
        /// (<c>ARBot.Common.Localization.CorridorSide</c> jako byte, 0 = zadna), jeji odstup od robotu
        /// [m, kladne = vlevo] a jestli byla sirka z mapy. Ve verzi 1 chybi (0 / false).
        /// </summary>
        public byte SingleSide;
        /// <summary>Znamenkovy odstup jedine hrany od robotu [m] (verze 2).</summary>
        public double EdgeOffset;
        /// <summary>Byla u jedine hrany sirka z mapy? (verze 2)</summary>
        public bool WidthFromMap;

        /// <summary>Format verze 2: pridano mereni z jedine hrany.</summary>
        public const int FormatVersion = 2;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public FreeRunMsg() : base("FreeRunMsg", FormatVersion)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(GoalX);
            bw.Write(GoalY);
            bw.Write(FromCorridor);
            bw.Write(Width);
            bw.Write(Lateral);
            bw.Write(DirectionRad);
            bw.Write(Reason);
            bw.Write(PoseX);
            bw.Write(PoseY);
            bw.Write(PoseTheta);
            bw.Write(HasPose);
            Write(bw, TimeStamp);
            if (Verze >= 2)
            {
                bw.Write(SingleSide);
                bw.Write(EdgeOffset);
                bw.Write(WidthFromMap);
            }
        }

        public override void FromData(BinaryReader br)
        {
            GoalX = br.ReadDouble();
            GoalY = br.ReadDouble();
            FromCorridor = br.ReadBoolean();
            Width = br.ReadDouble();
            Lateral = br.ReadDouble();
            DirectionRad = br.ReadDouble();
            Reason = br.ReadByte();
            PoseX = br.ReadDouble();
            PoseY = br.ReadDouble();
            PoseTheta = br.ReadDouble();
            HasPose = br.ReadBoolean();
            TimeStamp = ReadDateTime(br);
            // Starsi zaznamy (v1) mereni z jedine hrany nemaji - zustava 0 / false.
            if (Verze >= 2)
            {
                SingleSide = br.ReadByte();
                EdgeOffset = br.ReadDouble();
                WidthFromMap = br.ReadBoolean();
            }
        }

        public override Message Build() => new FreeRunMsg();
    }
}
