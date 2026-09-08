using System;

namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Kinematicky profil pohybu (1D). Odděluje dynamiku podvozku (accel-limitovaný rozjezd/brzdění,
    /// vazba dopředné rychlosti na dobu rotace) od logiky bodového regulátoru i plánovače dráhy —
    /// obojí ho sdílí. Viz <c>doc/path-following.md</c>.
    /// </summary>
    /// <remarks>
    /// Souřadnice/konvence: dopředná rychlost v m/s, rotační rychlost v rad/s v matematickém smyslu
    /// (proti smeru hodinových ručiček). Vše je bezstavové — čistá funkce vstupů.
    /// </remarks>
    public interface IMotionProfile
    {
        /// <summary>Maximální dovolená dopredná rychlost [m/s].</summary>
        double MaxSpeed { get; }
        /// <summary>Maximální dovolená rychlost otáčení [rad/s].</summary>
        double MaxRotationSpeed { get; }
        /// <summary>Zrychlení [m/s^2] (použito i jako decelerace).</summary>
        double Acceleration { get; }

        /// <summary>
        /// Vzdálenost, na které robot zrychlí/zpomalí z <paramref name="startSpeed"/> na
        /// <paramref name="endSpeed"/> při <see cref="Acceleration"/>.
        /// </summary>
        double Speed2Dist(double startSpeed, double endSpeed);

        /// <summary>
        /// Dopredná rychlost (akční zásah), kterou má robot jet rovně, aby z <paramref name="startSpeed"/>
        /// na vzdálenosti <paramref name="dist"/> dosáhl <paramref name="endSpeed"/>. Zrychluje
        /// (<c>start &lt; end</c>) i brzdí (<c>start &gt; end</c>).
        /// </summary>
        /// <remarks>
        /// ⚠️ Je to <b>jeden krok regulátoru</b>, ne průběh rychlosti po dráze: <paramref name="startSpeed"/>
        /// je okamžitá rychlost robotu a implementace smí být diskrétní (perioda vzorkování, rezerva).
        /// Kdo potřebuje „jak rychle smím jet v místě <c>s</c>", chce <see cref="Dist2MaxSpeed"/>.
        /// </remarks>
        RegulatorResult Dist2Speed(double dist, double startSpeed, double endSpeed);

        /// <summary>
        /// <b>Brzdná obálka</b>: nejvyšší rychlost, kterou robot smí mít ve vzdálenosti
        /// <paramref name="dist"/> <b>před</b> bodem, kde má být na <paramref name="endSpeed"/>.
        /// Nezávisí na aktuální rychlosti — je to čistě kinematický strop daný profilem.
        /// </summary>
        /// <remarks>
        /// <para>Vlastnosti, na které se smí spoléhat (hlídá <c>MotionProfileParityTests</c>):
        /// <c>Dist2MaxSpeed(0, v_e) == v_e</c>, roste s <paramref name="dist"/>, nikdy nepřesáhne
        /// <see cref="MaxSpeed"/>, a je <b>horní mezí</b> zásahu <see cref="Dist2Speed"/>.</para>
        /// <para>Pro tuhle metodu vzniklo místo tím, že se týž vzorec opisoval na třech místech
        /// (zpětný průchod v <c>PathPlanner</c>, rychlostní obálka lokálního plánovače a předpověď
        /// rampy při vyhlazování dráhy) — a plánovač si ho kvůli tomu musel modelovat sám.
        /// Viz doc/path-following.md a doc/occupancy-and-local-planning.md.</para>
        /// </remarks>
        double Dist2MaxSpeed(double dist, double endSpeed);

        /// <summary>
        /// Rotační rychlost (akční zásah), kterou má robot otáčet, aby z <paramref name="startRotSpeed"/>
        /// na úhlu <paramref name="beta"/> dosáhl <paramref name="endRotSpeed"/>.
        /// </summary>
        RegulatorResult Rot2RotSpeed(double beta, double startRotSpeed, double endRotSpeed);

        /// <summary>
        /// Omezí dopřednou rychlost tak, aby se robot stihnul natočit (vazba přes
        /// <see cref="RegulatorResult.RegulationTime"/> rotačního zásahu).
        /// </summary>
        /// <param name="speed">dopredná rychlost</param>
        /// <param name="d">vzdálenost, na které musí dojít k otočení</param>
        /// <param name="rotationResul">výsledek výpočtu rotační rychlosti</param>
        double SpeedLimit(double speed, double d, RegulatorResult rotationResul);
    }
}
