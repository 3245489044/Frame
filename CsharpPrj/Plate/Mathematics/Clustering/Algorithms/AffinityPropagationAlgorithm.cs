#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class AffinityPropagationAlgorithm : IClusteringAlgorithm<double[][]>
    {
        private readonly int _maxIterations;
        private readonly double _damping;

        public AffinityPropagationAlgorithm(int maxIterations = 100, double damping = 0.5)
        {
            _maxIterations = maxIterations;
            _damping = damping;
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
                var features = input.Select(d => (double[])d.Clone()).ToList();

                progress?.Report(new ComputationProgress(10, "Computing similarity matrix..."));
                cancellationToken.ThrowIfCancellationRequested();

                var similarity = ClusteringEngine.ComputeNegativeDistanceMatrix(features, config);
                double median = ClusteringEngine.GetMedian(similarity);

                for (int i = 0; i < n; i++)
                    similarity[i, i] = median;

                var responsibility = new double[n, n];
                var availability = new double[n, n];

                var result = IterationSkeleton.DampedIterate(
                    (Similarity: similarity, Responsibility: responsibility, Availability: availability),
                    _maxIterations,
                    (state, iter) =>
                    {
                        var oldR = (double[,])state.Responsibility.Clone();
                        var oldA = (double[,])state.Availability.Clone();

                        ClusteringEngine.UpdateAffinityResponsibility(
                            state.Similarity, state.Availability, state.Responsibility);
                        ClusteringEngine.UpdateAffinityAvailability(
                            state.Similarity, state.Availability, state.Responsibility);

                        ClusteringEngine.ApplyDamping(state.Responsibility, oldR, _damping);
                        ClusteringEngine.ApplyDamping(state.Availability, oldA, _damping);

                        return state;
                    },
                    (state, prevState) =>
                    {
                        double maxChange = 0;
                        for (int i = 0; i < n; i++)
                            for (int j = 0; j < n; j++)
                                maxChange = Math.Max(maxChange,
                                    Math.Abs(state.Responsibility[i, j] - prevState.Responsibility[i, j]));
                        return maxChange;
                    },
                    1e-6,
                    (state) =>
                    {
                        var exemplars = new List<int>();
                        for (int i = 0; i < n; i++)
                        {
                            if (state.Responsibility[i, i] + state.Availability[i, i] > 0)
                                exemplars.Add(i);
                        }

                        if (exemplars.Count == 0)
                            exemplars.Add(0);

                        var labels = new int[n];
                        for (int i = 0; i < n; i++)
                        {
                            int bestExemplar = 0;
                            double bestSim = double.MinValue;
                            for (int j = 0; j < exemplars.Count; j++)
                            {
                                if (state.Similarity[i, exemplars[j]] > bestSim)
                                {
                                    bestSim = state.Similarity[i, exemplars[j]];
                                    bestExemplar = j;
                                }
                            }
                            labels[i] = bestExemplar;
                        }

                        var centroids = new double[exemplars.Count][];
                        for (int c = 0; c < exemplars.Count; c++)
                        {
                            var points = input.Where((_, idx) => labels[idx] == c).ToList();
                            centroids[c] = VectorOperations.CalculateCentroid(points);
                        }

                        return new ClusteringResult
                        {
                            Labels = labels,
                            Centroids = centroids,
                            Inertia = 0
                        };
                    },
                    progress,
                    "AffinityPropagation");

                progress?.Report(new ComputationProgress(100, "Affinity propagation complete."));
                return result;
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
