using ARBot.Common.Coordinates;

namespace ARBot.HAL
{
    /// <summary>
    /// <b>IMU, které umí dostat model magnetického a gravitačního pole pro dané místo.</b>
    ///
    /// <para><b>Proč vlastní rozhraní a ne <see cref="IIMU"/>.</b> <c>IIMU</c> implementuje
    /// i T265, která magnetometr vůbec nemá — model pole je pro ni nesmysl. Zároveň runtime
    /// nemá vědět, jestli je zrovna připojená ASCII nebo binární verze VN100 driveru; obě
    /// tohle rozhraní implementují, takže stačí <c>(hw.IMU as IMagneticModel)?.SetModelParams(lla)</c>.</para>
    ///
    /// <para><b>Zatím to nikdo nevolá</b> — je to připravené na chvíli, kdy se bude řešit
    /// deklinace (viz doc/imu-and-frames.md). Volat to má smysl teprve tehdy, až je známá
    /// poloha robota, tedy po prvním kvalitním fixu GPS, ne při startu.</para>
    /// </summary>
    public interface IMagneticModel
    {
        /// <summary>
        /// Nastaví senzoru model pole pro danou polohu a aktuální datum.
        ///
        /// <para>Kurz z magnetometru je bez toho azimut k <b>magnetickému</b> severu; s modelem
        /// si senzor dopočítá referenční pole včetně <b>deklinace</b>, takže hlásí kurz
        /// k pravému severu.</para>
        ///
        /// <para>⚠️ Zápis je <b>nestálý</b> (po vypnutí senzoru je pryč) — trvalé uložení
        /// (<c>VNWNV</c>) je vědomý ruční krok, viz <c>deploy/vnrestore.sh</c>.</para>
        /// </summary>
        /// <param name="lla">Poloha robota; zeměpisné souřadnice <b>v radiánech</b>.</param>
        void SetModelParams(LLA lla);
    }
}
