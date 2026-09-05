#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public static class BatchedMatMul
    {
        private static readonly LoopEngine _engine = new();

        public static double[,] Multiply(double[,] a, double[,] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            int m = a.GetLength(0);
            int n = a.GetLength(1);
            int p = b.GetLength(1);

            if (n != b.GetLength(0))
                throw new ArgumentException(
                    $"Matrix dimensions mismatch: A[{m}×{n}] × B[{b.GetLength(0)}×{p}]",
                    nameof(b));

            var result = _engine.ExecuteSync(
                (A: a, B: b, N: n),
                (data, i, j) =>
                {
                    double sum = 0;
                    for (int k = 0; k < data.N; k++)
                        sum += data.A[i, k] * data.B[k, j];
                    return sum;
                },
                new LoopShape(m, p));

            var result2D = new double[m, p];
            for (int i = 0; i < m; i++)
                for (int j = 0; j < p; j++)
                    result2D[i, j] = result[i][j];
            return result2D;
        }

        public static async Task<double[,]> MultiplyAsync(
            double[,] a,
            double[,] b,
            CancellationToken cancellationToken = default)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            int m = a.GetLength(0);
            int n = a.GetLength(1);
            int p = b.GetLength(1);

            if (n != b.GetLength(0))
                throw new ArgumentException(
                    $"Matrix dimensions mismatch: A[{m}×{n}] × B[{b.GetLength(0)}×{p}]",
                    nameof(b));

            var result = await _engine.ExecuteAsync(
                (A: a, B: b, N: n),
                (data, i, j) =>
                {
                    double sum = 0;
                    for (int k = 0; k < data.N; k++)
                        sum += data.A[i, k] * data.B[k, j];
                    return sum;
                },
                new LoopShape(m, p),
                cancellationToken: cancellationToken);

            var result2D = new double[m, p];
            for (int i = 0; i < m; i++)
                for (int j = 0; j < p; j++)
                    result2D[i, j] = result[i][j];
            return result2D;
        }

        public static double[,,] BatchMultiply(double[,,] a, double[,,] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            int batchA = a.GetLength(0);
            int m = a.GetLength(1);
            int n = a.GetLength(2);
            int batchB = b.GetLength(0);
            int p = b.GetLength(2);

            if (n != b.GetLength(1))
                throw new ArgumentException(
                    $"Matrix dimensions mismatch: A[{batchA}×{m}×{n}] × B[{batchB}×{b.GetLength(1)}×{p}]",
                    nameof(b));

            int resultBatch = Math.Max(batchA, batchB);
            var result = new double[resultBatch, m, p];

            for (int bIdx = 0; bIdx < resultBatch; bIdx++)
            {
                int aBatch = batchA == 1 ? 0 : bIdx;
                int bBatch = batchB == 1 ? 0 : bIdx;

                var sliceA = GetSlice(a, aBatch);
                var sliceB = GetSlice(b, bBatch);
                var sliceResult = Multiply(sliceA, sliceB);

                SetSlice(result, bIdx, sliceResult);
            }

            return result;
        }

        public static async Task<double[,,]> BatchMultiplyAsync(
            double[,,] a,
            double[,,] b,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return BatchMultiply(a, b);
            }, cancellationToken);
        }

        #region 辅助方法

        private static double[,] GetSlice(double[,,] tensor, int batchIdx)
        {
            int rows = tensor.GetLength(1);
            int cols = tensor.GetLength(2);
            var slice = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    slice[i, j] = tensor[batchIdx, i, j];
            return slice;
        }

        private static void SetSlice(double[,,] tensor, int batchIdx, double[,] slice)
        {
            int rows = slice.GetLength(0);
            int cols = slice.GetLength(1);
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    tensor[batchIdx, i, j] = slice[i, j];
        }

        #endregion
    }
}
