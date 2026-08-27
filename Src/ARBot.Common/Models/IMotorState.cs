using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.Common.Models
{
    /// <summary>
    /// Motor control unit base information.
    /// </summary>
    public interface IMotorState
    {
        /// <summary>
        /// Emergency stop
        /// </summary>
        bool IsEmergencyStop { get; }
        /// <summary>
        /// Left encoder integral distance
        /// </summary>
        double LeftEncoder { get; }
        /// <summary>
        /// Right encoder integral distance
        /// </summary>
        double RightEncoder { get; }

        /// <summary>
        /// Nese tenhle ramec <b>skutecne merenie</b>? <c>false</c> = zastupny ramec, ktery driver
        /// vyrobil po chybe (nedostupny port, neparsovatelna odpoved) — plati z nej <b>jen</b>
        /// <see cref="IsEmergencyStop"/> (ten je fail-safe nastaveny na <c>true</c>), vsechno ostatni
        /// jsou nuly, ktere nikdo nemeril.
        ///
        /// <para><b>Proc to nejde poznat podle stopu:</b> pod nouzovym zastavenim je nulova rychlost
        /// <i>plnohodnotne merenie</i> (ridici jednotka ma prikaz stat a motory jsou rizene pozicne),
        /// zatimco po chybe parsovani je nula <i>vymysl</i>. Kdo to nerozlisi, posle fuzi „stojim"
        /// prave v okamziku, kdy o robotu nevi nic — a robot se pritom muze pohybovat.</para>
        ///
        /// <para>Vychozi je <c>true</c> (a je to <b>default interface implementation</b>), aby
        /// pridani priznaku nezmenilo chovani zadneho existujiciho implementatora.</para>
        /// </summary>
        bool HasMeasurement => true;

        /// <summary>
        /// Left wheel speed in m/s
        /// </summary>
        double LeftWheelSpeed { get; }
        /// <summary>
        /// Right wheel speed in m/s
        /// </summary>
        double RightWheelSpeed { get; }
        
        
        /// <summary>
        /// Voltage
        /// </summary>
        double Voltage { get; }
        /// <summary>
        /// Left motor current
        /// </summary>
        double LeftMotorCurrent { get; }
        /// <summary>
        /// Right motor current
        /// </summary>
        double RightMotorCurrent { get; }
    }
}
