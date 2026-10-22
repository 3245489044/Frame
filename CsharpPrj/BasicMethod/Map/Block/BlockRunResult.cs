#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.Block
{
    /// <summary>整条管线执行结果（单一结果类型）。</summary>
    public sealed record BlockRunResult(
        BlockRunOutcome Outcome,
        BlockContext Context,
        IReadOnlyList<BlockDiagnostic> Diagnostics)
    {
        public int SuccessCount { get; init; }

        public int FailedCount { get; init; }

        public int SkippedCount { get; init; }

        public int AbortedCount { get; init; }

        public TimeSpan TotalDuration { get; init; }

        /// <summary>兼容旧语义：仅全部成功（含全跳过）为 true；部分成功为 false。</summary>
        public bool OverallSuccess => Outcome == BlockRunOutcome.Success;

        /// <summary>无致命失败 / 无取消（部分成功算“可接受成功”）。</summary>
        public bool IsSuccess => Outcome is BlockRunOutcome.Success or BlockRunOutcome.PartialSuccess;

        public bool IsAborted => Outcome == BlockRunOutcome.Aborted;

        public bool HasFailures => FailedCount > 0;

        public IReadOnlyList<BlockDiagnostic> Failures =>
            Diagnostics.Where(d => d.Outcome == BlockOutcome.Failed).ToArray();

        public BlockDiagnostic? FirstFailure =>
            Diagnostics.FirstOrDefault(d => d.Outcome == BlockOutcome.Failed);
    }

    /// <summary>
    /// 旧结果类型兼容壳：仅由 <see cref="ExecutionPipeline"/> 返回，属性全部转发。
    /// 新代码请使用 <see cref="BlockRunResult"/>。
    /// </summary>
    [Obsolete("使用 BlockRunResult（BlockPipeline.RunAsync）。")]
    public sealed class PipelineResult
    {
        public PipelineResult(
            bool OverallSuccess,
            BlockContext Context,
            IReadOnlyList<BlockDiagnostic> Diagnostics)
        {
            this.OverallSuccess = OverallSuccess;
            this.Context = Context ?? throw new ArgumentNullException(nameof(Context));
            this.Diagnostics = Diagnostics ?? Array.Empty<BlockDiagnostic>();
        }

        public bool OverallSuccess { get; }

        public BlockContext Context { get; }

        public IReadOnlyList<BlockDiagnostic> Diagnostics { get; }

        public int SuccessCount { get; init; }

        public int FailedCount { get; init; }

        public int SkippedCount { get; init; }

        public int AbortedCount { get; init; }

        public TimeSpan TotalDuration { get; init; }

        public BlockRunOutcome Outcome { get; init; }

        public bool IsAborted => Outcome == BlockRunOutcome.Aborted;

        public static PipelineResult From(BlockRunResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            return new PipelineResult(
                result.OverallSuccess,
                result.Context,
                result.Diagnostics)
            {
                SuccessCount = result.SuccessCount,
                FailedCount = result.FailedCount,
                SkippedCount = result.SkippedCount,
                AbortedCount = result.AbortedCount,
                TotalDuration = result.TotalDuration,
                Outcome = result.Outcome
            };
        }

        public BlockRunResult ToBlockRunResult()
            => new(Outcome, Context, Diagnostics)
            {
                SuccessCount = SuccessCount,
                FailedCount = FailedCount,
                SkippedCount = SkippedCount,
                AbortedCount = AbortedCount,
                TotalDuration = TotalDuration
            };
    }
}
