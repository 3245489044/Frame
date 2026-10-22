using System;

namespace Share.Signal;

/// <summary>
/// 信号全局唯一身份（SCF：一个信号一个身份；从产生到终态全程可关联、可重放）。
/// <para>空值 = 未生成（调用方须在 Emit 段补齐；禁以 target 猜身份）。</para>
/// </summary>
public readonly record struct SignalId(string Value)
{
    /// <summary>是否已赋值。</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    /// <summary>生成新身份（ULID 语义用时间有序 GUID 近似；不引入新依赖）。</summary>
    public static SignalId New() => new($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}-{Guid.NewGuid():N}");

    /// <summary>文本化（日志/编码）。</summary>
    public override string ToString() => Value ?? string.Empty;
}
