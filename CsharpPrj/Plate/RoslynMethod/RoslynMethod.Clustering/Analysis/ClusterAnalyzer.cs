namespace MathematicalAlgorithms
{
    public class ClusterAnalyzer
    {
        private readonly CodeElementFeatureExtractor _featureExtractor = new();

        public List<Cluster<EnhancedCodeElement>> AnalyzeClusters(
            List<MathematicalAlgorithms.Cluster<EnhancedCodeElement>> mathClusters,
            IReadOnlyList<EnhancedCodeElement> elements)
        {
            var result = new List<Cluster<EnhancedCodeElement>>();

            foreach (var mc in mathClusters)
            {
                var cluster = new Cluster<EnhancedCodeElement>
                {
                    Items = mc.Items,
                    Centroid = mc.Centroid,
                    Cohesion = CalculateCohesion(mc),
                    Coupling = CalculateCoupling(mc, mathClusters),
                    AverageDistance = mc.AverageDistance,
                    MaxDistance = mc.MaxDistance,
                    Radius = mc.Radius,
                    Diameter = mc.Diameter,
                    Density = mc.Density
                };

                result.Add(cluster);
            }

            return result;
        }

        private double CalculateCohesion<T>(Cluster<T> cluster)
        {
            if (cluster.Items.Count < 2) return 1.0;
            return 1.0 / (1.0 + cluster.AverageDistance);
        }

        private double CalculateCoupling<T>(Cluster<T> cluster, List<Cluster<T>> allClusters)
        {
            if (allClusters.Count <= 1) return 0.0;

            double totalDistance = 0;
            int count = 0;

            foreach (var other in allClusters)
            {
                if (other == cluster) continue;
                totalDistance += MathematicalAlgorithms.VectorMath.EuclideanDistance(cluster.Centroid, other.Centroid);
                count++;
            }

            return count > 0 ? totalDistance / count : 0;
        }
    }
}
