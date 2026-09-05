#region USINGS
using System;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public class LeastSquaresFitting : BaseFittingStrategy
    {
        public override string Name => "LeastSquares";
        public override FittingType FittingType => FittingType.LeastSquares;

        protected override bool FitWeights(
            RuntimeContext context,
            Tensor X,
            Tensor y,
            Action<int, int, double>? progressCallback = null)
        {
            var config = context.Config;

            progressCallback?.Invoke(0, 1, 0);
            var features = ExtractFeatures(context, X);

            var XtX = ComputeXtX(features, config.RegularizationLambda);
            var Xty = ComputeXty(features, y);
            var W = SolveLinearSystem(XtX, Xty);

            context.OutputWeight = W;

            progressCallback?.Invoke(1, 1, context.FinalLoss);
            return true;
        }
    }
}
