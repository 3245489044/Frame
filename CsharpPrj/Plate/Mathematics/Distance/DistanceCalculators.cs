using System;

namespace MathematicalAlgorithms
{
    public class EuclideanDistanceCalculator : IDistanceCalculator
    {
        public DistanceMetricType MetricType => DistanceMetricType.Euclidean;

        public double Calculate(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                throw new ArgumentException("Vectors must be non-null and have same length");

            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double diff = a[i] - b[i];
                sum += diff * diff;
            }
            return Math.Sqrt(sum);
        }
    }

    public class ManhattanDistanceCalculator : IDistanceCalculator
    {
        public DistanceMetricType MetricType => DistanceMetricType.Manhattan;

        public double Calculate(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                throw new ArgumentException("Vectors must be non-null and have same length");

            double sum = 0;
            for (int i = 0; i < a.Length; i++)
                sum += Math.Abs(a[i] - b[i]);
            return sum;
        }
    }

    public class CosineDistanceCalculator : IDistanceCalculator
    {
        public DistanceMetricType MetricType => DistanceMetricType.Cosine;

        public double Calculate(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                throw new ArgumentException("Vectors must be non-null and have same length");

            double dotProduct = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            double denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
            return denominator < MathConstants.EPSILON ? 1.0 : 1.0 - dotProduct / denominator;
        }
    }


    public static class DistanceCalculatorFactory
    {
        public static IDistanceCalculator Create(DistanceMetricType metric)
        {
            return metric switch
            {
                DistanceMetricType.Euclidean => new EuclideanDistanceCalculator(),
                DistanceMetricType.Manhattan => new ManhattanDistanceCalculator(),
                DistanceMetricType.Cosine => new CosineDistanceCalculator(),
                _ => new EuclideanDistanceCalculator()
            };
        }
    }
}
