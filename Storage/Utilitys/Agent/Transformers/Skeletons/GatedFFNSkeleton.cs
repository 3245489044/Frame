#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class GatedFFNSkeleton
    {
        public static Tensor Compute(
            Tensor input,
            Tensor wGate,
            Tensor wUp,
            Tensor wDown,
            MathematicalAlgorithms.ActivationType activation = MathematicalAlgorithms.ActivationType.Swish)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (wGate == null) throw new ArgumentNullException(nameof(wGate));
            if (wUp == null) throw new ArgumentNullException(nameof(wUp));
            if (wDown == null) throw new ArgumentNullException(nameof(wDown));

            var gate = input.MatMul(wGate);
            gate = Agent.Transformers.Core.ActivationFunctions.Apply(gate, activation);

            var hidden = input.MatMul(wUp);

            var gated = gate.Multiply(hidden);
            var output = gated.MatMul(wDown);

            return output;
        }

        public static Task<Tensor> ComputeAsync(
            Tensor input,
            Tensor wGate,
            Tensor wUp,
            Tensor wDown,
            MathematicalAlgorithms.ActivationType activation = MathematicalAlgorithms.ActivationType.Swish,
            IProgress<ComputationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ComputationProgress(0, "GatedFFN: Computing"));
                var result = Compute(input, wGate, wUp, wDown, activation);
                progress?.Report(new ComputationProgress(100, "GatedFFN: Complete"));
                return result;
            }, cancellationToken);
        }
    }
}
