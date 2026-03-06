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

    // Maximum bubble-array points evaluated per dropout line.
    // 40 T-steps produces a visually smooth line while keeping flash calls low:
    // 40 pts × 22 bisection iterations × ~5 P/Invoke calls ≈ 4,400 calls/line.
    private const int LiquidLineMaxPoints = 40;

    // Bisection iterations per dropout point.
    // 22 halvings → relative tolerance < 2.4×10⁻⁷ on the pressure interval.
    private const int BisectMaxIter = 22;

    // CoolProp's C API uses global state internally (thread-local AbstractState
    // pools that share a global registry mutex). Concurrent flash calls from
    // multiple threads can deadlock on that mutex. The semaphore enforces that
    // all CoolProp flash calls for dropout lines run one-at-a-time.
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

        var liqK = GenerateLiquidLines(comps, z, bubK, dewK, request.LiquidLines, request.Backend);

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

    private static Dictionary<double, (double T_K, double P_Pa)[]> GenerateLiquidLines(
        string[] comps, double[] z,
        (double T_K, double P_Pa)[] bubble,
        (double T_K, double P_Pa)[] dew,
        double[] liquidFractions, string backend)
    {
        var result = new Dictionary<double, (double T_K, double P_Pa)[]>();

        if (bubble.Length < 2 || dew.Length < 2 || liquidFractions.Length == 0)
            return result;

        var bubByT = bubble.OrderBy(p => p.T_K).ToArray();
        var dewByT = dew.OrderBy(p => p.T_K).ToArray();
        var thinBub = ThinArray(bubByT, LiquidLineMaxPoints);

        Console.WriteLine($"[LiquidLines] {thinBub.Length} T-points × {liquidFractions.Length} fractions " +
                          $"× max {BisectMaxIter} iters (sequential, serialized lock).");

        // All CoolProp calls are serialized through _coolPropLock.
        // CoolProp's C API shares global registry state — concurrent access deadlocks.
        // Each fraction is computed fully before the next starts.
        foreach (double lf in liquidFractions)
        {
            var pts = new List<(double T, double P)>();
            double targetV = 1.0 - lf;
            int skipped = 0;

            foreach (var bp in thinBub)
            {
                double pDew = InterpolateByT(dewByT, bp.T_K);
                double pLo = Math.Min(bp.P_Pa, pDew);
                double pHi = Math.Max(bp.P_Pa, pDew);

                if (pHi <= pLo * 1.001 || pHi <= 0) { skipped++; continue; }

                double? p = BisectVaporFraction(comps, z, bp.T_K, pLo, pHi, targetV, backend);
                if (p.HasValue) pts.Add((bp.T_K, p.Value));
                else skipped++;
            }

            if (pts.Count > 1)
                result[lf] = pts.OrderBy(p => p.T).Select(p => (p.T, p.P)).ToArray();
            else
                Console.WriteLine($"[LiquidLines] LF={lf:P0}: {pts.Count} pt(s), {skipped} skipped — omitted.");
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

    private static double? BisectVaporFraction(
        string[] comps, double[] z,
        double tempK, double pLo, double pHi, double targetV, string backend)
    {
        // Probe the two-phase window and determine monotonicity.
        double? vAtLo = GetVaporFractionAtTP(comps, z, tempK, pLo + (pHi - pLo) * 0.05, backend);
        double? vAtHi = GetVaporFractionAtTP(comps, z, tempK, pLo + (pHi - pLo) * 0.95, backend);

        if (vAtLo is null && vAtHi is null) return null;

        bool higherPLowersV = !(vAtLo.HasValue && vAtHi.HasValue) || vAtHi.Value < vAtLo.Value;
        double vMin = Math.Min(vAtLo ?? 1.0, vAtHi ?? 0.0);
        double vMax = Math.Max(vAtLo ?? 0.0, vAtHi ?? 1.0);

        if (targetV < vMin - 0.05 || targetV > vMax + 0.05) return null;

        double lo = pLo, hi = pHi;

        for (int it = 0; it < BisectMaxIter; it++)
        {
            double mid = 0.5 * (lo + hi);
            if ((hi - lo) / Math.Max(Math.Abs(mid), 1.0) < 1e-7) return mid;

            double? V = GetVaporFractionAtTP(comps, z, tempK, mid, backend);
            if (V is null) { hi = mid; continue; }
            if (Math.Abs(V.Value - targetV) < 1e-4) return mid;

            if (higherPLowersV) { if (V.Value > targetV) lo = mid; else hi = mid; }
            else { if (V.Value > targetV) hi = mid; else lo = mid; }
        }

        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// Wraps every CoolProp PT flash in the serializing semaphore.
    /// This prevents concurrent access to CoolProp's global registry state,
    /// which is the root cause of the "frozen calculating" deadlock.
    /// </summary>
    private static double? GetVaporFractionAtTP(
        string[] comps, double[] z, double tempK, double pressurePa, string backend)
    {
        _coolPropLock.Wait();
        try
        {
            var (quality, phase, success) = CoolPropSharpWrapper.GetPhaseAtTP(
                backend, comps, z, tempK, pressurePa);
            if (!success || phase != "twophase") return null;
            return double.IsFinite(quality) ? quality : null;
        }
        finally
        {
            _coolPropLock.Release();
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