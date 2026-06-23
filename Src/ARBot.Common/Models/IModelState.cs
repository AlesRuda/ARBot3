using ARBot.Common.Common;
using System;
using System.Numerics;

namespace ARBot.Common.Models
{
    public interface IModelState: IHistoryItem<IModelState>
    {
        double Orientation { get; set; }
        /// <summary>
        /// Rychlost otaceni v rad/s v matematickem smyslu
        /// </summary>
        double OrientationVelocity { get; }
        double Pitch { get; set; }
        double Roll { get; set; }
        Matrix4x4 Rotation { get; }
        Matrix4x4 Trasnformation { get; }
        /// <summary>
        /// Rychlost v m/s
        /// </summary>
        double Velocity { get; }
        double X { get; set; }
        double Y { get; set; }

        IModelState Clone();
        IModelState Interpolate(IModelState prev, IModelState next, double d);
    }
}