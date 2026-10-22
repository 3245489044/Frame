using System;

namespace Share.Signal;

/// <summary>
/// 信号幂等键（SCF SC-I3）：<c>(Scope, Target, Kind)</c>。
/// <para><c>Target</c> 必须来自 DTC 归一后的 <c>TargetRef.Value</c>（禁实例键/幽灵键直接入键）。</para>
/// </summary>
public readonly record struct SignalKey(string Scope, string Target, string Kind)
{
    /// <summary>稳定文本形态（持久化/日志；分隔符不参与业务键）。</summary>
    public string Value => $"{Scope}\u001f{Target}\u001f{Kind}";

    /// <summary>是否完整（三段均非空）。</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Scope)
        && !string.IsNullOrWhiteSpace(Target)
        && !string.IsNullOrWhiteSpace(Kind);

    /// <summary>从三段构造（空白段原样保留，由 <see cref="IsComplete"/> 判定）。</summary>
    public static SignalKey Of(string? scope, string? target, string? kind)
        => new(scope ?? string.Empty, target ?? string.Empty, kind ?? string.Empty);

    public override string ToString() => Value;
}
