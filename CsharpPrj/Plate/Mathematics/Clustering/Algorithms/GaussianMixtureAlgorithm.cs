#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class GaussianMixtureAlgorithm : IClusteringAlgorithm<double[][]>
    {
        private readonly int _nComponents;
        private readonly int _maxIterations;

        public GaussianMixtureAlgorithm(int nComponents, int maxIterations = 50)
        {
            _nComponents = nComponents;
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

                progress?.Report(new ComputationProgress(5, "Initializing with K-Means..."));
                cancellationToken.ThrowIfCancellationRequested();

                var kmeans = new KMeansAlgorithm(_nComponents, numAttempts: 1);
                var kmeansResult = kmeans.ComputeAsync(input, config, null, cancellationToken).GetAwaiter().GetResult();
                var kmeansClusters = ClusteringEngine.BuildClusters<double[]>(
                    input.Select(d => (double[])d.Clone()).ToList(),
                    kmeansResult.Centroids, kmeansResult.Labels, _nComponents);

                var weights = new double[_nComponents];
                var means = new double[_nComponents][];
                var covariances = new double[_nComponents][,];

                for (int i = 0; i < _nComponents; i++)
                {
                    weights[i] = (double)kmeansClusters[i].FeatureVectors.Count / n;
                    means[i] = kmeansClusters[i].Centroid;
                    covariances[i] = new double[dim, dim];
                    for (int j = 0; j < dim; j++)
                        covariances[i][j, j] = 1.0;
                }

                var responsibilities = new double[n, _nComponents];

                var result = IterationSkeleton.Iterate(
                    (Weights: weights, Means: means, Covariances: covariances, Responsibilities: responsibilities),
                    _maxIterations,
                    (state, iter) =>
                    {
                        for (int i = 0; i < n; i++)
                        {
                            double total = 0;
                            for (int k = 0; k < _nComponents; k++)
                            {
                                state.Responsibilities[i, k] = state.Weights[k] *
                                    ClusteringEngine.GaussianPDF(input[i], state.Means[k], state.Covariances[k]);
                                total += state.Responsibilities[i, k];
                            }
                            if (total > 0)
                            {
                                for (int k = 0; k < _nComponents; k++)
                                    state.Responsibilities[i, k] /= total;
                            }
                        }

                        for (int k = 0; k < _nComponents; k++)
                        {
                            double nk = 0;
                            for (int i = 0; i < n; i++)
                                nk += state.Responsibilities[i, k];

                            if (nk > 0)
                            {
                                state.Weights[k] = nk / n;
                                for (int j = 0; j < dim; j++)
                                {
                                    state.Means[k][j] = 0;
                                    for (int i = 0; i < n; i++)
                                        state.Means[k][j] += state.Responsibilities[i, k] * input[i][j];
                                    state.Means[k][j] /= nk;
                                }
                            }
                        }

                        return state;
                    },
                    (state) => false,
                    (state) =>
                    {
                        var labels = new int[n];
                        for (int i = 0; i < n; i++)
                        {
                            int bestK = 0;
                            double maxResp = 0;
                            for (int k = 0; k < _nComponents; k++)
                            {
                                if (state.Responsibilities[i, k] > maxResp)
                                {
                                    maxResp = state.Responsibilities[i, k];
                                    bestK = k;
                                }
                            }
                            labels[i] = bestK;
                        }

                        return new ClusteringResult
                        {
                            Labels = labels,
                            Centroids = state.Means,
                            Inertia = 0
                        };
                    },
                    progress,
                    "GaussianMixture");

                progress?.Report(new ComputationProgress(100, "Gaussian mixture complete."));
                return result;
            }, cancellationToken);
        }

        public int GetExpectedClusterCount(double[][] input)
        {
            return _nComponents;
        }

        public Func<double[][], int, int, double> GetDistanceFunction()
        {
            return (data, i, j) => ClusteringEngine.EuclideanDistance(data[i], data[j]);
        }
    }
}
