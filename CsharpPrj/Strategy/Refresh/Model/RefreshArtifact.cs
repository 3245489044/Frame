using System;

namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 刷新产物（L0 契约，零领域）：一次渲染的完整反馈。
    /// <para><see cref="Fingerprint"/> 必填且须跨进程稳定；空串按 <see cref="RefreshOutcome.Failed"/> 处理。</para>
    /// <para><see cref="ProgressToken"/> 是「有进展」的结构事实（域侧单调值：轮次 / 序号 / 行版本等）。</para>
    /// <para><see cref="SuggestedDelay"/> 只是建议，最终节拍由框架调度器裁决。</para>
    /// </summary>
    public sealed record RefreshArtifact(
        object? Payload,
        RefreshOutcome Outcome,
        string Fingerprint,
        string? ProgressToken = null,
        TimeSpan? SuggestedDelay = null)
    {
        /// <summary>内容变化产物（唯一会进入交付的类型）。</summary>
        public static RefreshArtifact Changed(object payload, string fingerprint, string? progressToken = null)
            => new(payload, RefreshOutcome.Changed, fingerprint, progressToken);

        /// <summary>内容未变（合法 no-op，不触写落地）。</summary>
        public static RefreshArtifact Unchanged(string fingerprint, string? progressToken = null)
            => new(null, RefreshOutcome.Unchanged, fingerprint, progressToken);

        /// <summary>条件不满足 / 版本过期（不交付）。</summary>
        public static RefreshArtifact Skipped(string fingerprint)
            => new(null, RefreshOutcome.Skipped, fingerprint);

        /// <summary>渲染失败（不交付）。</summary>
        public static RefreshArtifact Failed(string fingerprint)
            => new(null, RefreshOutcome.Failed, fingerprint);

        /// <summary>是否进入交付路径。</summary>
        public bool IsDeliverable => Outcome == RefreshOutcome.Changed && Payload is not null;
    }
}
