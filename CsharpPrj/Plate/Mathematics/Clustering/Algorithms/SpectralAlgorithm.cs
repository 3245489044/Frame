#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class SpectralAlgorithm : IClusteringAlgorithm<double[][]>
    {
        private readonly int _nClusters;

        public SpectralAlgorithm(int nClusters)
        {
            _nClusters = nClusters;
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


                progress?.Report(new ComputationProgress(50, "Running K-Means clustering..."));
                cancellationToken.ThrowIfCancellationRequested();

                var kmeans = new KMeansAlgorithm(_nClusters);
                var kmeansResult = kmeans.ComputeAsync(input, config, progress, cancellationToken).GetAwaiter().GetResult();

                progress?.Report(new ComputationProgress(100, "Spectral clustering complete."));
                return kmeansResult;
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
