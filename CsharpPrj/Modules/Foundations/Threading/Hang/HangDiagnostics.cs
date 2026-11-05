namespace Modules.Threading;

/// <summary>卡死取证结论。</summary>
public enum HangVerdict
{
    /// <summary>确证卡死（转预算终止 / TO，交由引擎裁决）。</summary>
    ConfirmedHang,

    /// <summary>处于合法长等待相位（LLM / 暂停门），不判卡死。</summary>
    LegalWait,

    /// <summary>健康 / 未越阈值。</summary>
    Healthy
}

/// <summary>
/// 证据探针注入契约（零业务依赖）：具体资源侧（DB busy / Outbox 在途 / UI 忙）由消费方实现，
/// 叶子模块仅读取其结果快照，不反向引用任何业务类型（对齐“通用层零依赖、源侧只留适配/门面”）。
/// </summary>
public interface IHangEvidenceSource
{
    /// <summary>探针名（如 "db" / "outbox" / "ui"）。</summary>
    string Name { get; }

    /// <summary>抓取当前状态快照（可序列化值，取证瞬间调用）。</summary>
    object? Capture();
}

/// <summary>一次卡死取证的结构化证据包（可序列化，供写诊断事件 / ur_events 只增归档）。</summary>
public sealed record HangEvidence(
    DateTimeOffset CapturedAt,            // 取证时刻
    long Tick,                            // 心跳单调毫秒
    string? RunKey,                       // 承载上下文的 run/taskKey（由消费方填写）
    string? Step,                         // 承载上下文的 step（由消费方填写）
    HangProbe? Probe,                     // 卡死瞬间的位置近似（谁卡、等谁、多久）
    long StallMilliseconds,               // 停更时长
    IReadOnlyList<string> RecentLogs,     // 最近 N 条日志
    IReadOnlyList<KeyValuePair<string, object?>> Context, // 各资源侧探针快照
    HangVerdict Verdict);                 // 结论

/// <summary>
/// 卡死证据装配器（单一出口）：汇聚取证槽快照 + 最近日志环 + 注入的资源探针，产出 <see cref="HangEvidence"/>。
/// 写诊断/终止的动作不在此发生——仅装配，动作交由既有引擎裁决（对齐“写入口单一、不双写确定性核心”）。
/// </summary>
public sealed class HangEvidenceAssembler
{
    private readonly RingBuffer<string>? _logs;
    private readonly IReadOnlyList<IHangEvidenceSource>? _sources;

    /// <param name="logs">最近日志环（可空）。</param>
    /// <param name="sources">资源侧探针（可空）。</param>
    public HangEvidenceAssembler(RingBuffer<string>? logs = null, IReadOnlyList<IHangEvidenceSource>? sources = null)
    {
        _logs = logs;
        _sources = sources;
    }

    /// <summary>装配一份证据包。</summary>
    public HangEvidence Assemble(
        HangProbe? probe,
        long stallMilliseconds,
        HangVerdict verdict,
        string? runKey = null,
        string? step = null,
        DateTimeOffset? capturedAt = null)
    {
        var context = new List<KeyValuePair<string, object?>>();
        if (_sources is not null)
        {
            foreach (var source in _sources)
            {
                try { context.Add(new KeyValuePair<string, object?>(source.Name, source.Capture())); }
                catch (Exception ex) { context.Add(new KeyValuePair<string, object?>(source.Name, $"<capture-failed:{ex}>")); }
            }
        }

        return new HangEvidence(
            capturedAt ?? DateTimeOffset.UtcNow,
            probe?.Tick ?? 0,
            runKey,
            step,
            probe,
            stallMilliseconds,
            _logs?.Snapshot() ?? Array.Empty<string>(),
            context,
            verdict);
    }
}
