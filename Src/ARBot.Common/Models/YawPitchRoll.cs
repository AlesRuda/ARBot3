using ARBot.Common.Common;
using ARBot.Common.Devices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Models
{
    /// <summary>
    /// Orientation in radians.
    /// </summary>
    public class YawPitchRoll : SensorStateBase, IHistoryItem<YawPitchRoll>
    {
        public enum Euler { zyx, zyz, zxy, zxz, yxz, yxy, yzx, yzy, xyz, xyx, xzy, xzx };

        /// <summary>Bezparametrický ctor (nutný pro reflexi prototypů v MessageCollection.Msgs / Build).</summary>
        public YawPitchRoll()
        {
        }

        public YawPitchRoll(float yaw, float pitch, float roll)
        {
            Yaw = yaw;
            Pitch = pitch;
            Roll = roll;
        }

        /// <summary>
        /// Vytvari YPR na zaklade rotacni matice.
        /// http://lavalle.pl/planning/node103.html
        /// Matrix3D je transponovana oproto standardu.
        /// </summary>
        /// <param name="m"></param>
        public YawPitchRoll(Matrix4x4 m)
        {
            Yaw = MathF.Atan2(m.M12, m.M11);
            Pitch = -MathF.Atan2(-m.M13, MathF.Sqrt(m.M23*m.M23+m.M33*m.M33));
            Roll = MathF.Atan2(m.M23, m.M33);
        }

        /// <summary>
        /// Konstruktor
        /// Original je v MATLABU funkce quat2euler
        /// </summary>
        /// <param name="q"></param>
        /// <param name="type"></param>
        public YawPitchRoll(Quaternion q, Euler type)
        {
            // matlab: q=a+bi+cj+dk, i^2=j^2=k^2=ijk=-1
            // WPF: q=w+xi+yj+zk


            var q1 = q.W;
            var q2 = q.X;
            var q3 = q.Y;
            var q4 = q.Z;

            switch (type)
            {
                case Euler.zyx:
                    ThreeAxisRot(2 * (q2 * q3 + q1 * q4),
                                 q1 * q1 + q2 * q2 - q3 * q3 - q4 * q4,
                                 -2 * (q2 * q4 - q1 * q3),
                                 2 * (q3 * q4 + q1 * q2),
                                 q1 * q1 - q2 * q2 - q3 * q3 + q4 * q4);
                    break;
                case Euler.zyz:
                    TwoAxisRot(2 * (q3 * q4 - q1 * q2),
                                            2 * (q2 * q4 + q1 * q3),
                                            q1 * q1 - q2 * q2 - q3 * q3 + q4 * q4,
                                            2 * (q3 * q4 + q1 * q2),
                                           -2 * (q2 * q4 - q1 * q3));
                    break;

                case Euler.zxy:
                    ThreeAxisRot(-2 * (q2 * q3 - q1 * q4),
                                               q1 * q1 - q2 * q2 + q3 * q3 - q4 * q4,
                                               2 * (q3 * q4 + q1 * q2),
                                              -2 * (q2 * q4 - q1 * q3),
                                               q1 * q1 - q2 * q2 - q3 * q3 + q4 * q4);
                    break;

                case Euler.zxz:
                    TwoAxisRot(2 * (q2 * q4 + q1 * q3),
                                           -2 * (q3 * q4 - q1 * q2),
                                            q1 * q1 - q2 * q2 - q3 * q3 + q4 * q4,
                                            2 * (q2 * q4 - q1 * q3),
                                            2 * (q3 * q4 + q1 * q2));
                    break;

                case Euler.yxz:
                    ThreeAxisRot(2 * (q2 * q4 + q1 * q3),
                                              q1 * q1 - q2 * q2 - q3 * q3 + q4 * q4,
                                             -2 * (q3 * q4 - q1 * q2),
                                              2 * (q2 * q3 + q1 * q4),
                                              q1 * q1 - q2 * q2 + q3 * q3 - q4 * q4);
                    break;

                case Euler.yxy:
                    TwoAxisRot(2 * (q2 * q3 - q1 * q4),
                                            2 * (q3 * q4 + q1 * q2),
                                            q1 * q1 - q2 * q2 + q3 * q3 - q4 * q4,
                                            2 * (q2 * q3 + q1 * q4),
                                           -2 * (q3 * q4 - q1 * q2));
                    break;

                case Euler.yzx:
                    ThreeAxisRot(-2 * (q2 * q4 - q1 * q3),
                                               q1 * q1 + q2 * q2 - q3 * q3 - q4 * q4,
                                               2 * (q2 * q3 + q1 * q4),
                                              -2 * (q3 * q4 - q1 * q2),
                                               q1 * q1 - q2 * q2 + q3 * q3 - q4 * q4);
                    break;

                case Euler.yzy:
                    TwoAxisRot(2 * (q3 * q4 + q1 * q2),
                                           -2 * (q2 * q3 - q1 * q4),
                                            q1 * q1 - q2 * q2 + q3 * q3 - q4 * q4,
                                            2 * (q3 * q4 - q1 * q2),
                                            2 * (q2 * q3 + q1 * q4));
                    break;

                case Euler.xyz:
                    ThreeAxisRot(-2 * (q3 * q4 - q1 * q2),
                                               q1 * q1 - q2 * q2 - q3 * q3 + q4 * q4,
                                               2 * (q2 * q4 + q1 * q3),
                                              -2 * (q2 * q3 - q1 * q4),
                                               q1 * q1 + q2 * q2 - q3 * q3 - q4 * q4);
                    break;

                case Euler.xyx:
                    TwoAxisRot(2 * (q2 * q3 + q1 * q4),
                                           -2 * (q2 * q4 - q1 * q3),
                                            q1 * q1 + q2 * q2 - q3 * q3 - q4 * q4,
                                            2 * (q2 * q3 - q1 * q4),
                                            2 * (q2 * q4 + q1 * q3));
                    break;

                case Euler.xzy:
                    ThreeAxisRot(2 * (q3 * q4 + q1 * q2),
                                              q1 * q1 - q2 * q2 + q3 * q3 - q4 * q4,
                                             -2 * (q2 * q3 - q1 * q4),
                                              2 * (q2 * q4 + q1 * q3),
                                              q1 * q1 + q2 * q2 - q3 * q3 - q4 * q4);
                    break;

                case Euler.xzx:
                    TwoAxisRot(2 * (q2 * q4 - q1 * q3),
                                            2 * (q2 * q3 + q1 * q4),
                                            q1 * q1 + q2 * q2 - q3 * q3 - q4 * q4,
                                            2 * (q2 * q4 + q1 * q3),
                                           -2 * (q2 * q3 - q1 * q4));
                    break;
            }
        }

        /// <summary>
        /// Euler úhel okolo svislé osy v radianech, roste v matematickém směru (proti směru
        /// hodinových ručiček). Význam počátku závisí na referenčním framu zdrojového kvaternionu;
        /// pro ENU atitude v tomto projektu je to matematická orientace (0 = východ, +CCW).
        /// Převod na/z azimutu (0 = sever, +CW) přes <see cref="ARBot.Common.Common.Conversions.Orientation2Azimut"/>.
        /// </summary>
        public float Yaw
        {
            get;
            private set;
        }
        /// <summary>
        /// predozadni naklon v radianech, roste smerem nahoru
        /// </summary>
        public float Pitch
        {
            get;
            private set;
        }

        /// <summary>
        /// Pravolevy naklon v radianech, roste doprava
        /// </summary>
        public float Roll
        {
            get;
            private set;
        }
        DateTime IHistoryItem<YawPitchRoll>.TimeStamp { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public YawPitchRoll Interpolate(YawPitchRoll prev, YawPitchRoll next, float d)
        {
            return new YawPitchRoll(prev.Yaw + d * (next.Yaw - prev.Yaw), prev.Pitch + d * (next.Pitch - prev.Pitch), prev.Roll + d * (next.Roll - prev.Roll));
        }

        public override string ToString()
        {
            return string.Format("Yaw={0}, Pitch={1}, Roll={2}", Yaw, Pitch, Roll);
        }

        void ThreeAxisRot(float r11, float r12, float r21, float r31, float r32)
        {
            Yaw = MathF.Atan2(r11, r12);
            Pitch = MathF.Asin(r21);
            Roll = MathF.Atan2(r31, r32);
        }

        void TwoAxisRot(float r11, float r12, float r21, float r31, float r32)
        {
            Yaw = MathF.Atan2(r11, r12);
            Pitch = MathF.Acos(r21);
            Roll = MathF.Atan2(r31, r32);
        }

        Quaternion ToQ(float a, float b, float c, float d)
        {
            return new Quaternion(b, c, d, a);
        }
        public Quaternion ToQuaternion(Euler type)
        {
            var cang1 = MathF.Cos(Yaw / 2);
            var cang2 = MathF.Cos(Pitch / 2);
            var cang3 = MathF.Cos(Roll / 2);
            var sang1 = MathF.Sin(Yaw / 2);
            var sang2 = MathF.Sin(Pitch / 2);
            var sang3 = MathF.Sin(Roll / 2);

            switch (type)
            {
                case Euler.zyx:
                    return ToQ(
                        cang1 * cang2 * cang3 + sang1 * sang2 * sang3,
                        cang1 * cang2 * sang3 - sang1 * sang2 * cang3,
                        cang1 * sang2 * cang3 + sang1 * cang2 * sang3,
                        sang1 * cang2 * cang3 - cang1 * sang2 * sang3);
                case Euler.zyz:
                    return ToQ(
                        cang1 * cang2 * cang3 - sang1 * cang2 * sang3,
                        cang1 * sang2 * sang3 - sang1 * sang2 * cang3,
                        cang1 * sang2 * cang3 + sang1 * sang2 * sang3,
                        sang1 * cang2 * cang3 + cang1 * cang2 * sang3);
                case Euler.zxy:
                    return ToQ(
                        cang1 * cang2 * cang3 - sang1 * sang2 * sang3,
                        cang1 * sang2 * cang3 - sang1 * cang2 * sang3,
                        cang1 * cang2 * sang3 + sang1 * sang2 * cang3,
                        cang1 * sang2 * sang3 + sang1 * cang2 * cang3);
                case Euler.zxz:
                    return ToQ(
                        cang1 * cang2 * cang3 - sang1 * cang2 * sang3,
                        cang1 * sang2 * cang3 + sang1 * sang2 * sang3,
                        sang1 * sang2 * cang3 - cang1 * sang2 * sang3,
                        cang1 * cang2 * sang3 + sang1 * cang2 * cang3);
                case Euler.yxz:
                    return ToQ(
                        cang1 * cang2 * cang3 + sang1 * sang2 * sang3,
                        cang1 * sang2 * cang3 + sang1 * cang2 * sang3,
                        sang1 * cang2 * cang3 - cang1 * sang2 * sang3,
                        cang1 * cang2 * sang3 - sang1 * sang2 * cang3);
                case Euler.yxy:
                    return ToQ(
                        cang1 * cang2 * cang3 - sang1 * cang2 * sang3,
                        cang1 * sang2 * cang3 + sang1 * sang2 * sang3,
                        sang1 * cang2 * cang3 + cang1 * cang2 * sang3,
                        cang1 * sang2 * sang3 - sang1 * sang2 * cang3);
                case Euler.yzx:
                    return ToQ(
                        cang1 * cang2 * cang3 - sang1 * sang2 * sang3,
                        cang1 * cang2 * sang3 + sang1 * sang2 * cang3,
                        cang1 * sang2 * sang3 + sang1 * cang2 * cang3,
                        cang1 * sang2 * cang3 - sang1 * cang2 * sang3);
                case Euler.yzy:
                    return ToQ(
                        cang1 * cang2 * cang3 - sang1 * cang2 * sang3,
                        sang1 * sang2 * cang3 - cang1 * sang2 * sang3,
                        cang1 * cang2 * sang3 + sang1 * cang2 * cang3,
                        cang1 * sang2 * cang3 + sang1 * sang2 * sang3);
                case Euler.xyz:
                    return ToQ(
                        cang1 * cang2 * cang3 - sang1 * sang2 * sang3,
                        cang1 * sang2 * sang3 + sang1 * cang2 * cang3,
                        cang1 * sang2 * cang3 - sang1 * cang2 * sang3,
                        cang1 * cang2 * sang3 + sang1 * sang2 * cang3);
                case Euler.xyx:
                    return ToQ(
                        cang1 * cang2 * cang3 - sang1 * cang2 * sang3,
                        cang1 * cang2 * sang3 + sang1 * cang2 * cang3,
                        cang1 * sang2 * cang3 + sang1 * sang2 * sang3,
                        sang1 * sang2 * cang3 - cang1 * sang2 * sang3);
                case Euler.xzy:
                    return ToQ(
                        cang1 * cang2 * cang3 + sang1 * sang2 * sang3,
                        sang1 * cang2 * cang3 - cang1 * sang2 * sang3,
                        cang1 * cang2 * sang3 - sang1 * sang2 * cang3,
                        cang1 * sang2 * cang3 + sang1 * cang2 * sang3);
                //case Euler.xzx:
                default:
                    return ToQ(
                        cang1 * cang2 * cang3 - sang1 * cang2 * sang3,
                        cang1 * cang2 * sang3 + sang1 * cang2 * cang3,
                        cang1 * sang2 * sang3 - sang1 * sang2 * cang3,
                        cang1 * sang2 * cang3 + sang1 * sang2 * sang3);

            }
        }
    }
}
