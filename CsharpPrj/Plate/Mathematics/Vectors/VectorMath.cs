using System;

namespace MathematicalAlgorithms
{
    public static class VectorMath
    {
        private static readonly MathUtility _mathUtility = new();

        public static double EuclideanDistance(double[] a, double[] b)
            => DistanceCalculatorFactory.Create(DistanceMetricType.Euclidean).Calculate(a, b);

        public static double ManhattanDistance(double[] a, double[] b)
            => DistanceCalculatorFactory.Create(DistanceMetricType.Manhattan).Calculate(a, b);

        public static double CosineDistance(double[] a, double[] b)
            => DistanceCalculatorFactory.Create(DistanceMetricType.Cosine).Calculate(a, b);

        public static double[] Normalize(double[] vector)
        {
            if (vector == null || vector.Length == 0)
                return Array.Empty<double>();

            var magnitude = EuclideanDistance(vector, new double[vector.Length]);
            if (magnitude < MathConstants.EPSILON)
                return vector;

            var normalized = new double[vector.Length];
            for (int i = 0; i < vector.Length; i++)
                normalized[i] = vector[i] / magnitude;

            return normalized;
        }

        public static double DotProduct(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                throw new ArgumentException("Vectors must be non-null and have same length");

            double result = 0;
            for (int i = 0; i < a.Length; i++)
                result += a[i] * b[i];

            return result;
        }
    }
}
