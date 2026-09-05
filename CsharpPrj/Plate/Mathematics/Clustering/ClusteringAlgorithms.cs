using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MathematicalAlgorithms
{
    #region K-Means 聚类

    public class KMeansClusterer
    {
        private readonly KMeansAlgorithm _algorithm;

        public KMeansClusterer(int numClusters, int maxIterations = 100,
            DistanceMetricType metric = DistanceMetricType.Euclidean, int numAttempts = 10)
        {
            _algorithm = new KMeansAlgorithm(
                numClusters, maxIterations, metric, numAttempts);
        }

        public ClusteringResult Cluster(List<double[]> data)
        {
            if (data == null || data.Count == 0)
                return new ClusteringResult { Labels = Array.Empty<int>(), Centroids = Array.Empty<double[]>() };

            var dataArray = data.Select(d => d.ToArray()).ToArray();
            return _algorithm.ComputeAsync(dataArray, ClusteringComputationConfig.Default)
                .GetAwaiter().GetResult();
        }
    }

    #endregion

    #region K-Means 聚类（泛型版本）

    public class KMeansClustering<T> : IClusteringAnalyzer<T>
    {
        private readonly ClusteringConfig _config;
        private readonly IDistanceCalculator _distanceCalculator;
        private readonly Random _random;

        public KMeansClustering(ClusteringConfig config = null, DistanceMetricType metricType = DistanceMetricType.Euclidean)
        {
            _config = config ?? new ClusteringConfig();
            _distanceCalculator = DistanceCalculatorFactory.Create(metricType);
            _random = _config.Seed.HasValue ? new Random(_config.Seed.Value) : new Random();
        }

        public async Task<AnalysisResult<T>> AnalyzeAsync(IReadOnlyList<T> items, int maxClusters,
            IProgress<ComputationProgress> progress = null)
        {
            var result = new AnalysisResult<T>();
            var sw = Stopwatch.StartNew();

            try
            {
                ClusteringValidator.ValidateItems(items);
                ClusteringValidator.ValidateMaxClusters(maxClusters);

                var features = ExtractFeatures(items);
                var optimalK = DetermineOptimalK(features, maxClusters);
                var clusters = await Task.Run(() => RunKMeans(features, optimalK, progress));

                result.Clusters = clusters;
                result.Items = items.ToList();
                result.Metrics["OptimalK"] = optimalK;
                result.Metrics["TotalItems"] = items.Count;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            sw.Stop();
            result.AnalysisTime = sw.Elapsed.TotalMilliseconds;
            return result;
        }

        protected virtual List<double[]> ExtractFeatures(IReadOnlyList<T> items)
        {
            throw new NotSupportedException($"Feature extraction not supported for type {typeof(T).Name}");
        }

        private int DetermineOptimalK(List<double[]> features, int maxClusters)
        {
            int n = features.Count;
            if (n <= 2) return Math.Min(n, maxClusters);

            int maxK = Math.Min(maxClusters, n - 1);
            var distortions = new double[maxK - 1];

            for (int k = 2; k <= maxK; k++)
            {
                var clusters = RunKMeans(features, k, null);
                distortions[k - 2] = ClusteringMetrics.CalculateDistortion(features, clusters, _distanceCalculator);
            }

            int optimalK = 2;
            double maxImprovement = 0;
            for (int i = 1; i < distortions.Length; i++)
            {
                double improvement = distortions[i - 1] - distortions[i];
                if (improvement > maxImprovement)
                {
                    maxImprovement = improvement;
                    optimalK = i + 2;
                }
            }

            return optimalK;
        }

        private List<Cluster<T>> RunKMeans(List<double[]> features, int k,
            IProgress<ComputationProgress> progress)
        {
            var dataArray = features.Select(f => f.ToArray()).ToArray();
            var kmeans = new KMeansAlgorithm(k, 100,
                DistanceMetricType.Euclidean, 1, _config.Seed);
            var config = new ClusteringComputationConfig
            {
                EnableParallel = _config.EnableParallel,
                MaxDegreeOfParallelism = _config.MaxDegreeOfParallelism
            };
            var kmeansResult = kmeans.ComputeAsync(dataArray, config, progress)
                .GetAwaiter().GetResult();

            var clusters = ClusteringEngine.BuildClusters<T>(
                features, kmeansResult.Centroids, kmeansResult.Labels, k);

            foreach (var cluster in clusters)
            {
                ClusteringEngine.ComputeClusterStatistics(cluster, _distanceCalculator);
            }

            return clusters;
        }
    }

    #endregion

    #region 层次聚类

    public class HierarchicalClustering<T> : IClusteringAnalyzer<T>
    {
        private readonly ClusteringConfig _config;
        private readonly LinkageMethod _linkageMethod;
        private readonly IDistanceCalculator _distanceCalculator;

        public HierarchicalClustering(ClusteringConfig config = null,
            LinkageMethod linkageMethod = LinkageMethod.Average,
            DistanceMetricType metricType = DistanceMetricType.Euclidean)
        {
            _config = config ?? new ClusteringConfig();
            _linkageMethod = linkageMethod;
            _distanceCalculator = DistanceCalculatorFactory.Create(metricType);
        }

        public async Task<AnalysisResult<T>> AnalyzeAsync(IReadOnlyList<T> items, int maxClusters,
            IProgress<ComputationProgress> progress = null)
        {
            var result = new AnalysisResult<T>();
            var sw = Stopwatch.StartNew();

            try
            {
                ClusteringValidator.ValidateItems(items);
                ClusteringValidator.ValidateMaxClusters(maxClusters);

                var features = ExtractFeatures(items);
                var clusters = await Task.Run(() => RunHierarchical(features, maxClusters, progress));

                result.Clusters = clusters;
                result.Items = items.ToList();
                result.Metrics["LinkageMethod"] = _linkageMethod.ToString();
                result.Metrics["TotalItems"] = items.Count;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            sw.Stop();
            result.AnalysisTime = sw.Elapsed.TotalMilliseconds;
            return result;
        }

        protected virtual List<double[]> ExtractFeatures(IReadOnlyList<T> items)
        {
            throw new NotSupportedException($"Feature extraction not supported for type {typeof(T).Name}");
        }

        private List<Cluster<T>> RunHierarchical(List<double[]> features, int maxClusters,
            IProgress<ComputationProgress> progress)
        {
            int n = features.Count;
            var clusters = new List<Cluster<T>>();

            for (int i = 0; i < n; i++)
            {
                clusters.Add(new Cluster<T>
                {
                    FeatureVectors = new List<double[]> { features[i] },
                    Centroid = features[i]
                });
            }

            var distanceMatrix = ClusteringEngine.ComputeDistanceMatrix(features, _distanceCalculator);

            while (clusters.Count > maxClusters)
            {
                ClusteringEngine.TryMergeClosestClusters(clusters,
                    (a, b) => CalculateLinkageDistance(a, b, distanceMatrix));

                progress?.Report(new ComputationProgress(
                    (double)(n - clusters.Count) / (n - maxClusters) * 100,
                    $"Hierarchical clustering: {clusters.Count} clusters remaining"));
            }

            return clusters;
        }

        private double CalculateLinkageDistance(Cluster<T> clusterA, Cluster<T> clusterB, double[,] distanceMatrix)
        {
            return _linkageMethod switch
            {
                LinkageMethod.Single => CalculateSingleLinkage(clusterA, clusterB),
                LinkageMethod.Complete => CalculateCompleteLinkage(clusterA, clusterB),
                LinkageMethod.Average => CalculateAverageLinkage(clusterA, clusterB),
                _ => CalculateAverageLinkage(clusterA, clusterB)
            };
        }

        private double CalculateSingleLinkage(Cluster<T> clusterA, Cluster<T> clusterB)
        {
            double minDist = double.MaxValue;
            foreach (var va in clusterA.FeatureVectors)
            {
                foreach (var vb in clusterB.FeatureVectors)
                {
                    double dist = _distanceCalculator.Calculate(va, vb);
                    if (dist < minDist) minDist = dist;
                }
            }
            return minDist;
        }

        private double CalculateCompleteLinkage(Cluster<T> clusterA, Cluster<T> clusterB)
        {
            double maxDist = double.MinValue;
            foreach (var va in clusterA.FeatureVectors)
            {
                foreach (var vb in clusterB.FeatureVectors)
                {
                    double dist = _distanceCalculator.Calculate(va, vb);
                    if (dist > maxDist) maxDist = dist;
                }
            }
            return maxDist;
        }

        private double CalculateAverageLinkage(Cluster<T> clusterA, Cluster<T> clusterB)
        {
            double totalDist = 0;
            int count = 0;
            foreach (var va in clusterA.FeatureVectors)
            {
                foreach (var vb in clusterB.FeatureVectors)
                {
                    totalDist += _distanceCalculator.Calculate(va, vb);
                    count++;
                }
            }
            return count > 0 ? totalDist / count : double.MaxValue;
        }
    }

    #endregion

    #region 静态聚类算法入口

    public static class ClusteringAlgorithms
    {
        private static List<Cluster<double[]>> ConvertToClusters(ClusteringResult result, double[][] data)
        {
            var clusters = new List<Cluster<double[]>>();
            int k = result.Centroids.Length;

            for (int i = 0; i < k; i++)
                clusters.Add(new Cluster<double[]> { Centroid = result.Centroids[i] });

            for (int i = 0; i < data.Length; i++)
            {
                int label = result.Labels[i];
                if (label >= 0 && label < k)
                    clusters[label].FeatureVectors.Add(data[i]);
            }

            return clusters.Where(c => c.FeatureVectors.Count > 0).ToList();
        }

        public static List<Cluster<double[]>> KMeans(double[][] data, int k)
        {
            ClusteringValidator.ValidateData(data);
            ClusteringValidator.ValidateK(k, data.Length);

            var algorithm = new KMeansAlgorithm(k);
            var result = algorithm.ComputeAsync(data, ClusteringComputationConfig.Default)
                .GetAwaiter().GetResult();

            return ConvertToClusters(result, data);
        }

        public static List<Cluster<double[]>> HierarchicalClustering(double[][] data)
        {
            ClusteringValidator.ValidateData(data);

            var features = data.Select(d => (double[])d.Clone()).ToList();
            var clusters = new List<Cluster<double[]>>();

            for (int i = 0; i < features.Count; i++)
            {
                clusters.Add(new Cluster<double[]>
                {
                    FeatureVectors = new List<double[]> { features[i] },
                    Centroid = features[i]
                });
            }

            while (clusters.Count > 1)
            {
                ClusteringEngine.TryMergeClosestClusters(clusters,
                    (a, b) => ClusteringEngine.AverageLinkageDistance(a, b));
            }

            return clusters;
        }

        public static List<Cluster<double[]>> DBSCAN(double[][] data, double epsilon, int minPoints)
        {
            ClusteringValidator.ValidateData(data);
            ClusteringValidator.ValidateEpsilonAndMinPoints(epsilon, minPoints);

            var algorithm = new DbscanAlgorithm(epsilon, minPoints);
            var result = algorithm.ComputeAsync(data, ClusteringComputationConfig.Default)
                .GetAwaiter().GetResult();

            return ConvertToClusters(result, data);
        }

        public static List<Cluster<double[]>> SpectralClustering(double[][] data, int nClusters)
        {
            ClusteringValidator.ValidateData(data);
            ClusteringValidator.ValidateNClusters(nClusters, data.Length);

            var algorithm = new SpectralAlgorithm(nClusters);
            var result = algorithm.ComputeAsync(data, ClusteringComputationConfig.Default)
                .GetAwaiter().GetResult();

            return ConvertToClusters(result, data);
        }

        public static List<Cluster<double[]>> AffinityPropagation(double[][] data)
        {
            ClusteringValidator.ValidateData(data);

            var algorithm = new AffinityPropagationAlgorithm();
            var result = algorithm.ComputeAsync(data, ClusteringComputationConfig.Default)
                .GetAwaiter().GetResult();

            return ConvertToClusters(result, data);
        }

        public static List<Cluster<double[]>> MeanShift(double[][] data)
        {
            ClusteringValidator.ValidateData(data);

            var algorithm = new MeanShiftAlgorithm();
            var result = algorithm.ComputeAsync(data, ClusteringComputationConfig.Default)
                .GetAwaiter().GetResult();

            return ConvertToClusters(result, data);
        }

        public static List<Cluster<double[]>> GaussianMixture(double[][] data, int nComponents)
        {
            ClusteringValidator.ValidateData(data);
            ClusteringValidator.ValidateNComponents(nComponents, data.Length);

            var algorithm = new GaussianMixtureAlgorithm(nComponents);
            var result = algorithm.ComputeAsync(data, ClusteringComputationConfig.Default)
                .GetAwaiter().GetResult();

            return ConvertToClusters(result, data);
        }

        public static List<Cluster<double[]>> FuzzyCMeans(double[][] data, int nClusters, double m = 2.0)
        {
            ClusteringValidator.ValidateData(data);

            var algorithm = new FuzzyCMeansAlgorithm(nClusters, m);
            var result = algorithm.ComputeAsync(data, ClusteringComputationConfig.Default)
                .GetAwaiter().GetResult();

            return ConvertToClusters(result, data);
        }

        public static List<Cluster<double[]>> BIRCH(double[][] data, int nClusters, double threshold = 0.5)
        {
            ClusteringValidator.ValidateData(data);

            var algorithm = new BirchAlgorithm(nClusters, threshold);
            var result = algorithm.ComputeAsync(data, ClusteringComputationConfig.Default)
                .GetAwaiter().GetResult();

            return ConvertToClusters(result, data);
        }

        public static List<Cluster<double[]>> Optics(double[][] data, double epsilon, int minPoints)
        {
            ClusteringValidator.ValidateData(data);
            ClusteringValidator.ValidateEpsilonAndMinPoints(epsilon, minPoints);

            var algorithm = new OpticsAlgorithm(epsilon, minPoints);
            var result = algorithm.ComputeAsync(data, ClusteringComputationConfig.Default)
                .GetAwaiter().GetResult();

            return ConvertToClusters(result, data);
        }
    }

    #endregion
}
