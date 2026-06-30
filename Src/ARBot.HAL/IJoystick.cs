using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL
{
    /// <summary>
    /// Rozhrani pro krizovy ovladac
    /// </summary>
    public interface IJoystick
    {
        double RotationVelocity { get; set; }
        double ForwardVelocity { get; set; }
        bool Button1 { get; set; }
        bool Button2 { get; set; }
        bool Button3 { get; set; }
        bool Button4 { get; set; }

        void Read();
    }
}
