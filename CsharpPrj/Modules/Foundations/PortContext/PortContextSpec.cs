using System;
using System.Collections.Generic;
using System.Linq;
using ContractFramework.Codes;

namespace Share.PortContexts;

/// <summary>上下文规格（声明面派生）：容器层/成员层/边类型/上限/窗口预算/字段族/空策略/消费面。</summary>
/// <param name="ContainerLevel">容器层 Id（如 history）。</param>
/// <param name="MemberLevel">成员层 Id（如 task）。</param>
/// <param name="EdgeType">成员归属边类型（如 history_owns_task）。</param>
/// <param name="MaxMembers">成员数上限（≤0 ⇒ 有界缺省 32）。</param>
/// <param name="WindowTake">每成员窗口条数（≤0 ⇒ 有界缺省 6）。</param>
/// <param name="FieldClasses">字段族（code/semantic/window；空 ⇒ 全族）。</param>
/// <param name="EmptyPolicy">空策略（visible / not-applicable）。</param>
/// <param name="Consumers">消费面 Id（ui.board / prompt.runtime / diag.badge）。</param>
public sealed record PortContextSpec(
    string ContainerLevel,
    string MemberLevel,
    string EdgeType,
    int MaxMembers,
    int WindowTake,
    IReadOnlyList<string> FieldClasses,
    string EmptyPolicy,
    IReadOnlyList<string> Consumers)
{
    /// <summary>有界缺省成员上限。</summary>
    public const int DefaultMaxMembers = 32;

    /// <summary>有界缺省窗口条数。</summary>
    public const int DefaultWindowTake = 6;

    /// <summary>有效成员上限（≤0 ⇒ 缺省；禁无界）。</summary>
    public int EffectiveMaxMembers => MaxMembers > 0 ? MaxMembers : DefaultMaxMembers;

    /// <summary>有效窗口预算（≤0 ⇒ 缺省；禁无界）。</summary>
    public int EffectiveWindowTake => WindowTake > 0 ? WindowTake : DefaultWindowTake;

    /// <summary>未声明规格（调用方须显式失败或显式降级，禁伪造领域值）。</summary>
    public static PortContextSpec Unspecified { get; } = new(
        string.Empty, string.Empty, string.Empty,
        DefaultMaxMembers, DefaultWindowTake,
        Array.Empty<string>(), PortContextEmptyPolicy.Visible, Array.Empty<string>());
}

/// <summary>字段族受控词（单源；词表真源 = Config/nodes/transfer.ushar）。</summary>
public static class PortContextFieldClasses
{
    /// <summary>代码层固定状态。</summary>
    public const string Code = "code";

    /// <summary>语义层派生摘要。</summary>
    public const string Semantic = "semantic";

    /// <summary>窗口层（成员会话窗口片段）。</summary>
    public const string Window = "window";

    /// <summary>全部字段族。</summary>
    public static readonly IReadOnlyList<string> All = new[] { Code, Semantic, Window };
}

/// <summary>
/// 变化频率受控词（按频次升序 = 排序/分段依据：静态 → 慢变 → 快变 → 追加流）。
/// <para>兼容保留 <see cref="Stable"/>/<see cref="Volatile"/> 两档旧值（既有声明面与消费方）；新写入优先用四档。</para>
/// </summary>
public static class PortContextVolatility
{
    /// <summary>跨会话不变（身份/结构）：恒置最前，可进缓存前缀。</summary>
    public const string Static = "static";

    /// <summary>跨帧逐字稳定（兼容旧档；等价于静态/慢变之间的保守档）。</summary>
    public const string Stable = "stable";

    /// <summary>轮次边界变化（stage/round/actor）：中段。</summary>
    public const string Slow = "slow";

    /// <summary>逐拍变化（elapsed/status/语义摘要）：后段，尾部。</summary>
    public const string Fast = "fast";

    /// <summary>逐帧变化（兼容旧档）。</summary>
    public const string Volatile = "volatile";

    /// <summary>追加流（成员会话窗口/消息）：恒置最末，禁入前缀。</summary>
    public const string Stream = "stream";
}

