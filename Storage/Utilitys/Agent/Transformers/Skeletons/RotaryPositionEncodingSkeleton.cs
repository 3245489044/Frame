#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class RotaryPositionEncodingSkeleton
    {
        private static readonly LoopEngine _loopEngine = new();

        public static (Tensor Q, Tensor K) Apply(
            Tensor q,
            Tensor k,
            int seqLen,
            double theta = 10000.0)
        {
            if (q == null) throw new ArgumentNullException(nameof(q));
            if (k == null) throw new ArgumentNullException(nameof(k));

            int dim = q.Dimension;
            int halfDim = dim / 2;

            var rotatedQ = q.Clone();
            var rotatedK = k.Clone();

            var (cosValues, sinValues) = RotaryPositionEncoding.PrecomputeFrequencies(seqLen, dim, theta);

            _loopEngine.ExecuteInPlaceSync(
                (Q: rotatedQ.DataRef, Cos: cosValues, Sin: sinValues, HalfDim: halfDim, Dim: dim),
                (ctx, b, s) => {
                    int pos = Math.Min(s, seqLen - 1);
                    for (int kIdx = 0; kIdx < ctx.HalfDim; kIdx++)
                    {
                        int d1 = kIdx;
                        int d2 = kIdx + ctx.HalfDim;

                        double x1 = ctx.Q[b, s, d1];
                        double x2 = ctx.Q[b, s, d2];
                        double cos = ctx.Cos[pos, kIdx];
                        double sin = ctx.Sin[pos, kIdx];

                        ctx.Q[b, s, d1] = x1 * cos - x2 * sin;
                        ctx.Q[b, s, d2] = x2 * cos + x1 * sin;
                    }
                },
                new LoopShape(q.BatchSize, q.SeqLength));

            _loopEngine.ExecuteInPlaceSync(
                (K: rotatedK.DataRef, Cos: cosValues, Sin: sinValues, HalfDim: halfDim, Dim: dim),
                (ctx, b, s) => {
                    int pos = Math.Min(s, seqLen - 1);
                    for (int kIdx = 0; kIdx < ctx.HalfDim; kIdx++)
                    {
                        int d1 = kIdx;
                        int d2 = kIdx + ctx.HalfDim;

                        double x1 = ctx.K[b, s, d1];
                        double x2 = ctx.K[b, s, d2];
                        double cos = ctx.Cos[pos, kIdx];
                        double sin = ctx.Sin[pos, kIdx];

                        ctx.K[b, s, d1] = x1 * cos - x2 * sin;
                        ctx.K[b, s, d2] = x2 * cos + x1 * sin;
                    }
                },
                new LoopShape(k.BatchSize, k.SeqLength));

            return (rotatedQ, rotatedK);
        }

        public static Tensor ApplyToQ(
            Tensor q,
            int seqLen,
            double theta = 10000.0)
        {
            if (q == null) throw new ArgumentNullException(nameof(q));

            int dim = q.Dimension;
            int halfDim = dim / 2;

            var rotatedQ = q.Clone();

            var (cosValues, sinValues) = RotaryPositionEncoding.PrecomputeFrequencies(seqLen, dim, theta);

            _loopEngine.ExecuteInPlaceSync(
                (Q: rotatedQ.DataRef, Cos: cosValues, Sin: sinValues, HalfDim: halfDim, Dim: dim),
                (ctx, b, s) => {
                    int pos = Math.Min(s, seqLen - 1);
                    for (int kIdx = 0; kIdx < ctx.HalfDim; kIdx++)
                    {
                        int d1 = kIdx;
                        int d2 = kIdx + ctx.HalfDim;

                        double x1 = ctx.Q[b, s, d1];
                        double x2 = ctx.Q[b, s, d2];
                        double cos = ctx.Cos[pos, kIdx];
                        double sin = ctx.Sin[pos, kIdx];

                        ctx.Q[b, s, d1] = x1 * cos - x2 * sin;
                        ctx.Q[b, s, d2] = x2 * cos + x1 * sin;
                    }
                },
                new LoopShape(q.BatchSize, q.SeqLength));

            return rotatedQ;
        }

        public static Task<(Tensor Q, Tensor K)> ApplyAsync(
            Tensor q,
            Tensor k,
            int seqLen,
            double theta = 10000.0,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ComputationProgress(0, "RoPE: Applying rotary position encoding"));
                var result = Apply(q, k, seqLen, theta);
                progress?.Report(new ComputationProgress(100, "RoPE: Complete"));
                return result;
            }, cancellationToken);
        }
    }
}
