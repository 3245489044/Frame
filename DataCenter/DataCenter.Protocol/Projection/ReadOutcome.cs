#nullable enable

namespace DataCenter.Protocol.Projection;

/// <summary>读结果种类（五态：禁 <c>null</c> 同时承载「未装配 / 空 / 失败」）。</summary>
public enum ReadOutcomeKind
{
    /// <summary>有值（<see cref="ReadOutcome{T}.Value"/> 非 default）。</summary>
    Ok,

    /// <summary>源已装配且读取成功，但结果为空集（合法空态，非失败）。</summary>
    Empty,

    /// <summary>源未装配（可见诊断；调用方可退化为空态渲染）。</summary>
    NotAttached,

    /// <summary>读前/读后版本不一致：本拍值不写缓存，值可见但不保证一致（禁混合快照）。</summary>
    Stale,

    /// <summary>声明/投影违规（未知模型、类型不匹配、投影抛错）：不放行。</summary>
    Violation,
}

/// <summary>
/// 读结果（值 + 种类 + 诊断）。<see cref="Value"/> 仅在 <see cref="ReadOutcomeKind.Ok"/> 与
/// <see cref="ReadOutcomeKind.Stale"/> 下有意义；其余态为该类型默认值。
/// </summary>
/// <param name="Kind">结果种类。</param>
/// <param name="Value">投影结果（无值态 = default）。</param>
/// <param name="Diagnostic">诊断（无 = null；禁空串占位）。</param>
public readonly record struct ReadOutcome<T>(ReadOutcomeKind Kind, T Value, string? Diagnostic)
{
    /// <summary>有值。</summary>
    public static ReadOutcome<T> Ok(T value) => new(ReadOutcomeKind.Ok, value, null);

    /// <summary>合法空态。</summary>
    public static ReadOutcome<T> Empty() => new(ReadOutcomeKind.Empty, default!, null);

    /// <summary>源未装配（必须带诊断）。</summary>
    public static ReadOutcome<T> NotAttached(string diagnostic) => new(ReadOutcomeKind.NotAttached, default!, diagnostic);

    /// <summary>版本抖动（值可见，但不写缓存、不作一致快照）。</summary>
    public static ReadOutcome<T> Stale(T value, string diagnostic) => new(ReadOutcomeKind.Stale, value, diagnostic);

    /// <summary>违规（不放行）。</summary>
    public static ReadOutcome<T> Violation(string diagnostic) => new(ReadOutcomeKind.Violation, default!, diagnostic);

    /// <summary>是否可消费（Ok / Stale 均有值）。</summary>
    public bool HasValue => Kind is ReadOutcomeKind.Ok or ReadOutcomeKind.Stale;
}
