#region USINGS
using MathNet.Numerics.LinearAlgebra;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public static class MatrixEngine
    {
        private static readonly LoopEngine _loopEngine = new();

        private static LoopConfig CreateLoopConfig(MatrixComputationConfig config)
        {
            return new LoopConfig
            {
                EnableParallel = config.EnableParallel,
                MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
                EnableProgressReporting = config.EnableProgressReporting,
                ParallelThreshold = config.ParallelThreshold,
                ProgressReportInterval = 100
            };
        }

        public static async Task<Matrix<double>> ComputeAsync<TInput>(
            TInput input,
            Func<TInput, int, int, double> cellFunction,
            MatrixShape shape,
            MatrixComputationConfig config,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var matrix = Matrix<double>.Build.Dense(shape.Rows, shape.Columns);
            progress?.Report(new ComputationProgress(0, "Starting computation...", 0, 0, 0, shape.Rows));

            var loopConfig = CreateLoopConfig(config);

            await _loopEngine.ExecuteInPlaceAsync(
                (Input: input, Matrix: matrix, Func: cellFunction),
                (ctx, i, j) => ctx.Matrix[i, j] = ctx.Func(ctx.Input, i, j),
                new LoopShape(shape.Rows, shape.Columns),
                loopConfig,
                progress,
                cancellationToken);

            progress?.Report(new ComputationProgress(100, "Computation completed", 0, 0, shape.Rows, shape.Rows));
            return matrix;
        }

        public static async Task<Matrix<double>> ComputeBlockedAsync<TInput>(
            TInput input,
            Func<TInput, int, int, double> cellFunction,
            MatrixShape shape,
            MatrixComputationConfig config,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var matrix = Matrix<double>.Build.Dense(shape.Rows, shape.Columns);
            var loopConfig = CreateLoopConfig(config);

            progress?.Report(new ComputationProgress(0, "Starting blocked computation...", 0, 0, 0, shape.Rows));

            await _loopEngine.ExecuteBlockedAsync(
                (Input: input, Matrix: matrix, Func: cellFunction),
                (ctx, i, j) => ctx.Matrix[i, j] = ctx.Func(ctx.Input, i, j),
                new LoopShape(shape.Rows, shape.Columns),
                config.ChunkSize,
                loopConfig,
                progress,
                cancellationToken);

            progress?.Report(new ComputationProgress(100, "Blocked computation completed", 0, 0, shape.Rows, shape.Rows));
            return matrix;
        }
    }
}
