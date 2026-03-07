namespace PhaseDiagram.Services.Thermo;

/// <summary>
/// Phase envelope service using CoolProp PR/SRK build_phase_envelope.
/// DEFAULT UNITS: Pressure in psia, Temperature in °F (Field/Oilfield Units)
/// </summary>
public sealed class CoolPropPhaseEnvelopeService
{
    #region DTOs

    public sealed record PhaseEnvelopeRequest(
        string Backend, string[] Components, double[] Z,
        double TminF, double TmaxF, int Points, double[] LiquidLines);

    public sealed record PhaseEnvelopeResult(
        (double T_F, double P_psia)[] Bubble,
        (double T_F, double P_psia)[] Dew,
        Dictionary<double, (double T_F, double P_psia)[]> LiquidLines,
        (double Tcrit_F, double Pcrit_psia)? Critical,
        string Backend, string[] Components, double[] Composition);

    #endregion

    #region Constants

    private const double PA_TO_PSI = 1.0 / 6894.757293168;
    private const double F_TO_K_OFFSET = 459.67;
    private const double F_TO_K_SCALE = 5.0 / 9.0;

    // T-points sampled along the dew curve per dropout line.
    private const int LiquidLineMaxPoints = 20;

    // Bisection iterations per temperature point.
    private const int BisectMaxIter = 25;

    // Maximum consecutive non-two-phase results before aborting bisection.
    private const int MaxConsecutiveMisses = 4;

    // Timeout for a single PT flash call (ms). CoolProp cubic EOS PT flashes
    // near phase boundaries can hang indefinitely; this prevents that.
    private const int FlashTimeoutMs = 2000;

    // CoolProp's C API uses a global handle registry — serialise all calls.
    private static readonly SemaphoreSlim _coolPropLock = new(1, 1);

    #endregion

    #region Component Name Mapping

