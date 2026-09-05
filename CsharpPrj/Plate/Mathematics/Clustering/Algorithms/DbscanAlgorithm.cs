#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class DbscanAlgorithm : IClusteringAlgorithm<double[][]>
    {
        private readonly double _epsilon;
        private readonly int _minPoints;

        public DbscanAlgorithm(double epsilon, int minPoints)
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
                var visited = new bool[n];
                var clustered = new bool[n];
                var labels = new int[n];
                int currentCluster = 0;

                for (int i = 0; i < n; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (visited[i]) continue;
                    visited[i] = true;

                    progress?.Report(new ComputationProgress(
                        (double)i / n * 100,
                        $"DBSCAN processing point {i + 1}/{n}"));

                    var neighbors = ClusteringEngine.GetNeighbors(input, i, _epsilon);

                    if (neighbors.Count < _minPoints)
                    {
                        labels[i] = -1;
                    }
                    else
                    {
                        currentCluster++;
                        var queue = new Queue<int>();
                        queue.Enqueue(i);
                        clustered[i] = true;
                        labels[i] = currentCluster;

                        while (queue.Count > 0)
                        {
                            int current = queue.Dequeue();

                            if (!visited[current])
                            {
                                visited[current] = true;
                                var currentNeighbors = ClusteringEngine.GetNeighbors(input, current, _epsilon);

                                if (currentNeighbors.Count >= _minPoints)
                                {
                                    foreach (var neighbor in currentNeighbors)
                                    {
                                        if (!clustered[neighbor])
                                        {
                                            clustered[neighbor] = true;
                                            labels[neighbor] = currentCluster;
                                            queue.Enqueue(neighbor);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                var uniqueLabels = labels.Where(l => l > 0).Distinct().ToArray();
                var centroids = new double[uniqueLabels.Length][];

                for (int c = 0; c < uniqueLabels.Length; c++)
                {
                    int label = uniqueLabels[c];
                    var points = input.Where((_, idx) => labels[idx] == label).ToList();
                    centroids[c] = VectorOperations.CalculateCentroid(
                        points.Select(p => p.ToArray()));
                }

                progress?.Report(new ComputationProgress(100, "DBSCAN complete."));

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
