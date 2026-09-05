#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class ResidualSkeleton
    {
        public static Tensor Apply(
            Tensor input,
            Func<Tensor, Tensor> sublayer,
            double[] gamma,
            double[] beta,
            bool preNorm = true,
            double eps = 1e-5)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (sublayer == null) throw new ArgumentNullException(nameof(sublayer));
            if (gamma == null) throw new ArgumentNullException(nameof(gamma));
            if (beta == null) throw new ArgumentNullException(nameof(beta));

            if (preNorm)
            {
                var normalized = NormalizationSkeleton.LayerNorm(input, gamma, beta, eps);
                return input.Add(sublayer(normalized));
            }
            else
            {
                var output = input.Add(sublayer(input));
                return NormalizationSkeleton.LayerNorm(output, gamma, beta, eps);
            }
        }

        public static Task<Tensor> ApplyAsync(
            Tensor input,
            Func<Tensor, Tensor> sublayer,
            double[] gamma,
            double[] beta,
            bool preNorm = true,
            double eps = 1e-5,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Apply(input, sublayer, gamma, beta, preNorm, eps);
            }, cancellationToken);
        }
    }
}