    private static readonly Dictionary<string, string> ComponentMapping =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["C1"] = "Methane",
            ["Methane"] = "Methane",
            ["C2"] = "Ethane",
            ["Ethane"] = "Ethane",
            ["C3"] = "Propane",
            ["Propane"] = "Propane",
            ["iC4"] = "IsoButane",
            ["i-C4"] = "IsoButane",
            ["Isobutane"] = "IsoButane",
            ["nC4"] = "n-Butane",
            ["n-C4"] = "n-Butane",
            ["Butane"] = "n-Butane",
            ["iC5"] = "Isopentane",
            ["i-C5"] = "Isopentane",
            ["Isopentane"] = "Isopentane",
            ["nC5"] = "n-Pentane",
            ["n-C5"] = "n-Pentane",
            ["Pentane"] = "n-Pentane",
            ["C6"] = "n-Hexane",
            ["nC6"] = "n-Hexane",
            ["Hexane"] = "n-Hexane",
            ["C7"] = "n-Heptane",
            ["nC7"] = "n-Heptane",
            ["Heptane"] = "n-Heptane",
            ["C8"] = "n-Octane",
            ["nC8"] = "n-Octane",
            ["Octane"] = "n-Octane",
            ["C9"] = "n-Nonane",
            ["nC9"] = "n-Nonane",
            ["Nonane"] = "n-Nonane",
            ["C10"] = "n-Decane",
            ["nC10"] = "n-Decane",
            ["Decane"] = "n-Decane",
            ["CO2"] = "CarbonDioxide",
            ["CarbonDioxide"] = "CarbonDioxide",
            ["N2"] = "Nitrogen",
            ["Nitrogen"] = "Nitrogen",
            ["H2S"] = "HydrogenSulfide",
            ["HydrogenSulfide"] = "HydrogenSulfide",
            ["H2O"] = "Water",
            ["Water"] = "Water",
        };

    #endregion

    #region Unit Helpers

    private static double FahrenheitToKelvin(double f) => (f + F_TO_K_OFFSET) * F_TO_K_SCALE;
    private static double KelvinToFahrenheit(double k) => k / F_TO_K_SCALE - F_TO_K_OFFSET;
    private static double PascalToPsia(double pa) => pa * PA_TO_PSI;

    private static (double T_F, double P_psia)[] ToFieldUnits((double T_K, double P_Pa)[] pts)
        => pts.Select(p => (KelvinToFahrenheit(p.T_K), PascalToPsia(p.P_Pa))).ToArray();

    #endregion

    #region Public API

    public PhaseEnvelopeResult GeneratePhaseEnvelope(PhaseEnvelopeRequest request)
    {
        ValidateRequest(request);
        var comps = MapComponentNames(request.Components);
        var z = NormalizeComposition(request.Z);
        double tMinK = FahrenheitToKelvin(request.TminF);
        double tMaxK = FahrenheitToKelvin(request.TmaxF);

        var (bubK, dewK, critK) = TraceEnvelope(comps, z, tMinK, tMaxK, request.Backend);

        if (bubK.Length == 0 && dewK.Length == 0)
            Console.WriteLine($"[PhaseEnvelope] WARNING: No points for {string.Join("+", comps)} / {request.Backend}.");

        var liqK = GenerateLiquidLines(comps, z, bubK, dewK, critK, request.LiquidLines, request.Backend);

        return new PhaseEnvelopeResult(
            Bubble: ToFieldUnits(bubK),
            Dew: ToFieldUnits(dewK),
            LiquidLines: liqK.ToDictionary(kv => kv.Key, kv => ToFieldUnits(kv.Value)),
            Critical: critK.HasValue
                ? (KelvinToFahrenheit(critK.Value.T_K), PascalToPsia(critK.Value.P_Pa))
                : null,
            Backend: request.Backend,
            Components: comps,
            Composition: z);
    }

    public PhaseEnvelopeResult GenerateEnvelopeOilfield(
        string backend, Dictionary<string, double> composition,
        double minTempF = -150, double maxTempF = 350,
        int points = 50, double[]? liquidFractions = null)
    {
        liquidFractions ??= [0.01, 0.05, 0.10, 0.25];
        return GeneratePhaseEnvelope(new PhaseEnvelopeRequest(
            backend, composition.Keys.ToArray(), composition.Values.ToArray(),
            minTempF, maxTempF, points, liquidFractions));
    }

    #endregion

    #region Envelope Tracing

    private static (
        (double T_K, double P_Pa)[] bubble,
        (double T_K, double P_Pa)[] dew,
        (double T_K, double P_Pa)? critical)
        TraceEnvelope(string[] comps, double[] z, double tMinK, double tMaxK, string backend)
    {
        var (bubble, dew, critical) = CoolPropSharpWrapper.BuildPhaseEnvelope(backend, comps, z);

        if (bubble.Length == 0 && dew.Length == 0)
        {
            Console.WriteLine($"[TraceEnvelope] No points returned for backend={backend}.");
            return ([], [], null);
        }

        var bubF = bubble.Where(p => double.IsFinite(p.T_K) && p.T_K >= tMinK && p.T_K <= tMaxK).ToArray();
        var dewF = dew.Where(p => double.IsFinite(p.T_K) && p.T_K >= tMinK && p.T_K <= tMaxK).ToArray();

        Console.WriteLine($"[TraceEnvelope] bubble={bubF.Length}, dew={dewF.Length} pts after filter.");
        return (bubF, dewF, critical);
    }

    #endregion

    #region Liquid Dropout Lines

    /// <summary>
    /// Computes liquid dropout quality isolines in the RETROGRADE region only
    /// (T > Tcrit, along the dew curve side).
    ///
    /// Each temperature point gets a FRESH CoolProp handle to avoid corrupted
    /// internal state from failed PT flashes. Each individual flash call is
    /// wrapped in a timeout to prevent indefinite hangs from CoolProp's cubic
    /// EOS solver near phase boundaries.
    /// </summary>
    private static Dictionary<double, (double T_K, double P_Pa)[]> GenerateLiquidLines(
        string[] comps, double[] z,
        (double T_K, double P_Pa)[] bubble,
        (double T_K, double P_Pa)[] dew,
        (double T_K, double P_Pa)? critical,
        double[] liquidFractions, string backend)
    {
        var result = new Dictionary<double, (double T_K, double P_Pa)[]>();

        if (dew.Length < 2 || liquidFractions.Length == 0 || !critical.HasValue)
            return result;

        double tCritK = critical.Value.T_K;
        double pCritPa = critical.Value.P_Pa;

        var dewByT = dew.OrderBy(p => p.T_K).ToArray();
        var workDew = dewByT.Skip(1).Where(p => p.T_K > tCritK).ToArray();

        if (workDew.Length < 2)
        {
            Console.WriteLine("[LiquidLines] Insufficient dew points beyond critical — skipping.");
            return result;
        }

        var thinDew = ThinArray(workDew, LiquidLineMaxPoints);

        Console.WriteLine($"[LiquidLines] {thinDew.Length} dew T-points × {liquidFractions.Length} fractions. " +
                          $"T range: {KelvinToFahrenheit(thinDew[0].T_K):F1}–" +
                          $"{KelvinToFahrenheit(thinDew[^1].T_K):F1}°F, " +
                          $"Pcrit={pCritPa / 6894.76:F0} psia.");

        foreach (double lf in liquidFractions)
        {
            var pts = new List<(double T, double P)>();
            double targetV = 1.0 - lf;
            int skipped = 0;

            foreach (var dp in thinDew)
            {
                double pOuter = dp.P_Pa;
                double pInner = pCritPa;

                double pLo = Math.Min(pOuter, pInner);
                double pHi = Math.Max(pOuter, pInner);

                if ((pHi - pLo) / Math.Max(pHi, 1.0) < 0.005) { skipped++; continue; }

                // Use a fresh handle per T-point so a failed/hung flash
                // doesn't corrupt state for subsequent points.
                _coolPropLock.Wait();
                int handle = -1;
                try
                {
                    handle = CoolPropSharpWrapper.CreateMixtureHandle(backend, comps, z);
                    if (handle < 0) { skipped++; continue; }

                    double? p = BisectWithHandle(handle, dp.T_K, pLo, pHi, targetV);
                    if (p.HasValue) pts.Add((dp.T_K, p.Value));
                    else skipped++;
                }
                catch
                {
                    skipped++;
                }
                finally
                {
                    if (handle >= 0)
                        CoolPropSharpWrapper.FreeMixtureHandle(handle);
                    _coolPropLock.Release();
                }
            }

            if (pts.Count > 1)
            {
                result[lf] = pts.OrderBy(p => p.T).Select(p => (p.T, p.P)).ToArray();
                Console.WriteLine($"[LiquidLines] LF={lf:P0}: {pts.Count} pts, {skipped} skipped.");
            }
            else
            {
                Console.WriteLine($"[LiquidLines] LF={lf:P0}: {pts.Count} pt(s), {skipped} skipped — omitted.");
            }
        }

        return result;
    }

    private static T[] ThinArray<T>(T[] arr, int maxPts)
    {
        if (arr.Length <= maxPts) return arr;
        var result = new T[maxPts];
        double step = (arr.Length - 1.0) / (maxPts - 1.0);
        for (int i = 0; i < maxPts; i++)
            result[i] = arr[(int)Math.Round(i * step)];
        return result;
    }

    /// <summary>
    /// Bisects for the pressure at which vapor quality equals <paramref name="targetV"/>
    /// at fixed temperature. Each individual CoolProp flash is wrapped in a
    /// timeout to prevent indefinite hangs from CoolProp's cubic EOS PT solver.
    /// </summary>
    private static double? BisectWithHandle(
        int handle, double tempK, double pLo, double pHi, double targetV)
    {
        // Quick scan: 3 probes to verify two-phase exists in this bracket.
        const int ProbeCount = 3;
        int validCount = 0;
        double? qAtLow = null;
        double? qAtHigh = null;

        for (int pi = 0; pi < ProbeCount; pi++)
        {
            double frac = (pi + 1.0) / (ProbeCount + 1.0);
            double pProbe = pLo + (pHi - pLo) * frac;
            double? q = FlashQualityWithTimeout(handle, tempK, pProbe);
            if (q is null) continue;

            validCount++;
            if (frac < 0.5) qAtLow = q;
            else if (frac > 0.5) qAtHigh = q;
        }

        if (validCount == 0) return null;

        bool higherPLowersV = qAtLow.HasValue && qAtHigh.HasValue
            ? qAtHigh.Value < qAtLow.Value
            : true;

        double lo = pLo, hi = pHi;
        int consecutiveMisses = 0;

        for (int it = 0; it < BisectMaxIter; it++)
        {
            double mid = 0.5 * (lo + hi);
            if ((hi - lo) / Math.Max(Math.Abs(mid), 1.0) < 1e-6) return mid;

            double? V = FlashQualityWithTimeout(handle, tempK, mid);

            if (V is null)
            {
                consecutiveMisses++;
                if (consecutiveMisses >= MaxConsecutiveMisses) return null;

                double centre = 0.5 * (lo + hi);
                if (mid > centre) hi = mid; else lo = mid;
                continue;
            }

            consecutiveMisses = 0;

            if (Math.Abs(V.Value - targetV) < 5e-4) return mid;

            if (higherPLowersV) { if (V.Value > targetV) lo = mid; else hi = mid; }
            else { if (V.Value > targetV) hi = mid; else lo = mid; }
        }

        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// PT flash with a hard timeout. CoolProp's cubic EOS PT flash can hang
    /// indefinitely near phase boundaries for mixtures. Returns null on
    /// timeout, non-two-phase, or any error.
    /// </summary>
    private static double? FlashQualityWithTimeout(int handle, double tempK, double pressurePa)
    {
        try
        {
            double? result = null;
            var task = Task.Run(() =>
            {
                var (quality, phase, success) = CoolPropSharpWrapper.FlashPTWithHandle(handle, tempK, pressurePa);
                if (success && phase == "twophase" && double.IsFinite(quality))
                    return (double?)quality;
                return null;
            });

            if (task.Wait(FlashTimeoutMs))
                result = task.Result;
            else
                Console.WriteLine($"[LiquidLines] Flash TIMEOUT at T={tempK:F1}K P={pressurePa / 6894.76:F0}psia");

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static double InterpolateByT((double T_K, double P_Pa)[] sorted, double T)
    {
        if (sorted.Length == 0) return 0;
        if (T <= sorted[0].T_K) return sorted[0].P_Pa;
        if (T >= sorted[^1].T_K) return sorted[^1].P_Pa;
        int lo = 0, hi = sorted.Length - 1;
        while (hi - lo > 1) { int m = (lo + hi) / 2; if (sorted[m].T_K <= T) lo = m; else hi = m; }
        double frac = (T - sorted[lo].T_K) / (sorted[hi].T_K - sorted[lo].T_K);
        return sorted[lo].P_Pa + frac * (sorted[hi].P_Pa - sorted[lo].P_Pa);
    }

    #endregion

    #region Validation & Helpers

    private static void ValidateRequest(PhaseEnvelopeRequest request)
    {
        if (request.Components is null || request.Components.Length == 0)
            throw new ArgumentException("At least one component is required.");
        if (request.Z is null || request.Z.Length != request.Components.Length)
            throw new ArgumentException("Composition array length must match components.");
        if (request.Z.Any(z => z < 0))
            throw new ArgumentException("Negative mole fractions are not allowed.");
        if (request.Z.Sum() <= 0)
            throw new ArgumentException("Total mole fraction must be positive.");
        if (request.TminF >= request.TmaxF)
            throw new ArgumentException("Minimum temperature must be less than maximum.");
        if (request.Points < 10)
            throw new ArgumentException("At least 10 points required.");
        if (!new[] { "PR", "SRK" }.Contains(request.Backend, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Backend must be 'PR' or 'SRK'.");
    }

    private static string[] MapComponentNames(string[] components)
        => components.Select(c =>
            ComponentMapping.TryGetValue(c, out var mapped)
                ? mapped
                : throw new ArgumentException($"Unknown component: '{c}'"))
        .ToArray();

    private static double[] NormalizeComposition(double[] z)
    {
        double sum = z.Sum();
        return Math.Abs(sum - 1.0) < 1e-6 ? z : z.Select(zi => zi / sum).ToArray();
    }

    #endregion
}