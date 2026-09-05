using MathematicalAlgorithms;

namespace MathematicalAlgorithms
{
    public class DistanceMatrixBuilder
    {
        private readonly IDistanceCalculator _distanceCalculator;
        private readonly CodeElementFeatureExtractor _featureExtractor = new();

        public DistanceMatrixBuilder(DistanceMetricType metricType)
        {
            _distanceCalculator = DistanceCalculatorFactory.Create(metricType);
        }

        public double[,] BuildDistanceMatrix(IReadOnlyList<EnhancedCodeElement> elements)
        {
            var features = elements.Select(e => _featureExtractor.ExtractFeatures(e)).ToList();
            return ClusteringEngine.ComputeDistanceMatrix(features, _distanceCalculator);
        }

        public List<ElementDistance> BuildDistanceList(IReadOnlyList<EnhancedCodeElement> elements)
        {
            int n = elements.Count;
            var distances = new List<ElementDistance>();
            var features = elements.Select(e => _featureExtractor.ExtractFeatures(e)).ToArray();

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dist = _distanceCalculator.Calculate(features[i], features[j]);
                    double strength = elements[i].GetReferenceStrength(elements[j].Name);
                    distances.Add(new ElementDistance(
                        elements[i].Id, elements[j].Id, dist, strength, 1));
                }
            }

            return distances;
        }
    }
}
