#region USINGS
using MathNet.Numerics.LinearAlgebra;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class MatrixTransformFlow : TransformFlow<MatrixTransformInput, Matrix<double>>
    {
        public MatrixTransformFlow(TransformConfig config = null)
            : base(config)
        {
        }

        #region 抽象方法实现

        protected override void ValidateInput(MatrixTransformInput input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (input.CellFunction == null)
                throw new ArgumentNullException(nameof(input.CellFunction));
            if (input.Shape == null)
                throw new ArgumentNullException(nameof(input.Shape));
        }

        protected override void CheckMemory(MatrixTransformInput input)
        {
            MemoryCheckSkeleton.EnsureSufficientMemory(input.Shape, new MatrixComputationConfig
            {
                EnableParallel = Config.EnableParallel,
                EnableCaching = Config.EnableCaching,
                MaxDegreeOfParallelism = Config.MaxDegreeOfParallelism,
                EnableProgressReporting = Config.EnableProgressReporting,
                ChunkSize = Config.ChunkSize,
                MemoryLimitPercentage = Config.MemoryLimitPercentage,
                ParallelThreshold = Config.ParallelThreshold,
                CachePolicy = Config.CachePolicy,
                MemoryLimitBytes = Config.MemoryLimitBytes
            });
        }

        protected override TransformResult<Matrix<double>> CreateResult(
            Matrix<double> output,
            Stopwatch stopwatch,
            string transformName)
        {
            stopwatch.Stop();
            var shape = new MatrixShape(output.RowCount, output.ColumnCount);
            var metadata = new TransformMetadata(
                TransformName: transformName,
                Timestamp: DateTime.UtcNow,
                ComputationTimeMs: stopwatch.Elapsed.TotalMilliseconds,
                MemoryUsageMB: shape.EstimatedMemoryBytes / (1024.0 * 1024.0)
            );
            return new TransformResult<Matrix<double>>(output, stopwatch.Elapsed, metadata);
        }

        protected override string GenerateCacheKey(string transformId, MatrixTransformInput input)
        {
            var inputStr = input.Input?.ToString() ?? "null";
            var inputHash = inputStr.GetHashCode(StringComparison.Ordinal);
            return $"Matrix_{transformId}_{inputHash:X8}";
        }

        #endregion

        #region 公共方法

        public async Task<TransformResult<Matrix<double>>> ComputeAsync<TInput>(
            string algorithmId,
            TInput input,
            Func<TInput, int, int, double> cellFunction,
            MatrixShape shape,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var transformInput = new MatrixTransformInput
            {
                Input = input,
                CellFunction = (ctx, i, j) => cellFunction((TInput)ctx, i, j),
                Shape = shape
            };

            return await TransformAsync(
                algorithmId,
                transformInput,
                (ctx, config, p, ct) => ExecuteMatrixCompute(ctx, config, p, ct),
                progress,
                cancellationToken);
        }

        public async Task<TransformResult<Matrix<double>>> ComputeAsync<TInput>(
            string algorithmId,
            TInput input,
            IMatrixAlgorithm<TInput> algorithm,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var shape = algorithm.GetExpectedShape(input);
            var cellFunction = algorithm.GetCellFunction();
            return await ComputeAsync(algorithmId, input, cellFunction, shape, progress, cancellationToken);
        }

        #endregion

        #region 私有方法

        private async Task<Matrix<double>> ExecuteMatrixCompute(
            MatrixTransformInput ctx,
            TransformConfig config,
            IProgress<ComputationProgress> progress,
            CancellationToken cancellationToken)
        {
            var matrixConfig = new MatrixComputationConfig
            {
                EnableParallel = config.EnableParallel,
                EnableCaching = config.EnableCaching,
                MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
                EnableProgressReporting = config.EnableProgressReporting,
                ChunkSize = config.ChunkSize,
                MemoryLimitPercentage = config.MemoryLimitPercentage,
                ParallelThreshold = config.ParallelThreshold,
                CachePolicy = config.CachePolicy,
                MemoryLimitBytes = config.MemoryLimitBytes
            };

            bool useBlocked = config.ChunkSize > 0
                && ctx.Shape.Rows > config.ChunkSize * 2
                && ctx.Shape.Columns > config.ChunkSize * 2;

            if (useBlocked)
            {
                return await MatrixEngine.ComputeBlockedAsync(
                    ctx.Input, ctx.CellFunction, ctx.Shape, matrixConfig,
                    progress, cancellationToken);
            }
            else
            {
                return await MatrixEngine.ComputeAsync(
                    ctx.Input, ctx.CellFunction, ctx.Shape, matrixConfig,
                    progress, cancellationToken);
            }
        }

        #endregion

        #region 静态工厂方法

        private static TransformResult<Matrix<double>> CreateMatrix<TInput>(
            string name,
            TInput input,
            Func<TInput, int, int, double> cellFunc,
            MatrixShape shape)
        {
            var flow = new MatrixTransformFlow(new TransformConfig { EnableCaching = false });
            return RunSync(() => flow.ComputeAsync(name, input, cellFunc, shape));
        }

        public static TransformResult<Matrix<double>> CreateIdentity(int size)
        {
            return CreateMatrix(
                "IdentityMatrix",
                size,
                (s, i, j) => i == j ? 1.0 : 0.0,
                new MatrixShape(size, size));
        }

        public static TransformResult<Matrix<double>> CreateRandom(int rows, int cols, int? seed = null)
        {
            var random = seed.HasValue ? new Random(seed.Value) : new Random();
            return CreateMatrix(
                "RandomMatrix",
                (Rows: rows, Cols: cols, Random: random),
                (input, i, j) => input.Random.NextDouble(),
                new MatrixShape(rows, cols));
        }

        public static TransformResult<Matrix<double>> CreateOnes(int rows, int cols)
        {
            return CreateMatrix(
                "OnesMatrix",
                (Rows: rows, Cols: cols),
                (input, i, j) => 1.0,
                new MatrixShape(rows, cols));
        }

        public static TransformResult<Matrix<double>> CreateDistanceMatrix(int size, double scale = 1.0)
        {
            return CreateMatrix(
                "DistanceMatrix",
                (Size: size, Scale: scale),
                (input, i, j) => Math.Exp(-Math.Pow(i - j, 2) / input.Scale),
                new MatrixShape(size, size));
        }

        #endregion
    }

    public class MatrixTransformInput
    {
        public object Input { get; set; }
        public Func<object, int, int, double> CellFunction { get; set; }
        public MatrixShape Shape { get; set; }
    }
}
