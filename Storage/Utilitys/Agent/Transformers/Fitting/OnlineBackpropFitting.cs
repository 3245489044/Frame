#region USINGS
using System;
using System.Collections.Generic;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Layers;
#endregion

namespace Agent.Transformers.Runtime
{
    public class OnlineBackpropFitting : BaseFittingStrategy
    {
        public override string Name => "OnlineBackprop";
        public override FittingType FittingType => FittingType.OnlineBackprop;

        public double LearningRate { get; set; } = 0.01;

        public double ForgetFactor { get; set; } = 0.95;

        public double LrDecayRate { get; set; } = 0.99;

        public bool UseMomentum { get; set; } = true;

        public double MomentumCoeff { get; set; } = 0.9;

        public double PerturbationEpsilon { get; set; } = 1e-4;

        private Dictionary<int, Tensor> _momentumCache = new();

        protected override bool FitWeights(
            RuntimeContext context,
            Tensor X,
            Tensor y,
            Action<int, int, double>? progressCallback = null)
        {
            var config = context.Config;

            InitializeBlocks(context);

            int numSamples = X.BatchSize;
            int maxIter = config.MaxIterations;
            double lr = LearningRate;

            for (int iter = 0; iter < maxIter; iter++)
            {
                double totalLoss = 0;

                for (int i = 0; i < numSamples; i++)
                {
                    var sampleX = ExtractSample(X, i);
                    var sampleY = ExtractSample(y, i);

                    var (cache, attnWeights) = ForwardWithCacheAndWeights(context, sampleX);

                    var output = cache[^1];
                    var pooledOutput = PoolFeatures(output);
                    var prediction = pooledOutput.MatMul(context.OutputWeight!);
                    var outputError = SubtractTensors(prediction, sampleY);

                    UpdateOutputWeights(context, pooledOutput, outputError, lr);

                    var currentError = outputError;
                    for (int layerIdx = config.NumLayers - 1; layerIdx >= 0; layerIdx--)
                    {
                        var layerInput = layerIdx == 0
                            ? sampleX
                            : cache[layerIdx - 1];

                        var layerOutput = cache[layerIdx];

                        var attnWeight = attnWeights[layerIdx];

                        var pseudoGrad = ComputePseudoGradient(
                            context, layerIdx, layerInput, layerOutput, currentError);

                        ApplyLayerUpdate(context, layerIdx, pseudoGrad, lr);

                        currentError = BackpropagateErrorWithAttention(
                            context, layerIdx, layerInput, layerOutput, currentError, attnWeight);
                    }

                    totalLoss += ComputeMSE(prediction, sampleY);
                }

                double mse = totalLoss / (numSamples * context.OutputDim);
                context.LossHistory.Add(mse);
                progressCallback?.Invoke(iter, maxIter, mse);

                lr *= LrDecayRate;

                if (iter > 0 && Math.Abs(mse - context.LossHistory[^2]) < config.ConvergenceThreshold)
                    break;
            }

            context.FinalLoss = context.LossHistory[^1];

            return true;
        }


        #region 伪梯度计算与反向传播


        private Tensor ComputePseudoGradient(
            RuntimeContext context,
            int layerIdx,
            Tensor layerInput,
            Tensor layerOutput,
            Tensor outputError)
        {
            var config = context.Config;
            int modelDim = config.ModelDim;

            var pseudoGrad = new Tensor(layerOutput.Shape);

            for (int b = 0; b < layerOutput.BatchSize; b++)
            {
                for (int s = 0; s < layerOutput.SeqLength; s++)
                {
                    for (int d = 0; d < modelDim; d++)
                    {
                        double errorSum = 0;
                        for (int e = 0; e < outputError.Dimension; e++)
                        {
                            errorSum += outputError[b, 0, e];
                        }

                        pseudoGrad[b, s, d] = layerOutput[b, s, d] * errorSum / outputError.Dimension;
                    }
                }
            }

            return pseudoGrad;
        }

