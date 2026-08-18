using System;

namespace ARBot.Common.Telemetry
{
    /// <summary>Druh uhlove veliciny ve sloupci - rozhoduje, jak se prepocita mezi konvencemi.</summary>
    public enum AngleKind
    {
        /// <summary>Neni to uhel (nebo je to naklon, ktery se konvenci netyka - pitch, roll).
        /// Hodnota se nikdy nemeni.</summary>
        None = 0,

        /// <summary>Absolutni kurz. Ulozeny je MATEMATICKY (0 = vychod, +CCW), zobrazit jde
        /// i jako azimut (0 = sever, po smeru hodinovych rucicek).</summary>
        Heading = 1,

        /// <summary>Uhlova rychlost. Ulozena je MATEMATICKY (+ = doleva); ve svetove konvenci
        /// je kladne otaceni doprava, takze se prepocet omezuje na obraceni znamenka.</summary>
        Rate = 2,
    }

    /// <summary>Konvence, ve ktere se uhlove udaje ZOBRAZUJI.</summary>
    public enum AngleMode
    {
        /// <summary>Matematicka: kurz 0 = vychod, +CCW; kladna rychlost = doleva. Konvence celeho
        /// projektu (viz doc/imu-and-frames.md), tedy tatáz cisla, jaka jsou ve zpravach.</summary>
        Math = 0,

        /// <summary>Svetova: kurz jako azimut (0 = sever, po smeru hodinovych rucicek), kladna
        /// rychlost = doprava. Srovnatelne s kompasem a mapou.</summary>
        World = 1,
    }

    /// <summary>
    /// Prepocet uhlovych udaju mezi konvencemi. <b>Ulozena hodnota je vzdy matematicka a ve
    /// stupnich</b>; prepocet se deje az pri zobrazeni, takze prepnuti rezimu nemeni data.
    ///
    /// <para>Duvod, proc to je na jednom miste: v telemetrii se potkava kurz z fuze (matematicky),
    /// kurz z IMU (matematicky) i kurz z GPS (prijimac ho hlasi jako azimut) a uhlove rychlosti.
    /// Kdyby si kazdy sloupec prevadel po svem, tabulka by michala dve konvence - presne to se
    /// stalo drive u sloupce s GPS kurzem. Viz doc/telemetry-view.md.</para>
    /// </summary>
    public static class AnglePresentation
    {
        /// <summary>
        /// Prevede ulozenou (matematickou) hodnotu do zvolene konvence.
        /// </summary>
        /// <param name="value">Ulozena hodnota [° nebo °/s], matematicka konvence.</param>
        /// <param name="kind">Druh veliciny - co se s ni smi delat.</param>
        /// <param name="mode">Konvence zobrazeni.</param>
        public static double Present(double value, AngleKind kind, AngleMode mode)
        {
            switch (kind)
            {
                case AngleKind.Heading:
                    return mode == AngleMode.World
                        ? NormalizeAzimuth(90.0 - value)      // matematicky uhel -> azimut
                        : NormalizeSigned(value);

                case AngleKind.Rate:
                    return mode == AngleMode.World ? -value : value;

                default:
                    return value;
            }
        }

        /// <summary>Uhel do (-180, 180] - matematicke kurzy se ctou jako odchylka od vychodu.</summary>
        private static double NormalizeSigned(double deg)
        {
            deg %= 360.0;
            if (deg > 180.0) deg -= 360.0;
            if (deg <= -180.0) deg += 360.0;
            return deg;
        }

        /// <summary>Azimut do [0, 360) - kompas zaporne stupne nezna.</summary>
        private static double NormalizeAzimuth(double deg)
        {
            deg %= 360.0;
            if (deg < 0) deg += 360.0;
            return deg;
        }
    }
}
