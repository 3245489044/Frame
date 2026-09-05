#region USINGS
using System;
#endregion

namespace MathematicalAlgorithms
{
    public static class RotaryPositionEncoding
    {
        public static (double[,] cos, double[,] sin) PrecomputeFrequencies(
            int seqLen,
            int dim,
            double theta = 10000.0)
        {
            if (seqLen <= 0)
                throw new ArgumentException("seqLen must be positive", nameof(seqLen));
            if (dim <= 0)
                throw new ArgumentException("dim must be positive", nameof(dim));

            int halfDim = dim / 2;
            var cosValues = new double[seqLen, halfDim];
            var sinValues = new double[seqLen, halfDim];

            for (int pos = 0; pos < seqLen; pos++)
            {
                for (int kIdx = 0; kIdx < halfDim; kIdx++)
                {
                    double freq = 1.0 / Math.Pow(theta, 2.0 * kIdx / dim);
                    double angle = pos * freq;
                    cosValues[pos, kIdx] = Math.Cos(angle);
                    sinValues[pos, kIdx] = Math.Sin(angle);
                }
            }

            return (cosValues, sinValues);
        }

        public static double[] ApplyRotation(
            double[] x,
            double[] cos,
            double[] sin,
            int halfDim)
        {
            if (x == null) throw new ArgumentNullException(nameof(x));
            if (cos == null) throw new ArgumentNullException(nameof(cos));
            if (sin == null) throw new ArgumentNullException(nameof(sin));

            int dim = x.Length;
            var result = new double[dim];

            for (int kIdx = 0; kIdx < halfDim; kIdx++)
            {
                int d1 = kIdx;
                int d2 = kIdx + halfDim;

                double x1 = x[d1];
                double x2 = d2 < dim ? x[d2] : 0;

                result[d1] = x1 * cos[kIdx] - x2 * sin[kIdx];
                if (d2 < dim)
                    result[d2] = x2 * cos[kIdx] + x1 * sin[kIdx];
            }

            return result;
        }
    }
}
