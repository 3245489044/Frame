#region USINGS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Layers;
using Agent.Transformers.Skeletons;
#endregion

namespace Agent.Transformers.Models
{
    public class TransformerModel
    {
        private readonly TransformerConfig _config;
        private readonly List<TransformerBlock> _blocks;

        private readonly Tensor _tokenEmbedding;
        private readonly Tensor _positionEmbedding;

        private readonly double[] _gammaFinal;
        private readonly double[] _betaFinal;

        private readonly Tensor _lmHead;

        public TransformerModel(TransformerConfig? config = null)
        {
            _config = config ?? TransformerConfig.Default;

            var rng = _config.Seed.HasValue
                ? new Random(_config.Seed.Value)
                : new Random();

            _tokenEmbedding = Tensor.Random(
                new TensorShape3D(1, _config.VocabSize, _config.ModelDim), rng)
                .Multiply(0.02);

            _positionEmbedding = Tensor.Random(
                new TensorShape3D(1, _config.MaxSeqLen, _config.ModelDim), rng)
                .Multiply(0.02);

            _blocks = new List<TransformerBlock>(_config.NumLayers);
            for (int i = 0; i < _config.NumLayers; i++)
            {
                _blocks.Add(new TransformerBlock(
                    _config.ModelDim,
                    _config.NumHeads,
                    _config.FfnHiddenDim,
                    _config.Activation,
                    rng));
            }

            (_gammaFinal, _betaFinal) = NormalizationSkeleton.CreateLayerNormParams(_config.ModelDim);


            _lmHead = _tokenEmbedding;
        }

        public Tensor Forward(
            int[,] tokenIds,
            IProgress<ComputationProgress>? progress = null)
        {
            if (tokenIds == null)
                throw new ArgumentNullException(nameof(tokenIds));

            int batchSize = tokenIds.GetLength(0);
            int seqLen = tokenIds.GetLength(1);

            progress?.Report(new ComputationProgress(0, "Model: Step 1/3 - Embedding"));

            var hidden = Embed(tokenIds);

            progress?.Report(new ComputationProgress(10, "Model: Step 2/3 - Transformer Blocks"));

            for (int i = 0; i < _blocks.Count; i++)
            {
                double blockProgress = 10.0 + (double)i / _blocks.Count * 80.0;
                progress?.Report(new ComputationProgress(
                    (int)blockProgress,
                    $"Model: Block {i + 1}/{_blocks.Count}"));

                hidden = _blocks[i].Forward(hidden, null, progress);
            }

            progress?.Report(new ComputationProgress(90, "Model: Step 3/3 - Final LayerNorm"));

            var output = NormalizationSkeleton.LayerNorm(hidden, _gammaFinal, _betaFinal, _config.LayerNormEps);

            progress?.Report(new ComputationProgress(100, "Model: Complete"));

            return output;
        }

        public Tensor GetLogits(Tensor hidden)
        {
            var lmHeadT = _lmHead.Transpose(-2, -1);
            return hidden.MatMul(lmHeadT);
        }

        public int[] Generate(
            int[] inputTokens,
            int maxNewTokens = 50,
            double temperature = 1.0,
            IProgress<ComputationProgress>? progress = null)
        {
            if (inputTokens == null || inputTokens.Length == 0)
                throw new ArgumentException("inputTokens must not be empty");

            var generated = new List<int>(inputTokens);
            var rng = new Random();

            for (int step = 0; step < maxNewTokens; step++)
            {
                progress?.Report(new ComputationProgress(
                    (int)((double)step / maxNewTokens * 100),
                    $"Generate: Step {step + 1}/{maxNewTokens}"));

                int currentSeqLen = generated.Count;
                int takeLen = Math.Min(currentSeqLen, _config.MaxSeqLen);
                var inputIds = new int[1, takeLen];
                for (int i = 0; i < takeLen; i++)
                    inputIds[0, i] = generated[currentSeqLen - takeLen + i];

                var output = Forward(inputIds);

                var lastHidden = output.Slice(0, takeLen - 1, 1);
                var logits = GetLogits(lastHidden);

                int nextToken = SampleToken(logits, temperature, rng);

                generated.Add(nextToken);

                if (nextToken == _config.EosTokenId)
                    break;
            }

            return generated.ToArray();
        }

