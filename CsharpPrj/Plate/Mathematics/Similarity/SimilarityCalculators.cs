using System;
using System.Collections.Generic;
using System.Linq;

namespace MathematicalAlgorithms
{
    public static class SimilarityCalculators
    {
        public static double JaccardSimilarity(HashSet<string> setA, HashSet<string> setB)
        {
            if (setA == null || setB == null || setA.Count == 0 || setB.Count == 0)
                return 0;

            var intersection = setA.Intersect(setB).Count();
            var union = setA.Union(setB).Count();

            return union > 0 ? (double)intersection / union : 0;
        }

        public static double CosineSimilarity(double[] vectorA, double[] vectorB)
        {
            if (vectorA == null || vectorB == null || vectorA.Length != vectorB.Length)
                return 0;

            double dotProduct = 0, normA = 0, normB = 0;
            for (int i = 0; i < vectorA.Length; i++)
            {
                dotProduct += vectorA[i] * vectorB[i];
                normA += vectorA[i] * vectorA[i];
                normB += vectorB[i] * vectorB[i];
            }

            double denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
            return denominator < MathConstants.EPSILON ? 0 : dotProduct / denominator;
        }

        public static double PearsonCorrelation(double[] x, double[] y)
        {
            if (x == null || y == null || x.Length != y.Length || x.Length < 2)
                return 0;

            int n = x.Length;
            double sumX = x.Sum(), sumY = y.Sum();
            double sumXY = 0, sumX2 = 0, sumY2 = 0;

            for (int i = 0; i < n; i++)
            {
                sumXY += x[i] * y[i];
                sumX2 += x[i] * x[i];
                sumY2 += y[i] * y[i];
            }

            double numerator = n * sumXY - sumX * sumY;
            double denominator = Math.Sqrt((n * sumX2 - sumX * sumX) * (n * sumY2 - sumY * sumY));

            return Math.Abs(denominator) < MathConstants.EPSILON ? 0 : numerator / denominator;
        }
    }
}
