#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Block
{
    /// <summary>重试退避策略（Circuit 指数退避对齐）。</summary>
    public enum BlockRetryBackoff
    {
        Fixed,
        Linear,
        Exponential
    }

    /// <summary>
    /// 块策略：条件 / 致命性 / 重试 / 超时 / 收敛（Guard）/ 失败兜底。
    /// 唯一消费方 = <see cref="BlockRunner"/>。
    /// </summary>
    public sealed record BlockPolicy
    {
        /// <summary>默认策略：失败致命（新内核语义）。旧 <see cref="ExecutionPipeline"/> 以 FailFast 覆盖。</summary>
        public static BlockPolicy Default { get; } = new();

        public bool IsFatal { get; init; } = true;

        /// <summary>执行前条件；返回 false = Skipped。</summary>
        public Func<BlockContext, bool>? CanExecute { get; init; }

        public int MaxRetries { get; init; }

        public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(200);

        public BlockRetryBackoff RetryBackoff { get; init; } = BlockRetryBackoff.Fixed;

        public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(10);

        /// <summary>额外重试判定（缺省 = 失败即重试）。</summary>
        public Func<BlockContext, BlockResult, int, bool>? ShouldRetry { get; init; }

        /// <summary>单次尝试超时（<= 0 = 不启用）。</summary>
        public TimeSpan Timeout { get; init; }

        /// <summary>失败收敛：账本保留 Failed，但执行继续（原 Circuit Guard）。</summary>
        public bool Guarded { get; init; }

        /// <summary>
        /// 重试耗尽后的失败兜底（原 Circuit Fallback/CatchRoute）。
        /// 由内核在重试链之后调用，保证重试实现唯一。
        /// </summary>
        public Func<BlockContext, BlockResult, CancellationToken, Task<BlockResult>>? FallbackAsync { get; init; }

        public bool HasRetry => MaxRetries > 0;

        public bool HasTimeout => Timeout > TimeSpan.Zero;

        public bool IsDefault => ReferenceEquals(this, Default);

        /// <summary>计算第 attempt 次重试前的等待时长（attempt 从 1 开始）。</summary>
        public TimeSpan ResolveRetryDelay(int attempt)
        {
            if (attempt <= 0)
                return TimeSpan.Zero;

            var baseMs = Math.Max(0, RetryDelay.TotalMilliseconds);
            var delayMs = RetryBackoff switch
            {
                BlockRetryBackoff.Linear => baseMs * attempt,
                BlockRetryBackoff.Exponential => baseMs * Math.Pow(2, Math.Min(attempt - 1, 30)),
                _ => baseMs
            };

            var capMs = Math.Max(0, MaxRetryDelay.TotalMilliseconds);
            if (capMs > 0 && delayMs > capMs)
                delayMs = capMs;

            return delayMs <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(delayMs);
        }
    }
}
