#region USINGS
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public abstract class TransformFlow<TInput, TOutput>
    {
        private readonly TransformConfig _config;
        private readonly TransformCacheManager _cache;

        protected TransformConfig Config => _config;

        protected TransformCacheManager Cache => _cache;

        protected TransformFlow(TransformConfig config = null)
        {
            _config = config ?? TransformConfig.Default;
            _cache = _config.EnableCaching ? new TransformCacheManager(
                _config.MaxCacheSizeBytes,
                _config.CleanupInterval) : null;
        }

        #region 抽象方法（子类必须实现）

        protected abstract void ValidateInput(TInput input);

        protected abstract void CheckMemory(TInput input);

        protected abstract TransformResult<TOutput> CreateResult(
            TOutput output,
            Stopwatch stopwatch,
            string transformName);

        protected abstract string GenerateCacheKey(string transformId, TInput input);

        #endregion

        #region 骨架：核心计算流程

        protected async Task<TransformResult<TOutput>> ExecuteCoreAsync(
            string transformId,
            TInput input,
            Func<TInput, TransformConfig, IProgress<ComputationProgress>, CancellationToken, Task<TOutput>> computeFunc,
            bool enableCaching,
            bool enableProgressReporting,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateInput(input);

            progress?.Report(new ComputationProgress(0, $"Starting {transformId}..."));

            if (enableCaching && _cache != null)
            {
                progress?.Report(new ComputationProgress(5, "Checking cache..."));
                var cacheKey = GenerateCacheKey(transformId, input);
                if (_cache.TryGet<TOutput>(cacheKey, out var cachedOutput))
                {
                    progress?.Report(new ComputationProgress(100, "Cache hit - returning cached result."));
                    return new TransformResult<TOutput>(
                        cachedOutput,
                        TimeSpan.Zero,
                        new TransformMetadata(TransformName: transformId),
                        IsCached: true);
                }
            }

            progress?.Report(new ComputationProgress(10, "Checking memory..."));
            CheckMemory(input);

            progress?.Report(new ComputationProgress(20, "Executing computation..."));
            var stopwatch = Stopwatch.StartNew();

            var output = await computeFunc(input, _config, progress, cancellationToken);

            stopwatch.Stop();

            progress?.Report(new ComputationProgress(95, "Aggregating results..."));
            var result = CreateResult(output, stopwatch, transformId);

            if (enableCaching && _cache != null)
            {
                var cacheKey = GenerateCacheKey(transformId, input);
                _cache.Set(cacheKey, output, _config.CachePolicy.AbsoluteExpiration);
            }

            progress?.Report(new ComputationProgress(100, "Computation complete."));
            return result;
        }

        #endregion

        #region 骨架：安全同步执行

        protected static TResult RunSync<TResult>(Func<Task<TResult>> asyncFunc)
        {
            return Task.Run(async () => await asyncFunc()).GetAwaiter().GetResult();
        }

        #endregion

        public async Task<TransformResult<TOutput>> TransformAsync(
            string transformId,
            TInput input,
            Func<TInput, TransformConfig, IProgress<ComputationProgress>, CancellationToken, Task<TOutput>> computeFunc,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteCoreAsync(
                transformId, input, computeFunc,
                enableCaching: true,
                enableProgressReporting: true,
                progress, cancellationToken);
        }

        public async Task<TransformResult<TOutput>> QuickTransformAsync(
            string transformId,
            TInput input,
            Func<TInput, TransformConfig, IProgress<ComputationProgress>, CancellationToken, Task<TOutput>> computeFunc,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteCoreAsync(
                transformId, input, computeFunc,
                enableCaching: false,
                enableProgressReporting: false,
                null, cancellationToken);
        }

        public void Dispose()
        {
            _cache?.Dispose();
        }
    }
}
