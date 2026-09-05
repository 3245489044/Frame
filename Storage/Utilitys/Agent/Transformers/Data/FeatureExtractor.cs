#region USINGS
using System;
using System.Collections.Generic;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Layers;
using Agent.Transformers.Models;
using Agent.Transformers.Skeletons;
#endregion

namespace Agent.Transformers.Runtime
{
    public class FeatureExtractor
    {
        public PoolingStrategy Pooling { get; set; } = PoolingStrategy.Mean;

        private List<TransformerBlock>? _blocks;

        public int ModelDim { get; }

        public bool IsInitialized => _blocks != null;

        public FeatureExtractor(int modelDim)
        {
            ModelDim = modelDim;
        }

        public void Initialize(int numLayers, int numHeads, int ffnHiddenDim,
            Core.ActivationType activation, Random? rng = null)
        {
            rng ??= new Random(42);
            _blocks = new List<TransformerBlock>(numLayers);

            for (int i = 0; i < numLayers; i++)
            {
                _blocks.Add(new TransformerBlock(
                    ModelDim, numHeads, ffnHiddenDim, activation, rng));
            }
        }

        public Tensor Extract(Tensor X)
        {
            if (_blocks == null)
                throw new InvalidOperationException("FeatureExtractor not initialized.");

            var hidden = X;

            for (int i = 0; i < _blocks.Count; i++)
            {
                hidden = _blocks[i].Forward(hidden);
            }

            return Pooling switch
            {
                PoolingStrategy.Mean => MeanPool(hidden),
                PoolingStrategy.Max => MaxPool(hidden),
                PoolingStrategy.First => FirstToken(hidden),
                PoolingStrategy.Last => LastToken(hidden),
                _ => MeanPool(hidden)
            };
        }

        private static Tensor MeanPool(Tensor hidden)
        {
            int batchSize = hidden.BatchSize;
            int seqLen = hidden.SeqLength;
            int modelDim = hidden.Dimension;

            var pooled = new Tensor(new TensorShape3D(batchSize, 1, modelDim));
            for (int b = 0; b < batchSize; b++)
                for (int d = 0; d < modelDim; d++)
                {
                    double sum = 0;
                    for (int s = 0; s < seqLen; s++)
                        sum += hidden[b, s, d];
                    pooled[b, 0, d] = sum / seqLen;
                }

            return pooled;
        }

        private static Tensor MaxPool(Tensor hidden)
        {
            int batchSize = hidden.BatchSize;
            int seqLen = hidden.SeqLength;
            int modelDim = hidden.Dimension;

            var pooled = new Tensor(new TensorShape3D(batchSize, 1, modelDim));
            for (int b = 0; b < batchSize; b++)
                for (int d = 0; d < modelDim; d++)
                {
                    double maxVal = double.MinValue;
                    for (int s = 0; s < seqLen; s++)
                        if (hidden[b, s, d] > maxVal)
                            maxVal = hidden[b, s, d];
                    pooled[b, 0, d] = maxVal;
                }

            return pooled;
        }

        private static Tensor FirstToken(Tensor hidden)
        {
            int batchSize = hidden.BatchSize;
            int modelDim = hidden.Dimension;

            var pooled = new Tensor(new TensorShape3D(batchSize, 1, modelDim));
            for (int b = 0; b < batchSize; b++)
                for (int d = 0; d < modelDim; d++)
                    pooled[b, 0, d] = hidden[b, 0, d];

            return pooled;
        }

        private static Tensor LastToken(Tensor hidden)
        {
            int batchSize = hidden.BatchSize;
            int seqLen = hidden.SeqLength;
            int modelDim = hidden.Dimension;

            var pooled = new Tensor(new TensorShape3D(batchSize, 1, modelDim));
            for (int b = 0; b < batchSize; b++)
                for (int d = 0; d < modelDim; d++)
                    pooled[b, 0, d] = hidden[b, seqLen - 1, d];

            return pooled;
        }
    }

}
