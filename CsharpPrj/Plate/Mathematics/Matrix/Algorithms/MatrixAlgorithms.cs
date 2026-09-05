#region USINGS
using MathNet.Numerics.LinearAlgebra;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    internal static class AlgorithmResultSkeleton
    {
        public static MatrixResult CreateResult(
            Matrix<double> matrix,
            Stopwatch stopwatch,
            string algorithmName,
            MatrixShape shape)
        {
            stopwatch.Stop();
            var metadata = new AlgorithmMetadata(
                AlgorithmName: algorithmName,
                Timestamp: DateTime.UtcNow,
                OutputShape: shape,
                ComputationTimeMs: stopwatch.Elapsed.TotalMilliseconds,
                MemoryUsageMB: shape.EstimatedMemoryBytes / (1024.0 * 1024.0)
            );
            return new MatrixResult(matrix, stopwatch.Elapsed, metadata);
        }
    }

    public class FunctionalMatrixAlgorithm<TInput> : IMatrixAlgorithm<TInput>
    {
        private readonly Func<TInput, MatrixShape> _shapeProvider;
        private readonly Func<TInput, int, int, double> _cellFunction;
        private readonly string _algorithmName;

        public FunctionalMatrixAlgorithm(
            Func<TInput, MatrixShape> shapeProvider,
            Func<TInput, int, int, double> cellFunction,
            string algorithmName = null)
        {
            _shapeProvider = shapeProvider ?? throw new ArgumentNullException(nameof(shapeProvider));
            _cellFunction = cellFunction ?? throw new ArgumentNullException(nameof(cellFunction));
            _algorithmName = algorithmName ?? GetType().Name;
        }

        public async Task<MatrixResult> ComputeAsync(
            TInput input,
            MatrixComputationConfig config,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var shape = GetExpectedShape(input);

            MemoryCheckSkeleton.EnsureSufficientMemory(shape, config);

            var stopwatch = Stopwatch.StartNew();
            var matrix = await MatrixEngine.ComputeAsync(input, _cellFunction, shape, config, progress, cancellationToken);

            return AlgorithmResultSkeleton.CreateResult(matrix, stopwatch, _algorithmName, shape);
        }

        public MatrixShape GetExpectedShape(TInput input) => _shapeProvider(input);
        public Func<TInput, int, int, double> GetCellFunction() => _cellFunction;
    }

    public class BlockMatrixAlgorithm<TInput> : IMatrixAlgorithm<TInput>
    {
        private readonly IMatrixAlgorithm<TInput> _baseAlgorithm;
        private readonly int _blockSize;

        public BlockMatrixAlgorithm(
            IMatrixAlgorithm<TInput> baseAlgorithm,
            int blockSize = 1000)
        {
            _baseAlgorithm = baseAlgorithm ?? throw new ArgumentNullException(nameof(baseAlgorithm));
            _blockSize = Math.Max(1, blockSize);
        }

        public async Task<MatrixResult> ComputeAsync(
            TInput input,
            MatrixComputationConfig config,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var shape = GetExpectedShape(input);
            var cellFunction = _baseAlgorithm.GetCellFunction();

            MemoryCheckSkeleton.EnsureSufficientMemory(shape, config);

            var stopwatch = Stopwatch.StartNew();
            var blockedConfig = new MatrixComputationConfig
            {
                EnableParallel = config.EnableParallel,
                EnableCaching = config.EnableCaching,
                MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
                EnableProgressReporting = config.EnableProgressReporting,
                ChunkSize = _blockSize,
                MemoryLimitPercentage = config.MemoryLimitPercentage,
                ParallelThreshold = config.ParallelThreshold,
                CachePolicy = config.CachePolicy,
                MemoryLimitBytes = config.MemoryLimitBytes
            };
            var matrix = await MatrixEngine.ComputeBlockedAsync(input, cellFunction, shape, blockedConfig, progress, cancellationToken);

            return AlgorithmResultSkeleton.CreateResult(matrix, stopwatch,
                $"{_baseAlgorithm.GetType().Name} (Blocked)", shape);
        }

        public MatrixShape GetExpectedShape(TInput input) => _baseAlgorithm.GetExpectedShape(input);
        public Func<TInput, int, int, double> GetCellFunction() => _baseAlgorithm.GetCellFunction();
    }

    internal static class MemoryCheckSkeleton
    {
        public static void EnsureSufficientMemory(MatrixShape shape, MatrixComputationConfig config)
        {
            if (!MemoryManager.HasSufficientMemory(shape.EstimatedMemoryBytes, config))
            {
                throw new InsufficientMemoryException(
                    $"Insufficient memory for {shape} matrix. " +
                    $"Estimated: {MemoryManager.FormatBytes(shape.EstimatedMemoryBytes)}",
                    shape.EstimatedMemoryBytes);
            }
        }
    }
}
