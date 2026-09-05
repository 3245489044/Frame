#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class MoESkeleton
    {
        private static readonly LoopEngine _loopEngine = new();

        public static Tensor Compute(
            Tensor input,
            Tensor routerWeight,
            (Tensor w1, Tensor w2)[] expertWeights,
            int numExperts,
            int topK = 2,
            MathematicalAlgorithms.ActivationType activation = MathematicalAlgorithms.ActivationType.Swish,
            IProgress<ComputationProgress>? progress = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (routerWeight == null) throw new ArgumentNullException(nameof(routerWeight));
            if (expertWeights == null) throw new ArgumentNullException(nameof(expertWeights));
            if (numExperts <= 0)
                throw new ArgumentException("numExperts must be positive", nameof(numExperts));
            if (topK <= 0 || topK > numExperts)
                throw new ArgumentException($"topK ({topK}) must be between 1 and numExperts ({numExperts})", nameof(topK));

            int batchSize = input.BatchSize;
            int seqLen = input.SeqLength;
            int modelDim = input.Dimension;

            var routerLogits = input.MatMul(routerWeight);

            var routerProbs = new Tensor(SoftmaxCalculator.Compute(routerLogits.DataRef));

            var output = Tensor.Zeros(new TensorShape3D(batchSize, seqLen, modelDim));

            _loopEngine.ExecuteInPlaceSync(
                (Input: input.DataRef, RouterProbs: routerProbs.DataRef,
                 Output: output.DataRef, ExpertWeights: expertWeights,
                 NumExperts: numExperts, TopK: topK, ModelDim: modelDim, Activation: activation),
                (ctx, b, s) => {
                    var probs = new double[ctx.NumExperts];
                    for (int e = 0; e < ctx.NumExperts; e++)
                        probs[e] = ctx.RouterProbs[b, s, e];

                    var topExperts = SamplingOperations.GetTopK(probs, ctx.TopK);

                    for (int t = 0; t < ctx.TopK; t++)
                    {
                        int expertIdx = topExperts[t].index;
                        double weight = topExperts[t].prob;

                        var tokenInput = new double[1, 1, ctx.ModelDim];
                        for (int d = 0; d < ctx.ModelDim; d++)
                            tokenInput[0, 0, d] = ctx.Input[b, s, d];

                        var tokenTensor = new Tensor(tokenInput);

                        var (w1, w2) = ctx.ExpertWeights[expertIdx];
                        var hidden = tokenTensor.MatMul(w1);
                        hidden = Agent.Transformers.Core.ActivationFunctions.Apply(hidden, ctx.Activation);
                        var expertOutput = hidden.MatMul(w2);

                        for (int d = 0; d < ctx.ModelDim; d++)
                        {
                            ctx.Output[b, s, d] += weight * expertOutput[0, 0, d];
                        }
                    }
                },
                new LoopShape(batchSize, seqLen));

            return output;
        }


        public static double ComputeLoadBalancingLoss(
            Tensor routerProbs,
            int numExperts,
            int topK)
        {
            if (routerProbs == null) throw new ArgumentNullException(nameof(routerProbs));

            int batchSize = routerProbs.BatchSize;
            int seqLen = routerProbs.SeqLength;
            int totalTokens = batchSize * seqLen;

            var expertCounts = new double[numExperts];
            var expertProbs = new double[numExperts];

            for (int b = 0; b < batchSize; b++)
            {
                for (int s = 0; s < seqLen; s++)
                {
                    var probs = new double[numExperts];
                    for (int e = 0; e < numExperts; e++)
                        probs[e] = routerProbs[b, s, e];

                    var topExperts = SamplingOperations.GetTopK(probs, topK);

                    foreach (var (index, prob) in topExperts)
                    {
                        expertCounts[index]++;
                        expertProbs[index] += prob;
                    }
                }
            }

            double loss = 0;
            for (int e = 0; e < numExperts; e++)
            {
                double f_i = expertCounts[e] / (totalTokens * topK);
                double P_i = expertProbs[e] / totalTokens;
                loss += f_i * P_i;
            }

            return numExperts * loss;
        }

        public static Task<Tensor> ComputeAsync(
            Tensor input,
            Tensor routerWeight,
            (Tensor w1, Tensor w2)[] expertWeights,
            int numExperts,
            int topK = 2,
            MathematicalAlgorithms.ActivationType activation = MathematicalAlgorithms.ActivationType.Swish,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Compute(input, routerWeight, expertWeights, numExperts, topK, activation, progress);
            }, cancellationToken);
        }
    }
}
