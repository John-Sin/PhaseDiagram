namespace PhaseDiagram.Services.Thermo;

/// <summary>
/// Abstraction interface for CoolProp thermodynamic calculations.
/// Allows switching between different CoolProp wrappers (SharpProp, CoolProp.NET, etc.)
/// or mock implementations for testing.
/// </summary>
public interface ICoolPropWrapper
{
    /// <summary>
    /// Creates a thermodynamic state for the specified backend and fluid mixture.
    /// </summary>
    /// <param name="backend">EOS backend: "PR" (Peng-Robinson) or "SRK" (Soave-Redlich-Kwong)</param>
    /// <param name="fluidString">CoolProp fluid string: "Methane&Ethane&Propane"</param>
    /// <returns>Thermodynamic state interface</returns>
    IThermodynamicState CreateState(string backend, string fluidString);

    /// <summary>
    /// Gets critical properties for a pure component.
    /// </summary>
    ComponentCriticalProperties GetCriticalProperties(string componentName);
}

/// <summary>
/// Represents a thermodynamic state for phase equilibrium calculations.
/// </summary>
public interface IThermodynamicState : IDisposable
{
    /// <summary>
    /// Sets the mole fractions for the mixture.
    /// </summary>
    void SetMoleFractions(double[] moleFractions);

    /// <summary>
    /// Updates the state with specified inputs.
    /// </summary>
    /// <param name="input1">First input parameter (temperature or pressure)</param>
    /// <param name="input2">Second input parameter (temperature or pressure)</param>
    /// <param name="inputType">Type of inputs (PT = pressure-temperature)</param>
    void Update(double input1, double input2, InputPairType inputType);

    /// <summary>
    /// Gets the vapor quality (0 = pure liquid, 1 = pure vapor).
    /// Returns NaN if not in two-phase region.
    /// </summary>
    double Quality { get; }

    /// <summary>
    /// Gets the current pressure (Pa).
    /// </summary>
    double Pressure { get; }

    /// <summary>
    /// Gets the current temperature (K).
    /// </summary>
    double Temperature { get; }

    /// <summary>
    /// Gets the molar density (mol/m³).
    /// </summary>
    double MolarDensity { get; }

    /// <summary>
    /// Gets the compressibility factor (Z-factor).
    /// </summary>
    double CompressibilityFactor { get; }

    /// <summary>
    /// Performs a flash calculation to determine phase equilibrium.
    /// </summary>
    /// <param name="spec1">First specification (e.g., pressure)</param>
    /// <param name="spec2">Second specification (e.g., temperature)</param>
    /// <param name="flashType">Type of flash calculation</param>
    /// <returns>Flash calculation result</returns>
    FlashResult Flash(double spec1, double spec2, FlashType flashType);

    /// <summary>
    /// Gets fugacity coefficients for all components in current state.
    /// </summary>
    double[] FugacityCoefficients { get; }
}

/// <summary>
/// Input pair types for state updates.
/// </summary>
public enum InputPairType
{
    /// <summary>Pressure-Temperature</summary>
    PT,
    /// <summary>Pressure-Enthalpy</summary>
    PH,
    /// <summary>Pressure-Entropy</summary>
    PS,
    /// <summary>Temperature-Quality</summary>
    TQ,
    /// <summary>Pressure-Quality</summary>
    PQ
}

/// <summary>
/// Flash calculation types.
/// </summary>
public enum FlashType
{
    /// <summary>Pressure-Temperature flash</summary>
    PT,
    /// <summary>Pressure-Enthalpy flash</summary>
    PH,
    /// <summary>Temperature-Vapor fraction flash</summary>
    TV
}

/// <summary>
/// Result of a flash calculation.
/// </summary>
public sealed record FlashResult(
    double Temperature,
    double Pressure,
    double VaporFraction,
    double[] LiquidComposition,
    double[] VaporComposition,
    bool IsStable
);

/// <summary>
/// Critical properties for a pure component.
/// </summary>
public sealed record ComponentCriticalProperties(
    string Name,
    double CriticalTemperature,  // K
    double CriticalPressure,     // Pa
    double AcentricFactor,
    double MolarMass             // kg/mol
);