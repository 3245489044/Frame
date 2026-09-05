#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Skeletons;
#endregion

namespace Agent.Transformers.Layers
{
    public class MultiHeadAttentionLayer : BaseAttentionLayer
    {
        public MultiHeadAttentionLayer(
            int modelDim,
            int numHeads,
            Random? rng = null)
            : base(modelDim, numHeads, rng)
        {
        }

        public Tensor Forward(
            Tensor input,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            progress?.Report(new ComputationProgress(0, "MHA: Step 1/5 - QKV Projection"));

            var (Q, K, V) = ProjectQKV(input);

            progress?.Report(new ComputationProgress(20, "MHA: Step 2/5 - Split Heads"));

            var (qHeads, kHeads, vHeads) = SplitHeads(Q, K, V);

            progress?.Report(new ComputationProgress(40, "MHA: Step 3/5 - Attention"));

            var headOutputs = ComputeHeads(qHeads, kHeads, vHeads, mask, progress);

            progress?.Report(new ComputationProgress(70, "MHA: Step 4/5 - Concat Heads"));

            var output = ConcatAndProject(headOutputs);

            progress?.Report(new ComputationProgress(100, "MHA: Complete"));

            return output;
        }

        public Tensor ForwardWithAttentionWeights(
            Tensor input,
            out Tensor attentionWeights,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            progress?.Report(new ComputationProgress(0, "MHA: Step 1/5 - QKV Projection"));

            var (Q, K, V) = ProjectQKV(input);

            progress?.Report(new ComputationProgress(20, "MHA: Step 2/5 - Split Heads"));

            var (qHeads, kHeads, vHeads) = SplitHeads(Q, K, V);

            progress?.Report(new ComputationProgress(40, "MHA: Step 3/5 - Attention (with weights)"));

            var headOutputs = new Tensor[_numHeads];
            var allHeadWeights = new Tensor[_numHeads];

            for (int h = 0; h < _numHeads; h++)
            {
                headOutputs[h] = AttentionSkeleton.ComputeWithWeights(
                    qHeads[h], kHeads[h], vHeads[h],
                    out var headWeights,
                    mask, progress);
                allHeadWeights[h] = headWeights;
            }

            int batchSize = allHeadWeights[0].BatchSize;
            int seqLen = allHeadWeights[0].SeqLength;
            attentionWeights = new Tensor(new TensorShape3D(batchSize, seqLen, seqLen));
            for (int h = 0; h < _numHeads; h++)
            {
                for (int b = 0; b < batchSize; b++)
                    for (int s1 = 0; s1 < seqLen; s1++)
                        for (int s2 = 0; s2 < seqLen; s2++)
                            attentionWeights[b, s1, s2] += allHeadWeights[h][b, s1, s2] / _numHeads;
            }

            progress?.Report(new ComputationProgress(70, "MHA: Step 4/5 - Concat Heads"));

            var output = ConcatAndProject(headOutputs);

            progress?.Report(new ComputationProgress(100, "MHA: Complete"));

            return output;
        }


        public Tensor ForwardWithCache(
            Tensor input,
            KVCache kvCache,
            int layerIndex,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (kvCache == null)
                throw new ArgumentNullException(nameof(kvCache));

            var (Q, K, V) = ProjectQKV(input);

            int currentSeqLen = kvCache.CurrentLength + 1;
            var (rotatedQ, rotatedK) = RotaryPositionEncodingSkeleton.Apply(
                Q, K, currentSeqLen);

            kvCache.Append(layerIndex, rotatedK, V);

            var fullK = kvCache.GetK(layerIndex);
            var fullV = kvCache.GetV(layerIndex);

            var (qHeads, kHeads, vHeads) = SplitHeads(rotatedQ, fullK, fullV);

            var headOutputs = ComputeHeads(qHeads, kHeads, vHeads, mask, progress);

            var output = ConcatAndProject(headOutputs);

            return output;
        }

        public Task<Tensor> ForwardAsync(
            Tensor input,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Forward(input, mask, progress);
            }, cancellationToken);
        }
    }
}
