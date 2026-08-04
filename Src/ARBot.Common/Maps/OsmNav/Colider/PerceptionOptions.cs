using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>
/// Ladicí parametry predikce trajektorie a detekce kolizí.
/// </summary>
/// <param name="SigmaK">Počet směrodatných odchylek pro nafouknutí koridoru o nejistotu.</param>
/// <param name="TimeStepSeconds">Krok integrace trajektorie [s].</param>
/// <param name="MinHorizonSeconds">Minimální časový horizont (× rychlost) [s].</param>
/// <param name="MinHorizonMeters">Minimální délka horizontu [m].</param>
/// <param name="ReactionTimeSeconds">Reakční doba před zahájením brzdění [s].</param>
/// <param name="SafetyMarginMeters">Bezpečnostní rezerva za bodem zastavení [m].</param>
/// <param name="IncludeAcceleration">Zohlednit náběh rychlosti přes <c>MaxAcceleration</c>.</param>
public sealed record PerceptionOptions(
    double SigmaK = 3.0,
    double TimeStepSeconds = 0.1,
    double MinHorizonSeconds = 1.0,
    double MinHorizonMeters = 2.0,
    double ReactionTimeSeconds = 0.3,
    double SafetyMarginMeters = 0.5,
    bool IncludeAcceleration = true);
