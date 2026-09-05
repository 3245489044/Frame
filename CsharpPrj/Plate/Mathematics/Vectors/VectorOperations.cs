#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace MathematicalAlgorithms
{
    public static class VectorOperations
    {
        public static double[] CalculateCentroid(IEnumerable<double[]> vectors)
        {
            var list = vectors?.ToList() ?? new List<double[]>();
            if (list.Count == 0) return Array.Empty<double>();

            int dim = list[0].Length;
            var centroid = new double[dim];

            foreach (var v in list)
                for (int i = 0; i < dim; i++)
                    centroid[i] += v[i];

            for (int i = 0; i < dim; i++)
                centroid[i] /= list.Count;

            return centroid;
        }

        public static List<double[]> StandardizeFeatures(List<double[]> features)
        {
            if (features?.Count == 0) return features ?? new List<double[]>();

            int dim = features[0].Length;
            var means = new double[dim];
            var stdDevs = new double[dim];

            foreach (var f in features)
                for (int i = 0; i < dim; i++)
                    means[i] += f[i];

            for (int i = 0; i < dim; i++)
                means[i] /= features.Count;

            var mathUtil = new MathUtility();
            for (int i = 0; i < dim; i++)
                stdDevs[i] = mathUtil.StandardDeviation(features.Select(f => f[i]).ToList());

            return features.Select(f =>
            {
                var result = new double[dim];
                for (int i = 0; i < dim; i++)
                    result[i] = stdDevs[i] > MathConstants.EPSILON ? (f[i] - means[i]) / stdDevs[i] : 0;
                return result;
            }).ToList();
        }

        public static double DotProduct(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 0;
            double result = 0;
            for (int i = 0; i < a.Length; i++)
                result += a[i] * b[i];
            return result;
        }

        public static double Norm(double[] vector)
        {
            if (vector == null || vector.Length == 0) return 0;
            return Math.Sqrt(vector.Sum(v => v * v));
        }
    }

    public static class MatrixOperations
    {
        private static readonly LoopEngine _loopEngine = new();

        public static MathNet.Numerics.LinearAlgebra.Matrix<double> ConvertToMatrix(List<double[]> vectors)
        {
            int rows = vectors.Count, cols = vectors[0].Length;
            var matrix = MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.Dense(rows, cols);

            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    matrix[i, j] = vectors[i][j];

            return matrix;
        }

        public static List<double[]> ConvertFromMatrix(MathNet.Numerics.LinearAlgebra.Matrix<double> matrix)
        {
            int rows = matrix.RowCount, cols = matrix.ColumnCount;

            var result = _loopEngine.ExecuteSync(
                matrix,
                (m, i, j) => m[i, j],
                new LoopShape(rows, cols),
                LoopConfig.Sequential);

            return result.Select(row => row.ToArray()).ToList();
        }
    }
}
