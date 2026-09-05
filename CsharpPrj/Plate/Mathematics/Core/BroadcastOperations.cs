#region USINGS
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public static class BroadcastOperations
    {
        private static readonly LoopEngine _engine = new();

        #region 广播二元运算（同步）

        public static double[,] Add(double[,] a, double[,] b)
        {
            return BroadcastBinaryOp(a, b, (x, y) => x + y);
        }

        public static double[,] Subtract(double[,] a, double[,] b)
        {
            return BroadcastBinaryOp(a, b, (x, y) => x - y);
        }

        public static double[,] Multiply(double[,] a, double[,] b)
        {
            return BroadcastBinaryOp(a, b, (x, y) => x * y);
        }

        public static double[,] Divide(double[,] a, double[,] b)
        {
            return BroadcastBinaryOp(a, b, (x, y) => x / y);
        }

        public static double[,] BroadcastBinaryOp(
            double[,] a,
            double[,] b,
            Func<double, double, double> op)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (op == null) throw new ArgumentNullException(nameof(op));

            int rowsA = a.GetLength(0), colsA = a.GetLength(1);
            int rowsB = b.GetLength(0), colsB = b.GetLength(1);

            int resultRows = Math.Max(rowsA, rowsB);
            int resultCols = Math.Max(colsA, colsB);

            if ((rowsA != rowsB && rowsA != 1 && rowsB != 1) ||
                (colsA != colsB && colsA != 1 && colsB != 1))
            {
                throw new InvalidOperationException(
                    $"Shapes [{rowsA}×{colsA}] and [{rowsB}×{colsB}] cannot be broadcast together");
            }

            var result = _engine.ExecuteSync(
                (A: a, B: b, Op: op, RowsA: rowsA, ColsA: colsA, RowsB: rowsB, ColsB: colsB),
                (data, i, j) =>
                {
                    int srcI = data.RowsA == 1 ? 0 : i;
                    int srcJ = data.ColsA == 1 ? 0 : j;
                    double valA = data.A[srcI, srcJ];

                    srcI = data.RowsB == 1 ? 0 : i;
                    srcJ = data.ColsB == 1 ? 0 : j;
                    double valB = data.B[srcI, srcJ];

                    return data.Op(valA, valB);
                },
                new LoopShape(resultRows, resultCols));

            var result2D = new double[resultRows, resultCols];
            for (int i = 0; i < resultRows; i++)
                for (int j = 0; j < resultCols; j++)
                    result2D[i, j] = result[i][j];
            return result2D;
        }

        #endregion

        #region 广播二元运算（异步）

        public static async Task<double[,]> AddAsync(
            double[,] a,
            double[,] b,
            CancellationToken cancellationToken = default)
        {
            return await BroadcastBinaryOpAsync(a, b, (x, y) => x + y, cancellationToken);
        }

        public static async Task<double[,]> SubtractAsync(
            double[,] a,
            double[,] b,
            CancellationToken cancellationToken = default)
        {
            return await BroadcastBinaryOpAsync(a, b, (x, y) => x - y, cancellationToken);
        }

        public static async Task<double[,]> MultiplyAsync(
            double[,] a,
            double[,] b,
            CancellationToken cancellationToken = default)
        {
            return await BroadcastBinaryOpAsync(a, b, (x, y) => x * y, cancellationToken);
        }

        public static async Task<double[,]> DivideAsync(
            double[,] a,
            double[,] b,
            CancellationToken cancellationToken = default)
        {
            return await BroadcastBinaryOpAsync(a, b, (x, y) => x / y, cancellationToken);
        }

        public static async Task<double[,]> BroadcastBinaryOpAsync(
            double[,] a,
            double[,] b,
            Func<double, double, double> op,
            CancellationToken cancellationToken = default)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (op == null) throw new ArgumentNullException(nameof(op));

            int rowsA = a.GetLength(0), colsA = a.GetLength(1);
            int rowsB = b.GetLength(0), colsB = b.GetLength(1);

            int resultRows = Math.Max(rowsA, rowsB);
            int resultCols = Math.Max(colsA, colsB);

            if ((rowsA != rowsB && rowsA != 1 && rowsB != 1) ||
                (colsA != colsB && colsA != 1 && colsB != 1))
            {
                throw new InvalidOperationException(
                    $"Shapes [{rowsA}×{colsA}] and [{rowsB}×{colsB}] cannot be broadcast together");
            }

            var result = await _engine.ExecuteAsync(
                (A: a, B: b, Op: op, RowsA: rowsA, ColsA: colsA, RowsB: rowsB, ColsB: colsB),
                (data, i, j) =>
                {
                    int srcI = data.RowsA == 1 ? 0 : i;
                    int srcJ = data.ColsA == 1 ? 0 : j;
                    double valA = data.A[srcI, srcJ];

                    srcI = data.RowsB == 1 ? 0 : i;
                    srcJ = data.ColsB == 1 ? 0 : j;
                    double valB = data.B[srcI, srcJ];

                    return data.Op(valA, valB);
                },
                new LoopShape(resultRows, resultCols),
                cancellationToken: cancellationToken);

            var result2D = new double[resultRows, resultCols];
            for (int i = 0; i < resultRows; i++)
                for (int j = 0; j < resultCols; j++)
                    result2D[i, j] = result[i][j];
            return result2D;
        }

        #endregion
    }
}
