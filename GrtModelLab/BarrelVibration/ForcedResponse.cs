namespace GrtModelLab.BarrelVibration;

/// <summary>
/// Predicts the muzzle's actual transverse velocity over time for one real, simulated shot - the
/// piece Module 2's original "assume a step-response start" correlation was always missing, now that
/// GRT's own real P(t)/v(t) curve is readable (<see cref="GrtPluginKit.Ipc.GrtClient.GetTabResultsWithCurveAsync"/>).
///
/// EXCITATION MECHANISM (the genuinely uncertain part - stated plainly, not hidden in the math):
/// a barrel modeled as perfectly rigidly clamped at the breech cannot be excited by any force or
/// moment applied exactly at that clamp - a truly rigid support does no work and transmits nothing
/// into the free DOFs. So "recoil shakes the barrel" is NOT a mechanism this model (or any model with
/// a rigid clamp) can represent. The mechanism used here instead: the bore sags slightly under its
/// own weight ([[BeamFem.StaticSagMm]]), so a bullet traveling down it at the real, GRT-computed
/// axial velocity/acceleration must be transversely accelerated to follow that curve - by Newton's
/// third law, the bullet pushes back on the barrel with the opposite reaction force, and THAT is what
/// excites bending here. This is a real, textbook-cited candidate mechanism, not invented for this
/// project - but it is one candidate among several plausible ones (receiver/bedding flexibility, gas
/// asymmetry, etc.), none of which are modeled. Treat the result as "what this one mechanism alone
/// would predict", not "the barrel's actual motion".
///
/// Every input here is either measured (bullet mass, from the load file), computed from the
/// already-validated FEM (mode shapes, modal mass, static sag), or read from GRT's own real
/// simulation (the v(t) curve) - there is no invented scale factor or assumed excitation shape left
/// unaccounted for, unlike the retired step-response approximation.
/// </summary>
internal static class ForcedResponse
{
    public sealed record MuzzleTimeSeries(double[] TimeMs, double[] VelocityMmPerMs, double[] DisplacementMm);

    /// <summary>Diagnostic: each mode's own final-time contribution to muzzle displacement/velocity,
    /// to see which mode(s) actually dominate a given result rather than treating the sum as a black
    /// box - not used by the main calculation, only by --forcetest.</summary>
    public static (double FrequencyHz, double DispMm, double VelMmPerMs)[] PerModeFinalContribution(
        BeamFem.Mode[] modes, double[] sagMm, double barrelLengthMm, double bulletMassGr,
        double[] curveTimeMs, double[] curvePositionMm, double[] curveVelocityMps)
    {
        var whole = new (double, double, double)[modes.Length];
        for (int mi = 0; mi < modes.Length; mi++)
        {
            var series = MuzzleResponse(new[] { modes[mi] }, sagMm, barrelLengthMm, bulletMassGr, curveTimeMs, curvePositionMm, curveVelocityMps);
            whole[mi] = (modes[mi].FrequencyHz, series.DisplacementMm[^1], series.VelocityMmPerMs[^1]);
        }
        return whole;
    }

    /// <summary>
    /// Computes the muzzle's transverse displacement and velocity over the shot, via undamped modal
    /// superposition (Duhamel's integral per mode - no damping ratio is measured, so none is
    /// assumed; see the class doc's honesty note on what that does and doesn't affect).
    /// </summary>
    /// <param name="modes">Already-computed, already-validated bending modes (frequency, shape,
    /// modal mass) for this barrel.</param>
    /// <param name="sagMm">Static self-weight sag (mm) at each of the SAME node positions the modes
    /// use - see <see cref="BeamFem.StaticSagMm(BarrelProfile.BarrelProfileRecord,int)"/>.</param>
    /// <param name="barrelLengthMm">Free barrel length, mm - the muzzle is evaluated at this
    /// position on each mode's own shape.</param>
    /// <param name="bulletMassGr">Bullet mass, grains (from the load file).</param>
    /// <param name="curveTimeMs">GRT's own curve time samples, ms, from true ignition (t=0).</param>
    /// <param name="curvePositionMm">The bullet's own position at each curve sample, mm, in the SAME
    /// coordinate frame as the barrel profile (0 = breech, increasing toward the muzzle) - see
    /// reference_grt_obt_concept memory for why GRT's "x" field is used for this and how that was
    /// checked (its final value lands within a fraction of a mm of the barrel's own recorded length).</param>
    /// <param name="curveVelocityMps">The bullet's own axial velocity at each curve sample, m/s.</param>
    /// <summary>Set by --forcetest to dump the last few computed force samples for debugging - null
    /// (default) does nothing; production code never sets this.</summary>
    internal static Action<int, double, double, double, double, double>? DebugForceSink;

