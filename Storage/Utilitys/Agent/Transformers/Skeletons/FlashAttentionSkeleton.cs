#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class FlashAttentionSkeleton
    {
        private static readonly LoopEngine _loopEngine = new();

        public static Tensor Compute(
            Tensor q,
            Tensor k,
            Tensor v,
            int blockSize = 64,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null)
        {
            if (q == null) throw new ArgumentNullException(nameof(q));
            if (k == null) throw new ArgumentNullException(nameof(k));
            if (v == null) throw new ArgumentNullException(nameof(v));

            int batchSize = q.BatchSize;
            int seqLen = q.SeqLength;
            int dim = q.Dimension;

            var output = Tensor.Zeros(new TensorShape3D(batchSize, seqLen, dim));

            _loopEngine.ExecuteInPlaceSync(
                (Q: q.DataRef, K: k.DataRef, V: v.DataRef,
                 Output: output.DataRef, Mask: mask?.DataRef,
                 BlockSize: blockSize, SeqLen: seqLen, Dim: dim, BatchSize: batchSize),
                (ctx, b, _) => {
                    for (int qStart = 0; qStart < ctx.SeqLen; qStart += ctx.BlockSize)
                    {
                        int qEnd = Math.Min(qStart + ctx.BlockSize, ctx.SeqLen);
                        int qBlockSize = qEnd - qStart;

                        var m = new double[qBlockSize];
                        var l = new double[qBlockSize];
                        var oBlock = new double[qBlockSize, ctx.Dim];

                        for (int i = 0; i < qBlockSize; i++)
                            m[i] = double.NegativeInfinity;

                        for (int kvStart = 0; kvStart < ctx.SeqLen; kvStart += ctx.BlockSize)
                        {
                            int kvEnd = Math.Min(kvStart + ctx.BlockSize, ctx.SeqLen);
                            int kvBlockSize = kvEnd - kvStart;

                            for (int i = 0; i < qBlockSize; i++)
                            {
                                double oldM = m[i];

                                double newM = oldM;
                                for (int j = 0; j < kvBlockSize; j++)
                                {
                                    double val = ctx.Q[b, qStart + i, 0];
                                    double sVal = 0;
                                    for (int d = 0; d < ctx.Dim; d++)
                                        sVal += ctx.Q[b, qStart + i, d] * ctx.K[b, kvStart + j, d];
                                    
                                    if (ctx.Mask != null)
                                        sVal += ctx.Mask[b, qStart + i, kvStart + j];
                                    
                                    if (sVal > newM) newM = sVal;
                                }

                                double sumExp = 0;
                                for (int j = 0; j < kvBlockSize; j++)
                                {
                                    double sVal = 0;
                                    for (int d = 0; d < ctx.Dim; d++)
                                        sVal += ctx.Q[b, qStart + i, d] * ctx.K[b, kvStart + j, d];
                                    
                                    if (ctx.Mask != null)
                                        sVal += ctx.Mask[b, qStart + i, kvStart + j];

                                    double expVal = Math.Exp(sVal - newM);
                                    sumExp += expVal;

                                    for (int d = 0; d < ctx.Dim; d++)
                                    {
                                        oBlock[i, d] += expVal * ctx.V[b, kvStart + j, d];
                                    }
                                }

                                double rescale = Math.Exp(oldM - newM);
                                for (int d = 0; d < ctx.Dim; d++)
                                    oBlock[i, d] *= rescale;

                                l[i] = l[i] * rescale + sumExp;
                                m[i] = newM;
                            }
                        }

                        for (int i = 0; i < qBlockSize; i++)
                        {
                            for (int d = 0; d < ctx.Dim; d++)
                            {
                                ctx.Output[b, qStart + i, d] = oBlock[i, d] / l[i];
                            }
                        }
                    }
                },
                new LoopShape(batchSize, 1));

            return output;
        }


        public static Task<Tensor> ComputeAsync(
            Tensor q,
            Tensor k,
            Tensor v,
            int blockSize = 64,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Compute(q, k, v, blockSize, mask, progress);
            }, cancellationToken);
        }
    }
}
