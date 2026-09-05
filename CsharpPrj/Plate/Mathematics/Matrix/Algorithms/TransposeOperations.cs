#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public static class TransposeOperations
    {
        private static readonly LoopEngine _engine = new();

        public static double[,] Transpose(double[,] matrix)
        {
            if (matrix == null) throw new ArgumentNullException(nameof(matrix));

            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);

            if (rows == 0 || cols == 0)
                return new double[0, 0];

            var result = _engine.ExecuteSync(
                matrix,
                (m, i, j) => m[j, i],
                new LoopShape(cols, rows));

            var result2D = new double[cols, rows];
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                    result2D[i, j] = result[i][j];
            return result2D;
        }

        public static async Task<double[,]> TransposeAsync(
            double[,] matrix,
            CancellationToken cancellationToken = default)
        {
            if (matrix == null) throw new ArgumentNullException(nameof(matrix));

            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);

            if (rows == 0 || cols == 0)
                return new double[0, 0];

            var result = await _engine.ExecuteAsync(
                matrix,
                (m, i, j) => m[j, i],
                new LoopShape(cols, rows),
                cancellationToken: cancellationToken);

            var result2D = new double[cols, rows];
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                    result2D[i, j] = result[i][j];
            return result2D;
        }

        public static void TransposeInPlace(double[,] matrix)
        {
            if (matrix == null) throw new ArgumentNullException(nameof(matrix));

            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);

            if (rows != cols)
                throw new InvalidOperationException(
                    $"In-place transpose requires a square matrix, got [{rows}×{cols}]");

            for (int i = 0; i < rows; i++)
            {
                for (int j = i + 1; j < cols; j++)
                {
                    (matrix[i, j], matrix[j, i]) = (matrix[j, i], matrix[i, j]);
                }
            }
        }
    }
}
