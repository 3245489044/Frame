#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public static class NormalizationOperations
    {
        private static readonly LoopEngine _engine = new();

        public static double[,] LayerNorm(
            double[,] input,
            double[] gamma,
            double[] beta,
            double eps = 1e-5)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (gamma == null) throw new ArgumentNullException(nameof(gamma));
            if (beta == null) throw new ArgumentNullException(nameof(beta));

            int rows = input.GetLength(0);
            int cols = input.GetLength(1);

            if (gamma.Length != cols)
                throw new ArgumentException($"gamma length ({gamma.Length}) must equal cols ({cols})", nameof(gamma));
            if (beta.Length != cols)
                throw new ArgumentException($"beta length ({beta.Length}) must equal cols ({cols})", nameof(beta));

            var result = _engine.ExecuteSync(
                (Input: input, Gamma: gamma, Beta: beta, Eps: eps, Cols: cols),
                (data, i, j) =>
                {
                    double sum = 0, sumSq = 0;
                    for (int k = 0; k < data.Cols; k++)
                    {
                        double val = data.Input[i, k];
                        sum += val;
                        sumSq += val * val;
                    }
                    double mean = sum / data.Cols;
                    double var = sumSq / data.Cols - mean * mean;

                    double normalized = (data.Input[i, j] - mean) / Math.Sqrt(var + data.Eps);
                    return data.Gamma[j] * normalized + data.Beta[j];
                },
                new LoopShape(rows, cols));

            var result2D = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result2D[i, j] = result[i][j];
            return result2D;
        }

        public static async Task<double[,]> LayerNormAsync(
            double[,] input,
            double[] gamma,
            double[] beta,
            double eps = 1e-5,
            CancellationToken cancellationToken = default)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (gamma == null) throw new ArgumentNullException(nameof(gamma));
            if (beta == null) throw new ArgumentNullException(nameof(beta));

            int rows = input.GetLength(0);
            int cols = input.GetLength(1);

            if (gamma.Length != cols)
                throw new ArgumentException($"gamma length ({gamma.Length}) must equal cols ({cols})", nameof(gamma));
            if (beta.Length != cols)
                throw new ArgumentException($"beta length ({beta.Length}) must equal cols ({cols})", nameof(beta));

            var result = await _engine.ExecuteAsync(
                (Input: input, Gamma: gamma, Beta: beta, Eps: eps, Cols: cols),
                (data, i, j) =>
                {
                    double sum = 0, sumSq = 0;
                    for (int k = 0; k < data.Cols; k++)
                    {
                        double val = data.Input[i, k];
                        sum += val;
                        sumSq += val * val;
                    }
                    double mean = sum / data.Cols;
                    double var = sumSq / data.Cols - mean * mean;

                    double normalized = (data.Input[i, j] - mean) / Math.Sqrt(var + data.Eps);
                    return data.Gamma[j] * normalized + data.Beta[j];
                },
                new LoopShape(rows, cols),
                cancellationToken: cancellationToken);

            var result2D = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result2D[i, j] = result[i][j];
            return result2D;
        }

        public static double[,] RMSNorm(
            double[,] input,
            double[] gamma,
            double eps = 1e-5)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (gamma == null) throw new ArgumentNullException(nameof(gamma));

            int rows = input.GetLength(0);
            int cols = input.GetLength(1);

            if (gamma.Length != cols)
                throw new ArgumentException($"gamma length ({gamma.Length}) must equal cols ({cols})", nameof(gamma));

            var result = _engine.ExecuteSync(
                (Input: input, Gamma: gamma, Eps: eps, Cols: cols),
                (data, i, j) =>
                {
                    double sumSq = 0;
                    for (int k = 0; k < data.Cols; k++)
                    {
                        double val = data.Input[i, k];
                        sumSq += val * val;
                    }
                    double rms = Math.Sqrt(sumSq / data.Cols + data.Eps);

                    return data.Gamma[j] * (data.Input[i, j] / rms);
                },
                new LoopShape(rows, cols));

            var result2D = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result2D[i, j] = result[i][j];
            return result2D;
        }

        public static async Task<double[,]> RMSNormAsync(
            double[,] input,
            double[] gamma,
            double eps = 1e-5,
            CancellationToken cancellationToken = default)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (gamma == null) throw new ArgumentNullException(nameof(gamma));

            int rows = input.GetLength(0);
            int cols = input.GetLength(1);

            if (gamma.Length != cols)
                throw new ArgumentException($"gamma length ({gamma.Length}) must equal cols ({cols})", nameof(gamma));

            var result = await _engine.ExecuteAsync(
                (Input: input, Gamma: gamma, Eps: eps, Cols: cols),
                (data, i, j) =>
                {
                    double sumSq = 0;
                    for (int k = 0; k < data.Cols; k++)
                    {
                        double val = data.Input[i, k];
                        sumSq += val * val;
                    }
                    double rms = Math.Sqrt(sumSq / data.Cols + data.Eps);
                    return data.Gamma[j] * (data.Input[i, j] / rms);
                },
                new LoopShape(rows, cols),
                cancellationToken: cancellationToken);

            var result2D = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result2D[i, j] = result[i][j];
            return result2D;
        }
    }
}
