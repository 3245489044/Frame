using MathematicalAlgorithms;

namespace MathematicalAlgorithms
{
    public class SkeletonFlow
    {
        private readonly AdvancedClusteringConfig _config;
        private readonly IClusteringAnalyzer<EnhancedCodeElement> _analyzer;

        public SkeletonFlow(AdvancedClusteringConfig config = null)
        {
            _config = config ?? new AdvancedClusteringConfig();
            _analyzer = CreateAnalyzer();
        }

        public async Task<AnalysisResult<EnhancedCodeElement>> AnalyzeAsync(
            IReadOnlyList<EnhancedCodeElement> elements,
            int maxClusters = 5,
            IProgress<ComputationProgress> progress = null)
        {
            if (elements == null || elements.Count == 0)
                return new AnalysisResult<EnhancedCodeElement>
                {
                    Error = "Elements collection cannot be null or empty"
                };

            progress?.Report(new ComputationProgress(0, "Starting clustering analysis..."));

            progress?.Report(new ComputationProgress(10, "Extracting features..."));
            var features = ExtractFeatures(elements);

            progress?.Report(new ComputationProgress(20, "Building distance matrix..."));
            var distanceCalc = DistanceCalculatorFactory.Create(_config.DistanceMetric);
            var distanceMatrix = ClusteringEngine.ComputeDistanceMatrix(features, distanceCalc);

            progress?.Report(new ComputationProgress(30, "Running clustering algorithm..."));
            var analysisResult = await _analyzer.AnalyzeAsync(elements, maxClusters, progress);

            progress?.Report(new ComputationProgress(80, "Post-processing clusters..."));
            var clusterAnalyzer = new ClusterAnalyzer();
            var enhancedClusters = clusterAnalyzer.AnalyzeClusters(analysisResult.Clusters, elements);

            progress?.Report(new ComputationProgress(90, "Aggregating results..."));
            var finalResult = new AnalysisResult<EnhancedCodeElement>
            {
                Items = elements.ToList(),
                Clusters = enhancedClusters,
                Metrics = analysisResult.Metrics,
                AnalysisTime = analysisResult.AnalysisTime,
                Error = analysisResult.Error
            };

            finalResult.Metrics["DistanceMetric"] = _config.DistanceMetric.ToString();
            finalResult.Metrics["AlgorithmType"] = _config.AlgorithmType.ToString();
            finalResult.Metrics["SilhouetteScore"] = CalculateSilhouetteScore(features, enhancedClusters);

            progress?.Report(new ComputationProgress(100, "Analysis complete."));
            return finalResult;
        }

        public async Task<AnalysisResult<EnhancedCodeElement>> QuickAnalyzeAsync(
            IReadOnlyList<EnhancedCodeElement> elements,
            int maxClusters = 5)
        {
            if (elements == null || elements.Count == 0)
                return new AnalysisResult<EnhancedCodeElement>
                {
                    Error = "Elements collection cannot be null or empty"
                };

            var features = ExtractFeatures(elements);
            var analysisResult = await _analyzer.AnalyzeAsync(elements, maxClusters);

            var clusterAnalyzer = new ClusterAnalyzer();
            var enhancedClusters = clusterAnalyzer.AnalyzeClusters(analysisResult.Clusters, elements);

            return new AnalysisResult<EnhancedCodeElement>
            {
                Items = elements.ToList(),
                Clusters = enhancedClusters,
                Metrics = analysisResult.Metrics,
                AnalysisTime = analysisResult.AnalysisTime,
                Error = analysisResult.Error
            };
        }

