#region USINGS
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace MathematicalAlgorithms
{
    public class LoopEngine
    {
        #region 骨架：并行/顺序策略选择

        private bool ShouldUseParallel(LoopShape shape, LoopConfig config)
        {
            return config.EnableParallel && shape.OuterCount > config.ParallelThreshold;
        }

        private void ReportSequentialProgress(int currentIndex, int totalCount,
            LoopConfig config, IProgress<ComputationProgress> progress)
        {
            if (config.EnableProgressReporting && (currentIndex + 1) % config.ProgressReportInterval == 0)
            {
                progress?.Report(new ComputationProgress(
                    (double)(currentIndex + 1) / totalCount * 100,
                    $"Processed {currentIndex + 1}/{totalCount} rows",
                    0, 0, currentIndex + 1, totalCount));
            }
        }

        #endregion

        #region 双层循环 - 有返回值（异步）

        public async Task<TOutput[][]> ExecuteAsync<TInput, TOutput>(
            TInput input,
            Func<TInput, int, int, TOutput> transform,
            LoopShape shape,
            LoopConfig config = null,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            config ??= LoopConfig.Default;

            var result = new TOutput[shape.OuterCount][];
            progress?.Report(new ComputationProgress(0, $"Starting loop {shape}..."));

            if (ShouldUseParallel(shape, config))
            {
                await ExecuteParallel(input, transform, shape, config, result, progress, cancellationToken);
            }
            else
            {
                await ExecuteSequential(input, transform, shape, config, result, progress, cancellationToken);
            }

            progress?.Report(new ComputationProgress(100, $"Loop {shape} completed."));
            return result;
        }

        private Task ExecuteParallel<TInput, TOutput>(
            TInput input,
            Func<TInput, int, int, TOutput> transform,
            LoopShape shape,
            LoopConfig config,
            TOutput[][] result,
            IProgress<ComputationProgress> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                };

                int processedRows = 0;
                object progressLock = new();

                Parallel.For(0, shape.OuterCount, options, i =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var row = new TOutput[shape.InnerCount];
                    for (int j = 0; j < shape.InnerCount; j++)
                    {
                        row[j] = transform(input, i, j);
                    }
                    result[i] = row;

                    if (config.EnableProgressReporting)
                    {
                        int newProcessed = Interlocked.Increment(ref processedRows);
                        ReportParallelProgress(newProcessed, shape.OuterCount, progress, progressLock);
                    }
                });
            }, cancellationToken);
        }

        private Task ExecuteSequential<TInput, TOutput>(
            TInput input,
            Func<TInput, int, int, TOutput> transform,
            LoopShape shape,
            LoopConfig config,
            TOutput[][] result,
            IProgress<ComputationProgress> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                for (int i = 0; i < shape.OuterCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var row = new TOutput[shape.InnerCount];
                    for (int j = 0; j < shape.InnerCount; j++)
                    {
                        row[j] = transform(input, i, j);
                    }
                    result[i] = row;

                    ReportSequentialProgress(i, shape.OuterCount, config, progress);
                }
            }, cancellationToken);
        }

        #endregion

        #region 双层循环 - 无返回值（异步，就地修改）

        public async Task ExecuteInPlaceAsync<TInput>(
            TInput input,
            Action<TInput, int, int> action,
            LoopShape shape,
            LoopConfig config = null,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            config ??= LoopConfig.Default;
            progress?.Report(new ComputationProgress(0, $"Starting in-place loop {shape}..."));

            if (ShouldUseParallel(shape, config))
            {
                await ExecuteInPlaceParallel(input, action, shape, config, progress, cancellationToken);
            }
            else
            {
                await ExecuteInPlaceSequential(input, action, shape, config, progress, cancellationToken);
            }

            progress?.Report(new ComputationProgress(100, $"In-place loop {shape} completed."));
        }

        private Task ExecuteInPlaceParallel<TInput>(
            TInput input,
            Action<TInput, int, int> action,
            LoopShape shape,
            LoopConfig config,
            IProgress<ComputationProgress> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                };

                int processedRows = 0;
                object progressLock = new();

                Parallel.For(0, shape.OuterCount, options, i =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    for (int j = 0; j < shape.InnerCount; j++)
                    {
                        action(input, i, j);
                    }

                    if (config.EnableProgressReporting)
                    {
                        int newProcessed = Interlocked.Increment(ref processedRows);
                        ReportParallelProgress(newProcessed, shape.OuterCount, progress, progressLock);
                    }
                });
            }, cancellationToken);
        }

        private Task ExecuteInPlaceSequential<TInput>(
            TInput input,
            Action<TInput, int, int> action,
            LoopShape shape,
            LoopConfig config,
            IProgress<ComputationProgress> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                for (int i = 0; i < shape.OuterCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    for (int j = 0; j < shape.InnerCount; j++)
                    {
                        action(input, i, j);
                    }

                    ReportSequentialProgress(i, shape.OuterCount, config, progress);
                }
            }, cancellationToken);
        }

        #endregion

        #region 分块循环（异步，大矩阵分块并行计算）

        public async Task ExecuteBlockedAsync<TInput>(
            TInput input,
            Action<TInput, int, int> action,
            LoopShape shape,
            int blockSize,
            LoopConfig config = null,
            IProgress<ComputationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            config ??= LoopConfig.Default;
            var totalBlocks = (int)Math.Ceiling((double)shape.OuterCount / blockSize);

            progress?.Report(new ComputationProgress(0, "Starting block computation...",
                0, totalBlocks, 0, shape.OuterCount));

            var blockOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            };

            int processedBlocks = 0;
            object progressLock = new();

            await Task.Run(() =>
            {
                Parallel.ForEach(Enumerable.Range(0, totalBlocks), blockOptions, blockIndex =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int startRow = blockIndex * blockSize;
                    int blockRows = Math.Min(blockSize, shape.OuterCount - startRow);

                    for (int i = 0; i < blockRows; i++)
                    {
                        int row = startRow + i;
                        for (int col = 0; col < shape.InnerCount; col++)
                        {
                            action(input, row, col);
                        }
                    }

                    if (config.EnableProgressReporting)
                    {
                        int newProcessed = Interlocked.Increment(ref processedBlocks);
                        lock (progressLock)
                        {
                            progress?.Report(new ComputationProgress(
                                (double)newProcessed / totalBlocks * 100,
                                $"Processed block {newProcessed}/{totalBlocks}",
                                newProcessed, totalBlocks,
                                Math.Min(shape.OuterCount, startRow + blockRows), shape.OuterCount));
                        }
                    }
                });
            }, cancellationToken);

            progress?.Report(new ComputationProgress(100, "Block computation completed",
                totalBlocks, totalBlocks, shape.OuterCount, shape.OuterCount));
        }

        #endregion

        #region 快捷方法（同步）

        public TOutput[][] ExecuteSync<TInput, TOutput>(
            TInput input,
            Func<TInput, int, int, TOutput> transform,
            LoopShape shape,
            LoopConfig config = null)
        {
            config ??= LoopConfig.Default;
            var result = new TOutput[shape.OuterCount][];

            if (ShouldUseParallel(shape, config))
            {
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = config.MaxDegreeOfParallelism
                };

                Parallel.For(0, shape.OuterCount, options, i =>
                {
                    var row = new TOutput[shape.InnerCount];
                    for (int j = 0; j < shape.InnerCount; j++)
                        row[j] = transform(input, i, j);
                    result[i] = row;
                });
            }
            else
            {
                for (int i = 0; i < shape.OuterCount; i++)
                {
                    var row = new TOutput[shape.InnerCount];
                    for (int j = 0; j < shape.InnerCount; j++)
                        row[j] = transform(input, i, j);
                    result[i] = row;
                }
            }

            return result;
        }

        public void ExecuteInPlaceSync<TInput>(
            TInput input,
            Action<TInput, int, int> action,
            LoopShape shape,
            LoopConfig config = null)
        {
            config ??= LoopConfig.Default;

            if (ShouldUseParallel(shape, config))
            {
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = config.MaxDegreeOfParallelism
                };

                Parallel.For(0, shape.OuterCount, options, i =>
                {
                    for (int j = 0; j < shape.InnerCount; j++)
                        action(input, i, j);
                });
            }
            else
            {
                for (int i = 0; i < shape.OuterCount; i++)
                {
                    for (int j = 0; j < shape.InnerCount; j++)
                        action(input, i, j);
                }
            }
        }

        #endregion

        #region 私有方法

        private static void ReportParallelProgress(int processed, int total,
            IProgress<ComputationProgress> progress, object progressLock)
        {
            if (processed % 100 == 0 || processed == total)
            {
                lock (progressLock)
                {
                    progress?.Report(new ComputationProgress(
                        (double)processed / total * 100,
                        $"Processed {processed}/{total} rows",
                        0, 0, processed, total));
                }
            }
        }

        #endregion
    }
}
