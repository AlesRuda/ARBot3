using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL
{
    public class ControlRegisterGPIO:IGPIO
    {
        IMMR mmr;
        int adr;
        uint mask;

        public ControlRegisterGPIO(IMMR mmr, int adr, uint mask)
        {
            this.mmr = mmr;
            this.adr = adr;
            this.mask = mask;
        }
        public bool Value
        {
            get
            {
                return (RegisterFile.Read(mmr, adr) & mask) != 0;
            }
            set
            {
                if (value)
                    RegisterFile.Set(mmr, adr, mask);
                else
                    RegisterFile.Clear(mmr, adr, mask);
            }
        }

        public bool IsOutput
        {
            get
            {
                return true;
            }
            set
            {
                if(!value)
                    throw new NotSupportedException();
            }
        }

        public GPIOEdge Edge
        {
            get
            {
                return GPIOEdge.None;
            }
            set
            {
                if (value != GPIOEdge.None)
                    throw new NotSupportedException();
            }
        }
    }
}
