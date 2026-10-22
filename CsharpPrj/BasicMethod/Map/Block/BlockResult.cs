#nullable enable

using System;

namespace BasicMethod.Block
{
    /// <summary>
    /// 块执行结果。兼容旧工厂（<see cref="Ok"/>/<see cref="Fail"/>/<see cref="Skip"/>/<see cref="Abort"/>），
    /// 新增四态 <see cref="Outcome"/> 与“已收敛失败”（<see cref="Converged"/>）。
    /// </summary>
    public sealed record BlockResult(
        BlockOutcome Outcome,
        string? Message = null,
        Exception? Error = null,
        int Attempts = 1,
        bool Converged = false)
    {
        /// <summary>兼容旧属性：成功或跳过均视为“非失败”。</summary>
        public bool IsSuccess => Outcome is BlockOutcome.Success or BlockOutcome.Skipped;

        public bool IsSkipped => Outcome == BlockOutcome.Skipped;

        public bool IsAborted => Outcome == BlockOutcome.Aborted;

        /// <summary>兼容旧属性名（旧记录使用 Exception）。</summary>
        public Exception? Exception => Error;

        public static BlockResult Ok(string? message = null)
            => new(BlockOutcome.Success, message ?? "执行成功");

        public static BlockResult Fail(
            string? message = null,
            Exception? exception = null,
            bool converged = false)
            => new(BlockOutcome.Failed, message ?? "执行失败", exception, 1, converged);

        public static BlockResult Skip(string? message = null)
            => new(BlockOutcome.Skipped, message ?? "已跳过");

        public static BlockResult Abort(string? message = null)
            => new(BlockOutcome.Aborted, message ?? "已中止");

        /// <summary>已收敛失败：账本保留失败事实，但执行可继续（原 Circuit Guard 语义）。</summary>
        public static BlockResult ConvergedFailure(
            string? message = null,
            Exception? exception = null)
            => new(BlockOutcome.Failed, message ?? "失败已收敛", exception, 1, true);
    }
}
