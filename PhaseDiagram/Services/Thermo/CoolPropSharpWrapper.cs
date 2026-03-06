using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PhaseDiagram.Services.Thermo;

/// <summary>
/// Direct CoolProp wrapper using AbstractState C API for PR/SRK mixture support.
/// All P/Invoke types match the actual CoolProp C header (CoolPropLib.h).
/// Enum integers are resolved at startup via get_param_index / get_input_pair_index.
/// </summary>
public sealed class CoolPropSharpWrapper : ICoolPropWrapper
{
    private const string CoolPropLib = "CoolProp64";
    private const int ErrBufSize = 256;

    [DllImport(CoolPropLib, EntryPoint = "AbstractState_factory",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int AbstractState_factory(
        string backend, string fluids,
        ref int errCode, byte[] errMsg, int errMsgLen);

    [DllImport(CoolPropLib, EntryPoint = "AbstractState_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void AbstractState_free(
        int handle, ref int errCode, byte[] errMsg, int errMsgLen);

    [DllImport(CoolPropLib, EntryPoint = "AbstractState_set_fractions",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void AbstractState_set_fractions(
        int handle, double[] fractions, int N,
        ref int errCode, byte[] errMsg, int errMsgLen);

    [DllImport(CoolPropLib, EntryPoint = "AbstractState_update",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void AbstractState_update(
        int handle, int inputPair, double value1, double value2,
        ref int errCode, byte[] errMsg, int errMsgLen);

    [DllImport(CoolPropLib, EntryPoint = "AbstractState_keyed_output",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern double AbstractState_keyed_output(
        int handle, int key,
        ref int errCode, byte[] errMsg, int errMsgLen);

    [DllImport(CoolPropLib, EntryPoint = "AbstractState_specify_phase",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void AbstractState_specify_phase(
        int handle, string phase,
        ref int errCode, byte[] errMsg, int errMsgLen);

    [DllImport(CoolPropLib, EntryPoint = "AbstractState_build_phase_envelope",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void AbstractState_build_phase_envelope(
        int handle, string level,
        ref int errCode, byte[] errMsg, int errMsgLen);

    [DllImport(CoolPropLib, EntryPoint = "AbstractState_get_phase_envelope_data",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void AbstractState_get_phase_envelope_data(
        int handle, int length,
        double[] T, double[] p, double[] rhomolar_vap, double[] rhomolar_liq,
        double[] x, double[] y,
        ref int errCode, byte[] errMsg, int errMsgLen);

    [DllImport(CoolPropLib, EntryPoint = "PropsSI",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern double PropsSI(
        string output,
        string name1, double value1,
        string name2, double value2,
        string fluid);

    [DllImport(CoolPropLib, EntryPoint = "get_global_param_string",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int get_global_param_string(
        string param, byte[] outBuf, int outBufLen);

    [DllImport(CoolPropLib, EntryPoint = "get_param_index",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern long get_param_index(string param);

    [DllImport(CoolPropLib, EntryPoint = "get_input_pair_index",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern long get_input_pair_index(string inputPair);

    private static readonly int iT;
    private static readonly int iP;
    private static readonly int iQ;
    private static readonly int iPhase;
    private static readonly int iT_critical;
    private static readonly int iP_critical;
    private static readonly int imolar_mass;

    private static readonly int PQ_INPUTS;
    private static readonly int QT_INPUTS;
    private static readonly int PT_INPUTS;

    static CoolPropSharpWrapper()
    {
        iT      = ResolveParam("T");
        iP      = ResolveParam("P");
        iQ      = ResolveParam("Q");
        iPhase  = ResolveParam("Phase");
        iT_critical = ResolveParam("T_critical");

        iP_critical = TryResolveParam("P_critical") ?? TryResolveParam("p_critical")
            ?? throw new InvalidOperationException(
                "[CoolProp] Could not resolve critical pressure parameter.");

        imolar_mass = TryResolveParam("molar_mass") ?? TryResolveParam("M")
            ?? throw new InvalidOperationException(
                "[CoolProp] Could not resolve molar mass parameter.");

        PQ_INPUTS = ResolveInputPair("PQ_INPUTS");
        QT_INPUTS = ResolveInputPair("QT_INPUTS");
        PT_INPUTS = ResolveInputPair("PT_INPUTS");

        System.Diagnostics.Debug.WriteLine(
            $"[CoolProp] Enum indices — iT={iT} iP={iP} iQ={iQ} iPhase={iPhase} " +
            $"iT_critical={iT_critical} iP_critical={iP_critical} imolar_mass={imolar_mass} | " +
            $"PQ_INPUTS={PQ_INPUTS} QT_INPUTS={QT_INPUTS} PT_INPUTS={PT_INPUTS}");
    }

    private static int ResolveParam(string name)
        => TryResolveParam(name)
           ?? throw new InvalidOperationException(
               $"[CoolProp] get_param_index('{name}') returned -1.");

    private static int? TryResolveParam(string name)
    {
        long idx = get_param_index(name);
        return idx >= 0 ? (int)idx : null;
    }

    private static int ResolveInputPair(string name)
    {
        long idx = get_input_pair_index(name);
        if (idx < 0)
            throw new InvalidOperationException(
                $"[CoolProp] get_input_pair_index('{name}') returned {idx}.");
        return (int)idx;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Get saturation pressure at T and Q. Pure fluids (HEOS) only.
    /// For mixtures use <see cref="GetSaturationTempAtPQ"/>.
    /// </summary>
    public static (double pressurePa, bool success) GetSaturationPressureAtTQ(
        string backend, string[] componentNames, double[] moleFractions,
        double temperatureK, double vaporQuality)
    {
        if (componentNames.Length == 1)
            return GetSaturationPressurePureFluid(componentNames[0], temperatureK, vaporQuality);

        System.Diagnostics.Debug.WriteLine(
            "[CoolProp] WARNING: GetSaturationPressureAtTQ called for mixture — use GetSaturationTempAtPQ");
        return (0, false);
    }

    /// <summary>
    /// Get saturation temperature at P and Q using PQ_INPUTS.
    /// The only mixture saturation flash supported by CoolProp's PR/SRK backend.
    /// </summary>
    public static (double temperatureK, bool success) GetSaturationTempAtPQ(
        string backend, string[] componentNames, double[] moleFractions,
        double pressurePa, double vaporQuality)
    {
        if (!double.IsFinite(pressurePa) || pressurePa <= 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CoolProp] GetSaturationTempAtPQ: invalid pressurePa={pressurePa} — skipped.");
            return (0, false);
        }

        int handle = -1;
        var errMsg = new byte[ErrBufSize];
        int errCode = 0;

        try
        {
            string separator = backend is "PR" or "SRK" ? "&" : "|";
            string fluids = string.Join(separator, componentNames.Select(MapFluid));

            handle = AbstractState_factory(backend, fluids, ref errCode, errMsg, ErrBufSize);
            if (errCode != 0 || handle < 0) { LogCoolPropError("factory", errCode, errMsg); return (0, false); }

            AbstractState_set_fractions(handle, moleFractions, moleFractions.Length,
                ref errCode, errMsg, ErrBufSize);
            if (errCode != 0) { LogCoolPropError("set_fractions", errCode, errMsg); return (0, false); }

            // PQ_INPUTS: value1 = P (Pa), value2 = Q
            AbstractState_update(handle, PQ_INPUTS, pressurePa, vaporQuality,
                ref errCode, errMsg, ErrBufSize);
            if (errCode != 0) { LogCoolPropError("update(PQ)", errCode, errMsg); return (0, false); }

            double temperature = AbstractState_keyed_output(handle, iT, ref errCode, errMsg, ErrBufSize);
            if (errCode != 0) { LogCoolPropError("keyed_output(T)", errCode, errMsg); return (0, false); }

            if (!double.IsFinite(temperature) || temperature <= 0) return (0, false);
            return (temperature, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoolProp] GetSatTempAtPQ exception: {ex.Message}");
            return (0, false);
        }
        finally
        {
            FreeHandle(ref handle, errMsg);
        }
    }

    /// <summary>
    /// PT flash — returns vapor quality and phase at given T and P.
    /// Supported by all backends including PR/SRK mixtures.
    /// </summary>
    public static (double quality, string phase, bool success) GetPhaseAtTP(
        string backend, string[] componentNames, double[] moleFractions,
        double temperatureK, double pressurePa)
    {
        int handle = -1;
        var errMsg = new byte[ErrBufSize];
        int errCode = 0;

        try
        {
            string separator = backend is "PR" or "SRK" ? "&" : "|";
            string fluids = string.Join(separator, componentNames.Select(MapFluid));

            handle = AbstractState_factory(backend, fluids, ref errCode, errMsg, ErrBufSize);
            if (errCode != 0 || handle < 0) return (0, "unknown", false);

            AbstractState_set_fractions(handle, moleFractions, moleFractions.Length,
                ref errCode, errMsg, ErrBufSize);
            if (errCode != 0) return (0, "unknown", false);

            // PT_INPUTS: value1 = P, value2 = T
            AbstractState_update(handle, PT_INPUTS, pressurePa, temperatureK,
                ref errCode, errMsg, ErrBufSize);
            if (errCode != 0) return (0, "unknown", false);

            double phaseIndex = AbstractState_keyed_output(handle, iPhase,
                ref errCode, errMsg, ErrBufSize);
            string phase = GetPhaseString(phaseIndex);

            double quality = AbstractState_keyed_output(handle, iQ,
                ref errCode, errMsg, ErrBufSize);
            if (!double.IsFinite(quality))
                quality = phase == "gas" ? 1.0 : 0.0;

            return (quality, phase, true);
        }
        catch
        {
            return (0, "unknown", false);
        }
        finally
        {
            FreeHandle(ref handle, errMsg);
        }
    }

    /// <summary>
    /// Builds the full phase envelope for a mixture using CoolProp's internal
    /// continuation solver.
    ///
    /// CoolProp PR/SRK always begins tracing from the DEW side (Q≈1, rhoLiq > rhoVap).
    /// Raw array layout:
    ///   indices 0 → critIdx   : dew side  (high-T → Tcrit, decreasing T)
    ///   indices critIdx → end : bubble side (Tcrit → low-T, decreasing T)
    ///
    /// Returns:
    ///   bubble : low-T → Tcrit          (ascending T, Q=0 saturation line)
    ///   dew    : Tcrit → cricondentherm  (ascending T, Q=1 saturation line)
    /// </summary>
    public static (
        (double T_K, double P_Pa)[] bubble,
        (double T_K, double P_Pa)[] dew,
        (double T_K, double P_Pa)? critical)
        BuildPhaseEnvelope(string backend, string[] componentNames, double[] moleFractions)
    {
        int handle = -1;
        var errMsg = new byte[ErrBufSize];
        int errCode = 0;

        try
        {
            string separator = backend is "PR" or "SRK" ? "&" : "|";
            string fluids = string.Join(separator, componentNames.Select(MapFluid));

            handle = AbstractState_factory(backend, fluids, ref errCode, errMsg, ErrBufSize);
            if (errCode != 0 || handle < 0)
            {
                LogCoolPropError("factory", errCode, errMsg);
                return ([], [], null);
            }

            AbstractState_set_fractions(handle, moleFractions, moleFractions.Length,
                ref errCode, errMsg, ErrBufSize);
            if (errCode != 0) { LogCoolPropError("set_fractions", errCode, errMsg); return ([], [], null); }

            AbstractState_build_phase_envelope(handle, "", ref errCode, errMsg, ErrBufSize);
            if (errCode != 0) { LogCoolPropError("build_phase_envelope", errCode, errMsg); return ([], [], null); }

            const int MaxEnvelopePoints = 600;
            const int MaxComponents = 20;

            var T_arr     = new double[MaxEnvelopePoints];
            var p_arr     = new double[MaxEnvelopePoints];
            var rhoVap_arr = new double[MaxEnvelopePoints];
            var rhoLiq_arr = new double[MaxEnvelopePoints];
            var x_arr     = new double[MaxEnvelopePoints * MaxComponents];
            var y_arr     = new double[MaxEnvelopePoints * MaxComponents];

            int dataCode = 0;
            var dataBuf = new byte[ErrBufSize];
            AbstractState_get_phase_envelope_data(handle, MaxEnvelopePoints,
                T_arr, p_arr, rhoVap_arr, rhoLiq_arr, x_arr, y_arr,
                ref dataCode, dataBuf, ErrBufSize);

            if (dataCode != 0) { LogCoolPropError("get_phase_envelope_data", dataCode, dataBuf); return ([], [], null); }

            // Trim trailing unfilled slots.
            int n = 0;
            for (int i = 0; i < MaxEnvelopePoints; i++)
            {
                if (!double.IsFinite(T_arr[i]) || T_arr[i] < 10.0 ||
                    !double.IsFinite(p_arr[i]) || p_arr[i] <= 0.0)
                    break;
                n++;
            }

            if (n == 0)
            {
                System.Diagnostics.Debug.WriteLine("[CoolProp] BuildPhaseEnvelope: 0 valid points.");
                return ([], [], null);
            }

            // ── Critical point: maximum pressure index. ──────────────────────────────
            int critIdx = 0;
            double pMax = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (p_arr[i] > pMax) { pMax = p_arr[i]; critIdx = i; }
            }
            critIdx = Math.Clamp(critIdx, 0, n - 1);

            // ── Identify which physical side CoolProp started on. ────────────────────
            // On the dew side, liquid density is always greater than vapour density.
            bool startsOnDewSide = rhoLiq_arr[0] > rhoVap_arr[0];

            // ── Sanitize the post-critical tail. ────────────────────────────────────
            // The old approach (stop at first pressure increase) was too aggressive:
            // it discarded valid tail points that had tiny solver oscillations, leaving
            // only a handful of points and causing angular line segments on the plot.
            //
            // New approach — cumulative-minimum tolerance:
            //   Keep a running minimum of pressure seen so far on this side.
            //   Only stop when pressure rises more than PressureRiseTolerance (2%)
            //   above that running minimum. This allows small numerical wobbles but
            //   still stops genuine solver divergence / overshoot artifacts.
            const double PressureRiseTolerance = 0.02;   // 2% above running minimum

            int otherEnd = critIdx;
            double pRunMin = p_arr[critIdx];
            for (int i = critIdx + 1; i < n; i++)
            {
                double pCurr = p_arr[i];
                if (!double.IsFinite(pCurr) || pCurr <= 0) break;

                // Update the running minimum so legitimate pressure drop is tracked.
                if (pCurr < pRunMin) pRunMin = pCurr;

                // Stop only when pressure climbs more than tolerance above the minimum
                // seen so far — genuine overshoot, not a small numerical oscillation.
                if (pCurr > pRunMin * (1.0 + PressureRiseTolerance)) break;

                otherEnd = i;
            }

            // ── Build output arrays with correct physical assignment. ────────────────
            (double T_K, double P_Pa)[] bubble;
            (double T_K, double P_Pa)[] dew;

            if (startsOnDewSide)
            {
                // 0 → critIdx  = DEW  (high-T → Tcrit); reverse → Tcrit first (ascending T)
                int dewCount = critIdx + 1;
                dew = new (double T_K, double P_Pa)[dewCount];
                for (int i = 0; i < dewCount; i++)
                    dew[i] = (T_arr[critIdx - i], p_arr[critIdx - i]);

                // critIdx → otherEnd = BUBBLE (Tcrit → low-T); reverse → low-T first
                int bubCount = otherEnd - critIdx + 1;
                bubble = new (double T_K, double P_Pa)[bubCount];
                for (int i = 0; i < bubCount; i++)
                    bubble[i] = (T_arr[otherEnd - i], p_arr[otherEnd - i]);
            }
            else
            {
                // Rare: starts on bubble side.
                int bubCount = critIdx + 1;
                bubble = new (double T_K, double P_Pa)[bubCount];
                for (int i = 0; i < bubCount; i++)
                    bubble[i] = (T_arr[critIdx - i], p_arr[critIdx - i]);

                int dewCount = otherEnd - critIdx + 1;
                dew = new (double T_K, double P_Pa)[dewCount];
                for (int i = 0; i < dewCount; i++)
                    dew[i] = (T_arr[otherEnd - i], p_arr[otherEnd - i]);
            }

            (double T_K, double P_Pa)? critical = (T_arr[critIdx], p_arr[critIdx]);

            System.Diagnostics.Debug.WriteLine(
                $"[CoolProp] BuildPhaseEnvelope: n={n}, critIdx={critIdx}, otherEnd={otherEnd}, " +
                $"startsOnDewSide={startsOnDewSide}, " +
                $"bubble={bubble.Length} pts (T: {bubble[0].T_K - 273.15:F1}→{bubble[^1].T_K - 273.15:F1}°C), " +
                $"dew={dew.Length} pts (T: {dew[0].T_K - 273.15:F1}→{dew[^1].T_K - 273.15:F1}°C), " +
                $"Tcrit={critical.Value.T_K - 273.15:F1}°C, Pcrit={critical.Value.P_Pa / 6894.76:F0} psia");

            return (bubble, dew, critical);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoolProp] BuildPhaseEnvelope exception: {ex.Message}");
            return ([], [], null);
        }
        finally
        {
            FreeHandle(ref handle, errMsg);
        }
    }

    public ComponentCriticalProperties GetCriticalProperties(string componentName)
    {
        int handle = -1;
        var errMsg = new byte[ErrBufSize];
        int errCode = 0;

        try
        {
            string fluid = MapFluid(componentName);

            handle = AbstractState_factory("HEOS", fluid, ref errCode, errMsg, ErrBufSize);
            if (errCode != 0 || handle < 0)
                throw new InvalidOperationException(
                    $"CoolProp factory failed for {componentName}: {GetErrString(errMsg)}");

            double tc = AbstractState_keyed_output(handle, iT_critical, ref errCode, errMsg, ErrBufSize);
            double pc = AbstractState_keyed_output(handle, iP_critical, ref errCode, errMsg, ErrBufSize);
            double mw = AbstractState_keyed_output(handle, imolar_mass, ref errCode, errMsg, ErrBufSize);

            return new ComponentCriticalProperties(componentName, tc, pc, 0, mw);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to get critical properties for {componentName}", ex);
        }
        finally
        {
            FreeHandle(ref handle, errMsg);
        }
    }

    public IThermodynamicState CreateState(string backend, string fluidString)
        => throw new NotSupportedException("Use static methods directly.");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> FluidMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Methane"]         = "Methane",
            ["Ethane"]          = "Ethane",
            ["Propane"]         = "Propane",
            ["IsoButane"]       = "IsoButane",
            ["n-Butane"]        = "n-Butane",
            ["Isopentane"]      = "Isopentane",
            ["n-Pentane"]       = "n-Pentane",
            ["n-Hexane"]        = "n-Hexane",
            ["n-Heptane"]       = "n-Heptane",
            ["n-Octane"]        = "n-Octane",
            ["n-Nonane"]        = "n-Nonane",
            ["n-Decane"]        = "n-Decane",
            ["CarbonDioxide"]   = "CarbonDioxide",
            ["Nitrogen"]        = "Nitrogen",
            ["HydrogenSulfide"] = "HydrogenSulfide",
            ["Water"]           = "Water",
        };

    private static string MapFluid(string name) =>
        FluidMap.TryGetValue(name, out var mapped)
            ? mapped
            : throw new ArgumentException($"Unknown component '{name}'");

    private static (double pressurePa, bool success) GetSaturationPressurePureFluid(
        string componentName, double temperatureK, double vaporQuality)
    {
        var errMsg = new byte[ErrBufSize];
        int handle = -1;
        int errCode = 0;

        try
        {
            string fluid = MapFluid(componentName);

            double p = PropsSI("P", "T", temperatureK, "Q", vaporQuality, fluid);
            if (double.IsFinite(p) && p > 0) return (p, true);

            var errBuf = new byte[ErrBufSize];
            get_global_param_string("errstring", errBuf, ErrBufSize);
            System.Diagnostics.Debug.WriteLine(
                $"[CoolProp] PropsSI failed for {fluid} T={temperatureK}K: " +
                Encoding.ASCII.GetString(errBuf).TrimEnd('\0', ' '));

            handle = AbstractState_factory("HEOS", fluid, ref errCode, errMsg, ErrBufSize);
            if (errCode != 0 || handle < 0) { LogCoolPropError("factory", errCode, errMsg); return (0, false); }

            AbstractState_specify_phase(handle, "phase_twophase", ref errCode, errMsg, ErrBufSize);
            if (errCode != 0) { LogCoolPropError("specify_phase", errCode, errMsg); return (0, false); }

            var fracs = new[] { 1.0 };
            AbstractState_set_fractions(handle, fracs, 1, ref errCode, errMsg, ErrBufSize);
            if (errCode != 0) { LogCoolPropError("set_fractions", errCode, errMsg); return (0, false); }

            AbstractState_update(handle, QT_INPUTS, vaporQuality, temperatureK,
                ref errCode, errMsg, ErrBufSize);
            if (errCode != 0) { LogCoolPropError("update(QT)", errCode, errMsg); return (0, false); }

            double pressure = AbstractState_keyed_output(handle, iP, ref errCode, errMsg, ErrBufSize);
            if (errCode != 0) { LogCoolPropError("keyed_output(P)", errCode, errMsg); return (0, false); }

            if (!double.IsFinite(pressure) || pressure <= 0) return (0, false);
            return (pressure, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoolProp] GetSatPressurePure exception: {ex.Message}");
            return (0, false);
        }
        finally
        {
            FreeHandle(ref handle, errMsg);
        }
    }

    private static void FreeHandle(ref int handle, byte[] errMsg)
    {
        if (handle >= 0)
        {
            int freeErr = 0;
            AbstractState_free(handle, ref freeErr, errMsg, ErrBufSize);
            handle = -1;
        }
    }

    private static void LogCoolPropError(string context, int errCode, byte[] errMsg)
    {
        string msg = GetErrString(errMsg);
        System.Diagnostics.Debug.WriteLine(
            $"[CoolProp] {context} failed — errCode={errCode}, msg={msg}");
    }

    private static string GetErrString(byte[] errMsg) =>
        Encoding.ASCII.GetString(errMsg).TrimEnd('\0', ' ');

    private static string GetPhaseString(double phaseIndex) => phaseIndex switch
    {
        0 => "liquid",
        1 => "gas",
        2 => "twophase",
        3 => "supercritical_liquid",
        4 => "supercritical_gas",
        5 => "supercritical",
        6 => "unknown",
        _ => "unknown"
    };
}