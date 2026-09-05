#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
using MathematicalAlgorithms;
using Agent.Transformers.Core;
#endregion

namespace Agent.Transformers.Skeletons
{
    public static class NormalizationSkeleton
    {
        public static Tensor LayerNorm(
            Tensor input,
            double[] gamma,
            double[] beta,
            double eps = 1e-5)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return input.LayerNorm(gamma, beta, eps);
        }

        public static Tensor RMSNorm(
            Tensor input,
            double[] gamma,
            double eps = 1e-5)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return input.RMSNorm(gamma, eps);
        }


        public static Task<Tensor> LayerNormAsync(
            Tensor input,
            double[] gamma,
            double[] beta,
            double eps = 1e-5,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return LayerNorm(input, gamma, beta, eps);
            }, cancellationToken);
        }

        public static Task<Tensor> RMSNormAsync(
            Tensor input,
            double[] gamma,
            double eps = 1e-5,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return RMSNorm(input, gamma, eps);
            }, cancellationToken);
        }

        #region 初始化工厂方法

        public static double[] CreateGamma(int dim)
        {
            if (dim <= 0)
                throw new ArgumentException("dim must be positive", nameof(dim));
            var gamma = new double[dim];
            Array.Fill(gamma, 1.0);
            return gamma;
        }

        public static double[] CreateBeta(int dim)
        {
            if (dim <= 0)
                throw new ArgumentException("dim must be positive", nameof(dim));
            return new double[dim];
        }

        public static (double[] Gamma, double[] Beta) CreateLayerNormParams(int dim)
        {
            return (CreateGamma(dim), CreateBeta(dim));
        }

        public static double[] CreateRMSNormGamma(int dim)
        {
            return CreateGamma(dim);
        }

        #endregion

        #region 3D↔2D 转换（复用 Tensor 公共方法）

        private static double[,] To2D(Tensor input) => input.To2D();

        private static Tensor From2D(double[,] data2D, int batchSize, int seqLen)
            => Tensor.From2D(data2D, batchSize, seqLen);

        #endregion
    }
}
