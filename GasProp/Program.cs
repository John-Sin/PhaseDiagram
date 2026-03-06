using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

// ============================================================
// EXAMPLE 1: Pseudo-Critical from Gravity
// ============================================================

Console.ForegroundColor = ConsoleColor.DarkGreen;
Console.WriteLine("=== Pseudo-Critical from Gravity ===\n");

var gravityResult = PseudoCriticalFromGravity.Calculate(
    gasGravity: 0.75,
    yH2S: 0.04,
    yCO2: 0.04,
    yN2: 0.02,
    yH2O: 0.0
);

Console.WriteLine($"gamma_h      = {gravityResult.GammaH:F4}");
Console.WriteLine($"Tpch/Ppch    = {gravityResult.Tpch_R:F2} °R, {gravityResult.Ppch_psia:F2} psia");
Console.WriteLine($"Tpc/Ppc mix  = {gravityResult.Tpc_Mix_R:F2} °R, {gravityResult.Ppc_Mix_psia:F2} psia");
Console.WriteLine($"After W-A    = {gravityResult.Tpc_WA_R:F2} °R, {gravityResult.Ppc_WA_psia:F2} psia");
Console.WriteLine($"Final (Casey)= {gravityResult.Tpc_Final_R:F2} °R, {gravityResult.Ppc_Final_psia:F2} psia");

// ============================================================
// EXAMPLE 2: Pseudo-Critical from Composition
// ============================================================

Console.ForegroundColor = ConsoleColor.DarkYellow;
Console.WriteLine("\n=== Pseudo-Critical from Composition ===\n");

var gasComposition = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
{
    ["C1"] = 0.7,
    ["C2"] = 0.08,
    ["C3"] = 0.06,
    ["iC4"] = 0.04,
    ["nC4"] = 0.04,
    ["iC5"] = 0.02,
    ["nC5"] = 0.02,
    ["C6"] = 0.02,
    ["C7+"] = 0.02,
    ["CO2"] = 0.0,
    ["H2S"] = 0.0,
    ["N2"] = 0.0,
    ["H2O"] = 0.0
};

var compositionResult = PseudoCritical.Calculate(gasComposition);

Console.WriteLine($"Uncorrected:  Tpc={compositionResult.Tpc_Uncorr_R:F2} °R, Ppc={compositionResult.Ppc_Uncorr_psia:F2} psia");
Console.WriteLine($"After W-A:    Tpc={compositionResult.Tpc_WichertAziz_R:F2} °R, Ppc={compositionResult.Ppc_WichertAziz_psia:F2} psia");
Console.WriteLine($"Final (Casey):Tpc={compositionResult.Tpc_Final_R:F2} °R, Ppc={compositionResult.Ppc_Final_psia:F2} psia");

Console.ResetColor();

// ============================================================
// PSEUDO-CRITICAL CALCULATIONS
// ============================================================

/// <summary>
/// Calculate pseudo-critical properties from molar composition.
/// Applies Kay's mixing rule, Wichert-Aziz (CO2+H2S), and Casey (N2+H2O) corrections.
/// </summary>
public static class PseudoCritical
{
    public readonly record struct Crit(double TcR, double PcPsia);

    public sealed record Result(
        double Tpc_Uncorr_R,
        double Ppc_Uncorr_psia,
        double Tpc_WichertAziz_R,
        double Ppc_WichertAziz_psia,
        double Tpc_Final_R,
        double Ppc_Final_psia
    );

