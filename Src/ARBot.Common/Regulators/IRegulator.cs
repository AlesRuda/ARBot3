using ARBot.Common.Models;
using System;
namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Rozhrani pro regulator
    /// </summary>
    public interface IRegulator
    {
        /// <summary>
        /// Spocte na jake vzdalenosti zrychli ze startSpeed na endSpeed.
        /// </summary>
        /// <param name="startSpeed"></param>
        /// <param name="endSpeed"></param>
        /// <returns></returns>
        double Speed2Dist(double startSpeed, double endSpeed);
        /// <summary>
        /// Spocte rychlost (akcni zasah), kterou by mel jet rovne robot aby z pocatecni rychlosti na vzdalenosti dist dosahnul koncovou rychlost.
        /// </summary>
        /// <param name="dist"></param>
        /// <param name="startSpeed"></param>
        /// <param name="endSpeed"></param>
        /// <returns></returns>
        RegulatorResult Dist2Speed(double dist, double startSpeed, double endSpeed);
        /// <summary>
        /// Spocte rotacni rychlost (akcni zasah), kterou by mel robot otacet aby z pocatecni rychlosti na vzdalenosti uhlu beta dosahnul koncovou rychlost.
        /// </summary>
        /// <param name="beta"></param>
        /// <param name="startRotSpeed"></param>
        /// <param name="endRotSpeed"></param>
        /// <returns></returns>
        RegulatorResult Rot2RotSpeed(double beta, double startRotSpeed, double endRotSpeed);
        /// <summary>
        /// Spocte regulacni zasah pro projeti zadanymi body
        /// </summary>
        /// <param name="state"></param>
        /// <param name="points"></param>
        /// <returns></returns>
        RegulatorResult Control(IModelState state, RegulatorWayPoint[] points);

        /// <summary>
        /// Omezi doprednou rychlost na zaklade rychlosti rotace
        /// </summary>
        /// <param name="speed">dopredna rychlost </param>
        /// <param name="d">vzdalenost na ktere musi dojit k otoceni</param>
        /// <param name="rotationResul">Vysledek vypoctu rotacni rychlosti</param>
        /// <returns></returns>
        double SpeedLimit(double speed, double d, RegulatorResult rotationResul);
        /// <summary>
        /// Maximalni pocet bodu na ktere regulator reguluje
        /// </summary>
        int MaxWayPoints { get; }
    }
}
