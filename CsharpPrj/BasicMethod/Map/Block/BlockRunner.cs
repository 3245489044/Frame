#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Block
{
    /// <summary>
    /// 全仓唯一执行循环：条件 / 重试 / 超时 / 兜底 / 收敛 / 记账。
    /// 任何块不得自行写 <see cref="BlockContext.StageResults"/> 改变控制流。
    /// </summary>
    public sealed class BlockRunner
    {
        public static BlockRunner Shared { get; } = new();

        public Task<BlockRunResult> RunAsync(
            IReadOnlyList<IBlock> blocks,
            BlockContext? context = null,
            CancellationToken cancellationToken = default)
            => RunAsync(blocks, context, defaultIsFatal: true, cancellationToken);

        public async Task<BlockRunResult> RunAsync(
            IReadOnlyList<IBlock> blocks,
            BlockContext? context,
            bool defaultIsFatal,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(blocks);

            context ??= new BlockContext();
            NormalizeCancellation(context, cancellationToken);

            var totalSw = System.Diagnostics.Stopwatch.StartNew();

            int successCount = 0, failedCount = 0, skippedCount = 0, abortedCount = 0;
            var fatalFailure = false;

            foreach (var block in blocks)
            {
                ArgumentNullException.ThrowIfNull(block);

                if (context.CancellationToken.IsCancellationRequested)
                {
                    var aborted = new BlockDiagnostic(
                        block.Name,
                        false,
                        TimeSpan.Zero,
                        BlockStatus.Aborted,
                        "管线已取消")
                    {
                        Outcome = BlockOutcome.Aborted
                    };

                    context.Record(aborted);
                    abortedCount++;
                    fatalFailure = true;
                    break;
                }

                var policy = ResolvePolicy(block, defaultIsFatal);
                var diagnostic = await ExecuteBlockAsync(block, policy, context).ConfigureAwait(false);

                context.Record(diagnostic);

                switch (diagnostic.Outcome)
                {
                    case BlockOutcome.Success:
                        successCount++;
                        break;
                    case BlockOutcome.Skipped:
                        skippedCount++;
                        break;
                    case BlockOutcome.Failed:
                        failedCount++;
                        if (diagnostic.Fatal && !diagnostic.Converged)
                            fatalFailure = true;
                        break;
                    case BlockOutcome.Aborted:
                        abortedCount++;
                        fatalFailure = true;
                        break;
                }

                if (diagnostic.Outcome == BlockOutcome.Aborted)
                    break;

                if (diagnostic.Outcome == BlockOutcome.Failed
                    && diagnostic.Fatal
                    && !diagnostic.Converged)
                {
                    break;
                }
            }

            totalSw.Stop();

            var overall = abortedCount > 0
                ? BlockRunOutcome.Aborted
                : fatalFailure
                    ? BlockRunOutcome.Failed
                    : failedCount > 0
                        ? BlockRunOutcome.PartialSuccess
                        : BlockRunOutcome.Success;

            return new BlockRunResult(overall, context, context.Diagnostics)
            {
                SuccessCount = successCount,
                FailedCount = failedCount,
                SkippedCount = skippedCount,
                AbortedCount = abortedCount,
                TotalDuration = totalSw.Elapsed
            };
        }

        private static void NormalizeCancellation(
            BlockContext context,
            CancellationToken cancellationToken)
        {
            if (context.CancellationToken.CanBeCanceled && cancellationToken.CanBeCanceled)
            {
                var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    context.CancellationToken,
                    cancellationToken);
                context.CancellationToken = linked.Token;
            }
            else if (cancellationToken.CanBeCanceled)
            {
                context.CancellationToken = cancellationToken;
            }
        }

        private static BlockPolicy ResolvePolicy(IBlock block, bool defaultIsFatal)
        {
            var declared = block.Policy;
            if (declared is not null && !declared.IsDefault)
                return declared;

            var legacy = BuildLegacyPolicy(block, defaultIsFatal);
            return legacy ?? (defaultIsFatal ? BlockPolicy.Default : new BlockPolicy { IsFatal = false });
        }

        private static BlockPolicy? BuildLegacyPolicy(IBlock block, bool defaultIsFatal)
        {
            IBlockCondition? condition = block as IBlockCondition;
            IBlockRetry? retry = block as IBlockRetry;

            if (condition is null && retry is null)
                return null;

            return new BlockPolicy
            {
                IsFatal = defaultIsFatal,
                CanExecute = condition is null ? null : condition.ShouldExecute,
                MaxRetries = retry?.MaxRetries ?? 0,
                RetryDelay = TimeSpan.FromMilliseconds(Math.Max(0, retry?.RetryDelayMs ?? 0)),
                ShouldRetry = retry is null
                    ? null
                    : (ctx, result, attempt) => retry.ShouldRetry(ctx, result, attempt)
            };
        }

        private static async Task<BlockDiagnostic> ExecuteBlockAsync(
            IBlock block,
            BlockPolicy policy,
            BlockContext context)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                if (policy.CanExecute is not null && !policy.CanExecute(context))
                {
                    sw.Stop();
                    return new BlockDiagnostic(
                        block.Name,
                        true,
                        sw.Elapsed,
                        BlockStatus.Skipped,
                        "条件不满足，跳过执行")
                    {
                        Outcome = BlockOutcome.Skipped
                    };
                }

                if (block is IBlockDiagnosticProvider preDiagnostic
                    && !preDiagnostic.PreDiagnostic(context))
                {
                    sw.Stop();
                    return new BlockDiagnostic(
                        block.Name,
                        true,
                        sw.Elapsed,
                        BlockStatus.Skipped,
                        "执行前诊断返回 false，跳过执行")
                    {
                        Outcome = BlockOutcome.Skipped
                    };
                }

                var result = await ExecuteWithPolicyAsync(block, policy, context).ConfigureAwait(false);
                sw.Stop();

                var diagnostic = BlockDiagnostic.From(block.Name, result, sw.Elapsed, policy.IsFatal);
                NotifyPostDiagnostic(block, context, result);
                return diagnostic;
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                sw.Stop();
                return new BlockDiagnostic(
                    block.Name,
                    false,
                    sw.Elapsed,
                    BlockStatus.Aborted,
                    "块执行被取消")
                {
                    Outcome = BlockOutcome.Aborted
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                var converged = policy.Guarded;
                return new BlockDiagnostic(
                    block.Name,
                    false,
                    sw.Elapsed,
                    BlockStatus.Failed,
                    $"未捕获的异常: {ex.Message}",
                    ex)
                {
                    Outcome = BlockOutcome.Failed,
                    Fatal = policy.IsFatal && !converged,
                    Converged = converged
                };
            }
        }

        private static async Task<BlockResult> ExecuteWithPolicyAsync(
            IBlock block,
            BlockPolicy policy,
            BlockContext context)
        {
            var maxAttempts = Math.Max(1, policy.MaxRetries + 1);
            BlockResult? last = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    return BlockResult.Abort("执行前已取消");

                var result = await ExecuteOnceAsync(block, policy, context).ConfigureAwait(false);
                result = result with { Attempts = attempt };

                if (result.Outcome is BlockOutcome.Success or BlockOutcome.Skipped)
                    return result;

                if (result.Outcome == BlockOutcome.Aborted)
                    return result;

                last = result;

                var shouldRetry = attempt < maxAttempts
                    && (policy.ShouldRetry?.Invoke(context, result, attempt - 1) ?? true);

                if (!shouldRetry)
                    break;

                var delay = policy.ResolveRetryDelay(attempt);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, context.CancellationToken).ConfigureAwait(false);
            }

            if (last is null)
                return BlockResult.Fail("未执行");

            if (policy.FallbackAsync is not null)
            {
                try
                {
                    var fallback = await policy.FallbackAsync(context, last, context.CancellationToken)
                        .ConfigureAwait(false);
                    if (fallback.Outcome is BlockOutcome.Success or BlockOutcome.Skipped)
                        return fallback with { Attempts = last.Attempts };
                    last = fallback with { Attempts = last.Attempts };
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    return BlockResult.Abort("兜底执行被取消");
                }
                catch (Exception ex)
                {
                    last = BlockResult.Fail($"兜底执行失败: {ex.Message}", ex);
                }
            }

            if (policy.Guarded && last.Outcome == BlockOutcome.Failed)
                return last with { Converged = true, Attempts = last.Attempts };

            return last;
        }

        private static async Task<BlockResult> ExecuteOnceAsync(
            IBlock block,
            BlockPolicy policy,
            BlockContext context)
        {
            if (!policy.HasTimeout)
            {
                try
                {
                    return await block.ExecuteAsync(context, context.CancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    return BlockResult.Abort("执行被取消");
                }
                catch (Exception ex)
                {
                    return BlockResult.Fail(ex.Message, ex);
                }
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                context.CancellationToken);
            timeoutCts.CancelAfter(policy.Timeout);

            try
            {
                return await block.ExecuteAsync(context, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                return BlockResult.Abort("执行被取消");
            }
            catch (OperationCanceledException)
            {
                return BlockResult.Fail(
                    $"块执行超时 ({policy.Timeout.TotalMilliseconds:F0}ms)");
            }
            catch (Exception ex)
            {
                return BlockResult.Fail(ex.Message, ex);
            }
        }

        private static void NotifyPostDiagnostic(
            IBlock block,
            BlockContext context,
            BlockResult result)
        {
            if (block is not IBlockDiagnosticProvider postDiagnostic)
                return;

            try
            {
                postDiagnostic.PostDiagnostic(context, result);
            }
            catch (Exception ex)
            {
                // 钩子异常独立诊断，不翻转步骤 Outcome（E6）。
                context.Record(new BlockDiagnostic(
                    $"{block.Name}.post",
                    false,
                    TimeSpan.Zero,
                    BlockStatus.Failed,
                    $"PostDiagnostic 钩子异常: {ex.Message}",
                    ex)
                {
                    Outcome = BlockOutcome.Failed,
                    Fatal = false
                });
            }
        }
    }
}
