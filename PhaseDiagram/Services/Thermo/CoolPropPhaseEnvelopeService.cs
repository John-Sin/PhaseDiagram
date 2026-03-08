namespace PhaseDiagram.Services.Thermo;

/// <summary>
/// Phase envelope service using CoolProp PR/SRK build_phase_envelope.
/// DEFAULT UNITS: Pressure in psia, Temperature in °F (Field/Oilfield Units)
///
/// Liquid dropout lines are computed using CoolProp's PQ flash (pressure +
/// vapour quality → temperature).  Each dropout point is validated against
/// the phase envelope boundaries.
///
/// The PQ flash can occasionally return the wrong root in the retrograde
/// region, so each point is verified to lie between bubble and dew curves,
/// and cross-line monotonicity is enforced as a final post-processing step.
///
/// NOTE: CoolProp's BuildPhaseEnvelope can mis-classify the retrograde dew
/// tail as belonging to the bubble curve.  The boundary interpolation uses
/// BOTH curves independently and takes the true outer boundary as
/// max(dewMaxT, bubbleMaxT) at each pressure.
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
    private const double PSI_TO_PA = 6894.757293168;
    private const double F_TO_K_OFFSET = 459.67;
    private const double F_TO_K_SCALE = 5.0 / 9.0;

    /// <summary>Number of pressure levels to sample per dropout line.</summary>
    private const int LiquidLinePoints = 100;

    /// <summary>Maximum total time (ms) for all liquid line calculations.</summary>
    private const int TotalLiquidLineTimeoutMs = 60000;

    /// <summary>CoolProp C API global handle — serialise all calls.</summary>
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

    #region Liquid Dropout Lines — PQ Flash with Envelope Clamping

    private static Dictionary<double, (double T_K, double P_Pa)[]> GenerateLiquidLines(
        string[] comps, double[] z,
        (double T_K, double P_Pa)[] bubble,
        (double T_K, double P_Pa)[] dew,
        (double T_K, double P_Pa)? critical,
        double[] liquidFractions, string backend)
    {
        var result = new Dictionary<double, (double T_K, double P_Pa)[]>();

        if ((dew.Length + bubble.Length) < 5 || liquidFractions.Length == 0 || !critical.HasValue)
            return result;

        double tCritK = critical.Value.T_K;
        double pCritPa = critical.Value.P_Pa;

        // ── Valid temperature range from the full envelope ──
        double tMaxBub = bubble.Length > 0 ? bubble.Max(p => p.T_K) : double.MinValue;
        double tMaxDew = dew.Length > 0 ? dew.Max(p => p.T_K) : double.MinValue;
        double tMinBub = bubble.Length > 0 ? bubble.Min(p => p.T_K) : double.MaxValue;
        double tMinDew = dew.Length > 0 ? dew.Min(p => p.T_K) : double.MaxValue;
        double tMaxEnv = Math.Max(tMaxBub, tMaxDew);
        double tMinEnv = Math.Min(tMinBub, tMinDew);
        double tValidMin = tMinEnv - 2.0;
        double tValidMax = tMaxEnv + 2.0;

        // ── Pressure sweep range ──
        double pMinBub = bubble.Length > 0 ? bubble.Min(p => p.P_Pa) : double.MaxValue;
        double pMinDew = dew.Length > 0 ? dew.Min(p => p.P_Pa) : double.MaxValue;
        double pMinEnv = Math.Min(pMinBub, pMinDew);
        double pFloor = Math.Max(pMinEnv * 0.90, 20000.0);
        double pCeiling = pCritPa * 0.995;
        if (pCeiling <= pFloor)
            pCeiling = pCritPa * 0.9999;

        Console.WriteLine($"[LiquidLines] PQ flash sweep: {liquidFractions.Length} fractions, " +
                          $"{LiquidLinePoints} P-levels. " +
                          $"P: {PascalToPsia(pFloor):F0}–{PascalToPsia(pCeiling):F0} psia, " +
                          $"Pcrit={PascalToPsia(pCritPa):F0} psia, " +
                          $"T envelope: {KelvinToFahrenheit(tValidMin):F0}–{KelvinToFahrenheit(tValidMax):F0}°F.");

        var pressures = GeneratePressurePoints(pFloor, pCeiling, LiquidLinePoints);
        var sortedFractions = liquidFractions.OrderBy(f => f).ToArray();
        var allLines = new Dictionary<double, List<(double T_K, double P_Pa)>>();
        var overallSw = System.Diagnostics.Stopwatch.StartNew();

        foreach (double lf in sortedFractions)
        {
            if (overallSw.ElapsedMilliseconds > TotalLiquidLineTimeoutMs)
            {
                Console.WriteLine($"[LiquidLines] Timeout ({TotalLiquidLineTimeoutMs}ms) — stopping.");
                break;
            }

            double vaporQ = 1.0 - lf;
            var pts = new List<(double T_K, double P_Pa)>();
            int skipped = 0, outOfRange = 0, clamped = 0;

            foreach (double pPa in pressures)
            {
                if (overallSw.ElapsedMilliseconds > TotalLiquidLineTimeoutMs) break;

                _coolPropLock.Wait();
                try
                {
                    var (tempK, success) = CoolPropSharpWrapper.GetSaturationTempAtPQ(
                        backend, comps, z, pPa, vaporQ);

                    if (!success || !double.IsFinite(tempK) || tempK <= 0)
                    {
                        skipped++;
                        continue;
                    }

                    if (tempK < tValidMin || tempK > tValidMax)
                    {
                        outOfRange++;
                        continue;
                    }

                    // ── Envelope boundary clamping ──
                    // BuildPhaseEnvelope can mis-classify the retrograde dew
                    // tail into the bubble array.  Check BOTH curves for the
                    // true outer boundary at this pressure.
                    double rightBound = EnvelopeMaxT(bubble, dew, pPa);
                    double leftBound = EnvelopeMinT(bubble, dew, pPa);

                    if (rightBound > 0 && tempK > rightBound)
                    {
                        tempK = rightBound - 0.5;
                        clamped++;
                    }

                    if (leftBound > 0 && tempK < leftBound)
                    {
                        tempK = leftBound + 0.5;
                        clamped++;
                    }

                    // Dropout line must be INSIDE the envelope — strictly between
                    // the left (bubble) and right (dew) boundaries.
                    if (rightBound > 0 && leftBound > 0)
                    {
                        if (tempK <= leftBound || tempK >= rightBound)
                        {
                            outOfRange++;
                            continue;
                        }
                    }

                    if (tempK >= tValidMin && tempK <= tValidMax)
                        pts.Add((tempK, pPa));
                    else
                        outOfRange++;
                }
                catch { skipped++; }
                finally { _coolPropLock.Release(); }
            }

            if (pts.Count > 2)
            {
                pts.Sort((a, b) => a.P_Pa.CompareTo(b.P_Pa));
                var cleaned = RemoveOutliers(pts);

                // ── CP-PROTECTION (per Michelsen/Nichita recommendation #4): ──
                cleaned = TruncateAtDivergence(cleaned, tCritK, pCritPa);

                // All isolines converge at the critical point.
                if (cleaned.Count > 0)
                    cleaned.Add((tCritK, pCritPa));

                allLines[lf] = cleaned;
                Console.WriteLine($"[LiquidLines] LF={lf:P0}: {cleaned.Count} pts (inc. CP), " +
                                  $"{clamped} clamped, {skipped} failed, {outOfRange} out-of-range.");
            }
            else
            {
                Console.WriteLine($"[LiquidLines] LF={lf:P0}: {pts.Count} pt(s), " +
                                  $"{skipped} failed, {outOfRange} out-of-range — omitted.");
            }
        }

        // Enforce physical ordering: at any P, higher liquid fraction = lower T.
        EnforceMonotonicity(allLines, sortedFractions, bubble, dew);

        foreach (var kvp in allLines)
            result[kvp.Key] = kvp.Value.ToArray();

        return result;
    }

    /// <summary>
    /// CP-PROTECTION TRUNCATION — two-pass adaptive detection.
    ///
    /// PASS 1 — Slope-jump: Compute median |ΔT| step from the lower
    /// reliable portion (below 70% Pcrit). Any step exceeding 5× this
    /// baseline above 60% Pcrit is a PQ flash root-jump — truncate.
    ///
    /// PASS 2 — Running-minimum drift trim: Walk backward from the end.
    /// Track the smallest distance-to-Tcrit seen so far. Any point above
    /// 80% Pcrit whose distance exceeds this running minimum by more than
    /// 0.5K is drifting away from CP — remove it. This catches gradual
    /// drift that Pass 1 misses, and unlike a simple consecutive check,
    /// doesn't stop at the first non-drifting point.
    /// </summary>
    private static List<(double T_K, double P_Pa)> TruncateAtDivergence(
        List<(double T_K, double P_Pa)> pts, double tCritK, double pCritPa)
    {
        if (pts.Count < 6) return pts;

        // ════ PASS 1: Slope-jump detection ════
        double pBaseline = pCritPa * 0.70;
        var baselineSteps = new List<double>();

        for (int i = 1; i < pts.Count; i++)
        {
            if (pts[i].P_Pa > pBaseline) break;
            baselineSteps.Add(Math.Abs(pts[i].T_K - pts[i - 1].T_K));
        }

        if (baselineSteps.Count >= 3)
        {
            baselineSteps.Sort();
            double medianStep = baselineSteps[baselineSteps.Count / 2];
            double jumpThreshold = Math.Max(medianStep * 5.0, 2.0);

            double pProtectionFloor = pCritPa * 0.60;

            for (int i = 1; i < pts.Count; i++)
            {
                if (pts[i].P_Pa < pProtectionFloor) continue;

                double step = Math.Abs(pts[i].T_K - pts[i - 1].T_K);
                if (step > jumpThreshold)
                {
                    pts = pts.GetRange(0, i);
                    break;
                }
            }
        }

        // ════ PASS 2: Running-minimum drift trim above 80% Pcrit ════
        double pDriftFloor = pCritPa * 0.80;
        double minDistSeen = double.MaxValue;
        var keep = new bool[pts.Count];

        // Mark all points below the drift floor as kept.
        for (int i = 0; i < pts.Count; i++)
        {
            if (pts[i].P_Pa < pDriftFloor)
                keep[i] = true;
        }

        // Walk backward through the near-CP zone.
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            if (pts[i].P_Pa < pDriftFloor) break;

            double dist = Math.Abs(pts[i].T_K - tCritK);

            if (dist <= minDistSeen + 0.5)
            {
                keep[i] = true;
                minDistSeen = Math.Min(minDistSeen, dist);
            }
            else
            {
                keep[i] = false;
            }
        }

        var result = new List<(double T_K, double P_Pa)>();
        for (int i = 0; i < pts.Count; i++)
            if (keep[i]) result.Add(pts[i]);

        return result;
    }

    /// <summary>
    /// The true right (high-T) envelope boundary at a given pressure.
    /// Checks BOTH curves independently and returns the maximum T found.
    /// This handles the case where BuildPhaseEnvelope mis-classifies
    /// the retrograde dew tail as belonging to the bubble curve.
    /// </summary>
    private static double EnvelopeMaxT(
        (double T_K, double P_Pa)[] bubble,
        (double T_K, double P_Pa)[] dew,
        double P)
    {
        double maxBub = InterpolateCurveMaxT(bubble, P);
        double maxDew = InterpolateCurveMaxT(dew, P);

        if (maxBub > 0 && maxDew > 0) return Math.Max(maxBub, maxDew);
        if (maxBub > 0) return maxBub;
        return maxDew; // may be 0 if neither curve brackets this P
    }

    /// <summary>
    /// The true left (low-T) envelope boundary at a given pressure.
    /// Checks BOTH curves independently and returns the minimum T found.
    /// </summary>
    private static double EnvelopeMinT(
        (double T_K, double P_Pa)[] bubble,
        (double T_K, double P_Pa)[] dew,
        double P)
    {
        double minBub = InterpolateCurveMinT(bubble, P);
        double minDew = InterpolateCurveMinT(dew, P);

        if (minBub > 0 && minDew > 0) return Math.Min(minBub, minDew);
        if (minBub > 0) return minBub;
        return minDew;
    }

    #endregion

    #region Monotonicity Enforcement

    private static void EnforceMonotonicity(
        Dictionary<double, List<(double T_K, double P_Pa)>> allLines,
        double[] sortedFractions,
        (double T_K, double P_Pa)[] bubble,
        (double T_K, double P_Pa)[] dew)
    {
        for (int fi = 1; fi < sortedFractions.Length; fi++)
        {
            double lfSmaller = sortedFractions[fi - 1];
            double lfLarger = sortedFractions[fi];

            if (!allLines.ContainsKey(lfSmaller) || !allLines.ContainsKey(lfLarger))
                continue;

            var outerLine = allLines[lfSmaller];
            var innerLine = allLines[lfLarger];

            for (int i = 0; i < innerLine.Count; i++)
            {
                double p = innerLine[i].P_Pa;
                double innerT = innerLine[i].T_K;

                double outerT = InterpolateLineAtP(outerLine, p);
                if (outerT <= 0) continue;

                if (innerT >= outerT)
                {
                    double leftBound = EnvelopeMinT(bubble, dew, p);
                    double corrected = outerT - 1.0;
                    if (leftBound > 0 && corrected < leftBound + 0.5)
                        corrected = leftBound + 0.5;

                    innerLine[i] = (corrected, p);
                }
            }
        }
    }

    private static double InterpolateLineAtP(List<(double T_K, double P_Pa)> line, double P)
    {
        if (line.Count < 2) return 0;
        if (P < line[0].P_Pa || P > line[^1].P_Pa) return 0;

        for (int i = 0; i < line.Count - 1; i++)
        {
            if (P >= line[i].P_Pa && P <= line[i + 1].P_Pa)
            {
                double dp = line[i + 1].P_Pa - line[i].P_Pa;
                if (Math.Abs(dp) < 1e-10) return line[i].T_K;
                double frac = (P - line[i].P_Pa) / dp;
                return line[i].T_K + frac * (line[i + 1].T_K - line[i].T_K);
            }
        }
        return 0;
    }

    #endregion

    #region Envelope Curve Interpolation

    private static double InterpolateCurveMaxT((double T_K, double P_Pa)[] curve, double P)
    {
        if (curve.Length < 2) return 0;
        double maxT = 0;
        bool found = false;

        for (int i = 0; i < curve.Length - 1; i++)
        {
            double p0 = curve[i].P_Pa, p1 = curve[i + 1].P_Pa;
            double pMin = Math.Min(p0, p1), pMax = Math.Max(p0, p1);
            if (P < pMin || P > pMax) continue;
            double denom = p1 - p0;
            if (Math.Abs(denom) < 1e-10) continue;
            double frac = (P - p0) / denom;
            double t = curve[i].T_K + frac * (curve[i + 1].T_K - curve[i].T_K);
            if (t > maxT) { maxT = t; found = true; }
        }
        return found ? maxT : 0;
    }

    private static double InterpolateCurveMinT((double T_K, double P_Pa)[] curve, double P)
    {
        if (curve.Length < 2) return 0;
        double minT = double.MaxValue;
        bool found = false;

        for (int i = 0; i < curve.Length - 1; i++)
        {
            double p0 = curve[i].P_Pa, p1 = curve[i + 1].P_Pa;
            double pMin = Math.Min(p0, p1), pMax = Math.Max(p0, p1);
            if (P < pMin || P > pMax) continue;
            double denom = p1 - p0;
            if (Math.Abs(denom) < 1e-10) continue;
            double frac = (P - p0) / denom;
            double t = curve[i].T_K + frac * (curve[i + 1].T_K - curve[i].T_K);
            if (t < minT) { minT = t; found = true; }
        }
        return found ? minT : 0;
    }

    #endregion

    #region Outlier Removal

    private static List<(double T_K, double P_Pa)> RemoveOutliers(List<(double T_K, double P_Pa)> pts)
    {
        if (pts.Count <= 4)
            return new List<(double T_K, double P_Pa)>(pts);

        var slopes = new List<double>();
        for (int i = 1; i < pts.Count; i++)
        {
            double dp = pts[i].P_Pa - pts[i - 1].P_Pa;
            if (Math.Abs(dp) < 1e-6) continue;
            slopes.Add(Math.Abs((pts[i].T_K - pts[i - 1].T_K) / dp));
        }

        if (slopes.Count < 3)
            return new List<(double T_K, double P_Pa)>(pts);

        slopes.Sort();
        double medianSlope = slopes[slopes.Count / 2];
        double slopeThreshold = Math.Max(medianSlope * 10.0, 0.001);

        var keep = new bool[pts.Count];
        keep[0] = true;
        keep[pts.Count - 1] = true;

        for (int i = 1; i < pts.Count - 1; i++)
        {
            double dpPrev = pts[i].P_Pa - pts[i - 1].P_Pa;
            double dpNext = pts[i + 1].P_Pa - pts[i].P_Pa;

            if (Math.Abs(dpPrev) < 1e-6 || Math.Abs(dpNext) < 1e-6)
            { keep[i] = true; continue; }

            double slopePrev = Math.Abs((pts[i].T_K - pts[i - 1].T_K) / dpPrev);
            double slopeNext = Math.Abs((pts[i + 1].T_K - pts[i].T_K) / dpNext);

            keep[i] = !(slopePrev > slopeThreshold && slopeNext > slopeThreshold);
        }

        var cleaned = new List<(double T_K, double P_Pa)>();
        for (int i = 0; i < pts.Count; i++)
            if (keep[i]) cleaned.Add(pts[i]);
        return cleaned;
    }

    #endregion

    #region Pressure Sampling

    private static double[] GeneratePressurePoints(double pFloor, double pCeiling, int count)
    {
        int nLog = (int)(count * 0.40);
        int nMid = (int)(count * 0.25);
        int nTop = count - nLog - nMid;

        var pressures = new HashSet<double>();

        double logRatio = pCeiling / pFloor;
        for (int i = 0; i < nLog; i++)
        {
            double frac = (double)i / Math.Max(nLog - 1, 1);
            pressures.Add(pFloor * Math.Pow(logRatio, frac));
        }

        double pMidStart = pFloor + (pCeiling - pFloor) * 0.50;
        for (int i = 0; i < nMid; i++)
        {
            double frac = (double)i / Math.Max(nMid - 1, 1);
            pressures.Add(pMidStart + (pCeiling - pMidStart) * frac);
        }

        double pTopStart = pFloor + (pCeiling - pFloor) * 0.90;
        for (int i = 0; i < nTop; i++)
        {
            double frac = (double)i / Math.Max(nTop - 1, 1);
            pressures.Add(pTopStart + (pCeiling - pTopStart) * frac);
        }

        return pressures.OrderBy(p => p).ToArray();
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