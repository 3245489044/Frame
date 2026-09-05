#region USINGS
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class ClusteringTransformFlow : TransformFlow<double[][], ClusteringResult>
    {
        public ClusteringTransformFlow(TransformConfig config = null)
            : base(config)
        {
        }

        #region 抽象方法实现

        protected override void ValidateInput(double[][] input)
        {
            ClusteringValidator.ValidateData(input);
        }

        protected override void CheckMemory(double[][] input)
        {
            long estimatedMemory = EstimateMemory(input);
            if (!MemoryManager.HasSufficientMemory(estimatedMemory, new ClusteringComputationConfig
            {
                EnableParallel = Config.EnableParallel,
                EnableCaching = Config.EnableCaching,
                MaxDegreeOfParallelism = Config.MaxDegreeOfParallelism,
                EnableProgressReporting = Config.EnableProgressReporting,
                MemoryLimitPercentage = Config.MemoryLimitPercentage,
                ParallelThreshold = Config.ParallelThreshold,
                CachePolicy = Config.CachePolicy,
                MemoryLimitBytes = Config.MemoryLimitBytes
            }))
            {
                throw new InsufficientMemoryException(
                    $"Insufficient memory for clustering. Estimated: {MemoryManager.FormatBytes(estimatedMemory)}",
                    estimatedMemory);
            }
        }

        protected override TransformResult<ClusteringResult> CreateResult(
            ClusteringResult output,
            Stopwatch stopwatch,
            string transformName)
        {
            stopwatch.Stop();
            var metadata = new TransformMetadata(
                TransformName: transformName,
                Timestamp: DateTime.UtcNow,
                ComputationTimeMs: stopwatch.Elapsed.TotalMilliseconds
            );
            return new TransformResult<ClusteringResult>(output, stopwatch.Elapsed, metadata);
        }

        protected override string GenerateCacheKey(string transformId, double[][] input)
        {
            return TransformCacheManager.GenerateKey($"Clustering_{transformId}", input.Length, input.GetHashCode());
        }

        #endregion

        #region 公共方法

        public async Task<TransformResult<ClusteringResult>> ComputeAsync(
            string algorithmId,
            double[][] data,
            Func<double[][], TransformConfig, IProgress<ComputationProgress>, CancellationToken, Task<ClusteringResult>> computeFunc,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return await TransformAsync(
                algorithmId,
                data,
                (input, config, p, ct) =>
                {
                    var clusteringConfig = new ClusteringComputationConfig
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
                    return computeFunc(input, config, p, ct);
                },
                progress,
                cancellationToken);
        }

        #endregion

        #region 私有方法

        private static long EstimateMemory(double[][] data)
        {
            if (data == null || data.Length == 0) return 0;
            int dim = data[0]?.Length ?? 1;
            long dataSize = (long)data.Length * dim * sizeof(double);
            long matrixSize = (long)data.Length * data.Length * sizeof(double);
            return dataSize + matrixSize;
        }

        #endregion
    }
}
