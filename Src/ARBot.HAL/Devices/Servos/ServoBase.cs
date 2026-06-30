using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.Servos
{
    public class ServoBase: IHistoryItem<ServoBase>
    {
        public ServoBase()
        {
            middlePulseLen = 1500;
            scale = 500/(Math.PI/2);
        }
        public void Calibrate(double leftAngle, double leftPulse, double rightAngle, double rightPulse)
        {
            middlePulseLen = (int)((rightPulse * leftAngle - rightAngle * leftPulse) / (leftAngle - rightAngle));
            scale = leftAngle / (leftPulse - middlePulseLen);
        }

        private int middlePulseLen;
        private double scale;
        public int Channel;
        public int CurrentPulseLen { get; set; }

        private int? maxPulseSpeed;
        private double? maxSpeed;

        private int? pulseLen;
        private double? angle;

        /// <summary>
        /// Servo pulse len
        /// </summary>
        public int? PulseLen
        {
            get
            {
                if (pulseLen != null)
                    return pulseLen.Value;
                if(angle!=null)
                    return (int)(angle.Value / scale + middlePulseLen);
                return null;
            }
            set
            {
                if(PulseLen!=value)
                {
                    pulseLen = value;
                    angle = null;
                }
            }
        }

        /// <summary>
        /// Required servo angle
        /// </summary>
        public double? Angle
        {
            get
            {
                if (angle != null)
                    return angle.Value;
                if(pulseLen!=null)
                    return scale * (pulseLen - middlePulseLen);
                return null;
            }
            set
            {
                if (Angle != value)
                {
                    angle = value;
                    pulseLen = null;
                }
            }
        }

        /// <summary>
        /// Current servo angle
        /// </summary>
        public double CurrentAngle
        {
            get
            {
                return scale * (CurrentPulseLen - middlePulseLen);
            }
        }

        /// <summary>
        /// Maximum pulse speed
        /// </summary>
        public int? MaxPulseSpeed
        {
            get
            {
                if (maxPulseSpeed != null)
                    return maxPulseSpeed.Value;
                if(maxSpeed!=null)
                    return (int)(maxSpeed.Value / scale);
                return null;
            }
            set
            {
                if (MaxPulseSpeed != value)
                {
                    maxPulseSpeed = value;
                    maxSpeed = null;
                }
            }
        }

        /// <summary>
        /// Maximalni rychlost otaceni serva v radianech za sekundu
        /// </summary>
        public double? MaxSpeed
        {
            get
            {
                if (maxSpeed != null)
                    return maxSpeed.Value;
                if(maxPulseSpeed!=null)
                    return scale * MaxPulseSpeed;
                return null;
            }
            set
            {
                if (MaxSpeed != value)
                {
                    maxSpeed = value;
                    maxPulseSpeed = null;
                }
            }
        }

        public ServoBase Interpolate(ServoBase prev, ServoBase next, float d)
        {
            ServoBase r = new ServoBase();

            r.middlePulseLen = prev.middlePulseLen;
            r.scale = prev.scale;

            r.Angle = prev.Angle + d * (next.Angle - prev.Angle);
            r.CurrentPulseLen = (int)(prev.CurrentPulseLen + d * (next.CurrentPulseLen - prev.CurrentPulseLen));

            return r;
        }

        public DateTime TimeStamp
        {
            get; set;
        }
    }
}
