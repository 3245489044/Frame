#nullable enable

using System;

namespace BasicMethod.Block
{
    /// <summary>
    /// 阶段账本条目。位置参数保持旧签名以兼容既有读取方；
    /// 新字段以 init 属性追加，不破坏构造。
    /// </summary>
    public sealed record BlockDiagnostic(
        string BlockName,
        bool Success,
        TimeSpan Duration,
        BlockStatus Status,
        string? Message = null,
        Exception? Exception = null)
    {
        private readonly BlockOutcome? _outcome;

        /// <summary>四态结论；未显式设置时由 <see cref="Status"/> 推导。</summary>
        public BlockOutcome Outcome
        {
            get => _outcome ?? Status switch
            {
                BlockStatus.Success => BlockOutcome.Success,
                BlockStatus.Skipped => BlockOutcome.Skipped,
                BlockStatus.Aborted => BlockOutcome.Aborted,
                _ => BlockOutcome.Failed
            };
            init => _outcome = value;
        }

        public int Attempts { get; init; } = 1;

        /// <summary>失败是否为致命（由策略决定；非失败恒为 false）。</summary>
        public bool Fatal { get; init; }

        /// <summary>失败是否已收敛（Guard）。</summary>
        public bool Converged { get; init; }

        public static BlockDiagnostic From(
            string blockName,
            BlockResult result,
            TimeSpan duration,
            bool fatal)
        {
            var status = result.Outcome switch
            {
                BlockOutcome.Success => BlockStatus.Success,
                BlockOutcome.Skipped => BlockStatus.Skipped,
                BlockOutcome.Aborted => BlockStatus.Aborted,
                _ => BlockStatus.Failed
            };

            return new BlockDiagnostic(
                blockName,
                result.IsSuccess,
                duration,
                status,
                result.Message,
                result.Error)
            {
                Outcome = result.Outcome,
                Attempts = result.Attempts,
                Fatal = result.Outcome == BlockOutcome.Failed && fatal,
                Converged = result.Converged
            };
        }
    }
}
