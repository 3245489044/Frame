#region USINGS
using System;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class EmbeddingSkeleton
    {
        private static readonly LoopEngine _loopEngine = new();

        public static Tensor Lookup(
            int[,] tokenIds,
            Tensor tokenEmbedding,
            Tensor positionEmbedding,
            int vocabSize)
        {
            if (tokenIds == null)
                throw new ArgumentNullException(nameof(tokenIds));
            if (tokenEmbedding == null)
                throw new ArgumentNullException(nameof(tokenEmbedding));
            if (positionEmbedding == null)
                throw new ArgumentNullException(nameof(positionEmbedding));

            int batchSize = tokenIds.GetLength(0);
            int seqLen = tokenIds.GetLength(1);
            int modelDim = tokenEmbedding.Dimension;

            var tokenEmbShape = tokenEmbedding.Shape;
            Console.Error.WriteLine(
                $"[EmbeddingSkeleton] vocabSize={vocabSize}, " +
                $"tokenEmbedding shape=[{tokenEmbShape.Batch}×{tokenEmbShape.SeqLen}×{tokenEmbShape.Dim}], " +
                $"tokenIds shape=[{batchSize}×{seqLen}], " +
                $"tokenEmbedding.DataRef.Dim0={tokenEmbedding.DataRef.GetLength(0)}, " +
                $"tokenEmbedding.DataRef.Dim1={tokenEmbedding.DataRef.GetLength(1)}, " +
                $"tokenEmbedding.DataRef.Dim2={tokenEmbedding.DataRef.GetLength(2)}");

            var result = new Tensor(new TensorShape3D(batchSize, seqLen, modelDim));

            _loopEngine.ExecuteInPlaceSync(
                (TokenIds: tokenIds, TokenEmb: tokenEmbedding.DataRef,
                 PosEmb: positionEmbedding.DataRef, Result: result.DataRef,
                 VocabSize: vocabSize, ModelDim: modelDim),
                (ctx, b, s) => {
                    int tokenId = ctx.TokenIds[b, s];
                    if (tokenId < 0 || tokenId >= ctx.VocabSize)
                        throw new IndexOutOfRangeException(
                            $"Token ID {tokenId} out of range [0, {ctx.VocabSize}) " +
                            $"(tokenEmbedding shape=[{ctx.TokenEmb.GetLength(0)}×{ctx.TokenEmb.GetLength(1)}×{ctx.TokenEmb.GetLength(2)}])");

                    for (int d = 0; d < ctx.ModelDim; d++)
                    {
                        ctx.Result[b, s, d] = ctx.TokenEmb[0, tokenId, d]
                                            + ctx.PosEmb[0, s, d];
                    }
                },
                new LoopShape(batchSize, seqLen));

            return result;
        }
    }
}
