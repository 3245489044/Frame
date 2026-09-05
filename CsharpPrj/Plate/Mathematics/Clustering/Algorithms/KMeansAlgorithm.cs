#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class KMeansAlgorithm : IClusteringAlgorithm<double[][]>
    {
        private readonly int _k;
        private readonly int _maxIterations;
        private readonly DistanceMetricType _metric;
        private readonly int _numAttempts;
        private readonly int? _seed;

        public KMeansAlgorithm(
            int k,
            int maxIterations = 100,
            DistanceMetricType metric = DistanceMetricType.Euclidean,
            int numAttempts = 10,
            int? seed = null)
        {
            _k = Math.Max(1, k);
            _maxIterations = Math.Max(1, maxIterations);
            _metric = metric;
            _numAttempts = Math.Max(1, numAttempts);
            _seed = seed;
        }

        public Task<ClusteringResult> ComputeAsync(
            double[][] input,
            ClusteringComputationConfig config,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var data = input.Select(d => (double[])d.Clone()).ToList();
                int n = data.Count;
                int dim = data[0].Length;
                var rng = _seed.HasValue ? new Random(_seed.Value) : new Random();
                var distanceCalc = DistanceCalculatorFactory.Create(_metric);

                var bestResult = default(ClusteringResult);
                double bestInertia = double.MaxValue;

                for (int attempt = 0; attempt < _numAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report(new ComputationProgress(
                        (double)attempt / _numAttempts * 100,
                        $"K-Means attempt {attempt + 1}/{_numAttempts}"));

                    var centroids = ClusteringEngine.InitializeKMeansPlusPlus(data, _k, rng, distanceCalc);
                    var labels = new int[n];

                    for (int iter = 0; iter < _maxIterations; iter++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        bool changed = ClusteringEngine.AssignClusters(data, labels, centroids, distanceCalc, config);
                        if (!changed) break;

                        centroids = ClusteringEngine.UpdateCentroids(data, labels, _k, dim, rng, config);
                    }

                    double inertia = ClusteringMetrics.ComputeInertia(data, labels, centroids, distanceCalc);

                    if (inertia < bestInertia)
                    {
                        bestInertia = inertia;
                        bestResult = new ClusteringResult
                        {
                            Labels = labels,
                            Centroids = centroids,
                            Inertia = inertia
                        };
                    }
                }

                progress?.Report(new ComputationProgress(100, "K-Means complete."));
                return bestResult;
            }, cancellationToken);
        }

        public int GetExpectedClusterCount(double[][] input)
        {
            return _k;
        }

        public Func<double[][], int, int, double> GetDistanceFunction()
        {
            var distanceCalc = DistanceCalculatorFactory.Create(_metric);
            return (data, i, j) => distanceCalc.Calculate(data[i], data[j]);
        }
    }
}
