namespace ARBot.Common.Diagnostics
{
    /// <summary>
    /// Stav a vykon jednoho stupne pipeline za interval sberu.
    ///
    /// <para><b>Zahozene zpravy jsou to hlavni.</b> Stupne bezi na vlastnich vlaknech s frontou
    /// a politikou preteceni (DropOldest/DropNewest) - dosud se ale zahozeni nikde nepocitalo,
    /// takze stupen mohl tise ztracet data a nikdo to nepoznal. Viz doc/perf-monitoring.md.</para>
    /// </summary>
    public readonly struct StageSnapshot
    {
        public string Name { get; init; }
        /// <summary>Aktualni delka fronty (STAV, ne prirustek).</summary>
        public int QueueLength { get; init; }
        /// <summary>Zpracovanych zprav za interval.</summary>
        public long Processed { get; init; }
        /// <summary>ZAHOZENYCH zprav za interval.</summary>
        public long Dropped { get; init; }
        /// <summary>Prumerna doba zpracovani jedne zpravy [ms] za interval.</summary>
        public double AvgMs { get; init; }
        /// <summary>Nejdelsi zpracovani jedne zpravy [ms] za interval.</summary>
        public double MaxMs { get; init; }
    }
}
