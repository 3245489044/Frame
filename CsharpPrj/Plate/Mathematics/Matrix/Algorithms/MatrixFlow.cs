#region USINGS
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class MatrixComputationFlow
    {
        private readonly MatrixTransformFlow _transformFlow;
        private readonly MatrixComputationConfig _config;

        public MatrixComputationFlow(MatrixComputationConfig config = null)
        {
            _config = config ?? MatrixComputationConfig.Default;
            _transformFlow = new MatrixTransformFlow(ConvertConfig(_config));
        }

        public async Task<MatrixResult> ComputeAsync<TInput>(
            string algorithmId,
            TInput input,
            Func<TInput, int, int, double> cellFunction,
            MatrixShape shape,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var transformResult = await _transformFlow.ComputeAsync(
                algorithmId, input, cellFunction, shape, progress, cancellationToken);

            return ConvertToMatrixResult(transformResult);
        }

        public async Task<MatrixResult> QuickComputeAsync<TInput>(
            string algorithmId,
            TInput input,
            Func<TInput, int, int, double> cellFunction,
            MatrixShape shape,
            CancellationToken cancellationToken = default)
        {
            var transformResult = await _transformFlow.QuickTransformAsync(
                algorithmId,
                new MatrixTransformInput
                {
                    Input = input,
                    CellFunction = (ctx, i, j) => cellFunction((TInput)ctx, i, j),
                    Shape = shape
                },
                (ctx, config, p, ct) => ExecuteMatrixCompute(ctx, config, p, ct),
                cancellationToken);

            return ConvertToMatrixResult(transformResult);
        }

        public async Task<MatrixResult> ComputeAsync<TInput>(
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

        public void Dispose()
        {
            _transformFlow?.Dispose();
        }

        #region 私有方法

        private async Task<MathNet.Numerics.LinearAlgebra.Matrix<double>> ExecuteMatrixCompute(
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

        private static TransformConfig ConvertConfig(MatrixComputationConfig config)
        {
            return new TransformConfig
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
        }

        private static MatrixResult ConvertToMatrixResult(TransformResult<MathNet.Numerics.LinearAlgebra.Matrix<double>> transformResult)
        {
            var shape = new MatrixShape(
                transformResult.Output.RowCount,
                transformResult.Output.ColumnCount);

            var metadata = new AlgorithmMetadata(
                AlgorithmName: transformResult.Metadata.TransformName,
                Timestamp: transformResult.Metadata.Timestamp,
                OutputShape: shape,
                ComputationTimeMs: transformResult.Metadata.ComputationTimeMs,
                MemoryUsageMB: transformResult.Metadata.MemoryUsageMB
            );

            return new MatrixResult(
                transformResult.Output,
                transformResult.ComputationTime,
                metadata,
                transformResult.IsCached);
        }

        #endregion

        #region 静态工厂方法

        private static MatrixResult CreateMatrix<TInput>(
            string name,
            TInput input,
            Func<TInput, int, int, double> cellFunc,
            MatrixShape shape)
        {
            var flow = new MatrixComputationFlow(new MatrixComputationConfig { EnableCaching = false });
            return RunSync(() => flow.ComputeAsync(name, input, cellFunc, shape));
        }

        private static TResult RunSync<TResult>(Func<Task<TResult>> asyncFunc)
        {
            return Task.Run(async () => await asyncFunc()).GetAwaiter().GetResult();
        }

        public static MatrixResult CreateIdentity(int size)
        {
            return CreateMatrix(
                "IdentityMatrix",
                size,
                (s, i, j) => i == j ? 1.0 : 0.0,
                new MatrixShape(size, size));
        }

        public static MatrixResult CreateRandom(int rows, int cols, int? seed = null)
        {
            var random = seed.HasValue ? new Random(seed.Value) : new Random();
            return CreateMatrix(
                "RandomMatrix",
                (Rows: rows, Cols: cols, Random: random),
                (input, i, j) => input.Random.NextDouble(),
                new MatrixShape(rows, cols));
        }

        public static MatrixResult CreateOnes(int rows, int cols)
        {
            return CreateMatrix(
                "OnesMatrix",
                (Rows: rows, Cols: cols),
                (input, i, j) => 1.0,
                new MatrixShape(rows, cols));
        }

        public static MatrixResult CreateDistanceMatrix(int size, double scale = 1.0)
        {
            return CreateMatrix(
                "DistanceMatrix",
                (Size: size, Scale: scale),
                (input, i, j) => Math.Exp(-Math.Pow(i - j, 2) / input.Scale),
                new MatrixShape(size, size));
        }

        #endregion
    }
}
