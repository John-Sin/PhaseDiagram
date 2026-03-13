namespace Reflex.Modules.PhaseDiagram.Services.Thermo;

/// <summary>
/// Unit conversion utilities for petroleum engineering thermodynamics.
/// </summary>
public static class ThermoUnitConverter
{
    // Pressure conversions
    public const double PSI_TO_PA = 6894.757293168;
    public const double PA_TO_PSI = 1.0 / PSI_TO_PA;
    public const double BAR_TO_PA = 100_000.0;
    public const double PA_TO_BAR = 1.0 / 100_000.0;
    public const double ATM_TO_PA = 101_325.0;

    // Temperature conversions
    public const double KELVIN_TO_RANKINE = 1.8;
    public const double RANKINE_TO_KELVIN = 1.0 / 1.8;
    public const double CELSIUS_TO_KELVIN = 273.15;

    /// <summary>
    /// Converts Fahrenheit to Kelvin.
    /// </summary>
    public static double FahrenheitToKelvin(double fahrenheit)
        => (fahrenheit - 32.0) * 5.0 / 9.0 + CELSIUS_TO_KELVIN;

    /// <summary>
    /// Converts Kelvin to Fahrenheit.
    /// </summary>
    public static double KelvinToFahrenheit(double kelvin)
        => (kelvin - CELSIUS_TO_KELVIN) * 9.0 / 5.0 + 32.0;

    /// <summary>
    /// Converts psia to Pascals.
    /// </summary>
    public static double PsiaToPascal(double psia)
        => psia * PSI_TO_PA;

    /// <summary>
    /// Converts Pascals to psia.
    /// </summary>
    public static double PascalToPsia(double pascal)
        => pascal * PA_TO_PSI;

    /// <summary>
    /// Converts array of (T_K, P_Pa) to (T_°F, P_psia).
    /// </summary>
    public static (double T_F, double P_psia)[] ConvertToOilfieldUnits(
        (double T_K, double P_Pa)[] siPoints)
    {
        return siPoints
            .Select(p => (KelvinToFahrenheit(p.T_K), PascalToPsia(p.P_Pa)))
            .ToArray();
    }
}