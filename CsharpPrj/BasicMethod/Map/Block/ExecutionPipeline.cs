
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Block
{

    public class ExecutionPipeline : IBlockPipeline
    {
        public string Name { get; }

        public string Description { get; }

        public IReadOnlyList<IBlock> Blocks => _blocks.AsReadOnly();

        protected readonly List<IBlock> _blocks = new();

        public bool FailFast { get; set; }

        public ExecutionPipeline(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
        }

        public IBlockPipeline AddBlock(IBlock block)
        {
            ArgumentNullException.ThrowIfNull(block);
            _blocks.Add(block);
            return this;
        }

        public IBlockPipeline AddBlock<T>() where T : IBlock, new()
        {
            _blocks.Add(new T());
            return this;
        }

        public IBlockPipeline InsertBefore(string name, IBlock block)
        {
            ArgumentNullException.ThrowIfNull(block);
            var index = FindIndexByName(name);
            if (index >= 0)
                _blocks.Insert(index, block);
            else
                _blocks.Add(block);
            return this;
        }

        public IBlockPipeline InsertAfter(string name, IBlock block)
        {
            ArgumentNullException.ThrowIfNull(block);
            var index = FindIndexByName(name);
            if (index >= 0)
                _blocks.Insert(index + 1, block);
            else
                _blocks.Add(block);
            return this;
        }

        public IBlockPipeline Remove(string name)
        {
            var index = FindIndexByName(name);
            if (index >= 0)
                _blocks.RemoveAt(index);
            return this;
        }

        public void Clear() => _blocks.Clear();

        public async Task<PipelineResult> RunAsync(
            BlockContext? context = null,
            CancellationToken cancellationToken = default)
        {
            context ??= new BlockContext();

            CancellationTokenSource? linkedCts = null;
            if (context.CancellationToken.CanBeCanceled && cancellationToken.CanBeCanceled)
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    context.CancellationToken, cancellationToken);
                context.CancellationToken = linkedCts.Token;
            }
            else if (cancellationToken.CanBeCanceled)
            {
                context.CancellationToken = cancellationToken;
            }

            var totalSw = Stopwatch.StartNew();
            int successCount = 0, failedCount = 0, skippedCount = 0;

            foreach (var block in _blocks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    context.AddDiagnostic(new BlockDiagnostic(
                        block.Name, false, TimeSpan.Zero, BlockStatus.Aborted,
                        "管线已取消"));
                    break;
                }

                var diagnostic = await ExecuteBlockAsync(block, context, cancellationToken);

                context.AddDiagnostic(diagnostic);

                switch (diagnostic.Status)
                {
                    case BlockStatus.Success: successCount++; break;
                    case BlockStatus.Failed: failedCount++; break;
                    case BlockStatus.Skipped: skippedCount++; break;
                }

                if (FailFast && diagnostic.Status == BlockStatus.Failed)
                {
                    break;
                }
            }

            totalSw.Stop();

            linkedCts?.Dispose();

            return new PipelineResult(
                OverallSuccess: failedCount == 0,
                Context: context,
                Diagnostics: context.Diagnostics.AsReadOnly()
            )
            {
                SuccessCount = successCount,
                FailedCount = failedCount,
                SkippedCount = skippedCount,
                TotalDuration = totalSw.Elapsed
            };
        }

        protected virtual async Task<BlockDiagnostic> ExecuteBlockAsync(
            IBlock block,
            BlockContext context,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                if (block is IBlockCondition condition && !condition.ShouldExecute(context))
                {
                    sw.Stop();
                    return new BlockDiagnostic(
                        block.Name, true, sw.Elapsed, BlockStatus.Skipped,
                        "条件不满足，跳过执行");
                }

                if (block is IBlockDiagnosticProvider preDiag && !preDiag.PreDiagnostic(context))
                {
                    sw.Stop();
                    return new BlockDiagnostic(
                        block.Name, true, sw.Elapsed, BlockStatus.Skipped,
                        "执行前诊断返回 false，跳过执行");
                }

                BlockResult result;
                if (block is IBlockRetry retryBlock)
                {
                    result = await ExecuteWithRetryAsync(block, retryBlock, context, cancellationToken);
                }
                else
                {
                    result = await ExecuteSingleAsync(block, context, cancellationToken);
                }

                sw.Stop();

                if (block is IBlockDiagnosticProvider postDiag)
                {
                    postDiag.PostDiagnostic(context, result);
                }

                var status = result.IsSuccess ? BlockStatus.Success : BlockStatus.Failed;
                return new BlockDiagnostic(
                    block.Name, result.IsSuccess, sw.Elapsed, status,
                    result.Message, result.Exception);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new BlockDiagnostic(
                    block.Name, false, sw.Elapsed, BlockStatus.Aborted,
                    "块执行被取消");
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new BlockDiagnostic(
                    block.Name, false, sw.Elapsed, BlockStatus.Failed,
                    $"未捕获的异常: {ex.Message}", ex);
            }
        }

        private static async Task<BlockResult> ExecuteSingleAsync(
            IBlock block,
            BlockContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                return await block.ExecuteAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return BlockResult.Abort("块执行被取消");
            }
            catch (Exception ex)
            {
                return BlockResult.Fail($"块执行异常: {ex.Message}", ex);
            }
        }

        private static async Task<BlockResult> ExecuteWithRetryAsync(
            IBlock block,
            IBlockRetry retryBlock,
            BlockContext context,
            CancellationToken cancellationToken)
        {
            BlockResult result = BlockResult.Fail("未执行");

            for (int attempt = 0; attempt <= retryBlock.MaxRetries; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return BlockResult.Abort("重试过程中取消");

                if (attempt > 0)
                {
                    await Task.Delay(retryBlock.RetryDelayMs, cancellationToken);
                }

                result = await ExecuteSingleAsync(block, context, cancellationToken);

                if (result.IsSuccess)
                    return result;

                if (!retryBlock.ShouldRetry(context, result, attempt))
                    break;
            }

            return result;
        }

        private int FindIndexByName(string name)
        {
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (string.Equals(_blocks[i].Name, name, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }
    }
}
