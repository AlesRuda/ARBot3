using System;
namespace ARBot.Common.LocalMaps
{
    public interface ILocalMap
    {
        ARBot.Common.Common.Point Center { get; set; }
        int Height { get; }
        int Width { get; }
        void Move(int xd, int yd);
        double Resolution { get; }
        /// <summary>
        /// Zpristupnuje pixely s pocatkem v miste robota (Center)
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        BayesPixel this[int x, int y] { get; }
        /// <summary>
        /// Zpristupnuje pixely s pocatkem v miste robota (Center)
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        BayesPixel this[double x, double y] { get; }
        ARBot.Common.Logs.ImageMsg ToLogMessage(string name);
        /// <summary>
        /// Aktualizuje lokalni mapu podle jine lokalni mapy
        /// </summary>
        /// <param name="lm"></param>
        /// <param name="scale"></param>
        void Update(ILocalMap lm, double scale);
        /// <summary>
        /// Aktualizuje lokalni mapu podle jine lokalni mapy
        /// </summary>
        /// <param name="lms"></param>
        void Update(params ILocalMap[] lms);
    }
}
