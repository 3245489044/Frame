#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
#endregion

namespace Agent.Transformers.Core
{
    public static class TensorOperations
    {
        private static readonly LoopEngine _loopEngine = new();

        public static Tensor[] SplitHeads(Tensor input, int numHeads)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (numHeads <= 0)
                throw new ArgumentException("numHeads must be positive", nameof(numHeads));

            int batchSize = input.BatchSize;
            int seqLen = input.SeqLength;
            int modelDim = input.Dimension;

            if (modelDim % numHeads != 0)
                throw new ArgumentException(
                    $"ModelDim ({modelDim}) must be divisible by numHeads ({numHeads})",
                    nameof(modelDim));

            int headDim = modelDim / numHeads;
            var heads = new Tensor[numHeads];
            var headsData = new double[numHeads][,,];
            for (int h = 0; h < numHeads; h++)
                headsData[h] = new double[batchSize, seqLen, headDim];

            _loopEngine.ExecuteInPlaceSync(
                (Src: input.DataRef, Dst: headsData, NumHeads: numHeads, HeadDim: headDim),
                (ctx, b, s) => {
                    for (int h = 0; h < ctx.NumHeads; h++)
                        for (int d = 0; d < ctx.HeadDim; d++)
                            ctx.Dst[h][b, s, d] = ctx.Src[b, s, h * ctx.HeadDim + d];
                },
                new LoopShape(batchSize, seqLen));

            for (int h = 0; h < numHeads; h++)
                heads[h] = new Tensor(headsData[h]);
            return heads;
        }

        public static Tensor ConcatHeads(Tensor[] heads)
        {
            if (heads == null) throw new ArgumentNullException(nameof(heads));
            if (heads.Length == 0)
                throw new ArgumentException("heads array must not be empty", nameof(heads));

            int numHeads = heads.Length;
            int batchSize = heads[0].BatchSize;
            int seqLen = heads[0].SeqLength;
            int headDim = heads[0].Dimension;
            int modelDim = numHeads * headDim;

            var result = new double[batchSize, seqLen, modelDim];
            var headsData = new double[numHeads][,,];
            for (int h = 0; h < numHeads; h++)
                headsData[h] = heads[h].DataRef;

            _loopEngine.ExecuteInPlaceSync(
                (Src: headsData, Dst: result, NumHeads: numHeads, HeadDim: headDim),
                (ctx, b, s) => {
                    for (int h = 0; h < ctx.NumHeads; h++)
                        for (int d = 0; d < ctx.HeadDim; d++)
                            ctx.Dst[b, s, h * ctx.HeadDim + d] = ctx.Src[h][b, s, d];
                },
                new LoopShape(batchSize, seqLen));

            return new Tensor(result);
        }


        public static Task<Tensor[]> SplitHeadsAsync(
            Tensor input,
            int numHeads,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return SplitHeads(input, numHeads);
            }, cancellationToken);
        }

        public static Task<Tensor> ConcatHeadsAsync(
            Tensor[] heads,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ConcatHeads(heads);
            }, cancellationToken);
        }
    }
}
