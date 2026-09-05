#region USINGS
using System;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Skeletons;
#endregion

namespace Agent.AgentFlow.Extensions
{
    public static class SkeletonExtensions
    {
        public static Tensor Attention(this Tensor Q, Tensor K, Tensor V,
            Tensor? mask = null,
            IProgress<MathematicalAlgorithms.ComputationProgress>? progress = null)
        {
            return AttentionSkeleton.Compute(Q, K, V, mask, progress);
        }

        public static (Tensor QRotated, Tensor KRotated) ApplyRoPE(
            this Tensor Q, Tensor K, int headDim)
        {
            return RotaryPositionEncodingSkeleton.Apply(Q, K, headDim);
        }

        public static Tensor FlashAttention(this Tensor Q, Tensor K, Tensor V,
            int blockSize = 64,
            Tensor? mask = null,
            IProgress<MathematicalAlgorithms.ComputationProgress>? progress = null)
        {
            return FlashAttentionSkeleton.Compute(Q, K, V, blockSize, mask, progress);
        }

        public static Tensor GatedFFN(this Tensor input,
            Tensor wGate, Tensor wUp, Tensor wDown,
            MathematicalAlgorithms.ActivationType activation = MathematicalAlgorithms.ActivationType.Swish)
        {
            return GatedFFNSkeleton.Compute(
                input, wGate, wUp, wDown, activation);
        }

        public static int[,] Sample(this Tensor logits,
            double temperature = 1.0,
            int topK = 0,
            double topP = 0.0,
            double minP = 0.0,
            Random? rng = null)
        {
            return SamplingSkeleton.Sample(logits, temperature, topK, topP, minP, rng);
        }

        public static int[,] BeamSearch(this int[,] initialTokens,
            int maxNewTokens, int beamSize, int vocabSize,
            Func<int[,], double[,]> getLogits,
            double lengthPenalty = 1.0,
            double repetitionPenalty = 1.0,
            int eosTokenId = -1,
            IProgress<MathematicalAlgorithms.ComputationProgress>? progress = null)
        {
            return BeamSearchSkeleton.Search(
                initialTokens, maxNewTokens, beamSize, vocabSize,
                getLogits, lengthPenalty, repetitionPenalty,
                eosTokenId, progress);
        }
    }
}
