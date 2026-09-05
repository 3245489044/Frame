#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class ClusteringCalculator : IClusteringCalculator
    {
        private readonly ConcurrentDictionary<string, object> _algorithms = new();
        private readonly ClusteringFlow _flow;
        private bool _disposed;

        public ClusteringCalculator(ClusteringComputationConfig config = null)
        {
            _flow = new ClusteringFlow(config ?? ClusteringComputationConfig.Default);
        }

        public void RegisterAlgorithm<TInput>(string name, IClusteringAlgorithm<TInput> algorithm)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Algorithm name cannot be null or empty");

            _algorithms[name] = algorithm;
        }

        public bool HasAlgorithm(string name)
        {
            return _algorithms.ContainsKey(name);
        }

        public IReadOnlyCollection<string> GetAlgorithmNames()
        {
            return _algorithms.Keys.ToList().AsReadOnly();
        }

        public async Task<ClusteringResult> ComputeAsync<TInput>(
            string algorithmName,
            TInput input,
            ClusteringComputationConfig config = null,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!_algorithms.TryGetValue(algorithmName, out var algoObj))
                throw new KeyNotFoundException($"Algorithm '{algorithmName}' not registered");

            if (algoObj is IClusteringAlgorithm<TInput> algorithm)
            {
                return await algorithm.ComputeAsync(input, config ?? ClusteringComputationConfig.Default,
                    progress, cancellationToken);
            }

            throw new InvalidOperationException(
                $"Algorithm '{algorithmName}' does not support input type {typeof(TInput).Name}");
        }

        public async Task<ClusteringResult> ComputeWithFlowAsync(
            string algorithmId,
            double[][] data,
            Func<double[][], ClusteringComputationConfig, IProgress<ComputationProgress>, CancellationToken, Task<ClusteringResult>> computeFunc,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return await _flow.ComputeAsync(algorithmId, data, computeFunc, progress, cancellationToken);
        }

        public async Task<ClusteringResult> QuickComputeAsync(
            string algorithmId,
            double[][] data,
            Func<double[][], ClusteringComputationConfig, IProgress<ComputationProgress>, CancellationToken, Task<ClusteringResult>> computeFunc,
            CancellationToken cancellationToken = default)
        {
            return await _flow.QuickComputeAsync(algorithmId, data, computeFunc, cancellationToken);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _flow?.Dispose();
                _algorithms.Clear();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
