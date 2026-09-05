#region USINGS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public interface IClusteringAnalyzer<T>
    {
        Task<AnalysisResult<T>> AnalyzeAsync(IReadOnlyList<T> items, int maxClusters,
            IProgress<ComputationProgress> progress = null);
    }

    public interface IClusteringAlgorithm<TInput>
    {
        Task<ClusteringResult> ComputeAsync(
            TInput input,
            ClusteringComputationConfig config,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default);

        int GetExpectedClusterCount(TInput input);

        Func<TInput, int, int, double> GetDistanceFunction();
    }

    public interface IClusteringCalculator : IDisposable
    {
        Task<ClusteringResult> ComputeAsync<TInput>(
            string algorithmName,
            TInput input,
            ClusteringComputationConfig config = null,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default);

        void RegisterAlgorithm<TInput>(string name, IClusteringAlgorithm<TInput> algorithm);
        bool HasAlgorithm(string name);
        IReadOnlyCollection<string> GetAlgorithmNames();
    }
}
