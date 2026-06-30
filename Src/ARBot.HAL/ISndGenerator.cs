using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL
{
    public interface ISndGenerator
    {
        void Break();
        void EmergencyStop();
        void Go();
    }
}
