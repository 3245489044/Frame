#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class AttentionSkeleton
    {
        public static Tensor Compute(
            Tensor q,
            Tensor k,
            Tensor v,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null)
        {
            if (q == null) throw new ArgumentNullException(nameof(q));
            if (k == null) throw new ArgumentNullException(nameof(k));
            if (v == null) throw new ArgumentNullException(nameof(v));

            int batchSize = q.BatchSize;
            int seqLen = q.SeqLength;
            int dim = q.Dimension;

            double scale = 1.0 / Math.Sqrt(dim);
            var kTransposed = k.Transpose(-2, -1);
            var scores = q.MatMul(kTransposed).Multiply(scale);

            if (mask != null)
            {
                scores = scores.Add(mask);
            }

            var weights = Softmax(scores);

            var output = weights.MatMul(v);

            progress?.Report(new ComputationProgress(100, "Attention: Complete"));

            return output;
        }

        public static Tensor ComputeWithWeights(
            Tensor q,
            Tensor k,
            Tensor v,
            out Tensor attentionWeights,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null)
        {
            if (q == null) throw new ArgumentNullException(nameof(q));
            if (k == null) throw new ArgumentNullException(nameof(k));
            if (v == null) throw new ArgumentNullException(nameof(v));

            int batchSize = q.BatchSize;
            int seqLen = q.SeqLength;
            int dim = q.Dimension;

            double scale = 1.0 / Math.Sqrt(dim);
            var kTransposed = k.Transpose(-2, -1);
            var scores = q.MatMul(kTransposed).Multiply(scale);

            if (mask != null)
            {
                scores = scores.Add(mask);
            }

            attentionWeights = Softmax(scores);

            var output = attentionWeights.MatMul(v);

            progress?.Report(new ComputationProgress(100, "Attention: Complete"));

            return output;
        }


        private static Tensor Softmax(Tensor scores)
        {
            var rawData = scores.DataRef;
            var softmaxData = SoftmaxCalculator.Compute(rawData);
            return new Tensor(softmaxData);
        }

        public static Task<Tensor> ComputeAsync(
            Tensor q,
            Tensor k,
            Tensor v,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Compute(q, k, v, mask, progress);
            }, cancellationToken);
        }
    }
}
