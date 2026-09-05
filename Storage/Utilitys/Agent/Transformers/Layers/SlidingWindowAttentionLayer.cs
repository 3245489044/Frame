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
    public class SlidingWindowAttentionLayer : BaseAttentionLayer
    {
        private static readonly LoopEngine _loopEngine = new();
        private readonly int _windowSize;


        public SlidingWindowAttentionLayer(
            int modelDim,
            int numHeads,
            int windowSize = 4096,
            Random? rng = null)
            : base(modelDim, numHeads, rng)
        {
            if (windowSize <= 0)
                throw new ArgumentException("windowSize must be positive", nameof(windowSize));
            _windowSize = windowSize;
        }

        public Tensor Forward(
            Tensor input,
            Tensor? mask = null,
            IProgress<ComputationProgress>? progress = null)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            int seqLen = input.SeqLength;

            var (Q, K, V) = ProjectQKV(input);

            var windowMask = CreateSlidingWindowMask(seqLen);

            var finalMask = mask != null ? windowMask.Add(mask) : windowMask;

            var (qHeads, kHeads, vHeads) = SplitHeads(Q, K, V);

            var headOutputs = ComputeHeads(qHeads, kHeads, vHeads, finalMask, progress);

            var output = ConcatAndProject(headOutputs);

            return output;
        }

        private Tensor CreateSlidingWindowMask(int seqLen)
        {
            var maskData = new double[1, seqLen, seqLen];
            int halfWindow = _windowSize / 2;

            _loopEngine.ExecuteInPlaceSync(
                (Mask: maskData, HalfWindow: halfWindow, SeqLen: seqLen),
                (ctx, _, i) => {
                    for (int j = 0; j < ctx.SeqLen; j++)
                    {
                        if (j > i)
                        {
                            ctx.Mask[0, i, j] = double.NegativeInfinity;
                        }
                        else if (i - j > ctx.HalfWindow)
                        {
                            ctx.Mask[0, i, j] = double.NegativeInfinity;
                        }
                        else
                        {
                            ctx.Mask[0, i, j] = 0;
                        }
                    }
                },
                new LoopShape(1, seqLen));

            return new Tensor(maskData);
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
