namespace MathematicalAlgorithms
{
    public enum ClusteringAlgorithmType { KMeans, Hierarchical }

    public class AdvancedClusteringConfig
    {
        public bool EnableParallel { get; set; } = true;
        public bool NormalizeFeatures { get; set; } = false;
        public int? RandomSeed { get; set; } = null;
        public ClusteringAlgorithmType AlgorithmType { get; set; } = ClusteringAlgorithmType.KMeans;
        public LinkageMethod HierarchicalLinkage { get; set; } = LinkageMethod.Average;
        public DistanceMetricType DistanceMetric { get; set; } = DistanceMetricType.Euclidean;
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    }
}
