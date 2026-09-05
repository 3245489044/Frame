#region USINGS
using System;
using System.Collections.Generic;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
using Agent.Transformers.Layers;
#endregion

namespace Agent.Transformers.Runtime
{
    public class LayerwiseFitting : BaseFittingStrategy
    {
        public override string Name => "LayerwiseBP";
        public override FittingType FittingType => FittingType.LayerwiseBP;

        public int RefinementRounds { get; set; } = 3;

        public double LearningRate { get; set; } = 0.1;

        public bool UseMomentum { get; set; } = true;

        public double MomentumCoeff { get; set; } = 0.9;

        protected override bool FitWeights(
            RuntimeContext context,
            Tensor X,
            Tensor y,
            Action<int, int, double>? progressCallback = null)
        {
            var config = context.Config;

            InitializeBlocks(context);

            int totalSteps = RefinementRounds * config.NumLayers;
            int step = 0;

            for (int round = 0; round < RefinementRounds; round++)
            {
                var layerOutputs = ForwardWithCache(context, X);

                var residual = y;

                for (int layerIdx = config.NumLayers - 1; layerIdx >= 0; layerIdx--)
                {
                    var layerInput = layerIdx == 0
                        ? X
                        : layerOutputs[layerIdx - 1];

                    var currentOutput = layerOutputs[layerIdx];

                    var target = AddTensors(currentOutput, ScaleTensor(residual, LearningRate));

                    FitLayerWeights(context, layerIdx, layerInput, target);

                    var newOutput = ForwardLayer(context, layerIdx, layerInput);

                    residual = SubtractTensors(target, newOutput);

                    if (UseMomentum)
                    {
                        ApplyMomentum(context, layerIdx);
                    }

                    step++;
                    double currentLoss = ComputeMSE(newOutput, y);
                    context.LossHistory.Add(currentLoss);
                    progressCallback?.Invoke(step, totalSteps, currentLoss);
                }

                if (context.LossHistory.Count >= 2)
                {
                    double prevLoss = context.LossHistory[^2];
                    double currLoss = context.LossHistory[^1];
                    if (Math.Abs(currLoss - prevLoss) < config.ConvergenceThreshold)
                        break;
                }
            }

            var finalFeatures = ForwardWithCache(context, X);
            var finalOutput = finalFeatures[^1];

            FitOutputLayer(context, finalOutput, y);

            return true;
        }

        #region 前向传播与缓存

        private Tensor ForwardLayer(RuntimeContext context, int layerIdx, Tensor input)
        {
            if (context.Blocks != null && layerIdx < context.Blocks.Count)
            {
                return context.Blocks[layerIdx].Forward(input);
            }
            return input;
        }

        #endregion

        #region 权重初始化与更新


        private void FitLayerWeights(RuntimeContext context, int layerIdx, Tensor input, Tensor target)
        {
            var config = context.Config;
            int modelDim = config.ModelDim;

            var pooledInput = PoolFeatures(input);

            var pooledTarget = PoolFeatures(target);

            var XtX = ComputeXtX(pooledInput, config.RegularizationLambda);
            var Xty = ComputeXty(pooledInput, pooledTarget);
            var deltaW = SolveLinearSystem(XtX, Xty);

            if (context.Blocks != null && layerIdx < context.Blocks.Count)
            {
                var block = context.Blocks[layerIdx];
                var currentParams = block.GetParameters();
                var deltaParams = new double[currentParams.Length];

                int idx = 0;
                for (int i = 0; i < deltaW.BatchSize; i++)
                    for (int j = 0; j < deltaW.SeqLength; j++)
                        for (int k = 0; k < deltaW.Dimension; k++)
                            if (idx < deltaParams.Length)
                                deltaParams[idx++] = deltaW[i, j, k];

                for (int p = 0; p < currentParams.Length; p++)
                    currentParams[p] += deltaParams[p] * LearningRate;

                block.SetParameters(currentParams);
            }
        }

        private void FitOutputLayer(RuntimeContext context, Tensor features, Tensor y)
        {
            var config = context.Config;
            var pooled = PoolFeatures(features);

            var XtX = ComputeXtX(pooled, config.RegularizationLambda);
            var Xty = ComputeXty(pooled, y);
            var W = SolveLinearSystem(XtX, Xty);

            context.OutputWeight = W;
        }

        private void ApplyMomentum(RuntimeContext context, int layerIdx)
        {
        }

        #endregion

    }
}
