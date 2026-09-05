#region USINGS
using System;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
#endregion

namespace MathematicalAlgorithms
{
    public static class MatrixDecomposition
    {
        private static readonly LoopEngine _engine = new();

        public static (double[][] U, double[] S, double[][] Vt) SVD(double[][] matrix)
        {
            var rows = matrix.Length;
            var cols = matrix[0].Length;
            var denseMatrix = Matrix<double>.Build.Dense(rows, cols, (i, j) => matrix[i][j]);

            var svd = denseMatrix.Svd();
            var u = svd.U.ToRowArrays();
            var s = svd.S.ToArray();
            var vt = svd.VT.ToRowArrays();

            return (u, s, vt);
        }

        public static double[][] ReduceDimension(double[][] matrix, int targetDimensions)
        {
            var (u, s, vt) = SVD(matrix);
            var reducedU = u.Select(row => row.Take(targetDimensions).ToArray()).ToArray();
            var reducedS = s.Take(targetDimensions).ToArray();

            return _engine.ExecuteSync(
                (U: reducedU, S: reducedS),
                (input, i, j) => input.U[i][j] * input.S[j],
                new LoopShape(reducedU.Length, targetDimensions));
        }

        public static double[][] Transpose(double[][] matrix)
        {
            if (matrix == null || matrix.Length == 0) return Array.Empty<double[]>();

            int rows = matrix.Length, cols = matrix[0].Length;

            return _engine.ExecuteSync(
                matrix,
                (m, i, j) => m[j][i],
                new LoopShape(cols, rows));
        }

        public static double[][] Multiply(double[][] a, double[][] b)
        {
            if (a == null || b == null || a[0].Length != b.Length)
                throw new ArgumentException("Invalid matrix dimensions for multiplication");

            int m = a.Length, n = a[0].Length, p = b[0].Length;

            return _engine.ExecuteSync(
                (A: a, B: b),
                (input, i, j) =>
                {
                    double sum = 0;
                    for (int k = 0; k < n; k++)
                        sum += input.A[i][k] * input.B[k][j];
                    return sum;
                },
                new LoopShape(m, p));
        }
    }
}