        private void ApplyLayerUpdate(
            RuntimeContext context,
            int layerIdx,
            Tensor pseudoGrad,
            double lr)
        {
            if (context.Blocks != null && layerIdx < context.Blocks.Count)
            {
                var block = context.Blocks[layerIdx];
                var currentParams = block.GetParameters();

                var deltaParams = new double[currentParams.Length];
                int idx = 0;
                for (int i = 0; i < pseudoGrad.BatchSize; i++)
                    for (int j = 0; j < pseudoGrad.SeqLength; j++)
                        for (int k = 0; k < pseudoGrad.Dimension; k++)
                            if (idx < deltaParams.Length)
                                deltaParams[idx++] = pseudoGrad[i, j, k];

                for (int p = 0; p < currentParams.Length; p++)
                    currentParams[p] -= lr * deltaParams[p];

                block.SetParameters(currentParams);
            }
        }

        private Tensor BackpropagateError(
            RuntimeContext context,
            int layerIdx,
            Tensor layerInput,
            Tensor layerOutput,
            Tensor currentError)
        {
            var config = context.Config;
            int modelDim = config.ModelDim;

            var propagatedError = new Tensor(layerInput.Shape);

            double avgError = 0;
            for (int b = 0; b < currentError.BatchSize; b++)
                for (int d = 0; d < currentError.Dimension; d++)
                    avgError += currentError[b, 0, d];
            avgError /= (currentError.BatchSize * currentError.Dimension);

            for (int b = 0; b < propagatedError.BatchSize; b++)
                for (int s = 0; s < propagatedError.SeqLength; s++)
                    for (int d = 0; d < modelDim; d++)
                        propagatedError[b, s, d] = avgError;

            return propagatedError;
        }

        private Tensor BackpropagateErrorWithAttention(
            RuntimeContext context,
            int layerIdx,
            Tensor layerInput,
            Tensor layerOutput,
            Tensor currentError,
            Tensor attentionWeights)
        {
            var config = context.Config;
            int modelDim = config.ModelDim;
            int seqLen = layerInput.SeqLength;

            var propagatedError = new Tensor(layerInput.Shape);

            double[] outputErrorPerDim = new double[currentError.Dimension];
            for (int d = 0; d < currentError.Dimension; d++)
            {
                double sum = 0;
                for (int b = 0; b < currentError.BatchSize; b++)
                    sum += currentError[b, 0, d];
                outputErrorPerDim[d] = sum / currentError.BatchSize;
            }

            for (int b = 0; b < propagatedError.BatchSize; b++)
            {
                for (int sSrc = 0; sSrc < seqLen; sSrc++)
                {
                    for (int d = 0; d < modelDim; d++)
                    {
                        double weightedError = 0;
                        for (int sTgt = 0; sTgt < seqLen; sTgt++)
                        {
                            double attnW = attentionWeights[b, sTgt, sSrc];
                            double err = d < currentError.Dimension
                                ? outputErrorPerDim[d]
                                : 0;
                            weightedError += attnW * err;
                        }

                        double residualError = d < currentError.Dimension
                            ? outputErrorPerDim[d]
                            : 0;

                        propagatedError[b, sSrc, d] = weightedError + residualError;
                    }
                }
            }

            return propagatedError;
        }


        private void UpdateOutputWeights(
            RuntimeContext context,
            Tensor features,
            Tensor error,
            double lr)
        {
            if (context.OutputWeight == null)
            {
                int modelDim = context.Config.ModelDim;
                int outputDim = context.OutputDim;
                context.OutputWeight = new Tensor(new TensorShape3D(1, modelDim, outputDim));
            }

            for (int r = 0; r < context.Config.ModelDim; r++)
            {
                for (int c = 0; c < context.OutputDim; c++)
                {
                    double grad = features[0, 0, r] * error[0, 0, c];

                    if (UseMomentum)
                    {
                        int key = -1;
                        if (!_momentumCache.ContainsKey(key))
                            _momentumCache[key] = new Tensor(new TensorShape3D(1, context.Config.ModelDim, context.OutputDim));

                        _momentumCache[key][0, r, c] = MomentumCoeff * _momentumCache[key][0, r, c] - lr * grad;
                        context.OutputWeight[0, r, c] += _momentumCache[key][0, r, c];
                    }
                    else
                    {
                        context.OutputWeight[0, r, c] -= lr * grad;
                    }
                }
            }
        }

        #endregion

    }
}
