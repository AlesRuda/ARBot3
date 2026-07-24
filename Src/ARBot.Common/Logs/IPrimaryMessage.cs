namespace ARBot.Common.Logs
{
    /// <summary>
    /// Marker suroveho (primarniho) vstupu pipeline: senzorova mereni (potomci
    /// <c>SensorStateBase</c>) a externi prikazy. Router primarni zpravu posle
    /// do zpracovani i na Stream; odvozene (bez tohoto markeru) jen na Stream.
    /// Prazdny marker - nenese zadne cleny.
    /// </summary>
    public interface IPrimaryMessage
    {
    }
}
