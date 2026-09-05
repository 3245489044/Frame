#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class BirchAlgorithm : IClusteringAlgorithm<double[][]>
    {
        private readonly int _nClusters;
        private readonly double _threshold;

        public BirchAlgorithm(int nClusters, double threshold = 0.5)
        {
            _nClusters = nClusters;
            _threshold = threshold;
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
                var clusters = new List<Cluster<double[]>>();

                for (int i = 0; i < n; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report(new ComputationProgress(
                        (double)i / n * 80,
                        $"BIRCH inserting point {i + 1}/{n}"));

                    var point = input[i];
                    bool added = false;

                    foreach (var cluster in clusters)
                    {
                        double dist = ClusteringEngine.EuclideanDistance(point, cluster.Centroid);
                        if (dist < _threshold)
                        {
                            cluster.FeatureVectors.Add(point);
                            cluster.Centroid = VectorOperations.CalculateCentroid(cluster.FeatureVectors);
                            added = true;
                            break;
                        }
                    }

                    if (!added)
                    {
                        clusters.Add(new Cluster<double[]>
                        {
                            FeatureVectors = new List<double[]> { point },
                            Centroid = (double[])point.Clone()
                        });
                    }
                }

                progress?.Report(new ComputationProgress(85, $"Merging clusters ({clusters.Count} -> {_nClusters})..."));
                cancellationToken.ThrowIfCancellationRequested();

                while (clusters.Count > _nClusters)
                {
                    ClusteringEngine.TryMergeClosestClusters(clusters,
                        (a, b) => ClusteringEngine.EuclideanDistance(a.Centroid, b.Centroid));
                }

                var labels = new int[n];
                var centroids = new double[clusters.Count][];

                for (int c = 0; c < clusters.Count; c++)
                {
                    centroids[c] = clusters[c].Centroid;
                    foreach (var point in clusters[c].FeatureVectors)
                    {
                        int idx = Array.IndexOf(input, point);
                        if (idx >= 0) labels[idx] = c;
                    }
                }

                progress?.Report(new ComputationProgress(100, "BIRCH complete."));
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
            return _nClusters;
        }

        public Func<double[][], int, int, double> GetDistanceFunction()
        {
            return (data, i, j) => ClusteringEngine.EuclideanDistance(data[i], data[j]);
        }
    }
}