        public int[] GenerateWithCache(
            int[] inputTokens,
            int maxNewTokens = 50,
            double temperature = 1.0,
            IProgress<ComputationProgress>? progress = null)
        {
            if (inputTokens == null || inputTokens.Length == 0)
                throw new ArgumentException("inputTokens must not be empty");

            var generated = new List<int>(inputTokens);
            var rng = new Random();

            var kvCache = new KVCache(
                batchSize: 1,
                dimension: _config.ModelDim,
                numLayers: _config.NumLayers,
                maxCacheLen: _config.MaxSeqLen);

            int inputLen = inputTokens.Length;
            Tensor? lastHidden = null;

            for (int promptIdx = 0; promptIdx < inputLen; promptIdx++)
            {
                var currentInput = new int[1, 1] { { inputTokens[promptIdx] } };
                var hidden = Embed(currentInput);

                for (int layerIdx = 0; layerIdx < _blocks.Count; layerIdx++)
                {
                    hidden = _blocks[layerIdx].ForwardWithCache(
                        hidden, kvCache, layerIdx, null, progress);
                }

                if (promptIdx == inputLen - 1)
                {
                    lastHidden = hidden;
                }
            }

            var finalHidden = NormalizationSkeleton.LayerNorm(
                lastHidden!, _gammaFinal, _betaFinal, _config.LayerNormEps);
            var logits = GetLogits(finalHidden);

            int nextToken = SampleToken(logits, temperature, rng);
            generated.Add(nextToken);

            if (nextToken == _config.EosTokenId)
                return generated.ToArray();


            for (int step = 1; step < maxNewTokens; step++)
            {
                progress?.Report(new ComputationProgress(
                    (int)((double)step / maxNewTokens * 100),
                    $"Generate (Cached): Step {step + 1}/{maxNewTokens}"));

                var currentInput = new int[1, 1] { { nextToken } };

                var hidden = Embed(currentInput);

                for (int layerIdx = 0; layerIdx < _blocks.Count; layerIdx++)
                {
                    hidden = _blocks[layerIdx].ForwardWithCache(
                        hidden, kvCache, layerIdx, null, progress);
                }

                var finalHiddenStep = NormalizationSkeleton.LayerNorm(
                    hidden, _gammaFinal, _betaFinal, _config.LayerNormEps);

                var currentLogits = GetLogits(finalHiddenStep);

                nextToken = SampleToken(currentLogits, temperature, rng);
                generated.Add(nextToken);

                if (nextToken == _config.EosTokenId)
                    break;
            }

            return generated.ToArray();
        }

        private static int SampleToken(Tensor logits, double temperature, Random rng)
        {
            if (temperature <= 0)
            {
                return (int)logits.ArgMax(dim: 2)[0, 0, 0];
            }
            else
            {
                var probs = logits.Divide(temperature).Softmax(dim: 2);
                return SampleFromProbs(probs, rng);
            }
        }

        private static int SampleFromProbs(Tensor probs, Random rng)
        {
            double sample = rng.NextDouble();
            double cumulative = 0;
            for (int d = 0; d < probs.Dimension; d++)
            {
                cumulative += probs[0, 0, d];
                if (sample < cumulative)
                    return d;
            }
            return probs.Dimension - 1;
        }

        private Tensor Embed(int[,] tokenIds)
        {
            Console.Error.WriteLine(
                $"[TransformerModel.Embed] _config.VocabSize={_config.VocabSize}, " +
                $"_tokenEmbedding shape=[{_tokenEmbedding.Shape.Batch}×{_tokenEmbedding.Shape.SeqLen}×{_tokenEmbedding.Shape.Dim}]");
            return EmbeddingSkeleton.Lookup(
                tokenIds, _tokenEmbedding, _positionEmbedding, _config.VocabSize);
        }

        public Task<Tensor> ForwardAsync(
            int[,] tokenIds,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Forward(tokenIds, progress);
            }, cancellationToken);
        }
    }
}