    public static MuzzleTimeSeries MuzzleResponse(
        BeamFem.Mode[] modes, double[] sagMm, double barrelLengthMm, double bulletMassGr,
        double[] curveTimeMs, double[] curvePositionMm, double[] curveVelocityMps)
    {
        int n = curveTimeMs.Length;
        if (n < 3) throw new ArgumentException("need at least 3 curve points");
        if (modes.Length == 0) throw new ArgumentException("need at least one mode");

        // GRT's own sampling is non-uniform (denser in some regions than others - confirmed live);
        // a acceleration finite-difference needs a time gap comparable to the curve's TYPICAL spacing
        // to avoid amplifying noise, not merely a non-zero one. Using a fixed fraction of the whole
        // shot duration (not the smallest gap actually present) keeps this robust regardless of how
        // extreme the local sampling density gets at either end.
        double minDtMs = Math.Max(1e-6, (curveTimeMs[^1] - curveTimeMs[0]) / (n * 0.5));

        double[] sagPositionsMm = Enumerable.Range(0, sagMm.Length)
            .Select(i => i * barrelLengthMm / (sagMm.Length - 1)).ToArray();
        double SagAt(double xMm) => Interp(sagPositionsMm, sagMm, xMm);
        double sagH = Math.Max(1e-3, barrelLengthMm / (sagMm.Length * 200));
        // Interp CLAMPS outside [0, barrelLengthMm] (returns the flat end value) - differencing
        // straight across that clamp (e.g. evaluating At(L+h) when the true curve only exists up to
        // L) silently treats the clamped-flat region as real data. For a first derivative that just
        // halves the answer (one side is artificially flat), but for the SECOND derivative the
        // central-difference formula divides by h^2, so the same artifact turns an O(h) slope error
        // into an O(slope/h) curvature blowup - caught live: exactly at the muzzle (x=barrelLengthMm)
        // this produced a "curvature" ~1500x too large, which squared against v~845 m/s turned a
        // sub-2N force into a spurious +209,586N at the very last curve sample. Clamping the
        // EVALUATION point (not the function's own domain clamp) to stay >=h inside each end keeps
        // the stencil entirely within real data.
        double ClampForStencil(double xMm) => Math.Clamp(xMm, sagPositionsMm[0] + sagH, sagPositionsMm[^1] - sagH);
        double SagSlopeAt(double xMmRaw)
        {
            double xMm = ClampForStencil(xMmRaw);
            return (SagAt(xMm + sagH) - SagAt(xMm - sagH)) / (2 * sagH);
        }
        double SagCurvatureAt(double xMmRaw)
        {
            double xMm = ClampForStencil(xMmRaw);
            return (SagAt(xMm + sagH) - 2 * SagAt(xMm) + SagAt(xMm - sagH)) / (sagH * sagH);
        }

        double bulletMassKg = bulletMassGr * 0.06479891 / 1000.0;

        // Bullet's transverse reaction force on the barrel at each curve sample, from the
        // curve-following kinematics: y(t) = sag(x(t)) (mm) => y'' = sag''(x)*v^2 + sag'(x)*a,
        // with x in m for the calculus (sag'/sag'' as computed above are per-mm; convert once here).
        var forceN = new double[n];
        for (int i = 0; i < n; i++)
        {
            double xMm = Math.Clamp(curvePositionMm[i], 0, barrelLengthMm);
            double vMps = curveVelocityMps[i];
            double aMps2 = CentralDiff(curveVelocityMps, curveTimeMs, i, minDtMs) * 1000.0; // d(v)/d(ms) -> d(v)/d(s)
            double slope = SagSlopeAt(xMm); // dy/dx, dimensionless - same value whether x/y are in mm or m
            // d2y/dx2: SagCurvatureAt returns it as (mm)/(mm)^2 = 1/mm; multiply by 1000 to get 1/m
            // (y_m(x_m) = Y_mm(1000*x_m)/1000 => d2y_m/dx_m2 = 1000 * d2Y_mm/dX_mm2 - chain rule twice,
            // the two factors of 1000 from x only partially cancel the 1/1000 from y).
            double curvature_1_per_m = SagCurvatureAt(xMm) * 1000.0;
            double transverseAccelMps2 = curvature_1_per_m * vMps * vMps + slope * aMps2;
            // Newton's third law: the force the BULLET needs (mass*accel) to follow the sag curve is
            // exerted BACK on the barrel with the opposite sign. SI (N, m, s) from here on for the ODE.
            forceN[i] = -bulletMassKg * transverseAccelMps2;
            DebugForceSink?.Invoke(i, xMm, vMps, aMps2, slope, forceN[i]);
        }

        // Per-mode Duhamel convolution (undamped): q(t)=1/(M*w)*int F*sin(w*(t-tau))dtau,
        // q'(t)=1/M*int F*cos(w*(t-tau))dtau. Curve time is in ms; convert to seconds for the ODE.
        double[] tSec = curveTimeMs.Select(t => t / 1000.0).ToArray();
        var dispMm = new double[n];
        var velMmPerMs = new double[n];

        foreach (var mode in modes)
        {
            double omega = 2.0 * Math.PI * mode.FrequencyHz;
            double massKg = mode.ModalMassKg;
            double phiMuzzle = mode.At(barrelLengthMm);
            if (Math.Abs(phiMuzzle) < 1e-12) continue; // this mode doesn't move the muzzle at all

            var modalForce = new double[n];
            for (int i = 0; i < n; i++)
            {
                double xMm = Math.Clamp(curvePositionMm[i], 0, barrelLengthMm);
                modalForce[i] = mode.At(xMm) * forceN[i];
            }

            var q = new double[n];
            var qDot = new double[n];
            for (int k = 0; k < n; k++)
            {
                double sinSum = 0, cosSum = 0;
                for (int j = 0; j + 1 <= k; j++)
                {
                    double dt = tSec[j + 1] - tSec[j];
                    double tauMid = (tSec[j] + tSec[j + 1]) / 2.0;
                    double fMid = (modalForce[j] + modalForce[j + 1]) / 2.0;
                    double phase = omega * (tSec[k] - tauMid);
                    sinSum += fMid * Math.Sin(phase) * dt;
                    cosSum += fMid * Math.Cos(phase) * dt;
                }
                q[k] = sinSum / (massKg * omega);
                qDot[k] = cosSum / massKg;
            }

            for (int i = 0; i < n; i++)
            {
                dispMm[i] += phiMuzzle * q[i] * 1000.0; // m -> mm
                velMmPerMs[i] += phiMuzzle * qDot[i]; // m/s and mm/ms are numerically identical (both /1000 from base SI)
            }
        }

        return new MuzzleTimeSeries(curveTimeMs, velMmPerMs, dispMm);
    }

