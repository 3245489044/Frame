#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class FuzzyCMeansAlgorithm : IClusteringAlgorithm<double[][]>
    {
        private readonly int _nClusters;
        private readonly double _m;
        private readonly int _maxIterations;
        private readonly int? _seed;

        public FuzzyCMeansAlgorithm(int nClusters, double m = 2.0, int maxIterations = 100, int? seed = null)
        {
            _nClusters = nClusters;
            _m = m;
            _maxIterations = maxIterations;
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
                int n = input.Length;
                int dim = input[0].Length;
                var rng = _seed.HasValue ? new Random(_seed.Value) : new Random();

                progress?.Report(new ComputationProgress(5, "Initializing membership matrix..."));
                cancellationToken.ThrowIfCancellationRequested();

                var membership = ClusteringEngine.InitializeMembershipMatrix(n, _nClusters, rng);
                var centroids = new double[_nClusters][];
                for (int k = 0; k < _nClusters; k++)
                    centroids[k] = new double[dim];

                var result = IterationSkeleton.Iterate(
                    (Membership: membership, Centroids: centroids),
                    _maxIterations,
                    (state, iter) =>
                    {
                        ClusteringEngine.UpdateFuzzyCentroids(input, state.Membership, state.Centroids, _m);
                        ClusteringEngine.UpdateFuzzyMembership(input, state.Membership, state.Centroids, _m);
                        return state;
                    },
                    (state) => false,
                    (state) =>
                    {
                        var labels = new int[n];
                        for (int i = 0; i < n; i++)
                        {
                            int bestK = 0;
                            double maxMembership = 0;
                            for (int k = 0; k < _nClusters; k++)
                            {
                                if (state.Membership[i, k] > maxMembership)
                                {
                                    maxMembership = state.Membership[i, k];
                                    bestK = k;
                                }
                            }
                            labels[i] = bestK;
                        }

                        return new ClusteringResult
                        {
                            Labels = labels,
                            Centroids = state.Centroids,
                            Inertia = 0
                        };
                    },
                    progress,
                    "FuzzyCMeans");

                progress?.Report(new ComputationProgress(100, "Fuzzy C-Means complete."));
                return result;
            }, cancellationToken);
        }

        public int GetExpectedClusterCount(double[][] input)
        {
            return _nClusters;
        }

        public Func<double[][], int, int, double> GetDistanceFunction()
        {
            return (data, i, j) => ClusteringEngine.EuclideanDistance(data[i], data[j]);
        }
    }
}
