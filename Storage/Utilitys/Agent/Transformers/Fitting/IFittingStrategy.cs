#region USINGS
using System;
using System.Collections.Generic;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Runtime
{
    public interface IFittingStrategy
    {
        string Name { get; }

        FittingType FittingType { get; }

        bool Fit(
            RuntimeContext context,
            Tensor X,
            Tensor y,
            Action<int, int, double>? progressCallback = null);

        Tensor Predict(RuntimeContext context, Tensor X);

        double ComputeLoss(RuntimeContext context, Tensor X, Tensor y);

        (Tensor output, bool converged) Step(
            RuntimeContext context,
            Tensor X,
            Tensor? y = null,
            double learningRate = 0,
            IUpdateOperator? updater = null);
    }
}