/// <summary>
/// 变化频率排序单点（单一实现）：按频次升序 + 键序稳定排序。
/// <para>消费方禁各自 <c>OrderBy</c>：装配器、出口投影、UI 摘要、提示词分段统一调用本类。</para>
/// </summary>
public static class PortContextOrdering
{
    /// <summary>变化频率 → 排序秩（小者前置；未知档按 <see cref="PortContextVolatility.Fast"/> 处理，不抛）。</summary>
    /// <param name="volatility">变化频率档。</param>
    public static int Rank(string? volatility) => volatility switch
    {
        PortContextVolatility.Static => 0,
        PortContextVolatility.Stable => 1,
        PortContextVolatility.Slow => 2,
        PortContextVolatility.Fast => 3,
        PortContextVolatility.Volatile => 4,
        PortContextVolatility.Stream => 5,
        _ => 3,
    };

    /// <summary>按变化频率排序（秩升序 → 键序 → 原序稳定）。</summary>
    /// <param name="fields">待排序字段（返回新数组，不改入参）。</param>
    public static IReadOnlyList<PortContextField> Order(IReadOnlyList<PortContextField> fields)
    {
        if (fields is null || fields.Count <= 1) return fields ?? Array.Empty<PortContextField>();
        return fields
            .OrderBy(static f => Rank(f.Volatility))
            .ThenBy(static f => f.Key, StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>空策略受控词。</summary>
public static class PortContextEmptyPolicy
{
    /// <summary>空必可达（显式占位 + 原因码）。</summary>
    public const string Visible = "visible";

    /// <summary>该层/该声明不适用（⊥，不渲染）。</summary>
    public const string NotApplicable = "not-applicable";
}

/// <summary>空因码（受控词单源；禁在出口侧手写文案充当原因）。</summary>
public static class PortContextEmptyCodes
{
    /// <summary>成员枚举为空（边不存在/未挂载）。</summary>
    public const string NoOwnedMember = "no-owned-member";

    /// <summary>成员枚举端口未装配。</summary>
    public const string EnumeratorNotAttached = "enumerator-not-attached";

    /// <summary>成员枚举端口已装配但取数失败（异常已收敛为受控失败，非静默空）。</summary>
    public const string EnumeratorFailed = "enumerator-failed";

    /// <summary>运行态供数未装配。</summary>
    public const string StateProviderNotAttached = "state-provider-not-attached";

    /// <summary>运行态供数已装配但取数失败（异常已收敛为受控失败，非静默空）。</summary>
    public const string StateProviderFailed = "state-provider-failed";

    /// <summary>有成员但窗口为空。</summary>
    public const string WindowEmpty = "window-empty";

    /// <summary>成员窗口供数端口未装配（区别于「已装配但窗口内容为空」）。</summary>
    public const string WindowProviderNotAttached = "window-provider-not-attached";

    /// <summary>成员窗口供数端口已装配但取数失败（异常已收敛为受控失败，非静默空）。</summary>
    public const string WindowFailed = "window-failed";

    /// <summary>仅合成壳（无真实行）。</summary>
    public const string SyntheticOnly = "synthetic-only";

    /// <summary>容器键/成员键轴不匹配。</summary>
    public const string KeyAxisMismatch = "key-axis-mismatch";

    /// <summary>窗口/成员被预算截断（Present 但带截断标记）。</summary>
    public const string MemberWindowTruncated = "member-window-truncated";

    /// <summary>分页游标非法（解不出「成员键|偏移」或越界）。</summary>
    public const string CursorInvalid = "cursor-invalid";

    /// <summary>更早一侧仍有数据，但下一页游标会越过离尾偏移上界（截断显式可见，不再静默压 false）。</summary>
    public const string CursorBoundReached = "cursor-bound-reached";

    /// <summary>受控码集（门禁/诊断遍历单源；单一实现 = <see cref="CodeSet"/>）。</summary>
    public static readonly CodeSet Codes = new(
        "port-context-empty",
        NoOwnedMember, EnumeratorNotAttached, EnumeratorFailed,
        StateProviderNotAttached, StateProviderFailed,
        WindowEmpty, WindowProviderNotAttached, WindowFailed,
        SyntheticOnly, KeyAxisMismatch, MemberWindowTruncated,
        CursorInvalid, CursorBoundReached);
}