    /// <summary>Every zero-crossing time (ms) of the muzzle's transverse velocity, linearly
    /// interpolated between the two bracketing samples - the actual predicted "nodes in time" for
    /// this specific shot, from this one candidate mechanism.</summary>
    public static double[] VelocityZeroCrossingsMs(MuzzleTimeSeries series)
    {
        var crossings = new List<double>();
        for (int i = 0; i + 1 < series.TimeMs.Length; i++)
        {
            double v0 = series.VelocityMmPerMs[i], v1 = series.VelocityMmPerMs[i + 1];
            if (v0 == 0) { crossings.Add(series.TimeMs[i]); continue; }
            if (Math.Sign(v0) != Math.Sign(v1))
            {
                double t = series.TimeMs[i] + (series.TimeMs[i + 1] - series.TimeMs[i]) * (-v0) / (v1 - v0);
                crossings.Add(t);
            }
        }
        return crossings.ToArray();
    }

    /// <summary>Central (or one-sided, near the ends) finite difference dy/dx at index i. GRT's own
    /// curve has NON-uniform time steps that can be very small in places (confirmed live: near the
    /// end of a real shot, adjacent samples landed as little as ~0.0004ms apart) as well as literally
    /// duplicated (the first two points both land at t=0, matching the worked example in GRT's own
    /// plugin-api-cmd-get_chunk.txt). A naive immediate-neighbor difference over a too-small time gap
    /// doesn't just divide by zero at the duplicate - it amplifies ordinary sample-to-sample noise
    /// into a huge, spurious spike (caught live: mode 1's own velocity jumped from a smooth
    /// -0.0065 mm/ms trend to +0.155 mm/ms at the very last sample before this fix, entirely from one
    /// too-narrow denominator). So this widens the window outward until the time gap is at least
    /// <paramref name="minDtMs"/>, not merely non-zero.</summary>
    private static double CentralDiff(double[] y, double[] xMs, int i, double minDtMs)
    {
        for (int radius = 1; radius < y.Length; radius++)
        {
            int lo = Math.Max(0, i - radius), hi = Math.Min(y.Length - 1, i + radius);
            if (hi == lo) return 0;
            double dx = xMs[hi] - xMs[lo];
            if (Math.Abs(dx) >= minDtMs) return (y[hi] - y[lo]) / dx;
        }
        return 0;
    }

    private static double Interp(double[] xs, double[] ys, double x)
    {
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        for (int i = 0; i + 1 < xs.Length; i++)
        {
            if (x > xs[i + 1]) continue;
            double span = xs[i + 1] - xs[i];
            double t = span > 0 ? (x - xs[i]) / span : 0;
            return ys[i] + t * (ys[i + 1] - ys[i]);
        }
        return ys[^1];
    }
}
