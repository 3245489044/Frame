#region USINGS
using System;
#endregion

namespace MathematicalAlgorithms
{
    internal static class ClusteringValidator
    {
        public static void ValidateData(double[][] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Data cannot be null or empty");
        }

        public static void ValidateClusterCount(int count, int dataLength, string paramName = "k")
        {
            if (count <= 0 || count > dataLength)
                throw new ArgumentException($"{paramName} must be between 1 and the number of data points");
        }

        public static void ValidateK(int k, int dataLength)
            => ValidateClusterCount(k, dataLength, "k");

        public static void ValidateNClusters(int nClusters, int dataLength)
            => ValidateClusterCount(nClusters, dataLength, "nClusters");

        public static void ValidateNComponents(int nComponents, int dataLength)
            => ValidateClusterCount(nComponents, dataLength, "nComponents");

        public static void ValidateMaxClusters(int maxClusters)
        {
            if (maxClusters < 2)
                throw new ArgumentException("maxClusters must be at least 2");
        }

        public static void ValidateItems<T>(System.Collections.Generic.IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException("Items collection cannot be null or empty");
        }

        public static void ValidateEpsilonAndMinPoints(double epsilon, int minPoints)
        {
            if (epsilon <= 0)
                throw new ArgumentException("epsilon must be positive");
            if (minPoints < 1)
                throw new ArgumentException("minPoints must be at least 1");
        }
    }
}
