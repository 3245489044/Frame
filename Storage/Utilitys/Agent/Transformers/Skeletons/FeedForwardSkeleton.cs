#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class FeedForwardSkeleton
    {
        public static Tensor Compute(
            Tensor input,
            Tensor weight1, Tensor bias1,
            Tensor weight2, Tensor bias2,
            Core.ActivationType activation = Core.ActivationType.GELU,
            double dropoutRate = 0.0,
            IProgress<ComputationProgress> progress = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (weight1 == null) throw new ArgumentNullException(nameof(weight1));
            if (bias1 == null) throw new ArgumentNullException(nameof(bias1));
            if (weight2 == null) throw new ArgumentNullException(nameof(weight2));
            if (bias2 == null) throw new ArgumentNullException(nameof(bias2));

            progress?.Report(new ComputationProgress(0, "FFN: Step 1/4 - Linear 1"));

            var hidden = Linear(input, weight1, bias1);

            progress?.Report(new ComputationProgress(25, "FFN: Step 2/4 - Activation"));

            hidden = Core.ActivationFunctions.Apply(hidden, (MathematicalAlgorithms.ActivationType)activation);

            progress?.Report(new ComputationProgress(50, "FFN: Step 3/4 - Linear 2"));

            var output = Linear(hidden, weight2, bias2);

            progress?.Report(new ComputationProgress(75, "FFN: Step 4/4 - Dropout"));

            if (dropoutRate > 0)
            {
            }

            progress?.Report(new ComputationProgress(100, "FFN: Complete"));

            return output;
        }

        private static Tensor Linear(Tensor input, Tensor weight, Tensor bias)
        {

            var output = input.MatMul(weight);
            return output.Add(bias);
        }

        public static Task<Tensor> ComputeAsync(
            Tensor input,
            Tensor weight1, Tensor bias1,
            Tensor weight2, Tensor bias2,
            Core.ActivationType activation = Core.ActivationType.GELU,
            double dropoutRate = 0.0,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Compute(input, weight1, bias1, weight2, bias2,
                    activation, dropoutRate, progress);
            }, cancellationToken);
        }
    }
}
