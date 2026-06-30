using System;
using System.Collections.Generic;
using System.Text;

namespace ARBot.HAL
{
    /// <summary>
    /// Bus of GPIO array
    /// </summary>
    public class GPIOBus:IBus
    {
        IGPIO[] gpios;
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="gpios"></param>
        public GPIOBus(params IGPIO[] gpios)
        {
            this.gpios = gpios;
        }

        /// <summary>
        /// Value of the bus
        /// </summary>
        public int Value
        {
            get
            {
                int v = 0;
                for (int i = 0; i < gpios.Length; i++)
                {
                    IGPIO g = gpios[i];
                    if (g != null && g.Value)
                    {
                        v |= 1 << i;
                    }
                }
                return v;
            }
            set
            {
                int v = value;
                for (int i = 0; i < gpios.Length; i++)
                {
                    IGPIO g = gpios[i];
                    if (g != null)
                    {
                        g.Value=(v&(1<<i))!=0;
                    }
                }
            }
        }

        /// <summary>
        /// Direction
        /// </summary>
        public bool? IsOutput
        {
            get
            {
                bool? v = null;
                for (int i = 0; i < gpios.Length; i++)
                {
                    IGPIO g = gpios[i];
                    if (g != null)
                    {
                        if (v == null)
                            v = g.IsOutput;
                        else
                            if (v != g.IsOutput)
                                return null;
                    }
                }
                return v;
            }
            set
            {
                if (value != null)
                {
                    for (int i = 0; i < gpios.Length; i++)
                    {
                        IGPIO g = gpios[i];
                        if (g != null)
                            g.IsOutput = value.Value;
                    }
                }
            }
        }

        /// <summary>
        /// Edge sensitivity
        /// </summary>
        public GPIOEdge? Edge
        {
            get
            {
                GPIOEdge? v = null;
                for (int i = 0; i < gpios.Length; i++)
                {
                    IGPIO g = gpios[i];
                    if (g != null)
                    {
                        if (v == null)
                            v = g.Edge;
                        else
                            if (v != g.Edge)
                                return null;
                    }
                }
                return v;
            }
            set
            {
                if (value != null)
                {
                    for (int i = 0; i < gpios.Length; i++)
                    {
                        IGPIO g = gpios[i];
                        if (g != null)
                            g.Edge = value.Value;
                    }
                }
            }
        }
    }
}
