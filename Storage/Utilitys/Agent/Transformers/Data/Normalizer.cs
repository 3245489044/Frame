#region USINGS
using System;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public class Normalizer
    {
        public NormalizationType Type { get; }

        public double[]? Mean { get; private set; }

        public double[]? Std { get; private set; }

        public double[]? Min { get; private set; }

        public double[]? Max { get; private set; }

        public bool IsFitted { get; private set; }

        public Normalizer(NormalizationType type = NormalizationType.ZScore)
        {
            Type = type;
        }

        public void Fit(Tensor X)
        {
            int numSamples = X.BatchSize;
            int seqLen = X.SeqLength;
            int dim = X.Dimension;

            Mean = new double[dim];
            Std = new double[dim];
            Min = new double[dim];
            Max = new double[dim];

            for (int d = 0; d < dim; d++)
            {
                double sum = 0;
                double minVal = double.MaxValue;
                double maxVal = double.MinValue;
                int count = 0;

                for (int b = 0; b < numSamples; b++)
                {
                    for (int s = 0; s < seqLen; s++)
                    {
                        double val = X[b, s, d];
                        sum += val;
                        if (val < minVal) minVal = val;
                        if (val > maxVal) maxVal = val;
                        count++;
                    }
                }

                Mean[d] = sum / count;
                Min[d] = minVal;
                Max[d] = maxVal;
            }

            for (int d = 0; d < dim; d++)
            {
                double sumSq = 0;
                int count = 0;
                for (int b = 0; b < numSamples; b++)
                {
                    for (int s = 0; s < seqLen; s++)
                    {
                        double diff = X[b, s, d] - Mean[d];
                        sumSq += diff * diff;
                        count++;
                    }
                }
                Std[d] = Math.Sqrt(sumSq / count) + 1e-8;
            }

            IsFitted = true;
        }

        public Tensor Transform(Tensor X)
        {
            if (!IsFitted)
                throw new InvalidOperationException("Normalizer not fitted.");

            var result = new Tensor(X.Shape);

            for (int b = 0; b < X.BatchSize; b++)
            {
                for (int s = 0; s < X.SeqLength; s++)
                {
                    for (int d = 0; d < X.Dimension; d++)
                    {
                        double val = X[b, s, d];
                        result[b, s, d] = Type switch
                        {
                            NormalizationType.ZScore => (val - Mean![d]) / Std![d],
                            NormalizationType.MinMax => (val - Min![d]) / (Max![d] - Min![d] + 1e-8),
                            _ => val
                        };
                    }
                }
            }

            return result;
        }

        public Tensor InverseTransform(Tensor X)
        {
            if (!IsFitted)
                throw new InvalidOperationException("Normalizer not fitted.");

            var result = new Tensor(X.Shape);

            for (int b = 0; b < X.BatchSize; b++)
            {
                for (int s = 0; s < X.SeqLength; s++)
                {
                    for (int d = 0; d < X.Dimension; d++)
                    {
                        double val = X[b, s, d];
                        result[b, s, d] = Type switch
                        {
                            NormalizationType.ZScore => val * Std![d] + Mean![d],
                            NormalizationType.MinMax => val * (Max![d] - Min![d] + 1e-8) + Min![d],
                            _ => val
                        };
                    }
                }
            }

            return result;
        }
    }

}
