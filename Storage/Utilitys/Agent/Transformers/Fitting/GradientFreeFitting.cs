#region USINGS
using System;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public class GradientFreeFitting : BaseFittingStrategy
    {
        public override string Name => "GradientFree";
        public override FittingType FittingType => FittingType.GradientFree;

        private readonly Random _rng = new(42);

        protected override bool FitWeights(
            RuntimeContext context,
            Tensor X,
            Tensor y,
            Action<int, int, double>? progressCallback = null)
        {
            var config = context.Config;

            int numParams = context.OutputDim * config.ModelDim;
            var initialParams = new double[numParams];
            for (int i = 0; i < numParams; i++)
                initialParams[i] = (_rng.NextDouble() - 0.5) * 0.1;

            var optimizer = new NelderMeadOptimizer(
                maxIterations: config.MaxIterations,
                convergenceThreshold: config.ConvergenceThreshold);

            double bestLoss = double.MaxValue;
            double[] bestParams = initialParams;

            optimizer.Optimize(
                initialParams,
                (p) =>
                {
                    SetOutputWeight(context, p);
                    return ComputeLoss(context, X, y);
                },
                (iteration, loss) =>
                {
                    context.LossHistory.Add(loss);
                    progressCallback?.Invoke(iteration, config.MaxIterations, loss);

                    if (loss < bestLoss)
                    {
                        bestLoss = loss;
                        bestParams = (double[])initialParams.Clone();
                    }
                });

            SetOutputWeight(context, bestParams);
            context.FinalLoss = bestLoss;

            return true;
        }

        private void SetOutputWeight(RuntimeContext context, double[] parameters)
        {
            var config = context.Config;
            int modelDim = config.ModelDim;
            int outputDim = context.OutputDim;

            var W = new Tensor(new TensorShape3D(1, modelDim, outputDim));
            int idx = 0;
            for (int i = 0; i < modelDim; i++)
                for (int j = 0; j < outputDim; j++)
                    W[0, i, j] = parameters[idx++];

            context.OutputWeight = W;
        }
    }
}
