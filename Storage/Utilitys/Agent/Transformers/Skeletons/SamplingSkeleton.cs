#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class SamplingSkeleton
    {
        private static readonly LoopEngine _loopEngine = new();

        public static int[,] Sample(
            Tensor logits,
            double temperature = 1.0,
            int topK = 0,
            double topP = 0.0,
            double minP = 0.0,
            Random? rng = null)
        {
            if (logits == null) throw new ArgumentNullException(nameof(logits));

            rng ??= new Random();
            int batchSize = logits.BatchSize;
            int seqLen = logits.SeqLength;
            int vocabSize = logits.Dimension;

            var result = new int[batchSize, seqLen];

            _loopEngine.ExecuteInPlaceSync(
                (Logits: logits.DataRef, Result: result, VocabSize: vocabSize,
                 Temperature: temperature, TopK: topK, TopP: topP, MinP: minP, Rng: rng),
                (ctx, b, s) => {
                    var logitsVec = new double[ctx.VocabSize];
                    for (int d = 0; d < ctx.VocabSize; d++)
                        logitsVec[d] = ctx.Logits[b, s, d];
                    ctx.Result[b, s] = SamplingOperations.SampleToken(
                        logitsVec, ctx.Temperature, ctx.TopK, ctx.TopP, ctx.MinP, ctx.Rng);
                },
                new LoopShape(batchSize, seqLen));

            return result;
        }


        public static Task<int[,]> SampleAsync(
            Tensor logits,
            double temperature = 1.0,
            int topK = 0,
            double topP = 0.0,
            double minP = 0.0,
            Random? rng = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Sample(logits, temperature, topK, topP, minP, rng);
            }, cancellationToken);
        }
    }
}
