#region USINGS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#endregion

namespace MathematicalAlgorithms
{
    public static class MatrixAlgorithmBuilder
    {
        public static IMatrixAlgorithm<TInput> Create<TInput>(
            Func<TInput, MatrixShape> shapeProvider,
            Func<TInput, int, int, double> cellFunction,
            string algorithmName = null)
        {
            return new FunctionalMatrixAlgorithm<TInput>(shapeProvider, cellFunction, algorithmName);
        }

        public static IMatrixAlgorithm<TInput> CreateBlocked<TInput>(
            IMatrixAlgorithm<TInput> baseAlgorithm,
            int blockSize = 1000)
        {
            return new BlockMatrixAlgorithm<TInput>(baseAlgorithm, blockSize);
        }

        public static IMatrixAlgorithm<(int Rows, int Cols)> CreateRandomMatrix(Random random = null)
        {
            random ??= new Random();
            return Create<(int Rows, int Cols)>(
                input => new MatrixShape(input.Rows, input.Cols),
                (input, i, j) => random.NextDouble(),
                "RandomMatrix");
        }

        public static IMatrixAlgorithm<int> CreateIdentityMatrix()
        {
            return Create<int>(
                size => new MatrixShape(size, size),
                (size, i, j) => i == j ? 1.0 : 0.0,
                "IdentityMatrix");
        }

        public static IMatrixAlgorithm<(int Size, double Scale)> CreateDistanceMatrix()
        {
            return Create<(int Size, double Scale)>(
                input => new MatrixShape(input.Size, input.Size),
                (input, i, j) => Math.Exp(-Math.Pow(i - j, 2) / input.Scale),
                "DistanceMatrix");
        }

        public static IMatrixAlgorithm<(int Rows, int Cols)> CreateOnesMatrix()
        {
            return Create<(int Rows, int Cols)>(
                input => new MatrixShape(input.Rows, input.Cols),
                (input, i, j) => 1.0,
                "OnesMatrix");
        }
    }

    public static class MatrixExtensions
    {
        public static MathNet.Numerics.LinearAlgebra.Matrix<double> ToMathNetMatrix(this double[,] array) =>
            MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.DenseOfArray(array);

        public static double[,] ToArray(this MathNet.Numerics.LinearAlgebra.Matrix<double> matrix) => matrix.ToArray();

        public static MathNet.Numerics.LinearAlgebra.Matrix<double> MapInPlace(
            this MathNet.Numerics.LinearAlgebra.Matrix<double> matrix,
            Func<double, double> transform)
        {
            matrix.MapInplace(transform, MathNet.Numerics.LinearAlgebra.Zeros.Include);
            return matrix;
        }

        public static string ToFormattedString(
            this MathNet.Numerics.LinearAlgebra.Matrix<double> matrix,
            int maxRows = 10,
            int maxCols = 10)
        {
            var rows = Math.Min(matrix.RowCount, maxRows);
            var cols = Math.Min(matrix.ColumnCount, maxCols);

            using var writer = new StringWriter();
            writer.WriteLine($"Matrix [{matrix.RowCount}x{matrix.ColumnCount}]:");

            for (int i = 0; i < rows; i++)
            {
                var values = new List<string>();
                for (int j = 0; j < cols; j++)
                {
                    values.Add(matrix[i, j].ToString("F2"));
                }
                writer.WriteLine($"  [{string.Join(", ", values)}]");
            }

            if (rows < matrix.RowCount || cols < matrix.ColumnCount)
            {
                writer.WriteLine($"  ... (showing {rows}x{cols} of {matrix.RowCount}x{matrix.ColumnCount})");
            }

            return writer.ToString();
        }
    }
}
