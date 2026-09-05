#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class OpticsAlgorithm : IClusteringAlgorithm<double[][]>
    {
        private readonly double _epsilon;
        private readonly int _minPoints;

        public OpticsAlgorithm(double epsilon, int minPoints)
        {
            _epsilon = epsilon;
            _minPoints = minPoints;
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
                var processed = new bool[n];
                var reachability = new double[n];
                var coreDistance = new double[n];
                var order = new List<int>();

                for (int i = 0; i < n; i++)
                {
                    reachability[i] = double.MaxValue;
                    coreDistance[i] = double.MaxValue;
                }

                var seeds = new SortedSet<(double Reachability, int Index)>();

                for (int i = 0; i < n; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (processed[i]) continue;

                    progress?.Report(new ComputationProgress(
                        (double)i / n * 80,
                        $"OPTICS processing point {i + 1}/{n}"));

                    var neighbors = ClusteringEngine.GetNeighbors(input, i, _epsilon);
                    processed[i] = true;
                    order.Add(i);

                    if (neighbors.Count >= _minPoints)
                    {
                        coreDistance[i] = ClusteringEngine.GetNthDistance(input, i, neighbors, _minPoints);
                        seeds.Clear();

                        ClusteringEngine.UpdateSeeds(input, i, neighbors, coreDistance[i], ref reachability, seeds, _epsilon);

                        while (seeds.Count > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var (_, current) = seeds.Min;
                            seeds.Remove(seeds.Min);

                            if (processed[current]) continue;
                            processed[current] = true;
                            order.Add(current);

                            var currentNeighbors = ClusteringEngine.GetNeighbors(input, current, _epsilon);
                            if (currentNeighbors.Count >= _minPoints)
                            {
                                coreDistance[current] = ClusteringEngine.GetNthDistance(input, current, currentNeighbors, _minPoints);
                                ClusteringEngine.UpdateSeeds(input, current, currentNeighbors, coreDistance[current], ref reachability, seeds, _epsilon);
                            }
                        }
                    }
                }

                progress?.Report(new ComputationProgress(90, "Extracting clusters from reachability..."));
                cancellationToken.ThrowIfCancellationRequested();

                var labels = new int[n];
                int currentCluster = 0;

                for (int i = 0; i < order.Count; i++)
                {
                    int idx = order[i];
                    if (i > 0 && reachability[idx] > _epsilon * 0.5)
                    {
                        currentCluster++;
                    }
                    labels[idx] = currentCluster;
                }

                int clusterCount = currentCluster + 1;
                var centroids = new double[clusterCount][];
                for (int c = 0; c < clusterCount; c++)
                {
                    var points = input.Where((_, idx) => labels[idx] == c).ToList();
                    centroids[c] = points.Count > 0
                        ? VectorOperations.CalculateCentroid(points)
                        : Array.Empty<double>();
                }

                progress?.Report(new ComputationProgress(100, "OPTICS complete."));
                return new ClusteringResult
                {
                    Labels = labels,
                    Centroids = centroids,
                    Inertia = 0
                };
            }, cancellationToken);
        }

        public int GetExpectedClusterCount(double[][] input)
        {
            return Math.Max(1, input.Length / _minPoints);
        }

        public Func<double[][], int, int, double> GetDistanceFunction()
        {
            return (data, i, j) => ClusteringEngine.EuclideanDistance(data[i], data[j]);
        }
    }
}
