#region USINGS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class ClusteringFlow
    {
        private readonly ClusteringTransformFlow _transformFlow;
        private readonly ClusteringComputationConfig _config;

        public ClusteringFlow(ClusteringComputationConfig config = null)
        {
            _config = config ?? ClusteringComputationConfig.Default;
            _transformFlow = new ClusteringTransformFlow(ConvertConfig(_config));
        }

        public async Task<ClusteringResult> ComputeAsync(
            string algorithmId,
            double[][] data,
            Func<double[][], ClusteringComputationConfig, IProgress<ComputationProgress>, CancellationToken, Task<ClusteringResult>> computeFunc,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var transformResult = await _transformFlow.ComputeAsync(
                algorithmId,
                data,
                (input, config, p, ct) => computeFunc(input, _config, p, ct),
                progress,
                cancellationToken);

            return ConvertToClusteringResult(transformResult);
        }

        public async Task<ClusteringResult> QuickComputeAsync(
            string algorithmId,
            double[][] data,
            Func<double[][], ClusteringComputationConfig, IProgress<ComputationProgress>, CancellationToken, Task<ClusteringResult>> computeFunc,
            CancellationToken cancellationToken = default)
        {
            var transformResult = await _transformFlow.QuickTransformAsync(
                algorithmId,
                data,
                (input, config, p, ct) => computeFunc(input, _config, p, ct),
                cancellationToken);

            return ConvertToClusteringResult(transformResult);
        }

        public void Dispose()
        {
            _transformFlow?.Dispose();
        }

        #region 私有方法

        private static TransformConfig ConvertConfig(ClusteringComputationConfig config)
        {
            return new TransformConfig
            {
                EnableParallel = config.EnableParallel,
                EnableCaching = config.EnableCaching,
                MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
                EnableProgressReporting = config.EnableProgressReporting,
                MemoryLimitPercentage = config.MemoryLimitPercentage,
                ParallelThreshold = config.ParallelThreshold,
                CachePolicy = config.CachePolicy,
                MemoryLimitBytes = config.MemoryLimitBytes
            };
        }

        private static ClusteringResult ConvertToClusteringResult(
            TransformResult<ClusteringResult> transformResult)
        {
            return transformResult.Output;
        }

        #endregion
    }
}
