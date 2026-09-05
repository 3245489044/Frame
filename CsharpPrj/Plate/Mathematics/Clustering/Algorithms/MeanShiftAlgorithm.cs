#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class MeanShiftAlgorithm : IClusteringAlgorithm<double[][]>
    {
        private readonly double? _bandwidth;
        private readonly int _maxIterations;

        public MeanShiftAlgorithm(double? bandwidth = null, int maxIterations = 50)
        {
            _bandwidth = bandwidth;
            _maxIterations = maxIterations;
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
                double bandwidth = _bandwidth ?? ClusteringEngine.EstimateBandwidth(input);
                var points = input.Select(d => (double[])d.Clone()).ToList();

                progress?.Report(new ComputationProgress(10, $"MeanShift with bandwidth={bandwidth:F4}"));

                var shiftedPoints = new List<double[]>();

                for (int p = 0; p < points.Count; p++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report(new ComputationProgress(
                        10 + (double)p / points.Count * 60,
                        $"MeanShift shifting point {p + 1}/{points.Count}"));

                    var shiftedPoint = (double[])points[p].Clone();

                    for (int iter = 0; iter < _maxIterations; iter++)
                    {
                        var newPoint = new double[dim];
                        double totalWeight = 0;

                        foreach (var other in points)
                        {
                            double dist = ClusteringEngine.EuclideanDistance(shiftedPoint, other);
                            double weight = ClusteringEngine.GaussianKernel(dist, bandwidth);
                            for (int j = 0; j < dim; j++)
                                newPoint[j] += other[j] * weight;
                            totalWeight += weight;
                        }

                        if (totalWeight > 0)
                        {
                            for (int j = 0; j < dim; j++)
                                newPoint[j] /= totalWeight;
                        }

                        if (ClusteringEngine.EuclideanDistance(shiftedPoint, newPoint) < 1e-3)
                            break;

                        shiftedPoint = newPoint;
                    }

                    shiftedPoints.Add(shiftedPoint);
                }

                progress?.Report(new ComputationProgress(75, "Merging nearby centers..."));
                cancellationToken.ThrowIfCancellationRequested();

                var clusterCenters = new List<double[]>();
                foreach (var point in shiftedPoints)
                {
                    bool merged = false;
                    foreach (var center in clusterCenters)
                    {
                        if (ClusteringEngine.EuclideanDistance(point, center) < bandwidth / 2)
                        {
                            merged = true;
                            break;
                        }
                    }
                    if (!merged)
                        clusterCenters.Add(point);
                }

                progress?.Report(new ComputationProgress(90, "Assigning points to clusters..."));
                cancellationToken.ThrowIfCancellationRequested();

                var labels = new int[n];
                for (int i = 0; i < n; i++)
                {
                    int bestCluster = 0;
                    double bestDist = double.MaxValue;
                    for (int j = 0; j < clusterCenters.Count; j++)
                    {
                        double dist = ClusteringEngine.EuclideanDistance(points[i], clusterCenters[j]);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestCluster = j;
                        }
                    }
                    labels[i] = bestCluster;
                }

                var centroids = clusterCenters.ToArray();

                progress?.Report(new ComputationProgress(100, "MeanShift complete."));
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
            return Math.Max(1, (int)Math.Sqrt(input.Length));
        }

        public Func<double[][], int, int, double> GetDistanceFunction()
        {
            return (data, i, j) => ClusteringEngine.EuclideanDistance(data[i], data[j]);
        }
    }
}
