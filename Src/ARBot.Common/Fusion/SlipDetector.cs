using System;
using ARBot.Common.Models;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Jednoducha heuristika detekce smyku / hrabani kol. Kdyz zrychleni kola prekroci
    /// fyzikalni limit, povazuje odometrii za nespolehlivou a vraci nasobek pro nafouknuti
    /// kovariance jejiho merenia. Volajici (adapter) tim doCasne "neveri kolum".
    /// </summary>
    public class SlipDetector
    {
        private readonly FusionConfig cfg;
        private double? lastLeft, lastRight;
        private DateTime? lastT;

        public SlipDetector(FusionConfig config)
        {
            cfg = config ?? new FusionConfig();
        }

        /// <summary>Naposledy vyhodnoceno jako smyk.</summary>
        public bool IsSlipping { get; private set; }

        /// <summary>
        /// Vraci nasobek smerodatne odchylky R odometrie (>=1). Hodnota > 1 znamena
        /// podezreni na smyk (rychlost/uhlova rychlost z odometrie se pak vazi mensi vahou).
        /// </summary>
        public double OdometryStdScale(IMotorState motor, DateTime t)
        {
            IsSlipping = false;
            if (motor == null)
                return 1.0;

            double scale = 1.0;
            if (lastT.HasValue)
            {
                double dt = (t - lastT.Value).TotalSeconds;
                if (dt > 1e-3)
                {
                    double aL = Math.Abs(motor.LeftWheelSpeed - (lastLeft ?? motor.LeftWheelSpeed)) / dt;
                    double aR = Math.Abs(motor.RightWheelSpeed - (lastRight ?? motor.RightWheelSpeed)) / dt;
                    if (aL > cfg.MaxWheelAccel || aR > cfg.MaxWheelAccel)
                    {
                        IsSlipping = true;
                        scale = Math.Sqrt(cfg.SlipRScale); // scale je pro std, R roste kvadraticky
                    }
                }
            }

            lastLeft = motor.LeftWheelSpeed;
            lastRight = motor.RightWheelSpeed;
            lastT = t;
            return scale;
        }
    }
}