    private static readonly Dictionary<string, Crit> CritProps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CO2"] = new Crit(TcR: 547.58, PcPsia: 1071.0),
        ["H2S"] = new Crit(TcR: 672.35, PcPsia: 1306.0),
        ["N2"] = new Crit(TcR: 239.26, PcPsia: 507.5),
        ["H2O"] = new Crit(TcR: 1164.67, PcPsia: 3206.2),
        ["C1"] = new Crit(TcR: 343.0, PcPsia: 666.4),
        ["C2"] = new Crit(TcR: 549.59, PcPsia: 706.5),
        ["C3"] = new Crit(TcR: 665.73, PcPsia: 616.0),
        ["nC4"] = new Crit(TcR: 765.29, PcPsia: 550.6),
        ["iC4"] = new Crit(TcR: 734.13, PcPsia: 527.9),
        ["nC5"] = new Crit(TcR: 1305.14, PcPsia: 488.6),
        ["iC5"] = new Crit(TcR: 1288.44, PcPsia: 490.4),
        ["C6"] = new Crit(TcR: 913.27, PcPsia: 436.9),
        ["C7+"] = new Crit(TcR: 1005.25, PcPsia: 375.7),
    };

    public static Result Calculate(
        IReadOnlyDictionary<string, double> y,
        Crit? overrideC7Plus = null,
        bool normalize = true,
        double normalizeTolerance = 1e-6)
    {
        if (y is null || y.Count == 0)
            throw new ArgumentException("Composition is empty.");

        var comp = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in y)
        {
            if (v < 0) throw new ArgumentException($"Negative mole fraction for {k}.");
            if (v == 0) continue;
            comp[k.Trim()] = v;
        }

        if (overrideC7Plus is not null)
            CritProps["C7+"] = overrideC7Plus.Value;

        double sum = comp.Values.Sum();
        if (sum <= 0)
            throw new ArgumentException("Composition sums to zero.");

        if (normalize)
        {
            foreach (var k in comp.Keys.ToList())
                comp[k] /= sum;
        }
        else if (Math.Abs(sum - 1.0) > normalizeTolerance)
        {
            throw new ArgumentException(
                $"Composition must sum to 1.0 (got {sum:F6}). Set normalize=true to auto-normalize.");
        }

        double Tpc = 0.0, Ppc = 0.0;
        foreach (var (name, yi) in comp)
        {
            if (!CritProps.TryGetValue(name, out var cp))
                throw new ArgumentException($"Unsupported component '{name}'. Add its critical properties.");
            Tpc += yi * cp.TcR;
            Ppc += yi * cp.PcPsia;
        }

        double Tpc0 = Tpc, Ppc0 = Ppc;

        double yH2S = comp.GetValueOrDefault("H2S", 0.0);
        double yCO2 = comp.GetValueOrDefault("CO2", 0.0);
        double A = yH2S + yCO2;
        double B = yH2S;

        if (A > 0.0)
        {
            double eps = 120.0 * (Math.Pow(A, 0.9) - Math.Pow(A, 1.6))
                       + 15.0 * (Math.Pow(B, 0.5) - Math.Pow(B, 4.0));
            double Tpc1 = Tpc - eps;
            double denom = Tpc + B * (1.0 - B) * eps;

            if (denom <= 0.0)
                throw new InvalidOperationException($"Invalid Wichert-Aziz denominator (<=0). denom={denom}");

            double Ppc1 = (Ppc * Tpc1) / denom;
            Tpc = Tpc1;
            Ppc = Ppc1;
        }

        double TpcWA = Tpc, PpcWA = Ppc;

        double yN2 = comp.GetValueOrDefault("N2", 0.0);
        double yH2O = comp.GetValueOrDefault("H2O", 0.0);
        double yImp = yN2 + yH2O;

        if (yImp > 0.0)
        {
            if (yImp >= 1.0)
                throw new ArgumentException("N2+H2O mole fraction must be < 1.");

            var n2 = CritProps["N2"];
            var h2o = CritProps["H2O"];

            double dT = -246.1 * yN2 + 400.0 * yH2O;
            double dP = -162.0 * yN2 + 1270.0 * yH2O;

            Tpc = (Tpc - n2.TcR * yN2 - h2o.TcR * yH2O) / (1.0 - yImp) + dT;
            Ppc = (Ppc - n2.PcPsia * yN2 - h2o.PcPsia * yH2O) / (1.0 - yImp) + dP;
        }

        return new Result(Tpc0, Ppc0, TpcWA, PpcWA, Tpc, Ppc);
    }
}

