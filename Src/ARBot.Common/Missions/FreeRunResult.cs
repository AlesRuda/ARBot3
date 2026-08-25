using System;
using ARBot.Common.Localization;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// Vysledek jednoho cyklu mise <see cref="FreeRunMission"/>. Viz doc/mission-freerun.md.
    ///
    /// <para>Nese i pripad, kdy se jelo <b>rovne</b> (koridor nebyl) — bez toho by v zaznamu nebylo
    /// videt, jestli robot koridor sledoval, nebo jen drzel kurz.</para>
    /// </summary>
    public sealed class FreeRunResult
    {
        /// <summary>Cas snimku, ze ktereho cyklus vznikl.</summary>
        public DateTime TimeStamp;

        /// <summary>Mrkev poslana do lokalni vrstvy [m, world ENU].</summary>
        public double GoalX, GoalY;

        /// <summary>Polozila se mrkev podle KORIDORU? <c>false</c> = jelo se rovne.</summary>
        public bool FromCorridor;

        /// <summary>Sirka koridoru [m]; 0 kdyz koridor nebyl.</summary>
        public double Width;

        /// <summary>Pricna poloha robotu vuci ose koridoru [m], kladne = vlevo. 0 kdyz koridor nebyl.</summary>
        public double Lateral;

        /// <summary>Smer cesty v ramci robotu [rad]. 0 kdyz koridor nebyl.</summary>
        public double DirectionRad;

        /// <summary>Proc koridor (ne)vznikl — mapove nezavisla podmnozina duvodu.</summary>
        public CorridorFixReason Reason;

        /// <summary>Poza, se kterou se mrkev pokladala [m, m, rad].</summary>
        public double PoseX, PoseY, PoseTheta;

        /// <summary>Je poza vyplnena? (Nula je legitimni poloha, proto vlastni priznak.)</summary>
        public bool HasPose;

        /// <summary>
        /// Prevod na log-zpravu. Konvenci vlastni domena (viz CLAUDE.md) — <c>Logs</c> zustava
        /// pasivni DTO.
        /// </summary>
        public Logs.FreeRunMsg ToLogMessage()
            => new Logs.FreeRunMsg
            {
                TimeStamp = TimeStamp,
                GoalX = GoalX,
                GoalY = GoalY,
                FromCorridor = FromCorridor,
                Width = Width,
                Lateral = Lateral,
                DirectionRad = DirectionRad,
                Reason = (byte)Reason,
                PoseX = PoseX,
                PoseY = PoseY,
                PoseTheta = PoseTheta,
                HasPose = HasPose,
            };
    }
}