        public static List<Cluster<EnhancedCodeElement>> KMeansCluster(
            IReadOnlyList<EnhancedCodeElement> elements,
            int k,
            DistanceMetricType metric = DistanceMetricType.Euclidean)
        {
            if (elements == null || elements.Count == 0 || k <= 0)
                return new List<Cluster<EnhancedCodeElement>>();

            var extractor = new CodeElementFeatureExtractor();
            var features = elements.Select(e => extractor.ExtractFeatures(e)).ToList();
            var distanceCalc = DistanceCalculatorFactory.Create(metric);

            var kmeans = new KMeansClusterer(k, metric: metric);
            var result = kmeans.Cluster(features);

            var clusters = ClusteringEngine.BuildClusters<EnhancedCodeElement>(features, result.Centroids, result.Labels, k);

            for (int i = 0; i < elements.Count; i++)
            {
                int label = result.Labels[i];
                clusters[label].Items.Add(elements[i]);
            }

            for (int i = 0; i < k; i++)
            {
                var cluster = clusters[i];
                if (cluster.FeatureVectors.Count > 0)
                {
                    ClusteringEngine.ComputeClusterStatistics(cluster, distanceCalc);
                }
            }

            return clusters;
        }

        public static List<Cluster<EnhancedCodeElement>> HierarchicalCluster(
            IReadOnlyList<EnhancedCodeElement> elements,
            int maxClusters,
            LinkageMethod linkage = LinkageMethod.Average,
            DistanceMetricType metric = DistanceMetricType.Euclidean)
        {
            if (elements == null || elements.Count == 0 || maxClusters <= 0)
                return new List<Cluster<EnhancedCodeElement>>();

            var extractor = new CodeElementFeatureExtractor();
            var features = elements.Select(e => extractor.ExtractFeatures(e)).ToList();
            var distanceCalc = DistanceCalculatorFactory.Create(metric);

            var hierarchical = new HierarchicalClustering<EnhancedCodeElement>(
                linkageMethod: linkage, metricType: metric);

            var task = hierarchical.AnalyzeAsync(elements, maxClusters);
            var result = task.GetAwaiter().GetResult();

            return result.Clusters;
        }

        #region 私有方法

        private IClusteringAnalyzer<EnhancedCodeElement> CreateAnalyzer()
        {
            return _config.AlgorithmType switch
            {
                ClusteringAlgorithmType.KMeans => new KMeansClustering<EnhancedCodeElement>(
                    new ClusteringConfig
                    {
                        Seed = _config.RandomSeed,
                        EnableParallel = _config.EnableParallel,
                        MaxDegreeOfParallelism = _config.MaxDegreeOfParallelism,
                        NormalizeFeatures = _config.NormalizeFeatures
                    },
                    _config.DistanceMetric),

                ClusteringAlgorithmType.Hierarchical => new HierarchicalClustering<EnhancedCodeElement>(
                    new ClusteringConfig
                    {
                        Seed = _config.RandomSeed,
                        EnableParallel = _config.EnableParallel,
                        MaxDegreeOfParallelism = _config.MaxDegreeOfParallelism,
                        NormalizeFeatures = _config.NormalizeFeatures
                    },
                    _config.HierarchicalLinkage,
                    _config.DistanceMetric),

                _ => new KMeansClustering<EnhancedCodeElement>(
                    new ClusteringConfig
                    {
                        Seed = _config.RandomSeed,
                        EnableParallel = _config.EnableParallel,
                        MaxDegreeOfParallelism = _config.MaxDegreeOfParallelism,
                        NormalizeFeatures = _config.NormalizeFeatures
                    },
                    _config.DistanceMetric)
            };
        }

        private List<double[]> ExtractFeatures(IReadOnlyList<EnhancedCodeElement> elements)
        {
            var extractor = new CodeElementFeatureExtractor();
            var features = elements.Select(e => extractor.ExtractFeatures(e)).ToList();

            if (_config.NormalizeFeatures)
            {
                features = VectorOperations.StandardizeFeatures(features);
            }

            return features;
        }

        private double CalculateSilhouetteScore(
            List<double[]> features,
            List<Cluster<EnhancedCodeElement>> clusters)
        {
            if (features.Count < 2 || clusters.Count < 2)
                return 0;

            var labels = new int[features.Count];
            for (int c = 0; c < clusters.Count; c++)
            {
                for (int i = 0; i < clusters[c].FeatureVectors.Count; i++)
                {
                    var feature = clusters[c].FeatureVectors[i];
                    int idx = features.IndexOf(feature);
                    if (idx >= 0) labels[idx] = c;
                }
            }

            return ClusteringMetrics.CalculateSilhouetteScore(features, labels);
        }

        #endregion
    }
}
