#region USINGS
using System;
using Agent.Transformers.Core;
using Agent.Transformers.Skeletons;
#endregion

namespace Agent.AgentFlow.Extensions
{
    public static class TensorExtensions
    {
        public static Tensor LayerNorm(this Tensor input, double[] gamma, double[] beta, double eps = 1e-5)
        {
            return NormalizationSkeleton.LayerNorm(input, gamma, beta, eps);
        }

        public static Tensor RMSNorm(this Tensor input, double[] gamma, double eps = 1e-5)
        {
            return NormalizationSkeleton.RMSNorm(input, gamma, eps);
        }

        public static Tensor Residual(this Tensor input, Func<Tensor, Tensor> sublayer,
            double[] gamma, double[] beta, bool preNorm = true, double eps = 1e-5)
        {
            return ResidualSkeleton.Apply(input, sublayer, gamma, beta, preNorm, eps);
        }

        public static double[,] ToArray2D(this Tensor tensor)
        {
            int batchSize = tensor.BatchSize;
            int seqLen = tensor.SeqLength;
            int dim = tensor.Dimension;

            var result = new double[batchSize * seqLen, dim];
            for (int b = 0; b < batchSize; b++)
            {
                for (int s = 0; s < seqLen; s++)
                {
                    for (int d = 0; d < dim; d++)
                    {
                        result[b * seqLen + s, d] = tensor[b, s, d];
                    }
                }
            }
            return result;
        }

        public static string Describe(this Tensor tensor)
        {
            return $"Tensor[Batch={tensor.BatchSize}, Seq={tensor.SeqLength}, Dim={tensor.Dimension}]";
        }

        public static string Preview(this Tensor tensor, int maxItems = 5)
        {
            var parts = new System.Collections.Generic.List<string>();
            int count = 0;

            for (int b = 0; b < tensor.BatchSize && count < maxItems; b++)
            {
                for (int s = 0; s < tensor.SeqLength && count < maxItems; s++)
                {
                    for (int d = 0; d < tensor.Dimension && count < maxItems; d++)
                    {
                        parts.Add($"[{b},{s},{d}]:{tensor[b, s, d]:F4}");
                        count++;
                    }
                }
            }

            return $"Tensor[{tensor.BatchSize},{tensor.SeqLength},{tensor.Dimension}] [{string.Join(", ", parts)}]";
        }
    }
}