public static class PseudoCriticalFromGravity
{
    public sealed record Result(
        double GammaH, double Tpch_R, double Ppch_psia,
        double Tpc_Mix_R, double Ppc_Mix_psia,
        double Tpc_WA_R, double Ppc_WA_psia,
        double Tpc_Final_R, double Ppc_Final_psia
    );

    public static Result Calculate(
        double gasGravity,
        double yH2S = 0.0, double yCO2 = 0.0,
        double yN2 = 0.0, double yH2O = 0.0,
        bool clampToZero = true)
    {
        if (gasGravity <= 0)
            throw new ArgumentOutOfRangeException(nameof(gasGravity), "Gas gravity must be > 0.");

        if (yH2S < 0 || yCO2 < 0 || yN2 < 0 || yH2O < 0)
            throw new ArgumentOutOfRangeException("Mole fractions cannot be negative.");

        double yImp = yH2S + yCO2 + yN2 + yH2O;
        if (yImp >= 1.0)
            throw new ArgumentOutOfRangeException("Sum of impurity mole fractions must be < 1.0.");

        double gammaH = (gasGravity - 1.1767 * yH2S - 1.5196 * yCO2 - 0.9672 * yN2 - 0.6220 * yH2O) / (1.0 - yImp);

        if (gammaH <= 0)
            throw new InvalidOperationException($"Computed hydrocarbon gravity <= 0 (gammaH={gammaH:F4}).");

        double Ppch = 756.8 - 131.0 * gammaH - 3.6 * gammaH * gammaH;
        double Tpch = 169.2 + 349.5 * gammaH - 74.0 * gammaH * gammaH;

        const double Pc_H2S = 1306.0, Tc_H2S = 672.35;
        const double Pc_CO2 = 1071.0, Tc_CO2 = 547.58;
        const double Pc_N2 = 493.0, Tc_N2 = 227.16;
        const double Pc_H2O = 3200.1, Tc_H2O = 1164.9;

        double Ppc = (1.0 - yImp) * Ppch + Pc_H2S * yH2S + Pc_CO2 * yCO2 + Pc_N2 * yN2 + Pc_H2O * yH2O;
        double Tpc = (1.0 - yImp) * Tpch + Tc_H2S * yH2S + Tc_CO2 * yCO2 + Tc_N2 * yN2 + Tc_H2O * yH2O;

        double TpcMix = Tpc, PpcMix = Ppc;

        double A = yH2S + yCO2;
        double B = yH2S;

        if (A > 0.0)
        {
            double eps = 120.0 * (Math.Pow(A, 0.9) - Math.Pow(A, 1.6)) + 15.0 * (Math.Pow(B, 0.5) - Math.Pow(B, 4.0));
            double TpcWA = Tpc - eps;
            double denom = Tpc + B * (1.0 - B) * eps;

            if (denom <= 0.0)
                throw new InvalidOperationException($"Invalid Wichert-Aziz denominator. denom={denom}");

            double PpcWA = (Ppc * TpcWA) / denom;
            Tpc = TpcWA;
            Ppc = PpcWA;
        }

        double TpcAfterWA = Tpc, PpcAfterWA = Ppc;

        double yImpCasey = yN2 + yH2O;
        if (yImpCasey > 0.0)
        {
            double dT = -246.1 * yN2 + 400.0 * yH2O;
            double dP = -162.0 * yN2 + 1270.0 * yH2O;

            Tpc = (Tpc - Tc_N2 * yN2 - Tc_H2O * yH2O) / (1.0 - yImpCasey) + dT;
            Ppc = (Ppc - Pc_N2 * yN2 - Pc_H2O * yH2O) / (1.0 - yImpCasey) + dP;
        }

        if (clampToZero)
        {
            if (Tpc < 0) Tpc = 0;
            if (Ppc < 0) Ppc = 0;
        }

        return new Result(gammaH, Tpch, Ppch, TpcMix, PpcMix, TpcAfterWA, PpcAfterWA, Tpc, Ppc);
    }
}