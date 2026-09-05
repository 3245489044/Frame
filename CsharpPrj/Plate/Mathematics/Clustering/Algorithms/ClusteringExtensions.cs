#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public static class ClusteringExtensions
    {
        #region 便捷扩展方法

        public static List<Cluster<double[]>> ClusterKMeans(this double[][] data, int k)
        {
            return ClusteringAlgorithms.KMeans(data, k);
        }

        public static List<Cluster<double[]>> ClusterHierarchical(this double[][] data)
        {
            return ClusteringAlgorithms.HierarchicalClustering(data);
        }

        public static List<Cluster<double[]>> ClusterDBSCAN(this double[][] data, double epsilon, int minPoints)
        {
            return ClusteringAlgorithms.DBSCAN(data, epsilon, minPoints);
        }

        public static List<Cluster<double[]>> ClusterSpectral(this double[][] data, int nClusters)
        {
            return ClusteringAlgorithms.SpectralClustering(data, nClusters);
        }

        public static List<Cluster<double[]>> ClusterAffinityPropagation(this double[][] data)
        {
            return ClusteringAlgorithms.AffinityPropagation(data);
        }

        public static List<Cluster<double[]>> ClusterMeanShift(this double[][] data)
        {
            return ClusteringAlgorithms.MeanShift(data);
        }

        public static List<Cluster<double[]>> ClusterGaussianMixture(this double[][] data, int nComponents)
        {
            return ClusteringAlgorithms.GaussianMixture(data, nComponents);
        }

        public static List<Cluster<double[]>> ClusterFuzzyCMeans(this double[][] data, int nClusters, double m = 2.0)
        {
            return ClusteringAlgorithms.FuzzyCMeans(data, nClusters, m);
        }

        public static List<Cluster<double[]>> ClusterBIRCH(this double[][] data, int nClusters, double threshold = 0.5)
        {
            return ClusteringAlgorithms.BIRCH(data, nClusters, threshold);
        }

        public static List<Cluster<double[]>> ClusterOptics(this double[][] data, double epsilon, int minPoints)
        {
            return ClusteringAlgorithms.Optics(data, epsilon, minPoints);
        }

        #endregion

        #region 聚类结果分析扩展

        public static double SilhouetteScore(this List<Cluster<double[]>> clusters, double[][] data)
        {
            var labels = new int[data.Length];
            for (int i = 0; i < clusters.Count; i++)
            {
                foreach (var point in clusters[i].FeatureVectors)
                {
                    int idx = Array.IndexOf(data, point);
                    if (idx >= 0) labels[idx] = i;
                }
            }
            return ClusteringMetrics.CalculateSilhouetteScore(data.ToList(), labels);
        }

        public static double DaviesBouldinIndex(this List<Cluster<double[]>> clusters)
        {
            var distanceCalc = DistanceCalculatorFactory.Create(DistanceMetricType.Euclidean);
            return ClusteringMetrics.CalculateDaviesBouldinIndex(clusters, distanceCalc);
        }

        public static double CalinskiHarabaszIndex(this List<Cluster<double[]>> clusters, double[][] data)
        {
            var distanceCalc = DistanceCalculatorFactory.Create(DistanceMetricType.Euclidean);
            return ClusteringMetrics.CalculateCalinskiHarabaszIndex(data.ToList(), clusters, distanceCalc);
        }

        #endregion

        #region 聚类统计信息扩展

        public static void ComputeStatistics(this Cluster<double[]> cluster)
        {
            var distanceCalc = DistanceCalculatorFactory.Create(DistanceMetricType.Euclidean);
            ClusteringEngine.ComputeClusterStatistics(cluster, distanceCalc);
        }

        public static Dictionary<int, int> GetClusterSizeDistribution(this List<Cluster<double[]>> clusters)
        {
            var distribution = new Dictionary<int, int>();
            for (int i = 0; i < clusters.Count; i++)
            {
                distribution[i] = clusters[i].Count;
            }
            return distribution;
        }

        public static Cluster<double[]> GetLargestCluster(this List<Cluster<double[]>> clusters)
        {
            return clusters.OrderByDescending(c => c.Count).FirstOrDefault();
        }

        public static Cluster<double[]> GetSmallestCluster(this List<Cluster<double[]>> clusters)
        {
            return clusters.OrderBy(c => c.Count).FirstOrDefault();
        }

        #endregion
    }
}
