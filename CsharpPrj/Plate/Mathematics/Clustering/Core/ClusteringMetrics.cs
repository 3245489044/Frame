#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace MathematicalAlgorithms
{
    public static class ClusteringMetrics
    {
        public static double CalculateDistortion<T>(
            List<double[]> features,
            List<Cluster<T>> clusters,
            IDistanceCalculator distanceCalc)
        {
            double distortion = 0;
            foreach (var cluster in clusters)
            {
                foreach (var item in cluster.FeatureVectors)
                {
                    distortion += distanceCalc.Calculate(item, cluster.Centroid);
                }
            }
            return distortion;
        }

        public static double ComputeInertia(
            List<double[]> data,
            int[] labels,
            double[][] centroids,
            IDistanceCalculator distanceCalc)
        {
            double inertia = 0;
            for (int i = 0; i < data.Count; i++)
                inertia += distanceCalc.Calculate(data[i], centroids[labels[i]]);
            return inertia;
        }

        public static double CalculateSilhouetteScore(
            List<double[]> features,
            int[] labels,
            DistanceMetricType metric = DistanceMetricType.Euclidean)
        {
            if (features.Count < 2 || labels.Distinct().Count() < 2)
                return 0;

            var distanceCalc = DistanceCalculatorFactory.Create(metric);
            int n = features.Count;
            double totalScore = 0;

            for (int i = 0; i < n; i++)
            {
                int label = labels[i];
                var sameCluster = new List<int>();
                var otherClusters = new Dictionary<int, List<int>>();

                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    if (labels[j] == label)
                        sameCluster.Add(j);
                    else
                    {
                        if (!otherClusters.ContainsKey(labels[j]))
                            otherClusters[labels[j]] = new List<int>();
                        otherClusters[labels[j]].Add(j);
                    }
                }

                double a = 0;
                if (sameCluster.Count > 0)
                {
                    a = sameCluster.Average(j => distanceCalc.Calculate(features[i], features[j]));
                }

                double b = double.MaxValue;
                foreach (var kv in otherClusters)
                {
                    double avgDist = kv.Value.Average(j => distanceCalc.Calculate(features[i], features[j]));
                    if (avgDist < b) b = avgDist;
                }

                double max = Math.Max(a, b);
                if (max > 0)
                    totalScore += (b - a) / max;
            }

            return totalScore / n;
        }

        public static double CalculateDaviesBouldinIndex<T>(
            List<Cluster<T>> clusters,
            IDistanceCalculator distanceCalc)
        {
            int k = clusters.Count;
            if (k < 2) return 0;

            double totalScore = 0;

            for (int i = 0; i < k; i++)
            {
                double maxRatio = 0;
                double si = clusters[i].AverageDistance;

                for (int j = 0; j < k; j++)
                {
                    if (i == j) continue;
                    double sj = clusters[j].AverageDistance;
                    double dij = distanceCalc.Calculate(clusters[i].Centroid, clusters[j].Centroid);

                    if (dij > 0)
                    {
                        double ratio = (si + sj) / dij;
                        if (ratio > maxRatio) maxRatio = ratio;
                    }
                }

                totalScore += maxRatio;
            }

            return totalScore / k;
        }

        public static double CalculateCalinskiHarabaszIndex<T>(
            List<double[]> features,
            List<Cluster<T>> clusters,
            IDistanceCalculator distanceCalc)
        {
            int n = features.Count;
            int k = clusters.Count;
            if (k < 2 || n <= k) return 0;

            var globalCentroid = VectorOperations.CalculateCentroid(features);

            double ssb = 0;
            foreach (var cluster in clusters)
            {
                int size = cluster.FeatureVectors.Count;
                double dist = distanceCalc.Calculate(cluster.Centroid, globalCentroid);
                ssb += size * dist * dist;
            }

            double ssw = 0;
            foreach (var cluster in clusters)
            {
                foreach (var point in cluster.FeatureVectors)
                {
                    double dist = distanceCalc.Calculate(point, cluster.Centroid);
                    ssw += dist * dist;
                }
            }

            if (ssw == 0) return 0;

            return (ssb / (k - 1)) / (ssw / (n - k));
        }
    }
}
