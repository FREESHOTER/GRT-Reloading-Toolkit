using GrtModelLab.BarrelProfile;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace GrtModelLab.BarrelVibration;

/// <summary>
/// V2 — a real variable-profile Euler-Bernoulli finite-element model of the barrel: N elements
/// along the length, each with its own local diameter (so a real taper actually changes the
/// answer, unlike V1's single equivalent-uniform-diameter approximation), clamped at the chamber
/// end and free at the muzzle, solved as a generalized eigenvalue problem for natural frequencies
/// and mode shapes.
///
/// This is the FEM every mechanical-vibrations textbook builds for a stepped/tapered beam (e.g.
/// Rao, "Mechanical Vibrations", or Cook, "Concepts and Applications of Finite Element Analysis")
/// — nothing novel in the formulation. What's actually being trusted here is the arithmetic: fed a
/// UNIFORM profile, it must converge to BeamModel's (V1's) closed form as the element count grows,
/// verified in BeamFemTestCli rather than asserted.
/// </summary>
public static class BeamFem
{
    /// <summary>
    /// Consistent element stiffness and mass matrices for a 2-node Euler-Bernoulli beam element
    /// (4 DOF: transverse displacement and rotation at each end). Standard textbook matrices —
    /// e.g. Cook, "Concepts and Applications of Finite Element Analysis", or Rao, "Mechanical
    /// Vibrations" — reproduced here rather than cited from a library because this is a small,
    /// fixed, easily-checked pair of 4x4s and pulling in a whole FEM package for them would be a
    /// much larger dependency than the two matrices are worth.
    /// </summary>
    private static (Matrix<double> K, Matrix<double> M) ElementMatrices(double e, double momentOfInertia, double rho, double area, double length)
    {
        double l = length, l2 = length * length, l3 = length * length * length;

        double kCoeff = e * momentOfInertia / l3;
        var k = DenseMatrix.OfArray(new double[,]
        {
            { 12,      6 * l,    -12,     6 * l   },
            { 6 * l,   4 * l2,   -6 * l,  2 * l2  },
            { -12,    -6 * l,     12,    -6 * l   },
            { 6 * l,   2 * l2,   -6 * l,  4 * l2  },
        }) * kCoeff;

        double mCoeff = rho * area * l / 420.0;
        var m = DenseMatrix.OfArray(new double[,]
        {
            { 156,     22 * l,    54,    -13 * l  },
            { 22 * l,  4 * l2,    13 * l, -3 * l2 },
            { 54,      13 * l,   156,    -22 * l  },
            { -13 * l, -3 * l2,  -22 * l, 4 * l2  },
        }) * mCoeff;

        return (k, m);
    }

