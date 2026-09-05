#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace MathematicalAlgorithms
{
    public static class DistanceCalculator
    {
        public static double EuclideanDistance(double[] point1, double[] point2)
        {
            ValidateArrays(point1, point2);
            return MathematicalAlgorithms.VectorMath.EuclideanDistance(point1, point2);
        }

        public static double ManhattanDistance(double[] point1, double[] point2)
        {
            ValidateArrays(point1, point2);
            return MathematicalAlgorithms.VectorMath.ManhattanDistance(point1, point2);
        }

        public static double CosineSimilarity(double[] vector1, double[] vector2)
        {
            ValidateArrays(vector1, vector2);

            double dotProduct = 0;
            double norm1 = 0;
            double norm2 = 0;

            for (int i = 0; i < vector1.Length; i++)
            {
                dotProduct += vector1[i] * vector2[i];
                norm1 += vector1[i] * vector1[i];
                norm2 += vector2[i] * vector2[i];
            }

            norm1 = Math.Sqrt(norm1);
            norm2 = Math.Sqrt(norm2);

            if (norm1 < MathConstants.EPSILON || norm2 < MathConstants.EPSILON)
                return 0;

            return dotProduct / (norm1 * norm2);
        }

        public static double CosineDistance(double[] vector1, double[] vector2)
        {
            return 1.0 - CosineSimilarity(vector1, vector2);
        }

        public static double JaccardSimilarity<T>(HashSet<T> set1, HashSet<T> set2)
        {
            if (set1 == null || set2 == null)
                throw new ArgumentNullException("Input sets cannot be null");

            if (set1.Count == 0 && set2.Count == 0)
                return 1.0;

            var intersection = set1.Intersect(set2).Count();
            var union = set1.Union(set2).Count();

            return union == 0 ? 0 : (double)intersection / union;
        }

        public static double JaccardDistance<T>(HashSet<T> set1, HashSet<T> set2)
        {
            return 1.0 - JaccardSimilarity(set1, set2);
        }

        public static int HammingDistance(string str1, string str2)
        {
            if (str1 == null || str2 == null)
                throw new ArgumentNullException("Input strings cannot be null");

            if (str1.Length != str2.Length)
                throw new ArgumentException("Input strings must have the same length");

            int distance = 0;
            for (int i = 0; i < str1.Length; i++)
            {
                if (str1[i] != str2[i])
                    distance++;
            }

            return distance;
        }

        public static double ChebyshevDistance(double[] point1, double[] point2)
        {
            ValidateArrays(point1, point2);

            double max = 0;
            for (int i = 0; i < point1.Length; i++)
                max = Math.Max(max, Math.Abs(point1[i] - point2[i]));
            return max;
        }

        public static double MinkowskiDistance(double[] point1, double[] point2, double p)
        {
            ValidateArrays(point1, point2);

            if (p == 0)
                throw new ArgumentException("Minkowski distance order p cannot be 0", nameof(p));

            double sum = 0;
            for (int i = 0; i < point1.Length; i++)
                sum += Math.Pow(Math.Abs(point1[i] - point2[i]), p);
            return Math.Pow(sum, 1.0 / p);
        }

        private static void ValidateArrays(double[] array1, double[] array2)
        {
            if (array1 == null || array2 == null)
                throw new ArgumentNullException("Input arrays cannot be null");

            if (array1.Length != array2.Length)
                throw new ArgumentException("Input arrays must have the same length");
        }
    }
}
