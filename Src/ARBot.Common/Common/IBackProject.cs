using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Rozhrani pro prevod barevneho obrazku na pravdepodobnostni.
    /// </summary>
    public interface IBackProject
    {
        /// <summary>
        /// Prevede barevny obrazek na pravdepodobnostni.
        /// </summary>
        /// <param name="srcImg"></param>
        /// <param name="destImg"></param>
        void Process(Image<BGR32> srcImg, Image<Gray> destImg);
        /// <summary>
        /// Spocte velikost pravdepodobnostniho obrazku pro velikost vstupniho barevneho obrazku.
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        Size Size(int width, int height);
    }
}