    /// <summary>Piecewise-linear interpolation of a barrel profile's outer diameter at any
    /// position, clamped to the profile's own end values outside its span.</summary>
    private static Func<double, double> Interpolator(BarrelProfileRecord rec)
    {
        var pts = rec.Points.OrderBy(p => p.PositionMm).ToList();
        if (pts.Count < 2) throw new ArgumentException("profile needs at least 2 points");
        return posMm =>
        {
            if (posMm <= pts[0].PositionMm) return pts[0].OuterDiameterMm;
            if (posMm >= pts[^1].PositionMm) return pts[^1].OuterDiameterMm;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                if (posMm > pts[i + 1].PositionMm) continue;
                double span = pts[i + 1].PositionMm - pts[i].PositionMm;
                double t = span > 0 ? (posMm - pts[i].PositionMm) / span : 0;
                return pts[i].OuterDiameterMm + t * (pts[i + 1].OuterDiameterMm - pts[i].OuterDiameterMm);
            }
            return pts[^1].OuterDiameterMm;
        };
    }

    /// <summary>
    /// Natural frequencies in Hz for the first <paramref name="modeCount"/> bending modes of a
    /// clamped-free beam whose outer diameter varies along its length per
    /// <paramref name="outerDiameterMmAt"/>.
    /// </summary>
    /// <param name="elasticModulusGPa">Steel's Young's modulus, GPa.</param>
    /// <param name="densityKgM3">Steel density, kg/m3.</param>
    /// <param name="lengthMm">Free (cantilevered) barrel length, mm.</param>
    /// <param name="boreDiameterMm">Bore/groove diameter, mm — constant along the length.</param>
    /// <param name="outerDiameterMmAt">The taper: outer diameter, mm, at a given position from the
    /// clamped (breech) end, mm.</param>
    /// <param name="elementCount">Number of beam elements the length is divided into. More
    /// elements track the taper more closely at the cost of a bigger eigenproblem; see
    /// BeamFemTestCli for the convergence behavior this trades off.</param>
    public static double[] NaturalFrequenciesHz(double elasticModulusGPa, double densityKgM3, double lengthMm,
        double boreDiameterMm, Func<double, double> outerDiameterMmAt, int elementCount, int modeCount = 3)
    {
        if (elementCount < 1) throw new ArgumentException("need at least one element");
        if (modeCount < 1) throw new ArgumentException("need at least one mode");
        if (lengthMm <= 0) throw new ArgumentException("length must be positive");

        double e = elasticModulusGPa * 1e9;      // GPa -> Pa
        double rho = densityKgM3;
        double lengthTotal = lengthMm / 1000.0;   // mm -> m
        double elementLength = lengthTotal / elementCount;

        int dofCount = 2 * (elementCount + 1);
        var globalK = DenseMatrix.Create(dofCount, dofCount, 0.0);
        var globalM = DenseMatrix.Create(dofCount, dofCount, 0.0);

        for (int el = 0; el < elementCount; el++)
        {
            // Element properties from the taper at its midpoint - the standard "stepped beam"
            // approximation to a smoothly tapered member; refines as elementCount grows.
            double midPosMm = (el + 0.5) * elementLength * 1000.0;
            double odMm = outerDiameterMmAt(midPosMm);
            var (momentOfInertia, area) = BeamSection.Annulus(odMm, boreDiameterMm);
            var (ke, me) = ElementMatrices(e, momentOfInertia, rho, area, elementLength);

            int baseDof = 2 * el; // this element covers global DOFs [baseDof .. baseDof+3]
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                {
                    globalK[baseDof + r, baseDof + c] += ke[r, c];
                    globalM[baseDof + r, baseDof + c] += me[r, c];
                }
        }

        // Clamped at the breech (node 0): displacement and rotation both zero there, so those two
        // DOFs never appear in the equations of motion - drop the first two rows/columns rather
        // than solving a problem that expects them free.
        var freeK = globalK.SubMatrix(2, dofCount - 2, 2, dofCount - 2);
        var freeM = globalM.SubMatrix(2, dofCount - 2, 2, dofCount - 2);

        double[] omega2 = SolveGeneralizedEigenvalues(freeK, freeM);
        return omega2
            .Where(v => v > 0) // numerical noise can leave a near-zero value slightly negative
            .OrderBy(v => v)
            .Take(modeCount)
            .Select(v => Math.Sqrt(v) / (2.0 * Math.PI))
            .ToArray();
    }

    /// <summary>Runs V2 straight off a saved barrel profile - its own material, bore, length, and
    /// its real taper (not reduced to one equivalent diameter, unlike V1).</summary>
    public static double[] NaturalFrequenciesHz(BarrelProfileRecord rec, int elementCount, int modeCount = 3) =>
        NaturalFrequenciesHz(rec.ElasticModulusGPa, rec.DensityKgM3, rec.LengthMm, rec.BoreDiameterMm,
            Interpolator(rec), elementCount, modeCount);

    /// <summary>
    /// One mode: its frequency, and the barrel's transverse displacement shape at that frequency —
    /// one value per node (including the clamped breech node, always 0), evenly spaced from 0 to
    /// the full length. The displacement is normalized to a peak absolute value of 1: mode shapes
    /// are only defined up to an arbitrary scale (the eigensolver's own scale has no physical
    /// meaning), so this is a SHAPE to draw, not a real deflection in mm.
    /// </summary>
    /// <param name="ModalMassKg">Generalized (modal) mass, kg, corresponding to THIS peak-normalized
    /// shape specifically (phi^T * M * phi using the peak-normalized eigenvector) - needed to convert
    /// a modal force into a modal acceleration in a forced-response calculation
    /// (<see cref="ForcedResponse"/>). Meaningless on its own once the shape is renormalized to
    /// something else; always recompute together with the shape, never reuse in isolation.</param>
    public sealed record Mode(double FrequencyHz, double[] PositionsMm, double[] Displacement, double ModalMassKg)
    {
        /// <summary>Linear interpolation of this mode's own (peak-normalized) displacement at an
        /// arbitrary position, clamped to the mode's own endpoints outside its span - same pattern as
        /// <see cref="Interpolator"/> uses for the barrel's outer diameter.</summary>
        public double At(double posMm)
        {
            if (posMm <= PositionsMm[0]) return Displacement[0];
            if (posMm >= PositionsMm[^1]) return Displacement[^1];
            for (int i = 0; i + 1 < PositionsMm.Length; i++)
            {
                if (posMm > PositionsMm[i + 1]) continue;
                double span = PositionsMm[i + 1] - PositionsMm[i];
                double t = span > 0 ? (posMm - PositionsMm[i]) / span : 0;
                return Displacement[i] + t * (Displacement[i + 1] - Displacement[i]);
            }
            return Displacement[^1];
        }

        /// <summary>Central-difference slope (1/mm) of this mode's own shape at an arbitrary position -
        /// needed for the bullet-follows-the-bore-curve excitation mechanism, which couples to a
        /// mode's LOCAL SLOPE, not just its displacement, at the bullet's current position. Clamps
        /// the EVALUATION point (not just At's own domain clamp) to stay inside the real data by at
        /// least h - differencing straight across At's flat clamped extension silently halves the
        /// slope right at the endpoints (caught live in the same bug for BeamVibration.ForcedResponse's
        /// analogous sag-curvature helper, which is squared and blows up rather than just halving -
        /// fixed here too even though nothing currently calls SlopeAt exactly at an endpoint).</summary>
        public double SlopeAt(double posMm)
        {
            double h = Math.Max(1e-3, (PositionsMm[^1] - PositionsMm[0]) / (PositionsMm.Length * 200));
            double clamped = Math.Clamp(posMm, PositionsMm[0] + h, PositionsMm[^1] - h);
            return (At(clamped + h) - At(clamped - h)) / (2 * h);
        }
    }

    /// <summary>
    /// Same model as <see cref="NaturalFrequenciesHz(double,double,double,double,Func{double,double},int,int)"/>,
    /// but also returns each mode's shape — needed to eventually show where a given GRT-predicted
    /// barrel time falls relative to the barrel's actual bending pattern, not just what frequency
    /// it rings at.
    /// </summary>
    public static Mode[] Modes(double elasticModulusGPa, double densityKgM3, double lengthMm,
        double boreDiameterMm, Func<double, double> outerDiameterMmAt, int elementCount, int modeCount = 3)
    {
        if (elementCount < 1) throw new ArgumentException("need at least one element");
        if (modeCount < 1) throw new ArgumentException("need at least one mode");
        if (lengthMm <= 0) throw new ArgumentException("length must be positive");

        double e = elasticModulusGPa * 1e9;
        double rho = densityKgM3;
        double lengthTotal = lengthMm / 1000.0;
        double elementLength = lengthTotal / elementCount;

        int dofCount = 2 * (elementCount + 1);
        var globalK = DenseMatrix.Create(dofCount, dofCount, 0.0);
        var globalM = DenseMatrix.Create(dofCount, dofCount, 0.0);

        for (int el = 0; el < elementCount; el++)
        {
            double midPosMm = (el + 0.5) * elementLength * 1000.0;
            double odMm = outerDiameterMmAt(midPosMm);
            var (momentOfInertia, area) = BeamSection.Annulus(odMm, boreDiameterMm);
            var (ke, me) = ElementMatrices(e, momentOfInertia, rho, area, elementLength);

            int baseDof = 2 * el;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                {
                    globalK[baseDof + r, baseDof + c] += ke[r, c];
                    globalM[baseDof + r, baseDof + c] += me[r, c];
                }
        }

        var freeK = globalK.SubMatrix(2, dofCount - 2, 2, dofCount - 2);
        var freeM = globalM.SubMatrix(2, dofCount - 2, 2, dofCount - 2);

        var (omega2, lInvT) = SolveGeneralizedEigenproblem(freeK, freeM, out var eigenvectorsY);

        var order = Enumerable.Range(0, omega2.Length)
            .Where(i => omega2[i] > 0)
            .OrderBy(i => omega2[i])
            .Take(modeCount)
            .ToArray();

        double[] positions = Enumerable.Range(0, elementCount + 1)
            .Select(i => i * elementLength * 1000.0) // m -> mm
            .ToArray();

        var modes = new Mode[order.Length];
        for (int m = 0; m < order.Length; m++)
        {
            int col = order[m];
            var y = eigenvectorsY.Column(col);
            var phiReduced = lInvT * y; // back from the Cholesky-transformed space to real DOFs

            // phiReduced holds [v1,theta1,v2,theta2,...] for every node EXCEPT the clamped one;
            // node 0's displacement is always 0 and was never part of this vector.
            var displacement = new double[elementCount + 1];
            displacement[0] = 0.0;
            for (int node = 1; node <= elementCount; node++)
                displacement[node] = phiReduced[2 * (node - 1)];

            double peak = displacement.Select(Math.Abs).Max();
            double scale = peak > 0 ? 1.0 / peak : 1.0;
            if (peak > 0) for (int i = 0; i < displacement.Length; i++) displacement[i] /= peak;

            // Modal mass for THIS (peak-normalized) shape: phi^T*M*phi, computed in the same
            // Cholesky-transformed space the eigensolve already used (y = L^T*phi, so
            // phi^T*M*phi = phi^T*L*L^T*phi = y^T*y = |y|^2) - avoids re-multiplying the full mass
            // matrix out, and the eigensolver already normalizes y to unit length, so |y|^2 = 1
            // before rescaling; the peak-normalization above (dividing phi by `peak`) scales the
            // modal mass by `scale^2` accordingly.
            double modalMassKg = scale * scale;

            double freqHz = Math.Sqrt(omega2[col]) / (2.0 * Math.PI);
            modes[m] = new Mode(freqHz, positions, displacement, modalMassKg);
        }
        return modes;
    }

    /// <summary>
    /// Static transverse deflection (mm) of the barrel under its OWN weight, cantilevered at the
    /// breech exactly like the modal analysis - same stiffness assembly, solved once for a fixed
    /// gravity load instead of the eigenproblem. This is the assumed bore-centerline shape a
    /// traveling bullet must follow; see <see cref="ForcedResponse"/> for why that shape (not the
    /// barrel's cross-section) is what matters for one candidate transverse-excitation mechanism.
    /// Sign convention: positive deflection is in whatever direction gravity is assumed to pull
    /// (arbitrary - only slopes/curvatures derived from this shape are physically used, and those
    /// don't care about the sign convention as long as it's applied consistently).
    /// </summary>
    public static double[] StaticSagMm(double elasticModulusGPa, double densityKgM3, double lengthMm,
        double boreDiameterMm, Func<double, double> outerDiameterMmAt, int elementCount)
    {
        if (elementCount < 1) throw new ArgumentException("need at least one element");
        if (lengthMm <= 0) throw new ArgumentException("length must be positive");

        const double g = 9.80665; // m/s^2
        double e = elasticModulusGPa * 1e9;
        double rho = densityKgM3;
        double lengthTotal = lengthMm / 1000.0;
        double elementLength = lengthTotal / elementCount;

        int dofCount = 2 * (elementCount + 1);
        var globalK = DenseMatrix.Create(dofCount, dofCount, 0.0);
        var globalF = DenseVector.Create(dofCount, 0.0);
        Span<double> fe = stackalloc double[4];

        for (int el = 0; el < elementCount; el++)
        {
            double midPosMm = (el + 0.5) * elementLength * 1000.0;
            double odMm = outerDiameterMmAt(midPosMm);
            var (momentOfInertia, area) = BeamSection.Annulus(odMm, boreDiameterMm);
            var (ke, _) = ElementMatrices(e, momentOfInertia, rho, area, elementLength);

            // Consistent nodal load vector for a uniform distributed load q (N/m) over one element -
            // standard fixed-end-equivalent forces/moments (e.g. Cook, same source as the element
            // matrices above): [qL/2, qL^2/12, qL/2, -qL^2/12] in the same [v1,th1,v2,th2] DOF order.
            double q = rho * area * g;
            double l = elementLength, l2 = l * l;
            fe[0] = q * l / 2; fe[1] = q * l2 / 12; fe[2] = q * l / 2; fe[3] = -q * l2 / 12;

            int baseDof = 2 * el;
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++) globalK[baseDof + r, baseDof + c] += ke[r, c];
                globalF[baseDof + r] += fe[r];
            }
        }

        var freeK = globalK.SubMatrix(2, dofCount - 2, 2, dofCount - 2);
        var freeF = globalF.SubVector(2, dofCount - 2);
        var freeW = freeK.Solve(freeF);

        var sagM = new double[elementCount + 1];
        sagM[0] = 0.0;
        for (int node = 1; node <= elementCount; node++)
            sagM[node] = freeW[2 * (node - 1)];
        return sagM.Select(w => w * 1000.0).ToArray(); // m -> mm
    }

    /// <summary>Runs <see cref="StaticSagMm(double,double,double,double,Func{double,double},int)"/>
    /// straight off a saved barrel profile.</summary>
    public static double[] StaticSagMm(BarrelProfileRecord rec, int elementCount) =>
        StaticSagMm(rec.ElasticModulusGPa, rec.DensityKgM3, rec.LengthMm, rec.BoreDiameterMm,
            Interpolator(rec), elementCount);

    /// <summary>Runs <see cref="Modes(double,double,double,double,Func{double,double},int,int)"/>
    /// straight off a saved barrel profile.</summary>
    public static Mode[] Modes(BarrelProfileRecord rec, int elementCount, int modeCount = 3) =>
        Modes(rec.ElasticModulusGPa, rec.DensityKgM3, rec.LengthMm, rec.BoreDiameterMm,
            Interpolator(rec), elementCount, modeCount);

    /// <summary>
    /// Solves K*phi = omega^2*M*phi (both symmetric, M positive definite) by the standard Cholesky
    /// transform: M = L*L^T, y = L^T*phi turns it into the standard symmetric eigenproblem
    /// (L^-1*K*L^-T)*y = omega^2*y. Returns the omega^2 eigenvalues (unsorted, one per eigenvector
    /// column), L^-T (needed to map an eigenvector y back to the real DOFs phi = L^-T*y), and the
    /// eigenvectors y themselves via the out parameter.
    /// </summary>
    private static (double[] Omega2, Matrix<double> LInvT) SolveGeneralizedEigenproblem(
        Matrix<double> k, Matrix<double> m, out Matrix<double> eigenvectorsY)
    {
        var cholesky = m.Cholesky();
        var lInv = cholesky.Factor.Inverse();
        var a = lInv * k * lInv.Transpose();
        a = (a + a.Transpose()) * 0.5;
        var evd = a.Evd(MathNet.Numerics.LinearAlgebra.Symmetricity.Symmetric);
        eigenvectorsY = evd.EigenVectors;
        return (evd.EigenValues.Select(c => c.Real).ToArray(), lInv.Transpose());
    }

    private static double[] SolveGeneralizedEigenvalues(Matrix<double> k, Matrix<double> m)
    {
        var (omega2, _) = SolveGeneralizedEigenproblem(k, m, out _);
        return omega2;
    }
}
