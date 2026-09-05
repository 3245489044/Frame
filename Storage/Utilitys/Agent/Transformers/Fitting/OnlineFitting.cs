#region USINGS
using System;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public class OnlineFitting : BaseFittingStrategy
    {
        public override string Name => "Online";
        public override FittingType FittingType => FittingType.Online;

        private double _learningRate = 0.01;


        protected override bool FitWeights(
            RuntimeContext context,
            Tensor X,
            Tensor y,
            Action<int, int, double>? progressCallback = null)
        {
            var config = context.Config;

            if (context.OutputWeight == null)
            {
                int modelDim = config.ModelDim;
                int outputDim = context.OutputDim;
                context.OutputWeight = new Tensor(new TensorShape3D(1, modelDim, outputDim));
            }

            int numSamples = X.BatchSize;
            int maxIter = config.MaxIterations;

            for (int iter = 0; iter < maxIter; iter++)
            {
                double totalLoss = 0;

                for (int i = 0; i < numSamples; i++)
                {
                    var sampleX = new Tensor(new TensorShape3D(1, X.SeqLength, X.Dimension));
                    for (int s = 0; s < X.SeqLength; s++)
                        for (int d = 0; d < X.Dimension; d++)
                            sampleX[0, s, d] = X[i, s, d];

                    var features = ExtractFeatures(context, sampleX);

                    var prediction = features.MatMul(context.OutputWeight!);

                    double[] error = new double[context.OutputDim];
                    for (int j = 0; j < context.OutputDim; j++)
                    {
                        error[j] = prediction[0, 0, j] - y[i, 0, j];
                        totalLoss += error[j] * error[j];
                    }

                    for (int r = 0; r < config.ModelDim; r++)
                    {
                        for (int c = 0; c < context.OutputDim; c++)
                        {
                            double grad = features[0, 0, r] * error[c];
                            context.OutputWeight[0, r, c] -= _learningRate * grad;
                        }
                    }
                }

                double mse = totalLoss / (numSamples * context.OutputDim);
                context.LossHistory.Add(mse);
                progressCallback?.Invoke(iter, maxIter, mse);

                _learningRate *= 0.99;

                if (iter > 0 && Math.Abs(mse - context.LossHistory[^2]) < config.ConvergenceThreshold)
                    break;
            }

            context.FinalLoss = context.LossHistory[^1];

            return true;
        }
    }
}
